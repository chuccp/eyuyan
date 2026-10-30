using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Yy.Language.Structured;

namespace Yy.Compiler.Forms;

public sealed record YyFormBindingControl(
    string Name,
    string TypeName,
    string Id);

public sealed record YyFormBindingEvent(
    string Owner,
    string OwnerType,
    string EventName,
    string HandlerName,
    string OwnerId = "");

/// <summary>
/// 窗体与用户代码之间的生成层。文件内容是可读的易语声明代码，
/// 使用资源 ID 关联界面元素和事件，而不是 XML。
/// </summary>
public sealed class YyFormBindingDocument
{
    public int FormatVersion { get; init; } = 1;
    public string FormName { get; set; } = string.Empty;
    public string UiFile { get; set; } = string.Empty;
    public string CodeFile { get; set; } = string.Empty;
    public List<YyFormBindingControl> Controls { get; } = [];
    public List<YyFormBindingEvent> Events { get; } = [];
}

public static class YyFormBindingSerializer
{
    private static readonly JsonSerializerOptions StringSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

        foreach (var control in form.Controls)
        {
            binding.Controls.Add(new YyFormBindingControl(
                control.Name,
                control.Type,
                YyResourceIndex.ControlId(form.Name, control.Name)));
        }

        if (!string.IsNullOrWhiteSpace(form.LoadEvent))
        {
            binding.Events.Add(new YyFormBindingEvent(
                form.Name,
                "窗体",
                "载入",
                form.LoadEvent,
                YyResourceIndex.FormId(form.Name)));
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent))
        {
            binding.Events.Add(new YyFormBindingEvent(
                form.Name,
                "窗体",
                "关闭",
                form.CloseEvent,
                YyResourceIndex.FormId(form.Name)));
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

    /// <summary>生成纯文本易语代码，不使用 XML 根元素或节点表达关系。</summary>
    public static string Serialize(YyFormBindingDocument binding)
    {
        var builder = new StringBuilder();
        builder.AppendLine($".程序集 {binding.FormName}.Designer");
        builder.AppendLine("导入包 \"系统.界面\"");
        builder.AppendLine();
        builder.AppendLine("' 此文件由易语 Studio 自动生成，请勿手工修改。");
        builder.AppendLine("' 控件通过 @id/... 资源标识进入界面上下文。");
        builder.AppendLine(
            $"界面({Quote(binding.FormName)}, {Quote(binding.UiFile)}, {Quote(binding.CodeFile)})");
        builder.AppendLine();
        foreach (var control in binding.Controls)
        {
            builder.AppendLine(
                $"控件({Quote(control.Id)}, {Quote(control.Name)}, {Quote(control.TypeName)})");
        }

        builder.AppendLine();
        foreach (var item in binding.Events)
        {
            builder.AppendLine(
                $"绑定({Quote(GetOwnerId(binding, item))}, {Quote(item.EventName)}, {Quote(item.HandlerName)})");
        }

        return builder.ToString();
    }

    public static YyFormBindingDocument Deserialize(string text)
    {
        var binding = new YyFormBindingDocument();
        var pendingBindings = new List<(string Id, string EventName, string HandlerName, int Line)>();
        var controlsById = new Dictionary<string, YyFormBindingControl>(StringComparer.Ordinal);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var interfaceSeen = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 ||
                line.StartsWith('\'') ||
                line.StartsWith(".版本", StringComparison.Ordinal) ||
                line.StartsWith(".程序集", StringComparison.Ordinal) ||
                line.StartsWith("导入包 ", StringComparison.Ordinal))
            {
                continue;
            }

            var call = ParseCall(line, index + 1);
            if (call.Name == "界面")
            {
                if (interfaceSeen || call.Arguments.Count != 3)
                {
                    throw Error(index + 1, "界面() 声明必须且只能出现一次，并包含窗体、界面文件和代码文件。");
                }

                binding.FormName = call.Arguments[0];
                binding.UiFile = call.Arguments[1];
                binding.CodeFile = call.Arguments[2];
                interfaceSeen = true;
                continue;
            }

            if (call.Name == "控件")
            {
                if (call.Arguments.Count != 3)
                {
                    throw Error(index + 1, "控件() 需要标识、名称和类型。");
                }

                var control = new YyFormBindingControl(
                    call.Arguments[1],
                    call.Arguments[2],
                    call.Arguments[0]);
                if (!controlsById.TryAdd(control.Id, control))
                {
                    throw Error(index + 1, $"控件标识“{control.Id}”重复。");
                }

                binding.Controls.Add(control);
                continue;
            }

            if (call.Name == "绑定")
            {
                if (call.Arguments.Count != 3)
                {
                    throw Error(index + 1, "绑定() 需要控件标识、事件名和处理程序。");
                }

                pendingBindings.Add((
                    call.Arguments[0],
                    call.Arguments[1],
                    call.Arguments[2],
                    index + 1));
                continue;
            }

            throw Error(index + 1, $"不支持的界面关系声明“{call.Name}”。");
        }

        if (!interfaceSeen || string.IsNullOrWhiteSpace(binding.FormName))
        {
            throw Error(1, "缺少界面() 声明。");
        }

        foreach (var item in pendingBindings)
        {
            if (!YyFormSerializer.IsIdentifier(item.HandlerName))
            {
                throw Error(
                    item.Line,
                    $"事件处理程序“{item.HandlerName}”不是有效函数标识符。");
            }

            if (string.Equals(item.Id, YyResourceIndex.FormId(binding.FormName), StringComparison.Ordinal))
            {
                binding.Events.Add(new YyFormBindingEvent(
                    binding.FormName,
                    "窗体",
                    item.EventName,
                    item.HandlerName,
                    item.Id));
                continue;
            }

            if (!controlsById.TryGetValue(item.Id, out var control))
            {
                throw Error(item.Line, $"绑定的控件标识“{item.Id}”没有在控件() 中声明。");
            }

            binding.Events.Add(new YyFormBindingEvent(
                control.Name,
                control.TypeName,
                item.EventName,
                item.HandlerName,
                control.Id));
        }

        var duplicate = binding.Events
            .GroupBy(item => (item.OwnerId, item.EventName))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new YyFormFormatException(
                $"控件“{duplicate.First().Owner}”的“{duplicate.Key.EventName}”事件重复绑定。",
                1,
                1);
        }

        return binding;
    }

    public static void ApplyTo(YyFormModel form, YyFormBindingDocument binding)
    {
        if (!string.Equals(form.Name, binding.FormName, StringComparison.Ordinal))
        {
            throw new YyFormFormatException(
                $"关系代码属于窗体“{binding.FormName}”，但界面文件声明的是“{form.Name}”。",
                1,
                1);
        }

        var formId = YyResourceIndex.FormId(form.Name);
        var controlsById = new Dictionary<string, YyControlModel>(StringComparer.Ordinal);
        foreach (var control in form.Controls)
        {
            controlsById.Add(YyResourceIndex.ControlId(form.Name, control.Name), control);
        }

        if (binding.Controls.Count != controlsById.Count)
        {
            throw new YyFormFormatException(
                "关系代码声明的控件数量与界面不一致。",
                1,
                1);
        }

        foreach (var item in binding.Controls)
        {
            if (!controlsById.TryGetValue(item.Id, out var control) ||
                !string.Equals(control.Name, item.Name, StringComparison.Ordinal) ||
                !string.Equals(control.Type, item.TypeName, StringComparison.Ordinal))
            {
                throw new YyFormFormatException(
                    $"关系代码中的控件“{item.Name}”与界面标识“{item.Id}”不一致。",
                    1,
                    1);
            }
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

        foreach (var item in binding.Events)
        {
            if (item.OwnerType == "窗体")
            {
                if (!string.Equals(item.OwnerId, formId, StringComparison.Ordinal))
                {
                    throw new YyFormFormatException(
                        $"窗体事件绑定了无效标识“{item.OwnerId}”。",
                        1,
                        1);
                }

                ApplyFormEvent(form, item);
                continue;
            }

            if (!controlsById.TryGetValue(item.OwnerId, out var control))
            {
                throw new YyFormFormatException(
                    $"关系代码引用了界面中不存在的控件标识“{item.OwnerId}”。",
                    1,
                    1);
            }

            if (!string.Equals(control.Type, item.OwnerType, StringComparison.Ordinal))
            {
                throw new YyFormFormatException(
                    $"控件“{control.Name}”在界面中是“{control.Type}”，关系代码中却是“{item.OwnerType}”。",
                    1,
                    1);
            }

            ApplyControlEvent(control, item);
        }
    }

    private static string GetOwnerId(
        YyFormBindingDocument binding,
        YyFormBindingEvent item) =>
        !string.IsNullOrWhiteSpace(item.OwnerId)
            ? item.OwnerId
            : item.OwnerType == "窗体"
                ? YyResourceIndex.FormId(binding.FormName)
                : YyResourceIndex.ControlId(binding.FormName, item.Owner);

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
                handler,
                YyResourceIndex.ControlId(binding.FormName, control.Name)));
        }
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

    private static CallLine ParseCall(string line, int lineNumber)
    {
        var open = line.IndexOf('(');
        if (open <= 0 || !line.EndsWith(')'))
        {
            throw Error(lineNumber, "关系代码只接受“声明(参数...)”。");
        }

        var name = line[..open].Trim();
        var arguments = ParseArguments(line[(open + 1)..^1], lineNumber);
        return new CallLine(name, arguments);
    }

    private static List<string> ParseArguments(string content, int lineNumber)
    {
        var result = new List<string>();
        var start = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quoted)
            {
                escaped = true;
            }
            else if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                result.Add(ParseString(content[start..index].Trim(), lineNumber));
                start = index + 1;
            }
        }

        if (quoted)
        {
            throw Error(lineNumber, "声明中的文本缺少结束引号。");
        }

        if (start <= content.Length)
        {
            var last = content[start..].Trim();
            if (last.Length > 0 || result.Count > 0)
            {
                result.Add(ParseString(last, lineNumber));
            }
        }

        return result;
    }

    private static string ParseString(string value, int lineNumber)
    {
        if (!value.StartsWith('"') || !value.EndsWith('"'))
        {
            throw Error(lineNumber, $"参数“{value}”必须使用双引号。");
        }

        try
        {
            return JsonSerializer.Deserialize<string>(value)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new YyFormFormatException("关系代码中的文本转义无效。", lineNumber, 1);
        }
    }

    private static string Quote(string value) =>
        JsonSerializer.Serialize(value, StringSerializerOptions);

    private static YyFormFormatException Error(int line, string message) =>
        new(message, line, 1);

    private sealed record CallLine(string Name, IReadOnlyList<string> Arguments);
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
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("无法确定代码目录。");
        var fileName = Path.GetFileName(fullPath);
        var stem = fileName.EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^GeneratedSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, stem + ".yyx");
    }

    /// <summary>为用户代码补齐事件入口；已有子程序及用户内容保持不变。</summary>
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
        document.Packages.Add(Yy.Sdk.YySdk.CorePackage);
        document.Packages.Add(Yy.Sdk.YySdk.UiPackage);
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
