using Avalonia.Interactivity;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

public enum FormAlignment
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom
}

public sealed partial class FormDesignerView
{
    public void AlignSelection(FormAlignment alignment)
    {
        if (_editor is null || _selection.Count == 0)
        {
            return;
        }

        var before = _editor.BeginEdit();
        var form = _editor.Form;
        var left = _selection.Min(control => control.X);
        var right = _selection.Max(control => control.X + control.Width);
        var top = _selection.Min(control => control.Y);
        var bottom = _selection.Max(control => control.Y + control.Height);
        var single = _selection.Count == 1;
        foreach (var control in _selection)
        {
            control.X = alignment switch
            {
                FormAlignment.Left => single ? Math.Clamp(control.X, 0, Math.Max(0, form.Width - control.Width)) : left,
                FormAlignment.Center => single ? (form.Width - control.Width) / 2 : (left + right - control.Width) / 2,
                FormAlignment.Right => single ? Math.Max(0, form.Width - control.Width) : right - control.Width,
                _ => control.X
            };
            control.Y = alignment switch
            {
                FormAlignment.Top => single ? Math.Clamp(control.Y, 0, Math.Max(0, form.Height - control.Height)) : top,
                FormAlignment.Middle => single ? (form.Height - control.Height) / 2 : (top + bottom - control.Height) / 2,
                FormAlignment.Bottom => single ? Math.Max(0, form.Height - control.Height) : bottom - control.Height,
                _ => control.Y
            };
        }

        _editor.CommitEdit(before);
        RebuildCanvas();
        SetSelection(_selection);
        NotifyFormChanged();
        SetStatus($"已对齐 {_selection.Count} 个控件。");
    }

    public void DistributeSelection(bool horizontal)
    {
        if (_editor is null || _selection.Count < 3)
        {
            SetStatus("分布至少需要选择 3 个控件。");
            return;
        }

        var before = _editor.BeginEdit();
        var ordered = _selection
            .OrderBy(control => horizontal ? control.X : control.Y)
            .ThenBy(control => horizontal ? control.Y : control.X)
            .ToArray();
        if (horizontal)
        {
            var total = ordered.Sum(control => control.Width);
            var left = ordered[0].X;
            var right = ordered[^1].X + ordered[^1].Width;
            var gap = (right - left - total) / (ordered.Length - 1);
            var cursor = left;
            foreach (var control in ordered)
            {
                control.X = cursor;
                cursor += control.Width + gap;
            }
        }
        else
        {
            var total = ordered.Sum(control => control.Height);
            var top = ordered[0].Y;
            var bottom = ordered[^1].Y + ordered[^1].Height;
            var gap = (bottom - top - total) / (ordered.Length - 1);
            var cursor = top;
            foreach (var control in ordered)
            {
                control.Y = cursor;
                cursor += control.Height + gap;
            }
        }

        _editor.CommitEdit(before);
        RebuildCanvas();
        SetSelection(ordered);
        NotifyFormChanged();
        SetStatus($"已{(horizontal ? "水平" : "垂直")}分布 {ordered.Length} 个控件。");
    }

    private void OnAlignLeftClick(object? sender, RoutedEventArgs e) =>
        AlignSelection(FormAlignment.Left);

    private void OnAlignTopClick(object? sender, RoutedEventArgs e) =>
        AlignSelection(FormAlignment.Top);

    private void OnDistributeHorizontalClick(object? sender, RoutedEventArgs e) =>
        DistributeSelection(horizontal: true);

    private void OnDistributeVerticalClick(object? sender, RoutedEventArgs e) =>
        DistributeSelection(horizontal: false);
}
