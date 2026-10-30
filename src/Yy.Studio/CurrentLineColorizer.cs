using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Yy.Studio;

public sealed class CurrentLineColorizer : DocumentColorizingTransformer
{
    private readonly TextEditor _editor;
    private readonly Brush _brush = new SolidColorBrush(Color.FromRgb(229, 240, 250));

    public CurrentLineColorizer(TextEditor editor)
    {
        _editor = editor;
        _brush.Freeze();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.LineNumber != _editor.TextArea.Caret.Line)
        {
            return;
        }

        ChangeLinePart(
            line.Offset,
            line.EndOffset,
            element => element.TextRunProperties.SetBackgroundBrush(_brush));
    }
}

