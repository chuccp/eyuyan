using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Yy.Language.Structured;

namespace Yy.Studio.Forms;

public sealed record YyFormBindingEvent(
    string Owner,
    string OwnerType,
    string EventName,
    string HandlerName);

/// <summary>
/// 窗体与用户代码之间的生成层。界面只描述外观,本文件描述控件事件到用户子程序的映射。
/// 文件完全由窗体设计器生成,不承载用户逻辑。
/// </summary>
public sealed class YyFormBindingDocument
{
    public int FormatVersion { get; init; } = 1;
    public string FormName { get; set; } = string.Empty;
    public string UiFile { get; set; } = string.Empty;
    public string CodeFile { get; set; } = string.Empty;
    public List<YyFormBindingEvent> Events { get; } = [];
}

public static class YyFormBindingSerializer
{
    private static readonly XNamespace Namespace = "urn:yy-studio:form-binding:1";

    public static YyFormBindingDocument Create(
        YyFormModel form,
        string uiFile,
        string codeFile)
    {
        var binding = new YyFormBindingDocument
        {
            FormName = form.Name,
            UiFile = Path.GetFileName(uiFile),
            CodeFile = Path.GetFileName(codeFile)
        };

        if (!string.IsNullOrWhiteSpace(form.LoadEvent))
        {
            binding.Events.Add(new YyFormBindingEvent(
                form.Name,
                "窗体",
                "载入",
                form.LoadEvent));
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent))
        {
            binding.Events.Add(new YyFormBindingEvent(
                form.Name,
                "窗体",
                "关闭",
                form.CloseEvent));
        }

        foreach (var control in form.Controls)
        {
            AddControlEvent(binding, control, control.ClickEvent, "单击");
            AddControlEvent(binding, control, control.TextChangedEvent, "文本改变");
            AddControlEvent(binding, control, control.SelectionChangedEvent, "选择改变");
            AddControlEvent(binding, control, control.CheckedChangedEvent, "选中改变");
        }

        return binding;
    }

    public static string Generate(
        YyFormModel form,
        string uiFile,
        string codeFile) =>
        Serialize(Create(form, uiFile, codeFile));

    public static string Serialize(YyFormBindingDocument binding)
    {
        var root = new XElement(
            Namespace + "窗体绑定",
            new XAttribute(XNamespace.Xmlns + "yy", Namespace),
            new XAttribute("格式版本", binding.FormatVersion),
            new XAttribute("窗体", binding.FormName),
            new XAttribute("界面", binding.UiFile),
            new XAttribute("代码", binding.CodeFile),
            new XAttribute("生成器", "易语 Studio 窗体设计器"));

        foreach (var item in binding.Events.Where(item => item.OwnerType == "窗体"))
        {
            root.Add(new XElement(
                Namespace + "窗体事件",
                new XAttribute("类型", item.EventName),
                new XAttribute("处理程序", item.HandlerName)));
        }

        foreach (var group in binding.Events
                     .Where(item => item.OwnerType != "窗体")
                     .GroupBy(item => (item.Owner, item.OwnerType)))
        {
            var control = new XElement(
                Namespace + "控件",
                new XAttribute("名称", group.Key.Owner),
                new XAttribute("类型", group.Key.OwnerType));
            foreach (var item in group)
            {
                control.Add(new XElement(
                    Namespace + "事件",
                    new XAttribute("类型", item.EventName),
                    new XAttribute("处理程序", item.HandlerName)));
            }

            root.Add(control);
        }

        var document = new XDocument(
            new XComment(" 此文件由易语 Studio 自动生成。请勿手工修改,重新保存窗体时会完整覆盖。 "),
            root);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
               document.ToString(SaveOptions.None) + Environment.NewLine;
    }

    public static YyFormBindingDocument Deserialize(string text)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new YyFormFormatException(
                exception.Message,
                exception.LineNumber,
                exception.LinePosition);
        }

        var root = xml.Root;
        if (root is null || root.Name.LocalName != "窗体绑定")
        {
            throw Error(root, "窗体关系文件的根元素必须是“窗体绑定”。");
        }

        var version = Attribute(root, "格式版本", string.Empty);
        if (!int.TryParse(
                version,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var versionNumber) ||
            versionNumber != 1)
        {
            throw Error(root, $"不支持的关系文件版本“{version}”。");
        }

        var binding = new YyFormBindingDocument
        {
            FormatVersion = versionNumber,
            FormName = RequiredAttribute(root, "窗体"),
            UiFile = RequiredAttribute(root, "界面"),
            CodeFile = RequiredAttribute(root, "代码")
        };

        var controls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.Elements())
        {
            if (element.Name.LocalName == "窗体事件")
            {
                AddEvent(binding, binding.FormName, "窗体", element);
                continue;
            }

            if (element.Name.LocalName != "控件")
            {
                throw Error(element, $"不支持的关系元素“{element.Name.LocalName}”。");
            }

            var name = RequiredAttribute(element, "名称");
            var type = RequiredAttribute(element, "类型");
            if (!controls.Add(name))
            {
                throw Error(element, $"关系文件中的控件“{name}”重复。");
            }

            foreach (var eventElement in element.Elements())
            {
                if (eventElement.Name.LocalName != "事件")
                {
                    throw Error(eventElement, $"控件“{name}”包含不支持的元素“{eventElement.Name.LocalName}”。");
                }

                AddEvent(binding, name, type, eventElement);
            }
        }

        return binding;
    }

    public static void ApplyTo(YyFormModel form, YyFormBindingDocument binding)
    {
        if (!string.Equals(form.Name, binding.FormName, StringComparison.Ordinal))
        {
            throw new YyFormFormatException(
                $"关系文件属于窗体“{binding.FormName}”，但界面文件声明的是“{form.Name}”。",
                1,
                1);
        }

        form.LoadEvent = null;
        form.CloseEvent = null;
        foreach (var control in form.Controls)
        {
            control.ClickEvent = null;
            control.TextChangedEvent = null;
            control.SelectionChangedEvent = null;
            control.CheckedChangedEvent = null;
        }

        var controls = form.Controls.ToDictionary(
            control => control.Name,
            StringComparer.Ordinal);
        foreach (var item in binding.Events)
        {
            if (!YyFormSerializer.IsIdentifier(item.HandlerName))
            {
                throw new YyFormFormatException(
                    $"事件处理程序“{item.HandlerName}”不是有效函数标识符。",
                    1,
                    1);
            }

            if (item.OwnerType == "窗体")
            {
                ApplyFormEvent(form, item);
                continue;
            }

            if (!controls.TryGetValue(item.Owner, out var control))
            {
                throw new YyFormFormatException(
                    $"关系文件引用了界面中不存在的控件“{item.Owner}”。",
                    1,
                    1);
            }

            if (!string.Equals(control.Type, item.OwnerType, StringComparison.Ordinal))
            {
                throw new YyFormFormatException(
                    $"控件“{item.Owner}”在界面中是“{control.Type}”，关系文件中却是“{item.OwnerType}”。",
                    1,
                    1);
            }

            ApplyControlEvent(control, item);
        }
    }

    private static void AddControlEvent(
        YyFormBindingDocument binding,
        YyControlModel control,
        string? handler,
        string eventName)
    {
        if (!string.IsNullOrWhiteSpace(handler))
        {
            binding.Events.Add(new YyFormBindingEvent(
                control.Name,
                control.Type,
                eventName,
                handler));
        }
    }

    private static void AddEvent(
        YyFormBindingDocument binding,
        string owner,
        string ownerType,
        XElement element)
    {
        binding.Events.Add(new YyFormBindingEvent(
            owner,
            ownerType,
            RequiredAttribute(element, "类型"),
            RequiredAttribute(element, "处理程序")));
    }

    private static void ApplyFormEvent(YyFormModel form, YyFormBindingEvent item)
    {
        switch (item.EventName)
        {
            case "载入":
                EnsureUnassigned(form.LoadEvent, item);
                form.LoadEvent = item.HandlerName;
                break;
            case "关闭":
                EnsureUnassigned(form.CloseEvent, item);
                form.CloseEvent = item.HandlerName;
                break;
            default:
                throw new YyFormFormatException(
                    $"窗体不支持关系事件“{item.EventName}”。",
                    1,
                    1);
        }
    }

    private static void ApplyControlEvent(YyControlModel control, YyFormBindingEvent item)
    {
        switch (item.EventName)
        {
            case "单击":
                EnsureUnassigned(control.ClickEvent, item);
                control.ClickEvent = item.HandlerName;
                break;
            case "文本改变":
                EnsureUnassigned(control.TextChangedEvent, item);
                control.TextChangedEvent = item.HandlerName;
                break;
            case "选择改变":
                EnsureUnassigned(control.SelectionChangedEvent, item);
                control.SelectionChangedEvent = item.HandlerName;
                break;
            case "选中改变":
                EnsureUnassigned(control.CheckedChangedEvent, item);
                control.CheckedChangedEvent = item.HandlerName;
                break;
            default:
                throw new YyFormFormatException(
                    $"控件“{control.Name}”不支持关系事件“{item.EventName}”。",
                    1,
                    1);
        }
    }

    private static void EnsureUnassigned(string? existing, YyFormBindingEvent item)
    {
        if (!string.IsNullOrWhiteSpace(existing))
        {
            throw new YyFormFormatException(
                $"“{item.Owner}”的“{item.EventName}”事件重复绑定。",
                1,
                1);
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

    private static YyFormFormatException Error(XElement? element, string message)
    {
        var lineInfo = element is null ? null : (IXmlLineInfo)element;
        return new YyFormFormatException(
            message,
            lineInfo?.HasLineInfo() == true ? lineInfo.LineNumber : 1,
            lineInfo?.HasLineInfo() == true ? lineInfo.LinePosition : 1);
    }
}

public static class YyFormCompanionFiles
{
    public const string GeneratedSuffix = ".Designer.yyc";

    public static string UserCodePath(string formPath)
    {
        var fullPath = Path.GetFullPath(formPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("无法确定窗体目录。");
        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + ".yyc");
    }

    public static string BindingPath(string formPath)
    {
        var fullPath = Path.GetFullPath(formPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("无法确定窗体目录。");
        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + GeneratedSuffix);
    }

    public static bool IsGeneratedBindingPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.GetFileName(path).EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase);

    public static string GetAssociatedFormPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            throw new IOException("无法确定代码目录。");
        }

        var fileName = Path.GetFileName(fullPath);
        var stem = fileName.EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^GeneratedSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, stem + ".yyx");
    }

    /// <summary>为用户代码补齐事件入口;已有子程序及用户内容保持不变。</summary>
    public static bool EnsureEventHandlers(
        YyCodeDocument document,
        IEnumerable<YyFormBindingEvent> events)
    {
        var names = document.Functions
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var item in events)
        {
            if (!names.Add(item.HandlerName))
            {
                continue;
            }

            var function = new YyCodeFunction
            {
                Name = item.HandlerName,
                Remark = $"{item.Owner} · {item.EventName}"
            };
            function.Statements.Add(new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Call,
                Expression = $"' 在这里编写“{item.Owner} · {item.EventName}”的用户逻辑。"
            });
            document.Functions.Add(function);
            changed = true;
        }

        return changed;
    }

    public static YyCodeDocument CreateUserCodeDocument(
        YyFormModel form,
        YyFormBindingDocument binding)
    {
        var document = new YyCodeDocument
        {
            Name = form.Name + "代码",
            Remark = $"窗体“{form.Name}”的用户逻辑与事件入口。"
        };
        EnsureEventHandlers(document, binding.Events);
        return document;
    }

    public static void WriteGeneratedBinding(
        string formPath,
        YyFormModel form,
        string userCodePath)
    {
        var bindingPath = BindingPath(formPath);
        var directory = Path.GetDirectoryName(bindingPath)
            ?? throw new IOException("无法确定生成文件目录。");
        Directory.CreateDirectory(directory);
        var content = YyFormBindingSerializer.Generate(form, formPath, userCodePath);
        WriteAtomically(bindingPath, content);
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
