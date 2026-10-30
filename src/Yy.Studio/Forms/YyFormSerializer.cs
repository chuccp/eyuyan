using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Yy.Studio.Forms;

public static class YyFormSerializer
{
    private static readonly XNamespace Namespace = "urn:yy-studio:form:1";

    public static string CreateEmptyDocument(string title = "易语窗体")
    {
        return Serialize(new YyFormModel { Title = title });
    }

    public static string Serialize(YyFormModel form)
    {
        var root = new XElement(
            Namespace + "窗体",
            new XAttribute(XNamespace.Xmlns + "yy", Namespace),
            new XAttribute("标识", YyResourceIndex.FormId(form.Name)),
            new XAttribute("名称", form.Name),
            new XAttribute("标题", form.Title),
            new XAttribute("宽", Format(form.Width)),
            new XAttribute("高", Format(form.Height)),
            new XAttribute("背景", form.Background));
        if (!string.IsNullOrWhiteSpace(form.Remark))
        {
            root.SetAttributeValue("备注", form.Remark);
        }

        if (!string.IsNullOrWhiteSpace(form.Icon))
        {
            root.SetAttributeValue("图标", form.Icon);
        }

        root.SetAttributeValue("边框", form.BorderStyle);
        if (!form.Movable)
        {
            root.SetAttributeValue("可否移动", "假");
        }

        if (!string.Equals(form.StartPosition, "默认", StringComparison.Ordinal))
        {
            root.SetAttributeValue("初始位置", form.StartPosition);
        }

        if (!form.MinimizeBox)
        {
            root.SetAttributeValue("最小化按钮", "假");
        }

        if (!form.MaximizeBox)
        {
            root.SetAttributeValue("最大化按钮", "假");
        }

        foreach (var control in form.Controls)
        {
            root.Add(SerializeControl(form.Name, control));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root)
            .ToString(SaveOptions.None);
    }

    public static YyFormModel Deserialize(string text)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new YyFormFormatException(exception.Message, exception.LineNumber, exception.LinePosition);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "窗体")
        {
            throw new YyFormFormatException("窗体文件的根元素必须是“窗体”。", 1, 1);
        }

        var form = new YyFormModel
        {
            Name = RequiredAttribute(root, "名称"),
            Title = Attribute(root, "标题", "易语窗体"),
            Width = NumberAttribute(root, "宽", 800, 320, 3840),
            Height = NumberAttribute(root, "高", 520, 240, 2160),
            Background = Attribute(root, "背景", "#F4F7FB"),
            Icon = Attribute(root, "图标", string.Empty),
            Remark = Attribute(root, "备注", string.Empty),
            BorderStyle = Attribute(root, "边框", "可调边框"),
            Movable = BooleanAttribute(root, "可否移动", true),
            StartPosition = Attribute(root, "初始位置", "默认"),
            MinimizeBox = BooleanAttribute(root, "最小化按钮", true),
            MaximizeBox = BooleanAttribute(root, "最大化按钮", true)
        };
        RejectInterfaceLogicAttributes(root);
        ValidateIdentifier(root, YyResourceIndex.FormId(form.Name), "窗体");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.Elements())
        {
            var type = element.Name.LocalName;
            if (!YyControlTypes.All.Contains(type, StringComparer.Ordinal))
            {
                throw Error(element, $"不支持的控件类型“{type}”。");
            }

            var control = new YyControlModel
            {
                Type = type,
                Name = RequiredAttribute(element, "名称"),
                Text = Attribute(element, "文本", string.Empty),
                Remark = Attribute(element, "备注", string.Empty),
                X = NumberAttribute(element, "左", 40, -10_000, 10_000),
                Y = NumberAttribute(element, "上", 40, -10_000, 10_000),
                Width = NumberAttribute(element, "宽", 120, 24, 3840),
                Height = NumberAttribute(element, "高", 36, 20, 2160),
                FontSize = NumberAttribute(element, "字号", 14, 6, 96),
                Foreground = Attribute(element, "前景", "#17212B"),
                Background = Attribute(element, "背景", "#FFFFFF"),
                IsEnabled = BooleanAttribute(element, "启用", true),
                IsVisible = BooleanAttribute(element, "可见", true),
                IsChecked = BooleanAttribute(element, "选中", false),
                ReadOnly = BooleanAttribute(element, "只读", false),
                MultiLine = BooleanAttribute(element, "多行", false),
                PasswordChar = Attribute(element, "密码", string.Empty),
                MaxLength = (int)NumberAttribute(element, "最大长度", 0, 0, 100_000),
                Picture = Attribute(element, "图片", string.Empty),
                DataBindingTable = NullIfEmpty(Attribute(element, "数据表", string.Empty)),
                DataBindingColumn = NullIfEmpty(Attribute(element, "数据列", string.Empty))
            };

            if (!IsIdentifier(control.Name))
            {
                throw Error(element, $"控件名称“{control.Name}”不是有效标识符。");
            }

            if (!names.Add(control.Name))
            {
                throw Error(element, $"控件名称“{control.Name}”重复。");
            }

            ValidateIdentifier(
                element,
                YyResourceIndex.ControlId(form.Name, control.Name),
                "控件");
            form.Controls.Add(control);
        }

        if (!IsIdentifier(form.Name))
        {
            throw Error(root, $"窗体名称“{form.Name}”不是有效标识符。");
        }

        return form;
    }

    private static XElement SerializeControl(string formName, YyControlModel control)
    {
        var element = new XElement(
            Namespace + control.Type,
            new XAttribute("标识", YyResourceIndex.ControlId(formName, control.Name)),
            new XAttribute("名称", control.Name),
            new XAttribute("文本", control.Text),
            new XAttribute("左", Format(control.X)),
            new XAttribute("上", Format(control.Y)),
            new XAttribute("宽", Format(control.Width)),
            new XAttribute("高", Format(control.Height)),
            new XAttribute("字号", Format(control.FontSize)),
            new XAttribute("前景", control.Foreground),
            new XAttribute("背景", control.Background),
            new XAttribute("启用", control.IsEnabled),
            new XAttribute("可见", control.IsVisible),
            new XAttribute("选中", control.IsChecked));
        if (!string.IsNullOrWhiteSpace(control.Remark))
        {
            element.SetAttributeValue("备注", control.Remark);
        }

        if (control.ReadOnly)
        {
            element.SetAttributeValue("只读", true);
        }

        if (control.MultiLine)
        {
            element.SetAttributeValue("多行", true);
        }

        if (!string.IsNullOrEmpty(control.PasswordChar))
        {
            element.SetAttributeValue("密码", control.PasswordChar);
        }

        if (control.MaxLength > 0)
        {
            element.SetAttributeValue("最大长度", control.MaxLength);
        }

        if (!string.IsNullOrWhiteSpace(control.Picture))
        {
            element.SetAttributeValue("图片", control.Picture);
        }

        if (!string.IsNullOrWhiteSpace(control.DataBindingTable))
        {
            element.SetAttributeValue("数据表", control.DataBindingTable);
        }

        if (!string.IsNullOrWhiteSpace(control.DataBindingColumn))
        {
            element.SetAttributeValue("数据列", control.DataBindingColumn);
        }

        return element;
    }

    private static void ValidateIdentifier(
        XElement element,
        string expected,
        string description)
    {
        var actual = RequiredAttribute(element, "标识");
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Error(
                element,
                $"{description}标识“{actual}”与当前名称不匹配，应为“{expected}”。");
        }
    }

    private static void RejectInterfaceLogicAttributes(XElement root)
    {
        var logicAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "代码", "载入", "关闭", "单击", "文本改变", "选择改变", "选中改变"
        };
        foreach (var element in root.DescendantsAndSelf())
        {
            var attribute = element.Attributes()
                .FirstOrDefault(item => logicAttributes.Contains(item.Name.LocalName));
            if (attribute is not null)
            {
                throw Error(
                    element,
                    $"界面 XML 不能保存事件或代码关系“{attribute.Name.LocalName}”；" +
                    "事件属于自动生成的 .Designer.yyc，逻辑属于同名 .yyc 文件。");
            }
        }
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = Attribute(element, name, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(element, $"元素“{element.Name.LocalName}”缺少“{name}”属性。");
        }

        return value.Trim();
    }

    private static string Attribute(XElement element, string name, string defaultValue) =>
        element.Attribute(name)?.Value ?? defaultValue;

    private static double NumberAttribute(
        XElement element,
        string name,
        double defaultValue,
        double minimum,
        double maximum)
    {
        var raw = element.Attribute(name)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) ||
            value < minimum ||
            value > maximum)
        {
            throw Error(element, $"属性“{name}”必须是 {minimum} 到 {maximum} 之间的数字。");
        }

        return value;
    }

    private static bool BooleanAttribute(XElement element, string name, bool defaultValue)
    {
        var raw = element.Attribute(name)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        throw Error(element, $"属性“{name}”必须是 True 或 False。");
    }

    private static YyFormFormatException Error(XElement element, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return new YyFormFormatException(
            message,
            lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public static bool IsIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class YyFormFormatException(string message, int line, int column) : Exception(message)
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
}

