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
    private GridLength _inspectorWidth = new(300);
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
        StructuredEditor.DesignerActivationRequested += OnStructuredDesignerActivationRequested;
        StructuredEditor.HintRequested += text => ShowHint(text);
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
        Grid.SetRow(_startPageView, 0);
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

    private void ConfigureEditor()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Yy.Studio.Highlighting.Yy.xshd");
        if (stream is not null)
        {
            using var reader = XmlReader.Create(stream);
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.HighlightCurrentLine = false;
        Editor.TextArea.TextView.LineTransformers.Add(new CurrentLineColorizer(Editor));
        Editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
        SearchPanel.Install(Editor.TextArea);
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.TextEntered += OnEditorTextEntered;
        Editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        Editor.Document.UndoStack.SizeLimit = 500;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingDocument || _currentDocument is null)
        {
            return;
        }

        _currentDocument.Text = Editor.Text;
        UpdateDocumentIdentity();
        UpdateEditorHelp();
        UpdateParameterHint();
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        CaretText.Text = $"行 {Editor.TextArea.Caret.Line}，列 {Editor.TextArea.Caret.Column}";
        UpdateEditorHelp();
        UpdateParameterHint();
    }

    private void OnEditorTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is not null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (char.IsLetter(character) || character == '_' || character == '.')
        {
            ShowCompletion();
        }
    }

    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            BottomTabs.SelectedIndex = 1;
            UpdateEditorHelp();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void ShowCompletion()
    {
        if (_completionWindow is not null)
        {
            return;
        }

        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            Width = 335,
            MaxHeight = 330
        };
        var data = _completionWindow.CompletionList.CompletionData;
        var known = new HashSet<string>(StringComparer.Ordinal);
        var form = GetAssociatedFormForCurrentDocument();
        var memberOwner = TryGetMemberOwnerName();
        if (form is not null && memberOwner is not null)
        {
            var control = form.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, memberOwner, StringComparison.Ordinal));
            var properties = new (string Name, string Description)[]
            {
                ("文本", "控件的文本、标题或内容"),
                ("名称", "控件名称"),
                ("可见", "是否显示控件"),
                ("启用", "是否允许用户操作"),
                ("选中", "复选框是否选中"),
                ("左", "控件的左侧坐标"),
                ("上", "控件的顶部坐标"),
                ("宽", "控件宽度"),
                ("高", "控件高度")
            };
            foreach (var (name, description) in properties)
            {
                if (known.Add(name))
                {
                    data.Add(new CompletionData(
                        name,
                        control is null ? description : $"{description} · {control.Name}",
                        CompletionKind.Property));
                }
            }

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
            return;
        }

        if (form is not null)
        {
            foreach (var control in form.Controls)
            {
                if (known.Add(control.Name))
                {
                    data.Add(new CompletionData(
                        control.Name,
                        $"组件 · {control.Type}",
                        CompletionKind.Component));
                }
            }
        }

        try
        {
            var program = YyRunner.CompileSource(Editor.Text, _currentDocument?.FilePath);
            foreach (var function in program.Functions)
            {
                if (known.Add(function.Name))
                {
                    var parameters = string.Join(
                        ", ",
                        function.Parameters.Select(parameter =>
                            parameter.TypeName is null
                                ? parameter.Name
                                : $"{parameter.Name}: {parameter.TypeName}"));
                    data.Add(new CompletionData(
                        function.Name,
                        $"子程序 · {function.Name}({parameters})",
                        CompletionKind.Snippet));
                }
            }
        }
        catch (YyDiagnostic)
        {
            // Completion still works while the document is temporarily incomplete.
        }

        foreach (var (text, description) in StudioCatalog.Completions)
        {
            if (!known.Add(text))
            {
                continue;
            }

            var kind = text is "输出" or "长度" or "类型" or "数字" or "文本" or "范围" or "包含"
                or "分割" or "连接" or "追加" or "移除" or "排序" or "JSON解析" or "JSON生成"
                or "当前时间" or "格式化时间" or "断言"
                ? CompletionKind.Builtin
                : CompletionKind.Keyword;
            if (CodeSnippets.TryGetValue(text, out var snippet))
            {
                data.Add(new CompletionData(
                    text,
                    description + " · 插入代码块",
                    CompletionKind.Snippet,
                    snippet.InsertText,
                    snippet.CaretOffset,
                    snippet.SelectionLength));
            }
            else
            {
                data.Add(new CompletionData(text, description, kind));
            }
        }

        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    private YyFormModel? GetAssociatedFormForCurrentDocument()
    {
        var formPath = GetAssociatedFormPathForCurrentDocument();
        if (formPath is null)
        {
            return null;
        }

        if (_project is not null)
        {
            var projectForm = _project.Forms.FirstOrDefault(form =>
                string.Equals(form.FormPath, formPath, StringComparison.OrdinalIgnoreCase));
            if (projectForm is not null)
            {
                return projectForm.Model;
            }
        }

        try
        {
            return YyFormSerializer.Deserialize(File.ReadAllText(formPath));
        }
        catch (Exception exception) when (
            exception is YyFormFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? GetAssociatedFormPathForCurrentDocument()
    {
        var path = _currentDocument?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_project is not null)
        {
            var projectForm = _project.Forms.FirstOrDefault(form =>
                string.Equals(form.CodePath, path, StringComparison.OrdinalIgnoreCase));
            if (projectForm is not null)
            {
                return projectForm.FormPath;
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return null;
        }

        var candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".yyx");
        return File.Exists(candidate) ? candidate : null;
    }

    private string? TryGetMemberOwnerName()
    {
        var caret = Editor.CaretOffset;
        if (caret <= 0)
        {
            return null;
        }

        var text = Editor.Text;
        var cursor = caret - 1;
        while (cursor >= 0 && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
        {
            cursor--;
        }

        while (cursor >= 0 && char.IsWhiteSpace(text[cursor]))
        {
            cursor--;
        }

        if (cursor < 0 || text[cursor] != '.')
        {
            return null;
        }

        var end = cursor - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
        {
            start--;
        }

        return end > start ? text[(start + 1)..(end + 1)] : null;
    }

    private void UpdateParameterHint()
    {
        if (_currentDocument?.IsForm == true)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var caret = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
        var line = Editor.Document.GetLineByOffset(caret);
        var prefix = Editor.Document.GetText(line.Offset, caret - line.Offset);
        var openIndex = FindActiveOpenParenthesis(prefix);
        if (openIndex < 0)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var nameEnd = openIndex - 1;
        while (nameEnd >= 0 && char.IsWhiteSpace(prefix[nameEnd]))
        {
            nameEnd--;
        }

        var nameStart = nameEnd;
        while (nameStart >= 0 &&
               (char.IsLetterOrDigit(prefix[nameStart]) || prefix[nameStart] == '_'))
        {
            nameStart--;
        }

        if (nameEnd <= nameStart)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var name = prefix[(nameStart + 1)..(nameEnd + 1)];
        var callable = FindCallable(name);
        if (callable is null)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var argumentIndex = CountTopLevelArguments(prefix[(openIndex + 1)..]) + 1;
        ParameterHintText.Text =
            $"{callable.Value.Signature}    当前参数 {argumentIndex}    {callable.Value.Description}";
        ParameterHintBar.Visibility = Visibility.Visible;
    }

    private void UpdateEditorHelp()
    {
        if (_currentDocument?.IsForm == true)
        {
            EditorHelpBox.Text =
                "窗体设计模式\n\n" +
                "单击控件编辑属性；双击控件生成或打开默认事件代码；F5 预览窗体。";
            return;
        }

        var caret = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
        var word = GetWordAtOffset(Editor.Text, caret);
        var form = GetAssociatedFormForCurrentDocument();
        var memberOwner = TryGetMemberOwnerName();
        var ownerControl = form?.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, memberOwner, StringComparison.Ordinal));
        if (ownerControl is not null && !string.IsNullOrWhiteSpace(word))
        {
            EditorHelpBox.Text =
                $"{ownerControl.Name}.{word}\n\n" +
                $"组件类型：{ownerControl.Type}\n" +
                $"当前值：{GetControlPropertySummary(ownerControl, word)}\n\n" +
                "可使用赋值语句直接修改属性，例如：\n" +
                $"{ownerControl.Name}.{word} = 新值";
            return;
        }

        var control = form?.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, word, StringComparison.Ordinal));
        if (control is not null)
        {
            EditorHelpBox.Text =
                $"组件：{control.Name}\n" +
                $"类型：{control.Type}\n" +
                $"位置：左 {control.X}，上 {control.Y}；尺寸：{control.Width} × {control.Height}\n\n" +
                "可引用属性：文本、名称、可见、启用、选中、左、上、宽、高。\n" +
                $"示例：{control.Name}.文本 = \"新值\"";
            return;
        }

        var callable = FindCallable(word);
        if (callable is not null)
        {
            EditorHelpBox.Text =
                $"{callable.Value.Signature}\n\n{callable.Value.Description}\n\n" +
                "F1：上下文帮助；Ctrl+Space：命令补全。";
            return;
        }

        var keyword = StudioCatalog.Completions.FirstOrDefault(item =>
            string.Equals(item.Text, word, StringComparison.Ordinal));
        EditorHelpBox.Text = keyword.Text is null
            ? "将光标移到命令、函数或组件名上查看帮助。按 F1 打开提示面板，按 Ctrl+Space 打开补全。"
            : $"{keyword.Text}\n\n{keyword.Description}\n\nF1：上下文帮助；Ctrl+Space：命令补全。";
    }

    private static string GetControlPropertySummary(YyControlModel control, string property)
    {
        return property switch
        {
            "文本" => control.Text,
            "名称" => control.Name,
            "可见" => control.IsVisible ? "真" : "假",
            "启用" => control.IsEnabled ? "真" : "假",
            "选中" => control.IsChecked ? "真" : "假",
            "左" => control.X.ToString(),
            "上" => control.Y.ToString(),
            "宽" => control.Width.ToString(),
            "高" => control.Height.ToString(),
            _ => "可通过“组件名.属性名”访问"
        };
    }

    private (string Signature, string Description)? FindCallable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builtin = StudioCatalog.Builtins.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (builtin is not null)
        {
            return (builtin.Signature, builtin.Description);
        }

        if (!string.Equals(_helpFunctionsSource, Editor.Text, StringComparison.Ordinal))
        {
            _helpFunctionsSource = Editor.Text;
            _helpFunctions = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
            try
            {
                var program = YyRunner.CompileSource(Editor.Text, _currentDocument?.FilePath);
                _helpFunctions = program.Functions.ToDictionary(
                    function => function.Name,
                    StringComparer.Ordinal);
            }
            catch (YyDiagnostic)
            {
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (_helpFunctions.TryGetValue(name, out var userFunction))
        {
            var parameters = string.Join(
                ", ",
                userFunction.Parameters.Select(parameter =>
                    $"{parameter.Name}{(parameter.TypeName is null ? string.Empty : ": " + parameter.TypeName)}" +
                    (parameter.DefaultValue is null ? string.Empty : " = 默认值")));
            return (
                $"{userFunction.Name}({parameters}): {userFunction.ReturnType ?? "任意"}",
                "当前文件定义的子程序。");
        }

        return null;
    }

    private static string GetWordAtOffset(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var position = Math.Clamp(offset, 0, text.Length);
        if (position == text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position = Math.Max(0, position - 1);
        }

        if (position >= text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            return string.Empty;
        }

        var start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var end = position + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return text[start..end];
    }

    private static int FindActiveOpenParenthesis(string prefix)
    {
        var depth = 0;
        for (var index = prefix.Length - 1; index >= 0; index--)
        {
            if (prefix[index] == ')')
            {
                depth++;
            }
            else if (prefix[index] == '(')
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    private static int CountTopLevelArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var depth = 0;
        var commas = 0;
        foreach (var character in text)
        {
            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
    }

    private void OnNewFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is not null)
        {
            try
            {
                var sourcePath = _project.CreateSourceFile();
                OpenWorkspace(_project.DirectoryPath);
                OpenDocument(sourcePath);
                SetStatus($"已添加项目源码：{Path.GetFileName(sourcePath)}", ReadyBrush);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"无法创建项目源码：\n{exception.Message}",
                    "新建源码",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        AddNewDocument(StudioCatalog.StarterCode);
        OutputBox.Clear();
        VariableList.ItemsSource = null;
        RunInfoText.Text = string.Empty;
        SetStatus("已新建源码标签", ReadyBrush);
    }

    private void OnProjectPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show(
                "请先打开一个 .yyproj 项目。",
                "项目属性",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ProjectPropertiesWindow(_project) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.UpdatedProject is null)
        {
            return;
        }

        _project = dialog.UpdatedProject;
        OpenWorkspace(_project.DirectoryPath);
        SetStatus($"项目属性已保存：{_project.Name}", ReadyBrush);
    }

    private void OnNewProject(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "选择新项目的位置和名称",
            Filter = "易语项目 (*.yyproj)|*.yyproj",
            DefaultExt = ".yyproj",
            AddExtension = true,
            FileName = "新建易语项目.yyproj",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            MessageBox.Show(
                "项目文件已经存在。请使用“打开文件夹”打开已有项目。",
                "新建项目",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var projectName = Path.GetFileNameWithoutExtension(dialog.FileName);
        try
        {
            var project = YyProject.CreateProject(dialog.FileName, projectName);
            OpenWorkspace(project.DirectoryPath);
            SetStatus($"已创建项目：{project.Name}", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                $"无法创建项目：\n{exception.Message}",
                "新建项目",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnNewForm(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is not null)
        {
            try
            {
                var created = _project.CreateForm("新窗体");
                OpenWorkspace(_project.DirectoryPath);
                OpenDocument(created.FormPath);
                SetStatus($"已创建项目窗体：{Path.GetFileName(created.FormPath)}", ReadyBrush);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"无法创建项目窗体：\n{exception.Message}",
                    "新建窗体",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var document = new EditorDocument(
            null,
            NextUntitledFormName(),
            YyFormSerializer.CreateEmptyDocument("易语窗体"));
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
        SetStatus("已新建窗体标签", ReadyBrush);
    }

    /// <summary>“新建”程序类型对话框(对齐官方:点新建弹出类型选择,确定后内存中直接建程序)。</summary>
    private void OnNewProgramClick(object sender, RoutedEventArgs e) => ShowNewProgramDialog();

    private void ShowNewProgramDialog()
    {
        var dialog = new NewProgramDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.SuppressOnStartup && !_settings.NoNewProgramDialog)
        {
            _settings.NoNewProgramDialog = true;
            _settings.Save();
        }

        switch (dialog.SelectedKind)
        {
            case NewProgramDialog.ProgramKind.Window:
                CreateBlankWindowProgram();
                break;
            case NewProgramDialog.ProgramKind.Console:
                AddNewDocument(
                    ".版本 3\n.程序集 控制台程序\n\n' 控制台程序入口\n输出(\"你好，易语！\")\n");
                SetStatus("已新建易语控制台程序", ReadyBrush);
                break;
            case NewProgramDialog.ProgramKind.Module:
                AddNewDocument(
                    ".版本 3\n.程序集 模块1\n\n' 易模块:在子程序表把“易包”设为“真”后可编译导出\n子程序 示例函数(参数: 文本): 文本\n    返回 参数\n结束子程序\n");
                SetStatus("已新建易语模块", ReadyBrush);
                break;
            case NewProgramDialog.ProgramKind.Project:
                NewProjectCommand.Execute(null, this);
                break;
            case NewProgramDialog.ProgramKind.OpenFile:
                OpenFileCommand.Execute(null, this);
                break;
            case NewProgramDialog.ProgramKind.Sample when dialog.SelectedSamplePath is not null:
                OpenStartupPath(dialog.SelectedSamplePath);
                break;
        }
    }

    /// <summary>
    /// 官方“Windows 窗口程序”:内存中立即创建 _启动窗口 空白程序(窗体+代码后置),
    /// 不弹任何保存对话框,直接进入窗体设计器。
    /// </summary>
    private void CreateBlankWindowProgram()
    {
        var formName = "_启动窗口";
        var form = new YyFormModel
        {
            Name = formName,
            CodeFile = "未命名窗体.yyc",
            Title = "窗口1",
            Width = 500,
            Height = 340
        };
        var formDocument = new EditorDocument(
            null,
            NextUntitledFormName(),
            YyFormSerializer.Serialize(form));
        var codeModel = new YyCodeDocument { Name = "窗口程序集1" };
        var codeDocument = new EditorDocument(
            null,
            "未命名窗体.yyc",
            YyCodeSerializer.Serialize(codeModel));
        _documents.Add(codeDocument);
        _documents.Add(formDocument);
        DocumentTabs.SelectedItem = formDocument;
        SetStatus("已新建易语窗口程序：_启动窗口", ReadyBrush);
    }

    private void OnOpenFile(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开易语文件",
            Filter = "易语文件 (*.yyc;*.yyx)|*.yyc;*.yyx|结构化代码 (*.yyc)|*.yyc|窗体设计 (*.yyx)|*.yyx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OnOpenFolder(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "打开易语项目文件夹",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenWorkspace(dialog.FolderName);
        }
    }

    private void OnSaveFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            SaveDocument(_currentDocument);
        }
    }

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            SaveDocumentAs(_currentDocument);
        }
    }

    private void OnCloseTab(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            CloseDocument(_currentDocument);
        }
    }

    private async void OnRun(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument?.IsStartPage == true)
        {
            ShowHint("请先在起始页“新建”或“打开…”一个程序。");
            return;
        }

        if (_currentDocument?.IsForm == true)
        {
            PreviewCurrentForm();
            return;
        }

        if (_debugPaused is not null && _debugProbe is not null)
        {
            _debugPaused = null;
            _debugProbe.Continue();
            SetStatus("调试继续（全速）…", WarningBrush);
            RunInfoText.Text = "调试运行中…";
            return;
        }

        await RunDocumentAsync();
    }

    private void OnDebugStep(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepInto);

    private void OnDebugStepOver(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepOver);

    private void OnDebugStepOut(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepOut);

    private void OnDebugRunToCursor(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.RunToLine);

    private void DebugResumeOrStart(YyDebugMode mode)
    {
        if (_runCancellation is not null)
        {
            if (_debugProbe is null || _debugPaused is null)
            {
                SetStatus("程序正在运行（未暂停）…", WarningBrush);
                return;
            }

            var probe = _debugProbe;
            var pause = _debugPaused;
            switch (mode)
            {
                case YyDebugMode.StepInto:
                    _debugPaused = null;
                    probe.StepInto();
                    break;
                case YyDebugMode.StepOver:
                    _debugPaused = null;
                    probe.StepOver(pause.Depth);
                    break;
                case YyDebugMode.StepOut:
                    _debugPaused = null;
                    probe.StepOut(pause.Depth);
                    break;
                case YyDebugMode.RunToLine:
                    if (!TryGetCursorLine(out var target))
                    {
                        SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
                        return;
                    }

                    _debugPaused = null;
                    probe.RunToLine(target, pause.Depth);
                    break;
            }

            SetStatus("调试继续…", WarningBrush);
            RunInfoText.Text = "调试运行中…";
            return;
        }

        var probe2 = new YyDebugProbe(mode);
        if (mode == YyDebugMode.RunToLine)
        {
            if (!TryGetCursorLine(out var line))
            {
                SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
                return;
            }

            probe2 = new YyDebugProbe(YyDebugMode.RunToLine, line);
        }

        _ = RunDocumentAsync(probe2);
    }

    private bool TryGetCursorLine(out int line)
    {
        line = 0;
        if (DesignerPanel.Visibility == Visibility.Visible)
        {
            return false;
        }

        if (StructuredEditor.Visibility == Visibility.Visible)
        {
            EnsureLineMap();
            var statement = StructuredEditor.SelectedStatement;
            return statement is not null && _debugLineMap.TryGetValue(statement, out line);
        }

        if (Editor.Visibility == Visibility.Visible)
        {
            var offset = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
            line = Editor.Document.GetLineByOffset(offset).LineNumber;
            return true;
        }

        return false;
    }

    /// <summary>按需生成 语句→生成源码行号 映射(断点/到光标处在未运行时也要用)。</summary>
    private void EnsureLineMap()
    {
        if (_debugLineMap.Count > 0 || StructuredEditor.Document is null)
        {
            return;
        }

        YyCodeDocumentConverter.ToSource(StructuredEditor.Document, _debugLineMap);
    }

    private void OnDebugToggleBreakpoint(object sender, ExecutedRoutedEventArgs e)
    {
        if (!TryGetCursorLine(out var line))
        {
            SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
            return;
        }

        string message;
        if (_breakpoints.TryRemove(line, out _))
        {
            message = $"已清除第 {line} 行断点";
        }
        else
        {
            _breakpoints[line] = 0;
            message = $"已在第 {line} 行设置断点";
        }

        UpdateBreakpointMarkers();
        SetStatus(message, ReadyBrush);
    }

    private void UpdateBreakpointMarkers()
    {
        if (StructuredEditor.Document is null)
        {
            return;
        }

        EnsureLineMap();
        var statements = new HashSet<YyCodeStatement>();
        foreach (var pair in _debugLineMap)
        {
            if (_breakpoints.ContainsKey(pair.Value))
            {
                statements.Add(pair.Key);
            }
        }

        StructuredEditor.SetBreakpointStatements(statements);
    }

    // =============== 底部面板:监视表/搜寻/剪辑历史 ===============

    private void OnWatchAddClick(object sender, RoutedEventArgs e) => AddWatch();

    private void OnWatchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddWatch();
            e.Handled = true;
        }
    }

    private void AddWatch()
    {
        var name = WatchInputBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || _watchNames.Contains(name, StringComparer.Ordinal))
        {
            return;
        }

        _watchNames.Add(name);
        WatchInputBox.Text = string.Empty;
        RefreshWatches();
    }

    private void OnWatchRemoveClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in WatchList.SelectedItems.OfType<VariableRow>().ToList())
        {
            _watchNames.Remove(item.Key);
        }

        RefreshWatches();
    }

    private void RefreshWatches(IReadOnlyDictionary<string, object?>? variables = null)
    {
        WatchList.ItemsSource = _watchNames
            .Select(name => new VariableRow(
                name,
                variables is not null && variables.TryGetValue(name, out var value)
                    ? DisplayValue(value)
                    : "（运行暂停时显示）"))
            .ToList();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => RunSearch();

    private void OnSearchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        var keyword = SearchInputBox.Text;
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var text = StructuredEditor.Visibility == Visibility.Visible
            ? StructuredEditor.PlainText
            : Editor.Text;
        var rows = new List<SearchResultRow>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new SearchResultRow(i + 1, lines[i].Trim()));
            }
        }

        SearchResultsList1.ItemsSource = rows;
        BottomTabs.SelectedIndex = 5;
        SetStatus($"搜寻“{keyword}”：找到 {rows.Count} 处", ReadyBrush);
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not SearchResultRow row)
        {
            return;
        }

        if (Editor.Visibility == Visibility.Visible)
        {
            if (row.Line <= Editor.Document.LineCount)
            {
                var line = Editor.Document.GetLineByNumber(row.Line);
                Editor.Select(line.Offset, 0);
                Editor.ScrollToLine(row.Line);
                Editor.Focus();
            }

            return;
        }

        if (StructuredEditor.Visibility == Visibility.Visible)
        {
            foreach (var pair in _debugLineMap)
            {
                if (pair.Value == row.Line)
                {
                    StructuredEditor.SelectStatement(pair.Key);
                    return;
                }
            }
        }
    }

    private void OnClipHistoryInsert(object sender, MouseButtonEventArgs e)
    {
        if (ClipHistoryList.SelectedItem is ClipHistoryRow row &&
            Editor.Visibility == Visibility.Visible)
        {
            Editor.Document.Insert(Editor.CaretOffset, row.Text);
            Editor.Focus();
        }
    }

    private void RecordClipHistory()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _lastClipText, StringComparison.Ordinal))
            {
                return;
            }

            _lastClipText = text;
            var display = text.Replace("\r", string.Empty).Replace("\n", " ");
            if (display.Length > 200)
            {
                display = display[..200] + "…";
            }

            _clipHistory.Insert(0, new ClipHistoryRow(text));
            if (_clipHistory.Count > 50)
            {
                _clipHistory.RemoveAt(_clipHistory.Count - 1);
            }

            ClipHistoryList.ItemsSource = _clipHistory.ToList();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ExternalException)
        {
            // 剪贴板被占用时忽略本次记录。
        }
    }

    private void OnDebugPaused(YyDebugPause pause)
    {
        _debugPaused = pause;
        var lineText = pause.Line > 0 && pause.Line <= _debugSourceLines.Length
            ? _debugSourceLines[pause.Line - 1].Trim()
            : string.Empty;
        SetStatus($"调试暂停 · 第 {pause.Line} 行：{lineText}", WarningBrush);
        RunInfoText.Text = $"暂停 · 第 {pause.Line} 行";

        CallStackList.Items.Clear();
        CallStackList.Items.Add(new ListBoxItem { Content = $"第 {pause.Line} 行    {lineText}" });
        for (var depth = 0; depth < pause.Depth; depth++)
        {
            CallStackList.Items.Add(new ListBoxItem { Content = $"（外层调用 {depth + 1}）" });
        }

        ShowVariables(pause.Variables);
        RefreshWatches(pause.Variables);
        BottomTabs.SelectedIndex = 4;
    }

    private async void OnRunProject(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show(
                "当前目录没有可读取的 .yyproj 项目清单。",
                "运行项目",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenDocument(_project.EntryPath);
        var succeeded = await RunDocumentAsync();
        if (!succeeded || string.IsNullOrWhiteSpace(_project.StartupForm))
        {
            return;
        }

        var startupForm = _project.Forms.FirstOrDefault(form =>
            string.Equals(form.Model.Name, _project.StartupForm, StringComparison.Ordinal));
        if (startupForm is null)
        {
            return;
        }

        var preview = new YyFormPreviewWindow(startupForm.Model, startupForm.FormPath)
        {
            Owner = this
        };
        preview.Show();
    }

    private async void OnCompileProject(object sender, ExecutedRoutedEventArgs e)
    {
        await CompileProjectAsync(selfContained: false);
    }

    private async void OnStandaloneCompileClick(object sender, RoutedEventArgs e)
    {
        await CompileProjectAsync(selfContained: true);
    }

    private void OnCompileTargetOsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string rid })
        {
            return;
        }

        if (!rid.StartsWith("win-", StringComparison.Ordinal))
        {
            MessageBox.Show(
                this,
                "生成的应用基于 WPF 界面,目的操作系统目前仅支持 Windows。\nLinux/macOS 目标将在后续版本提供。",
                "编译到目的操作系统",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _settings.TargetRid = rid;
        _settings.Save();
        UpdateCompileTargetChecks();
        SetStatus($"目的操作系统:{(rid == "win-arm64" ? "Windows ARM64" : "Windows x64")}", ReadyBrush);
    }

    private void UpdateCompileTargetChecks()
    {
        TargetOsWinX64MenuItem.IsChecked = _settings.TargetRid == "win-x64";
        TargetOsWinArm64MenuItem.IsChecked = _settings.TargetRid == "win-arm64";
    }

    private async Task CompileProjectAsync(bool selfContained)
    {
        if (_project is null)
        {
            MessageBox.Show(
                "请先打开一个 .yyproj 项目。",
                "编译为 EXE",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!SaveDocumentsForBuild())
        {
            return;
        }

        try
        {
            _project = YyProject.LoadForDirectory(_project.DirectoryPath)
                ?? throw new InvalidDataException("项目清单已经不存在。");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "编译为 EXE", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = selfContained ? "独立编译为 Windows 可执行文件" : "编译为 Windows 可执行文件",
            Filter = "Windows 应用 (*.exe)|*.exe",
            DefaultExt = ".exe",
            AddExtension = true,
            FileName = _project.OutputName,
            InitialDirectory = _project.DirectoryPath
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        CompileProjectButton.IsEnabled = false;
        CompileProjectButton.Content = "编译中…";
        SetStatus("正在编译项目…", WarningBrush);
        var progress = new Progress<string>(message => SetStatus(message, WarningBrush));
        try
        {
            var compiler = new ProjectCompiler();
            var output = await compiler.CompileAsync(
                _project,
                dialog.FileName,
                selfContained,
                _settings.TargetRid,
                progress);
            SetStatus("EXE 编译完成", ReadyBrush);
            MessageBox.Show(
                selfContained
                    ? $"独立编译完成：\n{output}\n\n可以直接双击运行，不需要源码目录或 .NET 运行时。"
                    : $"编译完成：\n{output}\n\n目标机器需要安装 .NET 桌面运行时才能运行。",
                "编译完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SetStatus("EXE 编译失败", ErrorBrush);
            MessageBox.Show(
                exception.Message,
                "编译失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CompileProjectButton.Content = "编译 EXE";
            CompileProjectButton.IsEnabled = true;
        }
    }

    private bool SaveDocumentsForBuild()
    {
        var dirty = _documents.Where(document => document.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return true;
        }

        var result = MessageBox.Show(
            $"有 {dirty.Count} 个文件尚未保存。编译前需要保存这些文件，是否继续？",
            "保存并编译",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var document in dirty)
        {
            if (!SaveDocument(document))
            {
                return false;
            }
        }

        return true;
    }

    private void OnStop(object sender, ExecutedRoutedEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _debugProbe?.Stop();
            _debugPaused = null;
            _runCancellation.Cancel();
            SetStatus("正在停止…", WarningBrush);
            RunInfoText.Text = "已请求停止";
        }
    }

    private void OnPreviewForm(object sender, RoutedEventArgs e) => PreviewCurrentForm();

    private void OnOpenFormCodeBehind(object sender, RoutedEventArgs e)
    {
        if (_designForm is null ||
            string.IsNullOrWhiteSpace(_designForm.CodeFile) ||
            string.IsNullOrWhiteSpace(_currentDocument?.FilePath))
        {
            MessageBox.Show(
                "当前窗体没有关联代码后置文件。请先设置“代码后置文件”并保存窗体。",
                "代码后置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var formDirectory = Path.GetDirectoryName(Path.GetFullPath(_currentDocument.FilePath));
        if (formDirectory is null)
        {
            return;
        }

        var codePath = Path.GetFullPath(Path.Combine(formDirectory, _designForm.CodeFile));
        if (!File.Exists(codePath))
        {
            MessageBox.Show(
                $"找不到代码后置文件：\n{codePath}",
                "代码后置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        OpenDocument(codePath);
    }

    private void CreateOrOpenControlEventHandler(
        YyControlModel control,
        ControlEventKind eventKind)
    {
        if (_designForm is null)
        {
            return;
        }

        // 对齐官方:未保存的内存窗体也能直接生成事件代码(操作内存中的代码后置文档)。
        var isFileBacked = !string.IsNullOrWhiteSpace(_currentDocument?.FilePath);
        string? formDirectory = isFileBacked ? Path.GetDirectoryName(_currentDocument!.FilePath)! : null;
        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? (isFileBacked
                ? Path.GetFileNameWithoutExtension(_currentDocument!.FilePath!)
                : Path.GetFileNameWithoutExtension(_currentDocument?.DisplayName ?? "未命名窗体")) + ".yyc"
            : _designForm.CodeFile;
        string? codePath = isFileBacked ? Path.GetFullPath(Path.Combine(formDirectory!, codeFileName)) : null;
        var codeDocument = codePath is not null
            ? _documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, codePath, StringComparison.OrdinalIgnoreCase))
            : _documents.FirstOrDefault(document =>
                document.FilePath is null &&
                string.Equals(document.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(Path.GetExtension(codeFileName), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            CreateStructuredEventHandler(
                control,
                eventKind,
                codePath,
                codeDocument,
                formDirectory);
            return;
        }

        var source = codeDocument?.Text
            ?? (codePath is not null && File.Exists(codePath)
                ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                : $"""
                   .版本 3
                   .程序集 {_designForm.Name}代码

                   ' 在这里编写窗体事件代码。
                   """);

        var handlerName = GetControlEvent(control, eventKind);
        if (string.IsNullOrWhiteSpace(handlerName))
        {
            var baseName = EventHandlerNaming.ForControl(control.Name, GetEventDisplayName(eventKind));
            var existing = YyRunner.CompileSource(source, codePath).Functions
                .Select(function => function.Name)
                .ToHashSet(StringComparer.Ordinal);
            handlerName = EventHandlerNaming.Unique(baseName, existing.Contains);
        }

        var functionMarker = "子程序 " + handlerName;
        if (!source.Contains(functionMarker, StringComparison.Ordinal))
        {
            source = source.TrimEnd() + Environment.NewLine + Environment.NewLine +
                $"""
                 子程序 {handlerName}()
                     ' 在这里编写 {control.Name} 的 {GetEventDisplayName(eventKind)} 逻辑。
                 结束子程序
                 """ + Environment.NewLine;
        }

        if (codeDocument is null)
        {
            codeDocument = codePath is not null
                ? new EditorDocument(codePath, Path.GetFileName(codePath), source)
                : new EditorDocument(null, codeFileName, source);
            _documents.Add(codeDocument);
            if (codePath is null || !File.Exists(codePath))
            {
                codeDocument.MarkModified();
            }
        }
        else
        {
            codeDocument.Text = source;
        }

        SetControlEvent(control, eventKind, handlerName);
        if (formDirectory is not null && codePath is not null)
        {
            _designForm.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
        }

        DesignerChanged();
        DocumentTabs.SelectedItem = codeDocument;
        var offset = codeDocument.Text.IndexOf(functionMarker, StringComparison.Ordinal);
        if (offset >= 0)
        {
            Editor.CaretOffset = offset;
            var line = Editor.Document.GetLineByOffset(offset);
            Editor.Select(line.Offset, line.Length);
            Editor.ScrollToLine(line.LineNumber);
        }

        SetStatus($"已绑定 {GetEventDisplayName(eventKind)} 事件 {handlerName}()", ReadyBrush);
    }

    private void CreateStructuredEventHandler(
        YyControlModel control,
        ControlEventKind eventKind,
        string? codePath,
        EditorDocument? codeDocument,
        string? formDirectory)
    {
        try
        {
            var existingText = codeDocument?.Text
                ?? (codePath is not null && File.Exists(codePath)
                    ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                    : null);
            var model = string.IsNullOrWhiteSpace(existingText)
                ? new YyCodeDocument { Name = _designForm?.Name + "代码" ?? "窗体代码" }
                : YyCodeSerializer.Deserialize(existingText);
            var handlerName = GetControlEvent(control, eventKind);
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                var baseName = EventHandlerNaming.ForControl(control.Name, GetEventDisplayName(eventKind));
                var existingNames = model.Functions
                    .Select(function => function.Name)
                    .ToHashSet(StringComparer.Ordinal);
                handlerName = EventHandlerNaming.Unique(baseName, existingNames.Contains);
            }

            if (model.Functions.All(function =>
                    !string.Equals(function.Name, handlerName, StringComparison.Ordinal)))
            {
                model.Functions.Add(new YyCodeFunction { Name = handlerName });
            }

            var serialized = YyCodeSerializer.Serialize(model);
            var changed = !string.Equals(existingText, serialized, StringComparison.Ordinal);
            if (codeDocument is null)
            {
                codeDocument = codePath is not null
                    ? new EditorDocument(codePath, Path.GetFileName(codePath), serialized)
                    : new EditorDocument(null, _designForm?.CodeFile ?? "未命名窗体.yyc", serialized);
                _documents.Add(codeDocument);
                if (changed || codePath is null || !File.Exists(codePath))
                {
                    codeDocument.MarkModified();
                }
            }
            else
            {
                codeDocument.Text = serialized;
            }

            SetControlEvent(control, eventKind, handlerName);
            if (formDirectory is not null && codePath is not null)
            {
                _designForm!.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
            }

            DesignerChanged();
            DocumentTabs.SelectedItem = codeDocument;
            StructuredEditor.SelectFunction(handlerName);
            SetStatus($"已绑定 {GetEventDisplayName(eventKind)} 事件 {handlerName}()", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException)
        {
            MessageBox.Show(
                $"无法创建事件代码：\n{exception.Message}",
                "生成事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? GetControlEvent(YyControlModel control, ControlEventKind eventKind)
    {
        return eventKind switch
        {
            ControlEventKind.TextChanged => control.TextChangedEvent,
            ControlEventKind.SelectionChanged => control.SelectionChangedEvent,
            ControlEventKind.CheckedChanged => control.CheckedChangedEvent,
            _ => control.ClickEvent
        };
    }

    private static void SetControlEvent(
        YyControlModel control,
        ControlEventKind eventKind,
        string? eventName)
    {
        switch (eventKind)
        {
            case ControlEventKind.TextChanged:
                control.TextChangedEvent = eventName;
                break;
            case ControlEventKind.SelectionChanged:
                control.SelectionChangedEvent = eventName;
                break;
            case ControlEventKind.CheckedChanged:
                control.CheckedChangedEvent = eventName;
                break;
            default:
                control.ClickEvent = eventName;
                break;
        }
    }

    private static string GetEventDisplayName(ControlEventKind eventKind) => eventKind switch
    {
        ControlEventKind.TextChanged => EventHandlerNaming.TextChanged,
        ControlEventKind.SelectionChanged => EventHandlerNaming.SelectionChanged,
        ControlEventKind.CheckedChanged => EventHandlerNaming.CheckedChanged,
        _ => EventHandlerNaming.Click
    };

    /// <summary>
    /// 构建 事件子程序名 → 组件关联描述 映射(如 "_按钮1_被单击" → "按钮1 · 被单击"),
    /// 用于代码编辑页显示子程序与窗体组件的对应关系。
    /// </summary>
    private Dictionary<string, string> BuildEventAssociations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void AddForm(YyFormModel form)
        {
            void Add(string? handler, string display)
            {
                if (!string.IsNullOrWhiteSpace(handler))
                {
                    map[handler] = display;
                }
            }

            Add(form.LoadEvent, form.Name + " · " + EventHandlerNaming.FormLoad);
            Add(form.CloseEvent, form.Name + " · " + EventHandlerNaming.FormClose);
            foreach (var control in form.Controls)
            {
                Add(control.ClickEvent, control.Name + " · " + EventHandlerNaming.Click);
                Add(control.TextChangedEvent, control.Name + " · " + EventHandlerNaming.TextChanged);
                Add(control.SelectionChangedEvent, control.Name + " · " + EventHandlerNaming.SelectionChanged);
                Add(control.CheckedChangedEvent, control.Name + " · " + EventHandlerNaming.CheckedChanged);
            }
        }

        var associated = GetAssociatedFormForCurrentDocument();
        if (associated is not null)
        {
            AddForm(associated);
        }
        else if (_project is not null)
        {
            foreach (var form in _project.Forms)
            {
                AddForm(form.Model);
            }
        }
        else if (_designForm is not null)
        {
            AddForm(_designForm);
        }

        return map;
    }

    private void PreviewCurrentForm()
    {
        StudioTrace.Write($"Preview form requested; form loaded={_designForm is not null}");
        if (_designForm is null)
        {
            MessageBox.Show(
                "请先打开一个可解析的 .yyx 窗体文件。",
                "预览窗体",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preview = new YyFormPreviewWindow(_designForm, _currentDocument?.FilePath, ResolveCodeBehindText())
        {
            Owner = this
        };
        preview.Closed += (_, _) =>
        {
            StudioTrace.Write("Form preview closed");
            if (ReferenceEquals(_lastPreview, preview))
            {
                _lastPreview = null;
            }
        };
        _lastPreview = preview;
        preview.Show();
        StudioTrace.Write($"Form preview shown: {preview.Title}");
        RunInfoText.Text = $"窗体预览：{_designForm.Title}";
        SetStatus("窗体预览已打开", ReadyBrush);
    }

    /// <summary>
    /// 取当前窗体的代码后置文本(优先编辑器中的内存文档,其次磁盘文件),
    /// 让未保存的窗体也能在预览中运行事件代码(对齐官方)。
    /// </summary>
    private string? ResolveCodeBehindText()
    {
        if (_designForm is null)
        {
            return null;
        }

        var codeFileName = _designForm.CodeFile;
        var document = codeFileName is null
            ? null
            : _documents.FirstOrDefault(item =>
                  item.FilePath is not null &&
                  string.Equals(Path.GetFileName(item.FilePath), Path.GetFileName(codeFileName), StringComparison.OrdinalIgnoreCase))
              ?? _documents.FirstOrDefault(item =>
                  item.FilePath is null &&
                  string.Equals(item.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));
        if (document is not null)
        {
            return document.Text;
        }

        var path = ResolveCodeBehindPath();
        return path is not null && File.Exists(path)
            ? File.ReadAllText(path, new UTF8Encoding(false, true))
            : null;
    }

    /// <summary>解析当前窗体的代码后置磁盘路径(仅文件窗体有效)。</summary>
    private string? ResolveCodeBehindPath()
    {
        if (_designForm is null || string.IsNullOrWhiteSpace(_currentDocument?.FilePath))
        {
            return null;
        }

        var formDirectory = Path.GetDirectoryName(_currentDocument.FilePath);
        if (formDirectory is null)
        {
            return null;
        }

        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? Path.GetFileNameWithoutExtension(_currentDocument.FilePath) + ".yyc"
            : _designForm.CodeFile;
        return Path.GetFullPath(Path.Combine(formDirectory, codeFileName));
    }

    private void OpenWorkspace(string path)
    {
        _workspacePath = path;
        try
        {
            _project = YyProject.LoadForDirectory(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _project = null;
            MessageBox.Show(
                $"项目清单读取失败：\n{exception.Message}",
                "项目加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        PopulateProjectTree();
        _settings.AddRecent(path);
        SetStatus(
            _project is null
                ? $"已打开目录：{Path.GetFileName(path)}"
                : $"已打开项目：{_project.Name}",
            ReadyBrush);

        var startupForm = _project?.Forms.FirstOrDefault(form =>
            string.Equals(form.Model.Name, _project.StartupForm, StringComparison.Ordinal));
        var openPath = startupForm?.FormPath
            ?? _project?.EntryPath
            ?? Directory.EnumerateFiles(path, "*.yy*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), ".yyc", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IgnoredDirectories.Any(part =>
                file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains(part, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (openPath is not null)
        {
            OpenDocument(openPath);
        }
    }

    private sealed record TableLink(string Kind);

    private DataTableView? _dataTableView;

    private void PopulateProjectTree()
    {
        ProjectTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(_workspacePath) || !Directory.Exists(_workspacePath))
        {
            return;
        }

        // 官方“程序数据”树(对齐 img041):全局变量/自定义数据类型/Dll命令/窗口/程序集/常量表/资源表
        var root = new TreeViewItem
        {
            Header = MakeTreeHeader("程序数据"),
            IsExpanded = true
        };
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("全局变量"), Tag = new TableLink("全局变量") });
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("自定义数据类型"), Tag = new TableLink("自定义数据类型") });
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("Dll命令"), Tag = new TableLink("Dll命令") });

        var windowsItem = new TreeViewItem
        {
            Header = MakeTreeHeader("窗口"),
            IsExpanded = true
        };
        var assembliesItem = new TreeViewItem
        {
            Header = MakeTreeHeader("程序集"),
            IsExpanded = true
        };

        var formFiles = new List<string>();
        var linkedCodeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_project is not null)
        {
            foreach (var form in _project.Forms)
            {
                formFiles.Add(form.FormPath);
                windowsItem.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader(form.Model.Name),
                    Tag = form.FormPath
                });
                if (form.CodePath is not null)
                {
                    linkedCodeFiles.Add(form.CodePath);
                }
            }
        }
        else
        {
            foreach (var formFile in Directory.EnumerateFiles(_workspacePath, "*.yyx", SearchOption.AllDirectories)
                         .Where(file => !IsIgnoredPath(file))
                         .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                formFiles.Add(formFile);
                var formName = Path.GetFileNameWithoutExtension(formFile);
                try
                {
                    formName = YyFormSerializer.Deserialize(File.ReadAllText(formFile)).Name;
                }
                catch (Exception exception) when (exception is IOException or YyFormFormatException)
                {
                    // 名称回退为文件名,仍允许打开修复。
                }

                windowsItem.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader(formName),
                    Tag = formFile
                });
            }
        }

        var moduleFiles = Directory.EnumerateFiles(_workspacePath, "*.yyc", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredPath(file))
            .Where(file => !linkedCodeFiles.Contains(Path.GetFullPath(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
        foreach (var module in moduleFiles)
        {
            assembliesItem.Items.Add(new TreeViewItem
            {
                Header = MakeTreeHeader(Path.GetFileNameWithoutExtension(module)),
                Tag = module
            });
        }

        root.Items.Add(windowsItem);
        root.Items.Add(assembliesItem);
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("常量表..."), Tag = new TableLink("常量表") });

        var resourceItem = new TreeViewItem { Header = MakeTreeHeader("资源表") };
        resourceItem.Items.Add(new TreeViewItem { Header = MakeTreeHeader("图片或图片组..."), Tag = new TableLink("资源表") });
        resourceItem.Items.Add(new TreeViewItem { Header = MakeTreeHeader("声音..."), Tag = new TableLink("资源表") });
        root.Items.Add(resourceItem);

        ProjectTree.Items.Add(root);
    }

    private static bool IsIgnoredPath(string file) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or ".git" or ".vs");

    private static TextBlock MakeTreeHeader(string text) =>
        new()
        {
            Text = text,
            FontSize = 12
        };

    private void OnProjectTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case TreeViewItem { Tag: TableLink { Kind: "自定义数据类型" } }:
                ShowHint("自定义数据类型将在后续版本提供。");
                break;
            case TreeViewItem { Tag: TableLink link }:
                SetStatus($"数据表：{link.Kind}(双击打开)", ReadyBrush);
                break;
            case TreeViewItem { Tag: string path }:
                SetStatus($"{Path.GetFileName(path)}(双击打开)", ReadyBrush);
                break;
        }
    }

    private void OnProjectTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is not TreeViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case TableLink { Kind: "自定义数据类型" }:
                ShowHint("自定义数据类型将在后续版本提供。");
                break;
            case TableLink link:
                ShowDataTableView(link.Kind);
                break;
            case string path when File.Exists(path):
                OpenDocument(path);
                break;
            default:
                item.IsExpanded = !item.IsExpanded;
                break;
        }
    }

    private void ShowDataTableView(string kind)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目后再编辑数据表。");
            return;
        }

        _dataTableView ??= new DataTableView();
        if (!ReferenceEquals(DataTableViewHost.Content, _dataTableView))
        {
            DataTableViewHost.Content = _dataTableView;
        }

        Editor.Visibility = Visibility.Collapsed;
        StructuredEditor.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Collapsed;
        DataTableViewHost.Visibility = Visibility.Visible;

        _dataTableView.Load(_project, kind, () => _project.SaveManifest());
        SetStatus($"数据表：{kind}", ReadyBrush);
    }

    private void OpenDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".yyc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".yyx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "旧版源码格式不再受支持。\n\n请使用 .yyc 结构化代码、.yyx 窗体或 .yyproj 项目。",
                "不支持的旧格式",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var existing = _documents.FirstOrDefault(document =>
            string.Equals(document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing;
            return;
        }

        try
        {
            var source = File.ReadAllText(
                fullPath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
            var document = new EditorDocument(fullPath, Path.GetFileName(fullPath), source);
            _documents.Add(document);
            DocumentTabs.SelectedItem = document;
            _settings.AddRecent(fullPath);
            SetStatus($"已打开 {Path.GetFileName(fullPath)}", ReadyBrush);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            MessageBox.Show(
                $"无法打开文件：\n{exception.Message}",
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AddNewDocument(string source)
    {
        var displayName = NextUntitledName();
        var content = string.Equals(Path.GetExtension(displayName), ".yyc", StringComparison.OrdinalIgnoreCase)
            ? YyCodeSerializer.Serialize(YyCodeDocumentConverter.FromSource(source))
            : source;
        var document = new EditorDocument(null, displayName, content);
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
    }

    /// <summary>添加起始页标签(对齐官方:启动/关闭全部文档后先到起始页)。</summary>
    private void AddStartPage()
    {
        if (_documents.Any(document => document.IsStartPage))
        {
            DocumentTabs.SelectedItem = _documents.First(document => document.IsStartPage);
            return;
        }

        var document = new EditorDocument(null, "起始页", string.Empty) { IsStartPage = true };
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
    }

    private void ShowStartPage()
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        StructuredEditor.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Collapsed;
        _startPageView.Visibility = Visibility.Visible;
        RestoreInspectorColumn();
        PropertyRows.Visibility = Visibility.Collapsed;
        _designForm = null;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        ClearSelectionVisuals();
        RefreshStartPageRecent();
    }

    private void RefreshStartPageRecent() =>
        _startPageView.SetRecent(_settings.RecentFiles);

    private string NextUntitledName()
    {
        if (_documents.All(document => document.DisplayName != "未命名.yyc"))
        {
            return "未命名.yyc";
        }

        var index = 2;
        while (_documents.Any(document => document.DisplayName == $"未命名 {index}.yyc"))
        {
            index++;
        }

        return $"未命名 {index}.yyc";
    }

    private string NextUntitledFormName()
    {
        if (_documents.All(document => document.DisplayName != "未命名窗体.yyx"))
        {
            return "未命名窗体.yyx";
        }

        var index = 2;
        while (_documents.Any(document => document.DisplayName == $"未命名窗体 {index}.yyx"))
        {
            index++;
        }

        return $"未命名窗体 {index}.yyx";
    }

    private void ActivateDocument(EditorDocument document)
    {
        if (_currentDocument is not null && !_currentDocument.IsForm)
        {
            _currentDocument.CaretOffset = Editor.CaretOffset;
            _currentDocument.VerticalOffset = Editor.VerticalOffset;
        }

        _currentDocument = document;
        _helpFunctionsSource = null;
        _helpFunctions.Clear();
        if (document.IsStartPage)
        {
            ShowStartPage();
            UpdateDocumentIdentity();
            return;
        }

        if (document.IsForm)
        {
            ShowFormDesigner(document);
            UpdateDocumentIdentity();
            return;
        }

        if (document.IsStructuredCode)
        {
            ShowStructuredCode(document);
            UpdateDocumentIdentity();
            return;
        }

        ShowCodeEditor(document);
        _isLoadingDocument = true;
        try
        {
            Editor.Text = document.Text;
            Editor.CaretOffset = Math.Clamp(document.CaretOffset, 0, Editor.Document.TextLength);
            Editor.ScrollToVerticalOffset(document.VerticalOffset);
        }
        finally
        {
            _isLoadingDocument = false;
        }

        UpdateDocumentIdentity();
        CaretText.Text = $"行 {Editor.TextArea.Caret.Line}，列 {Editor.TextArea.Caret.Column}";
    }

    private void ShowCodeEditor(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        StructuredEditor.Visibility = Visibility.Collapsed;
        _startPageView.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        ParameterHintBar.Visibility = Visibility.Visible;
        RestoreInspectorColumn();
        PropertyRows.Visibility = Visibility.Collapsed;
        _designForm = null;
        _designerDocument = null;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        ClearSelectionVisuals();
    }

    private void ShowFormDesigner(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        try
        {
            _designForm = YyFormSerializer.Deserialize(document.Text);
            _designerDocument = document;
            var currentSnapshot = YyFormSerializer.Serialize(_designForm);
            if (document.LastDesignerSnapshot is null ||
                !string.Equals(document.LastDesignerSnapshot, currentSnapshot, StringComparison.Ordinal))
            {
                document.DesignerUndo.Clear();
                document.DesignerRedo.Clear();
                document.LastDesignerSnapshot = currentSnapshot;
            }

            Editor.Visibility = Visibility.Collapsed;
            StructuredEditor.Visibility = Visibility.Collapsed;
            _startPageView.Visibility = Visibility.Collapsed;
            DesignerPanel.Visibility = Visibility.Visible;
            ParameterHintBar.Visibility = Visibility.Collapsed;
            RestoreInspectorColumn();
            PropertyRows.Visibility = Visibility.Visible;
            RenderDesignerForm();
            UpdateDesignerHistoryButtons();
            UpdateDesignerTitleChrome();
            SetStatus($"窗体设计：{_designForm.Title}", ReadyBrush);
        }
        catch (YyFormFormatException exception)
        {
            ShowCodeEditor(document);
            _isLoadingDocument = true;
            try
            {
                Editor.Text = document.Text;
                NavigateTo(exception.Line, exception.Column);
            }
            finally
            {
                _isLoadingDocument = false;
            }

            AppendOutput(string.Empty);
            AppendOutput($"✕ 窗体 XML 错误 · 第 {exception.Line} 行，第 {exception.Column} 列");
            AppendOutput($"  {exception.Message}");
            SetStatus("窗体 XML 无法解析，已切换到文本编辑模式", ErrorBrush);
        }
    }

    private void ShowStructuredCode(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        try
        {
            var model = YyCodeSerializer.Deserialize(document.Text);
            Editor.Visibility = Visibility.Collapsed;
            DesignerPanel.Visibility = Visibility.Collapsed;
            _startPageView.Visibility = Visibility.Collapsed;
            StructuredEditor.Visibility = Visibility.Visible;
            ParameterHintBar.Visibility = Visibility.Collapsed;
            HideInspectorColumn();
            StructuredEditor.SetDesignContext(GetAssociatedFormForCurrentDocument());
            StructuredEditor.SetEventAssociations(BuildEventAssociations());
            StructuredEditor.LoadDocument(model);
            SetStatus($"结构化代码：{document.DisplayName}", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is YyCodeFormatException or YyDiagnostic or IOException or UnauthorizedAccessException)
        {
            ShowCodeEditor(document);
            _isLoadingDocument = true;
            try
            {
                Editor.Text = document.Text;
            }
            finally
            {
                _isLoadingDocument = false;
            }

            var line = exception switch
            {
                YyCodeFormatException codeError => codeError.Line,
                YyDiagnostic diagnostic => diagnostic.Line,
                _ => 1
            };
            var column = exception switch
            {
                YyCodeFormatException codeError => codeError.Column,
                YyDiagnostic diagnostic => diagnostic.Column,
                _ => 1
            };
            NavigateTo(line, column);
            AppendOutput($"✕ 结构化代码解析失败 · {exception.Message}");
            BottomTabs.SelectedIndex = 0;
            SetStatus("代码文件无法结构化解析，已切换为文本修复模式", ErrorBrush);
        }
    }

    private void OnStructuredDesignerActivationRequested(
        object? sender,
        DesignerActivationEventArgs e)
    {
        var formPath = GetAssociatedFormPathForCurrentDocument();
        if (formPath is null)
        {
            MessageBox.Show(
                "当前代码没有关联窗体。请在项目内打开代码后置，或放置同名的 .yyx 窗体文件。",
                "关联窗体",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenDocument(formPath);
        if (!string.IsNullOrWhiteSpace(e.ControlName) && _designForm is not null)
        {
            var control = _designForm.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, e.ControlName, StringComparison.Ordinal));
            if (control is not null)
            {
                SelectDesignerControl(control);
                SetStatus($"已定位控件：{control.Name}", ReadyBrush);
                return;
            }
        }

        SetStatus($"已打开关联窗体：{_designForm?.Name ?? Path.GetFileName(formPath)}", ReadyBrush);
    }

    private void HideInspectorColumn()
    {
        // 官方布局:窗口组件箱常驻右侧,不随文档视图隐藏。
    }

    private void RestoreInspectorColumn()
    {
        // 官方布局:窗口组件箱常驻右侧,不随文档视图隐藏。
    }

    private void OnStructuredCodeChanged(object? sender, EventArgs e)
    {
        if (_currentDocument?.IsStructuredCode != true || StructuredEditor.Document is null)
        {
            return;
        }

        _currentDocument.Text = YyCodeSerializer.Serialize(StructuredEditor.Document);
        UpdateDocumentIdentity();
    }

    private void RenderDesignerForm()
    {
        if (_designForm is null)
        {
            return;
        }

        DesignerCanvas.Children.Clear();
        _selectionVisuals.Clear();
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        DesignerCanvas.Width = _designForm.Width;
        DesignerCanvas.Height = _designForm.Height;
        DesignerCanvas.Background = CreateDesignerBackground(_designForm.Background);
        DesignerTitleText.Text = string.IsNullOrWhiteSpace(_designForm.Title)
            ? _designForm.Name
            : _designForm.Title;

        foreach (var control in _designForm.Controls)
        {
            AddControlVisual(control);
        }

        PopulateDesignerProperties();
    }

    private Brush CreateDesignerBackground(string color)
    {
        var baseColor = YyFormRenderer.Brush(color) is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(236, 233, 216);

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(baseColor),
            null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        // 官方式点阵:8px 网格交点画 1px 灰点
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private void AddControlVisual(YyControlModel model)
    {
        var element = YyFormRenderer.Create(model);
        element.Visibility = Visibility.Visible;
        element.Opacity = model.IsVisible ? 1 : 0.35;
        Canvas.SetLeft(element, model.X);
        Canvas.SetTop(element, model.Y);
        Panel.SetZIndex(element, _designForm?.Controls.IndexOf(model) ?? 0);
        DesignerCanvas.Children.Add(element);
    }

    private void OnToolboxMouseDown(object sender, MouseButtonEventArgs e)
    {
        _toolboxDragStart = e.GetPosition(ControlToolbox);
    }

    private void OnToolboxMouseMove(object sender, MouseEventArgs e)
    {
        if (_toolboxDragging ||
            e.LeftButton != MouseButtonState.Pressed ||
            ControlToolbox.SelectedItem is not ToolboxItem { Supported: true, Type: { } type })
        {
            return;
        }

        var current = e.GetPosition(ControlToolbox);
        if (Math.Abs(current.X - _toolboxDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _toolboxDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _toolboxDragging = true;
        try
        {
            System.Windows.DragDrop.DoDragDrop(ControlToolbox, type, DragDropEffects.Copy);
        }
        finally
        {
            _toolboxDragging = false;
        }
    }

    private void OnToolboxDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ControlToolbox.SelectedItem is ToolboxItem { Supported: true, Type: { } type } &&
            _designForm is not null)
        {
            AddDesignerControl(type, new Point(44 + _designForm.Controls.Count % 6 * 22, 44));
            e.Handled = true;
        }
        else if (ControlToolbox.SelectedItem is ToolboxItem { Supported: false } item)
        {
            ShowHint($"组件“{item.DisplayName}”暂未支持，后续版本提供。");
        }
    }

    private void OnDesignerCanvasDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string type &&
            YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDesignerCanvasDrop(object sender, DragEventArgs e)
    {
        if (_designForm is null || e.Data.GetData(typeof(string)) is not string type)
        {
            return;
        }

        AddDesignerControl(type, e.GetPosition(DesignerCanvas));
        e.Handled = true;
    }

    private void AddDesignerControl(string type, Point position)
    {
        if (_designForm is null)
        {
            return;
        }

        var sequence = 1;
        while (_designForm.Controls.Any(control =>
                   control.Name == GetControlBaseName(type) + sequence))
        {
            sequence++;
        }

        var control = YyControlTypes.Create(type, sequence);
        control.Name = GetControlBaseName(type) + sequence;
        control.X = Clamp(Snap(position.X), 0, Math.Max(0, _designForm.Width - control.Width));
        control.Y = Clamp(Snap(position.Y), 0, Math.Max(0, _designForm.Height - control.Height));
        _designForm.Controls.Add(control);
        AddControlVisual(control);
        SelectDesignerControl(control);
        DesignerChanged();
        SetStatus($"已添加{type}“{control.Name}”", ReadyBrush);
    }

    private static string GetControlBaseName(string type) =>
        YyControlTypes.GetBaseName(type);

    private static bool TryGetDefaultEvent(string controlType, out ControlEventKind eventKind)
    {
        eventKind = controlType switch
        {
            YyControlTypes.Button => ControlEventKind.Click,
            YyControlTypes.TextBox => ControlEventKind.TextChanged,
            YyControlTypes.ComboBox or YyControlTypes.ListBox => ControlEventKind.SelectionChanged,
            YyControlTypes.CheckBox or YyControlTypes.RadioButton => ControlEventKind.CheckedChanged,
            _ => default
        };

        return controlType is YyControlTypes.Button or YyControlTypes.TextBox
            or YyControlTypes.ComboBox or YyControlTypes.ListBox or YyControlTypes.CheckBox;
    }

    private void OnDesignerCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_designForm is null)
        {
            return;
        }

        var resizeTag = FindAncestorTag<ResizeHandleTag>(e.OriginalSource as DependencyObject);
        if (resizeTag is not null && _selectedControl is not null && _selectedControls.Count == 1)
        {
            _resizeDirection = resizeTag.Direction;
            _designerDragMode = DesignerDragMode.Resize;
            _designerDragStart = e.GetPosition(DesignerCanvas);
            _designerOriginalBounds = GetControlBounds(_selectedControl);
            _designerDragChanged = false;
            DesignerCanvas.CaptureMouse();
            DesignerCanvas.Focus();
            e.Handled = true;
            return;
        }

        var control = FindAncestorTag<YyControlModel>(e.OriginalSource as DependencyObject);
        if (control is not null)
        {
            if (e.ClickCount == 2 && TryGetDefaultEvent(control.Type, out var eventKind))
            {
                SelectDesignerControl(control);
                CreateOrOpenControlEventHandler(control, eventKind);
                e.Handled = true;
                return;
            }

            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (additive)
            {
                SelectDesignerControl(control, additive: true);
                e.Handled = true;
                return;
            }

            if (!_selectedControls.Contains(control))
            {
                SelectDesignerControl(control);
            }
            else if (_selectedControls.Count > 1)
            {
                _selectedControl = control;
                _selectedElement = FindControlElement(control);
                PopulateDesignerProperties();
                UpdateSelectionVisuals();
            }

            _designerDragMode = DesignerDragMode.Move;
            _designerDragStart = e.GetPosition(DesignerCanvas);
            _designerOriginalBounds = GetControlBounds(control);
            _designerDragBounds.Clear();
            foreach (var selected in _selectedControls)
            {
                _designerDragBounds[selected] = GetControlBounds(selected);
            }

            _designerDragChanged = false;
            DesignerCanvas.CaptureMouse();
            DesignerCanvas.Focus();
        }
        else
        {
            SelectDesignerForm();
            DesignerCanvas.Focus();
        }

        e.Handled = true;
    }

    private T? FindAncestorTag<T>(DependencyObject? source) where T : class
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, DesignerCanvas))
        {
            if (current is FrameworkElement { Tag: T value })
            {
                return value;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Rect GetControlBounds(YyControlModel control) =>
        new(control.X, control.Y, control.Width, control.Height);

    private void OnDesignerCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None ||
            _designForm is null ||
            _selectedControl is null ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(DesignerCanvas);
        var deltaX = current.X - _designerDragStart.X;
        var deltaY = current.Y - _designerDragStart.Y;

        if (_designerDragMode == DesignerDragMode.Move)
        {
            var primaryBounds = _designerOriginalBounds;
            var roundedDeltaX = Snap(primaryBounds.X + deltaX) - primaryBounds.X;
            var roundedDeltaY = Snap(primaryBounds.Y + deltaY) - primaryBounds.Y;
            var groupLeft = _designerDragBounds.Values.Min(bounds => bounds.Left);
            var groupTop = _designerDragBounds.Values.Min(bounds => bounds.Top);
            var groupRight = _designerDragBounds.Values.Max(bounds => bounds.Right);
            var groupBottom = _designerDragBounds.Values.Max(bounds => bounds.Bottom);
            var appliedDeltaX = Clamp(roundedDeltaX, -groupLeft, _designForm.Width - groupRight);
            var appliedDeltaY = Clamp(roundedDeltaY, -groupTop, _designForm.Height - groupBottom);

            foreach (var selected in _selectedControls)
            {
                if (!_designerDragBounds.TryGetValue(selected, out var original))
                {
                    continue;
                }

                selected.X = original.X + appliedDeltaX;
                selected.Y = original.Y + appliedDeltaY;
            }
        }
        else
        {
            ApplyResize(deltaX, deltaY);
        }

        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        _designerDragChanged = true;
    }

    private void ApplyResize(double deltaX, double deltaY)
    {
        if (_selectedControl is null || _designForm is null)
        {
            return;
        }

        const double minimumWidth = 24;
        const double minimumHeight = 20;
        var left = _designerOriginalBounds.Left;
        var top = _designerOriginalBounds.Top;
        var right = _designerOriginalBounds.Right;
        var bottom = _designerOriginalBounds.Bottom;

        if (_resizeDirection is ResizeDirection.West or ResizeDirection.NorthWest or ResizeDirection.SouthWest)
        {
            left = Clamp(Snap(_designerOriginalBounds.Left + deltaX), 0, right - minimumWidth);
        }

        if (_resizeDirection is ResizeDirection.East or ResizeDirection.NorthEast or ResizeDirection.SouthEast)
        {
            right = Clamp(
                Snap(_designerOriginalBounds.Right + deltaX),
                left + minimumWidth,
                _designForm.Width);
        }

        if (_resizeDirection is ResizeDirection.North or ResizeDirection.NorthWest or ResizeDirection.NorthEast)
        {
            top = Clamp(Snap(_designerOriginalBounds.Top + deltaY), 0, bottom - minimumHeight);
        }

        if (_resizeDirection is ResizeDirection.South or ResizeDirection.SouthWest or ResizeDirection.SouthEast)
        {
            bottom = Clamp(
                Snap(_designerOriginalBounds.Bottom + deltaY),
                top + minimumHeight,
                _designForm.Height);
        }

        _selectedControl.X = left;
        _selectedControl.Y = top;
        _selectedControl.Width = right - left;
        _selectedControl.Height = bottom - top;
    }

    private double Snap(double value) =>
        SnapCheckBox.IsChecked == true ? Math.Round(value / 8) * 8 : value;

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private void OnDesignerCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_designerDragMode == DesignerDragMode.None)
        {
            return;
        }

        DesignerCanvas.ReleaseMouseCapture();
        _designerDragMode = DesignerDragMode.None;
        if (_designerDragChanged)
        {
            DesignerChanged();
            SetStatus("窗体布局已更新", ReadyBrush);
        }

        _designerDragChanged = false;
        _designerDragBounds.Clear();
        e.Handled = true;
    }

    private void OnDesignerCanvasKeyDown(object sender, KeyEventArgs e)
    {
        if (_designForm is null)
        {
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetDesignerSelection(_designForm.Controls);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SelectDesignerForm();
            e.Handled = true;
            return;
        }

        if (_selectedControls.Count == 0)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedControls();
            e.Handled = true;
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var deltaX = 0d;
        var deltaY = 0d;
        switch (e.Key)
        {
            case Key.Left:
                deltaX = -step;
                break;
            case Key.Right:
                deltaX = step;
                break;
            case Key.Up:
                deltaY = -step;
                break;
            case Key.Down:
                deltaY = step;
                break;
            default:
                return;
        }

        MoveSelectedControls(deltaX, deltaY);
        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        DesignerChanged();
        e.Handled = true;
    }

    private void MoveSelectedControls(double deltaX, double deltaY)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        var left = _selectedControls.Min(control => control.X);
        var top = _selectedControls.Min(control => control.Y);
        var right = _selectedControls.Max(control => control.X + control.Width);
        var bottom = _selectedControls.Max(control => control.Y + control.Height);
        var appliedX = Clamp(deltaX, -left, _designForm.Width - right);
        var appliedY = Clamp(deltaY, -top, _designForm.Height - bottom);
        foreach (var control in _selectedControls)
        {
            control.X += appliedX;
            control.Y += appliedY;
        }
    }

    private void OnDesignerAlignMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnAlignDesignerControls(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string action } ||
            _designForm is null ||
            _selectedControls.Count < 2)
        {
            SetStatus("对齐至少需要选择两个控件（Ctrl+单击多选）", WarningBrush);
            return;
        }

        var left = _selectedControls.Min(control => control.X);
        var top = _selectedControls.Min(control => control.Y);
        var right = _selectedControls.Max(control => control.X + control.Width);
        var bottom = _selectedControls.Max(control => control.Y + control.Height);
        switch (action)
        {
            case "Left":
                foreach (var control in _selectedControls)
                {
                    control.X = left;
                }

                break;
            case "CenterX":
                var centerX = (left + right) / 2;
                foreach (var control in _selectedControls)
                {
                    control.X = centerX - control.Width / 2;
                }

                break;
            case "Right":
                foreach (var control in _selectedControls)
                {
                    control.X = right - control.Width;
                }

                break;
            case "Top":
                foreach (var control in _selectedControls)
                {
                    control.Y = top;
                }

                break;
            case "CenterY":
                var centerY = (top + bottom) / 2;
                foreach (var control in _selectedControls)
                {
                    control.Y = centerY - control.Height / 2;
                }

                break;
            case "Bottom":
                foreach (var control in _selectedControls)
                {
                    control.Y = bottom - control.Height;
                }

                break;
            case "DistributeX":
                if (_selectedControls.Count < 3)
                {
                    SetStatus("水平等距分布至少需要三个控件", WarningBrush);
                    return;
                }

                DistributeControls(horizontal: true);
                break;
            case "DistributeY":
                if (_selectedControls.Count < 3)
                {
                    SetStatus("垂直等距分布至少需要三个控件", WarningBrush);
                    return;
                }

                DistributeControls(horizontal: false);
                break;
            default:
                return;
        }

        UpdateSelectedControlVisual();
        PopulateDesignerProperties();
        DesignerChanged();
        SetStatus($"已对齐 {_selectedControls.Count} 个控件", ReadyBrush);
    }

    private void DistributeControls(bool horizontal)
    {
        var ordered = _selectedControls
            .OrderBy(control => horizontal ? control.X : control.Y)
            .ToArray();
        var start = horizontal ? ordered[0].X : ordered[0].Y;
        var end = horizontal
            ? ordered[^1].X + ordered[^1].Width
            : ordered[^1].Y + ordered[^1].Height;
        var totalSize = ordered.Sum(control => horizontal ? control.Width : control.Height);
        var gap = (end - start - totalSize) / (ordered.Length - 1);
        if (gap < 0)
        {
            SetStatus("控件总宽度或高度过大，无法等距分布", WarningBrush);
            return;
        }

        var cursor = start;
        foreach (var control in ordered)
        {
            if (horizontal)
            {
                control.X = cursor;
                cursor += control.Width + gap;
            }
            else
            {
                control.Y = cursor;
                cursor += control.Height + gap;
            }
        }
    }

    private void OnDesignerCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The designer canvas size is driven by the active form model.
    }

    /// <summary>设计器标题栏随窗体属性联动(最小化/最大化按钮与边框样式)。</summary>
    private void UpdateDesignerTitleChrome()
    {
        if (_designForm is null)
        {
            return;
        }

        var hasChrome = _designForm.BorderStyle != "无边框";
        DesignerMinGlyph.Visibility = hasChrome && _designForm.MinimizeBox
            ? Visibility.Visible
            : Visibility.Collapsed;
        DesignerMaxGlyph.Visibility = hasChrome && _designForm.MaximizeBox
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // =============== 设计器控件剪贴板(对齐官方 Ctrl+X/C/V) ===============

    private void OnDesignerCopy(object sender, ExecutedRoutedEventArgs e) =>
        CopySelectedControls(cut: false);

    private void OnDesignerCut(object sender, ExecutedRoutedEventArgs e) =>
        CopySelectedControls(cut: true);

    private void OnDesignerPaste(object sender, ExecutedRoutedEventArgs e) =>
        PasteDesignerControls();

    private void CopySelectedControls(bool cut)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        _designerClipboard = _selectedControls.Select(control => control.Clone()).ToList();
        if (cut)
        {
            DeleteSelectedControls();
            SetStatus($"已剪切 {_designerClipboard.Count} 个控件", ReadyBrush);
        }
        else
        {
            SetStatus($"已复制 {_designerClipboard.Count} 个控件", ReadyBrush);
        }
    }

    private void PasteDesignerControls()
    {
        if (_designForm is null || _designerClipboard.Count == 0)
        {
            return;
        }

        var pasted = new List<YyControlModel>();
        foreach (var prototype in _designerClipboard)
        {
            var model = prototype.Clone();
            var sequence = 1;
            while (_designForm.Controls.Any(control =>
                       control.Name == GetControlBaseName(model.Type) + sequence))
            {
                sequence++;
            }

            model.Name = GetControlBaseName(model.Type) + sequence;
            model.ClickEvent = null;
            model.TextChangedEvent = null;
            model.SelectionChangedEvent = null;
            model.CheckedChangedEvent = null;
            model.X = Clamp(model.X + 12, 0, Math.Max(0, _designForm.Width - model.Width));
            model.Y = Clamp(model.Y + 12, 0, Math.Max(0, _designForm.Height - model.Height));
            _designForm.Controls.Add(model);
            AddControlVisual(model);
            pasted.Add(model);
        }

        SetDesignerSelection(pasted);
        DesignerChanged();
        SetStatus($"已粘贴 {pasted.Count} 个控件", ReadyBrush);
    }

    private void OnSelectAllDesignerControls(object sender, RoutedEventArgs e)
    {
        if (_designForm is not null)
        {
            SetDesignerSelection(_designForm.Controls);
        }
    }

    private void OnDesignerBringFront(object sender, RoutedEventArgs e) =>
        ReorderSelectedControls(toFront: true);

    private void OnDesignerSendBack(object sender, RoutedEventArgs e) =>
        ReorderSelectedControls(toFront: false);

    private void ReorderSelectedControls(bool toFront)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        var selected = _designForm.Controls.Where(_selectedControls.Contains).ToList();
        foreach (var control in selected)
        {
            _designForm.Controls.Remove(control);
        }

        if (toFront)
        {
            _designForm.Controls.AddRange(selected);
        }
        else
        {
            _designForm.Controls.InsertRange(0, selected);
        }

        RenderDesignerForm();
        UpdateDesignerHistoryButtons();
        UpdateDesignerTitleChrome();
        DesignerChanged();
        SetStatus(toFront ? "已置前" : "已置后", ReadyBrush);
    }

    private void SelectDesignerControl(YyControlModel control, bool additive = false)
    {
        if (additive)
        {
            if (!_selectedControls.Remove(control))
            {
                _selectedControls.Add(control);
            }
        }
        else
        {
            _selectedControls.Clear();
            _selectedControls.Add(control);
        }

        if (_selectedControls.Count == 0)
        {
            SelectDesignerForm();
            return;
        }

        _selectedControl = _selectedControls.Contains(control)
            ? control
            : _selectedControls[^1];
        _selectedElement = FindControlElement(_selectedControl);
        PopulateDesignerProperties();
        UpdateSelectionVisuals();
        DesignerCanvas.Focus();
        SetStatus(
            _selectedControls.Count == 1
                ? $"已选择控件“{_selectedControl.Name}”"
                : $"已选择 {_selectedControls.Count} 个控件",
            ReadyBrush);
    }

    private void SetDesignerSelection(IEnumerable<YyControlModel> controls)
    {
        _selectedControls.Clear();
        _selectedControls.AddRange(controls.Distinct());
        if (_selectedControls.Count == 0)
        {
            SelectDesignerForm();
            return;
        }

        _selectedControl = _selectedControls[^1];
        _selectedElement = FindControlElement(_selectedControl);
        PopulateDesignerProperties();
        UpdateSelectionVisuals();
        SetStatus($"已选择 {_selectedControls.Count} 个控件", ReadyBrush);
    }

    private FrameworkElement? FindControlElement(YyControlModel control)
    {
        return DesignerCanvas.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => ReferenceEquals(element.Tag, control));
    }

    private void SelectDesignerForm()
    {
        StudioTrace.Write("DBG select-designer-form");
        _selectedControls.Clear();
        _selectedControl = null;
        _selectedElement = null;
        ClearSelectionVisuals();
        PopulateDesignerProperties();
        ShowHint("窗口用来设计程序的用户界面及对界面事件进行处理。");
    }

    private void UpdateSelectedControlVisual()
    {
        if (_selectedControls.Count == 0)
        {
            return;
        }

        foreach (var control in _selectedControls)
        {
            var element = FindControlElement(control);
            if (element is null)
            {
                continue;
            }

            element.Width = control.Width;
            element.Height = control.Height;
            Canvas.SetLeft(element, control.X);
            Canvas.SetTop(element, control.Y);
        }

        UpdateSelectionVisuals();
    }

    private void RefreshSelectedControlElement()
    {
        if (_selectedControl is null)
        {
            return;
        }

        if (_selectedElement is not null)
        {
            DesignerCanvas.Children.Remove(_selectedElement);
        }

        AddControlVisual(_selectedControl);
        _selectedElement = DesignerCanvas.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => ReferenceEquals(element.Tag, _selectedControl));
        UpdateSelectionVisuals();
    }

    private void ClearSelectionVisuals()
    {
        foreach (var visual in _selectionVisuals)
        {
            DesignerCanvas.Children.Remove(visual);
        }

        _selectionVisuals.Clear();
    }

    private void UpdateSelectionVisuals()
    {
        ClearSelectionVisuals();
        if (_selectedControls.Count == 0)
        {
            return;
        }

        foreach (var control in _selectedControls)
        {
            var outline = new Border
            {
                Width = control.Width,
                Height = control.Height,
                BorderBrush = YyFormRenderer.Brush(
                    ReferenceEquals(control, _selectedControl) ? "#000000" : "#808080"),
                BorderThickness = new Thickness(1.5),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, control.X);
            Canvas.SetTop(outline, control.Y);
            Panel.SetZIndex(outline, 1000);
            DesignerCanvas.Children.Add(outline);
            _selectionVisuals.Add(outline);
        }

        if (_selectedControl is null || _selectedControls.Count != 1)
        {
            return;
        }

        foreach (var direction in Enum.GetValues<ResizeDirection>())
        {
            var handle = new Border
            {
                Width = 8,
                Height = 8,
                Background = YyFormRenderer.Brush("#000000"),
                BorderBrush = YyFormRenderer.Brush("#FFFFFF"),
                BorderThickness = new Thickness(1),
                Tag = new ResizeHandleTag(direction)
            };
            var point = GetResizeHandlePoint(direction);
            Canvas.SetLeft(handle, point.X - 4);
            Canvas.SetTop(handle, point.Y - 4);
            Panel.SetZIndex(handle, 1001);
            DesignerCanvas.Children.Add(handle);
            _selectionVisuals.Add(handle);
        }
    }

    private Point GetResizeHandlePoint(ResizeDirection direction)
    {
        if (_selectedControl is null)
        {
            return new Point();
        }

        var left = _selectedControl.X;
        var top = _selectedControl.Y;
        var centerX = left + _selectedControl.Width / 2;
        var centerY = top + _selectedControl.Height / 2;
        var right = left + _selectedControl.Width;
        var bottom = top + _selectedControl.Height;
        return direction switch
        {
            ResizeDirection.NorthWest => new Point(left, top),
            ResizeDirection.North => new Point(centerX, top),
            ResizeDirection.NorthEast => new Point(right, top),
            ResizeDirection.East => new Point(right, centerY),
            ResizeDirection.SouthEast => new Point(right, bottom),
            ResizeDirection.South => new Point(centerX, bottom),
            ResizeDirection.SouthWest => new Point(left, bottom),
            ResizeDirection.West => new Point(left, centerY),
            _ => new Point()
        };
    }

    private void DesignerChanged()
    {
        // 快照永远写回窗体自己的文档:创建事件后当前文档可能已是 .yyc 代码,
        // 若盲目写 _currentDocument,窗体 XML 会覆盖代码文档(根元素校验失败)。
        var target = _designerDocument ?? _currentDocument;
        if (_designForm is null || target is null)
        {
            return;
        }

        var snapshot = YyFormSerializer.Serialize(_designForm);
        var previous = target.LastDesignerSnapshot;
        if (previous is not null && !string.Equals(previous, snapshot, StringComparison.Ordinal))
        {
            target.DesignerUndo.Push(previous);
            target.DesignerRedo.Clear();
        }

        target.LastDesignerSnapshot = snapshot;
        target.Text = snapshot;
        UpdateDesignerHistoryButtons();
        UpdateDocumentIdentity();
    }

    private void OnDesignerUndoClick(object sender, RoutedEventArgs e) => UndoDesigner();

    private void OnDesignerRedoClick(object sender, RoutedEventArgs e) => RedoDesigner();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            BottomTabs.SelectedIndex = 0;
            UpdateEditorHelp();
            e.Handled = true;
            return;
        }

        // 官方快捷键:Ctrl+E 菜单编辑器、Ctrl+L 插入局部变量、Ctrl+Enter 预览被设计窗口、Alt+1..4 面板
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.E)
        {
            OnMenuEditorClick(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
        {
            OnInsertLocalVariableClick(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Enter)
        {
            PreviewCurrentForm();
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && key is Key.D1 or Key.D2 or Key.D3 or Key.D4)
        {
            switch (key)
            {
                case Key.D1:
                    ExplorerPanel.Visibility = Visibility.Visible;
                    WorkTabs.SelectedIndex = 1;
                    break;
                case Key.D2:
                    ExplorerPanel.Visibility = Visibility.Visible;
                    WorkTabs.SelectedIndex = 0;
                    break;
                case Key.D3:
                    OnToggleExplorer(sender, e);
                    break;
                case Key.D4:
                    OnToggleToolboxPanel(sender, e);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (_currentDocument?.IsForm != true ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            e.Key is not (Key.Z or Key.Y))
        {
            return;
        }

        if (e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            RedoDesigner();
        }
        else
        {
            UndoDesigner();
        }

        e.Handled = true;
    }

    private void UndoDesigner()
    {
        if (_currentDocument is null || _currentDocument.DesignerUndo.Count == 0)
        {
            return;
        }

        var document = _designerDocument ?? _currentDocument!;
        var current = document.LastDesignerSnapshot;
        var undoTarget = document.DesignerUndo.Pop();
        if (current is not null)
        {
            document.DesignerRedo.Push(current);
        }

        RestoreDesignerSnapshot(undoTarget);
        SetStatus("已撤销窗体设计操作", ReadyBrush);
    }

    private void RedoDesigner()
    {
        if (_currentDocument is null || _currentDocument.DesignerRedo.Count == 0)
        {
            return;
        }

        var document = _designerDocument ?? _currentDocument!;
        var current = document.LastDesignerSnapshot;
        var redoTarget = document.DesignerRedo.Pop();
        if (current is not null)
        {
            document.DesignerUndo.Push(current);
        }

        RestoreDesignerSnapshot(redoTarget);
        SetStatus("已重做窗体设计操作", ReadyBrush);
    }

    private void RestoreDesignerSnapshot(string snapshot)
    {
        var document = _designerDocument ?? _currentDocument;
        if (document is null)
        {
            return;
        }

        var selectedName = _selectedControl?.Name;
        _designForm = YyFormSerializer.Deserialize(snapshot);
        document.LastDesignerSnapshot = snapshot;
        document.Text = snapshot;
        RenderDesignerForm();
        var selected = string.IsNullOrWhiteSpace(selectedName)
            ? null
            : _designForm.Controls.FirstOrDefault(control =>
                string.Equals(control.Name, selectedName, StringComparison.Ordinal));
        if (selected is not null)
        {
            SelectDesignerControl(selected);
        }

        UpdateDesignerHistoryButtons();
        UpdateDocumentIdentity();
    }

    private void UpdateDesignerHistoryButtons()
    {
        DesignerUndoButton.IsEnabled = _currentDocument?.DesignerUndo.Count > 0;
        DesignerRedoButton.IsEnabled = _currentDocument?.DesignerRedo.Count > 0;
    }

    private void PopulateDesignerProperties()
    {
        StudioTrace.Write($"DBG populate-props guard={_updatingDesignerProperties}");
        if (_designForm is null)
        {
            return;
        }

        _updatingDesignerProperties = true;
        _suppressPropertyCommit = true;
        try
        {
            BuildPropertyObjectCombo();

            if (_selectedControls.Count > 1)
            {
                PropertyRows.ItemsSource = Array.Empty<PropertyRow>();
                ResetEventSelectorPlaceholder();
                EventSelectorCombo.IsEnabled = false;
                return;
            }

            var rows = new List<PropertyRow>();
            if (_selectedControl is null)
            {
                rows.Add(new PropertyRow("名称", _designForm.Name, PropertyCellKind.Identifier, PropertyRowCatalog.HintName));
                rows.Add(new PropertyRow("备注", _designForm.Remark, PropertyCellKind.Text, PropertyRowCatalog.HintRemark));
                rows.Add(new PropertyRow("标题", _designForm.Title, PropertyCellKind.Text, PropertyRowCatalog.HintTitle));
                rows.Add(new PropertyRow("宽度", PropertyRowCatalog.FormatNumber(_designForm.Width), PropertyCellKind.Integer, PropertyRowCatalog.HintWidth));
                rows.Add(new PropertyRow("高度", PropertyRowCatalog.FormatNumber(_designForm.Height), PropertyCellKind.Integer, PropertyRowCatalog.HintHeight));
                rows.Add(new PropertyRow("背景", _designForm.Background, PropertyCellKind.Color, PropertyRowCatalog.HintBackground));
                rows.Add(new PropertyRow("边框", _designForm.BorderStyle, PropertyCellKind.Text, PropertyRowCatalog.HintBorderStyle));
                rows.Add(new PropertyRow("可否移动", PropertyRowCatalog.BoolText(_designForm.Movable), PropertyCellKind.Bool, PropertyRowCatalog.HintMovable));
                rows.Add(new PropertyRow("初始位置", _designForm.StartPosition, PropertyCellKind.Text, PropertyRowCatalog.HintStartPosition));
                rows.Add(new PropertyRow("最小化按钮", PropertyRowCatalog.BoolText(_designForm.MinimizeBox), PropertyCellKind.Bool, PropertyRowCatalog.HintMinBox));
                rows.Add(new PropertyRow("最大化按钮", PropertyRowCatalog.BoolText(_designForm.MaximizeBox), PropertyCellKind.Bool, PropertyRowCatalog.HintMaxBox));
                PropertyRows.ItemsSource = rows;
                BuildFormEventSelector();
            }
            else
            {
                var control = _selectedControl;
                rows.Add(new PropertyRow("名称", control.Name, PropertyCellKind.Identifier, PropertyRowCatalog.HintName));
                rows.Add(new PropertyRow("备注", control.Remark, PropertyCellKind.Text, PropertyRowCatalog.HintRemark));
                rows.Add(new PropertyRow("左边", PropertyRowCatalog.FormatNumber(control.X), PropertyCellKind.Integer, PropertyRowCatalog.HintLeft));
                rows.Add(new PropertyRow("顶边", PropertyRowCatalog.FormatNumber(control.Y), PropertyCellKind.Integer, PropertyRowCatalog.HintTop));
                rows.Add(new PropertyRow("宽度", PropertyRowCatalog.FormatNumber(control.Width), PropertyCellKind.Integer, PropertyRowCatalog.HintWidth));
                rows.Add(new PropertyRow("高度", PropertyRowCatalog.FormatNumber(control.Height), PropertyCellKind.Integer, PropertyRowCatalog.HintHeight));
                rows.Add(new PropertyRow("可视", PropertyRowCatalog.BoolText(control.IsVisible), PropertyCellKind.Bool, PropertyRowCatalog.HintVisible));
                rows.Add(new PropertyRow("禁止", PropertyRowCatalog.BoolText(!control.IsEnabled), PropertyCellKind.Bool, PropertyRowCatalog.HintForbidden));
                rows.Add(new PropertyRow("标题", control.Text, PropertyCellKind.Text, PropertyRowCatalog.HintTitle));
                rows.Add(new PropertyRow("字号", PropertyRowCatalog.FormatNumber(control.FontSize), PropertyCellKind.Integer, PropertyRowCatalog.HintFontSize));
                rows.Add(new PropertyRow("前景", control.Foreground, PropertyCellKind.Color, PropertyRowCatalog.HintForeground));
                rows.Add(new PropertyRow("背景", control.Background, PropertyCellKind.Color, PropertyRowCatalog.HintBackground));
                if (control.Type is YyControlTypes.TextBox)
                {
                    rows.Add(new PropertyRow("只读", PropertyRowCatalog.BoolText(control.ReadOnly), PropertyCellKind.Bool, PropertyRowCatalog.HintReadOnly));
                    rows.Add(new PropertyRow("多行", PropertyRowCatalog.BoolText(control.MultiLine), PropertyCellKind.Bool, PropertyRowCatalog.HintMultiLine));
                    rows.Add(new PropertyRow("密码", control.PasswordChar, PropertyCellKind.Text, PropertyRowCatalog.HintPassword));
                    rows.Add(new PropertyRow("最大长度", PropertyRowCatalog.FormatNumber(control.MaxLength), PropertyCellKind.Integer, PropertyRowCatalog.HintMaxLength));
                }

                if (control.Type is YyControlTypes.PictureBox)
                {
                    rows.Add(new PropertyRow("图片", control.Picture, PropertyCellKind.Text, PropertyRowCatalog.HintPicture));
                }

                rows.Add(new PropertyRow(
                    "数据绑定",
                    string.IsNullOrWhiteSpace(control.DataBindingTable)
                        ? string.Empty
                        : control.DataBindingTable + "." + control.DataBindingColumn,
                    PropertyCellKind.Text,
                    PropertyRowCatalog.HintDataBinding));
                if (control.Type is YyControlTypes.CheckBox or YyControlTypes.RadioButton)
                {
                    rows.Add(new PropertyRow("选中", PropertyRowCatalog.BoolText(control.IsChecked), PropertyCellKind.Bool, PropertyRowCatalog.HintChecked));
                }

                PropertyRows.ItemsSource = rows;
                BuildControlEventSelector(control);
            }
        }
        finally
        {
            _updatingDesignerProperties = false;
            // 属性行模板内的组合框在页签可见时才布局实现,其 SelectionChanged 在本方法
            // 返回后才补发;延迟到布局之后再放开提交,抑制这批伪事件。
            Dispatcher.BeginInvoke(
                new Action(() => _suppressPropertyCommit = false),
                DispatcherPriority.Background);
        }
    }

    private void BuildPropertyObjectCombo()
    {
        PropertyObjectCombo.Items.Clear();
        var formItem = new ComboBoxItem
        {
            Content = _designForm!.Name + "（窗口）",
            Tag = null
        };
        PropertyObjectCombo.Items.Add(formItem);
        foreach (var control in _designForm.Controls)
        {
            PropertyObjectCombo.Items.Add(new ComboBoxItem
            {
                Content = control.Name + "（" + YyControlTypes.GetDisplayName(control.Type) + "）",
                Tag = control
            });
        }

        PropertyObjectCombo.SelectedItem = _selectedControl is null
            ? formItem
            : PropertyObjectCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, _selectedControl))
              ?? formItem;
    }

    private void OnPropertyObjectChanged(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG prop-object-changed guard={_updatingDesignerProperties} item={(PropertyObjectCombo.SelectedItem is ComboBoxItem i ? (i.Content?.ToString() ?? "?") : "null")}");
        if (_updatingDesignerProperties || PropertyObjectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is YyControlModel control)
        {
            SelectDesignerControl(control);
        }
        else
        {
            SelectDesignerForm();
        }
    }

    private void BuildControlEventSelector(YyControlModel control)
    {
        EventSelectorCombo.Items.Clear();
        ResetEventSelectorPlaceholder();
        foreach (var kind in GetControlEventKinds(control.Type))
        {
            var bound = GetControlEvent(control, kind);
            EventSelectorCombo.Items.Add(new ComboBoxItem
            {
                Content = (string.IsNullOrWhiteSpace(bound) ? string.Empty : "✓ ") + GetEventDisplayName(kind),
                Tag = kind
            });
        }

        EventSelectorCombo.IsEnabled = true;
    }

    private void BuildFormEventSelector()
    {
        EventSelectorCombo.Items.Clear();
        ResetEventSelectorPlaceholder();
        EventSelectorCombo.Items.Add(new ComboBoxItem
        {
            Content = (string.IsNullOrWhiteSpace(_designForm?.LoadEvent) ? string.Empty : "✓ ") + EventHandlerNaming.FormLoad,
            Tag = "Load"
        });
        EventSelectorCombo.Items.Add(new ComboBoxItem
        {
            Content = (string.IsNullOrWhiteSpace(_designForm?.CloseEvent) ? string.Empty : "✓ ") + EventHandlerNaming.FormClose,
            Tag = "Close"
        });
        EventSelectorCombo.IsEnabled = true;
    }

    private void ResetEventSelectorPlaceholder()
    {
        var placeholder = new ComboBoxItem
        {
            Content = "在此处选择加入事件处理子程序",
            IsSelected = true
        };
        EventSelectorCombo.Items.Insert(0, placeholder);
        EventSelectorCombo.SelectedIndex = 0;
    }

    private static IEnumerable<ControlEventKind> GetControlEventKinds(string controlType) => controlType switch
    {
        YyControlTypes.Button => [ControlEventKind.Click],
        YyControlTypes.TextBox => [ControlEventKind.TextChanged],
        YyControlTypes.ComboBox or YyControlTypes.ListBox => [ControlEventKind.SelectionChanged],
        YyControlTypes.CheckBox or YyControlTypes.RadioButton => [ControlEventKind.CheckedChanged],
        _ => []
    };

    private void OnEventSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG event-selector-changed guard={_updatingDesignerProperties} selected={(EventSelectorCombo.SelectedItem is ComboBoxItem ei ? (ei.Content?.ToString() ?? "?") : "null")}");
        if (_updatingDesignerProperties || _designForm is null)
        {
            return;
        }

        if (EventSelectorCombo.SelectedItem is not ComboBoxItem { Tag: var tag } || tag is null)
        {
            return;
        }

        if (tag is ControlEventKind eventKind && _selectedControl is not null)
        {
            CreateOrOpenControlEventHandler(_selectedControl, eventKind);
        }
        else if (tag is string formKind)
        {
            CreateOrOpenFormEventHandler(formKind == "Load");
        }

        _updatingDesignerProperties = true;
        ResetEventSelectorPlaceholder();
        _updatingDesignerProperties = false;
        RefreshPropertyObjectLabel();
    }

    private void OnPropertyGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PropertyRows.SelectedItem is PropertyRow row)
        {
            ShowHint(row.Description);
        }
    }

    private void ShowHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        EditorHelpBox.Text = text;
        BottomTabs.SelectedIndex = 0;
    }

    private void OnPropertyRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box)
        {
            return;
        }

        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (box.DataContext is PropertyRow row)
        {
            CommitPropertyRow(row);
        }

        e.Handled = true;
    }

    private void OnPropertyRowCommit(object sender, RoutedEventArgs e)
    {
        StudioTrace.Write($"DBG prop-row-commit guard={_updatingDesignerProperties} sender={((sender as TextBox)?.DataContext is PropertyRow pr ? pr.Name : "other")}");
        if (_updatingDesignerProperties || _suppressPropertyCommit)
        {
            return;
        }

        if (sender is TextBox { DataContext: PropertyRow row })
        {
            if (string.Equals(row.ValueText, row.InitialValue, StringComparison.Ordinal))
            {
                return;
            }

            CommitPropertyRow(row);
            row.InitialValue = row.ValueText;
        }
    }

    private void OnPropertyBoolCommit(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG bool-commit guard={_updatingDesignerProperties} suppress={_suppressPropertyCommit}");
        if (_updatingDesignerProperties || _suppressPropertyCommit)
        {
            return;
        }

        if (sender is ComboBox { DataContext: PropertyRow row })
        {
            if (string.Equals(row.ValueText, row.InitialValue, StringComparison.Ordinal))
            {
                return;
            }

            CommitPropertyRow(row);
            row.InitialValue = row.ValueText;
        }
    }

    private void CommitPropertyRow(PropertyRow row)
    {
        if (_updatingDesignerProperties || _designForm is null)
        {
            return;
        }

        if (_selectedControl is null)
        {
            CommitFormProperty(row);
        }
        else
        {
            CommitControlProperty(row);
        }
    }

    private void CommitFormProperty(PropertyRow row)
    {
        switch (row.Name)
        {
            case "名称":
                var name = row.ValueText.Trim();
                if (YyFormSerializer.IsIdentifier(name))
                {
                    _designForm!.Name = name;
                }
                else
                {
                    row.ValueText = _designForm!.Name;
                    ShowHint("名称必须是中文、英文、数字或下划线组成的标识符。");
                    return;
                }

                break;
            case "备注":
                _designForm!.Remark = row.ValueText;
                break;
            case "标题":
                _designForm!.Title = row.ValueText;
                DesignerTitleText.Text = string.IsNullOrWhiteSpace(_designForm.Title)
                    ? _designForm.Name
                    : _designForm.Title;
                break;
            case "宽度":
                if (TryDesignerNumber(row.ValueText, 320, 3840, out var width))
                {
                    _designForm!.Width = width;
                }

                break;
            case "高度":
                if (TryDesignerNumber(row.ValueText, 240, 2160, out var height))
                {
                    _designForm!.Height = height;
                }

                break;
            case "背景":
                _designForm!.Background = row.ValueText.Trim();
                break;
            case "边框":
                var borderStyle = row.ValueText.Trim();
                if (borderStyle is "可调边框" or "对话框边框" or "无边框")
                {
                    _designForm!.BorderStyle = borderStyle;
                }
                else
                {
                    row.ValueText = _designForm!.BorderStyle;
                    ShowHint("边框只能是:可调边框/对话框边框/无边框。");
                    return;
                }

                break;
            case "可否移动":
                _designForm!.Movable = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "初始位置":
                var position = row.ValueText.Trim();
                if (position is "默认" or "居中显示" or "最小化" or "最大化")
                {
                    _designForm!.StartPosition = position;
                }
                else
                {
                    row.ValueText = _designForm!.StartPosition;
                    ShowHint("初始位置只能是:默认/居中显示/最小化/最大化。");
                    return;
                }

                break;
            case "最小化按钮":
                _designForm!.MinimizeBox = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "最大化按钮":
                _designForm!.MaximizeBox = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
        }

        DesignerCanvas.Width = _designForm!.Width;
        DesignerCanvas.Height = _designForm.Height;
        DesignerCanvas.Background = CreateDesignerBackground(_designForm.Background);
        UpdateDesignerTitleChrome();
        RefreshPropertyObjectLabel();
        DesignerChanged();
    }

    private void CommitControlProperty(PropertyRow row)
    {
        var control = _selectedControl!;
        switch (row.Name)
        {
            case "名称":
                var name = row.ValueText.Trim();
                if (!YyFormSerializer.IsIdentifier(name))
                {
                    row.ValueText = control.Name;
                    ShowHint("名称必须是中文、英文、数字或下划线组成的标识符。");
                    return;
                }

                if (_designForm!.Controls.Any(other =>
                        !ReferenceEquals(other, control) &&
                        string.Equals(other.Name, name, StringComparison.Ordinal)))
                {
                    row.ValueText = control.Name;
                    ShowHint("名称“" + name + "”已被其他控件使用。");
                    return;
                }

                control.Name = name;
                break;
            case "备注":
                control.Remark = row.ValueText;
                break;
            case "标题":
                control.Text = row.ValueText;
                break;
            case "左边":
                if (TryDesignerNumber(row.ValueText, -10_000, 10_000, out var x))
                {
                    control.X = x;
                }

                break;
            case "顶边":
                if (TryDesignerNumber(row.ValueText, -10_000, 10_000, out var y))
                {
                    control.Y = y;
                }

                break;
            case "宽度":
                if (TryDesignerNumber(row.ValueText, 24, 3840, out var width))
                {
                    control.Width = width;
                }

                break;
            case "高度":
                if (TryDesignerNumber(row.ValueText, 20, 2160, out var height))
                {
                    control.Height = height;
                }

                break;
            case "字号":
                if (TryDesignerNumber(row.ValueText, 6, 96, out var fontSize))
                {
                    control.FontSize = fontSize;
                }

                break;
            case "可视":
                control.IsVisible = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "禁止":
                control.IsEnabled = !PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "选中":
                control.IsChecked = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "前景":
                control.Foreground = row.ValueText.Trim();
                break;
            case "背景":
                control.Background = row.ValueText.Trim();
                break;
            case "只读":
                control.ReadOnly = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "多行":
                control.MultiLine = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "密码":
                control.PasswordChar = row.ValueText.Length > 0 ? row.ValueText[..1] : string.Empty;
                break;
            case "最大长度":
                if (TryDesignerNumber(row.ValueText, 0, 100_000, out var maxLength))
                {
                    control.MaxLength = (int)maxLength;
                }

                break;
            case "图片":
                control.Picture = row.ValueText.Trim();
                break;
            case "数据绑定":
                var binding = row.ValueText.Trim();
                if (string.IsNullOrEmpty(binding))
                {
                    control.DataBindingTable = null;
                    control.DataBindingColumn = null;
                }
                else
                {
                    var parts = binding.Split('.', 2);
                    if (parts.Length != 2 ||
                        string.IsNullOrWhiteSpace(parts[0]) ||
                        string.IsNullOrWhiteSpace(parts[1]))
                    {
                        row.ValueText = string.IsNullOrWhiteSpace(control.DataBindingTable)
                            ? string.Empty
                            : control.DataBindingTable + "." + control.DataBindingColumn;
                        ShowHint("数据绑定格式为 表名.列名。");
                        return;
                    }

                    control.DataBindingTable = parts[0].Trim();
                    control.DataBindingColumn = parts[1].Trim();
                }

                break;
            default:
                return;
        }

        RefreshSelectedControlElement();
        RefreshPropertyObjectLabel();
        DesignerChanged();
    }

    /// <summary>
    /// 仅刷新对象下拉标签,不重建属性行。
    /// 属性行模板里的真假/颜色组合框在页签可见时才布局实现,
    /// 重建触发的 SelectionChanged 会在守卫外补发;提交路径若整表重建,
    /// 会形成 提交→重建→布局→SelectionChanged→提交 的无限递归(栈溢出)。
    /// </summary>
    private void RefreshPropertyObjectLabel()
    {
        _updatingDesignerProperties = true;
        try
        {
            BuildPropertyObjectCombo();
        }
        finally
        {
            _updatingDesignerProperties = false;
        }
    }

    private void CreateOrOpenFormEventHandler(bool isLoad)
    {
        if (_designForm is null)
        {
            return;
        }

        // 对齐官方:未保存的内存窗体也能直接生成窗体事件代码。
        var isFileBacked = !string.IsNullOrWhiteSpace(_currentDocument?.FilePath);
        string? formDirectory = isFileBacked ? Path.GetDirectoryName(_currentDocument!.FilePath)! : null;
        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? (isFileBacked
                ? Path.GetFileNameWithoutExtension(_currentDocument!.FilePath!)
                : Path.GetFileNameWithoutExtension(_currentDocument?.DisplayName ?? "未命名窗体")) + ".yyc"
            : _designForm.CodeFile;
        string? codePath = isFileBacked ? Path.GetFullPath(Path.Combine(formDirectory!, codeFileName)) : null;
        var codeDocument = codePath is not null
            ? _documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, codePath, StringComparison.OrdinalIgnoreCase))
            : _documents.FirstOrDefault(document =>
                document.FilePath is null &&
                string.Equals(document.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));

        string? handlerName;
        try
        {
            var existingText = codeDocument?.Text
                ?? (codePath is not null && File.Exists(codePath)
                    ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                    : null);
            var model = string.IsNullOrWhiteSpace(existingText)
                ? new YyCodeDocument { Name = _designForm.Name + "代码" }
                : YyCodeSerializer.Deserialize(existingText);
            handlerName = isLoad ? _designForm.LoadEvent : _designForm.CloseEvent;
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                var baseName = EventHandlerNaming.ForForm(
                    _designForm.Name,
                    isLoad ? EventHandlerNaming.FormLoad : EventHandlerNaming.FormClose);
                var existingNames = model.Functions
                    .Select(function => function.Name)
                    .ToHashSet(StringComparer.Ordinal);
                handlerName = EventHandlerNaming.Unique(baseName, existingNames.Contains);
                if (isLoad)
                {
                    _designForm.LoadEvent = handlerName;
                }
                else
                {
                    _designForm.CloseEvent = handlerName;
                }
            }

            if (model.Functions.All(function =>
                    !string.Equals(function.Name, handlerName, StringComparison.Ordinal)))
            {
                model.Functions.Add(new YyCodeFunction { Name = handlerName });
            }

            var serialized = YyCodeSerializer.Serialize(model);
            var changed = !string.Equals(existingText, serialized, StringComparison.Ordinal);
            if (codeDocument is null)
            {
                codeDocument = codePath is not null
                    ? new EditorDocument(codePath, Path.GetFileName(codePath), serialized)
                    : new EditorDocument(null, codeFileName, serialized);
                _documents.Add(codeDocument);
                if (changed || codePath is null || !File.Exists(codePath))
                {
                    codeDocument.MarkModified();
                }
            }
            else
            {
                codeDocument.Text = serialized;
            }

            if (formDirectory is not null && codePath is not null)
            {
                _designForm.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
            }

            DesignerChanged();
            DocumentTabs.SelectedItem = codeDocument;
            StructuredEditor.SelectFunction(handlerName);
            SetStatus("已绑定窗体事件 " + handlerName + "()", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException)
        {
            MessageBox.Show(
                "无法创建事件代码：\n" + exception.Message,
                "生成事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool TryDesignerNumber(string text, double minimum, double maximum, out double value)
    {
        return double.TryParse(text, out value) &&
               double.IsFinite(value) &&
               value >= minimum &&
               value <= maximum;
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

    private bool SaveDocument(EditorDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            return SaveDocumentAs(document);
        }

        return WriteDocument(document, document.FilePath);
    }

    private bool SaveDocumentAs(EditorDocument document)
    {
        var isForm = document.IsForm;
        var dialog = new SaveFileDialog
        {
            Title = isForm ? "保存窗体设计" : "保存结构化代码",
            Filter = isForm
                ? "窗体设计 (*.yyx)|*.yyx"
                : "结构化代码 (*.yyc)|*.yyc",
            DefaultExt = isForm ? ".yyx" : ".yyc",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(document.FilePath)
                ? document.DisplayName
                : Path.GetFileName(document.FilePath),
            InitialDirectory = !string.IsNullOrWhiteSpace(_workspacePath)
                ? _workspacePath
                : !string.IsNullOrWhiteSpace(document.FilePath)
                    ? Path.GetDirectoryName(document.FilePath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        if ((isForm && !string.Equals(Path.GetExtension(dialog.FileName), ".yyx", StringComparison.OrdinalIgnoreCase)) ||
            (!isForm && !string.Equals(Path.GetExtension(dialog.FileName), ".yyc", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                isForm ? "窗体文件必须使用 .yyx 扩展名。" : "代码文件必须使用 .yyc 扩展名。",
                "文件格式",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            _workspacePath = Path.GetDirectoryName(dialog.FileName);
            PopulateProjectTree();
        }

        return WriteDocument(document, dialog.FileName);
    }

    private bool WriteDocument(EditorDocument document, string path)
    {
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(path, document.Text, encoding);
            document.MarkSaved(path);
            if (ReferenceEquals(document, _currentDocument))
            {
                UpdateDocumentIdentity();
            }

            if (!string.IsNullOrWhiteSpace(_workspacePath))
            {
                PopulateProjectTree();
            }

            _settings.AddRecent(path);
            SetStatus($"已保存 {Path.GetFileName(path)}", ReadyBrush);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException or YyDiagnostic)
        {
            MessageBox.Show(
                $"保存失败：\n{exception.Message}",
                "保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool EnsureCanCloseDocument(EditorDocument document)
    {
        if (!document.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            $"“{document.DisplayName}”还没有保存。是否现在保存？",
            "关闭未保存的标签",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(document),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool CloseDocument(EditorDocument document)
    {
        if (!EnsureCanCloseDocument(document))
        {
            return false;
        }

        var index = _documents.IndexOf(document);
        _documents.Remove(document);
        if (_documents.Count == 0)
        {
            // 官方先后逻辑:关闭全部文档后回到起始页。
            AddStartPage();
        }
        else
        {
            DocumentTabs.SelectedIndex = Math.Clamp(index - 1, 0, _documents.Count - 1);
        }

        SetStatus($"已关闭 {document.DisplayName}", ReadyBrush);
        return true;
    }

    private async Task<bool> RunDocumentAsync(YyDebugProbe? debugger = null)
    {
        if (_runCancellation is not null)
        {
            return false;
        }

        var document = _currentDocument;
        var source = document?.Text ?? Editor.Text;
        _debugLineMap.Clear();
        if (document?.IsStructuredCode == true &&
            string.Equals(
                Path.GetExtension(document.FilePath ?? document.DisplayName),
                ".yyc",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                source = YyCodeDocumentConverter.ToSource(
                    YyCodeSerializer.Deserialize(source),
                    _debugLineMap);
            }
            catch (YyCodeFormatException exception)
            {
                AppendOutput($"✕ 结构化代码错误 · 第 {exception.Line} 行，第 {exception.Column} 列");
                AppendOutput("  " + exception.Message);
                SetStatus("结构化代码无法运行", ErrorBrush);
                return false;
            }
        }

        var sourcePath = _currentDocument?.FilePath;
        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        OutputBox.Clear();
        var isDebugRun = debugger is not null;
        RunInfoText.Text = isDebugRun ? "调试运行…" : "正在运行…";
        SetRunningState(true);
        SetStatus(isDebugRun ? "调试运行中" : "程序运行中", WarningBrush);
        var succeeded = false;

        // 普通运行也挂探针,以便 F9 断点在运行中生效。
        debugger ??= new YyDebugProbe(YyDebugMode.Run);
        debugger.IsBreakpoint = line => _breakpoints.ContainsKey(line);
        _debugProbe = debugger;
        _debugSourceLines = source.Replace("\r\n", "\n").Split('\n');
        debugger.Paused += pause => Dispatcher.BeginInvoke(() => OnDebugPaused(pause));

        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                sourcePath,
                line => Dispatcher.Invoke(() => AppendOutput(line)),
                cancellation.Token,
                debugger));

            ShowVariables(result.Variables);
            var elapsed = Math.Max(0, result.Elapsed.TotalMilliseconds);
            AppendOutput(string.Empty);
            AppendOutput($"✓ 执行完成 · {elapsed:F1} ms");
            RunInfoText.Text = $"完成 · {elapsed:F1} ms";
            SetStatus("运行完成", ReadyBrush);
            succeeded = true;
        }
        catch (YyDiagnostic diagnostic)
        {
            ShowDiagnostic(
                "编译错误",
                diagnostic.Message,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Hint,
                diagnostic.SourceName);
        }
        catch (YyRuntimeError error)
        {
            ShowDiagnostic(
                "运行时错误",
                error.Message,
                error.Line,
                error.Column,
                error.Detail,
                error.SourceName);
        }
        catch (OperationCanceledException)
        {
            AppendOutput(string.Empty);
            AppendOutput("■ 程序已停止。");
            RunInfoText.Text = "已停止";
            SetStatus("已停止", WarningBrush);
        }
        catch (Exception exception)
        {
            ShowDiagnostic("内部错误", exception.Message, 1, 1,
                "请保存源码并在问题反馈中附上最小复现示例。");
        }
        finally
        {
            if (ReferenceEquals(_runCancellation, cancellation))
            {
                _runCancellation = null;
            }

            if (ReferenceEquals(_debugProbe, debugger))
            {
                _debugProbe = null;
                _debugPaused = null;
            }

            cancellation.Dispose();
            SetRunningState(false);
        }

        return succeeded;
    }

    private void ShowDiagnostic(
        string category,
        string message,
        int line,
        int column,
        string? hint,
        string? sourceName = null)
    {
        if (!string.IsNullOrWhiteSpace(sourceName) &&
            File.Exists(sourceName) &&
            !string.Equals(sourceName, _currentDocument?.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            OpenDocument(sourceName);
        }

        AppendOutput(string.Empty);
        var location = string.IsNullOrWhiteSpace(sourceName)
            ? string.Empty
            : $"{Path.GetFileName(sourceName)} · ";
        AppendOutput($"✕ {category} · {location}第 {line} 行，第 {column} 列");
        AppendOutput($"  {message}");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            AppendOutput($"  提示：{hint}");
        }

        RunInfoText.Text = $"{category} · {line}:{column}";
        NavigateTo(line, column);
        SetStatus($"{category}：{message}", ErrorBrush);
    }

    private void SetRunningState(bool running)
    {
        RunButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        Editor.IsReadOnly = running;
    }

    private void ShowVariables(IReadOnlyDictionary<string, object?> variables)
    {
        var display = variables
            .Select(pair => new VariableRow(pair.Key, DisplayValue(pair.Value)))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
        VariableList.ItemsSource = display.Length > 0 ? display : null;
        if (display.Length > 0)
        {
            BottomTabs.SelectedIndex = 4;
        }
    }

    private static string DisplayValue(object? value)
    {
        return value switch
        {
            null => "空",
            bool boolean => boolean ? "真" : "假",
            double number when number == Math.Truncate(number) => ((long)number).ToString(),
            string text => $"\"{text}\"",
            DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm:ss"),
            IReadOnlyDictionary<string, object?> dictionary =>
                "{" + string.Join(", ", dictionary.Select(pair => $"{pair.Key}: {DisplayValue(pair.Value)}")) + "}",
            IEnumerable<object?> sequence =>
                "[" + string.Join(", ", sequence.Select(DisplayValue)) + "]",
            _ => value.ToString() ?? string.Empty
        };
    }


    private void NavigateTo(int line, int column)
    {
        if (Editor.Document.LineCount == 0)
        {
            return;
        }

        var targetLine = Math.Clamp(line, 1, Editor.Document.LineCount);
        var documentLine = Editor.Document.GetLineByNumber(targetLine);
        Editor.CaretOffset = documentLine.Offset;
        Editor.Select(documentLine.Offset, documentLine.Length);
        Editor.ScrollToLine(targetLine);
        Editor.Focus();
        CaretText.Text = $"行 {targetLine}，列 {column}";
    }

    private void AppendOutput(string text)
    {
        OutputBox.AppendText(text + Environment.NewLine);
        OutputBox.ScrollToEnd();
    }

    private void SetStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusDot.Fill = brush;
    }

    private void UpdateDocumentIdentity()
    {
        var dirty = _currentDocument?.IsDirty == true;
        var prefix = dirty ? "● " : string.Empty;
        if (_currentDocument is null)
        {
            Title = $"{prefix}易语 Studio - 目的平台: Windows";
            return;
        }

        if (_currentDocument.IsStartPage)
        {
            Title = "易语 Studio - 目的平台: Windows - [起始页]";
            return;
        }

        var formName = _currentDocument.IsForm
            ? _designForm?.Name ?? _currentDocument.DisplayName
            : GetAssociatedFormForCurrentDocument()?.Name;

        Title = formName is null
            ? $"{prefix}易语 Studio - 目的平台: Windows - [程序集: {_currentDocument.DisplayName}]"
            : _currentDocument.IsForm
                ? $"{prefix}易语 Studio - 目的平台: Windows - [窗口: {formName}]"
                : $"{prefix}易语 Studio - 目的平台: Windows - [程序集: {_currentDocument.DisplayName} / {formName}]";
    }

    private void OnClearOutput(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
        RunInfoText.Text = string.Empty;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnSyntaxClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            """
            易语 3 快速语法

            变量 名称 = "值"
            变量 年龄: 数字 = 20
            常量 周长 = 2 * 3.14 * 半径

            如果 条件 则
                ...
            否则
                ...
            结束如果

            计次循环 序号 从 1 到 10
                ...
            结束循环

            遍历 元素 于 集合
                ...
            结束遍历

            子程序 问候(姓名: 文本 = "世界"): 文本
                返回 "你好，" + 姓名
            结束子程序

            集合：[1, 2, 3]
            对象：{姓名: "林川", 年龄: 20}
            模块：引入 "工具.yyc"
            """,
            "语法速查",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            """
            易语 Studio 3.0 MVP

            一个 Windows 原生、中文优先的现代易语言重制版。

            当前内核：解释执行、UTF-8、函数/递归、集合、
            对象、异常处理、JSON、行号诊断和可取消运行。

            原版 .e 二进制格式与窗体设计器尚未实现。
            """,
            "关于易语 Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnToggleExplorer(object sender, RoutedEventArgs e)
    {
        if (ExplorerPanel.Visibility == Visibility.Visible)
        {
            _explorerWidth = ExplorerColumn.Width;
            ExplorerPanel.Visibility = Visibility.Collapsed;
            ExplorerSplitter.Visibility = Visibility.Collapsed;
            ExplorerColumn.MinWidth = 0;
            ExplorerColumn.Width = new GridLength(0);
        }
        else
        {
            ExplorerPanel.Visibility = Visibility.Visible;
            ExplorerSplitter.Visibility = Visibility.Visible;
            ExplorerColumn.MinWidth = 180;
            ExplorerColumn.Width = _explorerWidth.Value > 0 ? _explorerWidth : new GridLength(252);
        }
    }

    private void OnToggleToolboxPanel(object sender, RoutedEventArgs e)
    {
        if (InspectorPanel.Visibility == Visibility.Visible)
        {
            _inspectorWidth = InspectorColumn.Width;
            InspectorPanel.Visibility = Visibility.Collapsed;
            InspectorSplitter.Visibility = Visibility.Collapsed;
            InspectorColumn.MinWidth = 0;
            InspectorColumn.Width = new GridLength(0);
        }
        else
        {
            InspectorPanel.Visibility = Visibility.Visible;
            InspectorSplitter.Visibility = Visibility.Visible;
            InspectorColumn.MinWidth = 104;
            InspectorColumn.Width = _inspectorWidth.Value > 0 ? _inspectorWidth : new GridLength(132);
        }
    }

    private void OnToggleStandardToolbar(object sender, RoutedEventArgs e)
    {
        StandardToolbarRow.Visibility =
            ToggleStandardToolbarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleAlignToolbar(object sender, RoutedEventArgs e)
    {
        AlignToolbarRow.Visibility =
            ToggleAlignToolbarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleStatusBar(object sender, RoutedEventArgs e)
    {
        StatusBarRow.Visibility =
            ToggleStatusBarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowWorkTab(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        ExplorerPanel.Visibility = Visibility.Visible;
        if (tag == "支持库")
        {
            WorkTabs.SelectedIndex = 0;
        }
        else if (tag == "程序")
        {
            WorkTabs.SelectedIndex = 1;
        }
        else if (tag == "属性")
        {
            WorkTabs.SelectedIndex = 2;
        }
    }

    private void OnOpenDataTableView(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string kind })
        {
            ShowDataTableView(kind);
        }
    }

    private void OnInsertFunctionClick(object sender, RoutedEventArgs e)
    {
        if (StructuredEditor.Visibility == Visibility.Visible)
        {
            StructuredEditor.InsertFunction();
        }
        else
        {
            SetStatus("请先打开 .yyc 代码文件。", WarningBrush);
        }
    }

    private void OnInsertConstantClick(object sender, RoutedEventArgs e)
    {
        ShowDataTableView("常量表");
    }

    private void OnInsertLocalVariableClick(object sender, RoutedEventArgs e)
    {
        if (StructuredEditor.Visibility == Visibility.Visible)
        {
            StructuredEditor.InsertLocalVariable();
        }
        else
        {
            SetStatus("请先打开 .yyc 代码文件。", WarningBrush);
        }
    }

    private void OnMenuEditorClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "菜单编辑器将在后续版本提供。\n当前可先在窗体属性中设置菜单。",
            "菜单编辑器",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null)
        {
            SetStatus("没有可打印的文档。", WarningBrush);
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            PagePadding = new Thickness(48),
            PageWidth = dialog.PrintableAreaWidth
        };
        flow.Blocks.Add(new Paragraph(new Run(_currentDocument.DisplayName))
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });
        foreach (var line in _currentDocument.Text.Split('\n'))
        {
            flow.Blocks.Add(new Paragraph(new Run(line.TrimEnd('\r')))
            {
                Margin = new Thickness(0)
            });
        }

        dialog.PrintDocument(
            ((IDocumentPaginatorSource)flow).DocumentPaginator,
            _currentDocument.DisplayName);
        SetStatus($"已发送到打印机：{_currentDocument.DisplayName}", ReadyBrush);
    }

    private void OnAddResourceClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再向资源表添加资源。");
            return;
        }

        if (sender is not MenuItem { Tag: string kind })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"添加{kind}资源",
            Filter = kind == "图片"
                ? "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.ico|所有文件|*.*"
                : "声音文件|*.wav;*.mp3;*.mid;*.wma|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(dialog.FileName);
        var existing = _project.ResourceTable
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        var unique = name;
        while (existing.Contains(unique))
        {
            unique += "_";
        }

        _project.ResourceTable.Add(new YyResourceEntry
        {
            Name = unique,
            TypeName = kind,
            File = dialog.FileName
        });
        _project.SaveManifest();
        ShowDataTableView("资源表");
        SetStatus($"已添加{kind}资源“{unique}”", ReadyBrush);
    }

    private void ApplySettings()
    {
        Editor.FontSize = _settings.EditorFontSize;
        Editor.Options.IndentationSize = _settings.TabWidth;
        SnapCheckBox.IsChecked = _settings.SnapGrid;
    }

    private void OnSystemConfigClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SystemConfigDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Save();
            ApplySettings();
            SetStatus("系统配置已保存", ReadyBrush);
        }
    }

    private void OnSupportConfigClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SupportConfigDialog(
            StudioCatalog.Categories.Select(category => category.Name),
            _settings.DisabledLibraries.ToHashSet(StringComparer.Ordinal))
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _settings.DisabledLibraries = dialog.CollectDisabled().ToList();
            _settings.Save();
            BuildSupportTree();
            SetStatus("支持库配置已保存", ReadyBrush);
        }
    }

    private void OnModuleManagerClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ModuleManagerDialog(_workspacePath) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnCompileModuleClick(object sender, RoutedEventArgs e)
    {
        if (_currentDocument?.IsStructuredCode != true || StructuredEditor.Document is null)
        {
            ShowHint("请先打开 .yyc 代码文件，再编译为易模块。");
            return;
        }

        var document = StructuredEditor.Document;
        var exports = document.Functions.Where(function => function.IsExport || function.IsPublic).ToList();
        if (exports.Count == 0)
        {
            MessageBox.Show(
                this,
                "当前程序集没有可导出的子程序。\n请在子程序表的“易包”或“公开”列设为“真”后重试。",
                "编译为易模块",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "编译为易模块",
            Filter = "易模块 (*.ec)|*.ec",
            DefaultExt = ".ec",
            AddExtension = true,
            FileName = document.Name + ".ec",
            InitialDirectory = !string.IsNullOrWhiteSpace(_workspacePath)
                ? _workspacePath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var module = YyModule.Compile(document, exports);
            module.Save(dialog.FileName);
            SetStatus($"易模块已生成：{Path.GetFileName(dialog.FileName)}", ReadyBrush);
            MessageBox.Show(
                this,
                $"易模块编译完成：\n{dialog.FileName}\n\n共导出 {exports.Count} 个公开子程序。",
                "编译为易模块",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("易模块编译失败", ErrorBrush);
            MessageBox.Show(this, exception.Message, "编译为易模块",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDatabaseDesignerClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再设计数据表。");
            return;
        }

        var dialog = new DatabaseDesignerDialog(
            _project.DatabaseTables,
            () => _project.SaveManifest())
        {
            Owner = this
        };
        dialog.ShowDialog();
        SetStatus("数据库结构已保存", ReadyBrush);
    }

    private void OnDataBindClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再设置数据绑定。");
            return;
        }

        if (_designForm is null || _selectedControl is null)
        {
            ShowHint("请先在窗体设计器中选择一个控件，再设置数据绑定。");
            return;
        }

        var dialog = new DataBindDialog(_selectedControl, _project.DatabaseTables) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            PopulateDesignerProperties();
            DesignerChanged();
            SetStatus($"已更新 {_selectedControl.Name} 的数据绑定", ReadyBrush);
        }
    }

    /// <summary>命令夹分类行:分类小图标 + 名称(对齐官方支持库夹)。</summary>
    private static FrameworkElement CategoryHeader(string name)
    {
        var (glyph, color) = name switch
        {
            "流程控制" => ("◆", "#000080"),
            "算术运算" => ("＋", "#008000"),
            "逻辑比较" => ("?", "#800080"),
            "位运算" => ("01", "#008080"),
            "变量操作" => ("x", "#800000"),
            "数组操作" => ("≣", "#000080"),
            "环境存取" => ("⌂", "#808000"),
            "拼音处理" => ("♪", "#800080"),
            "文本操作" => ("A", "#008000"),
            "字节集操作" => ("▤", "#008080"),
            "数值转换" => ("#", "#800000"),
            "时间操作" => ("◷", "#000080"),
            "磁盘操作" => ("▣", "#808000"),
            "文件读写" => ("≣", "#008000"),
            "系统处理" => ("⚙", "#800080"),
            "媒体播放" => ("▶", "#008080"),
            "程序调试" => ("❚❚", "#800000"),
            "其他" => ("…", "#808080"),
            "数据库" => ("▦", "#000080"),
            "网络通信" => ("◎", "#008000"),
            "控制台操作" => (">_", "#800080"),
            "数据类型" => ("T", "#008080"),
            "常量" => ("c", "#800000"),
            _ => ("●", "#808080")
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 18,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void BuildSupportTree()
    {
        SupportTree.Items.Clear();
        var root = new TreeViewItem
        {
            Header = "系统核心支持库",
            IsExpanded = true
        };
        foreach (var category in StudioCatalog.Categories)
        {
            if (_settings.DisabledLibraries.Contains(category.Name))
            {
                continue;
            }

            var categoryItem = new TreeViewItem
            {
                Header = CategoryHeader(category.Name),
                Tag = category
            };
            if (category.Commands.Count == 0)
            {
                categoryItem.Items.Add(new TreeViewItem
                {
                    Header = "（暂无命令）",
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
                });
            }
            else
            {
                foreach (var command in category.Commands)
                {
                    categoryItem.Items.Add(new TreeViewItem
                    {
                        Header = command.Name,
                        Tag = command
                    });
                }
            }

            root.Items.Add(categoryItem);
        }

        SupportTree.Items.Add(root);
    }

    private void OnSupportTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case TreeViewItem { Tag: BuiltinDoc command }:
                ShowHint(command.Signature + "\n" + command.Description);
                break;
            case TreeViewItem { Tag: CommandCategory category }:
                ShowHint(category.Commands.Count > 0
                    ? "“" + category.Name + "”分类包含 " + category.Commands.Count + " 条命令：\n" +
                      string.Join("、", category.Commands.Select(command => command.Name)) +
                      "\n\n展开分类后双击命令即可插入代码。"
                    : "“" + category.Name + "”分类暂无命令，后续版本补充。");
                break;
        }
    }

    private void OnSupportTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (SupportTree.SelectedItem is not TreeViewItem { Tag: BuiltinDoc command })
        {
            return;
        }

        if (StructuredEditor.Visibility == Visibility.Visible)
        {
            StructuredEditor.InsertCommand(command.Name);
            SetStatus($"已插入命令 {command.Name}()", ReadyBrush);
            e.Handled = true;
            return;
        }

        if (Editor.Visibility == Visibility.Visible &&
            _currentDocument is not null)
        {
            Editor.Document.Insert(Editor.CaretOffset, command.Name);
            Editor.Focus();
        }
    }

    private void BuildWindowMenu()
    {
        WindowMenu.Items.Clear();
        foreach (var document in _documents)
        {
            var item = new MenuItem
            {
                Header = document.DisplayName,
                Tag = document,
                IsChecked = ReferenceEquals(document, _currentDocument)
            };
            item.Click += (_, _) => DocumentTabs.SelectedItem = document;
            WindowMenu.Items.Add(item);
        }

        if (WindowMenu.Items.Count == 0)
        {
            WindowMenu.Items.Add(new MenuItem { Header = "（无打开的窗口）", IsEnabled = false });
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _runCancellation.Cancel();
        }

        foreach (var document in _documents.Where(document => document.IsDirty).ToList())
        {
            if (!EnsureCanCloseDocument(document))
            {
                e.Cancel = true;
                return;
            }
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
