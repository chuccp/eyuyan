using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Yy.Studio.Avalonia.Features.Forms;
using Yy.Studio.Avalonia.Features.Projects;
using Yy.Language;
using Yy.Studio.Core.Configuration;
using Yy.Studio.Avalonia.Editor;
using Yy.Studio.Avalonia.Features.Documentation;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow : Window
{
    private readonly StudioWorkspace _workspace = new();
    private readonly List<EditorDocument> _documents = [];
    private Process? _process;
    private bool _building;
    private TabItem? _startPageTab;
    private StartPageView? _startPage;
    private FormPreviewWindow? _lastPreview;
    private Control? _defaultPropertyContent;
    private readonly StudioRecentFiles _recentFiles = StudioRecentFiles.Load();
    private Dictionary<(StudioShortcutModifiers Modifiers, string Key), string> _shortcutActions = [];

    public StudioConfiguration Configuration { get; private set; } = new();

    public MainWindow()
        : this([])
    {
    }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        _defaultPropertyContent = WorkPropertyHost.Content as Control;
        Configuration = StudioConfiguration.Load(args);
        if (!string.IsNullOrWhiteSpace(Configuration.LoadWarning))
        {
            AppendConsole("配置：" + Configuration.LoadWarning);
        }

        TargetBox.ItemsSource = Yy.Compiler.YyBuildTargets.All;
        TargetBox.SelectedItem = Yy.Compiler.YyBuildTargets.Auto;
        InitializeToolWindowLayout();
        ShowStartPage();
        AppendConsole("易语 Studio · Avalonia 跨平台外壳");
        AppendConsole("打开 .yyproj 工程后，双击项目树中的文件开始编辑。");
        UpdateActionState();
        RefreshShortcuts();
        McpAttach(args);

        var projectPath = ReadProjectPathArgument(args);
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            Opened += (_, _) => OpenProjectPath(projectPath);
        }
    }

    private static string? ReadProjectPathArgument(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--config", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--mcp-port", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            return argument;
        }

        return null;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void AppendConsole(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendConsole(text));
            return;
        }

        ConsoleText.Text = string.IsNullOrEmpty(ConsoleText.Text)
            ? text
            : ConsoleText.Text + Environment.NewLine + text;
        ConsoleText.CaretIndex = ConsoleText.Text.Length;
    }

    private void AppendConsoleFromAnyThread(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendConsole(text);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AppendConsole(text));
        }
    }

    private void UpdateActionState()
    {
        var hasProject = _workspace.Project is not null;
        var hasSource = CurrentDocument is { } document &&
                        string.Equals(
                            Path.GetExtension(document.Path),
                            ".yyc",
                            StringComparison.OrdinalIgnoreCase);
        var canBuild = (hasProject || hasSource) && !_building;
        var running = _process is not null;
        CheckButton.IsEnabled = hasProject && !_building;
        BuildButton.IsEnabled = canBuild && !running;
        RunButton.IsEnabled = canBuild && !running;
        StopButton.IsEnabled = running;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var modifiers = StudioShortcutModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            modifiers |= StudioShortcutModifiers.Control;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= StudioShortcutModifiers.Shift;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= StudioShortcutModifiers.Alt;
        }

        if (_shortcutActions.TryGetValue(
                (modifiers, e.Key.ToString()),
                out var action))
        {
            e.Handled = DispatchShortcut(action);
        }
    }

    private void RefreshShortcuts()
    {
        var actions = new Dictionary<(StudioShortcutModifiers, string), string>();
        foreach (var (action, values) in Configuration.Shortcuts)
        {
            foreach (var value in values)
            {
                if (StudioShortcutGesture.TryParse(value, out var gesture))
                {
                    actions[(gesture.Modifiers, gesture.Key)] = action;
                }
            }
        }

        _shortcutActions = actions;
    }

    private bool DispatchShortcut(string action)
    {
        switch (action)
        {
            case StudioShortcutActions.NewFile:
                OnNewSource(null, new RoutedEventArgs());
                return true;
            case StudioShortcutActions.NewProject:
                OnNewProject(null, new RoutedEventArgs());
                return true;
            case StudioShortcutActions.NewForm:
                OnNewForm(null, new RoutedEventArgs());
                return true;
            case StudioShortcutActions.OpenFile:
                _ = OpenFileAsync();
                return true;
            case StudioShortcutActions.OpenFolder:
                _ = OpenFolderAsync();
                return true;
            case StudioShortcutActions.SaveFile:
                SaveCurrent();
                return true;
            case StudioShortcutActions.SaveAs:
                _ = SaveAsAsync();
                return true;
            case StudioShortcutActions.CloseTab:
                CloseCurrent();
                return true;
            case StudioShortcutActions.Compile:
                _ = BuildProjectAsync();
                return true;
            case StudioShortcutActions.Run:
            case StudioShortcutActions.RunProject:
                if (_debugProbe is not null && _debugPause is not null)
                {
                    return ContinueOrStartDebug();
                }

                _ = RunProjectAsync();
                return true;
            case StudioShortcutActions.Stop:
                if (_debugProbe is not null || _debugCancellation is not null)
                {
                    StopDebug();
                }
                else
                {
                    StopProcess(silent: false);
                }

                return true;
            case StudioShortcutActions.Completion:
                if (CurrentDocument is { Editor: { } completionEditor })
                {
                    EditorAssistance.GetSession(completionEditor)?.ShowCompletion();
                    return true;
                }

                return false;
            case StudioShortcutActions.Help:
                new DocumentationWindow().Show(this);
                return true;
            case StudioShortcutActions.ToggleComment:
                if (CurrentDocument is { StructuredView: null } commentDocument)
                {
                    YyTextEditing.ToggleComment(commentDocument.Editor);
                    return true;
                }

                return false;
            case StudioShortcutActions.DuplicateLine:
                if (CurrentDocument is { StructuredView: null } duplicateDocument)
                {
                    YyTextEditing.DuplicateLine(duplicateDocument.Editor);
                    return true;
                }

                return false;
            case StudioShortcutActions.DeleteLine:
                if (CurrentDocument is { StructuredView: null } deleteDocument)
                {
                    YyTextEditing.DeleteLine(deleteDocument.Editor);
                    return true;
                }

                return false;
            case StudioShortcutActions.DebugStep:
                _ = DebugAsync(YyDebugMode.StepInto);
                return true;
            case StudioShortcutActions.DebugStepOver:
                _ = DebugAsync(YyDebugMode.StepOver);
                return true;
            case StudioShortcutActions.DebugStepOut:
                _ = DebugAsync(YyDebugMode.StepOut);
                return true;
            case StudioShortcutActions.DebugRunToCursor:
                _ = DebugAsync(YyDebugMode.RunToLine);
                return true;
            case StudioShortcutActions.DebugToggleBreakpoint:
                OnDebugToggleBreakpoint(null, new RoutedEventArgs());
                return true;
            case StudioShortcutActions.FormatCode:
                return FormatCurrentCode();
            default:
                return false;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_documents.Any(document => document.IsDirty))
        {
            e.Cancel = true;
            SetStatus("有未保存的文档，请先保存（Ctrl+S / ⌘S）。");
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        StopProcess(silent: true);
        if (_toolWindowLayout is not null)
        {
            try
            {
                Configuration.WindowLayout = _toolWindowLayout.CaptureConfiguration();
                Configuration.Save();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppendConsole("无法保存窗口布局：" + exception.Message);
            }
        }
    }

    private void OnClearConsole(object? sender, RoutedEventArgs e) =>
        ConsoleText.Text = string.Empty;

    private void OnExit(object? sender, RoutedEventArgs e)
    {
        if (_documents.Any(document => document.IsDirty))
        {
            SetStatus("有未保存的文档，请先保存后再退出。");
            return;
        }

        Close();
    }

    private void ShowStartPage()
    {
        if (_startPageTab is not null)
        {
            DocumentTabs.SelectedItem = _startPageTab;
            return;
        }

        _startPage = new StartPageView(_recentFiles.RecentFiles);
        _startPage.ClearRequested += () =>
        {
            _recentFiles.RecentFiles.Clear();
            _recentFiles.Save();
            _startPage?.SetRecent(_recentFiles.RecentFiles);
        };
        _startPage.NewRequested += () => OnNewProgram(null, new RoutedEventArgs());
        _startPage.OpenRequested += () => _ = OpenFileAsync();
        _startPage.ProjectRequested += OpenProjectPath;
        _startPageTab = new TabItem
        {
            Header = "起始页",
            Content = _startPage
        };
        DocumentTabs.Items.Add(_startPageTab);
        DocumentTabs.SelectedItem = _startPageTab;
    }

    private void HideStartPage()
    {
        if (_startPageTab is null)
        {
            return;
        }

        DocumentTabs.Items.Remove(_startPageTab);
        _startPageTab = null;
        _startPage = null;
    }

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        SetStatus("易语 Studio · Avalonia 单一代码库 · 原生 Windows/macOS 编译目标");
        AppendConsole(
            "易语 Studio 使用 Avalonia 共享界面，原生程序仍由 Yy.Native 直接生成，" +
            "不经过托管运行时。");
    }
}
