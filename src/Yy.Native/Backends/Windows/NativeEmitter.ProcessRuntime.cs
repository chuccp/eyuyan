using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private string _processQuoteLabel = string.Empty;
    private string _processArgumentsLabel = string.Empty;
    private string _processEmptyTextLabel = string.Empty;

    private void PrepareProcessRuntime()
    {
        foreach (var name in new[]
                 {
                     "CreateProcessW", "WaitForSingleObject",
                     "GetExitCodeProcess", "CloseHandle"
                 })
        {
            AddImport("kernel32.dll", name);
        }

        _processQuoteLabel = _data.AddUtf8String("\"");
        _processArgumentsLabel = _data.AddUtf8String("\" ");
        _processEmptyTextLabel = _data.AddUtf8String(string.Empty);
    }

    private bool TryEmitProcessBuiltin(CallExpression call, NativeContext context)
    {
        if (call.Callee is not IdentifierExpression { Name: "运行程序" } ||
            call.Arguments.Count is not (1 or 2))
        {
            return false;
        }

        EmitExpression(call.Arguments[0], context);
        _code.PushRax();
        if (call.Arguments.Count == 2)
        {
            EmitExpression(call.Arguments[1], context);
        }
        else
        {
            _code.LeaRaxRip("data:" + _processEmptyTextLabel);
        }

        _code.MovRdxRax();
        _code.Pop(NativeRegister.Rcx);
        _code.CallCode("__yy_run_process");
        context.LastKind = NativeValueKind.Integer;
        return true;
    }

    private void EmitProcessRuntime()
    {
        var failed = NewLabel("process_failed");
        var end = NewLabel("process_end");
        _code.MarkLabel("__yy_run_process");
        _code.BeginFrame(160);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LeaRcxRip("data:" + _processQuoteLabel);
        _code.LoadRdxFromRbp(-8);
        _code.CallCode("__yy_concat_string");
        _code.StoreRaxToRbp(-24);
        _code.MovRcxRax();
        _code.LeaRdxRip("data:" + _processArgumentsLabel);
        _code.CallCode("__yy_concat_string");
        _code.MovRcxRax();
        _code.LoadRdxFromRbp(-16);
        _code.CallCode("__yy_concat_string");
        _code.MovRcxRax();
        _code.CallCode("__yy_utf8_to_utf16");
        _code.StoreRaxToRbp(-32);

        _code.MovImmediate(NativeRegister.Rcx, 104);
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-40);
        _code.MovImmediate(NativeRegister.Rcx, 104);
        _code.StoreRcxAtRax();
        _code.MovImmediate(NativeRegister.Rcx, 24);
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-48);

        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.LoadRdxFromRbp(-32);
        _code.MovImmediate(NativeRegister.R8, 0);
        _code.MovImmediate(NativeRegister.R9, 0);
        _code.StoreRspImmediate(32, 0);
        _code.StoreRspImmediate(40, 0);
        _code.StoreRspImmediate(48, 0);
        _code.StoreRspImmediate(56, 0);
        _code.LoadRaxFromRbp(-40);
        _code.StoreRaxRsp(64);
        _code.LoadRaxFromRbp(-48);
        _code.StoreRaxRsp(72);
        _code.CallImport("import:CreateProcessW");
        _code.MovEaxEax();
        _code.TestRaxRax();
        _code.JumpIfZero(failed);

        _code.LoadRaxFromRbp(-48);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.MovRcxRax();
        _code.MovImmediate(NativeRegister.Rdx, -1);
        _code.CallImport("import:WaitForSingleObject");
        _code.LoadRaxFromRbp(-48);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.MovRcxRax();
        _code.LeaRdxRbp(-56);
        _code.CallImport("import:GetExitCodeProcess");
        _code.LoadRaxFromRbp(-48);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.MovRcxRax();
        _code.CallImport("import:CloseHandle");
        _code.LoadRaxFromRbp(-48);
        _code.AddRaxByte(8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.MovRcxRax();
        _code.CallImport("import:CloseHandle");
        _code.LoadRaxFromRbp(-56);
        _code.MovEaxEax();
        _code.Jmp(end);

        _code.MarkLabel(failed);
        _code.MovImmediate(NativeRegister.Rax, -1);
        _code.MarkLabel(end);
        _code.EndFrame();
    }
}
