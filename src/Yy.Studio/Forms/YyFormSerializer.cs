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
            new XAttribute("名称", form.Name),
            new XAttribute("代码", form.CodeFile),
            new XAttribute("标题", form.Title),
            new XAttribute("宽", Format(form.Width)),
            new XAttribute("高", Format(form.Height)),
            new XAttribute("背景", form.Background));

        if (!string.IsNullOrWhiteSpace(form.LoadEvent))
        {
            root.SetAttributeValue("载入", form.LoadEvent);
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent))
        {
            root.SetAttributeValue("关闭", form.CloseEvent);
        }

        foreach (var control in form.Controls)
        {
            root.Add(SerializeControl(control));
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
            CodeFile = Attribute(root, "代码", string.Empty),
            LoadEvent = NullIfEmpty(Attribute(root, "载入", string.Empty)),
            CloseEvent = NullIfEmpty(Attribute(root, "关闭", string.Empty)),
            Title = Attribute(root, "标题", "易语窗体"),
            Width = NumberAttribute(root, "宽", 800, 320, 3840),
            Height = NumberAttribute(root, "高", 520, 240, 2160),
            Background = Attribute(root, "背景", "#F4F7FB")
        };
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
                ClickEvent = NullIfEmpty(Attribute(element, "单击", string.Empty)),
                X = NumberAttribute(element, "左", 40, -10_000, 10_000),
                Y = NumberAttribute(element, "上", 40, -10_000, 10_000),
                Width = NumberAttribute(element, "宽", 120, 24, 3840),
                Height = NumberAttribute(element, "高", 36, 20, 2160),
                FontSize = NumberAttribute(element, "字号", 14, 6, 96),
                Foreground = Attribute(element, "前景", "#17212B"),
                Background = Attribute(element, "背景", "#FFFFFF"),
                IsEnabled = BooleanAttribute(element, "启用", true),
                IsVisible = BooleanAttribute(element, "可见", true)
            };

            if (!IsIdentifier(control.Name))
            {
                throw Error(element, $"控件名称“{control.Name}”不是有效标识符。");
            }

            if (!string.IsNullOrWhiteSpace(control.ClickEvent) &&
                !IsIdentifier(control.ClickEvent))
            {
                throw Error(element, $"控件“{control.Name}”的事件名不是有效函数标识符。");
            }

            if (!names.Add(control.Name))
            {
                throw Error(element, $"控件名称“{control.Name}”重复。");
            }

            form.Controls.Add(control);
        }

        if (!IsIdentifier(form.Name))
        {
            throw Error(root, $"窗体名称“{form.Name}”不是有效标识符。");
        }

        if (!string.IsNullOrWhiteSpace(form.LoadEvent) && !IsIdentifier(form.LoadEvent))
        {
            throw Error(root, "窗体“载入”事件名不是有效函数标识符。");
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent) && !IsIdentifier(form.CloseEvent))
        {
            throw Error(root, "窗体“关闭”事件名不是有效函数标识符。");
        }

        if (!string.IsNullOrWhiteSpace(form.CodeFile) &&
            (Path.IsPathRooted(form.CodeFile) ||
             form.CodeFile.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("..", StringComparer.Ordinal)))
        {
            throw Error(root, "窗体“代码”属性必须是窗体目录内的相对路径。");
        }

        return form;
    }

    private static XElement SerializeControl(YyControlModel control)
    {
        var element = new XElement(
            Namespace + control.Type,
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
            new XAttribute("可见", control.IsVisible));

        if (!string.IsNullOrWhiteSpace(control.ClickEvent))
        {
            element.SetAttributeValue("单击", control.ClickEvent);
        }

        return element;
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

