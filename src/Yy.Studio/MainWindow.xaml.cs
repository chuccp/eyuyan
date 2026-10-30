using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;

namespace Yy.Studio;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NewFileCommand =
        new("新建", nameof(NewFileCommand), typeof(MainWindow));
    public static readonly RoutedUICommand NewProjectCommand =
        new("新建项目", nameof(NewProjectCommand), typeof(MainWindow));
    public static readonly RoutedUICommand NewFormCommand =
        new("新建窗体", nameof(NewFormCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenFileCommand =
        new("打开文件", nameof(OpenFileCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenFolderCommand =
        new("打开文件夹", nameof(OpenFolderCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveFileCommand =
        new("保存", nameof(SaveFileCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveAsCommand =
        new("另存为", nameof(SaveAsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand CloseTabCommand =
        new("关闭标签页", nameof(CloseTabCommand), typeof(MainWindow));
    public static readonly RoutedUICommand CompileProjectCommand =
        new("编译为 EXE", nameof(CompileProjectCommand), typeof(MainWindow));
    public static readonly RoutedUICommand RunCommand =
        new("运行", nameof(RunCommand), typeof(MainWindow));
    public static readonly RoutedUICommand RunProjectCommand =
        new("运行项目", nameof(RunProjectCommand), typeof(MainWindow));
    public static readonly RoutedUICommand StopCommand =
        new("停止", nameof(StopCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DebugStepCommand =
        new("单步", nameof(DebugStepCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DebugStepOverCommand =
        new("单步跳过", nameof(DebugStepOverCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DebugStepOutCommand =
        new("跳出", nameof(DebugStepOutCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DebugRunToCursorCommand =
        new("到光标处", nameof(DebugRunToCursorCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DebugToggleBreakpointCommand =
        new("切换断点", nameof(DebugToggleBreakpointCommand), typeof(MainWindow));

    private static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0, 96, 0));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(128, 128, 0));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(160, 0, 0));
    private static readonly HashSet<string> IgnoredDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules" };

    private sealed record ToolboxItem(string? Type, string DisplayName, string Glyph, bool Supported);

    private static readonly IReadOnlyList<ToolboxItem> ToolboxItems =
    [
        new(null, "指针", "➤", true),
        new(YyControlTypes.Button, "按钮", "▣", true),
        new(YyControlTypes.Label, "标签", "ab", true),
        new(YyControlTypes.TextBox, "编辑框", "⌨", true),
        new(YyControlTypes.RadioButton, "单选框", "◉", true),
        new(YyControlTypes.CheckBox, "复选框", "☑", true),
        new(YyControlTypes.ComboBox, "组合框", "⌄", true),
        new(YyControlTypes.ListBox, "列表框", "☷", true),
        new(YyControlTypes.GroupBox, "分组框", "▤", true),
        new(YyControlTypes.PictureBox, "图片框", "▨", true),
        new(null, "外形框", "▢", false),
        new(null, "画板", "✎", false),
        new(null, "选择夹", "▥", false),
        new(null, "横向滚动条", "↔", false),
        new(null, "纵向滚动条", "↕", false),
        new(null, "进度条", "▤", false),
        new(null, "滑块条", "═", false),
        new(null, "超级链接", "⚭", false),
        new(null, "时钟", "◷", false),
        new(null, "通用对话框", "▣", false),
        new(null, "透", "透", false),
        new(null, "OCX", "OCX", false)
    ];
    private static readonly IReadOnlyDictionary<string, CodeSnippet> CodeSnippets =
        new Dictionary<string, CodeSnippet>(StringComparer.Ordinal)
        {
            ["子程序"] = new("子程序 名称(参数)\n    \n结束子程序", 4, 2),
            ["变量"] = new("变量 名称 = 值", 3, 2),
            ["如果"] = new("如果 条件 则\n    \n结束如果", 3, 2),
            ["计次循环"] = new("计次循环 序号 从 1 到 10\n    \n结束循环", 5, 2),
            ["遍历"] = new("遍历 项目 于 集合\n    \n结束遍历", 3, 2),
            ["尝试"] = new("尝试\n    \n捕获 错误\n    \n结束尝试", 7, 0)
        };

    private readonly ObservableCollection<EditorDocument> _documents = [];
    private readonly List<FrameworkElement> _selectionVisuals = [];
    private readonly List<YyControlModel> _selectedControls = [];
    private readonly Dictionary<YyControlModel, Rect> _designerDragBounds = [];
    private CompletionWindow? _completionWindow;
    private EditorDocument? _currentDocument;
    private string? _workspacePath;
    private YyProject? _project;
    private YyFormModel? _designForm;
    private EditorDocument? _designerDocument;
    private YyControlModel? _selectedControl;
    private FrameworkElement? _selectedElement;
    private DesignerDragMode _designerDragMode;
    private Point _designerDragStart;
    private Rect _designerOriginalBounds;
    private ResizeDirection _resizeDirection;
    private bool _designerDragChanged;
    private bool _updatingDesignerProperties;
    private bool _suppressPropertyCommit;
    private Point _toolboxDragStart;
    private bool _toolboxDragging;
    private StudioSettings _settings = new();
    private YyDebugProbe? _debugProbe;
    private YyDebugPause? _debugPaused;
    private string[] _debugSourceLines = [];
    private readonly Dictionary<YyCodeStatement, int> _debugLineMap = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _breakpoints = [];
    private readonly List<string> _watchNames = [];
    private readonly List<ClipHistoryRow> _clipHistory = [];
    private string? _lastClipText;
    private readonly StartPageView _startPageView = new();
    private List<YyControlModel> _designerClipboard = [];
    private GridLength _explorerWidth = new(240);
    private string? _helpFunctionsSource;
    private Dictionary<string, FunctionDeclaration> _helpFunctions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _runCancellation;
    private bool _isLoadingDocument;

    static MainWindow()
    {
        ReadyBrush.Freeze();
        WarningBrush.Freeze();
        ErrorBrush.Freeze();
    }

    public MainWindow()
    {
        StudioTrace.Write("MainWindow ctor begin");
        InitializeComponent();
        StudioTrace.Write("InitializeComponent complete");

        _settings = StudioSettings.Load();
        ConfigureEditor();
        ApplySettings();
        UpdateCompileTargetChecks();
        StudioTrace.Write("ConfigureEditor complete");
        WindowMenu.SubmenuOpened += (_, _) => BuildWindowMenu();
        var clipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        clipTimer.Tick += (_, _) => RecordClipHistory();
        clipTimer.Start();
        // 设计器控件剪贴板(文本框聚焦时由文本框自身处理剪切/复制/粘贴,不会到这里)
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            OnDesignerCopy,
            (_, args) => args.CanExecute =
                DesignerPanel.Visibility == Visibility.Visible && _selectedControls.Count > 0));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Cut,
            OnDesignerCut,
            (_, args) => args.CanExecute =
                DesignerPanel.Visibility == Visibility.Visible && _selectedControls.Count > 0));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Paste,
            OnDesignerPaste,
            (_, args) => args.CanExecute =
                DesignerPanel.Visibility == Visibility.Visible && _designerClipboard.Count > 0));
        BuildSupportTree();
        ControlToolbox.ItemsSource = ToolboxItems;
        DocumentTabs.ItemsSource = _documents;
        Grid.SetRow(_startPageView, 3);
        Grid.SetRowSpan(_startPageView, 2);
        DocumentArea.Children.Add(_startPageView);
        _startPageView.NewRequested += () => ShowNewProgramDialog();
        _startPageView.OpenRequested += () => OpenFileCommand.Execute(null, this);
        _startPageView.RecentSelected += path => OpenStartupPath(path);
        _startPageView.ClearRequested += () =>
        {
            _settings.RecentFiles.Clear();
            _settings.Save();
            RefreshStartPageRecent();
        };
        AddStartPage();
        if (!_settings.NoNewProgramDialog)
        {
            // 官方启动第一步:弹出“新建”程序类型对话框(勾选“启动后不再显示”可关闭)。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_currentDocument?.IsStartPage == true)
                {
                    ShowNewProgramDialog();
                }
            }));
        }

        StudioTrace.Write("Initial document complete");
        AppendOutput("易语 Studio 已就绪。按 F5 运行当前源码。");
        SetStatus("就绪", ReadyBrush);
        StudioTrace.Write("MainWindow ctor complete");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
    }

    public void OpenStartupPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                OpenWorkspace(Path.GetFullPath(path));
            }
            else if (File.Exists(path))
            {
                if (string.Equals(Path.GetExtension(path), ".yyproj", StringComparison.OrdinalIgnoreCase))
                {
                    OpenWorkspace(Path.GetDirectoryName(Path.GetFullPath(path))!);
                }
                else
                {
                    OpenDocument(path);
                }
            }
            else
            {
                SetStatus("启动参数指向的文件或目录不存在", WarningBrush);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(
                exception.Message,
                "无法打开",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return;
        }

        OpenStartupPath(paths[0]);
        e.Handled = true;
    }

    private void OnDeleteDesignerControl(object sender, RoutedEventArgs e) => DeleteSelectedControls();

    private void DeleteSelectedControls()
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        var controls = _selectedControls.ToArray();
        foreach (var control in controls)
        {
            _designForm.Controls.Remove(control);
            var element = FindControlElement(control);
            if (element is not null)
            {
                DesignerCanvas.Children.Remove(element);
            }
        }

        _selectedControls.Clear();
        _selectedControl = null;
        _selectedElement = null;
        ClearSelectionVisuals();
        PopulateDesignerProperties();
        DesignerChanged();
        SetStatus(
            controls.Length == 1
                ? $"已删除控件“{controls[0].Name}”"
                : $"已删除 {controls.Length} 个控件",
            WarningBrush);
    }

    private void OnDocumentTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is EditorDocument document)
        {
            ActivateDocument(document);
        }
    }

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditorDocument document })
        {
            CloseDocument(document);
            e.Handled = true;
        }
    }

    private sealed record VariableRow(string Key, string Value);

    private sealed record SearchResultRow(int Line, string Text);

    private sealed record ClipHistoryRow(string Text);

    private sealed record CodeSnippet(
        string InsertText,
        int CaretOffset,
        int SelectionLength);

    private sealed record ResizeHandleTag(ResizeDirection Direction);


    private sealed record ProjectEventLink(
        string FormPath,
        string? CodePath,
        string EventName,
        string ControlName);

    private enum DesignerDragMode
    {
        None,
        Move,
        Resize
    }

    private enum ControlEventKind
    {
        Click,
        TextChanged,
        SelectionChanged,
        CheckedChanged
    }

    private enum ResizeDirection
    {
        NorthWest,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
