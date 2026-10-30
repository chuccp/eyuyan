using Yy.Compiler.Forms;
using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsBorder = 0x00800000;
    private const int WmDestroy = 0x0002;
    private const int WmCommand = 0x0111;
    private readonly Dictionary<string, string> _uiHandles = new(StringComparer.Ordinal);
    private readonly List<NativeUiControl> _uiControls = [];
    private string _windowClassLabel = string.Empty;
    private string _windowTitleLabel = string.Empty;
    private string _windowHandleLabel = string.Empty;
    private string _windowClassStructLabel = string.Empty;
    private string _windowInstanceLabel = string.Empty;

    private void PrepareWin32Data()
    {
        if (_form is null)
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "RegisterClassExW", "CreateWindowExW",
                     "ShowWindow", "UpdateWindow", "GetMessageW", "TranslateMessage",
                     "DispatchMessageW", "DefWindowProcW", "PostQuitMessage", "SetWindowTextW"
                 })
        {
            AddImport("user32.dll", name);
        }

        AddImport("kernel32.dll", "GetModuleHandleW");
        AddImport("kernel32.dll", "ExitProcess");
        _windowClassLabel = _data.AddUtf16String("YyNativeWindow_" + _form.Name);
        _windowTitleLabel = _data.AddUtf16String(_form.Title);
        _windowHandleLabel = AddHandleSlot("ui_window_" + _form.Name);
        _uiHandles[Yy.Compiler.YyResourceIndex.FormId(_form.Name)] = _windowHandleLabel;

        for (var index = 0; index < _form.Controls.Count; index++)
        {
            var control = _form.Controls[index];
            var id = Yy.Compiler.YyResourceIndex.ControlId(_form.Name, control.Name);
            var handle = AddHandleSlot("ui_control_" + index);
            _uiHandles[id] = handle;
            _uiControls.Add(new NativeUiControl(
                id,
                control.Name,
                ControlClass(control.Type),
                _data.AddUtf16String(control.Text),
                handle,
                1001 + index,
                ControlStyle(control),
                control.ClickEvent,
                (int)control.X,
                (int)control.Y,
                (int)control.Width,
                (int)control.Height));
        }

        _windowClassStructLabel = "window_class";
        _data.MarkLabel(_windowClassStructLabel);
        _data.AddUInt32(80);
        _data.AddUInt32(3);
        _data.AddFixup(FixupTargetKind.CodeLabel, "__yy_wndproc");
        _data.AddUInt32(0);
        _data.AddUInt32(0);
        _windowInstanceLabel = "window_instance";
        _data.MarkLabel(_windowInstanceLabel);
        _data.AddUInt64(0);
        _data.AddUInt64(0);
        _data.AddUInt64(0);
        _data.AddUInt64(6);
        _data.AddUInt64(0);
        _data.AddFixup(FixupTargetKind.DataLabel, _windowClassLabel);
        _data.AddUInt64(0);
    }

    private void EmitWin32Entry()
    {
        if (_form is null)
        {
            return;
        }

        _code.MarkLabel("__entry");
        _code.BeginFrame(256);
        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallImport("import:GetModuleHandleW");
        _code.StoreRaxRip("data:" + _windowInstanceLabel);
        _code.LeaRcxRip("data:" + _windowClassStructLabel);
        _code.CallImport("import:RegisterClassExW");

        EmitCreateWindow(
            "data:" + _windowClassLabel,
            "data:" + _windowTitleLabel,
            0x00CF0000,
            int.MinValue,
            int.MinValue,
            (int)_form.Width,
            (int)_form.Height,
            parentHandle: null,
            controlId: 0,
            "data:" + _windowHandleLabel);
        foreach (var control in _uiControls)
        {
            EmitCreateWindow(
                "data:" + _data.AddUtf16String(control.ClassName),
                "data:" + control.TextLabel,
                control.Style,
                (int)control.X,
                (int)control.Y,
                (int)control.Width,
                (int)control.Height,
                parentHandle: "data:" + _windowHandleLabel,
                controlId: control.Id,
                "data:" + control.HandleLabel);
        }

        _code.CallCode("__yy_main");
        if (_form.LoadEvent is not null &&
            _functionLabels.TryGetValue(_form.LoadEvent, out var loadLabel))
        {
            _code.CallCode(loadLabel);
        }

        _code.LoadRcxRip("data:" + _windowHandleLabel);
        _code.MovImmediate(NativeRegister.Rdx, 5);
        _code.CallImport("import:ShowWindow");
        _code.LoadRcxRip("data:" + _windowHandleLabel);
        _code.CallImport("import:UpdateWindow");

        var loop = NewLabel("message_loop");
        var end = NewLabel("message_end");
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
        _code.Jmp(loop);
        _code.MarkLabel(end);
        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallImport("import:ExitProcess");
        _code.EndFrame();
    }

    private void EmitCreateWindow(
        string classLabel,
        string textLabel,
        uint style,
        int x,
        int y,
        int width,
        int height,
        string? parentHandle,
        int controlId,
        string handleLabel)
    {
        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.LeaRdxRip(classLabel);
        _code.LeaR8Rip(textLabel);
        _code.MovImmediate(NativeRegister.R9, style);
        _code.StoreRspImmediate(32, x);
        _code.StoreRspImmediate(40, y);
        _code.StoreRspImmediate(48, width);
        _code.StoreRspImmediate(56, height);
        if (parentHandle is null)
        {
            _code.StoreRspImmediate(64, 0);
        }
        else
        {
            _code.LoadRaxRip(parentHandle);
            _code.StoreRaxRsp(64);
        }

        _code.StoreRspImmediate(72, controlId);
        _code.LoadRaxRip("data:" + _windowInstanceLabel);
        _code.StoreRaxRsp(80);
        _code.StoreRspImmediate(88, 0);
        _code.CallImport("import:CreateWindowExW");
        _code.StoreRaxRip(handleLabel);
    }

    private void EmitWindowProcedure()
    {
        var notDestroy = NewLabel("wndproc_not_destroy");
        var notCommand = NewLabel("wndproc_not_command");
        var defaultHandler = NewLabel("wndproc_default");
        _code.MarkLabel("__yy_wndproc");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.StoreR8ToRbp(-24);
        _code.StoreR9ToRbp(-32);
        _code.MovEaxEdx();
        _code.CmpEax(WmDestroy);
        _code.JumpIfNotZero(notDestroy);
        if (_form?.CloseEvent is not null &&
            _functionLabels.TryGetValue(_form.CloseEvent, out var closeLabel))
        {
            _code.CallCode(closeLabel);
        }

        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallImport("import:PostQuitMessage");
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.EndFrame();

        _code.MarkLabel(notDestroy);
        _code.LoadRdxFromRbp(-16);
        _code.MovEaxEdx();
        _code.CmpEax(WmCommand);
        _code.JumpIfNotZero(defaultHandler);
        _code.LoadR8FromRbp(-24);
        _code.MovEaxR8D();
        _code.AndEax(0xFFFF);
        foreach (var control in _uiControls.Where(item => item.ClickEvent is not null))
        {
            var next = NewLabel("wndproc_next");
            _code.CmpEax(control.Id);
            _code.JumpIfNotZero(next);
            if (_functionLabels.TryGetValue(control.ClickEvent!, out var handler))
            {
                _code.CallCode(handler);
            }

            _code.MovImmediate(NativeRegister.Rax, 0);
            _code.EndFrame();
            _code.MarkLabel(next);
        }

        _code.Jmp(notCommand);
        _code.MarkLabel(defaultHandler);
        _code.MarkLabel(notCommand);
        _code.LoadRcxFromRbp(-8);
        _code.LoadRdxFromRbp(-16);
        _code.LoadR8FromRbp(-24);
        _code.LoadR9FromRbp(-32);
        _code.CallImport("import:DefWindowProcW");
        _code.EndFrame();
    }

    private bool TryEmitControlLookup(CallExpression call, NativeContext context)
    {
        if (call.Arguments is not [LiteralExpression { Value: string id }] ||
            !_uiHandles.TryGetValue(id, out var handle))
        {
            Fail(call, "取控件 在原生 UI 中需要静态 @id/... 标识");
            return false;
        }

        _code.LoadRaxRip("data:" + handle);
        context.LastKind = NativeValueKind.Integer;
        return true;
    }

    private bool TryEmitControlPropertyAssignment(
        MemberExpression member,
        Expression value,
        NativeContext context)
    {
        if (member.Target is not CallExpression
            {
                Callee: IdentifierExpression { Name: "取控件" },
                Arguments: [LiteralExpression { Value: string id }]
            } ||
            !_uiHandles.TryGetValue(id, out var handle))
        {
            return false;
        }

        if (member.Member is not ("文本" or "标题"))
        {
            Fail(member, "原生 UI 当前只支持设置控件“文本/标题”");
        }

        if (value is not LiteralExpression { Value: string text })
        {
            Fail(value, "原生 UI 当前只支持把文本常量写入控件");
            return false;
        }

        _code.LeaRdxRip("data:" + _data.AddUtf16String(text));
        _code.LoadRcxRip("data:" + handle);
        _code.CallImport("import:SetWindowTextW");
        return true;
    }

    private void AddImport(string dll, string function)
    {
        if (!_imports.TryGetValue(dll, out var functions))
        {
            functions = new HashSet<string>(StringComparer.Ordinal);
            _imports[dll] = functions;
        }

        functions.Add(function);
    }

    private string AddHandleSlot(string name)
    {
        var label = "handle_" + name;
        _data.Align(8);
        _data.MarkLabel(label);
        _data.AddUInt64(0);
        return label;
    }

    private static string ControlClass(string type) => type switch
    {
        YyControlTypes.Label => "STATIC",
        YyControlTypes.Button or YyControlTypes.CheckBox or
            YyControlTypes.RadioButton or YyControlTypes.GroupBox => "BUTTON",
        YyControlTypes.TextBox => "EDIT",
        YyControlTypes.ComboBox => "COMBOBOX",
        YyControlTypes.ListBox => "LISTBOX",
        YyControlTypes.PictureBox => "STATIC",
        _ => "STATIC"
    };

    private static uint ControlStyle(YyControlModel control)
    {
        var baseStyle = WsChild | WsVisible | WsTabStop;
        return control.Type switch
        {
            YyControlTypes.TextBox => baseStyle | WsBorder |
                (control.MultiLine ? 0x0004u : 0x0080u),
            YyControlTypes.CheckBox => baseStyle | 0x0003u,
            YyControlTypes.RadioButton => baseStyle | 0x0009u,
            YyControlTypes.GroupBox => baseStyle | 0x0007u,
            YyControlTypes.ComboBox => baseStyle | 0x0003u,
            YyControlTypes.ListBox => baseStyle | 0x0001u,
            _ => baseStyle
        };
    }

    private sealed record NativeUiControl(
        string ResourceId,
        string Name,
        string ClassName,
        string TextLabel,
        string HandleLabel,
        int Id,
        uint Style,
        string? ClickEvent,
        int X,
        int Y,
        int Width,
        int Height);
}
