using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 官方易语言风格流程线(用户参考图对齐):
/// 同级兄弟块共用一条虚线车道;块关键字用短连接——首个 ┄┄ 起点,后续 └┄▸ 折线加实心三角;
/// 车道至同级最后一个块收口 ▼;语句行不画任何标记。
/// 几何画两遍:页边层(偏移+22)与文字区背景层(偏移0),裁剪在 x=0 互补拼合。
/// </summary>
public sealed class EasyLanguageFlowRenderer : Control, IBackgroundRenderer
{
    /// <summary>正文左侧页边宽度:车道画在其中,文字紧随其右。</summary>
    public const double GutterWidth = 22;

    private static readonly Pen FlowPen = CreatePen(Color.FromRgb(140, 140, 140));
    private static readonly IBrush ArrowBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));

    private readonly TextEditor _editor;

    public EasyLanguageFlowRenderer(TextEditor editor)
    {
        _editor = editor;
        Width = GutterWidth;
        ClipToBounds = false;
        IsHitTestVisible = false;
    }

    /// <summary>挂接到正文编辑器:页边 + 背景层 + 随文档/滚动重绘。</summary>
    public static void Attach(TextEditor editor)
    {
        var margin = new EasyLanguageFlowRenderer(editor);
        editor.TextArea.LeftMargins.Add(margin);
        editor.TextArea.TextView.BackgroundRenderers.Add(margin);
        editor.Document.Changed += (_, _) => margin.InvalidateVisual();
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => margin.InvalidateVisual();
        editor.TextArea.TextView.VisualLinesChanged += (_, _) => margin.InvalidateVisual();
    }

    KnownLayer IBackgroundRenderer.Layer => KnownLayer.Background;

    public override void Render(DrawingContext context) =>
        DrawFlow(context, GutterWidth);

    void IBackgroundRenderer.Draw(TextView textView, DrawingContext context) =>
        DrawFlow(context, 0);

    private sealed class Lane
    {
        public double X;
        public double StartY;
    }

    private void DrawFlow(DrawingContext context, double shift)
    {
        if (Environment.GetEnvironmentVariable("YY_FLOW_CALIBRATE") == "1")
        {
            // 校准:每 20px 画一个标记,标注坐标值,用于实测坐标空间。
            var red = new SolidColorBrush(Color.FromRgb(220, 0, 0));
            for (var x = -30; x <= 60; x += 10)
            {
                context.DrawRectangle(red, null, new Rect(x + shift - 2, 40, 4, 4));
            }

            return;
        }

        var textView = _editor.TextArea.TextView;
        if (_editor.Document is not { } document ||
            !textView.VisualLinesValid ||
            textView.VisualLines.Count == 0)
        {
            return;
        }

        double MapX(double textX) => textX + shift;

        // 可见行几何:文字起点 X + 行 Y/高。
        var rows = new List<(string Text, double TextX, double Y, double Height)>();
        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine is not { } line)
            {
                continue;
            }

            var raw = document.GetText(line);
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var leading = raw.Length - trimmed.Length;
            var segment = new SimpleSegment(line.Offset + leading, trimmed.Length);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                rows.Add((trimmed, rect.X, rect.Y, rect.Height));
                break;
            }
        }

        // 块栈:打开的块引用其(可能与兄弟共享的)车道。
        var stack = new List<(Lane Lane, double KeyX, double Level)>();
        // 同级延续:上一行是同层收口且车道仍存活。
        Lane? continuing = null;
        double continuingLevel = double.NaN;

        for (var index = 0; index < rows.Count; index++)
        {
            var (trimmed, textX, y, height) = rows[index];
            var midY = y + height / 2;
            var keyX = MapX(textX);
            var closes = ClosesBlock(trimmed);
            var opens = OpensBlock(trimmed);
            var branch = IsBranch(trimmed);

            // 缩进回退收口(Python 式,收口不作为文字行):
            // 普通行收同层及以上;分支行只收更深层。同层兄弟接缝不画 ▼,由起点 ┄▸ 桥接。
            if (!closes)
            {
                while (stack.Count > 0 &&
                       (branch ? stack[^1].Level > textX : stack[^1].Level >= textX))
                {
                    var block = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    var closeY = y + height * 0.3;
                    context.DrawLine(
                        FlowPen,
                        new Point(block.Lane.X, block.Lane.StartY),
                        new Point(block.Lane.X, closeY));
                    var junction = opens && Math.Abs(block.Level - textX) < 0.5;
                    if (!junction)
                    {
                        SolidArrow(context, block.Lane.X, closeY, right: false);
                    }

                    continuing = block.Lane;
                    continuingLevel = block.Level;
                }
            }

            if (opens)
            {
                // 每块独立一条车道:┄┄ 起点(后随同级块时带 ▸ 头)。
                // 有箭头(同级延续 ▸):箭头在文字上半部分,车道自下半部分引出;
                // 只有进和出(首块 ┄┄,无箭头):保持正中心。
                var laneX = keyX - 12;
                var afterSibling = continuing is not null &&
                                   Math.Abs(continuingLevel - textX) < 0.5;
                if (afterSibling)
                {
                    var arrowY = y + height * 0.3;
                    var outY = y + height * 0.72;
                    context.DrawLine(
                        FlowPen,
                        new Point(laneX, arrowY),
                        new Point(keyX - 3, arrowY));
                    SolidArrow(context, keyX - 3, arrowY, right: true);
                    stack.Add((new Lane { X = laneX, StartY = outY }, keyX, textX));
                }
                else
                {
                    context.DrawLine(
                        FlowPen,
                        new Point(laneX, midY),
                        new Point(keyX - 3, midY));
                    stack.Add((new Lane { X = laneX, StartY = midY }, keyX, textX));
                }

                continuing = null;
                continuingLevel = double.NaN;
                continue;
            }

            if (closes && stack.Count > 0)
            {
                // 显式收口行(旧源码兼容):本块车道到底画 ▼。
                var block = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                context.DrawLine(
                    FlowPen,
                    new Point(block.Lane.X, block.Lane.StartY),
                    new Point(block.Lane.X, midY));
                SolidArrow(context, block.Lane.X, midY, right: false);
                continuing = block.Lane;
                continuingLevel = block.Level;
                continue;
            }

            if (branch && stack.Count > 0)
            {
                // 否则/捕获:两条线——进的箭头(上半,自上方车道横向指入关键字)
                // 与出的虚线(下半,自箭头下方/关键字左缘另起一条竖线到块收口)。
                var lane = stack[^1].Lane;
                var arrowY = y + height * 0.3;
                var outY = y + height * 0.72;
                context.DrawLine(
                    FlowPen,
                    new Point(lane.X, lane.StartY),
                    new Point(lane.X, arrowY));
                context.DrawLine(
                    FlowPen,
                    new Point(lane.X, arrowY),
                    new Point(keyX - 3, arrowY));
                SolidArrow(context, keyX - 3, arrowY, right: true);
                lane.X = keyX - 5;
                lane.StartY = outY;
                continuing = null;
                continuingLevel = double.NaN;
                continue;
            }

            // 语句行打断同级延续。
            continuing = null;
            continuingLevel = double.NaN;
        }

        // 块到正文末尾结束(或滚出视口):车道到底并以 ▼ 收口,不悬空。
        var lastY = rows.Count == 0 ? 0 : rows[^1].Y + rows[^1].Height;
        var seen = new HashSet<Lane>();
        for (var depth = stack.Count - 1; depth >= 0; depth--)
        {
            var lane = stack[depth].Lane;
            if (!seen.Add(lane))
            {
                continue;
            }

            var closeY = Math.Max(lane.StartY + 6, lastY + 3);
            context.DrawLine(FlowPen, new Point(lane.X, lane.StartY), new Point(lane.X, closeY));
            SolidArrow(context, lane.X, closeY, right: false);
        }
    }

    private static bool OpensBlock(string text) =>
        text.StartsWith("如果真", StringComparison.Ordinal) ||
        text.StartsWith("判断开始", StringComparison.Ordinal) ||
        text.StartsWith("如果", StringComparison.Ordinal) ||
        text.StartsWith("当 ", StringComparison.Ordinal) ||
        text.StartsWith("计次循环", StringComparison.Ordinal) ||
        text.StartsWith("遍历", StringComparison.Ordinal) ||
        text.StartsWith("尝试", StringComparison.Ordinal);

    private static bool IsBranch(string text) =>
        text.StartsWith("判断", StringComparison.Ordinal) ||
        text.StartsWith("默认", StringComparison.Ordinal) ||
        text.StartsWith("否则", StringComparison.Ordinal) ||
        text.StartsWith("捕获", StringComparison.Ordinal);

    private static bool ClosesBlock(string text) =>
        text.StartsWith("结束如果", StringComparison.Ordinal) ||
        text.StartsWith("结束判断", StringComparison.Ordinal) ||
        text.StartsWith("判断结束", StringComparison.Ordinal) ||
        text.StartsWith("如果结束", StringComparison.Ordinal) ||
        text.StartsWith("结束循环", StringComparison.Ordinal) ||
        text.StartsWith("结束遍历", StringComparison.Ordinal) ||
        text.StartsWith("结束尝试", StringComparison.Ordinal);

    /// <summary>实心小三角箭头(官方样式)。</summary>
    private static void SolidArrow(
        DrawingContext context,
        double x,
        double y,
        bool right)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            if (right)
            {
                stream.BeginFigure(new Point(x + 2.5, y), isFilled: true);
                stream.LineTo(new Point(x - 2, y - 2.5));
                stream.LineTo(new Point(x - 2, y + 2.5));
            }
            else
            {
                stream.BeginFigure(new Point(x, y + 2.5), isFilled: true);
                stream.LineTo(new Point(x - 2.5, y - 2));
                stream.LineTo(new Point(x + 2.5, y - 2));
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(ArrowBrush, null, geometry);
    }

    private static Pen CreatePen(Color color) =>
        new(new SolidColorBrush(color), 1)
        {
            DashStyle = new DashStyle([2, 3], 0)
        };
}
