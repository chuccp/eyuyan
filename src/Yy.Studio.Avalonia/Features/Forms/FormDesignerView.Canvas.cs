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
        // 组件箱瓦片:双击放入窗体(对齐主分支“基本组件”面板)。
        foreach (var (glyph, display, type, supported) in ToolboxCatalog)
        {
            var tile = new Button
            {
                Width = 58,
                Height = 48,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                IsEnabled = supported,
                Tag = type
            };
            var stack = new StackPanel
            {
                Spacing = 1,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = 15,
                Foreground = supported
                    ? Brush.Parse("#3574F0")
                    : Brush.Parse("#808080"),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = display,
                FontSize = 10,
                Foreground = supported
                    ? Brush.Parse("#1F2328")
                    : Brush.Parse("#808080"),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 56
            });
            tile.Content = stack;
            if (type is not null)
            {
                tile.DoubleTapped += (_, _) => AddControl(type);
            }

            tile.PointerPressed += OnToolboxTilePressed;
            ToolboxTiles.Children.Add(tile);
        }
    }

    private void OnToolboxTilePressed(object? sender, PointerPressedEventArgs e)
    {
        foreach (var child in ToolboxTiles.Children)
        {
            if (child is Button tile)
            {
                tile.Background = Brushes.Transparent;
            }
        }

        if (sender is Button selected)
        {
            selected.Background = Brush.Parse("#DCE7FB");
        }
    }

    /// <summary>组件箱目录(对齐主分支基本组件面板)。</summary>
    private static readonly IReadOnlyList<(string Glyph, string Display, string? Type, bool Supported)>
        ToolboxCatalog =
        [
            ("➤", "指针", null, true),
            ("▣", "按钮", YyControlTypes.Button, true),
            ("ab", "标签", YyControlTypes.Label, true),
            ("⌨", "编辑框", YyControlTypes.TextBox, true),
            ("◉", "单选框", YyControlTypes.RadioButton, true),
            ("☑", "复选框", YyControlTypes.CheckBox, true),
            ("⌄", "组合框", YyControlTypes.ComboBox, true),
            ("☷", "列表框", YyControlTypes.ListBox, true),
            ("▤", "分组框", YyControlTypes.GroupBox, true),
            ("▨", "图片框", YyControlTypes.PictureBox, true),
            ("▢", "外形框", null, false),
            ("✎", "画板", null, false),
            ("▥", "选择夹", null, false),
            ("↔", "横向滚…", null, false),
            ("↕", "纵向滚…", null, false),
            ("▤", "进度条", null, false),
            ("═", "滑条块", null, false),
            ("⚭", "超吸链…", null, false),
            ("◷", "时钟", null, false),
            ("▣", "通用对…", null, false),
            ("透", "透明", null, false),
            ("OCX", "OCX", null, false)
        ];

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
        CanvasHost.Height = height + 28;
        TitleChrome.Width = width;
        TitleChromeText.Text = string.IsNullOrWhiteSpace(form?.Title) ? form?.Name ?? string.Empty : form.Title;
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
