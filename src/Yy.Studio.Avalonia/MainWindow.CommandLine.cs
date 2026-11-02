using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Yy.Studio.Avalonia.Features.CommandLine;
using Yy.Studio.Avalonia.Layout;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private sealed class CommandLineSession
    {
        public required TerminalTabView TabView { get; init; }
        public required TabItem Tab { get; init; }
        public required string DefaultName { get; init; }
        public string? UserTitle { get; set; }
        public Border Chip { get; set; } = null!;
        public Button TitleButton { get; set; } = null!;
        public TextBlock TitleText { get; set; } = null!;
        public TextBox TitleEditor { get; set; } = null!;
        public bool IsRenaming { get; set; }
    }

    private readonly List<CommandLineSession> _commandLines = [];
    private CommandLineView? _commandLine;
    private CommandLineSession? _activeCommandLine;
    private int _commandLineSequence;

    private void InitializeCommandLine()
    {
        BottomTabs.SelectionChanged += (_, _) => OnBottomTabSelectionChanged();
        AddCommandLineSession(Environment.CurrentDirectory, select: true);
    }

    private string CurrentProjectDirectory =>
        _workspace.Project?.DirectoryPath ?? Environment.CurrentDirectory;

    private CommandLineView CreateTerminalView(string workingDirectory)
    {
        var view = new CommandLineView(workingDirectory);
        view.SearchRequested += (_, _) => ShowTerminalSearch(view);
        return view;
    }

    private void OnNewCommandLine(object? sender, RoutedEventArgs e) =>
        AddCommandLineSession(CurrentProjectDirectory, select: true);

    /// <summary>左侧快捷轨：切换底部命令行；已在前台时再次点击隐藏底部工具窗。</summary>
    private void OnOpenCommandLineWindow(object? sender, RoutedEventArgs e)
    {
        var terminalActive = _commandLines.Any(
            session => ReferenceEquals(BottomTabs.SelectedItem, session.Tab));
        if (ToggleBottomToolWindow(terminalActive))
        {
            return;
        }

        if (_commandLines.Count == 0)
        {
            AddCommandLineSession(CurrentProjectDirectory, select: true);
        }
        else
        {
            SelectCommandLineSession(_activeCommandLine ?? _commandLines[0]);
        }

        _commandLine?.FocusTerminal();
    }

    /// <summary>左侧快捷轨：切换到底部运行输出。</summary>
    private void OnShowRunOutputWindow(object? sender, RoutedEventArgs e)
    {
        var consoleActive = ConsoleTab.IsVisible &&
            ReferenceEquals(BottomTabs.SelectedItem, ConsoleTab);
        if (ToggleBottomToolWindow(consoleActive))
        {
            return;
        }

        ConsoleTab.IsVisible = true;
        SelectBottomTab("控制台");
    }

    /// <summary>底部工具窗已显示且目标视图在前台时切换为隐藏。</summary>
    private bool ToggleBottomToolWindow(bool targetViewActive)
    {
        if (_toolWindowLayout is null)
        {
            return false;
        }

        if (_toolWindowLayout.GetState(ToolWindowKind.Console) == ToolWindowState.Visible &&
            targetViewActive)
        {
            _toolWindowLayout.Hide(ToolWindowKind.Console);
            UpdateBottomRailState();
            return true;
        }

        _toolWindowLayout.Show(ToolWindowKind.Console);
        UpdateBottomRailState();
        return false;
    }

    /// <summary>左侧运行输出/命令行按钮按当前底部视图高亮。</summary>
    private void UpdateBottomRailState()
    {
        var visible = _toolWindowLayout is null ||
            (!_toolWindowLayout.StartPageMode &&
             _toolWindowLayout.GetState(ToolWindowKind.Console) == ToolWindowState.Visible);
        var terminalActive = visible && _commandLines.Any(
            session => ReferenceEquals(BottomTabs.SelectedItem, session.Tab));
        var consoleActive = visible && ConsoleTab.IsVisible &&
            ReferenceEquals(BottomTabs.SelectedItem, ConsoleTab);
        SetRailActive(RailCommandLineButton, terminalActive);
        SetRailActive(RailRunOutputButton, consoleActive);
    }

    private static void SetRailActive(Button button, bool active)
    {
        if (active)
        {
            if (!button.Classes.Contains("active"))
            {
                button.Classes.Add("active");
            }
        }
        else
        {
            button.Classes.Remove("active");
        }
    }

    private CommandLineSession AddCommandLineSession(string workingDirectory, bool select)
    {
        var number = ++_commandLineSequence;
        var defaultName = number == 1 ? "本地" : $"本地 {number}";
        var initialView = CreateTerminalView(workingDirectory);
        var tabView = new TerminalTabView(initialView, CreateTerminalView);
        var tab = new TabItem
        {
            Header = defaultName,
            Content = tabView
        };
        var session = new CommandLineSession
        {
            TabView = tabView,
            Tab = tab,
            DefaultName = defaultName
        };
        session.Chip = CreateCommandLineChip(session);
        tabView.TitleChanged += (_, _) => RefreshSessionTitle(session);
        tabView.StructureChanged += (_, _) => RefreshSessionTitle(session);
        tabView.ActiveViewChanged += (_, _) => OnSessionActiveViewChanged(session);
        BottomTabs.Items.Add(tab);
        TerminalTabStrip.Children.Add(session.Chip);
        _commandLines.Add(session);
        RefreshSessionTitle(session);
        if (select)
        {
            SelectCommandLineSession(session);
        }

        return session;
    }

    private Border CreateCommandLineChip(CommandLineSession session)
    {
        var titleText = new TextBlock
        {
            Text = session.DefaultName,
            FontSize = 12,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleButton = new Button
        {
            Content = titleText,
            Classes = { "terminalTabTitle" }
        };
        titleButton.Click += (_, _) => SelectCommandLineSession(session);

        var titleEditor = new TextBox
        {
            IsVisible = false,
            MinWidth = 70,
            MaxWidth = 200,
            FontSize = 12,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitRename(session);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename(session);
                e.Handled = true;
            }
        };
        titleEditor.LostFocus += (_, _) =>
        {
            if (session.IsRenaming)
            {
                CommitRename(session);
            }
        };

        var titlePanel = new Grid
        {
            Children = { titleButton, titleEditor }
        };
        var close = new Button
        {
            Content = "×",
            Classes = { "terminalTabClose" },
            VerticalAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => CloseCommandLineSession(session);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { titlePanel, close }
        };
        session.TitleButton = titleButton;
        session.TitleText = titleText;
        session.TitleEditor = titleEditor;
        return new Border
        {
            Classes = { "terminalTab" },
            Child = content,
            ContextMenu = BuildTabContextMenu(session)
        };
    }

    private ContextMenu BuildTabContextMenu(CommandLineSession? session)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor(
            "新建命令行",
            () => AddCommandLineSession(CurrentProjectDirectory, select: true)));
        if (session is null)
        {
            return menu;
        }

        var index = _commandLines.IndexOf(session);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("重命名", () => BeginRename(session)));
        menu.Items.Add(MenuItemFor(
            "查找",
            () => ShowTerminalSearch(session.TabView.ActiveView)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("关闭", () => CloseCommandLineSession(session)));
        menu.Items.Add(MenuItemFor(
            "关闭其他",
            () => CloseOtherCommandLines(session),
            _commandLines.Count > 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor(
            "向左移动",
            () => MoveCommandLineSession(session, -1),
            index > 0));
        menu.Items.Add(MenuItemFor(
            "向右移动",
            () => MoveCommandLineSession(session, 1),
            index >= 0 && index < _commandLines.Count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor(
            "向右拆分",
            () => SplitCommandLineSession(session, Orientation.Horizontal)));
        menu.Items.Add(MenuItemFor(
            "向下拆分",
            () => SplitCommandLineSession(session, Orientation.Vertical)));
        return menu;
    }

    private static MenuItem MenuItemFor(
        string header,
        Action action,
        bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnTerminalMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control anchor)
        {
            return;
        }

        var session = _activeCommandLine ?? _commandLines.FirstOrDefault();
        BuildTabContextMenu(session).Open(anchor);
    }

    private void SelectCommandLineSession(CommandLineSession session)
    {
        if (!_commandLines.Contains(session))
        {
            return;
        }

        if (ReferenceEquals(BottomTabs.SelectedItem, session.Tab))
        {
            session.TabView.ActiveView.FocusTerminal();
            UpdateBottomChipSelection();
            return;
        }

        BottomTabs.SelectedItem = session.Tab;
    }

    private void CloseCommandLineSession(CommandLineSession session)
    {
        var index = _commandLines.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        var wasSelected = ReferenceEquals(BottomTabs.SelectedItem, session.Tab);
        if (_searchTarget is not null && session.TabView.Views.Contains(_searchTarget))
        {
            HideTerminalSearch(focusTerminal: false);
        }

        _commandLines.RemoveAt(index);
        BottomTabs.Items.Remove(session.Tab);
        TerminalTabStrip.Children.Remove(session.Chip);
        session.TabView.Stop();

        if (ReferenceEquals(_activeCommandLine, session))
        {
            _activeCommandLine = null;
        }

        if (_commandLine is not null && session.TabView.Views.Contains(_commandLine))
        {
            _commandLine = null;
        }

        if (wasSelected)
        {
            var next = _commandLines.ElementAtOrDefault(index)
                ?? _commandLines.ElementAtOrDefault(index - 1);
            if (next is not null)
            {
                SelectCommandLineSession(next);
            }
            else if (ConsoleTab.IsVisible)
            {
                SelectBottomTab("控制台");
            }
            else
            {
                _toolWindowLayout?.Hide(ToolWindowKind.Console);
            }
        }

        UpdateBottomHeader();
        UpdateBottomChipSelection();
        UpdateBottomRailState();
    }

    private void CloseOtherCommandLines(CommandLineSession session)
    {
        foreach (var other in _commandLines
                     .Where(item => !ReferenceEquals(item, session))
                     .ToArray())
        {
            CloseCommandLineSession(other);
        }

        SelectCommandLineSession(session);
    }

    private void MoveCommandLineSession(CommandLineSession session, int delta)
    {
        var index = _commandLines.IndexOf(session);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _commandLines.Count)
        {
            return;
        }

        _commandLines.RemoveAt(index);
        _commandLines.Insert(target, session);
        var chipIndex = TerminalTabStrip.Children.IndexOf(session.Chip);
        if (chipIndex >= 0)
        {
            TerminalTabStrip.Children.RemoveAt(chipIndex);
            TerminalTabStrip.Children.Insert(target, session.Chip);
        }

        SelectCommandLineSession(session);
        UpdateBottomChipSelection();
    }

    private void SplitCommandLineSession(
        CommandLineSession session,
        Orientation orientation)
    {
        if (!_commandLines.Contains(session))
        {
            return;
        }

        SelectCommandLineSession(session);
        session.TabView.Split(orientation);
        _commandLine = session.TabView.ActiveView;
        RefreshSessionTitle(session);
        session.TabView.ActiveView.FocusTerminal();
    }

    private void BeginRename(CommandLineSession session)
    {
        if (!_commandLines.Contains(session))
        {
            return;
        }

        session.IsRenaming = true;
        session.TitleEditor.Text = SessionTitle(session);
        session.TitleButton.IsVisible = false;
        session.TitleEditor.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            session.TitleEditor.Focus();
            session.TitleEditor.SelectAll();
        });
    }

    private void CommitRename(CommandLineSession session)
    {
        if (!session.IsRenaming)
        {
            return;
        }

        session.IsRenaming = false;
        var text = session.TitleEditor.Text?.Trim();
        session.UserTitle = string.IsNullOrWhiteSpace(text) ? null : text;
        session.TitleEditor.IsVisible = false;
        session.TitleButton.IsVisible = true;
        RefreshSessionTitle(session);
        if (_commandLines.Contains(session))
        {
            session.TabView.ActiveView.FocusTerminal();
        }
    }

    private void CancelRename(CommandLineSession session)
    {
        if (!session.IsRenaming)
        {
            return;
        }

        session.IsRenaming = false;
        session.TitleEditor.IsVisible = false;
        session.TitleButton.IsVisible = true;
        RefreshSessionTitle(session);
        session.TabView.ActiveView.FocusTerminal();
    }

    private static string SessionTitle(CommandLineSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.UserTitle))
        {
            return session.UserTitle;
        }

        var shellTitle = session.TabView.ActiveView.ShellTitle;
        return string.IsNullOrWhiteSpace(shellTitle)
            ? session.DefaultName
            : shellTitle;
    }

    private void RefreshSessionTitle(CommandLineSession session)
    {
        if (session.IsRenaming)
        {
            return;
        }

        var title = SessionTitle(session);
        session.TitleText.Text = title;
        session.Tab.Header = title;
    }

    private void OnSessionActiveViewChanged(CommandLineSession session)
    {
        if (ReferenceEquals(_activeCommandLine, session))
        {
            _commandLine = session.TabView.ActiveView;
        }

        if (ReferenceEquals(_activeCommandLine, session) &&
            _searchTarget is not null &&
            !ReferenceEquals(session.TabView.ActiveView, _searchTarget))
        {
            HideTerminalSearch(focusTerminal: false);
        }

        RefreshSessionTitle(session);
        UpdateBottomRailState();
    }

    private void OnBottomTabSelectionChanged()
    {
        if (BottomTabs.SelectedItem is TabItem tab)
        {
            var session = _commandLines.FirstOrDefault(
                candidate => ReferenceEquals(candidate.Tab, tab));
            if (session is not null)
            {
                _activeCommandLine = session;
                _commandLine = session.TabView.ActiveView;
                session.TabView.ActiveView.FocusTerminal();
                RefreshSessionTitle(session);
            }
        }

        if (_searchTarget is not null)
        {
            HideTerminalSearch(focusTerminal: false);
        }

        UpdateBottomHeader();
        UpdateBottomChipSelection();
        UpdateBottomRailState();
    }

    /// <summary>命令行与控制台互斥显示：控制台在前台时隐藏会话条和新建按钮。</summary>
    private void UpdateBottomHeader()
    {
        var consoleMode = ConsoleTab.IsVisible &&
            ReferenceEquals(BottomTabs.SelectedItem, ConsoleTab);
        if (consoleMode && TerminalSearchBar.IsVisible)
        {
            HideTerminalSearch(focusTerminal: false);
        }

        BottomPanelTitle.Text = consoleMode ? "控制台" : "命令行";
        TerminalTabStrip.IsVisible = !consoleMode;
        NewCommandLineButton.IsVisible = !consoleMode;
        TerminalMenuButton.IsVisible = !consoleMode;
        TerminalSearchButton.IsVisible = !consoleMode;
    }

    private void UpdateBottomChipSelection()
    {
        foreach (var session in _commandLines)
        {
            SetChipActive(
                session.Chip,
                ReferenceEquals(BottomTabs.SelectedItem, session.Tab));
        }
    }

    private static void SetChipActive(Border chip, bool active)
    {
        if (active)
        {
            if (!chip.Classes.Contains("active"))
            {
                chip.Classes.Add("active");
            }
        }
        else
        {
            chip.Classes.Remove("active");
        }
    }

    /// <summary>打开或新建工程后,把默认终端切到工程根目录并显示出来。</summary>
    private void EnterProjectCommandLine(string projectDirectory)
    {
        SetConsoleVisible(false);
        var session = _commandLines.FirstOrDefault();
        if (session is null)
        {
            AddCommandLineSession(projectDirectory, select: true);
            return;
        }

        session.TabView.ActiveView.EnterProject(projectDirectory);
        SelectCommandLineSession(session);
    }

    /// <summary>控制台只在运行或调试期间出现,结束后自动回到命令行。</summary>
    private void SetConsoleVisible(bool visible)
    {
        ConsoleTab.IsVisible = visible;
        if (!visible && ReferenceEquals(BottomTabs.SelectedItem, ConsoleTab))
        {
            SelectDefaultCommandLine();
            if (_commandLines.Count == 0)
            {
                _toolWindowLayout?.Hide(ToolWindowKind.Console);
            }
        }

        UpdateBottomHeader();
        UpdateBottomChipSelection();
        UpdateBottomRailState();
    }

    private void SelectDefaultCommandLine()
    {
        var session = _activeCommandLine is not null &&
            _commandLines.Contains(_activeCommandLine)
            ? _activeCommandLine
            : _commandLines.FirstOrDefault();
        if (session is not null)
        {
            SelectCommandLineSession(session);
        }
    }

}
