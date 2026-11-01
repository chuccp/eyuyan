using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>窗体设计器画布构建:网格层、控件视觉和选择装饰层。</summary>
public partial class FormDesignerView
{
    private void WireCanvas()
    {
        DesignerCanvas.PointerPressed += OnCanvasPointerPressed;
        DesignerCanvas.PointerMoved += OnCanvasPointerMoved;
        DesignerCanvas.PointerReleased += OnCanvasPointerReleased;
        DesignerCanvas.KeyDown += OnCanvasKeyDown;
        DragDrop.SetAllowDrop(DesignerCanvas, true);
        DragDrop.AddDragOverHandler(DesignerCanvas, OnCanvasDragOver);
        DragDrop.AddDropHandler(DesignerCanvas, OnCanvasDrop);
    }

    private void PopulateToolbox()
    {
        foreach (var type in YyControlTypes.All)
        {
            ToolboxCombo.Items.Add(new ComboBoxItem
            {
                Content = YyControlTypes.GetDisplayName(type),
                Tag = type
            });
        }

        ToolboxCombo.SelectedIndex = 0;
    }

    private void RebuildCanvas()
    {
        DesignerCanvas.Children.Clear();
        _visuals.Clear();

        var form = _editor?.Form;
        var width = form?.Width ?? 800;
        var height = form?.Height ?? 520;
        DesignerCanvas.Width = width;
        DesignerCanvas.Height = height;
        GridSurface.Width = width;
        GridSurface.Height = height;
        GridSurface.FormBackground = form is null
            ? Brushes.White
            : YyFormRenderer.Brush(form.Background);
        GridSurface.ShowGrid = true;
        GridSurface.GridSize = _editor?.GridSize ?? 8;
        GridSurface.InvalidateVisual();

        CanvasHost.Width = width;
        CanvasHost.Height = height;
        SelectionOverlay.Width = width;
        SelectionOverlay.Height = height;

        if (_editor is not null)
        {
            foreach (var control in _editor.Form.Controls)
            {
                AddControlVisual(control);
            }
        }

        UpdateSelectionVisuals();
    }

    private void AddControlVisual(YyControlModel control)
    {
        if (_editor is null)
        {
            return;
        }

        var element = YyFormRenderer.Create(control, _editor.ProjectDirectory);
        element.IsVisible = true;
        element.Opacity = control.IsVisible ? 1 : 0.35;
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, control.X);
        Canvas.SetTop(element, control.Y);
        element.ZIndex = _editor.Form.Controls.IndexOf(control);
        DesignerCanvas.Children.Add(element);
        _visuals[control] = element;
    }

    private void UpdateVisualBounds(YyControlModel control)
    {
        if (!_visuals.TryGetValue(control, out var element))
        {
            return;
        }

        Canvas.SetLeft(element, control.X);
        Canvas.SetTop(element, control.Y);
        element.Width = control.Width;
        element.Height = control.Height;
    }

    private void UpdateSelectionVisuals() =>
        SelectionOverlay.UpdateSelection(_selection.ToArray(), _selectedControl);

    private void ResetSelection()
    {
        _selection.Clear();
        _selectedControl = null;
    }

    private YyControlModel? HitTestControl(Point point)
    {
        if (_editor is null)
        {
            return null;
        }

        for (var index = _editor.Form.Controls.Count - 1; index >= 0; index--)
        {
            var control = _editor.Form.Controls[index];
            var bounds = new Rect(control.X, control.Y, control.Width, control.Height);
            if (bounds.Contains(point))
            {
                return control;
            }
        }

        return null;
    }

    private static Rect BoundsOf(YyControlModel control) =>
        new(control.X, control.Y, control.Width, control.Height);
}
