namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private const int UiWideTextCapacity = 4096;
    private string _uiWideTextBufferLabel = string.Empty;
    private string _uiUtf8TextBufferLabel = string.Empty;

    private bool TryEmitControlPropertyRead(
        Language.Syntax.MemberExpression member,
        NativeContext context)
    {
        if (member.Target is not Language.Syntax.CallExpression
            {
                Callee: Language.Syntax.IdentifierExpression { Name: "取控件" },
                Arguments:
                [
                    Language.Syntax.LiteralExpression { Value: string id }
                ]
            } ||
            !_uiHandles.TryGetValue(id, out var handle))
        {
            return false;
        }

        if (member.Member is not ("文本" or "标题"))
        {
            Fail(member, "原生 UI 当前只支持读取控件“文本/标题”");
        }

        _code.LoadRcxRip("data:" + handle);
        _code.CallCode("__yy_read_control_text");
        context.LastKind = NativeValueKind.Text;
        return true;
    }

    private void EmitReadControlText()
    {
        _code.MarkLabel("__yy_read_control_text");
        _code.BeginFrame(80);
        _code.StoreRcxToRbp(-8);
        _code.LoadRcxFromRbp(-8);
        _code.LeaRdxRip("data:" + _uiWideTextBufferLabel);
        _code.MovImmediate(NativeRegister.R8, UiWideTextCapacity);
        _code.CallImport("import:GetWindowTextW");

        _code.MovImmediate(NativeRegister.Rcx, 65001);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.LeaR8Rip("data:" + _uiWideTextBufferLabel);
        _code.MovImmediate(NativeRegister.R9, -1);
        _code.LeaRaxRip("data:" + _uiUtf8TextBufferLabel);
        _code.AddRaxByte(8);
        _code.StoreRaxRsp(32);
        _code.StoreRspImmediate(40, UiWideTextCapacity - 1);
        _code.StoreRspImmediate(48, 0);
        _code.StoreRspImmediate(56, 0);
        _code.CallImport("import:WideCharToMultiByte");
        _code.MovEaxEax();
        _code.StoreRaxToRbp(-16);
        _code.LeaRaxRip("data:" + _uiUtf8TextBufferLabel);
        _code.LoadRcxFromRbp(-16);
        _code.DecRcx();
        _code.StoreRcxAtRax();
        _code.LeaRcxRip("data:" + _uiUtf8TextBufferLabel);
        _code.CallCode("__yy_clone_string");
        _code.EndFrame();
    }

    private void EmitCloneString()
    {
        _code.MarkLabel("__yy_clone_string");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.LoadRaxFromRbp(-8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-16);
        _code.AddRaxByte(9);
        _code.MovRcxRax();
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-24);
        _code.MovRcxRbp(-16);
        _code.StoreRcxAtRax();
        _code.LoadRaxFromRbp(-24);
        _code.AddRaxByte(8);
        _code.MovRcxRax();
        _code.LoadRaxFromRbp(-8);
        _code.AddRaxByte(8);
        _code.MovRdxRax();
        _code.LoadR8FromRbp(-16);
        _code.CallCode("__yy_copy_bytes");
        _code.LoadRaxFromRbp(-24);
        _code.EndFrame();
    }

    private void EmitWriteControlText()
    {
        _code.MarkLabel("__yy_write_control_text");
        _code.BeginFrame(80);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LoadRaxFromRbp(-16);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-24);

        _code.MovImmediate(NativeRegister.Rcx, 65001);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.LoadRaxFromRbp(-16);
        _code.AddRaxByte(8);
        _code.MovR8Rax();
        _code.LoadR9FromRbp(-24);
        _code.LeaRaxRip("data:" + _uiWideTextBufferLabel);
        _code.StoreRaxRsp(32);
        _code.StoreRspImmediate(40, UiWideTextCapacity);
        _code.StoreRspImmediate(48, 0);
        _code.StoreRspImmediate(56, 0);
        _code.CallImport("import:MultiByteToWideChar");
        _code.MovEaxEax();
        _code.AddRaxRax();
        _code.LeaRcxRip("data:" + _uiWideTextBufferLabel);
        _code.AddRcxRax();
        _code.StoreZeroWordRcx();

        _code.LoadRcxFromRbp(-8);
        _code.LeaRdxRip("data:" + _uiWideTextBufferLabel);
        _code.CallImport("import:SetWindowTextW");
        _code.MovImmediate(NativeRegister.Rax, 1);
        _code.EndFrame();
    }
}
