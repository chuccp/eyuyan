using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private bool TryEmitWindowStatePropertyAssignment(
        MemberExpression member,
        Expression value,
        NativeContext context)
    {
        var label = GetWindowStateLabel(member, context);
        if (label is null)
        {
            return false;
        }

        if (Infer(value, context) != NativeValueKind.Text)
        {
            Fail(value, "窗体“参数/结果”属性只能写入文本");
            return true;
        }

        EmitExpression(value, context);
        _code.StoreRaxRip("data:" + label);
        return true;
    }

    private bool TryEmitWindowStatePropertyRead(
        MemberExpression member,
        NativeContext context)
    {
        var label = GetWindowStateLabel(member, context);
        if (label is null)
        {
            return false;
        }

        _code.LoadRaxRip("data:" + label);
        context.LastKind = NativeValueKind.Text;
        return true;
    }

    private string? GetWindowStateLabel(
        MemberExpression member,
        NativeContext context)
    {
        if (member.Target is not IdentifierExpression alias ||
            context.Slots.ContainsKey(alias.Name))
        {
            return null;
        }

        var form = _uiForms.FirstOrDefault(item =>
            string.Equals(item.Name, alias.Name, StringComparison.Ordinal));
        return member.Member switch
        {
            "参数" => form?.ParameterLabel,
            "结果" => form?.ResultLabel,
            _ => null
        };
    }

    private bool TryEmitWindowCommand(
        CallExpression call,
        NativeContext context)
    {
        if (call.Callee is not IdentifierExpression identifier ||
            identifier.Name is not ("打开窗口" or "显示窗口" or "关闭窗口"))
        {
            return false;
        }

        if (call.Arguments is not [LiteralExpression { Value: string requestedFormId }, ..])
        {
            Fail(call, $"{identifier.Name} 的第一个参数需要静态窗体 @id/... 标识");
            return true;
        }

        var form = FindWindowForm(call, requestedFormId);
        if (form is null)
        {
            return true;
        }

        if (identifier.Name == "关闭窗口")
        {
            EmitCloseWindow(call, form, context);
            return true;
        }

        if (call.Arguments.Count != 2 ||
            call.Arguments[1] is not LiteralExpression { Value: string ownerFormId })
        {
            Fail(call, $"{identifier.Name} 的第二个参数需要拥有者窗体 @id/... 标识");
            return true;
        }

        var owner = FindWindowForm(call, ownerFormId);
        if (owner is null)
        {
            return true;
        }

        if (string.Equals(form.FormId, owner.FormId, StringComparison.Ordinal))
        {
            Fail(call, "窗口不能以自己作为拥有者打开");
            return true;
        }

        EmitOpenWindow(
            form,
            owner,
            runModal: identifier.Name == "打开窗口",
            context);
        return true;
    }

    private void EmitOpenWindow(
        NativeUiForm form,
        NativeUiForm owner,
        bool runModal,
        NativeContext context)
    {
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.StoreRaxRip("data:" + form.AcceptedLabel);
        if (form.OpenEvent is not null &&
            _functionLabels.TryGetValue(form.OpenEvent, out var openHandler))
        {
            _code.CallCode(openHandler);
        }

        _code.LoadRcxRip("data:" + form.HandleLabel);
        _code.MovImmediate(NativeRegister.Rdx, 5);
        _code.CallImport("import:ShowWindow");
        _code.LoadRcxRip("data:" + form.HandleLabel);
        _code.CallImport("import:UpdateWindow");
        _code.LoadRcxRip("data:" + form.HandleLabel);
        _code.CallImport("import:SetForegroundWindow");
        if (runModal)
        {
            _code.LoadRcxRip("data:" + owner.HandleLabel);
            _code.LoadRaxRip("data:" + form.HandleLabel);
            _code.MovRdxRax();
            _code.CallCode("__yy_run_modal");
            _code.LoadRaxRip("data:" + form.AcceptedLabel);
        }
        else
        {
            _code.MovImmediate(NativeRegister.Rax, 1);
        }

        context.LastKind = NativeValueKind.Boolean;
    }

    private void EmitCloseWindow(
        CallExpression call,
        NativeUiForm form,
        NativeContext context)
    {
        if (call.Arguments.Count != 2)
        {
            Fail(call, "关闭窗口需要窗体标识和确认结果两个参数");
            return;
        }

        var accepted = call.Arguments[1];
        if (Infer(accepted, context) != NativeValueKind.Boolean)
        {
            Fail(accepted, "关闭窗口的第二个参数必须是逻辑值");
        }

        EmitExpression(accepted, context);
        _code.StoreRaxRip("data:" + form.AcceptedLabel);
        if (form.CloseEvent is not null &&
            _functionLabels.TryGetValue(form.CloseEvent, out var closeHandler))
        {
            _code.CallCode(closeHandler);
        }

        _code.LoadRcxRip("data:" + form.HandleLabel);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.CallImport("import:ShowWindow");
        _code.MovImmediate(NativeRegister.Rax, 1);
        context.LastKind = NativeValueKind.Boolean;
    }

    private NativeUiForm? FindWindowForm(CallExpression call, string formId)
    {
        var form = _uiForms.FirstOrDefault(item =>
            string.Equals(item.FormId, formId, StringComparison.Ordinal));
        if (form is null)
        {
            Fail(call, $"没有找到窗体标识“{formId}”");
        }

        return form;
    }

    private void EmitRunModal()
    {
        var loop = NewLabel("modal_loop");
        var end = NewLabel("modal_end");
        _code.MarkLabel("__yy_run_modal");
        _code.BeginFrame(256);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LoadRcxFromRbp(-8);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.CallImport("import:EnableWindow");

        _code.MarkLabel(loop);
        _code.LeaRcxRbp(-192);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.MovImmediate(NativeRegister.R8, 0);
        _code.MovImmediate(NativeRegister.R9, 0);
        _code.CallImport("import:GetMessageW");
        _code.TestRaxRax();
        _code.JumpIfLessOrEqual(end);
        _code.LeaRcxRbp(-192);
        _code.CallImport("import:TranslateMessage");
        _code.LeaRcxRbp(-192);
        _code.CallImport("import:DispatchMessageW");
        _code.LoadRcxFromRbp(-16);
        _code.CallImport("import:IsWindowVisible");
        _code.TestRaxRax();
        _code.JumpIfNotZero(loop);

        _code.MarkLabel(end);
        _code.LoadRcxFromRbp(-8);
        _code.MovImmediate(NativeRegister.Rdx, 1);
        _code.CallImport("import:EnableWindow");
        _code.LoadRcxFromRbp(-8);
        _code.CallImport("import:SetForegroundWindow");
        _code.EndFrame();
    }
}
