using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Yy.Studio.Structured;

public enum YyPairActionKind
{
    None,
    InsertPair,
    WrapSelection,
    SkipClosing
}

public readonly record struct YyPairAction(
    YyPairActionKind Kind,
    string Opening = "",
    string Closing = "");

/// <summary>所有易语编辑区域共用的小型编辑命令、自动配对和快捷操作。</summary>
public static class YyTextEditing
{
    private static readonly IReadOnlyDictionary<char, (string Open, string Close)> Pairs =
        new Dictionary<char, (string Open, string Close)>
        {
            ['('] = ("(", ")"),
            ['['] = ("[", "]"),
            ['{'] = ("{", "}"),
            ['"'] = ("\"", "\""),
            ['“'] = ("“", "”")
        };

    public static YyPairAction GetPairAction(
        string text,
        int selectionStart,
        int selectionLength,
        string input)
    {
        if (input.Length != 1)
        {
            return default;
        }

        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        var character = input[0];
        var next = start + length < text.Length ? text[start + length] : '\0';
        if (length == 0 &&
            Pairs.Values.Any(pair => pair.Close.Length == 1 && pair.Close[0] == character) &&
            next == character)
        {
            return new YyPairAction(YyPairActionKind.SkipClosing);
        }

        return Pairs.TryGetValue(character, out var pair)
            ? new YyPairAction(
                length > 0 ? YyPairActionKind.WrapSelection : YyPairActionKind.InsertPair,
                pair.Open,
                pair.Close)
            : default;
    }

    public static bool HandleTextEntering(TextEditor editor, TextCompositionEventArgs e)
    {
        if (!TryHandleTextInput(editor, e.Text))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    /// <summary>执行自动配对输入；不依赖 WPF TextComposition，便于无选区行为回归测试。</summary>
    public static bool TryHandleTextInput(TextEditor editor, string input)
    {
        var selection = editor.TextArea.Selection;
        var segment = selection.SurroundingSegment;
        var start = segment?.Offset ?? editor.CaretOffset;
        var length = segment?.Length ?? 0;
        var action = GetPairAction(editor.Text, start, length, input);
        switch (action.Kind)
        {
            case YyPairActionKind.SkipClosing:
                editor.CaretOffset = Math.Min(editor.Document.TextLength, start + 1);
                return true;
            case YyPairActionKind.InsertPair:
                editor.Document.Insert(start, action.Opening + action.Closing);
                editor.CaretOffset = start + action.Opening.Length;
                return true;
            case YyPairActionKind.WrapSelection:
                var selected = editor.Document.GetText(start, length);
                var replacement = action.Opening + selected + action.Closing;
                editor.Document.Replace(start, length, replacement);
                editor.Select(
                    start + action.Opening.Length,
                    Math.Max(0, selected.Length));
                return true;
            default:
                return false;
        }
    }

    public static bool HandleBackspace(TextEditor editor, KeyEventArgs e)
    {
        if (e.Key != Key.Back ||
            !editor.TextArea.Selection.IsEmpty ||
            editor.CaretOffset <= 0 ||
            editor.CaretOffset >= editor.Document.TextLength)
        {
            return false;
        }

        var previous = editor.Document.GetCharAt(editor.CaretOffset - 1);
        var next = editor.Document.GetCharAt(editor.CaretOffset);
        if (!Pairs.Values.Any(pair =>
                pair.Open.Length == 1 &&
                pair.Close.Length == 1 &&
                pair.Open[0] == previous &&
                pair.Close[0] == next))
        {
            return false;
        }

        editor.Document.Remove(editor.CaretOffset - 1, 2);
        e.Handled = true;
        return true;
    }

    public static void ToggleComment(TextEditor editor)
    {
        var document = editor.Document;
        var selection = editor.TextArea.Selection;
        var startOffset = selection.SurroundingSegment.Offset;
        var endOffset = startOffset + selection.SurroundingSegment.Length;
        var startLine = document.GetLineByOffset(startOffset);
        var endLine = document.GetLineByOffset(endOffset);
        if (endOffset > 0 &&
            endOffset == endLine.Offset &&
            endLine.LineNumber > startLine.LineNumber)
        {
            endLine = endLine.PreviousLine ?? endLine;
        }

        var lines = Enumerable.Range(startLine.LineNumber, endLine.LineNumber - startLine.LineNumber + 1)
            .Select(document.GetLineByNumber)
            .Where(line => !string.IsNullOrWhiteSpace(document.GetText(line)))
            .ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var uncomment = lines.All(line =>
        {
            var text = document.GetText(line);
            return text.TrimStart().StartsWith('\'');
        });
        document.BeginUpdate();
        try
        {
            for (var index = lines.Count - 1; index >= 0; index--)
            {
                var line = document.GetLineByNumber(lines[index].LineNumber);
                var text = document.GetText(line);
                var leading = text.Length - text.TrimStart().Length;
                if (uncomment)
                {
                    var remove = text[leading..].StartsWith("' ", StringComparison.Ordinal) ? 2 : 1;
                    document.Remove(line.Offset + leading, remove);
                }
                else
                {
                    document.Insert(line.Offset + leading, "' ");
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }

        var updatedStart = document.GetLineByNumber(startLine.LineNumber).Offset;
        var updatedEnd = document.GetLineByNumber(
            Math.Min(document.LineCount, endLine.LineNumber)).EndOffset;
        editor.Select(updatedStart, Math.Max(0, updatedEnd - updatedStart));
    }

    public static void DuplicateLine(TextEditor editor)
    {
        var document = editor.Document;
        var selection = editor.TextArea.Selection;
        if (!selection.IsEmpty)
        {
            var start = selection.SurroundingSegment.Offset;
            var length = selection.SurroundingSegment.Length;
            var selected = document.GetText(start, length);
            document.Insert(start + length, selected);
            editor.Select(start + length, selected.Length);
            return;
        }

        var line = document.GetLineByOffset(editor.CaretOffset);
        var text = document.GetText(line);
        var newLine = Environment.NewLine;
        document.Insert(line.EndOffset, newLine + text);
        editor.CaretOffset = line.EndOffset + newLine.Length + text.Length;
    }

    public static void DeleteLine(TextEditor editor)
    {
        var document = editor.Document;
        var selection = editor.TextArea.Selection;
        if (!selection.IsEmpty)
        {
            var selectionStart = selection.SurroundingSegment.Offset;
            var selectionLength = selection.SurroundingSegment.Length;
            document.Remove(selectionStart, selectionLength);
            editor.CaretOffset = Math.Min(selectionStart, document.TextLength);
            return;
        }

        var line = document.GetLineByOffset(editor.CaretOffset);
        var start = line.Offset;
        var length = line.TotalLength;
        if (length == 0 && start < document.TextLength)
        {
            length = 1;
        }

        document.Remove(start, length);
        editor.CaretOffset = Math.Min(start, document.TextLength);
    }
}
