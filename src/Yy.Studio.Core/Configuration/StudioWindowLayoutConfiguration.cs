using System.IO;
using System.Text.Json.Serialization;

namespace Yy.Studio.Core.Configuration;

/// <summary>IDEA 式无缝停靠布局。每个停靠区只能有一个活动工具窗口。</summary>
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
