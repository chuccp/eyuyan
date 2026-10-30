using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace Yy.Studio.Structured;

/// <summary>
/// 易语代码窗口的缩进规则：子程序、条件、循环和异常块自动增加一级，
/// 结束标记和中间分支自动回退一级。
/// </summary>
public sealed class YyIndentationStrategy : IIndentationStrategy
{
    private const int IndentSize = 4;

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var previous = line.PreviousLine;
        while (previous is not null &&
               string.IsNullOrWhiteSpace(document.GetText(previous)))
        {
            previous = previous.PreviousLine;
        }

        var indent = previous is null
            ? 0
            : LeadingSpaces(document.GetText(previous));
        if (previous is not null)
        {
            var previousCode = document.GetText(previous).Trim();
            if (StartsIndentedBlock(previousCode))
            {
                indent += IndentSize;
            }
            else if (StartsDedentedBlock(previousCode))
            {
                indent = Math.Max(0, indent - IndentSize);
            }
        }

        var text = document.GetText(line);
        var leading = text.Length - text.TrimStart().Length;
        document.Replace(
            line.Offset,
            leading,
            new string(' ', indent));
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        for (var number = beginLine; number <= endLine; number++)
        {
            if (number >= 1 && number <= document.LineCount)
            {
                IndentLine(document, document.GetLineByNumber(number));
            }
        }
    }

    private static int LeadingSpaces(string text)
    {
        var count = 0;
        while (count < text.Length && text[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static bool StartsIndentedBlock(string text) =>
        text.StartsWith("子程序 ", StringComparison.Ordinal) ||
        text.StartsWith("函数 ", StringComparison.Ordinal) ||
        text.StartsWith("如果", StringComparison.Ordinal) ||
        text.StartsWith("否则", StringComparison.Ordinal) ||
        text.StartsWith("当", StringComparison.Ordinal) ||
        text.StartsWith("计次循环", StringComparison.Ordinal) ||
        text.StartsWith("遍历", StringComparison.Ordinal) ||
        text.StartsWith("尝试", StringComparison.Ordinal) ||
        text.StartsWith("捕获", StringComparison.Ordinal);

    private static bool StartsDedentedBlock(string text) =>
        text.StartsWith("结束", StringComparison.Ordinal);
}
