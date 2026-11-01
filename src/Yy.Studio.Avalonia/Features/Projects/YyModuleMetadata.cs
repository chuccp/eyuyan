using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>易模块 (.ec) 元数据读取，不依赖 WPF Studio 的 YyModule 类型。</summary>
internal sealed class YyModuleMetadata
{
    private static readonly byte[] OfficialHeader = Encoding.ASCII.GetBytes("CNWTEPRG");

    [JsonPropertyName("格式")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("说明")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("创建时间")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("子程序")]
    public List<YyModuleFunctionMetadata> Functions { get; set; } = [];

    public static string ModulesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YyStudio",
        "modules");

    public static YyModuleMetadata Load(string path)
    {
        var header = new byte[OfficialHeader.Length];
        using (var stream = File.OpenRead(path))
        {
            _ = stream.Read(header, 0, header.Length);
        }

        if (header.AsSpan().SequenceEqual(OfficialHeader))
        {
            throw new InvalidDataException("这是官方二进制格式的易模块，当前版本暂不支持读取。");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<YyModuleMetadata>(json)
            ?? throw new InvalidDataException("易模块内容为空。");
    }
}

internal sealed class YyModuleFunctionMetadata
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("返回值类型")]
    public string ReturnType { get; set; } = string.Empty;

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("参数")]
    public List<YyModuleParameterMetadata> Parameters { get; set; } = [];
}

internal sealed class YyModuleParameterMetadata
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("类型")]
    public string TypeName { get; set; } = string.Empty;

    [JsonPropertyName("参考")]
    public bool ByRef { get; set; }
}
