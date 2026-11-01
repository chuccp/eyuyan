using Yy.Language.Syntax;

namespace Yy.Native.Semantics;

/// <summary>
/// 平台无关的栈帧模型。每个局部变量获得一个固定的帧内槽位，
/// 具体偏移由平台后端映射到自己的栈帧或寄存器。
/// </summary>
internal sealed class NativeContext(FunctionDeclaration? function)
{
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    public FunctionDeclaration? Function { get; } = function;
    public Dictionary<string, NativeValueKind> Types { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, NativeValueKind> ArrayElementTypes { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, NativeObjectShape> ObjectShapes { get; } =
        new(StringComparer.Ordinal);
    public Stack<NativeLoop> Loops { get; } = new();
    public string? EndLabel { get; set; }
    public NativeValueKind LastKind { get; set; }
    public IReadOnlyDictionary<string, int> Slots => _slots;

    public void AddSlot(string name)
    {
        if (!_slots.ContainsKey(name))
        {
            _slots[name] = (_slots.Count + 1) * -8;
        }
    }

    public int Slot(string name, YyNode node) =>
        _slots.TryGetValue(name, out var slot)
            ? slot
            : throw new NativeCompileException(
                $"变量“{name}”没有原生栈位置",
                node.Line,
                node.Column,
                null);
}

internal sealed record NativeLoop(string Continue, string Break);
