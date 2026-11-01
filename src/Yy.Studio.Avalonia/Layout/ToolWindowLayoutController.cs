using Avalonia.Controls;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Layout;

/// <summary>
/// 把 WPF 版 IDEA 式工具窗口布局迁移为独立、可复用的 Avalonia 控制器。
/// <para>
/// 宿主 Grid 约定为五列三行：<br/>
/// 第 0 列 = 左侧工具窗，第 1 列 = 左侧分隔条，第 2 列 = 中央编辑区，
/// 第 3 列 = 右侧分隔条，第 4 列 = 右侧工具窗；<br/>
/// 第 0 行 = 编辑区工具窗，第 1 行 = 底部水平分隔条，第 2 行 = 底部工具窗。
/// 底部工具窗横跨五列，中央编辑区始终位于第 2 列第 0 行并保持独立。
/// </para>
/// </summary>
public partial class ToolWindowLayoutController
{
    private static readonly ToolWindowKind[] AllKinds =
    [
        ToolWindowKind.Project,
        ToolWindowKind.Work,
        ToolWindowKind.Console
    ];

    private readonly Grid _hostGrid;
    private readonly Control _projectPanel;
    private readonly Control _workPanel;
    private readonly Control _consolePanel;
    private readonly Control? _statusBar;
    private readonly GridSplitter _projectSplitter;
    private readonly GridSplitter _workSplitter;
    private readonly GridSplitter _consoleSplitter;
    private readonly Control? _toolboxPanel;
    private readonly Button _leftRail;
    private readonly Button _rightRail;
    private readonly Button _bottomRail;

    private StudioWindowLayoutConfiguration _layout = new();
    private TabControl? _workTabs;
    private bool _suppressTabNotification;
    private bool _toolboxVisible;
    private double _toolboxWidth = ToolWindowLayoutMetrics.DefaultToolboxWidth;

    /// <summary>
    /// 创建工具窗口布局控制器。面板和分隔条若已在其它父级中会被移入宿主 Grid。
    /// </summary>
    /// <param name="hostGrid">五列三行的宿主 Grid。</param>
    /// <param name="projectPanel">项目工具窗口。</param>
    /// <param name="workPanel">程序/属性工具窗口。</param>
    /// <param name="consolePanel">控制台工具窗口。</param>
    /// <param name="statusBar">应用状态栏；可为 null（仅更新配置）。</param>
    /// <param name="projectSplitter">左侧分隔条；省略时自动创建。</param>
    /// <param name="workSplitter">右侧分隔条；省略时自动创建。</param>
    /// <param name="consoleSplitter">底部水平分隔条；省略时自动创建。</param>
    /// <param name="workTabControl">右侧“程序/属性”标签页；省略时仍可使用布局 API。</param>
    /// <param name="toolboxPanel">窗体设计器组件箱；省略时右侧不预留组件箱宽度。</param>
    public ToolWindowLayoutController(
        Grid hostGrid,
        Control projectPanel,
        Control workPanel,
        Control consolePanel,
        Control? statusBar = null,
        GridSplitter? projectSplitter = null,
        GridSplitter? workSplitter = null,
        GridSplitter? consoleSplitter = null,
        TabControl? workTabControl = null,
        Control? toolboxPanel = null)
    {
        _hostGrid = hostGrid ?? throw new ArgumentNullException(nameof(hostGrid));
        _projectPanel = projectPanel ??
            throw new ArgumentNullException(nameof(projectPanel));
        _workPanel = workPanel ?? throw new ArgumentNullException(nameof(workPanel));
        _consolePanel = consolePanel ??
            throw new ArgumentNullException(nameof(consolePanel));
        if (ReferenceEquals(projectPanel, workPanel) ||
            ReferenceEquals(projectPanel, consolePanel) ||
            ReferenceEquals(workPanel, consolePanel))
        {
            throw new ArgumentException("三个工具窗口面板必须使用不同控件。");
        }

        _statusBar = statusBar;
        _projectSplitter = projectSplitter ?? CreateColumnSplitter();
        _workSplitter = workSplitter ?? CreateColumnSplitter();
        _consoleSplitter = consoleSplitter ?? CreateRowSplitter();
        _toolboxPanel = toolboxPanel;
        _toolboxVisible = toolboxPanel?.IsVisible == true;
        _workTabs = workTabControl;

        _leftRail = CreateRail();
        _rightRail = CreateRail();
        _bottomRail = CreateRail();

        EnsureGridDefinitions();
        PlaceChrome();
        _projectSplitter.DragCompleted += OnSplitterDragCompleted;
        _workSplitter.DragCompleted += OnSplitterDragCompleted;
        _consoleSplitter.DragCompleted += OnSplitterDragCompleted;

        if (_workTabs is not null)
        {
            _workTabs.SelectionChanged += OnWorkTabsSelectionChanged;
        }

        Apply(new StudioWindowLayoutConfiguration());
    }

    /// <summary>控制器修改布局后触发；宿主可据此刷新菜单并持久化。</summary>
    public event EventHandler<ToolWindowLayoutChangedEventArgs>? LayoutChanged;

    /// <summary>用户操作需要写回 StudioConfiguration 时触发。</summary>
    public event EventHandler? SaveRequested;

    /// <summary>当前布局配置；控制器就地修改该对象。</summary>
    public StudioWindowLayoutConfiguration Configuration => _layout;

    /// <summary>状态栏当前是否可见。</summary>
    public bool StatusBarVisible =>
        _statusBar?.IsVisible ?? _layout.ShowStatusBar;

    /// <summary>组件箱是否可见；仅当传入组件箱面板时才会占用右侧空间。</summary>
    public bool ToolboxVisible => _toolboxVisible;

    /// <summary>组件箱占用的右侧宽度，默认 132。</summary>
    public double ToolboxWidth
    {
        get => _toolboxWidth;
        set
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ToolboxWidth),
                    "组件箱宽度必须是有限数值。");
            }

            var width = Math.Clamp(value, 0, 400);
            if (Math.Abs(width - _toolboxWidth) < 0.1)
            {
                return;
            }

            _toolboxWidth = width;
            ApplyCore();
        }
    }

    /// <summary>右侧活动页；未挂接标签控件时为配置值。</summary>
    public int RightToolsTab => _workTabs is { ItemCount: > 0 } tabs
        ? Math.Clamp(tabs.SelectedIndex, 0, 1)
        : Math.Clamp(_layout.RightToolsTab, 0, 1);

    /// <summary>
    /// 应用布局配置。该方法不触发 <see cref="LayoutChanged"/> 与
    /// <see cref="SaveRequested"/>，避免加载配置时形成保存回环。
    /// </summary>
    /// <exception cref="InvalidDataException">配置结构或尺寸不合法。</exception>
    public void Apply(
        StudioWindowLayoutConfiguration layout,
        bool raiseChanged = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Validate();
        _layout = layout;
        if (_toolboxPanel is not null)
        {
            _toolboxVisible = _toolboxPanel.IsVisible;
        }

        ApplyCore();

        if (raiseChanged)
        {
            LayoutChanged?.Invoke(
                this,
                new ToolWindowLayoutChangedEventArgs(_layout, "apply"));
        }
    }

    /// <summary>把 Grid 的实际尺寸、状态栏和右侧活动页写回配置对象。</summary>
    public StudioWindowLayoutConfiguration CaptureConfiguration()
    {
        CaptureGeometry();
        return _layout;
    }

    /// <summary>
    /// 捕获当前布局并发布保存通知。控制器不直接写文件；宿主应在
    /// <see cref="SaveRequested"/> 中调用 <c>StudioConfiguration.Save()</c>。
    /// </summary>
    public void SaveConfiguration()
    {
        CaptureGeometry();
        LayoutChanged?.Invoke(
            this,
            new ToolWindowLayoutChangedEventArgs(_layout, "保存"));
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>显示工具窗口。</summary>
    public void Show(ToolWindowKind kind) =>
        SetState(kind, ToolWindowState.Visible);

    /// <summary>隐藏工具窗口。</summary>
    public void Hide(ToolWindowKind kind) =>
        SetState(kind, ToolWindowState.Hidden);

    /// <summary>最小化为停靠边缘的可恢复标签。</summary>
    public void Minimize(ToolWindowKind kind) =>
        SetState(kind, ToolWindowState.Minimized);

    /// <summary>
    /// 设置工具窗口尺寸：侧边 180–760，底部 96–600。
    /// 尺寸按当前停靠位置自动收紧。
    /// </summary>
    public void SetSize(ToolWindowKind kind, double size)
    {
        if (!double.IsFinite(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "工具窗口尺寸必须是有限数值。");
        }

        var configuration = ConfigFor(kind);
        configuration.Size = ToolWindowLayoutProtocol.ClampSize(
            size,
            configuration.Dock);
        ApplyCore();
        NotifyLayoutChanged("size", kind);
    }

    /// <summary>移动工具窗口到目标停靠区；目标已有窗口时二者交换。</summary>
    public void MoveDock(ToolWindowKind kind, ToolWindowDock dock) =>
        MoveDock(kind, ToolWindowLayoutProtocol.DockName(dock));

    /// <summary>使用 Core 的“左侧/右侧/底部”字符串移动停靠区。</summary>
    public void MoveDock(ToolWindowKind kind, string dock)
    {
        if (string.IsNullOrWhiteSpace(dock))
        {
            throw new ArgumentException("停靠位置不能为空。", nameof(dock));
        }

        var targetDock = dock.Trim();
        if (!StudioToolWindowConfiguration.Docks.Contains(targetDock))
        {
            throw new ArgumentException(
                $"未知停靠位置“{dock}”。", nameof(dock));
        }

        var source = ConfigFor(kind);
        if (string.Equals(source.Dock, targetDock, StringComparison.Ordinal))
        {
            Show(kind);
            return;
        }

        var sourceDock = source.Dock;
        foreach (var otherKind in AllKinds)
        {
            if (otherKind == kind)
            {
                continue;
            }

            var occupant = ConfigFor(otherKind);
            if (!string.Equals(occupant.Dock, targetDock, StringComparison.Ordinal))
            {
                continue;
            }

            occupant.Dock = sourceDock;
            occupant.Size = ToolWindowLayoutProtocol.ClampSize(
                occupant.Size,
                sourceDock);
            break;
        }

        source.Dock = targetDock;
        source.Size = ToolWindowLayoutProtocol.ClampSize(
            source.Size,
            targetDock);
        ApplyCore();
        NotifyLayoutChanged("dock", kind);
    }

    /// <summary>
    /// 应用布局预设。支持 Core 定义的默认、专注代码、窗体设计和自定义；
    /// “自定义”只标记当前布局，不重置尺寸。
    /// </summary>
    public void SetPreset(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            throw new ArgumentException("布局预设不能为空。", nameof(preset));
        }

        preset = preset.Trim();
        if (string.Equals(
                preset,
                StudioWindowLayoutConfiguration.CustomPreset,
                StringComparison.Ordinal))
        {
            _layout.Preset = StudioWindowLayoutConfiguration.CustomPreset;
            NotifyLayoutChanged(
                "preset",
                markCustom: false,
                capture: false);
            return;
        }

        var template = ToolWindowLayoutPresets.Create(preset);
        CopyLayout(template);
        ApplyCore();
        NotifyLayoutChanged("preset", markCustom: false, capture: false);
    }

    /// <summary>显示右侧工具窗口并切换到指定标签页（0 程序，1 属性）。</summary>
    public void ShowWorkTab(int index)
    {
        _layout.WorkTools.State = StudioToolWindowConfiguration.VisibleState;
        var maximum = _workTabs is { ItemCount: > 0 } tabs ? tabs.ItemCount - 1 : 1;
        var selected = Math.Clamp(index, 0, Math.Min(maximum, 1));
        _layout.RightToolsTab = selected;
        ApplyCore();
        NotifyLayoutChanged(
            "work-tab",
            ToolWindowKind.Work,
            capture: false);
    }

    /// <summary>显示或隐藏状态栏。</summary>
    public void SetStatusBarVisible(bool visible)
    {
        _layout.ShowStatusBar = visible;
        ApplyStatusBarVisibility(visible);
        NotifyLayoutChanged("status-bar");
    }

    /// <summary>显示或隐藏组件箱，并重新计算右侧工具窗口宽度。</summary>
    public void SetToolboxVisible(bool visible)
    {
        _toolboxVisible = visible;
        ApplyCore();
        NotifyLayoutChanged("toolbox");
    }

    /// <summary>挂接或替换右侧“程序/属性”标签控件。</summary>
    public void AttachWorkTabControl(TabControl? tabs)
    {
        if (ReferenceEquals(_workTabs, tabs))
        {
            return;
        }

        if (_workTabs is not null)
        {
            _workTabs.SelectionChanged -= OnWorkTabsSelectionChanged;
        }

        _workTabs = tabs;
        if (_workTabs is not null)
        {
            _workTabs.SelectionChanged += OnWorkTabsSelectionChanged;
        }

        ApplyCore();
    }

    /// <summary>读取工具窗口显示状态。</summary>
    public ToolWindowState GetState(ToolWindowKind kind) =>
        ToolWindowLayoutProtocol.ParseState(ConfigFor(kind).State);

    /// <summary>读取工具窗口停靠位置。</summary>
    public ToolWindowDock GetDock(ToolWindowKind kind) =>
        ToolWindowLayoutProtocol.ParseDock(ConfigFor(kind).Dock);

    /// <summary>读取工具窗口在 Core 配置中的对象。</summary>
    public StudioToolWindowConfiguration GetConfiguration(ToolWindowKind kind) =>
        ConfigFor(kind);

    /// <summary>工具窗口标题；右侧窗口随活动标签页在“程序/属性”之间变化。</summary>
    public string GetTitle(ToolWindowKind kind)
    {
        if (kind == ToolWindowKind.Work)
        {
            return RightToolsTab == 1
                ? ToolWindowLayoutProtocol.PropertiesTitle
                : ToolWindowLayoutProtocol.ProgramTitle;
        }

        return kind == ToolWindowKind.Project
            ? ToolWindowLayoutProtocol.ProjectTitle
            : ToolWindowLayoutProtocol.ConsoleTitle;
    }

    private void SetState(ToolWindowKind kind, ToolWindowState state)
    {
        ConfigFor(kind).State = ToolWindowLayoutProtocol.StateName(state);
        ApplyCore();
        NotifyLayoutChanged("state", kind);
    }

    private void NotifyLayoutChanged(
        string reason,
        ToolWindowKind? kind = null,
        bool markCustom = true,
        bool capture = true)
    {
        if (capture)
        {
            CaptureGeometry();
        }

        if (markCustom)
        {
            _layout.Preset = StudioWindowLayoutConfiguration.CustomPreset;
        }

        LayoutChanged?.Invoke(
            this,
            new ToolWindowLayoutChangedEventArgs(_layout, reason, kind));
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWorkTabsSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressTabNotification || _workTabs is null)
        {
            return;
        }

        var index = Math.Clamp(_workTabs.SelectedIndex, 0, 1);
        var changed = _layout.RightToolsTab != index;
        _layout.RightToolsTab = index;
        UpdateMinimizedRails(ToolboxActive);
        if (changed)
        {
            NotifyLayoutChanged(
                "work-tab",
                ToolWindowKind.Work,
                markCustom: false,
                capture: false);
        }
    }

    private void CopyLayout(StudioWindowLayoutConfiguration source)
    {
        _layout.Preset = source.Preset;
        _layout.ShowStatusBar = source.ShowStatusBar;
        _layout.RightToolsTab = source.RightToolsTab;
        CopyToolWindow(_layout.Project, source.Project);
        CopyToolWindow(_layout.WorkTools, source.WorkTools);
        CopyToolWindow(_layout.Console, source.Console);
    }

    private static void CopyToolWindow(
        StudioToolWindowConfiguration target,
        StudioToolWindowConfiguration source)
    {
        target.Dock = source.Dock;
        target.State = source.State;
        target.Size = source.Size;
    }

    private StudioToolWindowConfiguration ConfigFor(ToolWindowKind kind) => kind switch
    {
        ToolWindowKind.Project => _layout.Project,
        ToolWindowKind.Work => _layout.WorkTools,
        ToolWindowKind.Console => _layout.Console,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private Control PanelFor(ToolWindowKind kind) => kind switch
    {
        ToolWindowKind.Project => _projectPanel,
        ToolWindowKind.Work => _workPanel,
        ToolWindowKind.Console => _consolePanel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
