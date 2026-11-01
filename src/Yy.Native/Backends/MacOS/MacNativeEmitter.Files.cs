using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private bool TryEmitFileBuiltin(
        CallExpression call,
        IdentifierExpression identifier,
        NativeContext context)
    {
        switch (identifier.Name)
        {
            case "文件存在" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.Bl(FileExistsLabel);
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "删除文件" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.Bl(FileDeleteLabel);
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "读文件" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.Bl(FileReadLabel);
                context.LastKind = NativeValueKind.Text;
                return true;
            case "写文件" when call.Arguments.Count == 2:
                EmitExpression(call.Arguments[0], context);
                _code.Push(0);
                EmitExpression(call.Arguments[1], context);
                _code.Pop(1);
                _code.MovReg(9, 0);
                _code.MovReg(0, 1);
                _code.MovReg(1, 9);
                _code.Bl(FileWriteLabel);
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "取目录文件" when call.Arguments.Count is 1 or 2:
                EmitExpression(call.Arguments[0], context);
                _code.Push(0);
                if (call.Arguments.Count == 2)
                {
                    EmitExpression(call.Arguments[1], context);
                }
                else
                {
                    _code.LoadAddress(0, _wildcardLabel);
                }

                _code.Pop(1);
                _code.MovReg(9, 0);
                _code.MovReg(0, 1);
                _code.MovReg(1, 9);
                _code.Bl(ListFilesLabel);
                context.LastKind = NativeValueKind.Array;
                return true;
            default:
                return false;
        }
    }

    private bool TryEmitProcessBuiltin(
        CallExpression call,
        IdentifierExpression identifier,
        NativeContext context)
    {
        if (identifier.Name != "运行程序" ||
            call.Arguments.Count is not (1 or 2))
        {
            return false;
        }

        EmitExpression(call.Arguments[0], context);
        _code.Push(0);
        if (call.Arguments.Count == 2)
        {
            EmitExpression(call.Arguments[1], context);
        }
        else
        {
            _code.LoadAddress(0, _emptyLabel);
        }

        _code.Pop(1);
        _code.MovReg(9, 0);
        _code.MovReg(0, 1);
        _code.MovReg(1, 9);
        _code.Bl(RunProcessLabel);
        context.LastKind = NativeValueKind.Integer;
        return true;
    }

    private void EmitFileRuntime()
    {
        EmitFileExists();
        EmitFileDelete();
        EmitFileRead();
        EmitFileWrite();
        EmitListFiles();
        EmitRunProcess();
    }

    private void EmitFileExists()
    {
        _code.MarkLabel(FileExistsLabel);
        _code.BeginFrame(16);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(1, 0);
        CallImport("_access");
        _code.CmpImm(0, 0);
        _code.Cset(0, Arm64Condition.Eq);
        _code.EndFrame();
    }

    private void EmitFileDelete()
    {
        _code.MarkLabel(FileDeleteLabel);
        _code.BeginFrame(16);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        CallImport("_unlink");
        _code.CmpImm(0, 0);
        _code.Cset(0, Arm64Condition.Eq);
        _code.EndFrame();
    }

    private void EmitFileRead()
    {
        var loop = NewLabel("read_loop");
        var done = NewLabel("read_done");
        var failed = NewLabel("read_failed");
        var end = NewLabel("read_end");
        _code.MarkLabel(FileReadLabel);
        _code.BeginFrame(128);
        _code.Str(0, 29, -8);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(1, 0);
        CallImport("_open");
        _code.Str(0, 29, -16);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Lt, failed);
        _code.Ldr(0, 29, -16);
        _code.LoadImmediate(1, 0);
        _code.LoadImmediate(2, 2);
        CallImport("_lseek");
        _code.Str(0, 29, -24);
        _code.Ldr(0, 29, -16);
        _code.LoadImmediate(1, 0);
        _code.LoadImmediate(2, 0);
        CallImport("_lseek");
        _code.Ldr(0, 29, -24);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset + 1);
        CallImport("_malloc");
        _code.Str(0, 29, -32);
        _code.LoadImmediate(9, 0);
        _code.Str(9, 29, -40);
        _code.MarkLabel(loop);
        _code.Ldr(9, 29, -40);
        _code.Ldr(10, 29, -24);
        _code.Cmp(9, 10);
        _code.Bcond(Arm64Condition.Ge, done);
        _code.Ldr(0, 29, -16);
        _code.Ldr(1, 29, -32);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(9, 29, -40);
        _code.AddReg(1, 1, 9);
        _code.Ldr(2, 29, -24);
        _code.Ldr(9, 29, -40);
        _code.SubReg(2, 2, 9);
        CallImport("_read");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Le, done);
        _code.Ldr(9, 29, -40);
        _code.AddReg(9, 9, 0);
        _code.Str(9, 29, -40);
        _code.B(loop);
        _code.MarkLabel(done);
        _code.Ldr(0, 29, -32);
        _code.Ldr(9, 29, -40);
        _code.Str(9, 0, NativeRuntimeLayout.CountOffset);
        _code.Ldr(0, 29, -16);
        CallImport("_close");
        _code.Ldr(0, 29, -32);
        _code.Ldr(9, 29, -40);
        _code.AddReg(0, 0, 9);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(10, 0);
        _code.Strb(10, 0, 0);
        _code.Ldr(0, 29, -32);
        _code.B(end);
        _code.MarkLabel(failed);
        _code.LoadAddress(0, _emptyLabel);
        _code.MarkLabel(end);
        _code.EndFrame();
    }

    private void EmitFileWrite()
    {
        var failed = NewLabel("write_file_failed");
        var end = NewLabel("write_file_end");
        _code.MarkLabel(FileWriteLabel);
        _code.BeginFrame(96);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(1, 0x601);
        // macOS 的 open 是可变参数函数：mode 走栈，不占用 x2。
        _code.SubImm(Arm64CodeBuffer.Sp, Arm64CodeBuffer.Sp, 16);
        _code.LoadImmediate(9, 0x1A4);
        _code.Str(9, Arm64CodeBuffer.Sp, 0);
        CallImport("_open");
        _code.AddImm(Arm64CodeBuffer.Sp, Arm64CodeBuffer.Sp, 16);
        _code.Str(0, 29, -24);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Lt, failed);
        _code.Ldr(0, 29, -24);
        _code.Ldr(1, 29, -16);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(2, 29, -16);
        _code.Ldr(2, 2, NativeRuntimeLayout.CountOffset);
        _code.Bl(WriteStreamLabel);
        _code.Str(0, 29, -32);
        _code.Ldr(0, 29, -24);
        CallImport("_close");
        _code.Ldr(0, 29, -32);
        _code.B(end);
        _code.MarkLabel(failed);
        _code.LoadImmediate(0, 0);
        _code.MarkLabel(end);
        _code.EndFrame();
    }

    private void EmitListFiles()
    {
        var loop = NewLabel("list_loop");
        var next = NewLabel("list_next");
        var dotOne = NewLabel("list_dot_one");
        var buildName = NewLabel("list_build_name");
        var closeEnd = NewLabel("list_close");
        var end = NewLabel("list_end");
        _code.MarkLabel(ListFilesLabel);
        _code.BeginFrame(160);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.LoadImmediate(0, 0);
        _code.Bl(ArrayNewLabel);
        _code.Str(0, 29, -24);
        _code.Ldr(0, 29, -8);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        CallImport("_opendir");
        _code.Str(0, 29, -32);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, end);
        _code.MarkLabel(loop);
        _code.Ldr(0, 29, -32);
        CallImport("_readdir");
        _code.Str(0, 29, -40);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, closeEnd);
        _code.Ldr(9, 29, -40);
        _code.Ldrb(10, 9, 20);
        _code.CmpImm(10, 4);
        _code.Bcond(Arm64Condition.Eq, next);
        _code.Ldr(9, 29, -40);
        _code.AddImm(1, 9, 21);
        _code.Ldrh(2, 9, 18);
        _code.CmpImm(2, 1);
        _code.Bcond(Arm64Condition.Eq, dotOne);
        _code.CmpImm(2, 2);
        _code.Bcond(Arm64Condition.Ne, buildName);
        _code.Ldrb(9, 1, 0);
        _code.CmpImm(9, '.');
        _code.Bcond(Arm64Condition.Ne, buildName);
        _code.Ldrb(9, 1, 1);
        _code.CmpImm(9, '.');
        _code.Bcond(Arm64Condition.Eq, next);
        _code.B(buildName);
        _code.MarkLabel(dotOne);
        _code.Ldrb(9, 1, 0);
        _code.CmpImm(9, '.');
        _code.Bcond(Arm64Condition.Eq, next);
        _code.MarkLabel(buildName);
        _code.MovReg(0, 1);
        _code.MovReg(1, 2);
        _code.Bl(TextNewLabel);
        _code.Str(0, 29, -48);
        _code.Ldr(0, 29, -16);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(1, 29, -48);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(2, 0);
        CallImport("_fnmatch");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, next);
        _code.Ldr(0, 29, -8);
        _code.LoadAddress(1, _slashLabel);
        _code.Bl(ConcatLabel);
        _code.Str(0, 29, -56);
        _code.Ldr(0, 29, -56);
        _code.Ldr(1, 29, -48);
        _code.Bl(ConcatLabel);
        _code.Str(0, 29, -64);
        _code.Ldr(0, 29, -24);
        _code.Ldr(1, 29, -64);
        _code.Bl(ArrayAppendLabel);
        _code.Str(0, 29, -24);
        _code.MarkLabel(next);
        _code.B(loop);
        _code.MarkLabel(closeEnd);
        _code.Ldr(0, 29, -32);
        CallImport("_closedir");
        _code.MarkLabel(end);
        _code.Ldr(0, 29, -24);
        _code.EndFrame();
    }

    private void EmitRunProcess()
    {
        var failed = NewLabel("process_failed");
        var end = NewLabel("process_end");
        _code.MarkLabel(RunProcessLabel);
        _code.BeginFrame(96);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.LoadAddress(0, _quoteLabel);
        _code.Ldr(1, 29, -8);
        _code.Bl(ConcatLabel);
        _code.Str(0, 29, -24);
        _code.Ldr(0, 29, -24);
        _code.LoadAddress(1, _quoteSpaceLabel);
        _code.Bl(ConcatLabel);
        _code.Str(0, 29, -32);
        _code.Ldr(0, 29, -32);
        _code.Ldr(1, 29, -16);
        _code.Bl(ConcatLabel);
        _code.Str(0, 29, -40);
        _code.Ldr(0, 29, -40);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        CallImport("_system");
        _code.LoadImmediate(9, -1);
        _code.Cmp(0, 9);
        _code.Bcond(Arm64Condition.Eq, failed);
        _code.MovReg(9, 0);
        _code.LslImm(9, 9, 57);
        _code.LsrImm(9, 9, 57);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Ne, failed);
        _code.LsrImm(0, 0, 8);
        _code.LslImm(0, 0, 56);
        _code.LsrImm(0, 0, 56);
        _code.B(end);
        _code.MarkLabel(failed);
        _code.LoadImmediate(0, -1);
        _code.MarkLabel(end);
        _code.EndFrame();
    }
}
