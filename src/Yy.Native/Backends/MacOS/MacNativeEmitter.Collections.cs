using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private static string ForeachSlotName(ForEachStatement loop, string suffix) =>
        $"__foreach_{loop.Line}_{loop.Column}_{suffix}";

    private void EmitArrayLiteral(ArrayExpression array, NativeContext context)
    {
        _code.LoadImmediate(0, array.Items.Count);
        _code.Bl(ArrayNewLabel);
        _code.Push(0);
        for (var index = 0; index < array.Items.Count; index++)
        {
            EmitExpression(array.Items[index], context);
            _code.Pop(1);
            _code.Str(0, 1, NativeRuntimeLayout.PayloadOffset + index * 8);
            _code.Push(1);
        }

        _code.Pop(0);
        context.LastKind = NativeValueKind.Array;
    }

    private void EmitObjectLiteral(ObjectExpression expression, NativeContext context)
    {
        _code.LoadImmediate(0, expression.Properties.Count);
        _code.Bl(ArrayNewLabel);
        _code.Push(0);
        for (var index = 0; index < expression.Properties.Count; index++)
        {
            EmitExpression(expression.Properties[index].Value, context);
            _code.Pop(1);
            _code.Str(0, 1, NativeRuntimeLayout.PayloadOffset + index * 8);
            _code.Push(1);
        }

        _code.Pop(0);
        context.LastKind = NativeValueKind.Object;
    }

    private void EmitArrayIndex(IndexExpression index, NativeContext context)
    {
        EmitExpression(index.Target, context);
        _code.Push(0);
        EmitExpression(index.Index, context);
        _code.LslImm(0, 0, 3);
        _code.Pop(1);
        _code.AddReg(0, 1, 0);
        _code.Ldr(0, 0, NativeRuntimeLayout.PayloadOffset);
        context.LastKind = InferArrayElement(index, context);
    }

    private void EmitArrayAppend(CallExpression call, NativeContext context)
    {
        if (call.Arguments.Count != 2 ||
            call.Arguments[0] is not IdentifierExpression arrayIdentifier)
        {
            Fail(call, "原生“追加”当前要求第一个参数是数组变量。");
            return;
        }

        var slot = context.Slot(arrayIdentifier.Name, arrayIdentifier);
        EmitExpression(call.Arguments[0], context);
        _code.Push(0);
        EmitExpression(call.Arguments[1], context);
        _code.Pop(1);
        _code.MovReg(9, 0);
        _code.MovReg(0, 1);
        _code.MovReg(1, 9);
        _code.Bl(ArrayAppendLabel);
        _code.Str(0, 29, slot);
        context.Types[arrayIdentifier.Name] = NativeValueKind.Array;
        context.ArrayElementTypes[arrayIdentifier.Name] =
            Infer(call.Arguments[1], context);
        context.LastKind = NativeValueKind.Array;
    }

    private void EmitForEach(ForEachStatement loop, NativeContext context)
    {
        var collectionSlot = context.Slot(ForeachSlotName(loop, "collection"), loop);
        var indexSlot = context.Slot(ForeachSlotName(loop, "index"), loop);
        var itemSlot = context.Slot(loop.VariableName, loop);
        var start = NewLabel("foreach_start");
        var update = NewLabel("foreach_update");
        var end = NewLabel("foreach_end");

        EmitExpression(loop.Collection, context);
        _code.Str(0, 29, collectionSlot);
        _code.LoadImmediate(0, 0);
        _code.Str(0, 29, indexSlot);
        context.Types[loop.VariableName] =
            InferArrayElement(loop.Collection, context);
        context.Loops.Push(new NativeLoop(update, end));

        _code.MarkLabel(start);
        _code.Ldr(0, 29, indexSlot);
        _code.Push(0);
        _code.Ldr(0, 29, collectionSlot);
        _code.Ldr(1, 0, NativeRuntimeLayout.CountOffset);
        _code.Pop(0);
        _code.Cmp(0, 1);
        _code.Bcond(Arm64Condition.Ge, end);

        _code.Ldr(0, 29, collectionSlot);
        _code.Ldr(1, 29, indexSlot);
        _code.LslImm(1, 1, 3);
        _code.AddReg(0, 0, 1);
        _code.Ldr(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Str(0, 29, itemSlot);
        EmitBlock(loop.Body, context);

        _code.MarkLabel(update);
        _code.Ldr(0, 29, indexSlot);
        _code.AddImm(0, 0, 1);
        _code.Str(0, 29, indexSlot);
        _code.B(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
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
        _code.Ldr(0, 0, NativeRuntimeLayout.PayloadOffset + index * 8);
        context.LastKind = field.Kind;
        return true;
    }
}
