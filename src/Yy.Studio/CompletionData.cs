using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows.Media;

namespace Yy.Studio;

public sealed class CompletionData(string text, string description, CompletionKind kind) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text => text;
    public object Content => text;
    public object Description => description;
    public double Priority => kind switch
    {
        CompletionKind.Keyword => 2,
        CompletionKind.Builtin => 1,
        _ => 0
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}

public enum CompletionKind
{
    Snippet,
    Builtin,
    Keyword
}

