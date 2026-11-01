using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Yy.Studio.Structured;

/// <summary>在正文背景层绘制易语言流程线，不占用光标排版左边距。</summary>
public sealed class EasyLanguageFlowRenderer : IBackgroundRenderer
{
    private static readonly Pen FlowPen = CreatePen(Color.FromRgb(113, 151, 128));
    private static readonly Pen BranchPen = CreatePen(Color.FromRgb(92, 132, 109));
    private static readonly Brush ArrowBrush = new SolidColorBrush(Color.FromRgb(75, 112, 91));

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is not { } document || !textView.VisualLinesValid)
        {
            return;
        }

        var lineHeight = Math.Max(16, textView.DefaultLineHeight);
        var first = Math.Max(1, (int)(textView.VerticalOffset / lineHeight));
        var last = Math.Min(
            document.LineCount,
            first + (int)(textView.ActualHeight / lineHeight) + 2);
        var depth = CalculateDepth(document, first);
        for (var number = first; number <= last; number++)
        {
            var line = document.GetLineByNumber(number);
            var text = document.GetText(line).TrimStart();
            var y = (number - 1) * lineHeight - textView.VerticalOffset;
            if (ClosesBlock(text) || IsBranch(text))
            {
                depth = Math.Max(0, depth - 1);
            }

            for (var level = 0; level < depth; level++)
            {
                var x = 4 + level * 10;
                drawingContext.DrawLine(FlowPen, new Point(x, y), new Point(x, y + lineHeight));
            }

            if (IsFlowLine(text))
            {
                var x = 4 + depth * 10;
                drawingContext.DrawLine(
                    BranchPen,
                    new Point(x - 4, y + lineHeight / 2),
                    new Point(x + 5, y + lineHeight / 2));
                DrawArrow(
                    drawingContext,
                    x + 6,
                    y + lineHeight / 2,
                    text.StartsWith("否则", StringComparison.Ordinal) ||
                    text.StartsWith("捕获", StringComparison.Ordinal));
            }

            if (OpensBlock(text) || IsBranch(text))
            {
                depth++;
            }
        }
    }

    private static int CalculateDepth(TextDocument document, int throughLine)
    {
        var depth = 0;
        for (var number = 1; number < throughLine; number++)
        {
            var text = document.GetText(document.GetLineByNumber(number)).TrimStart();
            if (ClosesBlock(text) || IsBranch(text))
            {
                depth = Math.Max(0, depth - 1);
            }

            if (OpensBlock(text) || IsBranch(text))
            {
                depth++;
            }
        }

        return depth;
    }

    private static bool OpensBlock(string text) =>
        text.StartsWith("如果", StringComparison.Ordinal) ||
        text.StartsWith("当", StringComparison.Ordinal) ||
        text.StartsWith("计次循环", StringComparison.Ordinal) ||
        text.StartsWith("遍历", StringComparison.Ordinal) ||
        text.StartsWith("尝试", StringComparison.Ordinal);

    private static bool IsFlowLine(string text) =>
        OpensBlock(text) || IsBranch(text) || ClosesBlock(text);

    private static bool IsBranch(string text) =>
        text.StartsWith("否则", StringComparison.Ordinal) ||
        text.StartsWith("捕获", StringComparison.Ordinal);

    private static bool ClosesBlock(string text) =>
        text.StartsWith("结束如果", StringComparison.Ordinal) ||
        text.StartsWith("结束循环", StringComparison.Ordinal) ||
        text.StartsWith("结束遍历", StringComparison.Ordinal) ||
        text.StartsWith("结束尝试", StringComparison.Ordinal);

    private static void DrawArrow(DrawingContext context, double x, double y, bool left)
    {
        var direction = left ? -1 : 1;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(x, y), true, true);
            stream.LineTo(new Point(x + direction * 4, y - 4), true, false);
            stream.LineTo(new Point(x + direction * 4, y + 4), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(ArrowBrush, null, geometry);
    }

    private static Pen CreatePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
