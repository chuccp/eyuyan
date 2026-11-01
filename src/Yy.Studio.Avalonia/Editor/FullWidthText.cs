namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 全角显示与半角存储之间的归一化。易语词法只接受半角括号和运算符，
/// 表格和经典视图可以在界面上显示全角字符，提交前统一转回半角。
/// </summary>
public static class FullWidthText
{
    public static string ToDisplay(string? text)
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

    public static string FromDisplay(string? text)
    {
        // 除括号/引号外，兼容旧草稿和中文输入法残留的全角运算符。
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
