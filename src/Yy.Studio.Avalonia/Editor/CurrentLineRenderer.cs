using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>当前行浅蓝底纹(对齐 WPF CurrentLineColorizer,#E5F0FA)。</summary>
public sealed class CurrentLineRenderer : IBackgroundRenderer
{
    private static readonly IBrush Tint = new SolidColorBrush(Color.FromRgb(229, 240, 250));

    private readonly AvaloniaEdit.Editing.TextArea _textArea;

    public CurrentLineRenderer(AvaloniaEdit.Editing.TextArea textArea)
    {
        _textArea = textArea;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null || textView.VisualLinesValid == false)
        {
            return;
        }

        var caretLine = _textArea.Caret.Line;
        if (caretLine < 1 || caretLine > textView.Document.LineCount)
        {
            return;
        }

        var line = textView.Document.GetLineByNumber(caretLine);
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
        {
            drawingContext.DrawRectangle(
                Tint,
                null,
                new Rect(rect.X, rect.Y, textView.Bounds.Width, rect.Height));
        }
    }

    public static void Attach(AvaloniaEdit.TextEditor editor)
    {
        var renderer = new CurrentLineRenderer(editor.TextArea);
        editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        editor.TextArea.Caret.PositionChanged += (_, _) =>
            editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }
}
