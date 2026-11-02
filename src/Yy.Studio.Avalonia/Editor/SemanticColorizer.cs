using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 对齐官方易语言的语义着色:常量 #xxx 紫色,用户子程序调用红色。
/// 关键字/内置命令/字符串由语法定义负责,这里只补动态名字。
/// </summary>
public sealed partial class SemanticColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush ConstantBrush =
        new SolidColorBrush(Color.FromRgb(128, 0, 128));

    private static readonly IBrush CallBrush =
        new SolidColorBrush(Color.FromRgb(192, 0, 0));

    private readonly Func<IReadOnlyList<string>> _functionNames;
    private readonly Func<IReadOnlyList<string>> _constantNames;

    public SemanticColorizer(
        Func<IReadOnlyList<string>> functionNames,
        Func<IReadOnlyList<string>>? constantNames = null)
    {
        _functionNames = functionNames;
        _constantNames = constantNames ?? (() => []);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext?.Document.GetText(line);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match match in ConstantRegex().Matches(text))
        {
            Colorize(line, match, ConstantBrush);
        }

        var constants = _constantNames();
        foreach (Match match in WordRegex().Matches(text))
        {
            if (constants.Contains(match.Value, StringComparer.Ordinal))
            {
                Colorize(line, match, ConstantBrush);
            }
        }

        var names = _functionNames();
        if (names.Count == 0)
        {
            return;
        }

        foreach (Match match in WordRegex().Matches(text))
        {
            if (names.Contains(match.Value, StringComparer.Ordinal))
            {
                // 仅当后面跟着调用括号时视为命令调用(与官方一致)。
                var after = line.Offset + match.Index + match.Length;
                var document = CurrentContext!.Document;
                var probe = Math.Min(after + 2, document.TextLength);
                var tail = document.GetText(after, probe - after).TrimStart();
                if (tail.StartsWith('(') || tail.StartsWith('（'))
                {
                    Colorize(line, match, CallBrush);
                }
            }
        }
    }

    private void Colorize(DocumentLine line, Match match, IBrush brush) =>
        ChangeLinePart(
            line.Offset + match.Index,
            line.Offset + match.Index + match.Length,
            element => element.TextRunProperties.SetForegroundBrush(brush));

    [GeneratedRegex(@"#[^\s()\[\]{}，。、""'＋+＝=]+")]
    private static partial Regex ConstantRegex();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*|[一-鿿]+")]
    private static partial Regex WordRegex();
}
