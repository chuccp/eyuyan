using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yy.Studio.Core.Configuration;

/// <summary>
/// 可编辑的 Studio 启动配置。用户配置优先于安装目录中的便携配置。
/// 模型不引用 WPF 或 Avalonia，桌面宿主只负责展示和编辑。
/// </summary>
public sealed class StudioConfiguration
{
    public const string FileName = "YyStudio.json";
    public const int CurrentFormatVersion = 1;
    public const int DefaultMcpPort = 37213;

    [JsonPropertyName("格式版本")]
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    [JsonPropertyName("MCP")]
    public StudioMcpConfiguration Mcp { get; set; } = new();

    [JsonPropertyName("编辑器")]
    public StudioEditorConfiguration Editor { get; set; } = new();

    [JsonPropertyName("窗体设计器")]
    public StudioDesignerConfiguration Designer { get; set; } = new();

    [JsonPropertyName("窗口布局")]
    public StudioWindowLayoutConfiguration WindowLayout { get; set; } = new();

    [JsonPropertyName("启动")]
    public StudioStartupConfiguration Startup { get; set; } = new();

    [JsonPropertyName("快捷键")]
    public Dictionary<string, List<string>> Shortcuts { get; set; } =
        StudioShortcutActions.CreateDefaults();

    [JsonIgnore]
    public string? ConfigurationPath { get; internal set; }

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    public static string UserFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YyStudio",
        FileName);

    public static string PortableFilePath =>
        Path.Combine(AppContext.BaseDirectory, FileName);

    public static StudioConfiguration Load(string[] args)
    {
        var explicitPath = ReadOption(args, "--config");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return LoadFile(Path.GetFullPath(explicitPath), createIfMissing: false);
        }

        if (File.Exists(UserFilePath))
        {
            return LoadFile(UserFilePath, createIfMissing: false);
        }

        if (File.Exists(PortableFilePath))
        {
            return LoadFile(PortableFilePath, createIfMissing: false);
        }

        var configuration = new StudioConfiguration
        {
            ConfigurationPath = UserFilePath
        };
        try
        {
            configuration.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            configuration.LoadWarning = "无法创建 Studio 配置文件：" + exception.Message;
        }

        return configuration;
    }

    public static StudioConfiguration Parse(string json, string? path = null)
    {
        var configuration = JsonSerializer.Deserialize<StudioConfiguration>(json)
            ?? throw new InvalidDataException("Studio 配置文件内容为空。");
        configuration.ConfigurationPath = path;
        configuration.Validate();
        return configuration;
    }

    public void Save()
    {
        var path = ConfigurationPath ?? UserFilePath;
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException("无法确定 Studio 配置目录。");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(this, SerializerOptions) + Environment.NewLine);
    }

    public StudioConfiguration Clone() =>
        Parse(JsonSerializer.Serialize(this, SerializerOptions), ConfigurationPath);

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Studio 配置格式无效，当前格式版本为 {CurrentFormatVersion}。");
        }

        Mcp ??= new StudioMcpConfiguration();
        if (Mcp.Port is < 1024 or > 65535)
        {
            throw new InvalidDataException("MCP 端口必须在 1024 到 65535 之间。");
        }

        if (Mcp.PortAttempts is < 1 or > 100)
        {
            throw new InvalidDataException("MCP 端口尝试次数必须在 1 到 100 之间。");
        }

        Editor ??= new StudioEditorConfiguration();
        if (Editor.FontSize is < 8 or > 28)
        {
            throw new InvalidDataException("编辑器字号必须在 8 到 28 之间。");
        }

        if (Editor.TabWidth is < 1 or > 16)
        {
            throw new InvalidDataException("编辑器制表宽度必须在 1 到 16 之间。");
        }

        WindowLayout ??= new StudioWindowLayoutConfiguration();
        WindowLayout.Validate();
        Startup ??= new StudioStartupConfiguration();
        Shortcuts ??= StudioShortcutActions.CreateDefaults();
        foreach (var (name, gestures) in StudioShortcutActions.CreateDefaults())
        {
            Shortcuts.TryAdd(name, gestures);
        }

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (action, gestures) in Shortcuts)
        {
            if (!StudioShortcutActions.All.Contains(action))
            {
                throw new InvalidDataException($"未知快捷键动作“{action}”。");
            }

            if (gestures is null)
            {
                throw new InvalidDataException(
                    $"快捷键动作“{action}”的值必须是数组，使用空数组可停用该动作。");
            }

            foreach (var value in gestures)
            {
                var gesture = StudioShortcutActions.ParseGesture(value, action);
                if (owners.TryGetValue(gesture.Display, out var owner))
                {
                    throw new InvalidDataException(
                        $"快捷键“{gesture.Display}”同时分配给了“{owner}”和“{action}”。");
                }

                owners[gesture.Display] = action;
            }
        }
    }

    public IReadOnlyList<StudioShortcutGesture> GetGestures(string action) =>
        Shortcuts.TryGetValue(action, out var values)
            ? values.Select(value => StudioShortcutActions.ParseGesture(value, action)).ToArray()
            : [];

    private static StudioConfiguration LoadFile(string path, bool createIfMissing)
    {
        try
        {
            if (!File.Exists(path))
            {
                if (!createIfMissing)
                {
                    throw new FileNotFoundException("Studio 配置文件不存在。", path);
                }

                var created = new StudioConfiguration { ConfigurationPath = path };
                created.Save();
                return created;
            }

            return Parse(File.ReadAllText(path), path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or InvalidDataException)
        {
            return new StudioConfiguration
            {
                ConfigurationPath = path,
                LoadWarning = $"配置文件“{path}”无法使用，已回落默认值：{exception.Message}"
            };
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length ? args[index + 1] : null;
            }

            var prefix = name + "=";
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[index][prefix.Length..];
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public sealed class StudioMcpConfiguration
{
    [JsonPropertyName("启用")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("端口")]
    public int Port { get; set; } = StudioConfiguration.DefaultMcpPort;

    [JsonPropertyName("自动寻找可用端口")]
    public bool AutoFindAvailablePort { get; set; } = true;

    [JsonPropertyName("端口尝试次数")]
    public int PortAttempts { get; set; } = 10;
}

public sealed class StudioEditorConfiguration
{
    [JsonPropertyName("字号")]
    public double FontSize { get; set; } = 13;

    [JsonPropertyName("制表宽度")]
    public int TabWidth { get; set; } = 4;
}

public sealed class StudioDesignerConfiguration
{
    [JsonPropertyName("吸附网格")]
    public bool SnapGrid { get; set; } = true;
}

public sealed class StudioStartupConfiguration
{
    [JsonPropertyName("显示新建程序对话框")]
    public bool ShowNewProgramDialog { get; set; } = true;
}

/// <summary>把最近打开的文件保存在 %AppData%\YyStudio\settings.json，不写入可编辑配置。</summary>
public sealed class StudioRecentFiles
{
    public const int MaximumCount = 12;

    [JsonPropertyName("最近项目")]
    public List<string> RecentFiles { get; set; } = [];

    [JsonIgnore]
    public string? StoragePath { get; private set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YyStudio",
        "settings.json");

    public static StudioRecentFiles Load(string? path = null)
    {
        path ??= FilePath;
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<StudioRecentFiles>(
                        File.ReadAllText(path))
                    ?? new StudioRecentFiles();
                settings.StoragePath = path;
                settings.RecentFiles ??= [];
                return settings;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 最近文件损坏时回落默认值，不影响 IDE 启动。
        }

        return new StudioRecentFiles { StoragePath = path };
    }

    /// <summary>记录最近打开的文件/项目目录（起始页显示）。</summary>
    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        RecentFiles.RemoveAll(item =>
            string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaximumCount)
        {
            RecentFiles.RemoveRange(MaximumCount, RecentFiles.Count - MaximumCount);
        }

        Save();
    }

    public void Save(string? path = null)
    {
        path ??= StoragePath ?? FilePath;
        StoragePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
