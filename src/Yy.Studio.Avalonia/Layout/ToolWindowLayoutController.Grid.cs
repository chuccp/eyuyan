using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Layout;

/// <summary>宿主 Grid 的定义、停靠放置、尺寸计算与拖拽捕获。</summary>
public partial class ToolWindowLayoutController
{
    /// <summary>左侧工具窗口列。</summary>
    public const int LeftPanelColumn = 0;

    /// <summary>左侧 GridSplitter 列。</summary>
    public const int LeftSplitterColumn = 1;

    /// <summary>中央编辑区列。</summary>
    public const int CenterColumn = 2;

    /// <summary>右侧 GridSplitter 列。</summary>
    public const int RightSplitterColumn = 3;

    /// <summary>右侧工具窗口列。</summary>
    public const int RightPanelColumn = 4;

    /// <summary>上部（编辑区与左右工具窗）行。</summary>
    public const int TopRow = 0;

    /// <summary>底部水平 GridSplitter 行。</summary>
    public const int BottomSplitterRow = 1;

    /// <summary>底部工具窗口行。</summary>
    public const int BottomPanelRow = 2;

    /// <summary>宿主 Grid 需要的列数。</summary>
    public const int ColumnCount = 5;

    /// <summary>宿主 Grid 需要的行数。</summary>
    public const int RowCount = 3;

    private ColumnDefinition _leftColumn = null!;
    private ColumnDefinition _leftSplitterColumn = null!;
    private ColumnDefinition _centerColumn = null!;
    private ColumnDefinition _rightSplitterColumn = null!;
    private ColumnDefinition _rightColumn = null!;
    private RowDefinition _topRow = null!;
    private RowDefinition _bottomSplitterRow = null!;
    private RowDefinition _bottomRow = null!;

    private bool ToolboxActive => _toolboxVisible && _toolboxPanel is not null;

    private void EnsureGridDefinitions()
    {
        while (_hostGrid.ColumnDefinitions.Count < ColumnCount)
        {
            _hostGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        while (_hostGrid.RowDefinitions.Count < RowCount)
        {
            _hostGrid.RowDefinitions.Add(new RowDefinition());
        }

        _leftColumn = _hostGrid.ColumnDefinitions[LeftPanelColumn];
        _leftSplitterColumn = _hostGrid.ColumnDefinitions[LeftSplitterColumn];
        _centerColumn = _hostGrid.ColumnDefinitions[CenterColumn];
        _rightSplitterColumn = _hostGrid.ColumnDefinitions[RightSplitterColumn];
        _rightColumn = _hostGrid.ColumnDefinitions[RightPanelColumn];
        _topRow = _hostGrid.RowDefinitions[TopRow];
        _bottomSplitterRow = _hostGrid.RowDefinitions[BottomSplitterRow];
        _bottomRow = _hostGrid.RowDefinitions[BottomPanelRow];

        _leftSplitterColumn.Width =
            new GridLength(ToolWindowLayoutMetrics.SplitterWidth);
        _rightSplitterColumn.Width =
            new GridLength(ToolWindowLayoutMetrics.SplitterWidth);
        _bottomSplitterRow.Height =
            new GridLength(ToolWindowLayoutMetrics.BottomSplitterHeight);
        _centerColumn.Width = new GridLength(1, GridUnitType.Star);
        _centerColumn.MinWidth = Math.Max(
            _centerColumn.MinWidth,
            ToolWindowLayoutMetrics.CenterMinWidth);
        _topRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private void PlaceChrome()
    {
        SetupColumnSplitter(_projectSplitter, LeftSplitterColumn);
        SetupColumnSplitter(_workSplitter, RightSplitterColumn);
        SetupRowSplitter(_consoleSplitter);
        SetupRail(
            _leftRail,
            LeftPanelColumn,
            TopRow,
            columnSpan: 1,
            rowSpan: RowCount,
            vertical: true);
        SetupRail(
            _rightRail,
            RightPanelColumn,
            TopRow,
            columnSpan: 1,
            rowSpan: RowCount,
            vertical: true);
        SetupRail(
            _bottomRail,
            LeftPanelColumn,
            BottomPanelRow,
            columnSpan: ColumnCount,
            rowSpan: 1,
            vertical: false);
    }

    private void SetupColumnSplitter(GridSplitter splitter, int column)
    {
        EnsureHostChild(splitter);
        splitter.Width = ToolWindowLayoutMetrics.SplitterWidth;
        splitter.Height = double.NaN;
        splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ShowsPreview = false;
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, TopRow);
        Grid.SetColumnSpan(splitter, 1);
        Grid.SetRowSpan(splitter, 1);
    }

    private void SetupRowSplitter(GridSplitter splitter)
    {
        EnsureHostChild(splitter);
        splitter.Width = double.NaN;
        splitter.Height = ToolWindowLayoutMetrics.BottomSplitterHeight;
        splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
        splitter.ResizeDirection = GridResizeDirection.Rows;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ShowsPreview = false;
        Grid.SetColumn(splitter, LeftPanelColumn);
        Grid.SetRow(splitter, BottomSplitterRow);
        Grid.SetColumnSpan(splitter, ColumnCount);
        Grid.SetRowSpan(splitter, 1);
    }

    private void SetupRail(
        Button rail,
        int column,
        int row,
        int columnSpan,
        int rowSpan,
        bool vertical)
    {
        EnsureHostChild(rail);
        rail.ZIndex = 20;
        Grid.SetColumn(rail, column);
        Grid.SetRow(rail, row);
        Grid.SetColumnSpan(rail, columnSpan);
        Grid.SetRowSpan(rail, rowSpan);

        if (vertical)
        {
            rail.Width = ToolWindowLayoutMetrics.RailSize;
            rail.Height = double.NaN;
            rail.HorizontalAlignment = HorizontalAlignment.Right;
            rail.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            rail.Width = double.NaN;
            rail.Height = ToolWindowLayoutMetrics.RailSize;
            rail.HorizontalAlignment = HorizontalAlignment.Stretch;
            rail.VerticalAlignment = VerticalAlignment.Top;
        }
    }

    private void ApplyCore()
    {
        EnsureGridDefinitions();
        EnsureHostChild(_projectPanel);
        EnsureHostChild(_workPanel);
        EnsureHostChild(_consolePanel);
        if (_toolboxPanel is not null)
        {
            EnsureHostChild(_toolboxPanel);
        }

        PlaceChrome();
        ApplyWorkTabSelection();

        var toolboxActive = ToolboxActive;
        foreach (var kind in AllKinds)
        {
            var configuration = ConfigFor(kind);
            var panel = PanelFor(kind);
            panel.IsVisible = ToolWindowLayoutProtocol.IsVisible(
                configuration.State);
            PlaceToolWindow(panel, configuration.Dock);
            panel.Margin = ToolWindowMargin(configuration.Dock, toolboxActive);
            panel.ZIndex = 10;
        }

        if (_toolboxPanel is not null)
        {
            PlaceToolbox(_toolboxPanel);
            _toolboxPanel.IsVisible = _toolboxVisible;
        }

        UpdateDockColumns(toolboxActive);
        UpdateMinimizedRails(toolboxActive);
        ApplyStatusBarVisibility(_layout.ShowStatusBar);
    }

    private static void PlaceToolWindow(Control view, string dock)
    {
        Grid.SetColumnSpan(view, 1);
        Grid.SetRowSpan(view, 1);
        switch (dock)
        {
            case StudioToolWindowConfiguration.RightDock:
                Grid.SetColumn(view, RightPanelColumn);
                Grid.SetRow(view, TopRow);
                break;
            case StudioToolWindowConfiguration.BottomDock:
                Grid.SetColumn(view, LeftPanelColumn);
                Grid.SetColumnSpan(view, ColumnCount);
                Grid.SetRow(view, BottomPanelRow);
                break;
            default:
                Grid.SetColumn(view, LeftPanelColumn);
                Grid.SetRow(view, TopRow);
                break;
        }
    }

    private Thickness ToolWindowMargin(
        string dock,
        bool toolboxActive)
    {
        // 外缘 6,面向相邻面板的内缝 1;与 4px 分割条合计成统一的 6px 缝。
        var outer = ToolWindowLayoutMetrics.PanelInset;
        var inner = ToolWindowLayoutMetrics.PanelGapInset;
        if (string.Equals(dock, StudioToolWindowConfiguration.RightDock, StringComparison.Ordinal))
        {
            var right = toolboxActive ? _toolboxWidth + inner : outer;
            return new Thickness(inner, 4, right, inner);
        }

        if (string.Equals(dock, StudioToolWindowConfiguration.BottomDock, StringComparison.Ordinal))
        {
            return new Thickness(outer, inner, outer, outer);
        }

        return new Thickness(outer, 4, inner, inner);
    }

    private void PlaceToolbox(Control toolbox)
    {
        Grid.SetColumn(toolbox, RightPanelColumn);
        Grid.SetRow(toolbox, TopRow);
        Grid.SetColumnSpan(toolbox, 1);
        Grid.SetRowSpan(toolbox, 1);
        toolbox.Width = _toolboxWidth;
        toolbox.Height = double.NaN;
        toolbox.HorizontalAlignment = HorizontalAlignment.Right;
        toolbox.VerticalAlignment = VerticalAlignment.Stretch;
        toolbox.Margin = new Thickness(
            ToolWindowLayoutMetrics.PanelGapInset,
            4,
            ToolWindowLayoutMetrics.PanelInset,
            ToolWindowLayoutMetrics.PanelGapInset);
        toolbox.ZIndex = 9;
    }

    private void UpdateDockColumns(bool toolboxActive)
    {
        var left = FindToolWindowByDock(StudioToolWindowConfiguration.LeftDock);
        ApplyColumn(
            _leftColumn,
            _projectSplitter,
            left?.State ?? StudioToolWindowConfiguration.HiddenState,
            left?.Size ?? 260,
            ToolWindowLayoutMetrics.SideMinSize,
            extraRail: 0);

        var right = FindToolWindowByDock(StudioToolWindowConfiguration.RightDock);
        var extraRail = toolboxActive ? _toolboxWidth : 0;
        ApplyColumn(
            _rightColumn,
            _workSplitter,
            right?.State ?? StudioToolWindowConfiguration.HiddenState,
            right?.Size ?? 360,
            toolboxActive
                ? ToolWindowLayoutMetrics.WorkMinSizeWithToolbox
                : ToolWindowLayoutMetrics.WorkMinSize,
            extraRail);

        var bottom = FindToolWindowByDock(
            StudioToolWindowConfiguration.BottomDock);
        ApplyBottomRow(
            _bottomRow,
            _consoleSplitter,
            bottom?.State ?? StudioToolWindowConfiguration.HiddenState,
            bottom?.Size ?? 172);
    }

    private static void ApplyColumn(
        ColumnDefinition column,
        GridSplitter splitter,
        string state,
        double configuredWidth,
        double minimum,
        double extraRail)
    {
        column.MaxWidth = ToolWindowLayoutMetrics.SideMaxSize;
        switch (state)
        {
            case StudioToolWindowConfiguration.VisibleState:
                column.MinWidth = minimum + extraRail;
                column.Width = new GridLength(
                    Math.Clamp(
                        configuredWidth,
                        minimum + extraRail,
                        ToolWindowLayoutMetrics.SideMaxSize));
                splitter.IsVisible = true;
                break;
            case StudioToolWindowConfiguration.MinimizedState:
                column.MinWidth =
                    ToolWindowLayoutMetrics.RailSize + extraRail;
                column.Width = new GridLength(
                    ToolWindowLayoutMetrics.RailSize + extraRail);
                splitter.IsVisible = true;
                break;
            default:
                column.MinWidth = extraRail;
                column.Width = new GridLength(extraRail);
                splitter.IsVisible = extraRail > 0;
                break;
        }
    }

    private static void ApplyBottomRow(
        RowDefinition row,
        GridSplitter splitter,
        string state,
        double configuredHeight)
    {
        row.MaxHeight = ToolWindowLayoutMetrics.BottomMaxSize;
        switch (state)
        {
            case StudioToolWindowConfiguration.VisibleState:
                row.MinHeight = ToolWindowLayoutMetrics.BottomMinSize;
                row.Height = new GridLength(
                    Math.Clamp(
                        configuredHeight,
                        ToolWindowLayoutMetrics.BottomMinSize,
                        ToolWindowLayoutMetrics.BottomMaxSize));
                splitter.IsVisible = true;
                break;
            case StudioToolWindowConfiguration.MinimizedState:
                row.MinHeight = ToolWindowLayoutMetrics.RailSize;
                row.Height =
                    new GridLength(ToolWindowLayoutMetrics.RailSize);
                splitter.IsVisible = false;
                break;
            default:
                row.MinHeight = 0;
                row.Height = new GridLength(0);
                splitter.IsVisible = false;
                break;
        }
    }

    private void CaptureGeometry()
    {
        _layout.ShowStatusBar =
            _statusBar?.IsVisible ?? _layout.ShowStatusBar;
        CaptureToolWindowSize(_layout.Project);
        CaptureToolWindowSize(_layout.WorkTools);
        CaptureToolWindowSize(_layout.Console);
        _layout.RightToolsTab = _workTabs is { ItemCount: > 0 } tabs
            ? Math.Clamp(tabs.SelectedIndex, 0, 1)
            : Math.Clamp(_layout.RightToolsTab, 0, 1);
    }

    private void CaptureToolWindowSize(StudioToolWindowConfiguration config)
    {
        if (!ToolWindowLayoutProtocol.IsVisible(config.State))
        {
            return;
        }

        var size = config.Dock switch
        {
            StudioToolWindowConfiguration.LeftDock => _leftColumn.Width.Value,
            StudioToolWindowConfiguration.RightDock => _rightColumn.Width.Value,
            _ => _bottomRow.Height.Value
        };
        if (double.IsFinite(size) && size > 0)
        {
            config.Size = ToolWindowLayoutProtocol.ClampSize(
                size,
                config.Dock);
        }
    }

    private StudioToolWindowConfiguration? FindToolWindowByDock(string dock)
    {
        foreach (var kind in AllKinds)
        {
            var configuration = ConfigFor(kind);
            if (string.Equals(
                    configuration.Dock,
                    dock,
                    StringComparison.Ordinal))
            {
                return configuration;
            }
        }

        return null;
    }

    private void ApplyWorkTabSelection()
    {
        if (_workTabs is null || _workTabs.ItemCount == 0)
        {
            return;
        }

        _suppressTabNotification = true;
        try
        {
            _workTabs.SelectedIndex = Math.Clamp(
                _layout.RightToolsTab,
                0,
                _workTabs.ItemCount - 1);
        }
        finally
        {
            _suppressTabNotification = false;
        }
    }

    private void ApplyStatusBarVisibility(bool visible)
    {
        if (_statusBar is not null)
        {
            _statusBar.IsVisible = visible;
        }
    }

    private void EnsureHostChild(Control control)
    {
        if (ReferenceEquals(control.Parent, _hostGrid))
        {
            return;
        }

        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl content:
                content.Content = null;
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }

        if (!_hostGrid.Children.Contains(control))
        {
            _hostGrid.Children.Add(control);
        }
    }

    private static GridSplitter CreateColumnSplitter() => new()
    {
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = false
    };

    private static GridSplitter CreateRowSplitter() => new()
    {
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Rows,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = false
    };

    private void OnSplitterDragCompleted(
        object? sender,
        VectorEventArgs e)
    {
        CaptureGeometry();
        NotifyLayoutChanged("splitter", capture: false);
    }
}
