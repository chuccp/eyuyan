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

    public static IReadOnlyCollection<string> KnownTypes => DisplayNames.Values;

    public static bool IsKnown(string name) => DisplayNames.ContainsKey(name);

    public static string CanonicalName(string name) => DisplayNames[name];

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
            _ => false
        };
    }

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
