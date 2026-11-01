using Yy.Compiler.Forms;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private sealed record MacColor(double Red, double Green, double Blue, double Alpha);

    private static MacColor? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (string.Equals(text, "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            return new MacColor(0, 0, 0, 0);
        }

        if (!text.StartsWith('#'))
        {
            return null;
        }

        var hex = text[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(ch => new string(ch, 2)));
        }

        if (hex.Length is not (6 or 8))
        {
            return null;
        }

        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value32))
        {
            return null;
        }

        if (hex.Length == 6)
        {
            return new MacColor(
                ((value32 >> 16) & 0xFF) / 255.0,
                ((value32 >> 8) & 0xFF) / 255.0,
                (value32 & 0xFF) / 255.0,
                1.0);
        }

        return new MacColor(
            ((value32 >> 16) & 0xFF) / 255.0,
            ((value32 >> 8) & 0xFF) / 255.0,
            (value32 & 0xFF) / 255.0,
            ((value32 >> 24) & 0xFF) / 255.0);
    }

    private void EmitDouble(int d, double value)
    {
        _code.LoadImmediate(9, BitConverter.DoubleToInt64Bits(value));
        _code.FmovDoubleFromX(d, 9);
    }

    private void EmitColor(MacColor color)
    {
        EmitObjcClass(0, "NSColor");
        _code.Push(0);
        EmitSelector(1, "colorWithRed:green:blue:alpha:");
        _code.Pop(0);
        EmitDouble(0, color.Red);
        EmitDouble(1, color.Green);
        EmitDouble(2, color.Blue);
        EmitDouble(3, color.Alpha);
        CallImport("_objc_msgSend", MacLibraries.ObjectiveC);
    }

    private void EmitApplyColor(string slot, string selector, MacColor color)
    {
        EmitColor(color);
        _code.Push(0);
        LoadSlot(0, slot);
        _code.Pop(2);
        EmitMsgSend1(0, 2, selector);
    }

    private void EmitApplyControlColors(MacUiControl control)
    {
        var foreground = ParseColor(control.Foreground);
        var background = ParseColor(control.Background);
        switch (control.Type)
        {
            case YyControlTypes.Label:
                if (foreground is not null)
                {
                    EmitApplyColor(control.Slot, "setTextColor:", foreground);
                }

                if (background is not null)
                {
                    EmitApplyColor(control.Slot, "setBackgroundColor:", background);
                    EmitMsgSend1ImmSlot(control.Slot, 1, "setDrawsBackground:");
                }

                break;
            case YyControlTypes.TextBox when control.MultiLine:
                if (background is not null)
                {
                    EmitApplyColor(control.AuxSlot, "setBackgroundColor:", background);
                    EmitApplyColor(control.Slot, "setBackgroundColor:", background);
                }

                break;
            case YyControlTypes.TextBox:
                if (foreground is not null)
                {
                    EmitApplyColor(control.Slot, "setTextColor:", foreground);
                }

                if (background is not null)
                {
                    EmitApplyColor(control.Slot, "setBackgroundColor:", background);
                    EmitMsgSend1ImmSlot(control.Slot, 1, "setDrawsBackground:");
                }

                break;
            case YyControlTypes.Button or YyControlTypes.CheckBox or
                YyControlTypes.RadioButton or YyControlTypes.ComboBox:
                if (foreground is not null)
                {
                    EmitApplyColor(control.Slot, "setContentTintColor:", foreground);
                }

                if (background is not null)
                {
                    EmitApplyColor(control.Slot, "setBezelColor:", background);
                }

                break;
            case YyControlTypes.GroupBox:
                if (background is not null)
                {
                    EmitMsgSend1ImmSlot(control.Slot, 4, "setBoxType:");
                    EmitApplyColor(control.Slot, "setFillColor:", background);
                }

                break;
            case YyControlTypes.ListBox:
                if (background is not null)
                {
                    EmitApplyColor(control.AuxSlot, "setBackgroundColor:", background);
                    EmitMsgSend1ImmSlot(control.Slot, 0, "setDrawsBackground:");
                }

                break;
        }
    }

    private void EmitApplyWindowColor(MacUiForm form)
    {
        var background = ParseColor(form.Background);
        if (background is not null)
        {
            EmitApplyColor(form.WindowSlot, "setBackgroundColor:", background);
        }
    }
}
