using System.Text;

namespace Yy.Native;

internal enum NativeRegister
{
    Rax,
    Rcx,
    Rdx,
    R8,
    R9
}

internal enum FixupTargetKind
{
    CodeLabel,
    DataLabel,
    Import
}

internal sealed record NativeFixup(
    int Offset,
    FixupTargetKind Kind,
    string Target,
    bool Relative);

internal sealed record NativeDataFixup(
    int Offset,
    FixupTargetKind Kind,
    string Target);

internal sealed class NativeCodeBuffer
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<NativeFixup> _fixups = [];

    public IReadOnlyList<byte> Bytes => _bytes;
    public IReadOnlyList<NativeFixup> Fixups => _fixups;

    public void MarkLabel(string name) => _labels[name] = _bytes.Count;

    public int ResolveLabel(string name) =>
        _labels.TryGetValue(name, out var offset)
            ? offset
            : throw new InvalidOperationException($"未定义的原生代码标签：{name}");

    public void Align(int alignment)
    {
        while (_bytes.Count % alignment != 0)
        {
            _bytes.Add(0x90);
        }
    }

    public void PushRbp()
    {
        Emit(0x55);
        Emit(0x48, 0x89, 0xE5);
    }

    public void BeginFrame(int size)
    {
        if (size % 16 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "栈帧必须是 16 字节对齐。");
        }

        PushRbp();
        Emit(0x48, 0x81, 0xEC);
        EmitInt32(size);
    }

    public void EndFrame()
    {
        Emit(0xC9, 0xC3);
    }

    public void Ret() => Emit(0xC3);

    public void PushRax() => Emit(0x50);

    public void Pop(NativeRegister register)
    {
        switch (register)
        {
            case NativeRegister.Rax:
                Emit(0x58);
                break;
            case NativeRegister.Rcx:
                Emit(0x59);
                break;
            case NativeRegister.Rdx:
                Emit(0x5A);
                break;
            case NativeRegister.R8:
                Emit(0x41, 0x58);
                break;
            case NativeRegister.R9:
                Emit(0x41, 0x59);
                break;
        }
    }

    public void MovImmediate(NativeRegister register, long value)
    {
        Emit(register switch
        {
            NativeRegister.Rax => (byte)0x48,
            NativeRegister.Rcx => 0x48,
            NativeRegister.Rdx => 0x48,
            NativeRegister.R8 => 0x49,
            NativeRegister.R9 => 0x49,
            _ => 0x48
        });
        Emit(register switch
        {
            NativeRegister.Rax => (byte)0xB8,
            NativeRegister.Rcx => 0xB9,
            NativeRegister.Rdx => 0xBA,
            NativeRegister.R8 => 0xB8,
            NativeRegister.R9 => 0xB9,
            _ => 0xB8
        });
        EmitInt64(value);
    }

    public void LoadRaxFromRbp(int displacement)
    {
        Emit(0x48, 0x8B, 0x85);
        EmitInt32(displacement);
    }

    public void LoadRcxFromRbp(int displacement)
    {
        Emit(0x48, 0x8B, 0x8D);
        EmitInt32(displacement);
    }

    public void LoadRdxFromRbp(int displacement)
    {
        Emit(0x48, 0x8B, 0x95);
        EmitInt32(displacement);
    }

    public void LoadR8FromRbp(int displacement)
    {
        Emit(0x4C, 0x8B, 0x85);
        EmitInt32(displacement);
    }

    public void LeaR9Rbp(int displacement)
    {
        Emit(0x4C, 0x8D, 0x8D);
        EmitInt32(displacement);
    }

    public void LeaRcxRbp(int displacement)
    {
        Emit(0x48, 0x8D, 0x8D);
        EmitInt32(displacement);
    }

    public void StoreRaxToRbp(int displacement)
    {
        Emit(0x48, 0x89, 0x85);
        EmitInt32(displacement);
    }

    public void StoreRcxToRbp(int displacement)
    {
        Emit(0x48, 0x89, 0x8D);
        EmitInt32(displacement);
    }

    public void StoreRdxToRbp(int displacement)
    {
        Emit(0x48, 0x89, 0x95);
        EmitInt32(displacement);
    }

    public void StoreR8ToRbp(int displacement)
    {
        Emit(0x4C, 0x89, 0x85);
        EmitInt32(displacement);
    }

    public void StoreR9ToRbp(int displacement)
    {
        Emit(0x4C, 0x89, 0x8D);
        EmitInt32(displacement);
    }

    public void LoadR9FromRbp(int displacement)
    {
        Emit(0x4C, 0x8B, 0x8D);
        EmitInt32(displacement);
    }

    public void StoreRegisterToRbp(NativeRegister register, int displacement)
    {
        switch (register)
        {
            case NativeRegister.Rax:
                StoreRaxToRbp(displacement);
                break;
            case NativeRegister.Rcx:
                StoreRcxToRbp(displacement);
                break;
            case NativeRegister.Rdx:
                StoreRdxToRbp(displacement);
                break;
            case NativeRegister.R8:
                StoreR8ToRbp(displacement);
                break;
            case NativeRegister.R9:
                StoreR9ToRbp(displacement);
                break;
        }
    }

    public void LoadRaxFromRcx() => Emit(0x48, 0x8B, 0x01);
    public void LoadRdxFromRcx() => Emit(0x48, 0x8B, 0x11);
    public void MovRcxRax() => Emit(0x48, 0x89, 0xC1);
    public void MovRdxRax() => Emit(0x48, 0x89, 0xC2);
    public void MovR8Rax() => Emit(0x49, 0x89, 0xC0);
    public void AddRcxByte(byte value) => Emit(0x48, 0x83, 0xC1, value);
    public void AddRaxRcx() => Emit(0x48, 0x01, 0xC8);
    public void SubRaxRcx() => Emit(0x48, 0x29, 0xC8);
    public void ImulRaxRcx() => Emit(0x48, 0x0F, 0xAF, 0xC1);
    public void Cqo() => Emit(0x48, 0x99);
    public void IdivRcx() => Emit(0x48, 0xF7, 0xF9);
    public void DivRcx() => Emit(0x48, 0xF7, 0xF1);
    public void NegRax() => Emit(0x48, 0xF7, 0xD8);
    public void NotRax() => Emit(0x48, 0xF7, 0xD0);
    public void TestRaxRax() => Emit(0x48, 0x85, 0xC0);
    public void TestRcxRcx() => Emit(0x48, 0x85, 0xC9);
    public void CmpRaxRcx() => Emit(0x48, 0x39, 0xC8);
    public void CmpRcxRax() => Emit(0x48, 0x39, 0xC1);
    public void CmpRaxZero() => Emit(0x48, 0x83, 0xF8, 0x00);

    public void CmpByteRbp(int displacement, byte value)
    {
        Emit(0x80, 0xBD);
        EmitInt32(displacement);
        Emit(value);
    }

    public void StoreByteRbp(int displacement, byte value)
    {
        Emit(0xC6, 0x85);
        EmitInt32(displacement);
        Emit(value);
    }

    public void StoreByteR10(byte value)
    {
        Emit(0x41, 0xC6, 0x02, value);
    }

    public void StoreByteR10Rdx()
    {
        Emit(0x41, 0x88, 0x12);
    }

    public void MovR10RbpOffset(int displacement)
    {
        Emit(0x4C, 0x8D, 0x95);
        EmitInt32(displacement);
    }

    public void MovR11R10() => Emit(0x4D, 0x89, 0xD3);
    public void IncR10() => Emit(0x49, 0xFF, 0xC2);
    public void IncR11() => Emit(0x49, 0xFF, 0xC3);
    public void DecR10() => Emit(0x49, 0xFF, 0xCA);
    public void AddDl(byte value) => Emit(0x80, 0xC2, value);
    public void XorEdxEdx() => Emit(0x31, 0xD2);
    public void MovRcxR10() => Emit(0x4C, 0x89, 0xD1);
    public void MovRdxR11SubR10() => Emit(0x4C, 0x89, 0xDA, 0x4C, 0x29, 0xD2);
    public void MovR8R10() => Emit(0x4D, 0x89, 0xD0);
    public void IncRax() => Emit(0x48, 0xFF, 0xC0);
    public void AddRaxImmediate(byte value) => Emit(0x48, 0x83, 0xC0, value);
    public void StoreZeroRsp32() => Emit(0x48, 0xC7, 0x44, 0x24, 0x20, 0, 0, 0, 0);
    public void EmitRaw(params byte[] bytes) => Emit(bytes);
    public void EmitRawInt32(int value) => EmitInt32(value);

    public void SetConditional(string condition)
    {
        var opcode = condition switch
        {
            "==" => (byte)0x94,
            "!=" => (byte)0x95,
            "<" => (byte)0x9C,
            "<=" => (byte)0x9E,
            ">" => (byte)0x9F,
            ">=" => (byte)0x9D,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
        Emit(0x0F, opcode, 0xC0, 0x48, 0x0F, 0xB6, 0xC0);
    }

    public void LeaRaxRip(string target) => LeaRip(0x05, target);
    public void LeaRcxRip(string target) => LeaRip(0x0D, target);
    public void LeaRdxRip(string target) => LeaRip(0x15, target);
    public void LeaR8Rip(string target) => LeaRip(0x05, target, rex: 0x4C);
    public void LeaR9Rip(string target) => LeaRip(0x0D, target, rex: 0x4C);

    private void LeaRip(byte modRm, string target, byte rex = 0x48)
    {
        Emit(rex, 0x8D, modRm);
        AddFixup(target, relative: true);
    }

    public void LoadRaxRip(string target)
    {
        Emit(0x48, 0x8B, 0x05);
        AddFixup(target, relative: true);
    }

    public void LoadRcxRip(string target)
    {
        Emit(0x48, 0x8B, 0x0D);
        AddFixup(target, relative: true);
    }

    public void StoreRaxRip(string target)
    {
        Emit(0x48, 0x89, 0x05);
        AddFixup(target, relative: true);
    }

    public void StoreRaxRsp(int displacement)
    {
        Emit(0x48, 0x89, 0x44, 0x24, (byte)displacement);
    }

    public void StoreRspImmediate(int displacement, int value)
    {
        Emit(0x48, 0xC7, 0x44, 0x24, (byte)displacement);
        EmitInt32(value);
    }

    public void MovEaxEdx() => Emit(0x89, 0xD0);
    public void MovEaxR8D() => Emit(0x44, 0x89, 0xC0);

    public void AndEax(int value)
    {
        Emit(0x25);
        EmitInt32(value);
    }

    public void CmpEax(int value)
    {
        Emit(0x3D);
        EmitInt32(value);
    }

    public void CallCode(string target)
    {
        Emit(0xE8);
        AddFixup(target, relative: true);
    }

    public void CallImport(string target)
    {
        Emit(0xFF, 0x15);
        AddFixup(target, relative: true);
    }

    public void Jmp(string label)
    {
        Emit(0xE9);
        AddFixup(label, relative: true);
    }

    public void JumpIfZero(string label) => ConditionalJump(0x84, label);
    public void JumpIfNotZero(string label) => ConditionalJump(0x85, label);
    public void JumpIfSigned(string label) => ConditionalJump(0x88, label);
    public void JumpIfNotSigned(string label) => ConditionalJump(0x89, label);
    public void JumpIfGreater(string label) => ConditionalJump(0x8F, label);
    public void JumpIfGreaterOrEqual(string label) => ConditionalJump(0x8D, label);
    public void JumpIfLessOrEqual(string label) => ConditionalJump(0x8E, label);

    private void ConditionalJump(byte opcode, string label)
    {
        Emit(0x0F, opcode);
        AddFixup(label, relative: true);
    }

    private void AddFixup(string target, bool relative)
    {
        var kind = target.StartsWith("import:", StringComparison.Ordinal)
            ? FixupTargetKind.Import
            : target.StartsWith("data:", StringComparison.Ordinal)
                ? FixupTargetKind.DataLabel
                : FixupTargetKind.CodeLabel;
        var normalized = kind switch
        {
            FixupTargetKind.Import => target["import:".Length..],
            FixupTargetKind.DataLabel => target["data:".Length..],
            _ => target
        };
        _fixups.Add(new NativeFixup(_bytes.Count, kind, normalized, relative));
        EmitInt32(0);
    }

    public void Patch(int offset, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        for (var index = 0; index < 4; index++)
        {
            _bytes[offset + index] = bytes[index];
        }
    }

    private void Emit(params byte[] bytes) => _bytes.AddRange(bytes);

    private void EmitInt32(int value) => _bytes.AddRange(BitConverter.GetBytes(value));

    private void EmitInt64(long value) => _bytes.AddRange(BitConverter.GetBytes(value));
}

internal sealed class NativeDataBuffer
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<NativeDataFixup> _fixups = [];

    public IReadOnlyList<byte> Bytes => _bytes;
    public IReadOnlyList<NativeDataFixup> Fixups => _fixups;

    public void Align(int alignment)
    {
        while (_bytes.Count % alignment != 0)
        {
            _bytes.Add(0);
        }
    }

    public void MarkLabel(string name) => _labels[name] = _bytes.Count;

    public int ResolveLabel(string name) =>
        _labels.TryGetValue(name, out var offset)
            ? offset
            : throw new InvalidOperationException($"未定义的数据标签：{name}");

    public string AddUtf8String(string value)
    {
        var name = "string_" + _labels.Count;
        Align(8);
        MarkLabel(name);
        var bytes = Encoding.UTF8.GetBytes(value);
        _bytes.AddRange(BitConverter.GetBytes((long)bytes.Length));
        _bytes.AddRange(bytes);
        return name;
    }

    public string AddUtf16String(string value)
    {
        var name = "utf16_" + _labels.Count;
        Align(2);
        MarkLabel(name);
        _bytes.AddRange(Encoding.Unicode.GetBytes(value));
        _bytes.Add(0);
        _bytes.Add(0);
        return name;
    }

    public void AddUInt32(uint value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void AddUInt64(ulong value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void AddZeroes(int count) => _bytes.AddRange(new byte[count]);

    public void AddFixup(FixupTargetKind kind, string target)
    {
        _fixups.Add(new NativeDataFixup(_bytes.Count, kind, target));
        AddZeroes(8);
    }

    public void AddBytes(string label, params byte[] bytes)
    {
        Align(8);
        MarkLabel(label);
        _bytes.AddRange(bytes);
    }
}
