using System.Buffers.Binary;
using System.Text;

namespace Yy.Native;

internal static class NativePeWriter
{
    private const uint SectionAlignment = 0x1000;
    private const uint FileAlignment = 0x200;
    private const ulong ImageBase = 0x140000000;
    private const uint TextRva = 0x1000;
    private const uint TextRaw = 0x200;

    public static byte[] Build(
        NativeCodeBuffer code,
        NativeDataBuffer data,
        IReadOnlyDictionary<string, IReadOnlyList<string>> imports,
        bool guiSubsystem = false)
    {
        var codeBytes = code.Bytes.ToArray();
        var textVirtualSize = Align((uint)codeBytes.Length, SectionAlignment);
        var textRawSize = Align((uint)codeBytes.Length, FileAlignment);
        var rdataRva = TextRva + textVirtualSize;
        var rdataRaw = TextRaw + textRawSize;
        var rdataBytes = data.Bytes.ToArray();
        var rdataVirtualSize = Align((uint)rdataBytes.Length, SectionAlignment);
        var rdataRawSize = Align((uint)rdataBytes.Length, FileAlignment);
        var idataRva = rdataRva + rdataVirtualSize;
        var idataRaw = rdataRaw + rdataRawSize;
        var idata = BuildImports(idataRva, imports);

        ResolveFixups(code, codeBytes, data, imports, idata.Slots, rdataRva);
        ResolveDataFixups(data, rdataBytes, code, rdataRva);

        var idataRawSize = Align((uint)idata.Bytes.Length, FileAlignment);
        var sizeOfImage = Align(idataRva + (uint)idata.Bytes.Length, SectionAlignment);
        var sizeHeaders = 0x200u;
        var image = new byte[idataRaw + idataRawSize];

        WriteHeaders(
            image,
            (uint)codeBytes.Length,
            textRawSize,
            (uint)rdataBytes.Length,
            rdataRawSize,
            rdataRva,
            (uint)idata.Bytes.Length,
            idataRawSize,
            idataRva,
            idata.ImportDirectoryRva,
            idata.ImportDirectorySize,
            sizeOfImage,
            sizeHeaders,
            guiSubsystem);
        codeBytes.CopyTo(image, (int)TextRaw);
        rdataBytes.CopyTo(image, (int)rdataRaw);
        idata.Bytes.CopyTo(image, (int)idataRaw);
        return image;
    }

    private static void ResolveDataFixups(
        NativeDataBuffer data,
        byte[] rdataBytes,
        NativeCodeBuffer code,
        uint rdataRva)
    {
        foreach (var fixup in data.Fixups)
        {
            var target = fixup.Kind switch
            {
                FixupTargetKind.CodeLabel =>
                    ImageBase + TextRva + (uint)code.ResolveLabel(fixup.Target),
                FixupTargetKind.DataLabel =>
                    ImageBase + rdataRva + (uint)data.ResolveLabel(fixup.Target),
                _ => throw new InvalidOperationException(
                    $"不支持的数据重定位：{fixup.Kind}")
            };
            BinaryPrimitives.WriteUInt64LittleEndian(
                rdataBytes.AsSpan(fixup.Offset, 8),
                target);
        }
    }

    private static void ResolveFixups(
        NativeCodeBuffer code,
        byte[] codeBytes,
        NativeDataBuffer data,
        IReadOnlyDictionary<string, IReadOnlyList<string>> imports,
        IReadOnlyDictionary<string, uint> importSlots,
        uint rdataRva)
    {
        foreach (var fixup in code.Fixups)
        {
            var target = fixup.Kind switch
            {
                FixupTargetKind.CodeLabel => TextRva + (uint)code.ResolveLabel(fixup.Target),
                FixupTargetKind.DataLabel => rdataRva + (uint)data.ResolveLabel(fixup.Target),
                FixupTargetKind.Import => importSlots.TryGetValue(fixup.Target, out var slot)
                    ? slot
                    : throw new InvalidOperationException($"缺少原生导入：{fixup.Target}"),
                _ => throw new InvalidOperationException("未知原生重定位。")
            };
            var next = TextRva + (uint)(fixup.Offset + 4);
            var displacement = unchecked((int)(target - next));
            BinaryPrimitives.WriteInt32LittleEndian(codeBytes.AsSpan(fixup.Offset, 4), displacement);
        }

        if (imports.Count == 0)
        {
            throw new InvalidOperationException("原生程序至少需要一个 Windows 导入。");
        }
    }

    private static ImportLayout BuildImports(
        uint baseRva,
        IReadOnlyDictionary<string, IReadOnlyList<string>> imports)
    {
        var descriptorRva = baseRva;
        var libraries = imports.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var cursor = Align(baseRva + (uint)(libraries.Length + 1) * 20, 8);
        var slots = new Dictionary<string, uint>(StringComparer.Ordinal);
        var lookupTables = new uint[libraries.Length];
        var addressTables = new uint[libraries.Length];
        for (var libraryIndex = 0; libraryIndex < libraries.Length; libraryIndex++)
        {
            lookupTables[libraryIndex] = cursor;
            cursor += (uint)(libraries[libraryIndex].Value.Count + 1) * 8;
            addressTables[libraryIndex] = cursor;
            cursor += (uint)(libraries[libraryIndex].Value.Count + 1) * 8;
        }

        cursor = Align(cursor, 2);
        var dllNames = new byte[libraries.Length][];
        var dllNameRvas = new uint[libraries.Length];
        for (var libraryIndex = 0; libraryIndex < libraries.Length; libraryIndex++)
        {
            dllNameRvas[libraryIndex] = cursor;
            dllNames[libraryIndex] = Encoding.ASCII.GetBytes(libraries[libraryIndex].Key + "\0");
            cursor += (uint)dllNames[libraryIndex].Length;
        }

        var hintNames = new uint[libraries.Length][];
        for (var libraryIndex = 0; libraryIndex < libraries.Length; libraryIndex++)
        {
            hintNames[libraryIndex] = new uint[libraries[libraryIndex].Value.Count];
            for (var functionIndex = 0; functionIndex < libraries[libraryIndex].Value.Count; functionIndex++)
            {
                cursor = Align(cursor, 2);
                hintNames[libraryIndex][functionIndex] = cursor;
                var functionName = libraries[libraryIndex].Value[functionIndex];
                cursor += 2 + (uint)Encoding.ASCII.GetByteCount(functionName) + 1;
                slots[functionName] = addressTables[libraryIndex] + (uint)functionIndex * 8;
            }
        }

        var bytes = new byte[cursor - baseRva];
        for (var libraryIndex = 0; libraryIndex < libraries.Length; libraryIndex++)
        {
            var descriptor = (int)(libraryIndex * 20);
            WriteUInt32(bytes, descriptor, lookupTables[libraryIndex]);
            WriteUInt32(bytes, descriptor + 12, dllNameRvas[libraryIndex]);
            WriteUInt32(bytes, descriptor + 16, addressTables[libraryIndex]);
            for (var functionIndex = 0; functionIndex < libraries[libraryIndex].Value.Count; functionIndex++)
            {
                WriteUInt64(
                    bytes,
                    (int)(lookupTables[libraryIndex] - baseRva + (uint)functionIndex * 8),
                    hintNames[libraryIndex][functionIndex]);
                WriteUInt64(
                    bytes,
                    (int)(addressTables[libraryIndex] - baseRva + (uint)functionIndex * 8),
                    hintNames[libraryIndex][functionIndex]);
            }

            dllNames[libraryIndex].CopyTo(bytes, (int)(dllNameRvas[libraryIndex] - baseRva));
            for (var functionIndex = 0; functionIndex < libraries[libraryIndex].Value.Count; functionIndex++)
            {
                var offset = (int)(hintNames[libraryIndex][functionIndex] - baseRva);
                var name = Encoding.ASCII.GetBytes(libraries[libraryIndex].Value[functionIndex] + "\0");
                name.CopyTo(bytes, offset + 2);
            }
        }

        return new ImportLayout(
            bytes,
            descriptorRva,
            (uint)(libraries.Length + 1) * 20,
            slots);
    }

    private static void WriteHeaders(
        byte[] image,
        uint textVirtualSize,
        uint textRawSize,
        uint rdataVirtualSize,
        uint rdataRawSize,
        uint rdataRva,
        uint idataVirtualSize,
        uint idataRawSize,
        uint idataRva,
        uint importRva,
        uint importSize,
        uint sizeOfImage,
        uint sizeOfHeaders,
        bool guiSubsystem)
    {
        WriteUInt16(image, 0, 0x5A4D);
        WriteUInt32(image, 0x3C, 0x80);
        var dosMessage = Encoding.ASCII.GetBytes("This program cannot be run in DOS mode.\r\r\n$");
        dosMessage.CopyTo(image, 0x4E);

        var pe = 0x80;
        WriteUInt32(image, pe, 0x00004550);
        var coff = pe + 4;
        WriteUInt16(image, coff, 0x8664);
        WriteUInt16(image, coff + 2, 3);
        WriteUInt16(image, coff + 16, 0xF0);
        WriteUInt16(image, coff + 18, 0x0022);

        var optional = coff + 20;
        WriteUInt16(image, optional, 0x20B);
        image[optional + 2] = 1;
        WriteUInt32(image, optional + 4, textRawSize);
        WriteUInt32(image, optional + 8, rdataRawSize + idataRawSize);
        WriteUInt32(image, optional + 16, TextRva);
        WriteUInt32(image, optional + 20, TextRva);
        WriteUInt64(image, optional + 24, ImageBase);
        WriteUInt32(image, optional + 32, SectionAlignment);
        WriteUInt32(image, optional + 36, FileAlignment);
        WriteUInt16(image, optional + 40, 6);
        WriteUInt16(image, optional + 48, 6);
        WriteUInt32(image, optional + 56, sizeOfImage);
        WriteUInt32(image, optional + 60, sizeOfHeaders);
        WriteUInt16(image, optional + 68, guiSubsystem ? (ushort)2 : (ushort)3);
        WriteUInt16(image, optional + 70, 0x8100);
        WriteUInt64(image, optional + 72, 0x100000);
        WriteUInt64(image, optional + 80, 0x1000);
        WriteUInt64(image, optional + 88, 0x100000);
        WriteUInt64(image, optional + 96, 0x1000);
        WriteUInt32(image, optional + 108, 16);
        var importDirectory = optional + 112 + 8;
        WriteUInt32(image, importDirectory, importRva);
        WriteUInt32(image, importDirectory + 4, importSize);

        var sections = optional + 0xF0;
        WriteSection(
            image,
            sections,
            ".text",
            textVirtualSize,
            TextRva,
            textRawSize,
            TextRaw,
            0x60000020);
        WriteSection(
            image,
            sections + 40,
            ".rdata",
            rdataVirtualSize,
            rdataRva,
            rdataRawSize,
            TextRaw + textRawSize,
            0xC0000040);
        WriteSection(
            image,
            sections + 80,
            ".idata",
            idataVirtualSize,
            idataRva,
            idataRawSize,
            TextRaw + textRawSize + rdataRawSize,
            0xC0000040);
    }

    private static void WriteSection(
        byte[] image,
        int offset,
        string name,
        uint virtualSize,
        uint virtualAddress,
        uint rawSize,
        uint rawPointer,
        uint characteristics)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(image, offset);
        WriteUInt32(image, offset + 8, virtualSize);
        WriteUInt32(image, offset + 12, virtualAddress);
        WriteUInt32(image, offset + 16, rawSize);
        WriteUInt32(image, offset + 20, rawPointer);
        WriteUInt32(image, offset + 36, characteristics);
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

    private static void WriteUInt64(byte[] buffer, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);

    private static uint Align(uint value, uint alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private sealed record ImportLayout(
        byte[] Bytes,
        uint ImportDirectoryRva,
        uint ImportDirectorySize,
        IReadOnlyDictionary<string, uint> Slots);
}
