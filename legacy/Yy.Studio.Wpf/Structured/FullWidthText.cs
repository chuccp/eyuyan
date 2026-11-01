namespace Yy.Studio.Structured;

/// <summary>
/// 表格编辑器的全角显示⇄半角存储归一化。对齐官方:仅括号/引号显示全角
/// (信息框（“你好”，0，)),运算符与逗号保持半角,词法分析只接受半角存储。
/// </summary>
public static class FullWidthText
{
    public static string ToDisplay(string text)
    {
        var result = (text ?? string.Empty)
            .Replace("(", "（", StringComparison.Ordinal)
            .Replace(")", "）", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder(result.Length + 4);
        var open = true;
        foreach (var character in result)
        {
            if (character == '"')
            {
                builder.Append(open ? '“' : '”');
                open = !open;
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string FromDisplay(string text)
    {
        // 除括号/引号外,兼容旧草稿里残留的全角运算符,一并归回半角。
        var result = text ?? string.Empty;
        foreach (var (half, full) in LegacyPairs)
        {
            result = result.Replace(full, half, StringComparison.Ordinal);
        }

        return result;
    }

    private static readonly (string Half, string Full)[] LegacyPairs =
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
        (">", "＞"),
        ("\"", "“"),
        ("\"", "”")
    ];
}
