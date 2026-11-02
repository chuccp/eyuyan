using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace Yy.Studio.Avalonia.Editor;

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

/// <summary>
/// AvaloniaEdit 补全项:命中字符加粗、右侧灰色类型提示(参照 IntelliJ LookupElement 表现),
/// 保留多行片段的缩进和插入后选中范围。
/// </summary>
public sealed class YyCompletionData : ICompletionData
{
    private readonly string _text;
    private readonly string _description;
    private readonly CompletionKind _kind;
    private readonly string? _insertText;
    private readonly int _caretOffset;
    private readonly int _selectionLength;
    private readonly int _matchScore;
    private readonly string? _typeHint;
    private readonly IReadOnlyList<(int Start, int Length)> _matchRanges;
    private object? _content;

    public YyCompletionData(
        string text,
        string description,
        CompletionKind kind,
        string? insertText = null,
        int caretOffset = 0,
        int selectionLength = 0,
        string? typeHint = null,
        int matchScore = 0,
        IReadOnlyList<(int Start, int Length)>? matchRanges = null)
    {
        _text = text;
        _description = description;
        _kind = kind;
        _insertText = insertText;
        _caretOffset = caretOffset;
        _selectionLength = selectionLength;
        _typeHint = typeHint;
        _matchScore = matchScore;
        _matchRanges = matchRanges ?? [];
    }

    public IImage? Image => null;

    public string Text => _text;

    /// <summary>匹配区间加粗 + 右侧类型提示的弹出项外观。</summary>
    public object Content
    {
        get
        {
            if (_content is not null)
            {
                return _content;
            }

            var name = new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (_matchRanges.Count == 0)
            {
                name.Text = _text;
            }
            else
            {
                var position = 0;
                foreach (var (start, length) in _matchRanges.OrderBy(range => range.Start))
                {
                    if (start > position)
                    {
                        name.Inlines.Add(_text[position..start]);
                    }

                    name.Inlines.Add(new global::Avalonia.Controls.Documents.Run(_text.Substring(start, length))
                    {
                        FontWeight = FontWeight.SemiBold
                    });
                    position = start + length;
                }

                if (position < _text.Length)
                {
                    name.Inlines.Add(_text[position..]);
                }
            }

            if (string.IsNullOrEmpty(_typeHint))
            {
                _content = name;
                return _content;
            }

            var grid = new DockPanel { LastChildFill = true };
            var hint = new TextBlock
            {
                Text = _typeHint,
                FontSize = 11,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new global::Avalonia.Thickness(10, 0, 2, 0)
            };
            DockPanel.SetDock(hint, Dock.Right);
            grid.Children.Add(hint);
            grid.Children.Add(name);
            _content = grid;
            return _content;
        }
    }

    public object Description => _description;

    /// <summary>匹配质量主导排序(参照 IntelliJ:前缀/词首/连续命中优先),类别作次级权重。</summary>
    public double Priority => _matchScore * 10 + KindRank(_kind);

    private static double KindRank(CompletionKind kind) => kind switch
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

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        var offset = completionSegment.Offset;
        var replacement = _insertText ?? _text;
        var adjustedCaretOffset = _caretOffset;
        if (_insertText is not null)
        {
            var line = textArea.Document.GetLineByOffset(offset);
            var lineText = textArea.Document.GetText(line.Offset, line.Length);
            var indentLength = lineText.TakeWhile(character => character is ' ' or '\t').Count();
            var indent = new string(' ', indentLength);
            replacement = replacement.Replace("\n", "\n" + indent, StringComparison.Ordinal);
            adjustedCaretOffset += indentLength * replacement[..Math.Min(_caretOffset, replacement.Length)]
                .Count(character => character == '\n');
        }

        textArea.Document.Replace(completionSegment, replacement);
        var caret = Math.Clamp(offset + adjustedCaretOffset, 0, textArea.Document.TextLength);
        if (_selectionLength > 0)
        {
            textArea.Selection = Selection.Create(
                textArea,
                caret,
                Math.Min(textArea.Document.TextLength, caret + _selectionLength));
        }
        else
        {
            textArea.Caret.Offset = caret;
        }
    }

    /// <summary>生成带匹配信息的副本(弹出项加粗区间/排序得分/类型提示)。</summary>
    public YyCompletionData WithMatch(
        int matchScore,
        IReadOnlyList<(int Start, int Length)> ranges,
        string? typeHint) =>
        new(_text, _description, _kind, _insertText, _caretOffset, _selectionLength,
            typeHint, matchScore, ranges);

    public override string ToString() => _text;
}
