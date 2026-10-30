using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed record NativeObjectField(
    string Name,
    NativeValueKind Kind,
    NativeValueKind ArrayElementKind);

internal sealed record NativeObjectShape(IReadOnlyList<NativeObjectField> Fields)
{
    public NativeObjectField? Find(string name) =>
        Fields.FirstOrDefault(field =>
            string.Equals(field.Name, name, StringComparison.Ordinal));

    public int IndexOf(string name)
    {
        for (var index = 0; index < Fields.Count; index++)
        {
            if (string.Equals(Fields[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed partial class NativeEmitter
{
    private void EmitObjectLiteral(ObjectExpression expression, NativeContext context)
    {
        _code.MovImmediate(NativeRegister.Rcx, expression.Properties.Count);
        _code.CallCode("__yy_array_new");
        _code.PushRax();
        for (var index = 0; index < expression.Properties.Count; index++)
        {
            EmitExpression(expression.Properties[index].Value, context);
            _code.MovRdxRax();
            _code.Pop(NativeRegister.Rcx);
            _code.StoreRdxAtRcxOffset(8 + index * 8);
            _code.PushRcx();
        }

        _code.Pop(NativeRegister.Rax);
        context.LastKind = NativeValueKind.Object;
    }

    private bool TryEmitObjectPropertyRead(
        MemberExpression member,
        NativeContext context)
    {
        var field = TryGetObjectField(member, context);
        if (field is null)
        {
            return false;
        }

        var shape = TryGetObjectShape(member.Target, context)
            ?? throw new InvalidOperationException("对象形状丢失。");
        var index = shape.IndexOf(field.Name);
        EmitExpression(member.Target, context);
        ReadObjectField(index);
        context.LastKind = field.Kind;
        return true;
    }

    private bool TryEmitObjectPropertyAssignment(
        MemberExpression member,
        Expression value,
        NativeContext context)
    {
        var field = TryGetObjectField(member, context);
        if (field is null)
        {
            return false;
        }

        if (Infer(value, context) != field.Kind)
        {
            Fail(value, $"属性“{member.Member}”需要 {field.Kind} 类型的值。");
        }

        EmitExpression(member.Target, context);
        _code.PushRax();
        EmitExpression(value, context);
        _code.MovRdxRax();
        _code.Pop(NativeRegister.Rcx);
        var shape = TryGetObjectShape(member.Target, context)
            ?? throw new InvalidOperationException("对象形状丢失。");
        _code.StoreRdxAtRcxOffset(8 + shape.IndexOf(field.Name) * 8);
        return true;
    }

    private void ReadObjectField(int index)
    {
        _code.AddRaxByte((byte)(8 + index * 8));
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
    }

    private NativeObjectField? TryGetObjectField(
        MemberExpression member,
        NativeContext context)
    {
        return TryGetObjectShape(member.Target, context)?.Find(member.Member);
    }

    private NativeObjectShape? TryGetObjectShape(
        Expression expression,
        NativeContext context)
    {
        if (expression is IdentifierExpression identifier &&
            context.ObjectShapes.TryGetValue(identifier.Name, out var shape))
        {
            return shape;
        }

        if (expression is ObjectExpression objectExpression)
        {
            var fields = new List<NativeObjectField>();
            foreach (var (name, value) in objectExpression.Properties)
            {
                var kind = Infer(value, context);
                fields.Add(new NativeObjectField(
                    name,
                    kind,
                    kind == NativeValueKind.Array
                        ? InferArrayElement(value, context)
                        : NativeValueKind.Integer));
            }

            return new NativeObjectShape(fields);
        }

        return null;
    }
}
