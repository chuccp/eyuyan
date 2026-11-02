using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Yy.Studio.Avalonia.Features.CommandLine;
using Yy.Studio.Avalonia.Layout;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private sealed class CommandLineSession
    {
        public required CommandLineView View { get; init; }
        public required TabItem Tab { get; init; }
        public required string Name { get; init; }
        public Border Chip { get; set; } = null!;
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
        var name = number == 1 ? "本地" : $"本地 {number}";
        var view = new CommandLineView(workingDirectory);
        var tab = new TabItem
        {
            Header = name,
            Content = new ContentControl { Content = view }
        };
        var session = new CommandLineSession
        {
            View = view,
            Tab = tab,
            Name = name
        };
        session.Chip = CreateCommandLineChip(session);
        BottomTabs.Items.Add(tab);
        TerminalTabStrip.Children.Add(session.Chip);
        _commandLines.Add(session);
        if (select)
        {
            SelectCommandLineSession(session);
        }

        return session;
    }

    private Border CreateCommandLineChip(CommandLineSession session)
    {
        var title = new Button
        {
            Content = session.Name,
            Classes = { "terminalTabTitle" }
        };
        title.Click += (_, _) => SelectCommandLineSession(session);

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
            Children = { title, close }
        };
        return new Border
        {
            Classes = { "terminalTab" },
            Child = content
        };
    }

    private void SelectCommandLineSession(CommandLineSession session)
    {
        if (!_commandLines.Contains(session))
        {
            return;
        }

        if (ReferenceEquals(BottomTabs.SelectedItem, session.Tab))
        {
            session.View.FocusTerminal();
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
        _commandLines.RemoveAt(index);
        BottomTabs.Items.Remove(session.Tab);
        TerminalTabStrip.Children.Remove(session.Chip);
        session.View.Stop();

        if (ReferenceEquals(_activeCommandLine, session))
        {
            _activeCommandLine = null;
        }

        if (ReferenceEquals(_commandLine, session.View))
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

        UpdateBottomChipSelection();
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
                _commandLine = session.View;
                session.View.FocusTerminal();
            }
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
        BottomPanelTitle.Text = consoleMode ? "控制台" : "命令行";
        TerminalTabStrip.IsVisible = !consoleMode;
        NewCommandLineButton.IsVisible = !consoleMode;
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

        session.View.EnterProject(projectDirectory);
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
