using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Compiler.Runtime;

public interface IYyFormHost
{
    IReadOnlyDictionary<string, FrameworkElement> YyControls { get; }
    IReadOnlyDictionary<string, FrameworkElement> YyControlsById { get; }
}

public sealed class ScriptRuntimeException(string message) : Exception(message);

public sealed class ScriptBreakSignal : Exception;

public sealed class ScriptContinueSignal : Exception;

public sealed class ScriptEnvironment
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

    public ScriptEnvironment(ScriptEnvironment? parent = null)
    {
        Parent = parent;
    }

    public ScriptEnvironment? Parent { get; }

    public void Define(string name, object? value, bool isConstant = false, string? typeName = null)
    {
        if (_values.ContainsKey(name))
        {
            throw new ScriptRuntimeException($"当前作用域已经存在“{name}”");
        }

        ScriptRuntime.EnsureType(value, typeName, $"变量“{name}”");
        _values[name] = value;
        if (isConstant)
        {
            _constants.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            _types[name] = typeName;
        }
    }

    public object? Get(string name)
    {
        if (_values.TryGetValue(name, out var value))
        {
            return value;
        }

        return Parent?.Get(name)
            ?? throw new ScriptRuntimeException($"变量或函数“{name}”没有定义");
    }

    public bool TryGet(string name, out object? value)
    {
        if (_values.TryGetValue(name, out value))
        {
            return true;
        }

        if (Parent is not null)
        {
            return Parent.TryGet(name, out value);
        }

        value = null;
        return false;
    }

    public void Assign(string name, object? value)
    {
        if (_values.ContainsKey(name))
        {
            if (_constants.Contains(name))
            {
                throw new ScriptRuntimeException($"常量“{name}”不能重新赋值");
            }

            if (_types.TryGetValue(name, out var type))
            {
                ScriptRuntime.EnsureType(value, type, $"变量“{name}”");
            }

            _values[name] = value;
            return;
        }

        if (Parent is null)
        {
            throw new ScriptRuntimeException($"不能给未定义的变量“{name}”赋值");
        }

        Parent.Assign(name, value);
    }
}

public static class ScriptRuntime
{
    public static IYyFormHost? CurrentForm { get; set; }

    public static List<string> OutputLines { get; } = [];

    public static void Output(object? value)
    {
        lock (OutputLines)
        {
            OutputLines.Add(Format(value));
        }
    }

    public static void Prompt(object? value)
    {
        Output("【提示】" + Format(value));
    }

    public static object? GetIdentifier(ScriptEnvironment environment, string name)
    {
        if (environment.TryGet(name, out var value))
        {
            return value;
        }

        if (CurrentForm?.YyControls.TryGetValue(name, out var control) == true)
        {
            return control;
        }

        throw new ScriptRuntimeException($"变量或控件“{name}”没有定义");
    }

    public static object? GetMember(object? target, string member)
    {
        if (target is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(member, out var value))
        {
            return value;
        }

        if (target is IDictionary<string, object?> dictionary &&
            dictionary.TryGetValue(member, out value))
        {
            return value;
        }

        if (target is FrameworkElement element)
        {
            return member switch
            {
                "文本" or "标题" => GetControlText(element),
                "名称" => element.Name,
                "可见" => element.Visibility == Visibility.Visible,
                "启用" => element.IsEnabled,
                "选中" => element is CheckBox { IsChecked: true },
                "左" => Canvas.GetLeft(element),
                "上" => Canvas.GetTop(element),
                "宽" => element.Width,
                "高" => element.Height,
                _ => throw new ScriptRuntimeException($"控件不支持属性“{member}”")
            };
        }

        throw new ScriptRuntimeException($"类型“{TypeName(target)}”不支持属性“{member}”");
    }

    public static void SetMember(object? target, string member, object? value)
    {
        if (target is IDictionary<string, object?> dictionary)
        {
            dictionary[member] = value;
            return;
        }

        if (target is FrameworkElement element)
        {
            switch (member)
            {
                case "文本":
                case "标题":
                    SetControlText(element, Format(value));
                    return;
                case "可见":
                    element.Visibility = Truthy(value) ? Visibility.Visible : Visibility.Hidden;
                    return;
                case "启用":
                    element.IsEnabled = Truthy(value);
                    return;
                case "选中":
                    if (element is CheckBox checkBox)
                    {
                        checkBox.IsChecked = Truthy(value);
                        return;
                    }

                    break;
                case "左":
                    Canvas.SetLeft(element, Number(value));
                    return;
                case "上":
                    Canvas.SetTop(element, Number(value));
                    return;
                case "宽":
                    element.Width = Number(value);
                    return;
                case "高":
                    element.Height = Number(value);
                    return;
            }

            throw new ScriptRuntimeException($"控件不支持设置属性“{member}”");
        }

        throw new ScriptRuntimeException($"类型“{TypeName(target)}”不支持设置属性“{member}”");
    }

    public static object? GetIndex(object? target, object? index)
    {
        return target switch
        {
            List<object?> list => list[Index(index, list.Count)],
            string text => text[Index(index, text.Length)].ToString(),
            IReadOnlyDictionary<string, object?> dictionary =>
                dictionary.TryGetValue(Format(index), out var value)
                    ? value
                    : throw new ScriptRuntimeException($"对象没有键“{Format(index)}”"),
            _ => throw new ScriptRuntimeException($"类型“{TypeName(target)}”不支持索引")
        };
    }

    public static void SetIndex(object? target, object? index, object? value)
    {
        switch (target)
        {
            case List<object?> list:
                list[Index(index, list.Count)] = value;
                return;
            case IDictionary<string, object?> dictionary:
                dictionary[Format(index)] = value;
                return;
            default:
                throw new ScriptRuntimeException($"类型“{TypeName(target)}”不支持通过索引赋值");
        }
    }

    public static object? ApplyBinary(string op, object? left, object? right)
    {
        return op switch
        {
            "+" => Add(left, right),
            "-" => Number(left) - Number(right),
            "*" => Number(left) * Number(right),
            "/" => Divide(left, right),
            "%" => Modulo(left, right),
            "==" or "等于" => Equal(left, right),
            "!=" or "不等于" => !Equal(left, right),
            "<" => Compare(left, right) < 0,
            "<=" => Compare(left, right) <= 0,
            ">" => Compare(left, right) > 0,
            ">=" => Compare(left, right) >= 0,
            _ => throw new ScriptRuntimeException($"不支持运算符“{op}”")
        };
    }

    public static bool And(Func<object?> left, Func<object?> right) =>
        Truthy(left()) && Truthy(right());

    public static bool Or(Func<object?> left, Func<object?> right) =>
        Truthy(left()) || Truthy(right());

    public static object? Coalesce(Func<object?> left, Func<object?> right) =>
        left() ?? right();

    public static object? ReturnValue(object? value, string? typeName, string target)
    {
        EnsureType(value, typeName, target);
        return value;
    }

    public static string[] SplitItems(string text) =>
        text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Not(object? value) => !Truthy(value);

    public static object Negate(object? value) => -Number(value);

    public static object Positive(object? value) => Number(value);

    public static object Add(object? left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber + rightNumber;
        }

        if (left is string || right is string)
        {
            return Format(left) + Format(right);
        }

        throw new ScriptRuntimeException($"类型“{TypeName(left)}”与“{TypeName(right)}”不能相加");
    }

    public static IEnumerable<object?> Enumerate(object? value)
    {
        return value switch
        {
            List<object?> list => list,
            IReadOnlyDictionary<string, object?> dictionary => dictionary.Keys.Cast<object?>(),
            string text => text.Select(character => (object?)character.ToString()),
            IEnumerable enumerable => enumerable.Cast<object?>(),
            _ => throw new ScriptRuntimeException($"类型“{TypeName(value)}”不能遍历")
        };
    }

    public static object? CallBuiltin(string name, object?[] arguments)
    {
        return name switch
        {
            "输出" or "打印" => Print(arguments),
            "提示" => PromptAndReturn(arguments),
            "长度" => (double)(arguments[0] switch
            {
                string text => text.Length,
                ICollection collection => collection.Count,
                _ => throw new ScriptRuntimeException("该值没有长度")
            }),
            "类型" => TypeName(arguments[0]),
            "取控件" => FindControl(Format(arguments[0])),
            "文本" => arguments.Length == 0 ? string.Empty : Format(arguments[0]),
            "数字" => ParseNumber(arguments[0]),
            "整数" => Math.Truncate(ParseNumber(arguments[0])),
            "小数" => Math.Round(
                ParseNumber(arguments[0]),
                arguments.Length > 1 ? ToIndex(arguments[1], int.MaxValue) : 2,
                MidpointRounding.AwayFromZero),
            "绝对值" => Math.Abs(Number(arguments[0])),
            "平方根" => Math.Sqrt(Number(arguments[0])),
            "幂" => Math.Pow(Number(arguments[0]), Number(arguments[1])),
            "最大" => arguments.Max(Number),
            "最小" => arguments.Min(Number),
            "随机数" => arguments.Length == 0
                ? Random.Shared.NextDouble()
                : Random.Shared.Next(
                    arguments.Length == 1 ? 0 : ToIndex(arguments[0], int.MaxValue),
                    ToIndex(arguments[^1], int.MaxValue)),
            "范围" => Range(arguments),
            "包含" => Contains(arguments[0], arguments[1]),
            "分割" => Format(arguments[0]).Split(
                Format(arguments[1]),
                StringSplitOptions.None).Cast<object?>().ToList(),
            "连接" => string.Join(
                arguments.Length > 1 ? Format(arguments[1]) : string.Empty,
                Enumerate(arguments[0]).Select(Format)),
            "替换" => Format(arguments[0]).Replace(
                Format(arguments[1]),
                Format(arguments[2]),
                StringComparison.Ordinal),
            "修剪" => Format(arguments[0]).Trim(),
            "转大写" => Format(arguments[0]).ToUpperInvariant(),
            "转小写" => Format(arguments[0]).ToLowerInvariant(),
            "取子文本" => TakeText(arguments),
            "追加" => Append(arguments),
            "插入" => Insert(arguments),
            "移除" => Remove(arguments),
            "排序" => Sort(arguments[0]),
            "倒序" => Reverse(arguments[0]),
            "键集" => ((IReadOnlyDictionary<string, object?>)arguments[0]!).Keys.Cast<object?>().ToList(),
            "值集" => ((IReadOnlyDictionary<string, object?>)arguments[0]!).Values.ToList(),
            "当前时间" => DateTime.Now,
            "格式化时间" => FormatDate(arguments),
            "JSON解析" => ParseJson(Format(arguments[0])),
            "JSON生成" => JsonSerializer.Serialize(arguments[0], new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            "断言" => Assert(arguments),
            "UTF8编码" => Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(Format(arguments[0]))),
            "UTF8解码" => System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(Format(arguments[0]))),
            _ => throw new ScriptRuntimeException($"未知内置函数“{name}”")
        };
    }

    private static FrameworkElement FindControl(string id)
    {
        if (CurrentForm?.YyControlsById.TryGetValue(id, out var control) == true)
        {
            return control;
        }

        throw new ScriptRuntimeException($"没有找到标识为“{id}”的界面控件");
    }

    public static void EnsureType(object? value, string? typeName, string target)
    {
        if (string.IsNullOrWhiteSpace(typeName) ||
            typeName == "任意" ||
            typeName switch
            {
                "数字" => value is double,
                "文本" => value is string,
                "逻辑" => value is bool,
                "数组" => value is IList,
                "对象" => value is IReadOnlyDictionary<string, object?>,
                "日期" => value is DateTime,
                "空" => value is null,
                _ => false
            })
        {
            return;
        }

        throw new ScriptRuntimeException(
            $"{target}需要“{typeName}”，实际得到“{TypeName(value)}”");
    }

    public static double Number(object? value)
    {
        return value switch
        {
            double number => number,
            int number => number,
            _ => throw new ScriptRuntimeException($"此处需要数字，实际得到“{TypeName(value)}”")
        };
    }

    public static bool Truthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            double number => number != 0,
            string text => text.Length > 0,
            ICollection collection => collection.Count > 0,
            _ => true
        };
    }

    public static bool Equal(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Equals(right);
    }

    public static string Format(object? value)
    {
        return value switch
        {
            null => "空",
            bool boolean => boolean ? "真" : "假",
            double number when number == Math.Truncate(number) =>
                ((long)number).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("G15", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            string text => text,
            IEnumerable enumerable => string.Join(", ", enumerable.Cast<object?>().Select(Format)),
            FrameworkElement element => GetControlText(element),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static string TypeName(object? value)
    {
        return value switch
        {
            null => "空",
            double => "数字",
            string => "文本",
            bool => "逻辑",
            IList => "数组",
            IReadOnlyDictionary<string, object?> => "对象",
            DateTime => "日期",
            FrameworkElement => "控件",
            _ => value.GetType().Name
        };
    }

    public static Brush Brush(string value)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
        }

        return Brushes.Transparent;
    }

    public static Window CreateOutputWindow(string title)
    {
        string output;
        lock (OutputLines)
        {
            output = OutputLines.Count == 0
                ? "程序执行完成，没有输出。"
                : string.Join(Environment.NewLine, OutputLines);
        }

        return new Window
        {
            Title = title,
            Width = 760,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brush("#101318"),
            Content = new TextBox
            {
                Text = output,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(18),
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
                FontSize = 13,
                Background = Brush("#101318"),
                Foreground = Brush("#E1EAF4"),
                BorderThickness = new Thickness(0)
            }
        };
    }

    private static object? Print(object?[] arguments)
    {
        Output(string.Join(" ", arguments.Select(Format)));
        return null;
    }

    private static object? PromptAndReturn(object?[] arguments)
    {
        Prompt(string.Join(" ", arguments.Select(Format)));
        return null;
    }

    private static double Divide(object? left, object? right)
    {
        var denominator = Number(right);
        if (denominator == 0)
        {
            throw new ScriptRuntimeException("不能除以零");
        }

        return Number(left) / denominator;
    }

    private static double Modulo(object? left, object? right)
    {
        var denominator = Number(right);
        if (denominator == 0)
        {
            throw new ScriptRuntimeException("不能对零取余");
        }

        return Number(left) % denominator;
    }

    private static int Compare(object? left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.Ordinal);
        }

        throw new ScriptRuntimeException("这两个值不能比较大小");
    }

    private static double ParseNumber(object? value)
    {
        if (double.TryParse(
                Format(value),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return result;
        }

        throw new ScriptRuntimeException($"“{Format(value)}”不是有效数字");
    }

    private static int ToIndex(object? value, int maximum)
    {
        var number = Number(value);
        if (number != Math.Truncate(number) || number < 0 || number > maximum)
        {
            throw new ScriptRuntimeException("索引超出有效范围");
        }

        return (int)number;
    }

    private static int Index(object? value, int count)
    {
        var index = ToIndex(value, count - 1);
        return index;
    }

    private static List<object?> Range(object?[] arguments)
    {
        var start = arguments.Length == 1 ? 0 : Number(arguments[0]);
        var end = Number(arguments.Length == 1 ? arguments[0] : arguments[1]);
        var step = arguments.Length > 2 ? Number(arguments[2]) : 1;
        if (step == 0)
        {
            throw new ScriptRuntimeException("范围步长不能为 0");
        }

        var result = new List<object?>();
        for (var value = start; step > 0 ? value < end : value > end; value += step)
        {
            if (result.Count >= 1_000_000)
            {
                throw new ScriptRuntimeException("范围超过一百万项");
            }

            result.Add(value);
        }

        return result;
    }

    private static bool Contains(object? collection, object? value)
    {
        return collection switch
        {
            string text => text.Contains(Format(value), StringComparison.Ordinal),
            List<object?> list => list.Contains(value),
            _ => false
        };
    }

    private static string TakeText(object?[] arguments)
    {
        var text = Format(arguments[0]);
        var start = ToIndex(arguments[1], text.Length);
        var length = arguments.Length > 2
            ? ToIndex(arguments[2], text.Length - start)
            : text.Length - start;
        return text.Substring(start, length);
    }

    private static object? Append(object?[] arguments)
    {
        var list = (List<object?>)arguments[0]!;
        list.AddRange(arguments.Skip(1));
        return list;
    }

    private static object? Insert(object?[] arguments)
    {
        var list = (List<object?>)arguments[0]!;
        list.Insert(ToIndex(arguments[1], list.Count), arguments[2]);
        return list;
    }

    private static object? Remove(object?[] arguments)
    {
        var list = (List<object?>)arguments[0]!;
        var index = ToIndex(arguments[1], list.Count - 1);
        var value = list[index];
        list.RemoveAt(index);
        return value;
    }

    private static object? Sort(object? value)
    {
        var list = (List<object?>)value!;
        list.Sort((left, right) => Compare(left, right));
        return list;
    }

    private static object? Reverse(object? value)
    {
        var list = (List<object?>)value!;
        list.Reverse();
        return list;
    }

    private static string FormatDate(object?[] arguments)
    {
        if (arguments[0] is not DateTime dateTime)
        {
            throw new ScriptRuntimeException("格式化时间需要日期时间值");
        }

        return dateTime.ToString(
            arguments.Length > 1 ? Format(arguments[1]) : "yyyy/MM/dd HH:mm:ss",
            CultureInfo.InvariantCulture);
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
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJson(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static object? Assert(object?[] arguments)
    {
        if (!Truthy(arguments[0]))
        {
            throw new ScriptRuntimeException(
                arguments.Length > 1 ? Format(arguments[1]) : "断言失败");
        }

        return true;
    }

    private static string GetControlText(FrameworkElement element)
    {
        return element switch
        {
            TextBox textBox => textBox.Text,
            Button button => button.Content?.ToString() ?? string.Empty,
            TextBlock textBlock => textBlock.Text,
            CheckBox checkBox => checkBox.Content?.ToString() ?? string.Empty,
            GroupBox groupBox => groupBox.Header?.ToString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static void SetControlText(FrameworkElement element, string text)
    {
        switch (element)
        {
            case TextBox textBox:
                textBox.Text = text;
                break;
            case Button button:
                button.Content = text;
                break;
            case TextBlock textBlock:
                textBlock.Text = text;
                break;
            case CheckBox checkBox:
                checkBox.Content = text;
                break;
            case GroupBox groupBox:
                groupBox.Header = text;
                break;
        }
    }
}
