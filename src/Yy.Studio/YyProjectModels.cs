using System.Text.Json.Serialization;
using Yy.Studio.Forms;

namespace Yy.Studio;

public sealed record YyProjectForm(
    string FormPath,
    string BindingPath,
    string? CodePath,
    YyFormModel Model);

public sealed class YyGlobalVarEntry
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("类型")]
    public string TypeName { get; set; } = "文本";

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;
}
public sealed class YyConstantEntry
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("值")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;
}
public sealed class YyResourceEntry
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("类型")]
    public string TypeName { get; set; } = "图片";

    [JsonPropertyName("文件")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;
}
public sealed class YyDllCommandEntry
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Dll文件")]
    public string DllFile { get; set; } = string.Empty;

    [JsonPropertyName("命令名")]
    public string CommandName { get; set; } = string.Empty;

    [JsonPropertyName("参数")]
    public string Parameters { get; set; } = string.Empty;

    [JsonPropertyName("返回类型")]
    public string ReturnType { get; set; } = "整数型";

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;
}

public sealed class YyDbColumn
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("类型")]
    public string TypeName { get; set; } = "文本";

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;
}

public sealed class YyDbTable
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("列")]
    public List<YyDbColumn> Columns { get; set; } = [];
}
