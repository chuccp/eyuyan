using Yy.Compiler.Forms;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private const string TextToNsStringLabel = "__yy_text_to_nsstring";
    private const string NsStringToTextLabel = "__yy_nsstring_to_text";
    private const string ActionImpLabel = "__yy_action_imp";
    private const string ControlTextChangedImpLabel = "__yy_control_text_changed";
    private const string ListRowsImpLabel = "__yy_list_rows";
    private const string ListValueImpLabel = "__yy_list_value";
    private const string ListSelectionImpLabel = "__yy_list_selection";
    private const string MultilineTextChangedImpLabel = "__yy_multiline_text_changed";
    private const string ListBoxSelectedTextLabel = "__yy_listbox_selected_text";
    private const string WindowWillCloseImpLabel = "__yy_window_will_close";
    private const string WindowShouldCloseImpLabel = "__yy_window_should_close";

    private readonly Dictionary<string, string> _cStringLabels =
        new(StringComparer.Ordinal);

    private string AddDataSlot(string name)
    {
        var label = "slot_" + name;
        _data.Align(8);
        _data.MarkLabel(label);
        _data.AddZeroes(8);
        return label;
    }

    private void LoadSlot(int register, string label)
    {
        _code.LoadAddress(9, label);
        _code.Ldr(register, 9, 0);
    }

    private void StoreSlot(int register, string label)
    {
        _code.LoadAddress(9, label);
        _code.Str(register, 9, 0);
    }

    private void LoadCStringAddress(int register, string value)
    {
        if (!_cStringLabels.TryGetValue(value, out var label))
        {
            label = _data.AddUtf8String(value);
            _cStringLabels[value] = label;
        }

        _code.LoadAddress(register, label);
        _code.AddImm(register, register, NativeRuntimeLayout.PayloadOffset);
    }

    private void EmitObjcClass(int register, string name)
    {
        LoadCStringAddress(0, name);
        CallImport("_objc_getClass", MacLibraries.ObjectiveC);
        _code.MovReg(register, 0);
    }

    private void EmitSelector(int register, string name)
    {
        LoadCStringAddress(0, name);
        CallImport("_sel_registerName", MacLibraries.ObjectiveC);
        _code.MovReg(register, 0);
    }

    private void EmitMsgSend0(int receiver, string selector)
    {
        _code.Push(receiver);
        EmitSelector(1, selector);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSend1(int receiver, int argument, string selector)
    {
        _code.Push(receiver);
        _code.Push(argument);
        EmitSelector(1, selector);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSend2(int receiver, int first, int second, string selector)
    {
        _code.Push(receiver);
        _code.Push(first);
        _code.Push(second);
        EmitSelector(1, selector);
        _code.Pop(3);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSend3(
        int receiver,
        int first,
        int second,
        int third,
        string selector)
    {
        _code.Push(receiver);
        _code.Push(first);
        _code.Push(second);
        _code.Push(third);
        EmitSelector(1, selector);
        _code.Pop(4);
        _code.Pop(3);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSend1ImmSlot(string slot, long value, string selector)
    {
        LoadSlot(0, slot);
        EmitMsgSend1Imm(0, value, selector);
    }

    private void EmitMsgSend1Imm(int receiver, long value, string selector)
    {
        _code.Push(receiver);
        _code.LoadImmediate(9, value);
        _code.Push(9);
        EmitSelector(1, selector);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSendRect0(
        int receiver,
        string selector,
        int x,
        int y,
        int width,
        int height)
    {
        _code.Push(receiver);
        EmitSelector(1, selector);
        _code.Pop(0);
        EmitRect(x, y, width, height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSendRect1Imm(
        int receiver,
        string selector,
        int x,
        int y,
        int width,
        int height,
        long value)
    {
        _code.Push(receiver);
        EmitSelector(1, selector);
        _code.Pop(0);
        _code.LoadImmediate(2, value);
        EmitRect(x, y, width, height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitRect(int x, int y, int width, int height)
    {
        _code.LoadImmediate(9, x);
        _code.ScvtfDouble(0, 9);
        _code.LoadImmediate(9, y);
        _code.ScvtfDouble(1, 9);
        _code.LoadImmediate(9, width);
        _code.ScvtfDouble(2, 9);
        _code.LoadImmediate(9, height);
        _code.ScvtfDouble(3, 9);
    }

    private void EmitAppKitRuntime()
    {
        EmitTextToNsString();
        EmitNsStringToText();
        EmitActionImp();
        EmitControlTextChangedImp();
        EmitListDataSourceImps();
        EmitListSelectionImp();
        EmitListBoxSelectedText();
        EmitMultilineTextChangedImp();
        EmitWindowDelegateImps();
    }

    private void EmitTextToNsString()
    {
        _code.MarkLabel(TextToNsStringLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.Ldr(0, 29, -8);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Push(0);
        EmitObjcClass(0, "NSString");
        _code.Pop(2);
        EmitMsgSend1(0, 2, "stringWithUTF8String:");
        _code.EndFrame();
    }

    private void EmitNsStringToText()
    {
        var empty = NewLabel("nsstring_empty");
        var done = NewLabel("nsstring_done");
        _code.MarkLabel(NsStringToTextLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, empty);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "UTF8String");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, empty);
        _code.Push(0);
        CallImport("_strlen");
        _code.MovReg(1, 0);
        _code.Pop(0);
        _code.Bl(TextNewLabel);
        _code.B(done);
        _code.MarkLabel(empty);
        _code.LoadAddress(0, _emptyLabel);
        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitActionImp()
    {
        var done = NewLabel("action_done");
        _code.MarkLabel(ActionImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "tag");
        foreach (var control in _uiControls.Where(item => item.ActionEvent is not null))
        {
            var next = NewLabel("action_next");
            _code.LoadImmediate(9, control.Tag);
            _code.Cmp(0, 9);
            _code.Bcond(Arm64Condition.Ne, next);
            if (_functionLabels.TryGetValue(control.ActionEvent!, out var handler))
            {
                _code.Bl(handler);
            }

            _code.B(done);
            _code.MarkLabel(next);
        }

        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitControlTextChangedImp()
    {
        var done = NewLabel("text_changed_done");
        _code.MarkLabel(ControlTextChangedImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "object");
        _code.Str(0, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "tag");
        foreach (var control in _uiControls.Where(item => item.TextChangedEvent is not null))
        {
            var next = NewLabel("text_changed_next");
            _code.LoadImmediate(9, control.Tag);
            _code.Cmp(0, 9);
            _code.Bcond(Arm64Condition.Ne, next);
            if (_functionLabels.TryGetValue(control.TextChangedEvent!, out var handler))
            {
                _code.Bl(handler);
            }

            _code.B(done);
            _code.MarkLabel(next);
        }

        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitGetAssociatedArrayFromSlot(int slot, int destination)
    {
        _code.Ldr(0, 29, slot);
        _code.Push(0);
        EmitSelector(1, "items");
        _code.Pop(0);
        CallImport("_objc_getAssociatedObject", MacLibraries.ObjectiveC);
        _code.MovReg(destination, 0);
    }

    private void EmitListDataSourceImps()
    {
        var zeroRows = NewLabel("list_zero_rows");
        var rowsDone = NewLabel("list_rows_done");
        _code.MarkLabel(ListRowsImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        EmitGetAssociatedArrayFromSlot(-8, 9);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, zeroRows);
        EmitMsgSend0(9, "count");
        _code.B(rowsDone);
        _code.MarkLabel(zeroRows);
        _code.LoadImmediate(0, 0);
        _code.MarkLabel(rowsDone);
        _code.EndFrame();

        var emptyValue = NewLabel("list_empty_value");
        var valueDone = NewLabel("list_value_done");
        _code.MarkLabel(ListValueImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Str(4, 29, -16);
        EmitGetAssociatedArrayFromSlot(-8, 9);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, emptyValue);
        _code.Ldr(2, 29, -16);
        EmitMsgSend1(9, 2, "objectAtIndex:");
        _code.B(valueDone);
        _code.MarkLabel(emptyValue);
        _code.LoadImmediate(0, 0);
        _code.MarkLabel(valueDone);
        _code.EndFrame();
    }

    private void EmitListSelectionImp()
    {
        var done = NewLabel("list_selection_done");
        _code.MarkLabel(ListSelectionImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "object");
        _code.Str(0, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "tag");
        foreach (var control in _uiControls.Where(item =>
                     item.Type == YyControlTypes.ListBox &&
                     item.ActionEvent is not null))
        {
            var next = NewLabel("list_selection_next");
            _code.LoadImmediate(9, control.Tag);
            _code.Cmp(0, 9);
            _code.Bcond(Arm64Condition.Ne, next);
            if (_functionLabels.TryGetValue(control.ActionEvent!, out var handler))
            {
                _code.Bl(handler);
            }

            _code.B(done);
            _code.MarkLabel(next);
        }

        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitListBoxSelectedText()
    {
        var empty = NewLabel("listbox_empty");
        var done = NewLabel("listbox_done");
        _code.MarkLabel(ListBoxSelectedTextLabel);
        _code.BeginFrame(64);
        _code.Str(0, 29, -8);
        EmitGetAssociatedArrayFromSlot(-8, 9);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, empty);
        _code.Str(9, 29, -16);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "selectedRow");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Lt, empty);
        _code.MovReg(2, 0);
        _code.Ldr(0, 29, -16);
        EmitMsgSend1(0, 2, "objectAtIndex:");
        _code.Bl(NsStringToTextLabel);
        _code.B(done);
        _code.MarkLabel(empty);
        _code.LoadAddress(0, _emptyLabel);
        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitMultilineTextChangedImp()
    {
        var done = NewLabel("multiline_text_done");
        _code.MarkLabel(MultilineTextChangedImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "object");
        _code.Str(0, 29, -8);
        foreach (var control in _uiControls.Where(item =>
                     item.MultiLine &&
                     item.TextChangedEvent is not null))
        {
            var next = NewLabel("multiline_text_next");
            _code.Ldr(0, 29, -8);
            LoadSlot(9, control.AuxSlot);
            _code.Cmp(0, 9);
            _code.Bcond(Arm64Condition.Ne, next);
            if (_functionLabels.TryGetValue(control.TextChangedEvent!, out var handler))
            {
                _code.Bl(handler);
            }

            _code.B(done);
            _code.MarkLabel(next);
        }

        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitWindowDelegateImps()
    {
        var done = NewLabel("window_close_done");
        _code.MarkLabel(WindowWillCloseImpLabel);
        _code.BeginFrame(64);
        _code.Str(2, 29, -8);
        _code.Ldr(0, 29, -8);
        EmitMsgSend0(0, "object");
        _code.Str(0, 29, -8);
        foreach (var form in _uiForms)
        {
            var next = NewLabel("window_close_next");
            var noModal = NewLabel("window_close_no_modal");
            _code.Ldr(0, 29, -8);
            LoadSlot(9, form.WindowSlot);
            _code.Cmp(0, 9);
            _code.Bcond(Arm64Condition.Ne, next);
            _code.LoadImmediate(0, 0);
            StoreSlot(0, form.AcceptedSlot);
            if (form.CloseEvent is not null &&
                _functionLabels.TryGetValue(form.CloseEvent, out var closeHandler))
            {
                _code.Bl(closeHandler);
            }

            LoadSlot(9, form.ModalSlot);
            _code.CmpImm(9, 0);
            _code.Bcond(Arm64Condition.Eq, noModal);
            LoadSlot(0, _appSlot);
            EmitMsgSend1Imm(0, 0, "stopModalWithCode:");
            _code.LoadImmediate(0, 0);
            StoreSlot(0, form.ModalSlot);
            _code.MarkLabel(noModal);
            if (ReferenceEquals(form, _startupUiForm))
            {
                LoadSlot(0, _appSlot);
                EmitMsgSend1Imm(0, 0, "terminate:");
            }

            _code.B(done);
            _code.MarkLabel(next);
        }

        _code.MarkLabel(done);
        _code.EndFrame();

        _code.MarkLabel(WindowShouldCloseImpLabel);
        _code.LoadImmediate(0, 1);
        _code.Ret();
    }
}
