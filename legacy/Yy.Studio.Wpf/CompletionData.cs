using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Media;

namespace Yy.Studio;

public sealed class CompletionData(
    string text,
    string description,
    CompletionKind kind,
    string? insertText = null,
    int caretOffset = 0,
    int selectionLength = 0) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => text;
    public object Content => text;
    public object Description => description;
    public double Priority => kind switch
    {
        CompletionKind.Package => 4.2,
        CompletionKind.Component => 4,
        CompletionKind.Parameter => 3.8,
        CompletionKind.Variable => 3.7,
        CompletionKind.Property => 3,
        CompletionKind.Keyword => 2,
        CompletionKind.Builtin => 1,
        _ => 0
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var offset = completionSegment.Offset;
        var replacement = insertText ?? Text;
        var adjustedCaretOffset = caretOffset;
        if (insertText is not null)
        {
            var line = textArea.Document.GetLineByOffset(offset);
            var lineText = textArea.Document.GetText(line.Offset, line.Length);
            var indentLength = lineText.TakeWhile(character => character is ' ' or '\t').Count();
            var indent = new string(' ', indentLength);
            replacement = replacement.Replace("\n", "\n" + indent, StringComparison.Ordinal);
            adjustedCaretOffset += indentLength * replacement[..Math.Min(caretOffset, replacement.Length)]
                .Count(character => character == '\n');
        }

        textArea.Document.Replace(completionSegment, replacement);
        var caret = Math.Clamp(offset + adjustedCaretOffset, 0, textArea.Document.TextLength);
        if (selectionLength > 0)
        {
            textArea.Selection = Selection.Create(
                textArea,
                caret,
                Math.Min(textArea.Document.TextLength, caret + selectionLength));
        }
        else
        {
            textArea.Caret.Offset = caret;
        }
    }
}

public enum CompletionKind
{
    Snippet,
    Package,
    Builtin,
    Keyword,
    Component,
    Property,
    Parameter,
    Variable
}

