using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yy.Studio.Avalonia.Layout;
using Yy.Studio.Core.Configuration;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private const double ActionRailWidth = 34;
    private ToolWindowLayoutController? _toolWindowLayout;

    private void InitializeToolWindowLayout()
    {
        _toolWindowLayout = new ToolWindowLayoutController(
            ToolWindowHost,
            ProjectPanel,
            WorkPanel,
            ConsolePanel,
            StatusBarRow,
            ProjectSplitter,
            WorkSplitter,
            ConsoleSplitter,
            WorkTabs);
        _toolWindowLayout.LayoutChanged += (_, args) =>
        {
            Configuration.WindowLayout = args.Configuration;
            Configuration.Save();
            UpdateBottomRailState();
        };
        _toolWindowLayout.StartPageModeChanged += (_, _) =>
        {
            ApplyStartPageChrome();
            UpdateBottomRailState();
        };
        _toolWindowLayout.Apply(Configuration.WindowLayout);
        RefreshWorkPanel();
        UpdateBottomRailState();
    }

    private void ApplyStartPageChrome()
    {
        var startPage = _toolWindowLayout?.StartPageMode == true;
        var railWidth = new GridLength(startPage ? 0 : ActionRailWidth);
        SideActionShell.ColumnDefinitions[0].Width = railWidth;
        SideActionShell.ColumnDefinitions[2].Width = railWidth;
        LeftActionRail.IsVisible = !startPage;
        RightActionRail.IsVisible = !startPage;
    }

    private void OnMoveToolWindow(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is null ||
            (sender as MenuItem)?.Tag is not string tag)
        {
            return;
        }

        var parts = tag.Split('|');
        if (parts.Length != 2 ||
            !Enum.TryParse<ToolWindowKind>(parts[0], out var kind))
        {
            return;
        }

        var dock = parts[1] switch
        {
            "Left" => ToolWindowDock.Left,
            "Right" => ToolWindowDock.Right,
            _ => ToolWindowDock.Bottom
        };
        _toolWindowLayout.MoveDock(kind, dock);
    }

    private void OnLayoutPreset(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is not null &&
            (sender as MenuItem)?.Tag is string preset)
        {
            _toolWindowLayout.SetPreset(preset);
        }
    }

    private void OnWorkTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProgramOutline.SelectedItem is ProjectNode { FullPath: { } path } &&
            File.Exists(path))
        {
            OpenProjectPath(path);
        }
    }

    private void OnToggleProjectWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Project);

    /// <summary>顶栏“布局”入口:工具窗口显示、布局预设和状态栏开关。</summary>
    private void OnLayoutButtonClick(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(CreateLayoutToggleItem("项目", "Alt+1", ToolWindowKind.Project));
        menu.Items.Add(CreateLayoutToggleItem("程序", "Alt+2", ToolWindowKind.Work));
        menu.Items.Add(CreateLayoutToggleItem("控制台", "Alt+4", ToolWindowKind.Console));
        var toolbox = new MenuItem
        {
            Header = "窗口组件箱",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = CurrentDocument?.FormView?.IsToolboxVisible == true
        };
        toolbox.Click += OnToggleToolboxPanel;
        menu.Items.Add(toolbox);
        menu.Items.Add(new Separator());

        var presets = new MenuItem { Header = "布局预设" };
        foreach (var (header, tag) in new[]
                 {
                     ("默认布局", "默认"),
                     ("专注代码", "专注代码"),
                     ("窗体设计", "窗体设计")
                 })
        {
            var item = new MenuItem { Header = header, Tag = tag };
            item.Click += OnLayoutPreset;
            presets.Items.Add(item);
        }

        menu.Items.Add(presets);
        menu.Items.Add(new Separator());
        var statusBar = new MenuItem
        {
            Header = "状态栏",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _toolWindowLayout?.StatusBarVisible != false
        };
        statusBar.Click += OnToggleStatusBar;
        menu.Items.Add(statusBar);

        if (sender is Control anchor)
        {
            menu.Open(anchor);
        }
    }

    private MenuItem CreateLayoutToggleItem(
        string header,
        string gesture,
        ToolWindowKind kind)
    {
        var state = _toolWindowLayout?.GetState(kind) ?? ToolWindowState.Visible;
        var item = new MenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = state != ToolWindowState.Hidden
        };
        item.Click += (_, _) => CycleToolWindow(kind);
        return item;
    }

    private void OnToggleToolboxPanel(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument?.FormView is { } view)
        {
            view.IsToolboxVisible = !view.IsToolboxVisible;
        }
        else
        {
            SetStatus("请先打开窗体设计器。");
        }
    }

    private void OnToggleWorkWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Work);

    private void OnToggleConsoleWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Console);

    /// <summary>快捷轨的显示/隐藏按钮只切换两种状态,不经过最小化。</summary>
    private void OnToggleProjectWindowRail(object? sender, RoutedEventArgs e) =>
        ToggleToolWindow(ToolWindowKind.Project);

    private void OnToggleWorkWindowRail(object? sender, RoutedEventArgs e) =>
        ToggleToolWindow(ToolWindowKind.Work);

    private void OnToggleConsoleWindowRail(object? sender, RoutedEventArgs e) =>
        ToggleToolWindow(ToolWindowKind.Console);

    private void ToggleToolWindow(ToolWindowKind kind)
    {
        if (_toolWindowLayout is null)
        {
            return;
        }

        if (_toolWindowLayout.GetState(kind) == ToolWindowState.Visible)
        {
            _toolWindowLayout.Hide(kind);
        }
        else
        {
            _toolWindowLayout.Show(kind);
        }
    }

    private void OnToggleStatusBar(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is null)
        {
            return;
        }

        _toolWindowLayout.SetStatusBarVisible(!_toolWindowLayout.StatusBarVisible);
    }

    private void CycleToolWindow(ToolWindowKind kind)
    {
        if (_toolWindowLayout is null)
        {
            return;
        }

        switch (_toolWindowLayout.GetState(kind))
        {
            case ToolWindowState.Visible:
                _toolWindowLayout.Minimize(kind);
                break;
            case ToolWindowState.Minimized:
                _toolWindowLayout.Hide(kind);
                break;
            default:
                _toolWindowLayout.Show(kind);
                break;
        }
    }

    private void RefreshWorkPanel()
    {
        if (ProgramOutline is null)
        {
            return;
        }

        var root = new ProjectNode("程序", null, isDirectory: true);
        if (_workspace.Project is { } project)
        {
            var forms = new ProjectNode("窗体", null, isDirectory: true);
            foreach (var form in project.Forms)
            {
                var formNode = new ProjectNode(
                    form.Model.Title,
                    form.FormPath,
                    isDirectory: false)
                {
                    IsExpanded = true
                };
                foreach (var control in form.Model.Controls)
                {
                    formNode.Children.Add(new ProjectNode(
                        $"{control.Name}（{control.Type}）",
                        null,
                        isDirectory: false));
                }

                forms.Children.Add(formNode);
            }

            root.Children.Add(forms);
            var resources = new ProjectNode("资源", null, isDirectory: true);
            foreach (var resource in project.ResourceTable)
            {
                resources.Children.Add(new ProjectNode(
                    $"{resource.Name}（{resource.TypeName}）",
                    null,
                    isDirectory: false));
            }

            root.Children.Add(resources);
        }
        else
        {
            root.Children.Add(new ProjectNode("尚未打开工程", null, isDirectory: false));
        }

        if (CurrentDocument is { } document)
        {
            root.Children.Add(new ProjectNode(
                "文档：" + document.FileName,
                document.Path,
                isDirectory: false));
            if (document.StructuredView?.Document is { } code)
            {
                var functions = new ProjectNode("子程序", null, isDirectory: true);
                foreach (var function in code.Functions)
                {
                    functions.Children.Add(new ProjectNode(
                        function.Name,
                        null,
                        isDirectory: false));
                }

                root.Children.Add(functions);
            }
        }

        ProgramOutline.ItemsSource = new[] { root };
        PropertySummary.Text = CurrentDocument switch
        {
            null => "选择代码、窗体或控件后显示摘要。",
            { FormView.CurrentForm: { } form } =>
                $"{form.Name}\n{form.Title}\n{form.Width} × {form.Height}",
            { FormView.SelectedControl: { } control } =>
                $"{control.Name}\n{control.Type}\n" +
                $"位置 {control.X:0.#}, {control.Y:0.#}\n" +
                $"尺寸 {control.Width:0.#} × {control.Height:0.#}",
            { StructuredView.Document: { } code } =>
                $"{CurrentDocument?.FileName}\n{code.Functions.Count} 个子程序",
            { } other => other.FileName
        };
    }
}
