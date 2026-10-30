using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private static string ForeachSlotName(ForEachStatement loop, string suffix) =>
        $"__foreach_{loop.Line}_{loop.Column}_{suffix}";

    private void EmitArrayRuntime()
    {
        _code.MarkLabel("__yy_array_new");
        _code.BeginFrame(32);
        _code.StoreRcxToRbp(-8);
        _code.LoadRaxFromRbp(-8);
        _code.ShlRax3();
        _code.AddRaxByte(8);
        _code.MovRcxRax();
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-16);
        _code.LoadRcxFromRbp(-8);
        _code.StoreRcxAtRax();
        _code.LoadRaxFromRbp(-16);
        _code.EndFrame();

        _code.MarkLabel("__yy_array_append");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LoadRaxFromRbp(-8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-24);

        _code.ShlRax3();
        _code.AddRaxByte(16);
        _code.MovRcxRax();
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-32);
        _code.LoadRcxFromRbp(-32);
        _code.LoadRdxFromRbp(-8);
        _code.LoadRaxFromRbp(-24);
        _code.ShlRax3();
        _code.AddRaxByte(8);
        _code.MovR8Rax();
        _code.CallCode("__yy_copy_bytes");

        _code.LoadRaxFromRbp(-32);
        _code.LoadRcxFromRbp(-24);
        _code.IncRcx();
        _code.StoreRcxAtRax();
        _code.LoadRcxFromRbp(-32);
        _code.LoadRaxFromRbp(-24);
        _code.ShlRax3();
        _code.AddRaxByte(8);
        _code.LoadRdxFromRbp(-16);
        _code.StoreRdxAtRcxRax();
        _code.LoadRaxFromRbp(-32);
        _code.EndFrame();
    }

    private void EmitArrayLiteral(ArrayExpression array, NativeContext context)
    {
        _code.MovImmediate(NativeRegister.Rcx, array.Items.Count);
        _code.CallCode("__yy_array_new");
        _code.PushRax();
        for (var index = 0; index < array.Items.Count; index++)
        {
            EmitExpression(array.Items[index], context);
            _code.MovRdxRax();
            _code.Pop(NativeRegister.Rcx);
            _code.StoreRdxAtRcxOffset(8 + index * 8);
            _code.PushRcx();
        }

        _code.Pop(NativeRegister.Rax);
        context.LastKind = NativeValueKind.Array;
    }

    private void EmitArrayIndex(IndexExpression index, NativeContext context)
    {
        EmitExpression(index.Target, context);
        _code.PushRax();
        EmitExpression(index.Index, context);
        _code.ShlRax3();
        _code.AddRaxByte(8);
        _code.Pop(NativeRegister.Rcx);
        _code.MovRaxRcxIndexed();
        context.LastKind = InferArrayElement(index, context);
    }

    private void EmitArrayAppend(CallExpression call, NativeContext context)
    {
        if (call.Arguments.Count != 2 ||
            call.Arguments[0] is not IdentifierExpression)
        {
            Fail(call, "原生“追加”当前要求第一个参数是数组变量。");
            return;
        }

        var arrayIdentifier = (IdentifierExpression)call.Arguments[0];
        var slot = context.Slot(arrayIdentifier.Name, arrayIdentifier);
        EmitExpression(call.Arguments[0], context);
        _code.PushRax();
        EmitExpression(call.Arguments[1], context);
        _code.MovRdxRax();
        _code.Pop(NativeRegister.Rcx);
        _code.CallCode("__yy_array_append");
        _code.StoreRaxToRbp(slot);
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
        _code.StoreRaxToRbp(collectionSlot);
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.StoreRaxToRbp(indexSlot);
        var elementKind = InferArrayElement(loop.Collection, context);
        context.Types[loop.VariableName] = elementKind;
        context.Loops.Push(new NativeLoop(update, end));

        _code.MarkLabel(start);
        _code.LoadRaxFromRbp(indexSlot);
        _code.PushRax();
        _code.LoadRaxFromRbp(collectionSlot);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.MovRdxRax();
        _code.Pop(NativeRegister.Rax);
        _code.CmpRaxRdx();
        _code.JumpIfGreaterOrEqual(end);

        _code.LoadRaxFromRbp(collectionSlot);
        _code.MovRcxRax();
        _code.LoadRaxFromRbp(indexSlot);
        _code.ShlRax3();
        _code.AddRaxByte(8);
        _code.MovRaxRcxIndexed();
        _code.StoreRaxToRbp(itemSlot);
        EmitBlock(loop.Body, context);

        _code.MarkLabel(update);
        _code.LoadRaxFromRbp(indexSlot);
        _code.IncRax();
        _code.StoreRaxToRbp(indexSlot);
        _code.Jmp(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
    }

    private NativeValueKind InferArrayElement(
        Expression expression,
        NativeContext context)
    {
        if (expression is ArrayExpression array)
        {
            if (array.Items.Count == 0)
            {
                return NativeValueKind.Integer;
            }

            var kind = Infer(array.Items[0], context);
            if (array.Items.Skip(1).Any(item => Infer(item, context) != kind))
            {
                Fail(array, "原生数组当前要求所有元素类型一致。");
            }

            return kind;
        }

        if (expression is IdentifierExpression identifier &&
            context.ArrayElementTypes.TryGetValue(identifier.Name, out var elementKind))
        {
            return elementKind;
        }

        if (expression is IndexExpression index &&
            index.Target is IdentifierExpression indexIdentifier &&
            context.ArrayElementTypes.TryGetValue(indexIdentifier.Name, out var indexedKind))
        {
            return indexedKind;
        }

        if (expression is MemberExpression member &&
            TryGetObjectField(member, context) is
            {
                Kind: NativeValueKind.Array
            } field)
        {
            return field.ArrayElementKind;
        }

        if (expression is CallExpression
            {
                Callee: IdentifierExpression { Name: "取目录文件" }
            })
        {
            return NativeValueKind.Text;
        }

        return NativeValueKind.Integer;
    }
}
