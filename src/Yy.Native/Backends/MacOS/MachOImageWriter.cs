using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Yy.Native.MacOS;

/// <summary>
/// 直接写出 arm64 Mach-O。镜像通过 LC_DYLD_INFO_ONLY 绑定 libSystem 符号，
/// 并内嵌 ad-hoc CodeDirectory，因此不需要外部链接器或签名工具。
/// </summary>
internal static class MachOImageWriter
{
    private const uint PageSize = 0x4000;
    private const int HashPageShift = 14;
    private const int EntryOffset = 0x1000;
    private const ulong TextVm = 0x100000000;
    private const uint CpuTypeArm64 = 0x0100000C;
    private const uint MachMagic64 = 0xFEEDFACF;

    private static readonly byte[] RequirementBlob =
        [0xFA, 0xDE, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] EmbeddedSignatureBlob =
        [0xFA, 0xDE, 0x0B, 0x01, 0x00, 0x00, 0x00, 0x08];

    public static byte[] Build(
        Arm64CodeBuffer code,
        NativeDataBuffer data,
        IReadOnlyList<MacImport> imports,
        IReadOnlyList<string> libraries)
    {
        var symbols = imports
            .OrderBy(item => item.Library, StringComparer.Ordinal)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
        var codeBytes = code.Bytes.ToArray();
        var dataBytes = data.Bytes.ToArray();

        var gotBase = Align(dataBytes.Length, 8);
        var gotSize = symbols.Length * 8;
        if (gotSize > 0 && gotBase % PageSize + gotSize > PageSize)
        {
            gotBase = Align(gotBase, (int)PageSize);
        }

        var dataSize = Align((int)Math.Max(gotBase + gotSize, 8), (int)PageSize);
        var stubBase = Align(codeBytes.Length, 4);
        var stubSize = symbols.Length * 12;
        var textSize = Align(
            Math.Max(EntryOffset + stubBase + stubSize, 0x2000),
            (int)PageSize);
        var linkeditOffset = textSize + dataSize;
        var bind = BuildBindings(symbols, libraries, gotBase);
        var signatureOffset = Align(linkeditOffset + bind.Length, 16);
        var codeSlots = (signatureOffset + (int)PageSize - 1) / (int)PageSize;
        var signature = BuildSignature(signatureOffset, codeSlots);
        var linkeditFileSize = signatureOffset - linkeditOffset + signature.Length;
        var linkeditVmSize = Align(linkeditFileSize, (int)PageSize);
        var dataVm = TextVm + (ulong)textSize;
        var linkeditVm = dataVm + (ulong)dataSize;

        var image = new byte[signatureOffset + signature.Length];
        WriteHeader(image, textSize, dataSize, linkeditOffset, linkeditFileSize,
            linkeditVmSize, bind.Length, signatureOffset, signature.Length, dataVm,
            linkeditVm, libraries);
        codeBytes.CopyTo(image, EntryOffset);
        WriteStubs(image, symbols, stubBase, gotBase, dataVm);
        dataBytes.CopyTo(image, textSize);
        bind.CopyTo(image, linkeditOffset);
        signature.CopyTo(image, signatureOffset);
        PatchFixups(image, code, data, symbols, stubBase, textSize, dataVm);
        WriteHashes(image, signatureOffset, codeSlots);
        return image;
    }

    public static void MarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
    }

    private static void PatchFixups(
        byte[] image,
        Arm64CodeBuffer code,
        NativeDataBuffer data,
        IReadOnlyList<MacImport> symbols,
        int stubBase,
        int dataFileOffset,
        ulong dataVm)
    {
        foreach (var fixup in code.Fixups)
        {
            var instructionVma = TextVm + (ulong)EntryOffset + (ulong)fixup.Offset;
            switch (fixup.Kind)
            {
                case Arm64FixupKind.Branch26:
                case Arm64FixupKind.CondBranch19:
                case Arm64FixupKind.CompareBranch19:
                {
                    var targetVma = ResolveCodeTarget(
                        fixup.Target, code, symbols, stubBase);
                    var delta = (long)(targetVma - instructionVma);
                    PatchBranch(image, EntryOffset + fixup.Offset, fixup.Kind, delta);
                    break;
                }
                case Arm64FixupKind.AdrpPage:
                {
                    var targetVma = ResolveDataOrCodeTarget(fixup.Target, code, dataVm, data);
                    var pageDelta = (long)(targetVma >> 12) - (long)(instructionVma >> 12);
                    PatchAdrp(image, EntryOffset + fixup.Offset, pageDelta);
                    break;
                }
                case Arm64FixupKind.AddPageOffset:
                {
                    var targetVma = ResolveDataOrCodeTarget(fixup.Target, code, dataVm, data);
                    PatchAddImm(image, EntryOffset + fixup.Offset, (int)(targetVma & 0xFFF));
                    break;
                }
                default:
                    throw new InvalidOperationException($"未知的 arm64 重定位：{fixup.Kind}");
            }
        }
    }

    private static ulong ResolveCodeTarget(
        string target,
        Arm64CodeBuffer code,
        IReadOnlyList<MacImport> symbols,
        int stubBase)
    {
        if (target.StartsWith("import:", StringComparison.Ordinal))
        {
            var symbol = target["import:".Length..];
            var index = -1;
            for (var i = 0; i < symbols.Count; i++)
            {
                if (string.Equals(symbols[i].Symbol, symbol, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw new InvalidOperationException($"缺少 macOS 导入：{symbol}");
            }

            return TextVm + (ulong)EntryOffset + (ulong)(stubBase + index * 12);
        }

        return TextVm + (ulong)EntryOffset + (ulong)code.ResolveLabel(target);
    }

    private static ulong ResolveDataOrCodeTarget(
        string target,
        Arm64CodeBuffer code,
        ulong dataVm,
        NativeDataBuffer data)
    {
        if (target.StartsWith("code:", StringComparison.Ordinal))
        {
            return TextVm + (ulong)EntryOffset +
                (ulong)code.ResolveLabel(target["code:".Length..]);
        }

        return dataVm + (ulong)data.ResolveLabel(target);
    }

    private static void PatchBranch(
        byte[] image,
        int offset,
        Arm64FixupKind kind,
        long delta)
    {
        if (delta % 4 != 0)
        {
            throw new InvalidOperationException("arm64 分支未按 4 字节对齐。");
        }

        var displacement = delta / 4;
        var word = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, 4));
        word = kind switch
        {
            Arm64FixupKind.Branch26 =>
                (word & 0xFC000000u) | ((uint)displacement & 0x03FFFFFFu),
            _ =>
                (word & ~(0x7FFFFu << 5)) | (((uint)displacement & 0x7FFFFu) << 5)
        };
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, 4), word);
    }

    private static void PatchAdrp(byte[] image, int offset, long pageDelta)
    {
        if (pageDelta < -(1 << 20) || pageDelta >= (1 << 20))
        {
            throw new InvalidOperationException("arm64 ADRP 页偏移超出范围。");
        }

        var encoded = (uint)pageDelta & 0x1FFFFF;
        var immlo = encoded & 3;
        var immhi = (encoded >> 2) & 0x7FFFF;
        var word = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, 4));
        word &= ~((3u << 29) | (0x7FFFFu << 5));
        word |= (immlo << 29) | (immhi << 5);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, 4), word);
    }

    private static void PatchAddImm(byte[] image, int offset, int value)
    {
        var word = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, 4));
        word &= ~(0xFFFu << 10);
        word |= (uint)(value & 0xFFF) << 10;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset, 4), word);
    }

    private static void WriteStubs(
        byte[] image,
        IReadOnlyList<MacImport> symbols,
        int stubBase,
        int gotBase,
        ulong dataVm)
    {
        for (var index = 0; index < symbols.Count; index++)
        {
            var stubOffset = EntryOffset + stubBase + index * 12;
            var stubVma = TextVm + (ulong)stubOffset;
            var gotVma = dataVm + (ulong)(gotBase + index * 8);
            var pageDelta = (long)(gotVma >> 12) - (long)(stubVma >> 12);
            var encoded = (uint)pageDelta & 0x1FFFFF;
            var adrp = 0x90000010u | ((encoded & 3) << 29) | (((encoded >> 2) & 0x7FFFF) << 5);
            var ldrOffset = (int)(gotVma & 0xFFF);
            var ldr = 0xF9400210u | ((uint)(ldrOffset / 8) << 10);
            var br = 0xD61F0200u;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(stubOffset, 4), adrp);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(stubOffset + 4, 4), ldr);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(stubOffset + 8, 4), br);
        }
    }

    private static byte[] BuildBindings(
        IReadOnlyList<MacImport> symbols,
        IReadOnlyList<string> libraries,
        int gotBase)
    {
        var bytes = new List<byte>();
        for (var index = 0; index < symbols.Count; index++)
        {
            var ordinal = -1;
            for (var libraryIndex = 0; libraryIndex < libraries.Count; libraryIndex++)
            {
                if (string.Equals(
                        libraries[libraryIndex],
                        symbols[index].Library,
                        StringComparison.Ordinal))
                {
                    ordinal = libraryIndex + 1;
                    break;
                }
            }

            if (ordinal < 1)
            {
                throw new InvalidOperationException(
                    $"导入 {symbols[index].Symbol} 引用了未加载的库 {symbols[index].Library}。");
            }

            bytes.Add((byte)(0x10 | ordinal));
            bytes.Add(0x40); // BIND_OPCODE_SET_SYMBOL_TRAILING_FLAGS_IMM
            bytes.AddRange(Encoding.ASCII.GetBytes(symbols[index].Symbol));
            bytes.Add(0);
            bytes.Add(0x51); // BIND_OPCODE_SET_TYPE_IMM(pointer)
            bytes.Add(0x72); // BIND_OPCODE_SET_SEGMENT_AND_OFFSET_ULEB(__DATA)
            bytes.AddRange(EncodeUleb(gotBase + index * 8));
            bytes.Add(0x90); // BIND_OPCODE_DO_BIND
        }

        bytes.Add(0x00); // BIND_OPCODE_DONE
        return bytes.ToArray();
    }

    private static byte[] EncodeUleb(int value)
    {
        var bytes = new List<byte>();
        var remaining = value;
        do
        {
            var chunk = (byte)(remaining & 0x7F);
            remaining >>= 7;
            bytes.Add(remaining != 0 ? (byte)(chunk | 0x80) : chunk);
        }
        while (remaining != 0);
        return bytes.ToArray();
    }

    private static byte[] BuildSignature(int codeLimit, int codeSlots)
    {
        const int headerSize = 0x30;
        var identifier = Encoding.ASCII.GetBytes("yy\0");
        var hashOffset = headerSize + identifier.Length + 64;
        var cdLength = hashOffset + codeSlots * 32;
        var requirementOffset = 12 + 3 * 8 + cdLength;
        var embeddedOffset = requirementOffset + RequirementBlob.Length;
        var total = embeddedOffset + EmbeddedSignatureBlob.Length;
        var blob = new byte[total];

        WriteUInt32BigEndian(blob, 0, 0xFADE0CC0);
        WriteUInt32BigEndian(blob, 4, (uint)total);
        WriteUInt32BigEndian(blob, 8, 3);
        WriteUInt32BigEndian(blob, 12, 0);
        WriteUInt32BigEndian(blob, 16, 36);
        WriteUInt32BigEndian(blob, 20, 2);
        WriteUInt32BigEndian(blob, 24, (uint)requirementOffset);
        WriteUInt32BigEndian(blob, 28, 0x10000);
        WriteUInt32BigEndian(blob, 32, (uint)embeddedOffset);

        var cd = 36;
        WriteUInt32BigEndian(blob, cd, 0xFADE0C02);
        WriteUInt32BigEndian(blob, cd + 4, (uint)cdLength);
        WriteUInt32BigEndian(blob, cd + 8, 0x20100);
        WriteUInt32BigEndian(blob, cd + 12, 0x2);
        WriteUInt32BigEndian(blob, cd + 16, (uint)hashOffset);
        WriteUInt32BigEndian(blob, cd + 20, headerSize);
        WriteUInt32BigEndian(blob, cd + 24, 2);
        WriteUInt32BigEndian(blob, cd + 28, (uint)codeSlots);
        WriteUInt32BigEndian(blob, cd + 32, (uint)codeLimit);
        blob[cd + 36] = 32;
        blob[cd + 37] = 2;
        blob[cd + 38] = 0;
        blob[cd + 39] = HashPageShift;

        identifier.CopyTo(blob, cd + headerSize);
        var special = cd + headerSize + identifier.Length;
        SHA256.HashData(RequirementBlob).CopyTo(blob, special);
        RequirementBlob.CopyTo(blob, requirementOffset);
        EmbeddedSignatureBlob.CopyTo(blob, embeddedOffset);
        return blob;
    }

    private static void WriteHashes(byte[] image, int codeLimit, int codeSlots)
    {
        var cd = 36;
        var hashOffset = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(codeLimit + cd + 16, 4));
        var hashStart = codeLimit + cd + (int)hashOffset;
        for (var index = 0; index < codeSlots; index++)
        {
            var offset = index * (int)PageSize;
            var length = Math.Min((int)PageSize, codeLimit - offset);
            SHA256.HashData(image.AsSpan(offset, length))
                .CopyTo(image, hashStart + index * 32);
        }
    }

    private static void WriteHeader(
        byte[] image,
        int textSize,
        int dataSize,
        int linkeditOffset,
        int linkeditFileSize,
        int linkeditVmSize,
        int bindSize,
        int signatureOffset,
        int signatureSize,
        ulong dataVm,
        ulong linkeditVm,
        IReadOnlyList<string> libraries)
    {
        WriteUInt32(image, 0, MachMagic64);
        WriteInt32(image, 4, (int)CpuTypeArm64);
        WriteInt32(image, 8, 0);
        WriteUInt32(image, 12, 2);
        WriteUInt32(image, 16, (uint)(8 + libraries.Count));
        WriteUInt32(image, 24, 0x1 | 0x4 | 0x200000);

        var cursor = 32;
        cursor = WriteSegment(image, cursor, "__PAGEZERO", 0, 0x100000000, 0, 0, 0);
        cursor = WriteSegment(image, cursor, "__TEXT", TextVm, (ulong)textSize, 0,
            (ulong)textSize, 5);
        cursor = WriteSegment(image, cursor, "__DATA", dataVm, (ulong)dataSize,
            (ulong)textSize, (ulong)dataSize, 3);
        cursor = WriteSegment(image, cursor, "__LINKEDIT", linkeditVm,
            (ulong)linkeditVmSize, (ulong)linkeditOffset, (ulong)linkeditFileSize, 1);

        var dylinker = Encoding.ASCII.GetBytes("/usr/lib/dyld\0");
        var dylinkerSize = Align(12 + dylinker.Length, 8);
        WriteUInt32(image, cursor, 0xE);
        WriteUInt32(image, cursor + 4, (uint)dylinkerSize);
        WriteUInt32(image, cursor + 8, 12);
        dylinker.CopyTo(image, cursor + 12);
        cursor += dylinkerSize;

        WriteUInt32(image, cursor, 0x80000028);
        WriteUInt32(image, cursor + 4, 24);
        WriteUInt64(image, cursor + 8, EntryOffset);
        WriteUInt64(image, cursor + 16, 0);
        cursor += 24;

        WriteUInt32(image, cursor, 0x80000022);
        WriteUInt32(image, cursor + 4, 48);
        WriteUInt32(image, cursor + 16, (uint)linkeditOffset);
        WriteUInt32(image, cursor + 20, (uint)bindSize);
        cursor += 48;

        foreach (var library in libraries)
        {
            var dylib = Encoding.ASCII.GetBytes(library + "\0");
            var dylibSize = Align(24 + dylib.Length, 8);
            WriteUInt32(image, cursor, 0xC);
            WriteUInt32(image, cursor + 4, (uint)dylibSize);
            WriteUInt32(image, cursor + 8, 24);
            dylib.CopyTo(image, cursor + 24);
            cursor += dylibSize;
        }

        WriteUInt32(image, cursor, 0x1D);
        WriteUInt32(image, cursor + 4, 16);
        WriteUInt32(image, cursor + 8, (uint)signatureOffset);
        WriteUInt32(image, cursor + 12, (uint)signatureSize);
        cursor += 16;

        WriteUInt32(image, 20, (uint)(cursor - 32));
    }

    private static int WriteSegment(
        byte[] image,
        int offset,
        string name,
        ulong vmAddress,
        ulong vmSize,
        ulong fileOffset,
        ulong fileSize,
        int protection)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(image, offset + 8);
        WriteUInt32(image, offset, 0x19);
        WriteUInt32(image, offset + 4, 72);
        WriteUInt64(image, offset + 24, vmAddress);
        WriteUInt64(image, offset + 32, vmSize);
        WriteUInt64(image, offset + 40, fileOffset);
        WriteUInt64(image, offset + 48, fileSize);
        WriteInt32(image, offset + 56, protection);
        WriteInt32(image, offset + 60, protection);
        WriteUInt32(image, offset + 64, 0);
        WriteUInt32(image, offset + 68, 0);
        return offset + 72;
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);

    private static void WriteUInt64(byte[] buffer, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), value);
}
