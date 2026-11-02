using System.Collections;

namespace Yy.Language;

public static class YyTypeSystem
{
    private static readonly Dictionary<string, string> DisplayNames =
        new(StringComparer.Ordinal)
        {
            ["任意"] = "任意",
            ["数字"] = "数字",
            ["文本"] = "文本",
            ["逻辑"] = "逻辑",
            ["数组"] = "数组",
            ["对象"] = "对象",
            ["日期"] = "日期",
            ["空"] = "空"
        };

    // 官方易语言类型名映射到规范名(强类型词汇)。
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.Ordinal)
        {
            ["整数型"] = "数字",
            ["短整数型"] = "数字",
            ["长整数型"] = "数字",
            ["小数型"] = "数字",
            ["双精度小数型"] = "数字",
            ["字节型"] = "数字",
            ["文本型"] = "文本",
            ["逻辑型"] = "逻辑",
            ["字节集"] = "任意",
            ["日期时间型"] = "日期",
            ["数组型"] = "数组",
            ["对象型"] = "对象"
        };

    public static IReadOnlyCollection<string> KnownTypes => DisplayNames.Values;

    public static bool IsKnown(string name) =>
        DisplayNames.ContainsKey(name) || Aliases.ContainsKey(name);

    public static string CanonicalName(string name) =>
        DisplayNames.TryGetValue(name, out var canonical)
            ? canonical
            : Aliases[name];

    public static bool Matches(object? value, string typeName)
    {
        return typeName switch
        {
            "任意" => true,
            "数字" => value is double,
            "文本" => value is string,
            "逻辑" => value is bool,
            "数组" => value is IList,
            "对象" => value is IReadOnlyDictionary<string, object?>,
            "日期" => value is DateTime,
            "空" => value is null,
            _ => value is IReadOnlyDictionary<string, object?>
        };
    }

    /// <summary>无初始值声明(老语法 .局部变量 行)的默认值。</summary>
    public static object? DefaultValue(string? typeName) =>
        CanonicalName(typeName ?? "任意") switch
        {
            "数字" => 0d,
            "文本" => string.Empty,
            "逻辑" => false,
            "数组" => new List<object?>(),
            _ => null
        };

    public static void Ensure(
        object? value,
        string? typeName,
        int line,
        int column,
        string target)
    {
        if (string.IsNullOrWhiteSpace(typeName) || Matches(value, typeName))
        {
            return;
        }

        throw new YyRuntimeError(
            $"{target}需要“{typeName}”，实际得到“{Runtime.Interpreter.TypeName(value)}”",
            line,
            column,
            "请在赋值的每一步保持类型一致，或显式转换。");
    }
}
