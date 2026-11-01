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
    private const int WmClose = 0x0010;
    private readonly Dictionary<string, string> _uiHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _uiAliases = new(StringComparer.Ordinal);
    private readonly List<NativeUiControl> _uiControls = [];
    private readonly List<NativeUiForm> _uiForms = [];
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
                     "ShowWindow", "UpdateWindow", "SetForegroundWindow", "GetMessageW", "TranslateMessage",
                     "DispatchMessageW", "DefWindowProcW", "PostQuitMessage", "SetWindowTextW",
                     "GetWindowTextW", "SendMessageW", "EnableWindow", "IsWindowVisible"
                 })
        {
            AddImport("user32.dll", name);
        }

        AddImport("kernel32.dll", "GetModuleHandleW");
        AddImport("kernel32.dll", "ExitProcess");
        AddImport("kernel32.dll", "MultiByteToWideChar");
        AddImport("kernel32.dll", "WideCharToMultiByte");
        foreach (var name in new[]
                 {
                     "GdiplusStartup", "GdipCreateBitmapFromFile",
                     "GdipCreateHBITMAPFromBitmap", "GdipDisposeImage"
                 })
        {
            AddImport("gdiplus.dll", name);
        }
        _uiWideTextBufferLabel = "ui_wide_text_buffer";
        _data.Align(8);
        _data.MarkLabel(_uiWideTextBufferLabel);
        _data.AddZeroes(8192);
        _uiUtf8TextBufferLabel = "ui_utf8_text_buffer";
        _data.Align(8);
        _data.MarkLabel(_uiUtf8TextBufferLabel);
        _data.AddZeroes(8192);
        _gdiPlusTokenLabel = "gdiplus_token";
        _data.Align(8);
        _data.MarkLabel(_gdiPlusTokenLabel);
        _data.AddUInt64(0);
        _gdiPlusInputLabel = "gdiplus_input";
        _data.Align(8);
        _data.MarkLabel(_gdiPlusInputLabel);
        _data.AddUInt32(1);
        _data.AddUInt32(0);
        _data.AddUInt64(0);
        _data.AddUInt32(0);
        _data.AddUInt32(0);
        _windowInstanceLabel = "window_instance";
        _data.Align(8);
        _data.MarkLabel(_windowInstanceLabel);
        _data.AddUInt64(0);
        var emptyTextLabel = _data.AddUtf8String(string.Empty);

        var nextControlId = 1001;
        for (var formIndex = 0; formIndex < _forms.Count; formIndex++)
        {
            var form = _forms[formIndex];
            var classLabel = _data.AddUtf16String("YyNativeWindow_" + form.Name);
            var titleLabel = _data.AddUtf16String(form.Title);
            var handle = AddHandleSlot("ui_window_" + form.Name);
            var parameter = AddPointerSlot("ui_window_parameter_" + formIndex, emptyTextLabel);
            var result = AddPointerSlot("ui_window_result_" + formIndex, emptyTextLabel);
            var accepted = AddHandleSlot("ui_window_accepted_" + formIndex);
            var formId = Yy.Compiler.YyResourceIndex.FormId(form.Name);
            _uiHandles[formId] = handle;
            _uiAliases[form.Name] = handle;
            if (formIndex == 0)
            {
                _uiAliases["窗口"] = handle;
            }

            _uiForms.Add(new NativeUiForm(
                form.Name,
                formId,
                classLabel,
                titleLabel,
                handle,
                "window_class_" + formIndex,
                (int)form.Width,
                (int)form.Height,
                parameter,
                result,
                accepted,
                form.LoadEvent,
                form.OpenEvent,
                form.CloseEvent));

            for (var controlIndex = 0; controlIndex < form.Controls.Count; controlIndex++)
            {
                var control = form.Controls[controlIndex];
                var id = Yy.Compiler.YyResourceIndex.ControlId(form.Name, control.Name);
                var controlHandle = AddHandleSlot("ui_control_" + formIndex + "_" + controlIndex);
                _uiHandles[id] = controlHandle;
                _uiAliases.TryAdd(control.Name, controlHandle);
                _uiControls.Add(new NativeUiControl(
                    id,
                    form.Name,
                    control.Name,
                    ControlClass(control.Type),
                    _data.AddUtf16String(control.Text),
                    controlHandle,
                    nextControlId++,
                    ControlStyle(control),
                    control.ClickEvent,
                    (int)control.X,
                    (int)control.Y,
                    (int)control.Width,
                    (int)control.Height));
            }
        }

        foreach (var form in _uiForms)
        {
            _data.Align(8);
            _data.MarkLabel(form.ClassStructLabel);
            _data.AddUInt32(80);
            _data.AddUInt32(3);
            _data.AddFixup(FixupTargetKind.CodeLabel, "__yy_wndproc");
            _data.AddUInt32(0);
            _data.AddUInt32(0);
            _data.AddFixup(FixupTargetKind.DataLabel, _windowInstanceLabel);
            _data.AddUInt64(0);
            _data.AddUInt64(0);
            _data.AddUInt64(6);
            _data.AddUInt64(0);
            _data.AddFixup(FixupTargetKind.DataLabel, form.ClassLabel);
            _data.AddUInt64(0);
        }
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
        foreach (var form in _uiForms)
        {
            _code.LeaRcxRip("data:" + form.ClassStructLabel);
            _code.CallImport("import:RegisterClassExW");
        }

        foreach (var form in _uiForms)
        {
            EmitCreateWindow(
                "data:" + form.ClassLabel,
                "data:" + form.TitleLabel,
                0x00CF0000,
                int.MinValue,
                int.MinValue,
                form.Width,
                form.Height,
                parentHandle: null,
                controlId: 0,
                "data:" + form.HandleLabel);
            foreach (var control in _uiControls.Where(item =>
                         string.Equals(item.OwnerForm, form.Name, StringComparison.Ordinal)))
            {
                EmitCreateWindow(
                    "data:" + _data.AddUtf16String(control.ClassName),
                    "data:" + control.TextLabel,
                    control.Style,
                    control.X,
                    control.Y,
                    control.Width,
                    control.Height,
                    parentHandle: "data:" + form.HandleLabel,
                    controlId: control.Id,
                    "data:" + control.HandleLabel);
            }
        }

        _code.CallCode("__yy_main");
        foreach (var form in _uiForms)
        {
            if (form.LoadEvent is not null &&
                _functionLabels.TryGetValue(form.LoadEvent, out var loadLabel))
            {
                _code.CallCode(loadLabel);
            }
        }

        var startupForm = _uiForms[0];
        if (startupForm.OpenEvent is not null &&
            _functionLabels.TryGetValue(startupForm.OpenEvent, out var openLabel))
        {
            _code.CallCode(openLabel);
        }

        _code.LoadRcxRip("data:" + startupForm.HandleLabel);
        _code.MovImmediate(NativeRegister.Rdx, 5);
        _code.CallImport("import:ShowWindow");
        _code.LoadRcxRip("data:" + startupForm.HandleLabel);
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
        var notClose = NewLabel("wndproc_not_close");
        var notDestroy = NewLabel("wndproc_not_destroy");
        var defaultHandler = NewLabel("wndproc_default");
        _code.MarkLabel("__yy_wndproc");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.StoreR8ToRbp(-24);
        _code.StoreR9ToRbp(-32);
        _code.MovEaxEdx();
        _code.CmpEax(WmClose);
        _code.JumpIfNotZero(notClose);
        foreach (var form in _uiForms.Skip(1))
        {
            var next = NewLabel("wndproc_close_next");
            _code.LoadRcxFromRbp(-8);
            _code.LoadRaxRip("data:" + form.HandleLabel);
            _code.CmpRaxRcx();
            _code.JumpIfNotZero(next);
            _code.MovImmediate(NativeRegister.Rax, 0);
            _code.StoreRaxRip("data:" + form.AcceptedLabel);
            if (form.CloseEvent is not null &&
                _functionLabels.TryGetValue(form.CloseEvent, out var closeEventLabel))
            {
                _code.CallCode(closeEventLabel);
            }

            _code.LoadRcxFromRbp(-8);
            _code.MovImmediate(NativeRegister.Rdx, 0);
            _code.CallImport("import:ShowWindow");
            _code.MovImmediate(NativeRegister.Rax, 0);
            _code.EndFrame();
            _code.MarkLabel(next);
        }

        _code.Jmp(defaultHandler);

        _code.MarkLabel(notClose);
        _code.MovEaxEdx();
        _code.CmpEax(WmDestroy);
        _code.JumpIfNotZero(notDestroy);
        var startupForm = _uiForms[0];
        _code.LoadRcxFromRbp(-8);
        _code.LoadRaxRip("data:" + startupForm.HandleLabel);
        _code.CmpRaxRcx();
        var notStartupDestroy = NewLabel("wndproc_not_startup_destroy");
        _code.JumpIfNotZero(notStartupDestroy);
        if (startupForm.CloseEvent is not null &&
            _functionLabels.TryGetValue(startupForm.CloseEvent, out var closeLabel))
        {
            _code.CallCode(closeLabel);
        }

        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallImport("import:PostQuitMessage");
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.EndFrame();
        _code.MarkLabel(notStartupDestroy);
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.EndFrame();

        _code.MarkLabel(notDestroy);
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

        _code.Jmp(defaultHandler);
        _code.MarkLabel(defaultHandler);
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
        if (!TryGetUiHandle(member.Target, context, out var handle))
        {
            return false;
        }

        if (member.Member == "图片")
        {
            if (Infer(value, context) != NativeValueKind.Text)
            {
                Fail(value, "图片框“图片”属性需要文本路径。");
            }

            _code.LoadRaxRip("data:" + handle);
            _code.PushRax();
            EmitExpression(value, context);
            _code.MovRdxRax();
            _code.Pop(NativeRegister.Rcx);
            _code.CallCode("__yy_set_control_picture");
            return true;
        }

        if (member.Member is not ("文本" or "标题"))
        {
            Fail(member, "原生 UI 当前只支持设置控件“文本/标题”");
        }

        if (Infer(value, context) != NativeValueKind.Text)
        {
            Fail(value, "原生 UI 当前只支持把文本值写入控件");
            return false;
        }

        if (value is LiteralExpression { Value: string text })
        {
            _code.LeaRdxRip("data:" + _data.AddUtf16String(text));
            _code.LoadRcxRip("data:" + handle);
            _code.CallImport("import:SetWindowTextW");
            return true;
        }

        _code.LoadRaxRip("data:" + handle);
        _code.PushRax();
        EmitExpression(value, context);
        _code.MovRdxRax();
        _code.Pop(NativeRegister.Rcx);
        _code.CallCode("__yy_write_control_text");
        return true;
    }

    private bool TryGetUiHandle(
        Expression target,
        NativeContext context,
        out string handle)
    {
        handle = string.Empty;
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

            var form = _uiForms.FirstOrDefault(item =>
                string.Equals(item.Name, formAlias.Name, StringComparison.Ordinal));
            var owner = form ?? (formAlias.Name == "窗口" ? _uiForms.FirstOrDefault() : null);
            if (owner is null)
            {
                return false;
            }

            var controlId = Yy.Compiler.YyResourceIndex.ControlId(owner.Name, controlName);
            if (_uiHandles.TryGetValue(controlId, out var nestedHandle))
            {
                handle = nestedHandle;
                return true;
            }

            return false;
        }

        if (target is IdentifierExpression identifier)
        {
            // 用户变量和参数可以遮蔽生成控件名，保持普通词法作用域。
            if (context.Slots.ContainsKey(identifier.Name))
            {
                return false;
            }

            if (_uiAliases.TryGetValue(identifier.Name, out var aliasHandle))
            {
                handle = aliasHandle;
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
            if (_uiHandles.TryGetValue(id, out var idHandle))
            {
                handle = idHandle;
                return true;
            }

            return false;
        }

        return false;
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

    private string AddPointerSlot(string name, string target)
    {
        var label = "pointer_" + name;
        _data.Align(8);
        _data.MarkLabel(label);
        _data.AddFixup(FixupTargetKind.DataLabel, target);
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
            YyControlTypes.PictureBox => baseStyle | 0x000Eu,
            _ => baseStyle
        };
    }

    private sealed record NativeUiControl(
        string ResourceId,
        string OwnerForm,
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

    private sealed record NativeUiForm(
        string Name,
        string FormId,
        string ClassLabel,
        string TitleLabel,
        string HandleLabel,
        string ClassStructLabel,
        int Width,
        int Height,
        string ParameterLabel,
        string ResultLabel,
        string AcceptedLabel,
        string? LoadEvent,
        string? OpenEvent,
        string? CloseEvent);
}
