using System.Buffers.Binary;

namespace Yy.Native.MacOS;

internal enum Arm64Condition
{
    Eq,
    Ne,
    Hs,
    Lo,
    Mi,
    Pl,
    Vs,
    Vc,
    Hi,
    Ls,
    Ge,
    Lt,
    Gt,
    Le
}

internal enum Arm64FixupKind
{
    Branch26,
    CondBranch19,
    CompareBranch19,
    AdrpPage,
    AddPageOffset
}

internal sealed record Arm64Fixup(
    int Offset,
    Arm64FixupKind Kind,
    string Target);

/// <summary>
/// AArch64 指令缓冲。所有代码以 x0 作为表达式结果和返回值，
/// x0-x7 传递参数，x29/x30 保存调用帧。
/// </summary>
internal sealed class Arm64CodeBuffer
{
    public const int Sp = 31;
    public const int Xzr = 31;

    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<Arm64Fixup> _fixups = [];

    public IReadOnlyList<byte> Bytes => _bytes;
    public IReadOnlyList<Arm64Fixup> Fixups => _fixups;
    public int Position => _bytes.Count;

    public void MarkLabel(string name) => _labels[name] = _bytes.Count;

    public int ResolveLabel(string name) =>
        _labels.TryGetValue(name, out var offset)
            ? offset
            : throw new InvalidOperationException($"未定义的 ARM64 标签：{name}");

    public void Align(int alignment)
    {
        while (_bytes.Count % alignment != 0)
        {
            _bytes.Add(0x1F);
            _bytes.Add(0x20);
            _bytes.Add(0x03);
            _bytes.Add(0xD5);
        }
    }

    public void BeginFrame(int frameSize)
    {
        if (frameSize % 16 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSize), "栈帧必须是 16 字节对齐。");
        }

        EmitWord(0xA9BF7BFD); // stp x29, x30, [sp, #-16]!
        EmitWord(0x910003FD); // mov x29, sp
        SubSp(frameSize);
    }

    public void EndFrame()
    {
        EmitWord(0x910003BF); // mov sp, x29
        EmitWord(0xA8C17BFD); // ldp x29, x30, [sp], #16
        Ret();
    }

    public void Ret() => EmitWord(0xD65F03C0);

    public void Push(int register)
    {
        SubImm(Sp, Sp, 16);
        Str(register, Sp, 0);
    }

    public void Pop(int register)
    {
        Ldr(register, Sp, 0);
        AddImm(Sp, Sp, 16);
    }

    public void MovReg(int rd, int rm)
    {
        if (rd == rm)
        {
            return;
        }

        EmitWord(0xAA0003E0u | ((uint)rm << 16) | (uint)rd);
    }

    public void LoadImmediate(int rd, long value)
    {
        var bits = unchecked((ulong)value);
        EmitWord(0xD2800000u | ((uint)(bits & 0xFFFF) << 5) | (uint)rd);
        for (var shift = 16; shift < 64; shift += 16)
        {
            var chunk = (uint)((bits >> shift) & 0xFFFF);
            if (chunk != 0)
            {
                var hw = (uint)(shift / 16);
                EmitWord(0xF2800000u | (hw << 21) | (chunk << 5) | (uint)rd);
            }
        }
    }

    public void LoadAddress(int rd, string dataLabel)
    {
        EmitWord(0x90000000u | (uint)rd); // adrp
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, Arm64FixupKind.AdrpPage, dataLabel));
        AddImm(rd, rd, 0);
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, Arm64FixupKind.AddPageOffset, dataLabel));
    }

    public void LoadCodeAddress(int rd, string codeLabel)
    {
        EmitWord(0x90000000u | (uint)rd);
        _fixups.Add(new Arm64Fixup(
            _bytes.Count - 4,
            Arm64FixupKind.AdrpPage,
            "code:" + codeLabel));
        AddImm(rd, rd, 0);
        _fixups.Add(new Arm64Fixup(
            _bytes.Count - 4,
            Arm64FixupKind.AddPageOffset,
            "code:" + codeLabel));
    }

    public void AddImm(int rd, int rn, int value)
    {
        if (value is < 0 or > 0xFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "ADD 立即数必须在 0..4095。");
        }

        EmitWord(0x91000000u | ((uint)value << 10) | ((uint)rn << 5) | (uint)rd);
    }

    public void SubImm(int rd, int rn, int value)
    {
        if (value is < 0 or > 0xFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "SUB 立即数必须在 0..4095。");
        }

        EmitWord(0xD1000000u | ((uint)value << 10) | ((uint)rn << 5) | (uint)rd);
    }

    private void SubSp(int size)
    {
        while (size > 0)
        {
            var chunk = Math.Min(size, 4095);
            chunk -= chunk % 16;
            if (chunk == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "无法对齐栈帧。");
            }

            SubImm(Sp, Sp, chunk);
            size -= chunk;
        }
    }

    public void AddReg(int rd, int rn, int rm) =>
        EmitWord(0x8B000000u | ((uint)rm << 16) | ((uint)rn << 5) | (uint)rd);

    public void SubReg(int rd, int rn, int rm) =>
        EmitWord(0xCB000000u | ((uint)rm << 16) | ((uint)rn << 5) | (uint)rd);

    public void Mul(int rd, int rn, int rm) =>
        EmitWord(0x9B007C00u | ((uint)rm << 16) | ((uint)rn << 5) | (uint)rd);

    public void Sdiv(int rd, int rn, int rm) =>
        EmitWord(0x9AC00C00u | ((uint)rm << 16) | ((uint)rn << 5) | (uint)rd);

    public void Udiv(int rd, int rn, int rm) =>
        EmitWord(0x9AC00800u | ((uint)rm << 16) | ((uint)rn << 5) | (uint)rd);

    public void Neg(int rd, int rm) =>
        EmitWord(0xCB000000u | ((uint)rm << 16) | ((uint)Xzr << 5) | (uint)rd);

    public void Cmp(int rn, int rm) =>
        EmitWord(0xEB000000u | ((uint)rm << 16) | ((uint)rn << 5) | Xzr);

    public void CmpImm(int rn, int value)
    {
        if (value is < 0 or > 0xFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "CMP 立即数必须在 0..4095。");
        }

        EmitWord(0xF1000000u | ((uint)value << 10) | ((uint)rn << 5) | Xzr);
    }

    public void ScvtfDouble(int d, int rn) =>
        EmitWord(0x9E620000u | ((uint)rn << 5) | (uint)d);

    public void FmovDoubleFromX(int d, int rn) =>
        EmitWord(0x9E670000u | ((uint)rn << 5) | (uint)d);

    public void Cset(int rd, Arm64Condition condition)
    {
        var inverted = condition switch
        {
            Arm64Condition.Eq => Arm64Condition.Ne,
            Arm64Condition.Ne => Arm64Condition.Eq,
            Arm64Condition.Ge => Arm64Condition.Lt,
            Arm64Condition.Lt => Arm64Condition.Ge,
            Arm64Condition.Gt => Arm64Condition.Le,
            Arm64Condition.Le => Arm64Condition.Gt,
            Arm64Condition.Hi => Arm64Condition.Ls,
            Arm64Condition.Ls => Arm64Condition.Hi,
            Arm64Condition.Hs => Arm64Condition.Lo,
            Arm64Condition.Lo => Arm64Condition.Hs,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
        EmitWord(0x9A9F07E0u | ((uint)inverted << 12) | (uint)rd);
    }

    public void LslImm(int rd, int rn, int shift)
    {
        if (shift is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(shift));
        }

        var immr = (64 - shift) & 63;
        var imms = 63 - shift;
        EmitWord(0xD3400000u | ((uint)immr << 16) | ((uint)imms << 10) | ((uint)rn << 5) | (uint)rd);
    }

    public void LsrImm(int rd, int rn, int shift)
    {
        if (shift is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(shift));
        }

        EmitWord(0xD3400000u | ((uint)shift << 16) | (63u << 10) | ((uint)rn << 5) | (uint)rd);
    }

    public void Ldr(int rt, int rn, int offset)
    {
        if (offset >= 0 && offset % 8 == 0 && offset <= 32760)
        {
            EmitWord(0xF9400000u | ((uint)(offset / 8) << 10) | ((uint)rn << 5) | (uint)rt);
            return;
        }

        EmitUnscaled(0xF8400000u, rt, rn, offset);
    }

    public void Str(int rt, int rn, int offset)
    {
        if (offset >= 0 && offset % 8 == 0 && offset <= 32760)
        {
            EmitWord(0xF9000000u | ((uint)(offset / 8) << 10) | ((uint)rn << 5) | (uint)rt);
            return;
        }

        EmitUnscaled(0xF8000000u, rt, rn, offset);
    }

    public void Ldrb(int rt, int rn, int offset) => EmitUnscaled(0x38400000u, rt, rn, offset);

    public void Strb(int rt, int rn, int offset) => EmitUnscaled(0x38000000u, rt, rn, offset);

    public void Ldrh(int rt, int rn, int offset) => EmitUnscaled(0x78400000u, rt, rn, offset);

    public void Strh(int rt, int rn, int offset) => EmitUnscaled(0x78000000u, rt, rn, offset);

    private void EmitUnscaled(uint opcode, int rt, int rn, int offset)
    {
        if (offset is < -256 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "未缩放访存偏移必须在 -256..255。");
        }

        EmitWord(opcode | ((uint)(offset & 0x1FF) << 12) | ((uint)rn << 5) | (uint)rt);
    }

    public void Bl(string label) =>
        AddBranch(0x94000000u, Arm64FixupKind.Branch26, label);

    public void B(string label) =>
        AddBranch(0x14000000u, Arm64FixupKind.Branch26, label);

    public void Bcond(Arm64Condition condition, string label)
    {
        var code = condition switch
        {
            Arm64Condition.Eq => 0u,
            Arm64Condition.Ne => 1u,
            Arm64Condition.Hs => 2u,
            Arm64Condition.Lo => 3u,
            Arm64Condition.Mi => 4u,
            Arm64Condition.Pl => 5u,
            Arm64Condition.Vs => 6u,
            Arm64Condition.Vc => 7u,
            Arm64Condition.Hi => 8u,
            Arm64Condition.Ls => 9u,
            Arm64Condition.Ge => 10u,
            Arm64Condition.Lt => 11u,
            Arm64Condition.Gt => 12u,
            Arm64Condition.Le => 13u,
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
        EmitWord(0x54000000u | code);
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, Arm64FixupKind.CondBranch19, label));
    }

    public void Cbz(int register, string label)
    {
        EmitWord(0xB4000000u | (uint)register);
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, Arm64FixupKind.CompareBranch19, label));
    }

    public void Cbnz(int register, string label)
    {
        EmitWord(0xB5000000u | (uint)register);
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, Arm64FixupKind.CompareBranch19, label));
    }

    public void CallImport(string symbol)
    {
        Bl("import:" + symbol);
    }

    private void AddBranch(uint opcode, Arm64FixupKind kind, string label)
    {
        EmitWord(opcode);
        _fixups.Add(new Arm64Fixup(_bytes.Count - 4, kind, label));
    }

    private void EmitWord(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _bytes.AddRange(buffer);
    }
}
