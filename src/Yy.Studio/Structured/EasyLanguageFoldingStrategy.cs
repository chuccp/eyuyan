using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Yy.Studio.Structured;

/// <summary>按易语块关键字生成代码折叠区域，保持旧版易语言编辑器左侧折叠感。</summary>
public static class EasyLanguageFoldingStrategy
{
    private sealed record FoldingStart(int Offset, string EndMarker, string Name);

    public static IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var stack = new Stack<FoldingStart>();
        var foldings = new List<NewFolding>();
        for (var number = 1; number <= document.LineCount; number++)
        {
            var line = document.GetLineByNumber(number);
            var text = document.GetText(line).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var start = GetStart(text, line.Offset);
            if (start is not null)
            {
                stack.Push(start);
                continue;
            }

            var endMarker = EndMarkerFor(text);
            if (endMarker is null)
            {
                continue;
            }

            while (stack.Count > 0 &&
                   !string.Equals(stack.Peek().EndMarker, endMarker, StringComparison.Ordinal))
            {
                // 容错处理：未闭合的内层块不会吞掉后续外层块。
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                continue;
            }

            var foldingStart = stack.Pop();
            foldings.Add(new NewFolding(foldingStart.Offset, line.EndOffset)
            {
                Name = foldingStart.Name,
                IsDefinition = true
            });
        }

        return foldings;
    }

    private static FoldingStart? GetStart(string text, int offset)
    {
        if (text.StartsWith("子程序 ", StringComparison.Ordinal) ||
            text.StartsWith("函数 ", StringComparison.Ordinal))
        {
            return new FoldingStart(offset, "结束子程序", text);
        }

        if (text.StartsWith("如果", StringComparison.Ordinal) &&
            !text.StartsWith("否则如果", StringComparison.Ordinal))
        {
            return new FoldingStart(offset, "结束如果", text);
        }

        if (text.StartsWith("当", StringComparison.Ordinal) ||
            text.StartsWith("计次循环", StringComparison.Ordinal))
        {
            return new FoldingStart(offset, "结束循环", text);
        }

        if (text.StartsWith("遍历", StringComparison.Ordinal))
        {
            return new FoldingStart(offset, "结束遍历", text);
        }

        return text.StartsWith("尝试", StringComparison.Ordinal)
            ? new FoldingStart(offset, "结束尝试", text)
            : null;
    }

    private static string? EndMarkerFor(string text) => text switch
    {
        _ when text.StartsWith("结束子程序", StringComparison.Ordinal) ||
               text.StartsWith("结束函数", StringComparison.Ordinal) => "结束子程序",
        _ when text.StartsWith("结束如果", StringComparison.Ordinal) => "结束如果",
        _ when text.StartsWith("结束循环", StringComparison.Ordinal) => "结束循环",
        _ when text.StartsWith("结束遍历", StringComparison.Ordinal) => "结束遍历",
        _ when text.StartsWith("结束尝试", StringComparison.Ordinal) => "结束尝试",
        _ => null
    };
}
