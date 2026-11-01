using Yy.Compiler.Forms;
using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private NativeValueKind? TryInferPlatform(
        Expression expression,
        NativeContext context) =>
        expression switch
        {
            MemberExpression { Member: "参数" or "结果" } member
                when GetWindowStateSlot(member, context) is not null =>
                NativeValueKind.Text,
            MemberExpression { Member: "文本" or "标题" } member
                when TryResolveUiTarget(member.Target, context, out var target) &&
                     target.SupportsText =>
                NativeValueKind.Text,
            _ => null
        };

    private bool TryEmitWindowStatePropertyRead(
        MemberExpression member,
        NativeContext context)
    {
        var slot = GetWindowStateSlot(member, context);
        if (slot is null)
        {
            return false;
        }

        LoadSlot(0, slot);
        context.LastKind = NativeValueKind.Text;
        return true;
    }

    private bool TryEmitWindowStatePropertyAssignment(
        MemberExpression member,
        AssignStatement assignment,
        NativeContext context)
    {
        var slot = GetWindowStateSlot(member, context);
        if (slot is null)
        {
            return false;
        }

        if (assignment.Operator != "=")
        {
            Fail(assignment, "窗体参数/结果只支持直接赋值");
        }

        if (Infer(assignment.Value, context) != NativeValueKind.Text)
        {
            Fail(assignment.Value, "窗体参数/结果只能写入文本");
        }

        EmitExpression(assignment.Value, context);
        StoreSlot(0, slot);
        return true;
    }

    private string? GetWindowStateSlot(MemberExpression member, NativeContext context)
    {
        if (member.Target is not IdentifierExpression alias ||
            context.Slots.ContainsKey(alias.Name))
        {
            return null;
        }

        var form = FindFormByName(alias.Name);
        return member.Member switch
        {
            "参数" => form?.ParameterSlot,
            "结果" => form?.ResultSlot,
            _ => null
        };
    }

    private MacUiForm? FindFormByName(string name)
    {
        if (_uiFormsByName.TryGetValue(name, out var form))
        {
            return form;
        }

        if (string.Equals(name, "窗口", StringComparison.Ordinal))
        {
            return _startupUiForm ?? _uiForms.FirstOrDefault();
        }

        return null;
    }

    private bool TryResolveUiTarget(
        Expression target,
        NativeContext context,
        out MacUiTarget result)
    {
        result = default!;
        if (target is MemberExpression
            {
                Target: IdentifierExpression formAlias,
                Member: string controlName
            })
        {
            if (context.Slots.ContainsKey(formAlias.Name))
            {
                return false;
            }

            var form = FindFormByName(formAlias.Name);
            var control = form?.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, controlName, StringComparison.Ordinal));
            if (control is null)
            {
                return false;
            }

            result = new MacUiTarget(control.Slot, false, control.Type, control.Name, control.AuxSlot, control.MultiLine);
            return true;
        }

        if (target is IdentifierExpression identifier)
        {
            if (context.Slots.ContainsKey(identifier.Name))
            {
                return false;
            }

            var form = FindFormByName(identifier.Name);
            if (form is not null)
            {
                result = new MacUiTarget(form.WindowSlot, true, null, null, null, false);
                return true;
            }

            if (_uiControlAliases.TryGetValue(identifier.Name, out var control))
            {
                result = new MacUiTarget(
                    control.Slot,
                    false,
                    control.Type,
                    control.Name,
                    control.AuxSlot,
                    control.MultiLine);
                return true;
            }

            return false;
        }

        if (target is CallExpression
            {
                Callee: IdentifierExpression { Name: "取控件" },
                Arguments: [LiteralExpression { Value: string id }]
            })
        {
            if (_uiControlsById.TryGetValue(id, out var control))
            {
                result = new MacUiTarget(
                    control.Slot,
                    false,
                    control.Type,
                    control.Name,
                    control.AuxSlot,
                    control.MultiLine);
                return true;
            }

            if (_uiFormsById.TryGetValue(id, out var form))
            {
                result = new MacUiTarget(form.WindowSlot, true, null, null, null, false);
                return true;
            }
        }

        return false;
    }

    private static string? TextSelector(MacUiTarget target, bool write) => target switch
    {
        { IsWindow: true } => write ? "setTitle:" : "title",
        { ControlType: YyControlTypes.Button or YyControlTypes.CheckBox or
            YyControlTypes.RadioButton } => write ? "setTitle:" : "title",
        { ControlType: YyControlTypes.TextBox or YyControlTypes.Label } =>
            write ? "setStringValue:" : "stringValue",
        { ControlType: YyControlTypes.ComboBox } =>
            write ? "selectItemWithTitle:" : "titleOfSelectedItem",
        { ControlType: YyControlTypes.GroupBox } =>
            write ? "setTitle:" : "title",
        _ => null
    };

    private bool TryEmitControlPropertyRead(
        MemberExpression member,
        NativeContext context)
    {
        if (member.Member is not ("文本" or "标题") ||
            !TryResolveUiTarget(member.Target, context, out var target))
        {
            return false;
        }

        if (target.ControlType == YyControlTypes.ListBox)
        {
            LoadSlot(0, target.AuxSlot!);
            _code.Bl(ListBoxSelectedTextLabel);
            context.LastKind = NativeValueKind.Text;
            return true;
        }

        if (target.Multiline)
        {
            LoadSlot(0, target.AuxSlot!);
            EmitMsgSend0(0, "string");
            _code.Bl(NsStringToTextLabel);
            context.LastKind = NativeValueKind.Text;
            return true;
        }

        var selector = TextSelector(target, write: false);
        if (selector is null)
        {
            Fail(member, "该控件不支持读取文本");
        }

        LoadSlot(0, target.Slot);
        EmitMsgSend0(0, selector!);
        _code.Bl(NsStringToTextLabel);
        context.LastKind = NativeValueKind.Text;
        return true;
    }

    private bool TryEmitControlPropertyAssignment(
        MemberExpression member,
        AssignStatement assignment,
        NativeContext context)
    {
        if (!TryResolveUiTarget(member.Target, context, out var target))
        {
            return false;
        }

        if (assignment.Operator != "=")
        {
            Fail(assignment, "界面属性只支持直接赋值");
        }

        if (member.Member == "图片")
        {
            if (target.IsWindow ||
                !string.Equals(
                    target.ControlType,
                    YyControlTypes.PictureBox,
                    StringComparison.Ordinal))
            {
                Fail(member, "只有图片框支持“图片”属性");
            }

            if (Infer(assignment.Value, context) != NativeValueKind.Text)
            {
                Fail(assignment.Value, "图片框“图片”属性需要文本路径");
            }

            EmitExpression(assignment.Value, context);
            _code.Bl(TextToNsStringLabel);
            EmitImageFromPath();
            _code.Push(0);
            LoadSlot(0, target.Slot);
            _code.Pop(2);
            EmitMsgSend1(0, 2, "setImage:");
            return true;
        }

        if (member.Member is not ("文本" or "标题"))
        {
            Fail(member, "当前 AppKit 后端只支持控件“文本/标题/图片”");
        }

        if (Infer(assignment.Value, context) != NativeValueKind.Text)
        {
            Fail(assignment.Value, "控件文本只能写入文本值");
        }

        if (target.ControlType == YyControlTypes.ListBox)
        {
            Fail(member, "AppKit 列表框的文本属性只读，请使用选择改变事件读取当前项");
        }

        if (target.Multiline)
        {
            EmitSetTextOnSlot(
                target.AuxSlot!,
                () => EmitExpression(assignment.Value, context),
                "setString:");
            return true;
        }

        var selector = TextSelector(target, write: true);
        if (selector is null)
        {
            Fail(member, "该控件不支持写入文本");
        }

        EmitSetTextOnSlot(
            target.Slot,
            () => EmitExpression(assignment.Value, context),
            selector!);
        return true;
    }
}
