using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace Yy.Studio.Avalonia.Editor;

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

/// <summary>所有易语编辑区域共用的小型编辑命令、自动配对和全角归一化。</summary>
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

    /// <summary>把中文输入法产生的全角括号、运算符和引号归回半角存储。</summary>
    public static string NormalizeFullWidthInput(string? input) =>
        string.IsNullOrEmpty(input) ? string.Empty : FullWidthText.FromDisplay(input);

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

    public static bool HandleTextEntering(TextEditor editor, TextInputEventArgs e)
    {
        var input = NormalizeFullWidthInput(e.Text);
        if (input.Length == 0)
        {
            return false;
        }

        if (input != e.Text)
        {
            e.Text = input;
        }

        if (!TryHandleTextInput(editor, input, normalizeFullWidth: false))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    /// <summary>执行自动配对输入；不依赖输入法事件，便于无选区行为回归测试。</summary>
    public static bool TryHandleTextInput(
        TextEditor editor,
        string input,
        bool normalizeFullWidth = true)
    {
        if (normalizeFullWidth)
        {
            input = NormalizeFullWidthInput(input);
        }

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
        var surrounding = selection.SurroundingSegment;
        var startOffset = surrounding?.Offset ?? editor.CaretOffset;
        var endOffset = startOffset + (surrounding?.Length ?? 0);
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
        if (!selection.IsEmpty && selection.SurroundingSegment is { } segment)
        {
            var start = segment.Offset;
            var selected = document.GetText(start, segment.Length);
            document.Insert(start + segment.Length, selected);
            editor.Select(start + segment.Length, selected.Length);
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
        if (!selection.IsEmpty && selection.SurroundingSegment is { } segment)
        {
            document.Remove(segment.Offset, segment.Length);
            editor.CaretOffset = Math.Min(segment.Offset, document.TextLength);
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

    /// <summary>取得指定偏移处的标识符，供 F1 上下文帮助使用。</summary>
    public static string GetWordAtOffset(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var position = Math.Clamp(offset, 0, text.Length);
        if (position == text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position = Math.Max(0, position - 1);
        }

        if (position >= text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            return string.Empty;
        }

        var start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var end = position + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return text[start..end];
    }
}
