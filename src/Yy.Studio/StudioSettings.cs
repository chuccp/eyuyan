using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yy.Studio;

/// <summary>IDE 系统配置(系统配置/支持库配置),存于 %AppData%\YyStudio\settings.json。</summary>
public sealed class StudioSettings
{
    [JsonPropertyName("编辑器字号")]
    public double EditorFontSize { get; set; } = 13;

    [JsonPropertyName("制表宽度")]
    public int TabWidth { get; set; } = 4;

    [JsonPropertyName("吸附网格")]
    public bool SnapGrid { get; set; } = true;

    [JsonPropertyName("目的平台")]
    public string TargetRid { get; set; } = "win-x64";

    [JsonPropertyName("最近项目")]
    public List<string> RecentFiles { get; set; } = [];

    [JsonPropertyName("启动不再显示新建对话框")]
    public bool NoNewProgramDialog { get; set; }

    /// <summary>记录最近打开的文件/项目目录(起始页显示)。</summary>
    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 12)
        {
            RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
        }

        Save();
    }

    [JsonPropertyName("停用支持库")]
    public List<string> DisabledLibraries { get; set; } = [];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YyStudio",
        "settings.json");

    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(FilePath))
                    ?? new StudioSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 配置损坏时回落默认值,不影响 IDE 启动。
        }

        return new StudioSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}
