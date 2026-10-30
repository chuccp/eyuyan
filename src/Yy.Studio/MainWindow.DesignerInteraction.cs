using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;


namespace Yy.Studio;

/// <summary>
/// 窗体画布渲染、拖放、缩放、选择、剪贴板与设计器历史。
/// </summary>
public partial class MainWindow : Window
{
    private void RenderDesignerForm()
    {
        if (_designForm is null)
        {
            return;
        }

        DesignerCanvas.Children.Clear();
        _selectionVisuals.Clear();
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        DesignerCanvas.Width = _designForm.Width;
        DesignerCanvas.Height = _designForm.Height;
        DesignerCanvas.Background = CreateDesignerBackground(_designForm.Background);
        DesignerTitleText.Text = string.IsNullOrWhiteSpace(_designForm.Title)
            ? _designForm.Name
            : _designForm.Title;

        foreach (var control in _designForm.Controls)
        {
            AddControlVisual(control);
        }

        PopulateDesignerProperties();
    }

    private Brush CreateDesignerBackground(string color)
    {
        var baseColor = YyFormRenderer.Brush(color) is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(236, 233, 216);

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(baseColor),
            null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        // 官方式点阵:8px 网格交点画 1px 灰点
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private void AddControlVisual(YyControlModel model)
    {
        var element = YyFormRenderer.Create(model);
        element.Visibility = Visibility.Visible;
        element.Opacity = model.IsVisible ? 1 : 0.35;
        Canvas.SetLeft(element, model.X);
        Canvas.SetTop(element, model.Y);
        Panel.SetZIndex(element, _designForm?.Controls.IndexOf(model) ?? 0);
        DesignerCanvas.Children.Add(element);
    }

    private void OnToolboxMouseDown(object sender, MouseButtonEventArgs e)
    {
        _toolboxDragStart = e.GetPosition(ControlToolbox);
    }

    private void OnToolboxMouseMove(object sender, MouseEventArgs e)
    {
        if (_toolboxDragging ||
            e.LeftButton != MouseButtonState.Pressed ||
            ControlToolbox.SelectedItem is not ToolboxItem { Supported: true, Type: { } type })
        {
            return;
        }

        var current = e.GetPosition(ControlToolbox);
        if (Math.Abs(current.X - _toolboxDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _toolboxDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _toolboxDragging = true;
        try
        {
            System.Windows.DragDrop.DoDragDrop(ControlToolbox, type, DragDropEffects.Copy);
        }
        finally
        {
            _toolboxDragging = false;
        }
    }

    private void OnToolboxDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ControlToolbox.SelectedItem is ToolboxItem { Supported: true, Type: { } type } &&
            _designForm is not null)
        {
            AddDesignerControl(type, new Point(44 + _designForm.Controls.Count % 6 * 22, 44));
            e.Handled = true;
        }
        else if (ControlToolbox.SelectedItem is ToolboxItem { Supported: false } item)
        {
            ShowHint($"组件“{item.DisplayName}”暂未支持，后续版本提供。");
        }
    }

    private void OnDesignerCanvasDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string type &&
            YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDesignerCanvasDrop(object sender, DragEventArgs e)
    {
        if (_designForm is null || e.Data.GetData(typeof(string)) is not string type)
        {
            return;
        }

        AddDesignerControl(type, e.GetPosition(DesignerCanvas));
        e.Handled = true;
    }

    private void AddDesignerControl(string type, Point position)
    {
        if (_designForm is null)
        {
            return;
        }

        var sequence = 1;
        while (_designForm.Controls.Any(control =>
                   control.Name == GetControlBaseName(type) + sequence))
        {
            sequence++;
        }

        var control = YyControlTypes.Create(type, sequence);
        control.Name = GetControlBaseName(type) + sequence;
        control.X = Clamp(Snap(position.X), 0, Math.Max(0, _designForm.Width - control.Width));
        control.Y = Clamp(Snap(position.Y), 0, Math.Max(0, _designForm.Height - control.Height));
        _designForm.Controls.Add(control);
        AddControlVisual(control);
        SelectDesignerControl(control);
        DesignerChanged();
        SetStatus($"已添加{type}“{control.Name}”", ReadyBrush);
    }

    private static string GetControlBaseName(string type) =>
        YyControlTypes.GetBaseName(type);

    private static bool TryGetDefaultEvent(string controlType, out ControlEventKind eventKind)
    {
        eventKind = controlType switch
        {
            YyControlTypes.Button => ControlEventKind.Click,
            YyControlTypes.TextBox => ControlEventKind.TextChanged,
            YyControlTypes.ComboBox or YyControlTypes.ListBox => ControlEventKind.SelectionChanged,
            YyControlTypes.CheckBox or YyControlTypes.RadioButton => ControlEventKind.CheckedChanged,
            _ => default
        };

        return controlType is YyControlTypes.Button or YyControlTypes.TextBox
            or YyControlTypes.ComboBox or YyControlTypes.ListBox or YyControlTypes.CheckBox;
    }

    private void OnDesignerCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_designForm is null)
        {
            return;
        }

        var resizeTag = FindAncestorTag<ResizeHandleTag>(e.OriginalSource as DependencyObject);
        if (resizeTag is not null && _selectedControl is not null && _selectedControls.Count == 1)
        {
            _resizeDirection = resizeTag.Direction;
            _designerDragMode = DesignerDragMode.Resize;
            _designerDragStart = e.GetPosition(DesignerCanvas);
            _designerOriginalBounds = GetControlBounds(_selectedControl);
            _designerDragChanged = false;
            DesignerCanvas.CaptureMouse();
            DesignerCanvas.Focus();
            e.Handled = true;
            return;
        }

        var control = FindAncestorTag<YyControlModel>(e.OriginalSource as DependencyObject);
        if (control is not null)
        {
            if (e.ClickCount == 2 && TryGetDefaultEvent(control.Type, out var eventKind))
            {
                SelectDesignerControl(control);
                CreateOrOpenControlEventHandler(control, eventKind);
                e.Handled = true;
                return;
            }

            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (additive)
            {
                SelectDesignerControl(control, additive: true);
                e.Handled = true;
                return;
            }

            if (!_selectedControls.Contains(control))
            {
                SelectDesignerControl(control);
            }
            else if (_selectedControls.Count > 1)
            {
                _selectedControl = control;
                _selectedElement = FindControlElement(control);
                PopulateDesignerProperties();
                UpdateSelectionVisuals();
            }

            _designerDragMode = DesignerDragMode.Move;
            _designerDragStart = e.GetPosition(DesignerCanvas);
            _designerOriginalBounds = GetControlBounds(control);
            _designerDragBounds.Clear();
            foreach (var selected in _selectedControls)
            {
                _designerDragBounds[selected] = GetControlBounds(selected);
            }

            _designerDragChanged = false;
            DesignerCanvas.CaptureMouse();
            DesignerCanvas.Focus();
        }
        else
        {
            SelectDesignerForm();
            DesignerCanvas.Focus();
        }

        e.Handled = true;
    }

    private T? FindAncestorTag<T>(DependencyObject? source) where T : class
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, DesignerCanvas))
        {
            if (current is FrameworkElement { Tag: T value })
            {
                return value;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Rect GetControlBounds(YyControlModel control) =>
        new(control.X, control.Y, control.Width, control.Height);

    private void OnDesignerCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None ||
            _designForm is null ||
            _selectedControl is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(DesignerCanvas);
        var deltaX = current.X - _designerDragStart.X;
        var deltaY = current.Y - _designerDragStart.Y;

        if (_designerDragMode == DesignerDragMode.Move)
        {
            var primaryBounds = _designerOriginalBounds;
            var roundedDeltaX = Snap(primaryBounds.X + deltaX) - primaryBounds.X;
            var roundedDeltaY = Snap(primaryBounds.Y + deltaY) - primaryBounds.Y;
            var groupLeft = _designerDragBounds.Values.Min(bounds => bounds.Left);
            var groupTop = _designerDragBounds.Values.Min(bounds => bounds.Top);
            var groupRight = _designerDragBounds.Values.Max(bounds => bounds.Right);
            var groupBottom = _designerDragBounds.Values.Max(bounds => bounds.Bottom);
            var appliedDeltaX = Clamp(roundedDeltaX, -groupLeft, _designForm.Width - groupRight);
            var appliedDeltaY = Clamp(roundedDeltaY, -groupTop, _designForm.Height - groupBottom);

            foreach (var selected in _selectedControls)
            {
                if (!_designerDragBounds.TryGetValue(selected, out var original))
                {
                    continue;
                }

                selected.X = original.X + appliedDeltaX;
                selected.Y = original.Y + appliedDeltaY;
            }
        }
        else
        {
            ApplyResize(deltaX, deltaY);
        }

        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        _designerDragChanged = true;
    }

    private void ApplyResize(double deltaX, double deltaY)
    {
        if (_selectedControl is null || _designForm is null)
        {
            return;
        }

        const double minimumWidth = 24;
        const double minimumHeight = 20;
        var left = _designerOriginalBounds.Left;
        var top = _designerOriginalBounds.Top;
        var right = _designerOriginalBounds.Right;
        var bottom = _designerOriginalBounds.Bottom;

        if (_resizeDirection is ResizeDirection.West or ResizeDirection.NorthWest or ResizeDirection.SouthWest)
        {
            left = Clamp(Snap(_designerOriginalBounds.Left + deltaX), 0, right - minimumWidth);
        }

        if (_resizeDirection is ResizeDirection.East or ResizeDirection.NorthEast or ResizeDirection.SouthEast)
        {
            right = Clamp(
                Snap(_designerOriginalBounds.Right + deltaX),
                left + minimumWidth,
                _designForm.Width);
        }

        if (_resizeDirection is ResizeDirection.North or ResizeDirection.NorthWest or ResizeDirection.NorthEast)
        {
            top = Clamp(Snap(_designerOriginalBounds.Top + deltaY), 0, bottom - minimumHeight);
        }

        if (_resizeDirection is ResizeDirection.South or ResizeDirection.SouthWest or ResizeDirection.SouthEast)
        {
            bottom = Clamp(
                Snap(_designerOriginalBounds.Bottom + deltaY),
                top + minimumHeight,
                _designForm.Height);
        }

        _selectedControl.X = left;
        _selectedControl.Y = top;
        _selectedControl.Width = right - left;
        _selectedControl.Height = bottom - top;
    }

    private double Snap(double value) =>
        SnapCheckBox.IsChecked == true ? Math.Round(value / 8) * 8 : value;

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private void OnDesignerCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None)
        {
            return;
        }

        DesignerCanvas.ReleaseMouseCapture();
        _designerDragMode = DesignerDragMode.None;
        if (_designerDragChanged)
        {
            DesignerChanged();
            SetStatus("窗体布局已更新", ReadyBrush);
        }

        _designerDragChanged = false;
        _designerDragBounds.Clear();
        e.Handled = true;
    }

    private void OnDesignerCanvasKeyDown(object sender, KeyEventArgs e)
    {
        if (_designForm is null)
        {
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetDesignerSelection(_designForm.Controls);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SelectDesignerForm();
            e.Handled = true;
            return;
        }

        if (_selectedControls.Count == 0)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedControls();
            e.Handled = true;
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
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

        MoveSelectedControls(deltaX, deltaY);
        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        DesignerChanged();
        e.Handled = true;
    }

    private void MoveSelectedControls(double deltaX, double deltaY)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        var left = _selectedControls.Min(control => control.X);
        var top = _selectedControls.Min(control => control.Y);
        var right = _selectedControls.Max(control => control.X + control.Width);
        var bottom = _selectedControls.Max(control => control.Y + control.Height);
        var appliedX = Clamp(deltaX, -left, _designForm.Width - right);
        var appliedY = Clamp(deltaY, -top, _designForm.Height - bottom);
        foreach (var control in _selectedControls)
        {
            control.X += appliedX;
            control.Y += appliedY;
        }
    }

    private void OnDesignerAlignMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnAlignDesignerControls(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action } ||
            _designForm is null ||
            _selectedControls.Count < 2)
        {
            SetStatus("对齐至少需要选择两个控件（Ctrl+单击多选）", WarningBrush);
            return;
        }

        var left = _selectedControls.Min(control => control.X);
        var top = _selectedControls.Min(control => control.Y);
        var right = _selectedControls.Max(control => control.X + control.Width);
        var bottom = _selectedControls.Max(control => control.Y + control.Height);
        switch (action)
        {
            case "Left":
                foreach (var control in _selectedControls)
                {
                    control.X = left;
                }

                break;
            case "CenterX":
                var centerX = (left + right) / 2;
                foreach (var control in _selectedControls)
                {
                    control.X = centerX - control.Width / 2;
                }

                break;
            case "Right":
                foreach (var control in _selectedControls)
                {
                    control.X = right - control.Width;
                }

                break;
            case "Top":
                foreach (var control in _selectedControls)
                {
                    control.Y = top;
                }

                break;
            case "CenterY":
                var centerY = (top + bottom) / 2;
                foreach (var control in _selectedControls)
                {
                    control.Y = centerY - control.Height / 2;
                }

                break;
            case "Bottom":
                foreach (var control in _selectedControls)
                {
                    control.Y = bottom - control.Height;
                }

                break;
            case "DistributeX":
                if (_selectedControls.Count < 3)
                {
                    SetStatus("水平等距分布至少需要三个控件", WarningBrush);
                    return;
                }

                DistributeControls(horizontal: true);
                break;
            case "DistributeY":
                if (_selectedControls.Count < 3)
                {
                    SetStatus("垂直等距分布至少需要三个控件", WarningBrush);
                    return;
                }

                DistributeControls(horizontal: false);
                break;
            default:
                return;
        }

        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        DesignerChanged();
        SetStatus($"已对齐 {_selectedControls.Count} 个控件", ReadyBrush);
    }

    private void DistributeControls(bool horizontal)
    {
        var ordered = _selectedControls
            .OrderBy(control => horizontal ? control.X : control.Y)
            .ToArray();
        var start = horizontal ? ordered[0].X : ordered[0].Y;
        var end = horizontal
            ? ordered[^1].X + ordered[^1].Width
            : ordered[^1].Y + ordered[^1].Height;
        var totalSize = ordered.Sum(control => horizontal ? control.Width : control.Height);
        var gap = (end - start - totalSize) / (ordered.Length - 1);
        if (gap < 0)
        {
            SetStatus("控件总宽度或高度过大，无法等距分布", WarningBrush);
            return;
        }

        var cursor = start;
        foreach (var control in ordered)
        {
            if (horizontal)
            {
                control.X = cursor;
                cursor += control.Width + gap;
            }
            else
            {
                control.Y = cursor;
                cursor += control.Height + gap;
            }
        }
    }

    private void OnDesignerCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The designer canvas size is driven by the active form model.
    }

    /// <summary>设计器标题栏随窗体属性联动(最小化/最大化按钮与边框样式)。</summary>
    private void UpdateDesignerTitleChrome()
    {
        if (_designForm is null)
        {
            return;
        }

        var hasChrome = _designForm.BorderStyle != "无边框";
        DesignerMinGlyph.Visibility = hasChrome && _designForm.MinimizeBox
            ? Visibility.Visible
            : Visibility.Collapsed;
        DesignerMaxGlyph.Visibility = hasChrome && _designForm.MaximizeBox
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // =============== 设计器控件剪贴板(对齐官方 Ctrl+X/C/V) ===============

}