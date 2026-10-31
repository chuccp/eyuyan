using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Yy.Studio;

/// <summary>IDEA 式工具窗口停靠、隐藏、最小化和可持久化布局。</summary>
public partial class MainWindow
{
    private const double ToolWindowRailSize = 28;

    private enum ToolWindowKind
    {
        Project,
        Work,
        Console
    }

    private void ApplyWindowLayoutConfiguration()
    {
        Configuration.WindowLayout.Validate();
        WorkTabs.SelectedIndex = Math.Clamp(
            Configuration.WindowLayout.RightToolsTab,
            0,
            WorkTabs.Items.Count - 1);
        ApplyDockedToolWindows();
        SetStatusBarVisible(Configuration.WindowLayout.ShowStatusBar);
    }

    private void EnsureWorkspaceChild(FrameworkElement view)
    {
        if (ReferenceEquals(view.Parent, WorkspaceGrid))
        {
            return;
        }

        if (view.Parent is Panel panel)
        {
            panel.Children.Remove(view);
        }
        else if (view.Parent is ContentControl content)
        {
            content.Content = null;
        }
        else if (view.Parent is Decorator decorator)
        {
            decorator.Child = null;
        }

        WorkspaceGrid.Children.Add(view);
    }

    private void ApplyDockedToolWindows()
    {
        EnsureWorkspaceChild(ProjectExplorerPanel);
        EnsureWorkspaceChild(RightToolsPanel);
        EnsureWorkspaceChild(BottomToolWindowPanel);

        foreach (var kind in Enum.GetValues<ToolWindowKind>())
        {
            var configuration = ToolWindowConfiguration(kind);
            var view = ToolWindowView(kind);
            var visible = configuration.State ==
                StudioToolWindowConfiguration.VisibleState;
            view.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PlaceToolWindow(view, configuration.Dock);
            Panel.SetZIndex(view, 10);
        }

        var toolboxVisible = InspectorPanel.Visibility == Visibility.Visible;
        foreach (var kind in Enum.GetValues<ToolWindowKind>())
        {
            var configuration = ToolWindowConfiguration(kind);
            var view = ToolWindowView(kind);
            view.Margin =
                configuration.Dock == StudioToolWindowConfiguration.RightDock &&
                toolboxVisible &&
                configuration.State == StudioToolWindowConfiguration.VisibleState
                    ? new Thickness(0, 0, 132, 0)
                    : new Thickness(0);
        }

        UpdateDockColumns(toolboxVisible);
        UpdateMinimizedRails(toolboxVisible);
    }

    private static void PlaceToolWindow(FrameworkElement view, string dock)
    {
        switch (dock)
        {
            case StudioToolWindowConfiguration.RightDock:
                Grid.SetColumn(view, 4);
                Grid.SetRow(view, 0);
                Grid.SetRowSpan(view, 3);
                break;
            case StudioToolWindowConfiguration.BottomDock:
                Grid.SetColumn(view, 2);
                Grid.SetRow(view, 2);
                Grid.SetRowSpan(view, 1);
                break;
            default:
                Grid.SetColumn(view, 0);
                Grid.SetRow(view, 0);
                Grid.SetRowSpan(view, 3);
                break;
        }
    }

    private void UpdateDockColumns(bool toolboxVisible)
    {
        var left = FindToolWindowByDock(StudioToolWindowConfiguration.LeftDock);
        var leftState = left?.State ?? StudioToolWindowConfiguration.HiddenState;
        ApplyColumn(
            ExplorerColumn,
            ExplorerSplitter,
            leftState,
            left?.Size ?? 260,
            minimum: 180,
            extraRail: 0);

        var right = FindToolWindowByDock(StudioToolWindowConfiguration.RightDock);
        var rightState = right?.State ?? StudioToolWindowConfiguration.HiddenState;
        ApplyColumn(
            InspectorColumn,
            InspectorSplitter,
            rightState,
            right?.Size ?? 360,
            minimum: toolboxVisible ? 320 : 220,
            extraRail: toolboxVisible ? 132 : 0);

        var bottom = FindToolWindowByDock(StudioToolWindowConfiguration.BottomDock);
        var bottomState = bottom?.State ?? StudioToolWindowConfiguration.HiddenState;
        switch (bottomState)
        {
            case StudioToolWindowConfiguration.VisibleState:
                BottomToolWindowRow.MinHeight = 96;
                BottomToolWindowRow.Height =
                    new GridLength(Math.Clamp(bottom?.Size ?? 172, 96, 600));
                BottomToolWindowSplitter.Visibility = Visibility.Visible;
                break;
            case StudioToolWindowConfiguration.MinimizedState:
                BottomToolWindowRow.MinHeight = ToolWindowRailSize;
                BottomToolWindowRow.Height = new GridLength(ToolWindowRailSize);
                BottomToolWindowSplitter.Visibility = Visibility.Collapsed;
                break;
            default:
                BottomToolWindowRow.MinHeight = 0;
                BottomToolWindowRow.Height = new GridLength(0);
                BottomToolWindowSplitter.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private static void ApplyColumn(
        ColumnDefinition column,
        GridSplitter splitter,
        string state,
        double configuredWidth,
        double minimum,
        double extraRail)
    {
        switch (state)
        {
            case StudioToolWindowConfiguration.VisibleState:
                column.MinWidth = minimum + extraRail;
                column.Width = new GridLength(
                    Math.Clamp(configuredWidth, minimum + extraRail, 760));
                splitter.Visibility = Visibility.Visible;
                break;
            case StudioToolWindowConfiguration.MinimizedState:
                column.MinWidth = ToolWindowRailSize + extraRail;
                column.Width = new GridLength(ToolWindowRailSize + extraRail);
                splitter.Visibility =
                    ToolWindowRailSize + extraRail > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                break;
            default:
                column.MinWidth = extraRail;
                column.Width = new GridLength(extraRail);
                splitter.Visibility =
                    extraRail > 0 ? Visibility.Visible : Visibility.Collapsed;
                break;
        }
    }

    private void UpdateMinimizedRails(bool toolboxVisible)
    {
        LeftToolWindowRailButton.Visibility = Visibility.Collapsed;
        RightToolWindowRailButton.Visibility = Visibility.Collapsed;
        BottomToolWindowRailButton.Visibility = Visibility.Collapsed;
        RightToolWindowRailButton.Margin =
            toolboxVisible ? new Thickness(0, 0, 132, 0) : new Thickness(0);

        foreach (var kind in Enum.GetValues<ToolWindowKind>())
        {
            var configuration = ToolWindowConfiguration(kind);
            if (configuration.State != StudioToolWindowConfiguration.MinimizedState)
            {
                continue;
            }

            var rail = RailFor(configuration.Dock);
            rail.Tag = kind.ToString();
            rail.Content = configuration.Dock == StudioToolWindowConfiguration.BottomDock
                ? HorizontalRailLabel(ToolWindowTitle(kind))
                : VerticalRailLabel(ToolWindowTitle(kind));
            rail.ToolTip = $"恢复{ToolWindowTitle(kind)}";
            rail.Visibility = Visibility.Visible;
            Panel.SetZIndex(rail, 8);
        }
    }

    private Button RailFor(string dock) => dock switch
    {
        StudioToolWindowConfiguration.RightDock => RightToolWindowRailButton,
        StudioToolWindowConfiguration.BottomDock => BottomToolWindowRailButton,
        _ => LeftToolWindowRailButton
    };

    private static TextBlock VerticalRailLabel(string title) =>
        new()
        {
            Text = string.Join('\n', title.EnumerateRunes().Select(rune => rune.ToString())),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

    private static TextBlock HorizontalRailLabel(string title) =>
        new()
        {
            Text = "▲ " + title,
            TextAlignment = TextAlignment.Center
        };

    private StudioToolWindowConfiguration ToolWindowConfiguration(ToolWindowKind kind) =>
        kind switch
        {
            ToolWindowKind.Project => Configuration.WindowLayout.Project,
            ToolWindowKind.Work => Configuration.WindowLayout.WorkTools,
            _ => Configuration.WindowLayout.Console
        };

    private FrameworkElement ToolWindowView(ToolWindowKind kind) =>
        kind switch
        {
            ToolWindowKind.Project => ProjectExplorerPanel,
            ToolWindowKind.Work => RightToolsPanel,
            _ => BottomToolWindowPanel
        };

    private string ToolWindowTitle(ToolWindowKind kind) => kind switch
    {
        ToolWindowKind.Project => "项目",
        ToolWindowKind.Work => WorkTabs.SelectedIndex == 1 ? "属性" : "程序",
        _ => "控制台"
    };

    private StudioToolWindowConfiguration? FindToolWindowByDock(string dock) =>
        new[]
            {
                Configuration.WindowLayout.Project,
                Configuration.WindowLayout.WorkTools,
                Configuration.WindowLayout.Console
            }
            .FirstOrDefault(item =>
                string.Equals(item.Dock, dock, StringComparison.Ordinal));

    private void SetProjectToolWindowVisible(bool visible, double? width = null)
    {
        var config = Configuration.WindowLayout.Project;
        config.State = visible
            ? StudioToolWindowConfiguration.VisibleState
            : StudioToolWindowConfiguration.HiddenState;
        if (visible && width is not null)
        {
            config.Size = Math.Clamp(width.Value, 180, 760);
        }

        ApplyDockedToolWindows();
    }

    private void SetRightToolsVisible(bool visible, double? width = null)
    {
        var config = Configuration.WindowLayout.WorkTools;
        config.State = visible
            ? StudioToolWindowConfiguration.VisibleState
            : StudioToolWindowConfiguration.HiddenState;
        if (visible && width is not null)
        {
            config.Size = Math.Clamp(width.Value, 220, 760);
        }

        ApplyDockedToolWindows();
    }

    private void SetBottomToolWindowVisible(bool visible, double? height = null)
    {
        var config = Configuration.WindowLayout.Console;
        config.State = visible
            ? StudioToolWindowConfiguration.VisibleState
            : StudioToolWindowConfiguration.HiddenState;
        if (visible && height is not null)
        {
            config.Size = Math.Clamp(height.Value, 96, 600);
        }

        ApplyDockedToolWindows();
    }

    private void SetStatusBarVisible(bool visible)
    {
        StatusBarRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ToggleStatusBarMenuItem.IsChecked = visible;
        Configuration.WindowLayout.ShowStatusBar = visible;
    }

    private void UpdateRightToolsColumn() => ApplyDockedToolWindows();

    private void ShowRightToolsTab(int index)
    {
        Configuration.WindowLayout.WorkTools.State =
            StudioToolWindowConfiguration.VisibleState;
        WorkTabs.SelectedIndex = Math.Clamp(index, 0, WorkTabs.Items.Count - 1);
        ApplyDockedToolWindows();
        PersistWindowLayout(markCustom: true);
    }

    private void OnShowConsoleToolWindow(object sender, RoutedEventArgs e)
    {
        ShowConsolePanel();
        PersistWindowLayout(markCustom: true);
    }

    private void ShowConsolePanel()
    {
        Configuration.WindowLayout.Console.State =
            StudioToolWindowConfiguration.VisibleState;
        BottomTabs.SelectedIndex = 1;
        ApplyDockedToolWindows();
    }

    private void SetToolWindowState(ToolWindowKind kind, string state)
    {
        ToolWindowConfiguration(kind).State = state;
        ApplyDockedToolWindows();
        PersistWindowLayout(markCustom: true);
        SetStatus($"{ToolWindowTitle(kind)}：{state}", ReadyBrush);
    }

    private void MoveToolWindow(ToolWindowKind kind, string targetDock)
    {
        var source = ToolWindowConfiguration(kind);
        if (string.Equals(source.Dock, targetDock, StringComparison.Ordinal))
        {
            SetToolWindowState(kind, StudioToolWindowConfiguration.VisibleState);
            return;
        }

        var sourceDock = source.Dock;
        var occupant = new[]
            {
                (Kind: ToolWindowKind.Project, Config: Configuration.WindowLayout.Project),
                (Kind: ToolWindowKind.Work, Config: Configuration.WindowLayout.WorkTools),
                (Kind: ToolWindowKind.Console, Config: Configuration.WindowLayout.Console)
            }
            .FirstOrDefault(item =>
                item.Kind != kind &&
                string.Equals(item.Config.Dock, targetDock, StringComparison.Ordinal));
        if (occupant.Config is not null)
        {
            occupant.Config.Dock = sourceDock;
        }

        source.Dock = targetDock;
        if (targetDock == StudioToolWindowConfiguration.BottomDock)
        {
            source.Size = Math.Clamp(source.Size, 96, 600);
        }
        else
        {
            source.Size = Math.Clamp(source.Size, 180, 760);
        }

        ApplyDockedToolWindows();
        PersistWindowLayout(markCustom: true);
        SetStatus($"{ToolWindowTitle(kind)}已移到{targetDock}", ReadyBrush);
    }

    private void OnToolWindowRailClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } &&
            Enum.TryParse<ToolWindowKind>(value, out var kind))
        {
            SetToolWindowState(kind, StudioToolWindowConfiguration.VisibleState);
        }
    }

    private void OnApplyLayoutPreset(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string preset })
        {
            ApplyWindowLayoutPreset(preset);
        }
    }

    private void ApplyWindowLayoutPreset(string preset)
    {
        Configuration.WindowLayout = preset switch
        {
            StudioWindowLayoutConfiguration.FocusCodePreset =>
                CreatePreset(preset, hidden: true),
            StudioWindowLayoutConfiguration.DesignerPreset =>
                CreatePreset(preset, hidden: false, rightTab: 1, showBottom: false),
            _ => CreatePreset(StudioWindowLayoutConfiguration.DefaultPreset, hidden: false)
        };
        ApplyWindowLayoutConfiguration();
        SaveWindowLayout();
        SetStatus($"窗口布局：{Configuration.WindowLayout.Preset}", ReadyBrush);
    }

    private static StudioWindowLayoutConfiguration CreatePreset(
        string preset,
        bool hidden,
        int rightTab = 0,
        bool showBottom = true) =>
        new()
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

    private StudioWindowLayoutConfiguration CaptureWindowLayout()
    {
        var layout = Configuration.WindowLayout;
        CaptureToolWindowSize(
            layout.Project,
            ExplorerColumn,
            InspectorColumn,
            BottomToolWindowRow);
        CaptureToolWindowSize(
            layout.WorkTools,
            ExplorerColumn,
            InspectorColumn,
            BottomToolWindowRow);
        CaptureToolWindowSize(
            layout.Console,
            ExplorerColumn,
            InspectorColumn,
            BottomToolWindowRow);
        layout.ShowStatusBar = StatusBarRow.Visibility == Visibility.Visible;
        layout.RightToolsTab = Math.Clamp(WorkTabs.SelectedIndex, 0, 1);
        return layout;
    }

    private static void CaptureToolWindowSize(
        StudioToolWindowConfiguration config,
        ColumnDefinition leftColumn,
        ColumnDefinition rightColumn,
        RowDefinition bottomRow)
    {
        if (config.State != StudioToolWindowConfiguration.VisibleState)
        {
            return;
        }

        var size = config.Dock switch
        {
            StudioToolWindowConfiguration.LeftDock => leftColumn.Width.Value,
            StudioToolWindowConfiguration.RightDock => rightColumn.Width.Value,
            _ => bottomRow.Height.Value
        };
        if (size > 0)
        {
            config.Size = size;
        }
    }

    private void PersistWindowLayout(bool markCustom)
    {
        var layout = CaptureWindowLayout();
        if (markCustom)
        {
            layout.Preset = StudioWindowLayoutConfiguration.CustomPreset;
        }

        Configuration.WindowLayout = layout;
        SaveWindowLayout();
    }

    private void SaveWindowLayout()
    {
        try
        {
            Configuration.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AppendOutput("窗口布局保存失败：" + exception.Message);
        }
    }

    private void OnViewToolsMenuOpened(object sender, RoutedEventArgs e)
    {
        ViewToolsMenu.Items.Clear();
        PopulateToolWindowMenus(ViewToolsMenu.Items);
    }

    private void OnLayoutButtonClick(object sender, RoutedEventArgs e)
    {
        BuildLayoutContextMenu();
        LayoutContextMenu.PlacementTarget = LayoutButton;
        LayoutContextMenu.Placement = PlacementMode.Bottom;
        LayoutContextMenu.IsOpen = true;
    }

    private void OnLayoutContextMenuOpened(object sender, RoutedEventArgs e) =>
        BuildLayoutContextMenu();

    private void BuildLayoutContextMenu()
    {
        LayoutContextMenu.Items.Clear();
        PopulateToolWindowMenus(LayoutContextMenu.Items);
        LayoutContextMenu.Items.Add(new Separator());
        LayoutContextMenu.Items.Add(CreateLayoutPresetsMenu());
        LayoutContextMenu.Items.Add(CreateCheckMenuItem(
            "状态栏",
            StatusBarRow.Visibility == Visibility.Visible,
            OnToggleStatusBar));
    }

    private void PopulateToolWindowMenus(ItemCollection items)
    {
        foreach (var kind in Enum.GetValues<ToolWindowKind>())
        {
            items.Add(CreateToolWindowSubmenu(kind));
        }

        items.Add(CreateCheckMenuItem(
            "窗口组件箱",
            InspectorPanel.Visibility == Visibility.Visible,
            OnToggleToolboxPanel,
            "Alt+5"));
    }

    private MenuItem CreateToolWindowSubmenu(ToolWindowKind kind)
    {
        var config = ToolWindowConfiguration(kind);
        var menu = new MenuItem
        {
            Header = ToolWindowTitle(kind),
            InputGestureText = kind switch
            {
                ToolWindowKind.Project => "Alt+1",
                ToolWindowKind.Work => "Alt+2",
                _ => "Alt+4"
            }
        };
        menu.Items.Add(CreateStateItem(
            "显示",
            config.State == StudioToolWindowConfiguration.VisibleState,
            () => SetToolWindowState(kind, StudioToolWindowConfiguration.VisibleState)));
        menu.Items.Add(CreateStateItem(
            "最小化",
            config.State == StudioToolWindowConfiguration.MinimizedState,
            () => SetToolWindowState(kind, StudioToolWindowConfiguration.MinimizedState)));
        menu.Items.Add(CreateStateItem(
            "隐藏",
            config.State == StudioToolWindowConfiguration.HiddenState,
            () => SetToolWindowState(kind, StudioToolWindowConfiguration.HiddenState)));
        menu.Items.Add(new Separator());

        if (kind == ToolWindowKind.Work)
        {
            menu.Items.Add(CreateStateItem(
                "显示程序",
                config.State == StudioToolWindowConfiguration.VisibleState &&
                    WorkTabs.SelectedIndex == 0,
                () => ShowRightToolsTab(0)));
            menu.Items.Add(CreateStateItem(
                "显示属性",
                config.State == StudioToolWindowConfiguration.VisibleState &&
                    WorkTabs.SelectedIndex == 1,
                () => ShowRightToolsTab(1)));
            menu.Items.Add(new Separator());
        }

        var moveMenu = new MenuItem { Header = "移到" };
        foreach (var dock in new[]
                 {
                     StudioToolWindowConfiguration.LeftDock,
                     StudioToolWindowConfiguration.RightDock,
                     StudioToolWindowConfiguration.BottomDock
                 })
        {
            moveMenu.Items.Add(CreateStateItem(
                dock,
                string.Equals(config.Dock, dock, StringComparison.Ordinal),
                () => MoveToolWindow(kind, dock)));
        }

        menu.Items.Add(moveMenu);
        return menu;
    }

    private static MenuItem CreateStateItem(
        string header,
        bool isChecked,
        Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked
        };
        item.Click += (_, _) => action();
        return item;
    }

    private MenuItem CreateLayoutPresetsMenu()
    {
        var menu = new MenuItem { Header = "布局预设" };
        foreach (var preset in new[]
                 {
                     StudioWindowLayoutConfiguration.DefaultPreset,
                     StudioWindowLayoutConfiguration.FocusCodePreset,
                     StudioWindowLayoutConfiguration.DesignerPreset
                 })
        {
            menu.Items.Add(CreateStateItem(
                preset,
                string.Equals(
                    Configuration.WindowLayout.Preset,
                    preset,
                    StringComparison.Ordinal),
                () => ApplyWindowLayoutPreset(preset)));
        }

        return menu;
    }

    private static MenuItem CreateCheckMenuItem(
        string header,
        bool isChecked,
        RoutedEventHandler handler,
        string? gesture = null,
        string? tag = null)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked,
            InputGestureText = gesture ?? string.Empty,
            Tag = tag
        };
        item.Click += handler;
        return item;
    }

    private void BuildWindowMenu()
    {
        WindowMenu.Items.Clear();
        WindowMenu.Items.Add(CreateLayoutPresetsMenu());
        WindowMenu.Items.Add(new Separator());
        PopulateToolWindowMenus(WindowMenu.Items);
        WindowMenu.Items.Add(new Separator());
        WindowMenu.Items.Add(new MenuItem
        {
            Header = "打开的文档",
            IsEnabled = false
        });
        foreach (var document in _documents)
        {
            var item = new MenuItem
            {
                Header = document.DisplayName,
                Tag = document,
                IsCheckable = true,
                IsChecked = ReferenceEquals(document, _currentDocument)
            };
            item.Click += (_, _) => DocumentTabs.SelectedItem = document;
            WindowMenu.Items.Add(item);
        }

        if (_documents.Count == 0)
        {
            WindowMenu.Items.Add(new MenuItem
            {
                Header = "（无打开的窗口）",
                IsEnabled = false
            });
        }
    }
}
