using System.ComponentModel;
using System.Globalization;

namespace Yy.Studio.Properties;

public enum PropertyCellKind
{
    Text,
    Integer,
    Bool,
    Color,
    Identifier
}

/// <summary>属性夹中的一行属性(名称/值/编辑方式/提示说明)。</summary>
public sealed class PropertyRow : INotifyPropertyChanged
{
    private string _valueText = string.Empty;

    public PropertyRow(string name, string valueText, PropertyCellKind kind, string description, bool enabled = true)
    {
        Name = name;
        _valueText = valueText;
        InitialValue = valueText;
        Kind = kind;
        Description = description;
        IsEnabled = enabled;
    }

    public string Name { get; }

    /// <summary>
    /// 构建行时的模型值。模板实现化会给组合框补发 SelectionChanged,
    /// 值未变化的提交应跳过(否则误标脏、并可能引发重建递归)。
    /// </summary>
    public string InitialValue { get; set; }

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value)
            {
                return;
            }

            _valueText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
        }
    }

    public PropertyCellKind Kind { get; }

    public string Description { get; }

    public bool IsEnabled { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>属性行目录与官方属性提示文案(选中属性行时在提示夹显示)。</summary>
public static class PropertyRowCatalog
{
    public static readonly string[] TrueFalse = ["真", "假"];

    /// <summary>颜色属性行的常用色板(官方颜色选择器下拉)。</summary>
    public static readonly string[] ColorPresets =
    [
        "#000000", "#FFFFFF", "#C0C0C0", "#808080",
        "#FF0000", "#008000", "#0000FF", "#FFFF00",
        "#800080", "#008080", "#FF8000", "#FF00FF",
        "#F4F7FB", "#D4D0C8", "#ECE9D8", "#0A246A",
        "#316AC5", "#C0392B", "#006000", "#FFFFE1"
    ];

    public const string HintName = "本属性记录当前所选择对象的名称。";
    public const string HintRemark = "本属性记录当前所选择对象的备注说明。";
    public const string HintTitle = "本属性指定显示在对象上的文字内容。";
    public const string HintLeft = "本属性指定对象左边缘到窗口左边框的距离。";
    public const string HintTop = "本属性指定对象上边缘到窗口上边框的距离。";
    public const string HintWidth = "本属性指定对象的宽度。";
    public const string HintHeight = "本属性指定对象的高度。";
    public const string HintVisible = "本属性指定对象在程序运行时是否可见。";
    public const string HintForbidden = "本属性指定对象在程序运行时是否被禁止操作。";
    public const string HintFontSize = "本属性指定对象上文字的大小。";
    public const string HintForeground = "本属性指定对象的前景(文字)颜色。";
    public const string HintBackground = "本属性指定对象的背景颜色。";
    public const string HintChecked = "本属性指定对象初始是否处于选中状态。";
    public const string HintDataBinding = "本属性把控件文本绑定到数据表列,格式:表名.列名。窗体预览时从工作区“数据.json”读取首行填充。";
    public const string HintReadOnly = "本属性指定是否禁止用户编辑文本内容。为“真”时文本仅用于显示。";
    public const string HintMultiLine = "本属性设置本组件是否允许输入多行文本。";
    public const string HintPassword = "本属性设置当输入方式为“密码输入”时输入字符后显示在输入框中的替代字符。为空则正常显示。";
    public const string HintMaxLength = "本属性设置最大允许输入到编辑框中的字符数目,如为0则输入字符数目不受限制。";
    public const string HintPicture = "本属性指定图片框中显示的图片文件路径;也可在代码中用 图片框.图片 = \"路径\" 动态更换。";
    public const string HintBorderStyle = "本属性指定窗口边框类型:可调边框/对话框边框/无边框。";
    public const string HintMovable = "本属性指定窗口在运行时是否可以被鼠标拖动移动。";
    public const string HintStartPosition = "本属性指定窗口启动后的初始位置:默认/居中显示/最小化/最大化。";
    public const string HintMinBox = "本属性指定窗口标题栏中是否显示最小化按钮。";
    public const string HintMaxBox = "本属性指定窗口标题栏中是否显示最大化按钮。";

    public static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string BoolText(bool value) => value ? "真" : "假";

    public static bool ParseBool(string text) => text.Trim() == "真";
}

/// <summary>颜色属性行色板用:十六进制颜色文本转画刷。</summary>
public sealed class HexToBrushConverter : System.Windows.Data.IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            try
            {
                var brush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text)!);
                brush.Freeze();
                return brush;
            }
            catch (FormatException)
            {
                // 非法颜色文本按灰色处理。
            }
        }

        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
