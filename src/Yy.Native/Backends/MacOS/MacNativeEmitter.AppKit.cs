using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private sealed record MacUiControl(
        string ResourceId,
        string OwnerForm,
        string Name,
        string Type,
        string Text,
        string TextLabel,
        string Slot,
        int Tag,
        string? ActionEvent,
        string? TextChangedEvent,
        string Foreground,
        string Background,
        IReadOnlyList<string> Items,
        IReadOnlyList<string> ItemLabels,
        string AuxSlot,
        string ItemArraySlot,
        int X,
        int Y,
        int Width,
        int Height,
        int FontSize,
        bool IsEnabled,
        bool IsVisible,
        bool IsChecked,
        bool ReadOnly,
        bool MultiLine,
        string PasswordChar,
        string PicturePath,
        string PictureLabel);

    private sealed record MacUiForm(
        string Name,
        string FormId,
        string WindowSlot,
        string ParameterSlot,
        string ResultSlot,
        string AcceptedSlot,
        string ModalSlot,
        string TitleLabel,
        string Background,
        int Width,
        int Height,
        int StyleMask,
        string? LoadEvent,
        string? OpenEvent,
        string? CloseEvent,
        List<MacUiControl> Controls);

    private sealed record MacUiTarget(
        string Slot,
        bool IsWindow,
        string? ControlType,
        string? ControlName,
        string? AuxSlot,
        bool Multiline)
    {
        public bool SupportsText =>
            IsWindow ||
            ControlType is not null &&
            ControlType != YyControlTypes.PictureBox;
    }

    private readonly List<MacUiForm> _uiForms = [];
    private readonly List<MacUiControl> _uiControls = [];
    private readonly Dictionary<string, MacUiForm> _uiFormsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MacUiForm> _uiFormsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MacUiControl> _uiControlAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MacUiControl> _uiControlsById = new(StringComparer.Ordinal);
    private MacUiForm? _startupUiForm;
    private string _appSlot = string.Empty;
    private string _actionTargetSlot = string.Empty;
    private string _windowDelegateSlot = string.Empty;
    private string _actionTargetClassSlot = string.Empty;
    private string _windowDelegateClassSlot = string.Empty;
    private string _listDataSourceSlot = string.Empty;
    private string _listDataSourceClassSlot = string.Empty;
    private string _listColumnLabel = string.Empty;
    private int _nextControlTag = 1;

    private void PrepareAppKitData()
    {
        _appSlot = AddDataSlot("app");
        _actionTargetSlot = AddDataSlot("action_target");
        _windowDelegateSlot = AddDataSlot("window_delegate");
        _actionTargetClassSlot = AddDataSlot("action_target_class");
        _windowDelegateClassSlot = AddDataSlot("window_delegate_class");
        _listDataSourceSlot = AddDataSlot("list_data_source");
        _listDataSourceClassSlot = AddDataSlot("list_data_source_class");
        _listColumnLabel = _data.AddUtf8String("value");

        for (var formIndex = 0; formIndex < _forms.Count; formIndex++)
        {
            var form = _forms[formIndex];
            var formId = YyResourceIndex.FormId(form.Name);
            var controls = new List<MacUiControl>();
            var uiForm = new MacUiForm(
                form.Name,
                formId,
                AddDataSlot($"window_{formIndex}"),
                AddDataSlot($"parameter_{formIndex}"),
                AddDataSlot($"result_{formIndex}"),
                AddDataSlot($"accepted_{formIndex}"),
                AddDataSlot($"modal_{formIndex}"),
                _data.AddUtf8String(form.Title),
                form.Background,
                (int)form.Width,
                (int)form.Height,
                WindowStyleMask(form),
                form.LoadEvent,
                form.OpenEvent,
                form.CloseEvent,
                controls);
            _uiForms.Add(uiForm);
            _uiFormsById[formId] = uiForm;
            _uiFormsByName[form.Name] = uiForm;

            for (var controlIndex = 0; controlIndex < form.Controls.Count; controlIndex++)
            {
                var control = form.Controls[controlIndex];
                var id = YyResourceIndex.ControlId(form.Name, control.Name);
                var slot = AddDataSlot($"control_{formIndex}_{controlIndex}");
                var picturePath = ResolvePicture(control.Picture);
                var isList = control.Type is YyControlTypes.ListBox or YyControlTypes.ComboBox;
                var items = isList
                    ? control.Text.Split(
                        [',', '，'],
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    : [];
                var actionEvent = control.Type switch
                {
                    YyControlTypes.ListBox or YyControlTypes.ComboBox =>
                        control.SelectionChangedEvent ?? control.ClickEvent,
                    YyControlTypes.CheckBox or YyControlTypes.RadioButton =>
                        control.CheckedChangedEvent ?? control.ClickEvent,
                    _ => control.ClickEvent
                };
                var textChangedEvent = control.TextChangedEvent;
                var tag = actionEvent is null && textChangedEvent is null
                    ? 0
                    : _nextControlTag++;
                var auxSlot = control.Type == YyControlTypes.ListBox ||
                              control.Type == YyControlTypes.TextBox && control.MultiLine
                    ? AddDataSlot($"aux_{formIndex}_{controlIndex}")
                    : string.Empty;
                var itemArraySlot = control.Type == YyControlTypes.ListBox
                    ? AddDataSlot($"listitems_{formIndex}_{controlIndex}")
                    : string.Empty;
                var itemLabels = items
                    .Select(item => _data.AddUtf8String(item))
                    .ToArray();
                var uiControl = new MacUiControl(
                    id,
                    form.Name,
                    control.Name,
                    control.Type,
                    control.Text,
                    _data.AddUtf8String(control.Text),
                    slot,
                    tag,
                    actionEvent,
                    textChangedEvent,
                    control.Foreground,
                    control.Background,
                    items,
                    itemLabels,
                    auxSlot,
                    itemArraySlot,
                    (int)control.X,
                    (int)control.Y,
                    (int)control.Width,
                    (int)control.Height,
                    (int)control.FontSize,
                    control.IsEnabled,
                    control.IsVisible,
                    control.IsChecked,
                    control.ReadOnly,
                    control.MultiLine,
                    control.PasswordChar,
                    picturePath,
                    _data.AddUtf8String(picturePath));
                controls.Add(uiControl);
                _uiControls.Add(uiControl);
                _uiControlsById[id] = uiControl;
                _uiControlAliases.TryAdd(control.Name, uiControl);
            }
        }

        _startupUiForm = _uiForms.FirstOrDefault(item =>
                string.Equals(item.Name, _startupForm?.Name, StringComparison.Ordinal))
            ?? _uiForms.FirstOrDefault();
    }

    private string ResolvePicture(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return string.Empty;
        }

        return YyResourceIndex.ResolveResourcePath(_projectDirectory, reference)
            ?? (reference.StartsWith("@资源/", StringComparison.Ordinal)
                ? string.Empty
                : reference);
    }

    private void EmitAppKitEntry()
    {
        _code.MarkLabel("__entry");
        _code.BeginFrame(64);
        CallImport("_objc_autoreleasePoolPush", MacLibraries.ObjectiveC);
        EmitCreateApplication();
        EmitCreateRuntimeClasses();
        EmitRegisterNotificationObservers();
        EmitCreateWindows();
        foreach (var form in _uiForms)
        {
            _code.LoadAddress(0, _emptyLabel);
            StoreSlot(0, form.ParameterSlot);
            StoreSlot(0, form.ResultSlot);
        }

        _code.Bl("__yy_main");
        foreach (var form in _uiForms)
        {
            if (form.LoadEvent is not null &&
                _functionLabels.TryGetValue(form.LoadEvent, out var loadLabel))
            {
                _code.Bl(loadLabel);
            }
        }

        if (_startupUiForm is not null)
        {
            if (_startupUiForm.OpenEvent is not null &&
                _functionLabels.TryGetValue(_startupUiForm.OpenEvent, out var openLabel))
            {
                _code.Bl(openLabel);
            }

            EmitShowWindow(_startupUiForm);
        }

        LoadSlot(0, _appSlot);
        EmitMsgSend0(0, "run");
        _code.LoadImmediate(0, 0);
        CallImport("_exit");
        _code.EndFrame();
    }

    private void EmitCreateApplication()
    {
        EmitObjcClass(0, "NSApplication");
        EmitMsgSend0(0, "sharedApplication");
        StoreSlot(0, _appSlot);
        LoadSlot(0, _appSlot);
        EmitMsgSend1Imm(0, 0, "setActivationPolicy:");
    }

    private void EmitCreateRuntimeClasses()
    {
        EmitCreateClass(
            "YyAppKitTarget",
            _actionTargetClassSlot,
            (ActionImpLabel, "handleAction:", "v@:@"));
        LoadSlot(0, _actionTargetClassSlot);
        EmitMsgSend0(0, "new");
        StoreSlot(0, _actionTargetSlot);

        EmitCreateClass(
            "YyAppKitWindowDelegate",
            _windowDelegateClassSlot,
            (WindowWillCloseImpLabel, "windowWillClose:", "v@:@"),
            (ControlTextChangedImpLabel, "controlTextDidChange:", "v@:@"),
            (MultilineTextChangedImpLabel, "textDidChange:", "v@:@"),
            (ListSelectionImpLabel, "tableViewSelectionDidChange:", "v@:@"),
            (WindowShouldCloseImpLabel, "windowShouldClose:", "c@:@"));
        LoadSlot(0, _windowDelegateClassSlot);
        EmitMsgSend0(0, "new");
        StoreSlot(0, _windowDelegateSlot);

        EmitCreateClass(
            "YyListDataSource",
            _listDataSourceClassSlot,
            (ListRowsImpLabel, "numberOfRowsInTableView:", "q@:@"),
            (ListValueImpLabel, "tableView:objectValueForTableColumn:row:", "@@:@@q"));
        LoadSlot(0, _listDataSourceClassSlot);
        EmitMsgSend0(0, "new");
        StoreSlot(0, _listDataSourceSlot);
    }

    private void EmitCreateClass(
        string name,
        string classSlot,
        params (string Implementation, string Selector, string Types)[] methods)
    {
        EmitObjcClass(12, "NSObject");
        LoadCStringAddress(1, name);
        _code.MovReg(0, 12);
        _code.LoadImmediate(2, 0);
        CallImport("_objc_allocateClassPair", MacLibraries.ObjectiveC);
        StoreSlot(0, classSlot);

        foreach (var (implementation, selector, types) in methods)
        {
            LoadSlot(0, classSlot);
            _code.Push(0);
            _code.LoadCodeAddress(2, implementation);
            _code.Push(2);
            LoadCStringAddress(3, types);
            _code.Push(3);
            EmitSelector(1, selector);
            _code.Pop(3);
            _code.Pop(2);
            _code.Pop(0);
            CallImport("_class_addMethod", MacLibraries.ObjectiveC);
        }

        LoadSlot(0, classSlot);
        CallImport("_objc_registerClassPair", MacLibraries.ObjectiveC);
    }

    private void EmitCreateWindows()
    {
        foreach (var form in _uiForms)
        {
            EmitCreateWindow(form);
            foreach (var control in form.Controls)
            {
                EmitCreateControl(form, control);
            }
        }
    }

    private void EmitCreateWindow(MacUiForm form)
    {
        EmitObjcClass(0, "NSWindow");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithContentRect:styleMask:backing:defer:");
        _code.Pop(0);
        _code.LoadImmediate(2, form.StyleMask);
        _code.LoadImmediate(3, 2);
        _code.LoadImmediate(4, 0);
        EmitRect(0, 0, form.Width, form.Height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        StoreSlot(0, form.WindowSlot);

        EmitSetTextOnSlot(
            form.WindowSlot,
            () => _code.LoadAddress(0, form.TitleLabel),
            "setTitle:");
        LoadSlot(0, form.WindowSlot);
        EmitMsgSend1Imm(0, 0, "setReleasedWhenClosed:");
        LoadSlot(0, form.WindowSlot);
        EmitMsgSend0(0, "center");
        EmitApplyWindowColor(form);
    }

    private void EmitRegisterNotificationObservers()
    {
        EmitRegisterNotificationObserver(
            "NSWindowWillCloseNotification",
            "windowWillClose:");
        EmitRegisterNotificationObserver(
            "NSControlTextDidChangeNotification",
            "controlTextDidChange:");
        EmitRegisterNotificationObserver(
            "NSTableViewSelectionDidChangeNotification",
            "tableViewSelectionDidChange:");
        EmitRegisterNotificationObserver(
            "NSTextDidChangeNotification",
            "textDidChange:");
    }

    private void EmitRegisterNotificationObserver(
        string notificationName,
        string selectorName)
    {
        var skip = NewLabel("observer_skip");
        _code.LoadImmediate(0, -2); // RTLD_DEFAULT
        LoadCStringAddress(1, notificationName);
        CallImport("_dlsym");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, skip);
        _code.Ldr(9, 0, 0);
        _code.Push(9);
        EmitObjcClass(0, "NSNotificationCenter");
        EmitMsgSend0(0, "defaultCenter");
        _code.Push(0);
        LoadSlot(9, _windowDelegateSlot);
        _code.Push(9);
        EmitSelector(3, selectorName);
        _code.Push(3);
        EmitSelector(1, "addObserver:selector:name:object:");
        _code.Pop(3);
        _code.Pop(2);
        _code.Pop(0);
        _code.Pop(4);
        _code.LoadImmediate(5, 0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        _code.MarkLabel(skip);
    }

    private static int WindowStyleMask(YyFormModel form)
    {
        var mask = 1 | 2;
        if (form.MinimizeBox)
        {
            mask |= 4;
        }

        if (string.Equals(form.BorderStyle, "可调边框", StringComparison.Ordinal))
        {
            mask |= 8;
        }

        return mask;
    }

    private void EmitShowWindow(MacUiForm form)
    {
        LoadSlot(0, form.WindowSlot);
        EmitMsgSend1Imm(0, 0, "makeKeyAndOrderFront:");
        LoadSlot(0, _appSlot);
        EmitMsgSend1Imm(0, 1, "activateIgnoringOtherApps:");
    }

    private bool TryEmitControlLookup(CallExpression call, NativeContext context)
    {
        if (call.Arguments is not [LiteralExpression { Value: string id }])
        {
            return false;
        }

        if (_uiControlsById.TryGetValue(id, out var control))
        {
            LoadSlot(0, control.Slot);
            context.LastKind = NativeValueKind.Integer;
            return true;
        }

        if (_uiFormsById.TryGetValue(id, out var form))
        {
            LoadSlot(0, form.WindowSlot);
            context.LastKind = NativeValueKind.Integer;
            return true;
        }

        Fail(call, $"没有找到界面标识“{id}”");
        return true;
    }

    private bool TryEmitWindowCommand(CallExpression call, NativeContext context)
    {
        if (call.Callee is not IdentifierExpression identifier ||
            !NativeBuiltinCatalog.IsWindowCommand(identifier.Name))
        {
            return false;
        }

        if (call.Arguments is not [LiteralExpression { Value: string formId }, ..])
        {
            Fail(call, $"{identifier.Name} 的第一个参数需要静态窗体 @id/... 标识");
            return true;
        }

        var form = FindForm(call, formId);
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

        var owner = FindForm(call, ownerFormId);
        if (owner is null)
        {
            return true;
        }

        if (ReferenceEquals(form, owner))
        {
            Fail(call, "窗口不能以自己作为拥有者打开");
            return true;
        }

        EmitOpenWindow(form, identifier.Name == "打开窗口", context);
        return true;
    }

    private MacUiForm? FindForm(CallExpression call, string formId)
    {
        if (_uiFormsById.TryGetValue(formId, out var form))
        {
            return form;
        }

        Fail(call, $"没有找到窗体标识“{formId}”");
        return null;
    }

    private void EmitOpenWindow(MacUiForm form, bool runModal, NativeContext context)
    {
        _code.LoadImmediate(0, 0);
        StoreSlot(0, form.AcceptedSlot);
        if (form.OpenEvent is not null &&
            _functionLabels.TryGetValue(form.OpenEvent, out var openHandler))
        {
            _code.Bl(openHandler);
        }

        EmitShowWindow(form);
        if (runModal)
        {
            _code.LoadImmediate(0, 1);
            StoreSlot(0, form.ModalSlot);
            LoadSlot(0, _appSlot);
            LoadSlot(2, form.WindowSlot);
            EmitMsgSend1(0, 2, "runModalForWindow:");
            _code.LoadImmediate(0, 0);
            StoreSlot(0, form.ModalSlot);
            LoadSlot(0, form.AcceptedSlot);
        }
        else
        {
            _code.LoadImmediate(0, 1);
        }

        context.LastKind = NativeValueKind.Boolean;
    }

    private void EmitCloseWindow(
        CallExpression call,
        MacUiForm form,
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
        StoreSlot(0, form.AcceptedSlot);
        if (form.CloseEvent is not null &&
            _functionLabels.TryGetValue(form.CloseEvent, out var closeHandler))
        {
            _code.Bl(closeHandler);
        }

        var noModal = NewLabel("close_window_no_modal");
        LoadSlot(9, form.ModalSlot);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, noModal);
        LoadSlot(0, _appSlot);
        LoadSlot(2, form.AcceptedSlot);
        EmitMsgSend1(0, 2, "stopModalWithCode:");
        _code.LoadImmediate(0, 0);
        StoreSlot(0, form.ModalSlot);
        _code.MarkLabel(noModal);
        LoadSlot(0, form.WindowSlot);
        EmitMsgSend1Imm(0, 0, "orderOut:");
        _code.LoadImmediate(0, 1);
        context.LastKind = NativeValueKind.Boolean;
    }
}
