using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>窗体设计器指针、键盘和工具箱拖放交互。</summary>
public partial class FormDesignerView
{
    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null ||
            !e.GetCurrentPoint(DesignerCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        DesignerCanvas.Focus();
        var point = e.GetPosition(DesignerCanvas);
        var handle = _selection.Count == 1
            ? DesignerSelectionOverlay.HitTestHandle(_selectedControl, point)
            : null;
        if (handle is not null)
        {
            StartResize(handle.Value, point, e);
            e.Handled = true;
            return;
        }

        var control = HitTestControl(point);
        if (control is null)
        {
            SelectForm();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 &&
            FormDesignerEvents.TryGetDefaultKind(control.Type, out var kind))
        {
            SelectControl(control);
            RequestEvent(control, kind);
            e.Handled = true;
            return;
        }

        var additive = IsCommandModifier(e.KeyModifiers);
        if (additive)
        {
            SelectControl(control, additive: true);
            e.Handled = true;
            return;
        }

        if (!_selection.Contains(control))
        {
            SelectControl(control);
        }
        else if (_selection.Count > 1)
        {
            _selectedControl = control;
            PopulatePropertyPanel();
            UpdateSelectionVisuals();
        }

        StartMove(point, e);
        e.Handled = true;
    }

    private void StartMove(Point point, PointerPressedEventArgs e)
    {
        if (_editor is null || _selectedControl is null)
        {
            return;
        }

        _dragMode = DragMode.Move;
        _dragSnapshot = _editor.BeginEdit();
        _dragStart = point;
        _dragOriginalBounds = BoundsOf(_selectedControl);
        _dragBounds.Clear();
        foreach (var control in _selection)
        {
            _dragBounds[control] = BoundsOf(control);
        }

        _dragChanged = false;
        e.Pointer.Capture(DesignerCanvas);
    }

    private void StartResize(
        ResizeDirection direction,
        Point point,
        PointerPressedEventArgs e)
    {
        if (_editor is null || _selectedControl is null)
        {
            return;
        }

        _dragMode = DragMode.Resize;
        _resizeDirection = direction;
        _dragSnapshot = _editor.BeginEdit();
        _dragStart = point;
        _dragOriginalBounds = BoundsOf(_selectedControl);
        _dragChanged = false;
        e.Pointer.Capture(DesignerCanvas);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None ||
            _editor is null ||
            _selectedControl is null ||
            !e.GetCurrentPoint(DesignerCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(DesignerCanvas);
        var deltaX = point.X - _dragStart.X;
        var deltaY = point.Y - _dragStart.Y;

        if (_dragMode == DragMode.Move)
        {
            if (_dragBounds.Count == 0)
            {
                return;
            }

            var roundedDeltaX = _editor.Snap(_dragOriginalBounds.X + deltaX) -
                                _dragOriginalBounds.X;
            var roundedDeltaY = _editor.Snap(_dragOriginalBounds.Y + deltaY) -
                                _dragOriginalBounds.Y;
            var groupLeft = _dragBounds.Values.Min(bounds => bounds.Left);
            var groupTop = _dragBounds.Values.Min(bounds => bounds.Top);
            var groupRight = _dragBounds.Values.Max(bounds => bounds.Right);
            var groupBottom = _dragBounds.Values.Max(bounds => bounds.Bottom);
            var appliedDeltaX = Math.Clamp(
                roundedDeltaX,
                -groupLeft,
                _editor.Form.Width - groupRight);
            var appliedDeltaY = Math.Clamp(
                roundedDeltaY,
                -groupTop,
                _editor.Form.Height - groupBottom);

            foreach (var pair in _dragBounds)
            {
                pair.Key.X = pair.Value.X + appliedDeltaX;
                pair.Key.Y = pair.Value.Y + appliedDeltaY;
                UpdateVisualBounds(pair.Key);
            }
        }
        else
        {
            _editor.ResizeControl(
                _selectedControl,
                _resizeDirection,
                _dragOriginalBounds,
                deltaX,
                deltaY);
            UpdateVisualBounds(_selectedControl);
        }

        UpdateSelectionVisuals();
        UpdatePropertyEditorsFromModel();
        _dragChanged = true;
        e.Handled = true;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        e.Pointer.Capture(null);
        _dragMode = DragMode.None;
        if (_dragChanged && _dragSnapshot is not null && _editor is not null)
        {
            _editor.CommitEdit(_dragSnapshot);
            NotifyFormChanged();
            SetStatus("窗体布局已更新");
        }

        _dragChanged = false;
        _dragSnapshot = null;
        _dragBounds.Clear();
        e.Handled = true;
    }

    private void OnCanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        var command = IsCommandModifier(e.KeyModifiers);
        if (command && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SelectForm();
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Redo();
            }
            else
            {
                Undo();
            }

            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (_selection.Count == 0)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.C)
        {
            CopySelection(cut: false);
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.X)
        {
            CopySelection(cut: true);
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.V)
        {
            PasteSelection();
            e.Handled = true;
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var deltaX = 0d;
        var deltaY = 0d;
        switch (e.Key)
        {
            case Key.Left:
                deltaX = -step;
                break;
            case Key.Right:
                deltaX = step;
                break;
            case Key.Up:
                deltaY = -step;
                break;
            case Key.Down:
                deltaY = step;
                break;
            default:
                return;
        }

        var before = _editor.BeginEdit();
        _editor.MoveControls(_selection, deltaX, deltaY);
        _editor.CommitEdit(before);
        foreach (var control in _selection)
        {
            UpdateVisualBounds(control);
        }

        UpdateSelectionVisuals();
        UpdatePropertyEditorsFromModel();
        NotifyFormChanged();
        SetStatus("窗体布局已更新");
        e.Handled = true;
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        var type = GetDraggedControlType(e);
        if (type is not null &&
            YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        var type = GetDraggedControlType(e);
        if (type is null ||
            !YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            return;
        }

        AddControl(type, e.GetPosition(DesignerCanvas));
        e.Handled = true;
    }

    /// <summary>工具箱拖放使用纯文本控件类型(如“按钮”),与 WPF 设计器数据格式一致。</summary>
    private static string? GetDraggedControlType(DragEventArgs e) =>
        e.DataTransfer is { } dataTransfer
            ? dataTransfer.TryGetText()
            : null;
}
