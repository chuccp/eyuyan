using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yy.Language.Structured;

namespace Yy.Studio;

public sealed class YyModuleParameter
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("类型")]
    public string TypeName { get; set; } = string.Empty;

    [JsonPropertyName("参考")]
    public bool ByRef { get; set; }
}

public sealed class YyModuleFunction
{
    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("返回值类型")]
    public string ReturnType { get; set; } = string.Empty;

    [JsonPropertyName("备注")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("参数")]
    public List<YyModuleParameter> Parameters { get; set; } = [];

    [JsonPropertyName("代码")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>易模块(.ec):一组可复用的公开子程序,含签名表与源码。</summary>
public sealed class YyModule
{
    [JsonPropertyName("格式")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("名称")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("说明")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("创建时间")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("子程序")]
    public List<YyModuleFunction> Functions { get; set; } = [];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ModulesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YyStudio",
        "modules");

    public static YyModule Compile(YyCodeDocument document, IEnumerable<YyCodeFunction> functions)
    {
        var module = new YyModule
        {
            Name = document.Name,
            Remark = document.Remark,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        foreach (var function in functions)
        {
            module.Functions.Add(new YyModuleFunction
            {
                Name = function.Name,
                ReturnType = function.ReturnType,
                Remark = function.Remark,
                Parameters = function.Parameters
                    .Select(parameter => new YyModuleParameter
                    {
                        Name = parameter.Name,
                        TypeName = parameter.TypeName,
                        ByRef = parameter.ByRef
                    })
                    .ToList(),
                Source = FunctionToSource(function)
            });
        }

        return module;
    }

    public void Save(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions), new UTF8Encoding(false));
    }

    public static YyModule Load(string path)
    {
        var header = new byte[8];
        using (var stream = File.OpenRead(path))
        {
            _ = stream.Read(header, 0, header.Length);
        }

        if (System.Text.Encoding.ASCII.GetString(header) == "CNWTEPRG")
        {
            throw new InvalidDataException("这是官方二进制格式的易模块，当前版本暂不支持读取。");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<YyModule>(json)
            ?? throw new InvalidDataException("易模块内容为空。");
    }

    private static string FunctionToSource(YyCodeFunction function)
    {
        var single = new YyCodeDocument { Name = "_模块" };
        single.Functions.Add(function);
        return YyCodeDocumentConverter.ToSource(single);
    }
}
