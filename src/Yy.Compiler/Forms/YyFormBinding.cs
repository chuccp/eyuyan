using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Yy.Language;
using Yy.Language.Structured;
using Yy.Language.Syntax;

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
        builder.AppendLine("' 窗体、控件和事件使用结构体描述；@id/... 只保存在生成关系中。");
        builder.AppendLine(
            $"结构体 {binding.FormName}文件");
        builder.AppendLine($"    界面: 文本 = {Quote(binding.UiFile)}");
        builder.AppendLine($"    代码: 文本 = {Quote(binding.CodeFile)}");
        builder.AppendLine("结束结构体");
        builder.AppendLine();
        builder.AppendLine($"结构体 {binding.FormName}界面");
        foreach (var control in binding.Controls)
        {
            builder.AppendLine(
                $"    {control.Name}: 任意 = " +
                $"{{标识: {Quote(control.Id)}, 类型: {Quote(control.TypeName)}}}");
        }

        builder.AppendLine("结束结构体");
        builder.AppendLine();
        builder.AppendLine($"结构体 {binding.FormName}事件");
        foreach (var item in binding.Events)
        {
            var fieldName = item.OwnerType == "窗体"
                ? item.EventName
                : item.Owner + "_" + item.EventName;
            builder.AppendLine(
                $"    {fieldName}: 文本 = {Quote(item.HandlerName)}");
        }

        builder.AppendLine("结束结构体");
        return builder.ToString();
    }

    public static YyFormBindingDocument Deserialize(string text)
    {
        YyProgram program;
        try
        {
            program = new Parser(text).Parse();
        }
        catch (YyDiagnostic diagnostic)
        {
            throw new YyFormFormatException(
                diagnostic.Message,
                diagnostic.Line,
                diagnostic.Column);
        }

        var fileStruct = program.Structs.FirstOrDefault(structure =>
            structure.Name.EndsWith("文件", StringComparison.Ordinal));
        var interfaceStruct = program.Structs.FirstOrDefault(structure =>
            structure.Name.EndsWith("界面", StringComparison.Ordinal));
        var eventStruct = program.Structs.FirstOrDefault(structure =>
            structure.Name.EndsWith("事件", StringComparison.Ordinal));
        if (fileStruct is null || interfaceStruct is null || eventStruct is null)
        {
            throw Error(1, "缺少窗体文件、界面或事件结构体。");
        }

        var formName = fileStruct.Name[..^"文件".Length];
        if (!string.Equals(interfaceStruct.Name, formName + "界面", StringComparison.Ordinal) ||
            !string.Equals(eventStruct.Name, formName + "事件", StringComparison.Ordinal))
        {
            throw Error(1, "窗体文件、界面和事件结构体不属于同一个窗体。");
        }

        var binding = new YyFormBindingDocument
        {
            FormName = formName,
            UiFile = RequireStructString(fileStruct, "界面"),
            CodeFile = RequireStructString(fileStruct, "代码")
        };
        foreach (var field in interfaceStruct.Fields)
        {
            var descriptor = field.DefaultValue as ObjectExpression;
            var id = RequireObjectString(
                descriptor,
                "标识",
                $"控件结构体字段“{field.Name}”");
            var typeName = RequireObjectString(
                descriptor,
                "类型",
                $"控件结构体字段“{field.Name}”");
            if (binding.Controls.Any(control =>
                    string.Equals(control.Id, id, StringComparison.Ordinal)))
            {
                throw Error(1, $"控件标识“{id}”重复。");
            }

            binding.Controls.Add(new YyFormBindingControl(field.Name, typeName, id));
        }

        foreach (var field in eventStruct.Fields)
        {
            var handler = RequireStructString(eventStruct, field.Name);
            if (!YyFormSerializer.IsIdentifier(handler))
            {
                throw Error(1, $"事件处理程序“{handler}”不是有效函数标识符。");
            }

            if (field.Name is "载入" or "关闭")
            {
                binding.Events.Add(new YyFormBindingEvent(
                    formName,
                    "窗体",
                    field.Name,
                    handler,
                    YyResourceIndex.FormId(formName)));
                continue;
            }

            var control = binding.Controls
                .Where(item =>
                    field.Name.StartsWith(item.Name + "_", StringComparison.Ordinal))
                .OrderByDescending(item => item.Name.Length)
                .FirstOrDefault();
            if (control is null)
            {
                throw Error(1, $"事件结构体字段“{field.Name}”找不到对应控件。");
            }

            binding.Events.Add(new YyFormBindingEvent(
                control.Name,
                control.TypeName,
                field.Name[(control.Name.Length + 1)..],
                handler,
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

    private static string RequireStructString(
        StructDeclaration structure,
        string fieldName)
    {
        var field = structure.Fields.FirstOrDefault(item =>
            string.Equals(item.Name, fieldName, StringComparison.Ordinal));
        if (field?.DefaultValue is LiteralExpression { Value: string value })
        {
            return value;
        }

        throw Error(
            structure.Line,
            $"结构体“{structure.Name}”缺少文本字段“{fieldName}”的默认值。");
    }

    private static string RequireObjectString(
        ObjectExpression? expression,
        string propertyName,
        string description)
    {
        if (expression?.Properties.FirstOrDefault(property =>
                string.Equals(property.Key, propertyName, StringComparison.Ordinal))
            .Value is LiteralExpression { Value: string value })
        {
            return value;
        }

        throw Error(1, $"{description}缺少文本属性“{propertyName}”。");
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

    /// <summary>
    /// 源码编辑模式允许界面结构变化。按稳定标识恢复仍兼容的事件绑定，
    /// 返回已保留的事件数量；已删除或改名控件的关系由下一次保存重建。
    /// </summary>
    public static int ApplyCompatibleTo(
        YyFormModel form,
        YyFormBindingDocument binding)
    {
        ClearEvents(form);
        var formId = YyResourceIndex.FormId(form.Name);
        var controlsById = form.Controls.ToDictionary(
            control => YyResourceIndex.ControlId(form.Name, control.Name),
            StringComparer.Ordinal);
        var applied = 0;
        foreach (var item in binding.Events)
        {
            if (item.OwnerType == "窗体")
            {
                if (!string.Equals(item.OwnerId, formId, StringComparison.Ordinal))
                {
                    continue;
                }

                ApplyFormEvent(form, item);
                applied++;
                continue;
            }

            if (!controlsById.TryGetValue(item.OwnerId, out var control) ||
                !string.Equals(control.Type, item.OwnerType, StringComparison.Ordinal))
            {
                continue;
            }

            ApplyControlEvent(control, item);
            applied++;
        }

        return applied;
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

    private static void ClearEvents(YyFormModel form)
    {
        form.LoadEvent = null;
        form.CloseEvent = null;
        foreach (var control in form.Controls)
        {
            control.ClickEvent = null;
            control.TextChangedEvent = null;
            control.SelectionChangedEvent = null;
            control.CheckedChangedEvent = null;
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
                Expression =
                    $"' 在此编写“{item.Owner} · {item.EventName}”逻辑；" +
                    "可直接使用生成的窗口名、控件名和属性。"
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
