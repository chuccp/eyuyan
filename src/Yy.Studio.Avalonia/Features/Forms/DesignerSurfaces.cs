using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>设计器缩放方向(8 个手柄)。</summary>
public enum ResizeDirection
{
    NorthWest,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West
}

/// <summary>
/// 窗体设计器网格层:绘制窗体背景与 8px 点阵网格。
/// 控件放在其上方独立的 Canvas 中(Avalonia 的 Panel.Render 是密封的)。
/// </summary>
public sealed class DesignerSurface : Control
{
    public static readonly StyledProperty<IBrush?> FormBackgroundProperty =
        AvaloniaProperty.Register<DesignerSurface, IBrush?>(nameof(FormBackground));

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<DesignerSurface, bool>(nameof(ShowGrid), true);

    public static readonly StyledProperty<double> GridSizeProperty =
        AvaloniaProperty.Register<DesignerSurface, double>(nameof(GridSize), 8);

    private static readonly IBrush GridBrush =
        new SolidColorBrush(Color.FromArgb(38, 80, 96, 112));

    private static readonly IBrush MajorGridBrush =
        new SolidColorBrush(Color.FromArgb(70, 80, 96, 112));

    private static readonly IPen BorderPen =
        new Pen(new SolidColorBrush(Color.FromRgb(176, 186, 198)), 1);

    private static readonly IPen GridPen = new Pen(GridBrush, 1);

    private static readonly IPen MajorGridPen = new Pen(MajorGridBrush, 1);

    static DesignerSurface()
    {
        AffectsRender<DesignerSurface>(
            FormBackgroundProperty,
            ShowGridProperty,
            GridSizeProperty);
    }

    public IBrush? FormBackground
    {
        get => GetValue(FormBackgroundProperty);
        set => SetValue(FormBackgroundProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public double GridSize
    {
        get => GetValue(GridSizeProperty);
        set => SetValue(GridSizeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        context.FillRectangle(FormBackground ?? Brushes.White, new Rect(0, 0, width, height));

        var gridSize = GridSize;
        if (ShowGrid && gridSize >= 2)
        {
            var index = 0;
            for (var x = gridSize; x < width; x += gridSize)
            {
                index++;
                context.DrawLine(
                    index % 5 == 0 ? MajorGridPen : GridPen,
                    new Point(x, 0),
                    new Point(x, height));
            }

            index = 0;
            for (var y = gridSize; y < height; y += gridSize)
            {
                index++;
                context.DrawLine(
                    index % 5 == 0 ? MajorGridPen : GridPen,
                    new Point(0, y),
                    new Point(width, y));
            }
        }

        context.DrawRectangle(null, BorderPen, new Rect(0, 0, width, height));
        base.Render(context);
    }
}

/// <summary>
/// 选择装饰层:绘制选中控件外框和当前控件的 8 个缩放手柄。
/// 该层不参与命中测试，手柄命中由 <see cref="FormDesignerView"/> 按几何位置计算。
/// </summary>
public sealed class DesignerSelectionOverlay : Control
{
    private static readonly IBrush PrimaryOutline = new SolidColorBrush(Colors.Black);
    private static readonly IBrush SecondaryOutline =
        new SolidColorBrush(Color.FromRgb(128, 128, 128));

    private static readonly IBrush HandleFill = new SolidColorBrush(Colors.Black);
    private static readonly IPen HandleBorder = new Pen(new SolidColorBrush(Colors.White), 1);

    private static readonly ResizeDirection[] AllDirections =
        Enum.GetValues<ResizeDirection>();

    private IReadOnlyList<YyControlModel> _selection = [];
    private YyControlModel? _primary;

    public void UpdateSelection(
        IReadOnlyList<YyControlModel> selection,
        YyControlModel? primary)
    {
        _selection = selection;
        _primary = primary;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_selection.Count == 0)
        {
            return;
        }

        foreach (var control in _selection)
        {
            var brush = ReferenceEquals(control, _primary) ? PrimaryOutline : SecondaryOutline;
            var pen = new Pen(brush, 1.5);
            context.DrawRectangle(null, pen, BoundsOf(control));
        }

        if (_primary is null || _selection.Count != 1)
        {
            return;
        }

        foreach (var direction in AllDirections)
        {
            var center = HandlePoint(_primary, direction);
            var rect = new Rect(center.X - 4, center.Y - 4, 8, 8);
            context.DrawRectangle(HandleFill, HandleBorder, rect);
        }
    }

    public static Rect BoundsOf(YyControlModel control) =>
        new(control.X, control.Y, control.Width, control.Height);

    public static Point HandlePoint(YyControlModel control, ResizeDirection direction)
    {
        var left = control.X;
        var top = control.Y;
        var centerX = left + control.Width / 2;
        var centerY = top + control.Height / 2;
        var right = left + control.Width;
        var bottom = top + control.Height;
        return direction switch
        {
            ResizeDirection.NorthWest => new Point(left, top),
            ResizeDirection.North => new Point(centerX, top),
            ResizeDirection.NorthEast => new Point(right, top),
            ResizeDirection.East => new Point(right, centerY),
            ResizeDirection.SouthEast => new Point(right, bottom),
            ResizeDirection.South => new Point(centerX, bottom),
            ResizeDirection.SouthWest => new Point(left, bottom),
            ResizeDirection.West => new Point(left, centerY),
            _ => new Point()
        };
    }

    /// <summary>判断画布坐标是否落在某个缩放手柄上(命中区域 10x10)。</summary>
    public static ResizeDirection? HitTestHandle(
        YyControlModel? control,
        Point point)
    {
        if (control is null)
        {
            return null;
        }

        foreach (var direction in AllDirections)
        {
            var center = HandlePoint(control, direction);
            var hit = new Rect(center.X - 5, center.Y - 5, 10, 10);
            if (hit.Contains(point))
            {
                return direction;
            }
        }

        return null;
    }
}
