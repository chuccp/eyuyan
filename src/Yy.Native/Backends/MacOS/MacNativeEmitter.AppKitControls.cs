using Yy.Compiler.Forms;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private void EmitCreateControl(MacUiForm form, MacUiControl control)
    {
        var y = form.Height - (control.Y + control.Height);
        if (control.Type == YyControlTypes.ListBox)
        {
            EmitCreateListBox(form, control, y);
            return;
        }

        if (control.Type == YyControlTypes.TextBox && control.MultiLine)
        {
            EmitCreateMultilineTextBox(form, control, y);
            return;
        }

        var className = control.Type == YyControlTypes.TextBox &&
                        !string.IsNullOrEmpty(control.PasswordChar)
            ? "NSSecureTextField"
            : ControlClass(control.Type);
        var pullsDown = control.Type == YyControlTypes.ComboBox;
        EmitCreateControlInstance(
            className,
            pullsDown,
            control.X,
            y,
            control.Width,
            control.Height);
        StoreSlot(0, control.Slot);

        switch (control.Type)
        {
            case YyControlTypes.Label:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setStringValue:");
                EmitMsgSend1ImmSlot(control.Slot, 0, "setBezeled:");
                EmitMsgSend1ImmSlot(control.Slot, 0, "setEditable:");
                EmitMsgSend1ImmSlot(control.Slot, 0, "setDrawsBackground:");
                EmitMsgSend1ImmSlot(control.Slot, 0, "setSelectable:");
                break;
            case YyControlTypes.TextBox:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setStringValue:");
                EmitMsgSend1ImmSlot(control.Slot, 1, "setBezeled:");
                EmitMsgSend1ImmSlot(control.Slot, control.ReadOnly ? 0 : 1, "setEditable:");
                EmitMsgSend1ImmSlot(control.Slot, 1, "setSelectable:");
                EmitMsgSend1ImmSlot(control.Slot, 1, "setDrawsBackground:");
                break;
            case YyControlTypes.Button:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setTitle:");
                EmitMsgSend1ImmSlot(control.Slot, 1, "setBezelStyle:");
                break;
            case YyControlTypes.CheckBox:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setTitle:");
                EmitMsgSend1ImmSlot(control.Slot, 3, "setButtonType:");
                EmitMsgSend1ImmSlot(control.Slot, control.IsChecked ? 1 : 0, "setState:");
                break;
            case YyControlTypes.RadioButton:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setTitle:");
                EmitMsgSend1ImmSlot(control.Slot, 4, "setButtonType:");
                EmitMsgSend1ImmSlot(control.Slot, control.IsChecked ? 1 : 0, "setState:");
                break;
            case YyControlTypes.ComboBox:
                foreach (var itemLabel in control.ItemLabels)
                {
                    EmitMsgSendTextConstant(
                        control.Slot,
                        itemLabel,
                        "addItemWithTitle:");
                }

                if (control.ItemLabels.Count > 0)
                {
                    EmitMsgSend1ImmSlot(control.Slot, 0, "selectItemAtIndex:");
                }

                break;
            case YyControlTypes.GroupBox:
                EmitSetTextOnSlot(
                    control.Slot,
                    () => _code.LoadAddress(0, control.TextLabel),
                    "setTitle:");
                break;
            case YyControlTypes.PictureBox:
                EmitMsgSend1ImmSlot(control.Slot, 3, "setImageScaling:");
                EmitMsgSend1ImmSlot(control.Slot, 0, "setEditable:");
                if (!string.IsNullOrWhiteSpace(control.PicturePath))
                {
                    EmitSetImageFromLabel(control.Slot, control.PictureLabel);
                }

                break;
        }

        if (control.Type != YyControlTypes.GroupBox)
        {
            EmitSetFont(control.Slot, control.FontSize);
        }

        if (control.ActionEvent is not null &&
            control.Type is not (YyControlTypes.GroupBox or YyControlTypes.ListBox))
        {
            LoadSlot(0, control.Slot);
            LoadSlot(2, _actionTargetSlot);
            EmitMsgSend1(0, 2, "setTarget:");
            EmitSetAction(control.Slot, "handleAction:");
        }

        if (control.Tag != 0)
        {
            EmitMsgSend1ImmSlot(control.Slot, control.Tag, "setTag:");
        }

        if (!control.IsEnabled && control.Type != YyControlTypes.GroupBox)
        {
            EmitMsgSend1ImmSlot(control.Slot, 0, "setEnabled:");
        }

        if (!control.IsVisible)
        {
            EmitMsgSend1ImmSlot(control.Slot, 1, "setHidden:");
        }

        EmitApplyControlColors(control);
        EmitAddSubview(form, control);
    }

    private static string ControlClass(string type) => type switch
    {
        YyControlTypes.Label or YyControlTypes.TextBox => "NSTextField",
        YyControlTypes.Button or YyControlTypes.CheckBox or
            YyControlTypes.RadioButton => "NSButton",
        YyControlTypes.ComboBox => "NSPopUpButton",
        YyControlTypes.PictureBox => "NSImageView",
        YyControlTypes.GroupBox => "NSBox",
        YyControlTypes.ListBox => "NSScrollView",
        _ => "NSTextField"
    };

    private void EmitCreateControlInstance(
        string className,
        bool pullsDown,
        int x,
        int y,
        int width,
        int height)
    {
        EmitObjcClass(0, className);
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(
            1,
            pullsDown ? "initWithFrame:pullsDown:" : "initWithFrame:");
        _code.Pop(0);
        if (pullsDown)
        {
            _code.LoadImmediate(2, 0);
        }

        EmitRect(x, y, width, height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitSetTextOnSlot(
        string slot,
        Action emitText,
        string selector)
    {
        LoadSlot(0, slot);
        _code.Push(0);
        emitText();
        _code.Bl(TextToNsStringLabel);
        _code.Push(0);
        EmitSelector(1, selector);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitMsgSendTextConstant(
        string slot,
        string textLabel,
        string selector)
    {
        LoadSlot(0, slot);
        _code.Push(0);
        _code.LoadAddress(0, textLabel);
        _code.Bl(TextToNsStringLabel);
        _code.Push(0);
        EmitSelector(1, selector);
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitSetAction(string slot, string actionSelector)
    {
        LoadSlot(0, slot);
        _code.Push(0);
        EmitSelector(2, actionSelector);
        _code.Push(2);
        EmitSelector(1, "setAction:");
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitSetFont(string slot, int size)
    {
        EmitObjcClass(0, "NSFont");
        _code.Push(0);
        EmitSelector(1, "systemFontOfSize:");
        _code.Pop(0);
        _code.LoadImmediate(9, size);
        _code.ScvtfDouble(0, 9);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        _code.Push(0);
        LoadSlot(0, slot);
        _code.Pop(2);
        EmitMsgSend1(0, 2, "setFont:");
    }

    private void EmitAddSubview(MacUiForm form, MacUiControl control)
    {
        LoadSlot(0, form.WindowSlot);
        EmitMsgSend0(0, "contentView");
        LoadSlot(2, control.Slot);
        EmitMsgSend1(0, 2, "addSubview:");
    }

    private void EmitSetImageFromLabel(string slot, string pathLabel)
    {
        _code.LoadAddress(0, pathLabel);
        _code.Bl(TextToNsStringLabel);
        EmitImageFromPath();
        _code.Push(0);
        LoadSlot(0, slot);
        _code.Pop(2);
        EmitMsgSend1(0, 2, "setImage:");
    }

    private void EmitImageFromPath()
    {
        // NSString 路径在 x0；返回 [[NSImage alloc] initWithContentsOfFile:]
        _code.Push(0);
        EmitObjcClass(0, "NSImage");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithContentsOfFile:");
        _code.Pop(0);
        _code.Pop(2);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }
    private void EmitCreateMultilineTextBox(
        MacUiForm form,
        MacUiControl control,
        int y)
    {
        EmitObjcClass(0, "NSScrollView");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithFrame:");
        _code.Pop(0);
        EmitRect(control.X, y, control.Width, control.Height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        StoreSlot(0, control.Slot);

        EmitObjcClass(0, "NSTextView");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithFrame:");
        _code.Pop(0);
        EmitRect(0, 0, control.Width, control.Height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        StoreSlot(0, control.AuxSlot);

        LoadSlot(0, control.Slot);
        LoadSlot(2, control.AuxSlot);
        EmitMsgSend1(0, 2, "setDocumentView:");
        LoadSlot(0, control.Slot);
        EmitMsgSend1Imm(0, 1, "setHasVerticalScroller:");
        LoadSlot(0, control.Slot);
        EmitMsgSend1Imm(0, 2, "setBorderType:");

        EmitSetTextOnSlot(
            control.AuxSlot,
            () => _code.LoadAddress(0, control.TextLabel),
            "setString:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, control.ReadOnly ? 0 : 1, "setEditable:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 1, "setSelectable:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 1, "setDrawsBackground:");
        EmitSetFont(control.AuxSlot, control.FontSize);
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 0, "setHorizontallyResizable:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 1, "setVerticallyResizable:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend0(0, "textContainer");
        EmitMsgSend1Imm(0, 1, "setWidthTracksTextView:");

        EmitApplyControlColors(control);
        if (!control.IsEnabled)
        {
            EmitMsgSend1ImmSlot(control.AuxSlot, 0, "setEditable:");
            EmitMsgSend1ImmSlot(control.AuxSlot, 0, "setSelectable:");
        }

        if (!control.IsVisible)
        {
            EmitMsgSend1ImmSlot(control.Slot, 1, "setHidden:");
        }

        EmitAddSubview(form, control);
    }

    private void EmitCreateListBox(
        MacUiForm form,
        MacUiControl control,
        int y)
    {
        EmitObjcClass(0, "NSScrollView");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithFrame:");
        _code.Pop(0);
        EmitRect(control.X, y, control.Width, control.Height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        StoreSlot(0, control.Slot);

        EmitObjcClass(0, "NSTableView");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        EmitSelector(1, "initWithFrame:");
        _code.Pop(0);
        EmitRect(0, 0, control.Width, control.Height);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        StoreSlot(0, control.AuxSlot);

        EmitObjcClass(0, "NSTableColumn");
        EmitMsgSend0(0, "alloc");
        _code.Push(0);
        _code.LoadAddress(0, _listColumnLabel);
        _code.Bl(TextToNsStringLabel);
        _code.Push(0);
        EmitSelector(1, "initWithIdentifier:");
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
        _code.Push(0);
        LoadSlot(0, control.AuxSlot);
        _code.Pop(2);
        EmitMsgSend1(0, 2, "addTableColumn:");

        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 0, "setHeaderView:");
        LoadSlot(0, control.AuxSlot);
        LoadSlot(2, _listDataSourceSlot);
        EmitMsgSend1(0, 2, "setDataSource:");
        LoadSlot(0, control.AuxSlot);
        EmitMsgSend1Imm(0, 0, "setAllowsMultipleSelection:");

        LoadSlot(0, control.Slot);
        LoadSlot(2, control.AuxSlot);
        EmitMsgSend1(0, 2, "setDocumentView:");
        LoadSlot(0, control.Slot);
        EmitMsgSend1Imm(0, 1, "setHasVerticalScroller:");

        EmitObjcClass(0, "NSMutableArray");
        EmitMsgSend0(0, "array");
        StoreSlot(0, control.ItemArraySlot);
        foreach (var itemLabel in control.ItemLabels)
        {
            EmitAddArrayItem(control.ItemArraySlot, itemLabel);
        }

        LoadSlot(0, control.AuxSlot);
        _code.Push(0);
        LoadSlot(9, control.ItemArraySlot);
        _code.Push(9);
        EmitSelector(1, "items");
        _code.Pop(2);
        _code.Pop(0);
        _code.LoadImmediate(3, 1);
        CallImport("_objc_setAssociatedObject", MacLibraries.ObjectiveC);

        EmitSetFont(control.AuxSlot, control.FontSize);
        if (control.Tag != 0)
        {
            EmitMsgSend1ImmSlot(control.AuxSlot, control.Tag, "setTag:");
        }

        if (!control.IsEnabled)
        {
            EmitMsgSend1ImmSlot(control.AuxSlot, 0, "setEnabled:");
        }

        if (!control.IsVisible)
        {
            EmitMsgSend1ImmSlot(control.Slot, 1, "setHidden:");
        }

        EmitApplyControlColors(control);
        EmitAddSubview(form, control);
    }

    private void EmitAddArrayItem(string arraySlot, string textLabel)
    {
        LoadSlot(0, arraySlot);
        _code.Push(0);
        _code.LoadAddress(0, textLabel);
        _code.Bl(TextToNsStringLabel);
        _code.Push(0);
        EmitSelector(1, "addObject:");
        _code.Pop(2);
        _code.Pop(0);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }
}
