using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Yy.Studio;

/// <summary>可编辑的 Studio 启动配置。用户配置优先于安装目录中的便携配置。</summary>
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

            foreach (var value in gestures)
            {
                var gesture = StudioShortcutActions.ParseGesture(value, action);
                var display = gesture.GetDisplayStringForCulture(CultureInfo.InvariantCulture);
                if (owners.TryGetValue(display, out var owner))
                {
                    throw new InvalidDataException(
                        $"快捷键“{display}”同时分配给了“{owner}”和“{action}”。");
                }

                owners[display] = action;
            }
        }
    }

    public IReadOnlyList<KeyGesture> GetGestures(string action) =>
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

public sealed class StudioWindowLayoutConfiguration
{
    public const string DefaultPreset = "默认";
    public const string FocusCodePreset = "专注代码";
    public const string DesignerPreset = "窗体设计";
    public const string CustomPreset = "自定义";

    public static IReadOnlySet<string> Presets { get; } = new HashSet<string>(
        [DefaultPreset, FocusCodePreset, DesignerPreset, CustomPreset],
        StringComparer.Ordinal);

    [JsonPropertyName("预设")]
    public string Preset { get; set; } = DefaultPreset;

    [JsonPropertyName("显示状态栏")]
    public bool ShowStatusBar { get; set; } = true;

    [JsonPropertyName("项目")]
    public StudioToolWindowConfiguration Project { get; set; } = new()
    {
        Dock = StudioToolWindowConfiguration.LeftDock,
        Size = 260
    };

    [JsonPropertyName("程序与属性")]
    public StudioToolWindowConfiguration WorkTools { get; set; } = new()
    {
        Dock = StudioToolWindowConfiguration.RightDock,
        Size = 360
    };

    [JsonPropertyName("控制台")]
    public StudioToolWindowConfiguration Console { get; set; } = new()
    {
        Dock = StudioToolWindowConfiguration.BottomDock,
        Size = 172
    };

    [JsonPropertyName("右侧活动页")]
    public int RightToolsTab { get; set; }

    public void Validate()
    {
        Preset = string.IsNullOrWhiteSpace(Preset) ? DefaultPreset : Preset.Trim();
        if (!Presets.Contains(Preset))
        {
            throw new InvalidDataException($"未知窗口布局预设“{Preset}”。");
        }

        Project ??= new StudioToolWindowConfiguration();
        WorkTools ??= new StudioToolWindowConfiguration();
        Console ??= new StudioToolWindowConfiguration();
        Project.Validate("项目");
        WorkTools.Validate("程序与属性");
        Console.Validate("控制台");

        if (RightToolsTab is < 0 or > 1)
        {
            throw new InvalidDataException("右侧工具窗活动页必须是 0（程序）或 1（属性）。");
        }

        var docks = new[] { Project.Dock, WorkTools.Dock, Console.Dock };
        if (docks.Distinct(StringComparer.Ordinal).Count() != docks.Length)
        {
            throw new InvalidDataException("每个停靠区只能放置一个工具窗口。");
        }
    }
}

public sealed class StudioToolWindowConfiguration
{
    public const string LeftDock = "左侧";
    public const string RightDock = "右侧";
    public const string BottomDock = "底部";
    public const string VisibleState = "显示";
    public const string MinimizedState = "最小化";
    public const string HiddenState = "隐藏";

    public static IReadOnlySet<string> Docks { get; } = new HashSet<string>(
        [LeftDock, RightDock, BottomDock],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> States { get; } = new HashSet<string>(
        [VisibleState, MinimizedState, HiddenState],
        StringComparer.Ordinal);

    [JsonPropertyName("停靠")]
    public string Dock { get; set; } = LeftDock;

    [JsonPropertyName("状态")]
    public string State { get; set; } = VisibleState;

    [JsonPropertyName("尺寸")]
    public double Size { get; set; } = 260;

    public void Validate(string title)
    {
        Dock = string.IsNullOrWhiteSpace(Dock) ? LeftDock : Dock.Trim();
        State = string.IsNullOrWhiteSpace(State) ? VisibleState : State.Trim();
        if (!Docks.Contains(Dock))
        {
            throw new InvalidDataException($"工具窗口“{title}”的停靠位置“{Dock}”无效。");
        }

        if (!States.Contains(State))
        {
            throw new InvalidDataException($"工具窗口“{title}”的显示状态“{State}”无效。");
        }

        var validSize = Dock == BottomDock
            ? Size is >= 96 and <= 600
            : Size is >= 180 and <= 760;
        if (!validSize)
        {
            throw new InvalidDataException(
                Dock == BottomDock
                    ? $"工具窗口“{title}”的底部高度必须在 96 到 600 之间。"
                    : $"工具窗口“{title}”的侧边宽度必须在 180 到 760 之间。");
        }
    }
}

public sealed class StudioStartupConfiguration
{
    [JsonPropertyName("显示新建程序对话框")]
    public bool ShowNewProgramDialog { get; set; } = true;
}

public static class StudioShortcutActions
{
    public const string NewFile = "新建源码";
    public const string NewProject = "新建项目";
    public const string NewForm = "新建窗体";
    public const string OpenFile = "打开文件";
    public const string OpenFolder = "打开文件夹";
    public const string SaveFile = "保存";
    public const string SaveAs = "另存为";
    public const string CloseTab = "关闭标签";
    public const string Compile = "编译";
    public const string Run = "运行";
    public const string RunProject = "运行项目";
    public const string Stop = "停止运行";
    public const string DebugStep = "调试单步";
    public const string DebugStepOver = "调试跳过";
    public const string DebugStepOut = "调试跳出";
    public const string DebugRunToCursor = "运行到光标";
    public const string DebugToggleBreakpoint = "切换断点";
    public const string Completion = "代码补全";
    public const string Help = "上下文帮助";
    public const string FormatCode = "格式化代码";
    public const string ToggleComment = "切换注释";
    public const string DuplicateLine = "复制当前行";
    public const string DeleteLine = "删除当前行";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            NewFile,
            NewProject,
            NewForm,
            OpenFile,
            OpenFolder,
            SaveFile,
            SaveAs,
            CloseTab,
            Compile,
            Run,
            RunProject,
            Stop,
            DebugStep,
            DebugStepOver,
            DebugStepOut,
            DebugRunToCursor,
            DebugToggleBreakpoint,
            Completion,
            Help,
            FormatCode,
            ToggleComment,
            DuplicateLine,
            DeleteLine
        ],
        StringComparer.Ordinal);

    public static Dictionary<string, List<string>> CreateDefaults() => new(StringComparer.Ordinal)
    {
        [NewFile] = ["Ctrl+N"],
        [NewProject] = ["Ctrl+Alt+N"],
        [NewForm] = ["Ctrl+Shift+N"],
        [OpenFile] = ["Ctrl+O"],
        [OpenFolder] = ["Ctrl+Shift+O"],
        [SaveFile] = ["Ctrl+S"],
        [SaveAs] = ["Ctrl+Shift+S"],
        [CloseTab] = ["Ctrl+W"],
        [Compile] = ["Ctrl+B", "F7"],
        [Run] = ["F5"],
        [RunProject] = ["Ctrl+F5"],
        [Stop] = ["Shift+F5"],
        [DebugStep] = ["F8"],
        [DebugStepOver] = ["F10"],
        [DebugStepOut] = ["Shift+F8"],
        [DebugRunToCursor] = ["F4"],
        [DebugToggleBreakpoint] = ["F9"],
        [Completion] = ["Ctrl+Space"],
        [Help] = ["F1"],
        [FormatCode] = ["Ctrl+Alt+L"],
        [ToggleComment] = ["Ctrl+/"],
        [DuplicateLine] = ["Ctrl+D"],
        [DeleteLine] = ["Ctrl+Shift+K"]
    };

    public static KeyGesture ParseGesture(string value, string action)
    {
        var normalized = value.Trim();
        if (normalized.EndsWith("+/", StringComparison.Ordinal))
        {
            normalized = normalized[..^1] + "OemQuestion";
        }

        try
        {
            return new KeyGestureConverter().ConvertFromString(normalized) as KeyGesture
                ?? throw new FormatException();
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException(
                $"快捷键“{action}”的值“{value}”无效，请使用例如 Ctrl+S、F5、Ctrl+Shift+K。",
                exception);
        }
    }
}
