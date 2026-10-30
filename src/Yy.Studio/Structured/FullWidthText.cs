namespace Yy.Studio.Structured;

/// <summary>
/// 表格编辑器的全角显示⇄半角存储归一化(官方代码行显示全角运算符,如 标签1.标题 ＝ "…"）。
/// 词法分析仅接受半角运算符,编辑确认边界必须用 FromDisplay 转回。
/// </summary>
public static class FullWidthText
{
    private static readonly (string Half, string Full)[] Pairs =
    [
        ("<>", "＜＞"),
        ("<=", "＜＝"),
        (">=", "＞＝"),
        ("=", "＝"),
        ("+", "＋"),
        ("-", "－"),
        ("*", "＊"),
        ("/", "／"),
        ("(", "（"),
        (")", "）"),
        ("<", "＜"),
        (">", "＞")
    ];

    public static string ToDisplay(string text)
    {
        var result = text ?? string.Empty;
        foreach (var (half, full) in Pairs)
        {
            result = result.Replace(half, full, StringComparison.Ordinal);
        }

        return result;
    }

    public static string FromDisplay(string text)
    {
        var result = text ?? string.Empty;
        foreach (var (half, full) in Pairs)
        {
            result = result.Replace(full, half, StringComparison.Ordinal);
        }

        return result;
    }
}
