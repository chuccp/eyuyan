namespace Yy.Studio.Core.Completion;

/// <summary>
/// 补全匹配器(参照 IntelliJ MinusculeMatcher/CamelHumpMatcher 语义):
/// 词首(驼峰大写、下划线、每个汉字)优先强匹配,支持子序列模糊匹配;
/// 返回命中区间(供弹出项加粗)与匹配得分(供排序)。ASCII 大小写不敏感。
/// </summary>
public static class CompletionMatcher
{
    public readonly record struct MatchResult(int Score, IReadOnlyList<(int Start, int Length)> Ranges);

    /// <summary>pattern 为空视为全部命中;未命中返回 null。</summary>
    public static MatchResult? Match(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return new MatchResult(0, []);
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var score = 0;
        var ranges = new List<(int Start, int Length)>();
        var nameIndex = 0;
        var consecutive = 0;
        var previousEnd = -1;

        foreach (var patternChar in pattern)
        {
            var found = -1;
            for (var index = nameIndex; index < name.Length; index++)
            {
                if (CharsEqual(patternChar, name[index]))
                {
                    found = index;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            score += 1;
            if (IsWordStart(name, found))
            {
                score += 6;
            }

            if (found == previousEnd)
            {
                consecutive++;
                score += 3 + Math.Min(consecutive, 4);
            }
            else
            {
                consecutive = 0;
            }

            if (ranges.Count > 0 &&
                ranges[^1].Start + ranges[^1].Length == found)
            {
                var last = ranges[^1];
                ranges[^1] = (last.Start, last.Length + 1);
            }
            else
            {
                ranges.Add((found, 1));
            }

            previousEnd = found + 1;
            nameIndex = found + 1;
        }

        // 前缀命中是强信号(参照 isStartMatch):整词前缀得高分。
        if (name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (IsWordStart(name, ranges[0].Start))
        {
            score += 16;
        }

        // 更短的名字更接近输入(同分时优先)。
        score += Math.Max(0, 12 - Math.Min(12, name.Length - pattern.Length));
        return new MatchResult(score, ranges);
    }

    /// <summary>补全光标处的输入前缀。</summary>
    public static string WordBefore(string text, int caretOffset)
    {
        var start = Math.Max(0, caretOffset);
        while (start > 0)
        {
            var ch = text[start - 1];
            if (char.IsLetterOrDigit(ch) || ch == '_' || IsCjk(ch))
            {
                start--;
            }
            else
            {
                break;
            }
        }

        return text[start..Math.Min(text.Length, caretOffset)];
    }

    private static bool CharsEqual(char patternChar, char nameChar) =>
        patternChar == nameChar ||
        char.ToUpperInvariant(patternChar) == char.ToUpperInvariant(nameChar);

    private static bool IsWordStart(string name, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var current = name[index];
        var previous = name[index - 1];
        if (previous is '_' or '-' or '.' or ' ')
        {
            return true;
        }

        // 驼峰词首:小写/数字 → 大写。
        if (char.IsUpper(current) && !char.IsUpper(previous) && !char.IsDigit(previous))
        {
            return true;
        }

        // 每个汉字/全角字符自成词。
        return IsCjk(current) && !IsCjk(previous);
    }

    private static bool IsCjk(char ch) =>
        ch >= 0x2E80 && ch <= 0x9FFF;
}
