using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Layout;

/// <summary>IDE 式停靠布局中的三个工具窗口。</summary>
public enum ToolWindowKind
{
    Project,
    Work,
    Console
}

/// <summary>工具窗口可停靠的三个区域。</summary>
public enum ToolWindowDock
{
    Left,
    Right,
    Bottom
}

/// <summary>工具窗口的显示状态。</summary>
public enum ToolWindowState
{
    Visible,
    Minimized,
    Hidden
}

/// <summary>布局控制器修改配置后的事件参数。</summary>
public sealed class ToolWindowLayoutChangedEventArgs : EventArgs
{
    public ToolWindowLayoutChangedEventArgs(
        StudioWindowLayoutConfiguration configuration,
        string reason,
        ToolWindowKind? kind = null)
    {
        Configuration = configuration ??
            throw new ArgumentNullException(nameof(configuration));
        Reason = reason ?? string.Empty;
        Kind = kind;
    }

    /// <summary>与控制器共享的布局配置对象，可直接交给 StudioConfiguration。</summary>
    public StudioWindowLayoutConfiguration Configuration { get; }

    /// <summary>变更原因：state、dock、preset、work-tab、status-bar、toolbox、保存。</summary>
    public string Reason { get; }

    /// <summary>与本次变更相关的工具窗口；布局级变更时为空。</summary>
    public ToolWindowKind? Kind { get; }
}

/// <summary>Core 配置字符串与公开枚举之间的唯一转换点。</summary>
internal static class ToolWindowLayoutProtocol
{
    public const string ProjectTitle = "项目";
    public const string ProgramTitle = "程序";
    public const string PropertiesTitle = "属性";
    public const string ConsoleTitle = "控制台";

    public static string DockName(ToolWindowDock dock) => dock switch
    {
        ToolWindowDock.Right => StudioToolWindowConfiguration.RightDock,
        ToolWindowDock.Bottom => StudioToolWindowConfiguration.BottomDock,
        _ => StudioToolWindowConfiguration.LeftDock
    };

    public static ToolWindowDock ParseDock(string? dock) => dock switch
    {
        StudioToolWindowConfiguration.RightDock => ToolWindowDock.Right,
        StudioToolWindowConfiguration.BottomDock => ToolWindowDock.Bottom,
        _ => ToolWindowDock.Left
    };

    public static string StateName(ToolWindowState state) => state switch
    {
        ToolWindowState.Minimized => StudioToolWindowConfiguration.MinimizedState,
        ToolWindowState.Hidden => StudioToolWindowConfiguration.HiddenState,
        _ => StudioToolWindowConfiguration.VisibleState
    };

    public static ToolWindowState ParseState(string? state) => state switch
    {
        StudioToolWindowConfiguration.MinimizedState => ToolWindowState.Minimized,
        StudioToolWindowConfiguration.HiddenState => ToolWindowState.Hidden,
        _ => ToolWindowState.Visible
    };

    public static double ClampSize(double size, string dock)
    {
        if (!double.IsFinite(size))
        {
            size = dock == StudioToolWindowConfiguration.BottomDock
                ? ToolWindowLayoutMetrics.BottomMinSize
                : ToolWindowLayoutMetrics.SideMinSize;
        }

        return dock == StudioToolWindowConfiguration.BottomDock
            ? Math.Clamp(size, ToolWindowLayoutMetrics.BottomMinSize,
                ToolWindowLayoutMetrics.BottomMaxSize)
            : Math.Clamp(size, ToolWindowLayoutMetrics.SideMinSize,
                ToolWindowLayoutMetrics.SideMaxSize);
    }

    public static bool IsVisible(string? state) =>
        state == StudioToolWindowConfiguration.VisibleState;

    public static bool IsMinimized(string? state) =>
        state == StudioToolWindowConfiguration.MinimizedState;
}

/// <summary>与 WPF 布局保持一致的尺寸常量。</summary>
internal static class ToolWindowLayoutMetrics
{
    public const double RailSize = 28;
    public const double PanelInset = 6;
    /// <summary>面板面向相邻面板一侧的内缝;与分割条合计 1+4+1=6,全布局统一。</summary>
    public const double PanelGapInset = 1;
    public const double SplitterWidth = 4;
    public const double BottomSplitterHeight = 4;
    public const double SideMinSize = 180;
    public const double WorkMinSize = 220;
    public const double WorkMinSizeWithToolbox = 320;
    public const double SideMaxSize = 760;
    public const double BottomMinSize = 96;
    public const double BottomMaxSize = 600;
    public const double DefaultToolboxWidth = 132;
    public const double CenterMinWidth = 420;
}
