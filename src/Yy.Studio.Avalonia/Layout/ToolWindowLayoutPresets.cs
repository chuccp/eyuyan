using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Layout;

/// <summary>
/// 与 WPF <c>MainWindow.WindowLayout.cs</c> 相同的布局预设。
/// “自定义”是用户操作后的标记，不是可构造的布局模板。
/// </summary>
public static class ToolWindowLayoutPresets
{
    /// <summary>“默认布局”是“默认”的别名，便于按界面文案调用。</summary>
    public const string DefaultPresetAlias = "默认布局";

    /// <summary>菜单中可显示的预设名称，顺序与 WPF 一致。</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        StudioWindowLayoutConfiguration.DefaultPreset,
        StudioWindowLayoutConfiguration.FocusCodePreset,
        StudioWindowLayoutConfiguration.DesignerPreset,
        StudioWindowLayoutConfiguration.CustomPreset
    };

    /// <summary>
    /// 创建预设布局。仅支持默认、专注代码和窗体设计；
    /// “自定义”由控制器就地标记当前布局。
    /// </summary>
    public static StudioWindowLayoutConfiguration Create(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            throw new ArgumentException("布局预设不能为空。", nameof(preset));
        }

        preset = preset.Trim();
        return preset switch
        {
            DefaultPresetAlias =>
                Build(StudioWindowLayoutConfiguration.DefaultPreset),
            StudioWindowLayoutConfiguration.DefaultPreset =>
                Build(StudioWindowLayoutConfiguration.DefaultPreset),
            StudioWindowLayoutConfiguration.FocusCodePreset =>
                Build(StudioWindowLayoutConfiguration.FocusCodePreset),
            StudioWindowLayoutConfiguration.DesignerPreset =>
                Build(
                    StudioWindowLayoutConfiguration.DesignerPreset,
                    rightTab: 1,
                    showBottom: false),
            StudioWindowLayoutConfiguration.CustomPreset => throw new ArgumentException(
                "“自定义”不是可构造的布局模板，请使用 SetPreset 标记当前布局。",
                nameof(preset)),
            _ => throw new ArgumentException(
                $"未知窗口布局预设“{preset}”。", nameof(preset))
        };
    }

    private static StudioWindowLayoutConfiguration Build(
        string preset,
        int rightTab = 0,
        bool showBottom = true)
    {
        var hidden = preset == StudioWindowLayoutConfiguration.FocusCodePreset;
        return new StudioWindowLayoutConfiguration
        {
            Preset = preset,
            ShowStatusBar = true,
            Project = new StudioToolWindowConfiguration
            {
                Dock = StudioToolWindowConfiguration.LeftDock,
                State = hidden
                    ? StudioToolWindowConfiguration.HiddenState
                    : StudioToolWindowConfiguration.VisibleState,
                Size = 260
            },
            WorkTools = new StudioToolWindowConfiguration
            {
                Dock = StudioToolWindowConfiguration.RightDock,
                State = hidden
                    ? StudioToolWindowConfiguration.HiddenState
                    : StudioToolWindowConfiguration.VisibleState,
                Size = 360
            },
            Console = new StudioToolWindowConfiguration
            {
                Dock = StudioToolWindowConfiguration.BottomDock,
                State = hidden || !showBottom
                    ? StudioToolWindowConfiguration.HiddenState
                    : StudioToolWindowConfiguration.VisibleState,
                Size = 172
            },
            RightToolsTab = rightTab
        };
    }
}
