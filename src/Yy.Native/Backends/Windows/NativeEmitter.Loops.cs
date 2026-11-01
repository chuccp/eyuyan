using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private void EmitWhile(WhileStatement loop, NativeContext context)
    {
        var start = NewLabel("while_start");
        var end = NewLabel("while_end");
        context.Loops.Push(new NativeLoop(start, end));
        _code.MarkLabel(start);
        EmitExpression(loop.Condition, context);
        _code.TestRaxRax();
        _code.JumpIfZero(end);
        EmitBlock(loop.Body, context);
        _code.Jmp(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
    }

    private void EmitCountLoop(CountLoopStatement loop, NativeContext context)
    {
        var slot = context.Slot(loop.VariableName, loop);
        var start = NewLabel("count_start");
        var update = NewLabel("count_update");
        var end = NewLabel("count_end");
        EmitExpression(loop.Start, context);
        _code.StoreRaxToRbp(slot);
        context.Types[loop.VariableName] = NativeValueKind.Integer;
        context.Loops.Push(new NativeLoop(update, end));
        _code.MarkLabel(start);
        EmitExpression(loop.End, context);
        _code.PushRax();
        _code.LoadRaxFromRbp(slot);
        _code.Pop(NativeRegister.Rcx);
        _code.CmpRaxRcx();
        _code.JumpIfGreater(end);
        EmitBlock(loop.Body, context);
        _code.MarkLabel(update);
        EmitExpression(loop.Step, context);
        _code.PushRax();
        _code.LoadRaxFromRbp(slot);
        _code.Pop(NativeRegister.Rcx);
        _code.AddRaxRcx();
        _code.StoreRaxToRbp(slot);
        _code.Jmp(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
    }
}
