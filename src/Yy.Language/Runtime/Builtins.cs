using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Yy.Language.Runtime;

internal static class Builtins
{
    public static IReadOnlyCollection<string> FunctionNames { get; } =
    [
        "输出", "打印", "提示", "断言", "长度", "类型", "文本", "数字", "整数", "小数",
        "绝对值", "平方根", "幂", "最大", "最小", "随机数", "范围", "包含", "分割", "连接",
        "替换", "修剪", "转大写", "转小写", "取子文本", "追加", "插入", "移除", "排序",
        "倒序", "键集", "值集", "当前时间", "格式化时间", "JSON解析", "JSON生成",
        "UTF8编码", "UTF8解码"
    ];

    public static void Install(Environment globals, Action<object?> write, CancellationToken cancellationToken)
    {
        Add("输出", 0, int.MaxValue, args =>
        {
            write(string.Join(" ", args.Select(Interpreter.Format)));
            return null;
        });
        Add("打印", 0, int.MaxValue, args =>
        {
            write(string.Join(" ", args.Select(Interpreter.Format)));
            return null;
        });
        Add("提示", 1, int.MaxValue, args =>
        {
            write("【提示】" + string.Join(" ", args.Select(Interpreter.Format)));
            return null;
        });
        Add("断言", 1, 2, args =>
        {
            if (!Interpreter.IsTruthy(args[0]))
            {
                throw new YyRuntimeError(
                    args.Length > 1 ? Interpreter.Format(args[1]) : "断言失败",
                    1,
                    1);
            }

            return true;
        });

        Add("长度", 1, 1, args => (double)(args[0] switch
        {
            string text => text.Length,
            List<object?> list => list.Count,
            Dictionary<string, object?> dictionary => dictionary.Count,
            _ => throw new YyRuntimeError($"类型“{Interpreter.TypeName(args[0])}”没有长度", 1, 1)
        }));
        Add("类型", 1, 1, args => Interpreter.TypeName(args[0]));
        Add("文本", 0, 1, args => args.Length == 0 ? string.Empty : Interpreter.Format(args[0]));
        Add("数字", 1, 1, args => ToNumber(args[0]));
        Add("整数", 1, 1, args => Math.Truncate(ToNumber(args[0])));
        Add("小数", 1, 2, args =>
        {
            var value = ToNumber(args[0]);
            var places = args.Length > 1 ? Interpreter.RequireIndex(args[1], 1, 1) : 2;
            return Math.Round(value, places, MidpointRounding.AwayFromZero);
        });

        Add("绝对值", 1, 1, args => Math.Abs(Interpreter.RequireNumber(args[0], 1, 1)));
        Add("平方根", 1, 1, args =>
        {
            var number = Interpreter.RequireNumber(args[0], 1, 1);
            if (number < 0)
            {
                throw new YyRuntimeError("负数不能求平方根", 1, 1);
            }

            return Math.Sqrt(number);
        });
        Add("幂", 2, 2, args =>
            Math.Pow(
                Interpreter.RequireNumber(args[0], 1, 1),
                Interpreter.RequireNumber(args[1], 1, 1)));
        Add("最大", 1, int.MaxValue, args => args.Max(value => Interpreter.RequireNumber(value, 1, 1)));
        Add("最小", 1, int.MaxValue, args => args.Min(value => Interpreter.RequireNumber(value, 1, 1)));
        Add("随机数", 0, 2, args =>
        {
            if (args.Length == 0)
            {
                return Random.Shared.NextDouble();
            }

            var minimum = args.Length == 1 ? 0 : Interpreter.RequireIndex(args[0], 1, 1);
            var maximum = Interpreter.RequireIndex(args[^1], 1, 1);
            if (minimum > maximum)
            {
                throw new YyRuntimeError("随机数下限不能大于上限", 1, 1);
            }

            return (double)Random.Shared.Next(minimum, maximum + 1);
        });
        Add("范围", 1, 3, args => Range(args, cancellationToken));

        Add("包含", 2, 2, args => Contains(args[0], args[1]));
        Add("分割", 2, 2, args =>
        {
            var text = RequireString(args[0], "分割");
            var separator = RequireString(args[1], "分割");
            if (separator.Length == 0)
            {
                throw new YyRuntimeError("分割符不能为空", 1, 1);
            }

            return text.Split(separator, StringSplitOptions.None)
                .Select(part => (object?)part)
                .ToList();
        });
        Add("连接", 1, 2, args =>
        {
            var separator = args.Length > 1 ? Interpreter.Format(args[1]) : string.Empty;
            return string.Join(separator, ToObjects(args[0]).Select(Interpreter.Format));
        });
        Add("替换", 3, 3, args =>
            RequireString(args[0], "替换").Replace(
                RequireString(args[1], "替换"),
                RequireString(args[2], "替换"),
                StringComparison.Ordinal));
        Add("修剪", 1, 1, args => RequireString(args[0], "修剪").Trim());
        Add("转大写", 1, 1, args => RequireString(args[0], "转大写").ToUpperInvariant());
        Add("转小写", 1, 1, args => RequireString(args[0], "转小写").ToLowerInvariant());
        Add("取子文本", 2, 3, args =>
        {
            var text = RequireString(args[0], "取子文本");
            var start = Interpreter.RequireIndex(args[1], 1, 1);
            var length = args.Length > 2 ? Interpreter.RequireIndex(args[2], 1, 1) : text.Length - start;
            if (start < 0 || start > text.Length || length < 0 || start + length > text.Length)
            {
                throw new YyRuntimeError("子文本范围越界", 1, 1);
            }

            return text.Substring(start, length);
        });

        Add("追加", 2, int.MaxValue, args =>
        {
            var list = RequireList(args[0], "追加");
            list.AddRange(args.Skip(1));
            return list;
        });
        Add("插入", 3, 3, args =>
        {
            var list = RequireList(args[0], "插入");
            var index = Interpreter.RequireIndex(args[1], 1, 1);
            if (index < 0 || index > list.Count)
            {
                throw new YyRuntimeError("插入位置越界", 1, 1);
            }

            list.Insert(index, args[2]);
            return list;
        });
        Add("移除", 2, 2, args =>
        {
            var list = RequireList(args[0], "移除");
            var index = Interpreter.RequireIndex(args[1], 1, 1);
            if (index < 0 || index >= list.Count)
            {
                throw new YyRuntimeError("移除位置越界", 1, 1);
            }

            var value = list[index];
            list.RemoveAt(index);
            return value;
        });
        Add("排序", 1, 1, args =>
        {
            var list = RequireList(args[0], "排序");
            list.Sort(CompareValues);
            return list;
        });
        Add("倒序", 1, 1, args =>
        {
            var list = RequireList(args[0], "倒序");
            list.Reverse();
            return list;
        });
        Add("键集", 1, 1, args =>
            RequireDictionary(args[0], "键集").Keys.Cast<object?>().ToList());
        Add("值集", 1, 1, args =>
            RequireDictionary(args[0], "值集").Values.ToList());

        Add("当前时间", 0, 0, _ => DateTime.Now);
        Add("格式化时间", 1, 2, args =>
        {
            if (args[0] is not DateTime dateTime)
            {
                throw new YyRuntimeError("格式化时间需要日期时间值", 1, 1);
            }

            var pattern = args.Length > 1 ? Interpreter.Format(args[1]) : "yyyy/MM/dd HH:mm:ss";
            return dateTime.ToString(pattern, CultureInfo.InvariantCulture);
        });

        Add("JSON解析", 1, 1, args => ParseJson(RequireString(args[0], "JSON解析")));
        Add("JSON生成", 1, 1, args =>
            JsonSerializer.Serialize(args[0], new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

        Add("UTF8编码", 1, 1, args =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Interpreter.Format(args[0]))));
        Add("UTF8解码", 1, 1, args =>
            Encoding.UTF8.GetString(Convert.FromBase64String(Interpreter.Format(args[0]))));

        void Add(string name, int minimumArguments, int maximumArguments, Func<object?[], object?> invoke)
        {
            globals.Define(name, new NativeFunction(name, minimumArguments, maximumArguments, invoke));
        }
    }

    private static double ToNumber(object? value)
    {
        if (value is double number)
        {
            return number;
        }

        var text = Interpreter.Format(value).Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new YyRuntimeError($"“{text}”不是有效数字", 1, 1);
    }

    private static List<object?> Range(object?[] args, CancellationToken cancellationToken)
    {
        var start = Interpreter.RequireNumber(args[0], 1, 1);
        var end = args.Length > 1 ? Interpreter.RequireNumber(args[1], 1, 1) : start;
        if (args.Length == 1)
        {
            start = 0;
        }

        var step = args.Length > 2 ? Interpreter.RequireNumber(args[2], 1, 1) : 1;
        if (step == 0)
        {
            throw new YyRuntimeError("范围步长不能为 0", 1, 1);
        }

        var result = new List<object?>();
        var count = 0;
        for (var value = start; step > 0 ? value < end : value > end; value += step)
        {
            if (++count > 1_000_000)
            {
                throw new YyRuntimeError("范围元素超过一百万，已终止", 1, 1);
            }

            if ((count & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            result.Add(value);
        }

        return result;
    }

    private static bool Contains(object? collection, object? sought)
    {
        return collection switch
        {
            string text => text.Contains(Interpreter.Format(sought), StringComparison.Ordinal),
            List<object?> list => list.Any(item => Interpreter.ValuesEqual(item, sought)),
            Dictionary<string, object?> dictionary => dictionary.ContainsKey(Interpreter.Format(sought)),
            _ => throw new YyRuntimeError(
                $"类型“{Interpreter.TypeName(collection)}”不支持“包含”",
                1,
                1)
        };
    }

    private static string RequireString(object? value, string function)
    {
        if (value is string text)
        {
            return text;
        }

        throw new YyRuntimeError($"函数“{function}”需要文本参数", 1, 1);
    }

    private static List<object?> RequireList(object? value, string function)
    {
        if (value is List<object?> list)
        {
            return list;
        }

        throw new YyRuntimeError($"函数“{function}”的第一个参数需要是数组", 1, 1);
    }

    private static Dictionary<string, object?> RequireDictionary(object? value, string function)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        throw new YyRuntimeError($"函数“{function}”需要对象参数", 1, 1);
    }

    private static IEnumerable<object?> ToObjects(object? value)
    {
        return value switch
        {
            List<object?> list => list,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object?>(),
            _ => throw new YyRuntimeError($"类型“{Interpreter.TypeName(value)}”不能作为集合使用", 1, 1)
        };
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.Ordinal);
        }

        throw new YyRuntimeError("数组中的元素类型无法比较", 1, 1);
    }

    private static object? ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ConvertJson(document.RootElement);
    }

    private static object? ConvertJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJson(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
