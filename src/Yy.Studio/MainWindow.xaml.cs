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
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;

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

    private static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(85, 214, 179));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(243, 183, 97));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 116, 116));
    private static readonly HashSet<string> IgnoredDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "bin", "obj", "node_modules" };
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

    private readonly DispatcherTimer _outlineTimer;
    private readonly ObservableCollection<EditorDocument> _documents = [];
    private readonly List<FrameworkElement> _selectionVisuals = [];
    private readonly List<YyControlModel> _selectedControls = [];
    private readonly Dictionary<YyControlModel, Rect> _designerDragBounds = [];
    private CompletionWindow? _completionWindow;
    private EditorDocument? _currentDocument;
    private string? _workspacePath;
    private YyProject? _project;
    private YyFormModel? _designForm;
    private YyControlModel? _selectedControl;
    private FrameworkElement? _selectedElement;
    private DesignerDragMode _designerDragMode;
    private Point _designerDragStart;
    private Rect _designerOriginalBounds;
    private ResizeDirection _resizeDirection;
    private bool _designerDragChanged;
    private bool _updatingDesignerProperties;
    private Point _toolboxDragStart;
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

        _outlineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _outlineTimer.Tick += (_, _) =>
        {
            _outlineTimer.Stop();
            RefreshOutline(silent: true);
        };

        ConfigureEditor();
        StudioTrace.Write("ConfigureEditor complete");
        BuiltinList.ItemsSource = StudioCatalog.Builtins;
        ControlToolbox.ItemsSource = YyControlTypes.All;
        DocumentTabs.ItemsSource = _documents;
        AddNewDocument(StudioCatalog.StarterCode);
        StudioTrace.Write("Initial document complete");
        RefreshOutline(silent: true);
        AppendOutput("易语 Studio 已就绪。按 F5 运行当前源码。");
        SetStatus("就绪", ReadyBrush);
        StudioTrace.Write("MainWindow ctor complete");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
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

        _outlineTimer.Stop();
        _outlineTimer.Start();
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
                return projectForm.Model;
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return null;
        }

        var candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".yyx");
        if (!File.Exists(candidate))
        {
            return null;
        }

        try
        {
            return YyFormSerializer.Deserialize(File.ReadAllText(candidate));
        }
        catch (Exception exception) when (
            exception is YyFormFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

    private void OnOpenFile(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开易语源码",
            Filter = "易语文件 (*.ey;*.yyx)|*.ey;*.yyx|易语源码 (*.ey)|*.ey|窗体设计 (*.yyx)|*.yyx|所有文件 (*.*)|*.*",
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
        if (_currentDocument?.IsForm == true)
        {
            PreviewCurrentForm();
            return;
        }

        await RunDocumentAsync();
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
            Title = "编译为 Windows 可执行文件",
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
            var output = await compiler.CompileAsync(_project, dialog.FileName, progress);
            SetStatus("EXE 编译完成", ReadyBrush);
            MessageBox.Show(
                $"编译完成：\n{output}\n\n可以直接双击运行，不需要源码目录或 .NET SDK。",
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
        if (_designForm is null || string.IsNullOrWhiteSpace(_currentDocument?.FilePath))
        {
            MessageBox.Show(
                "请先保存窗体，再双击控件生成代码后置事件。",
                "生成事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var formPath = _currentDocument.FilePath;
        var formDirectory = Path.GetDirectoryName(formPath)!;
        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? Path.GetFileNameWithoutExtension(formPath) + ".ey"
            : _designForm.CodeFile;
        var codePath = Path.GetFullPath(Path.Combine(formDirectory, codeFileName));
        var codeDocument = _documents.FirstOrDefault(document =>
            string.Equals(document.FilePath, codePath, StringComparison.OrdinalIgnoreCase));
        var source = codeDocument?.Text
            ?? (File.Exists(codePath)
                ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                : $"""
                   .版本 3
                   .程序集 {_designForm.Name}代码

                   ' 在这里编写窗体事件代码。
                   """);

        var handlerName = GetControlEvent(control, eventKind);
        if (string.IsNullOrWhiteSpace(handlerName))
        {
            var suffix = eventKind switch
            {
                ControlEventKind.TextChanged => "_文本改变",
                ControlEventKind.SelectionChanged => "_选择改变",
                ControlEventKind.CheckedChanged => "_选中改变",
                _ => "_被单击"
            };
            var baseName = control.Name + suffix;
            handlerName = baseName;
            var index = 2;
            var existing = YyRunner.CompileSource(source, codePath).Functions
                .Select(function => function.Name)
                .ToHashSet(StringComparer.Ordinal);
            while (existing.Contains(handlerName))
            {
                handlerName = baseName + index++;
            }
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
            codeDocument = new EditorDocument(codePath, Path.GetFileName(codePath), source);
            _documents.Add(codeDocument);
            if (!File.Exists(codePath))
            {
                codeDocument.MarkModified();
            }
        }
        else
        {
            codeDocument.Text = source;
        }

        SetControlEvent(control, eventKind, handlerName);
        _designForm.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
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
        ControlEventKind.TextChanged => "文本改变",
        ControlEventKind.SelectionChanged => "选择改变",
        ControlEventKind.CheckedChanged => "选中改变",
        _ => "单击"
    };

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

        var preview = new YyFormPreviewWindow(_designForm, _currentDocument?.FilePath) { Owner = this };
        preview.Closed += (_, _) => StudioTrace.Write("Form preview closed");
        preview.Show();
        StudioTrace.Write($"Form preview shown: {preview.Title}");
        RunInfoText.Text = $"窗体预览：{_designForm.Title}";
        SetStatus("窗体预览已打开", ReadyBrush);
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

        ProjectTitleText.Text = _project?.Name ?? "项目";
        PopulateProjectTree();
        SetStatus(
            _project is null
                ? $"已打开目录：{Path.GetFileName(path)}"
                : $"已打开项目：{_project.Name}",
            ReadyBrush);

        var startupForm = _project?.Forms.FirstOrDefault(form =>
            string.Equals(form.Model.Name, _project.StartupForm, StringComparison.Ordinal));
        var openPath = startupForm?.FormPath
            ?? _project?.EntryPath
            ?? Directory.EnumerateFiles(path, "*.ey", SearchOption.AllDirectories)
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

    private void PopulateProjectTree()
    {
        ProjectTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(_workspacePath) || !Directory.Exists(_workspacePath))
        {
            return;
        }

        var root = BuildDirectoryNode(_workspacePath);
        root.IsExpanded = true;
        ProjectTree.Items.Add(root);
    }

    private TreeViewItem BuildDirectoryNode(string path)
    {
        var item = new TreeViewItem
        {
            Header = MakeTreeHeader("▸  " + Path.GetFileName(path), isFolder: true),
            Tag = path,
            IsExpanded = path == _workspacePath
        };

        IEnumerable<string> directories;
        IEnumerable<string> files;
        try
        {
            directories = Directory.EnumerateDirectories(path)
                .Where(directory => !IgnoredDirectories.Contains(Path.GetFileName(directory)))
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            files = Directory.EnumerateFiles(path)
                .Where(file => string.Equals(Path.GetExtension(file), ".ey", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetExtension(file), ".yyx", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(Path.GetExtension(file), ".yyproj", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            directories = [];
            files = [];
        }

        foreach (var directory in directories)
        {
            item.Items.Add(BuildDirectoryNode(directory));
        }

        var formFiles = files
            .Where(file => string.Equals(Path.GetExtension(file), ".yyx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var linkedCodeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var formFile in formFiles)
        {
            var formItem = new TreeViewItem
            {
                Header = MakeTreeHeader("▣  " + Path.GetFileName(formFile), isFolder: false),
                Tag = formFile,
                Padding = new Thickness(3, 3, 3, 3)
            };
            try
            {
                var form = YyFormSerializer.Deserialize(File.ReadAllText(formFile));
                string? codePath = null;
                if (!string.IsNullOrWhiteSpace(form.CodeFile))
                {
                    codePath = Path.GetFullPath(
                        Path.Combine(Path.GetDirectoryName(formFile)!, form.CodeFile));
                    if (File.Exists(codePath))
                    {
                        linkedCodeFiles.Add(codePath);
                        formItem.Items.Add(new TreeViewItem
                        {
                            Header = MakeTreeHeader("◈  " + Path.GetFileName(codePath), isFolder: false),
                            Tag = codePath,
                            Padding = new Thickness(3, 3, 3, 3)
                        });
                    }
                }

                var componentsItem = new TreeViewItem
                {
                    Header = MakeTreeHeader($"◆  组件 ({form.Controls.Count})", isFolder: true),
                    IsExpanded = false
                };
                foreach (var control in form.Controls)
                {
                    var controlItem = new TreeViewItem
                    {
                        Header = MakeTreeHeader($"◇  {control.Name}  ·  {control.Type}", isFolder: false),
                        Tag = new ProjectControlLink(formFile, control.Name)
                    };
                    foreach (var (eventName, description) in GetControlEvents(control))
                    {
                        controlItem.Items.Add(new TreeViewItem
                        {
                            Header = MakeTreeHeader($"ƒ  {description}  ·  {eventName}", isFolder: false),
                            Tag = new ProjectEventLink(formFile, codePath, eventName, control.Name)
                        });
                    }

                    componentsItem.Items.Add(controlItem);
                }

                formItem.Items.Add(componentsItem);
            }
            catch (Exception exception) when (exception is IOException or YyFormFormatException)
            {
                // The file remains openable so the user can repair invalid form XML.
            }

            item.Items.Add(formItem);
        }

        foreach (var file in files.Where(file =>
                     !linkedCodeFiles.Contains(file) &&
                     !formFiles.Contains(file, StringComparer.OrdinalIgnoreCase)))
        {
            item.Items.Add(new TreeViewItem
            {
                Header = MakeTreeHeader(
                    (string.Equals(Path.GetExtension(file), ".ey", StringComparison.OrdinalIgnoreCase)
                        ? "◈  "
                        : "◇  ") + Path.GetFileName(file),
                    isFolder: false),
                Tag = file,
                Padding = new Thickness(3, 3, 3, 3)
            });
        }

        return item;
    }

    private static TextBlock MakeTreeHeader(string text, bool isFolder)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(isFolder
                ? Color.FromRgb(211, 221, 232)
                : Color.FromRgb(196, 212, 229))
        };
    }

    private void OnProjectTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: ProjectControlLink controlLink })
        {
            OpenDocument(controlLink.FormPath);
            if (_designForm is not null)
            {
                var control = _designForm.Controls.FirstOrDefault(item =>
                    string.Equals(item.Name, controlLink.ControlName, StringComparison.Ordinal));
                if (control is not null)
                {
                    SelectDesignerControl(control);
                }
            }

            return;
        }

        if (e.NewValue is TreeViewItem { Tag: ProjectEventLink eventLink })
        {
            OpenEventHandlerFromProject(eventLink);
            return;
        }

        if (e.NewValue is not TreeViewItem { Tag: string path } || !File.Exists(path))
        {
            return;
        }

        if (string.Equals(Path.GetExtension(path), ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkspace(Path.GetDirectoryName(Path.GetFullPath(path))!);
            return;
        }

        if (!string.Equals(path, _currentDocument?.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            OpenDocument(path);
        }
    }

    private void OpenEventHandlerFromProject(ProjectEventLink link)
    {
        if (string.IsNullOrWhiteSpace(link.CodePath) || !File.Exists(link.CodePath))
        {
            MessageBox.Show(
                $"控件“{link.ControlName}”绑定了事件“{link.EventName}”，但还没有代码后置文件。\n\n请在窗体设计器中双击该控件生成事件代码。",
                "事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenDocument(link.CodePath);
        var marker = "子程序 " + link.EventName;
        var offset = Editor.Text.IndexOf(marker, StringComparison.Ordinal);
        if (offset < 0)
        {
            MessageBox.Show(
                $"代码后置中还没有事件函数“{link.EventName}”。\n\n请在窗体设计器中双击控件生成它。",
                "事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Editor.CaretOffset = offset;
        var line = Editor.Document.GetLineByOffset(offset);
        Editor.Select(line.Offset, line.Length);
        Editor.ScrollToLine(line.LineNumber);
        Editor.Focus();
    }

    private static IEnumerable<(string Name, string Description)> GetControlEvents(YyControlModel control)
    {
        if (!string.IsNullOrWhiteSpace(control.ClickEvent))
        {
            yield return (control.ClickEvent, "单击");
        }

        if (!string.IsNullOrWhiteSpace(control.TextChangedEvent))
        {
            yield return (control.TextChangedEvent, "文本改变");
        }

        if (!string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
        {
            yield return (control.SelectionChangedEvent, "选择改变");
        }

        if (!string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
        {
            yield return (control.CheckedChangedEvent, "选中改变");
        }
    }

    private void OnProjectTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is TreeViewItem { Tag: string path } &&
            Directory.Exists(path))
        {
            if (ProjectTree.SelectedItem is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }
    }

    private void OpenDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
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
        var document = new EditorDocument(null, displayName, source);
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
    }

    private string NextUntitledName()
    {
        if (_documents.All(document => document.DisplayName != "未命名.ey"))
        {
            return "未命名.ey";
        }

        var index = 2;
        while (_documents.Any(document => document.DisplayName == $"未命名 {index}.ey"))
        {
            index++;
        }

        return $"未命名 {index}.ey";
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
        if (document.IsForm)
        {
            ShowFormDesigner(document);
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
        RefreshOutline(silent: true);
        CaretText.Text = $"行 {Editor.TextArea.Caret.Line}，列 {Editor.TextArea.Caret.Column}";
    }

    private void ShowCodeEditor(EditorDocument document)
    {
        DesignerPanel.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        DesignerInspector.Visibility = Visibility.Collapsed;
        InspectorTabs.Visibility = Visibility.Visible;
        _designForm = null;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        ClearSelectionVisuals();
    }

    private void ShowFormDesigner(EditorDocument document)
    {
        try
        {
            _designForm = YyFormSerializer.Deserialize(document.Text);
            var currentSnapshot = YyFormSerializer.Serialize(_designForm);
            if (document.LastDesignerSnapshot is null ||
                !string.Equals(document.LastDesignerSnapshot, currentSnapshot, StringComparison.Ordinal))
            {
                document.DesignerUndo.Clear();
                document.DesignerRedo.Clear();
                document.LastDesignerSnapshot = currentSnapshot;
            }

            Editor.Visibility = Visibility.Collapsed;
            DesignerPanel.Visibility = Visibility.Visible;
            InspectorTabs.Visibility = Visibility.Collapsed;
            DesignerInspector.Visibility = Visibility.Visible;
            RenderDesignerForm();
            UpdateDesignerHistoryButtons();
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
            : Color.FromRgb(244, 247, 251);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(44, 48, 64, 82)), 1);
        gridPen.Freeze();

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(baseColor),
            null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(
            null,
            gridPen,
            new GeometryGroup
            {
                Children =
                {
                    new LineGeometry(new Point(0, 0), new Point(16, 0)),
                    new LineGeometry(new Point(0, 0), new Point(0, 16))
                }
            }));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
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
        if (e.LeftButton != MouseButtonState.Pressed ||
            ControlToolbox.SelectedItem is not string type)
        {
            return;
        }

        var current = e.GetPosition(ControlToolbox);
        if (Math.Abs(current.X - _toolboxDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _toolboxDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        System.Windows.DragDrop.DoDragDrop(ControlToolbox, type, DragDropEffects.Copy);
    }

    private void OnToolboxDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ControlToolbox.SelectedItem is string type && _designForm is not null)
        {
            AddDesignerControl(type, new Point(44 + _designForm.Controls.Count % 6 * 22, 44));
            e.Handled = true;
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
        type == YyControlTypes.Label ? "标签" : type;

    private static bool TryGetDefaultEvent(string controlType, out ControlEventKind eventKind)
    {
        eventKind = controlType switch
        {
            YyControlTypes.Button => ControlEventKind.Click,
            YyControlTypes.TextBox => ControlEventKind.TextChanged,
            YyControlTypes.ComboBox or YyControlTypes.ListBox => ControlEventKind.SelectionChanged,
            YyControlTypes.CheckBox => ControlEventKind.CheckedChanged,
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
        _selectedControls.Clear();
        _selectedControl = null;
        _selectedElement = null;
        ClearSelectionVisuals();
        PopulateDesignerProperties();
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
                    ReferenceEquals(control, _selectedControl) ? "#007ACC" : "#569CD6"),
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
                Background = YyFormRenderer.Brush("#007ACC"),
                BorderBrush = YyFormRenderer.Brush("#1E1E1E"),
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
        if (_designForm is null || _currentDocument is null)
        {
            return;
        }

        var snapshot = YyFormSerializer.Serialize(_designForm);
        var previous = _currentDocument.LastDesignerSnapshot;
        if (previous is not null && !string.Equals(previous, snapshot, StringComparison.Ordinal))
        {
            _currentDocument.DesignerUndo.Push(previous);
            _currentDocument.DesignerRedo.Clear();
        }

        _currentDocument.LastDesignerSnapshot = snapshot;
        _currentDocument.Text = snapshot;
        UpdateDesignerHistoryButtons();
        UpdateDocumentIdentity();
    }

    private void OnDesignerUndoClick(object sender, RoutedEventArgs e) => UndoDesigner();

    private void OnDesignerRedoClick(object sender, RoutedEventArgs e) => RedoDesigner();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            BottomTabs.SelectedIndex = 1;
            UpdateEditorHelp();
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

        var current = _currentDocument.LastDesignerSnapshot;
        var target = _currentDocument.DesignerUndo.Pop();
        if (current is not null)
        {
            _currentDocument.DesignerRedo.Push(current);
        }

        RestoreDesignerSnapshot(target);
        SetStatus("已撤销窗体设计操作", ReadyBrush);
    }

    private void RedoDesigner()
    {
        if (_currentDocument is null || _currentDocument.DesignerRedo.Count == 0)
        {
            return;
        }

        var current = _currentDocument.LastDesignerSnapshot;
        var target = _currentDocument.DesignerRedo.Pop();
        if (current is not null)
        {
            _currentDocument.DesignerUndo.Push(current);
        }

        RestoreDesignerSnapshot(target);
        SetStatus("已重做窗体设计操作", ReadyBrush);
    }

    private void RestoreDesignerSnapshot(string snapshot)
    {
        if (_currentDocument is null)
        {
            return;
        }

        var selectedName = _selectedControl?.Name;
        _designForm = YyFormSerializer.Deserialize(snapshot);
        _currentDocument.LastDesignerSnapshot = snapshot;
        _currentDocument.Text = snapshot;
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
        if (_designForm is null)
        {
            return;
        }

        _updatingDesignerProperties = true;
        try
        {
            if (_selectedControls.Count > 1)
            {
                PropertyTypeText.Text = $"已选择 {_selectedControls.Count} 个控件";
                PropertyNameBox.Text = string.Empty;
                PropertyTextBox.Text = string.Empty;
                PropertyXBox.Text = string.Empty;
                PropertyYBox.Text = string.Empty;
                PropertyWidthBox.Text = string.Empty;
                PropertyHeightBox.Text = string.Empty;
                PropertyBackgroundBox.Text = string.Empty;
                PropertyForegroundBox.Text = string.Empty;
                PropertyFontSizeBox.Text = string.Empty;
                PropertyCodeFileBox.Text = string.Empty;
                PropertyEventBox.Text = string.Empty;
                PropertyTextChangedEventBox.Text = string.Empty;
                PropertySelectionChangedEventBox.Text = string.Empty;
                PropertyCheckedChangedEventBox.Text = string.Empty;
                PropertyLoadEventBox.Text = string.Empty;
                PropertyCloseEventBox.Text = string.Empty;
                PropertyNameBox.IsEnabled = false;
                PropertyTextBox.IsEnabled = false;
                PropertyXBox.IsEnabled = false;
                PropertyYBox.IsEnabled = false;
                PropertyWidthBox.IsEnabled = false;
                PropertyHeightBox.IsEnabled = false;
                PropertyBackgroundBox.IsEnabled = false;
                PropertyForegroundBox.IsEnabled = false;
                PropertyFontSizeBox.IsEnabled = false;
                PropertyCodeFileBox.IsEnabled = false;
                PropertyEventBox.IsEnabled = false;
                PropertyTextChangedEventBox.IsEnabled = false;
                PropertySelectionChangedEventBox.IsEnabled = false;
                PropertyCheckedChangedEventBox.IsEnabled = false;
                PropertyLoadEventBox.IsEnabled = false;
                PropertyCloseEventBox.IsEnabled = false;
                PropertyEnabledCheck.IsEnabled = false;
                PropertyVisibleCheck.IsEnabled = false;
                PropertyCheckedCheck.IsEnabled = false;
            }
            else if (_selectedControl is null)
            {
                PropertyTypeText.Text = "窗体";
                PropertyNameBox.Text = _designForm.Name;
                PropertyTextBox.Text = _designForm.Title;
                PropertyXBox.Text = "0";
                PropertyYBox.Text = "0";
                PropertyWidthBox.Text = _designForm.Width.ToString("0.###");
                PropertyHeightBox.Text = _designForm.Height.ToString("0.###");
                PropertyBackgroundBox.Text = _designForm.Background;
                PropertyForegroundBox.Text = string.Empty;
                PropertyFontSizeBox.Text = string.Empty;
                PropertyCodeFileBox.Text = _designForm.CodeFile;
                PropertyEventBox.Text = string.Empty;
                PropertyTextChangedEventBox.Text = string.Empty;
                PropertySelectionChangedEventBox.Text = string.Empty;
                PropertyCheckedChangedEventBox.Text = string.Empty;
                PropertyLoadEventBox.Text = _designForm.LoadEvent ?? string.Empty;
                PropertyCloseEventBox.Text = _designForm.CloseEvent ?? string.Empty;
                PropertyXBox.IsEnabled = false;
                PropertyYBox.IsEnabled = false;
                PropertyForegroundBox.IsEnabled = false;
                PropertyFontSizeBox.IsEnabled = false;
                PropertyCodeFileBox.IsEnabled = true;
                PropertyEventBox.IsEnabled = false;
                PropertyTextChangedEventBox.IsEnabled = false;
                PropertySelectionChangedEventBox.IsEnabled = false;
                PropertyCheckedChangedEventBox.IsEnabled = false;
                PropertyLoadEventBox.IsEnabled = true;
                PropertyCloseEventBox.IsEnabled = true;
                PropertyNameBox.IsEnabled = true;
                PropertyTextBox.IsEnabled = true;
                PropertyWidthBox.IsEnabled = true;
                PropertyHeightBox.IsEnabled = true;
                PropertyBackgroundBox.IsEnabled = true;
                PropertyEnabledCheck.IsEnabled = false;
                PropertyVisibleCheck.IsEnabled = false;
                PropertyCheckedCheck.IsEnabled = false;
                PropertyEnabledCheck.IsChecked = true;
                PropertyVisibleCheck.IsChecked = true;
            }
            else
            {
                PropertyTypeText.Text = $"控件 · {_selectedControl.Type}";
                PropertyNameBox.Text = _selectedControl.Name;
                PropertyTextBox.Text = _selectedControl.Text;
                PropertyXBox.Text = _selectedControl.X.ToString("0.###");
                PropertyYBox.Text = _selectedControl.Y.ToString("0.###");
                PropertyWidthBox.Text = _selectedControl.Width.ToString("0.###");
                PropertyHeightBox.Text = _selectedControl.Height.ToString("0.###");
                PropertyBackgroundBox.Text = _selectedControl.Background;
                PropertyForegroundBox.Text = _selectedControl.Foreground;
                PropertyFontSizeBox.Text = _selectedControl.FontSize.ToString("0.###");
                PropertyCodeFileBox.Text = string.Empty;
                PropertyEventBox.Text = _selectedControl.ClickEvent ?? string.Empty;
                PropertyTextChangedEventBox.Text = _selectedControl.TextChangedEvent ?? string.Empty;
                PropertySelectionChangedEventBox.Text = _selectedControl.SelectionChangedEvent ?? string.Empty;
                PropertyCheckedChangedEventBox.Text = _selectedControl.CheckedChangedEvent ?? string.Empty;
                PropertyLoadEventBox.Text = string.Empty;
                PropertyCloseEventBox.Text = string.Empty;
                PropertyXBox.IsEnabled = true;
                PropertyYBox.IsEnabled = true;
                PropertyForegroundBox.IsEnabled = true;
                PropertyFontSizeBox.IsEnabled = true;
                PropertyCodeFileBox.IsEnabled = false;
                PropertyEventBox.IsEnabled = _selectedControl.Type == YyControlTypes.Button;
                PropertyTextChangedEventBox.IsEnabled = _selectedControl.Type == YyControlTypes.TextBox;
                PropertySelectionChangedEventBox.IsEnabled =
                    _selectedControl.Type is YyControlTypes.ComboBox or YyControlTypes.ListBox;
                PropertyCheckedChangedEventBox.IsEnabled = _selectedControl.Type == YyControlTypes.CheckBox;
                PropertyLoadEventBox.IsEnabled = false;
                PropertyCloseEventBox.IsEnabled = false;
                PropertyNameBox.IsEnabled = true;
                PropertyTextBox.IsEnabled = true;
                PropertyXBox.IsEnabled = true;
                PropertyYBox.IsEnabled = true;
                PropertyWidthBox.IsEnabled = true;
                PropertyHeightBox.IsEnabled = true;
                PropertyBackgroundBox.IsEnabled = true;
                PropertyEnabledCheck.IsEnabled = true;
                PropertyVisibleCheck.IsEnabled = true;
                PropertyCheckedCheck.IsEnabled = _selectedControl.Type == YyControlTypes.CheckBox;
                PropertyEnabledCheck.IsChecked = _selectedControl.IsEnabled;
                PropertyVisibleCheck.IsChecked = _selectedControl.IsVisible;
                PropertyCheckedCheck.IsChecked = _selectedControl.IsChecked;
            }
        }
        finally
        {
            _updatingDesignerProperties = false;
        }
    }

    private void OnDesignerPropertyChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingDesignerProperties || _designForm is null)
        {
            return;
        }

        if (_selectedControl is null)
        {
            _designForm.Name = PropertyNameBox.Text.Trim();
            _designForm.Title = PropertyTextBox.Text;
            if (TryDesignerNumber(PropertyWidthBox, 320, 3840, out var width))
            {
                _designForm.Width = width;
            }

            if (TryDesignerNumber(PropertyHeightBox, 240, 2160, out var height))
            {
                _designForm.Height = height;
            }

            _designForm.Background = PropertyBackgroundBox.Text.Trim();
            _designForm.CodeFile = PropertyCodeFileBox.Text.Trim();
            _designForm.LoadEvent = string.IsNullOrWhiteSpace(PropertyLoadEventBox.Text)
                ? null
                : PropertyLoadEventBox.Text.Trim();
            _designForm.CloseEvent = string.IsNullOrWhiteSpace(PropertyCloseEventBox.Text)
                ? null
                : PropertyCloseEventBox.Text.Trim();
            DesignerCanvas.Width = _designForm.Width;
            DesignerCanvas.Height = _designForm.Height;
            DesignerCanvas.Background = CreateDesignerBackground(_designForm.Background);
        }
        else
        {
            var proposedName = PropertyNameBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(proposedName) &&
                _designForm.Controls.All(control =>
                    ReferenceEquals(control, _selectedControl) ||
                    !string.Equals(control.Name, proposedName, StringComparison.Ordinal)))
            {
                _selectedControl.Name = proposedName;
            }

            _selectedControl.Text = PropertyTextBox.Text;
            if (TryDesignerNumber(PropertyXBox, -10_000, 10_000, out var x))
            {
                _selectedControl.X = x;
            }

            if (TryDesignerNumber(PropertyYBox, -10_000, 10_000, out var y))
            {
                _selectedControl.Y = y;
            }

            if (TryDesignerNumber(PropertyWidthBox, 24, 3840, out var width))
            {
                _selectedControl.Width = width;
            }

            if (TryDesignerNumber(PropertyHeightBox, 20, 2160, out var height))
            {
                _selectedControl.Height = height;
            }

            _selectedControl.Background = PropertyBackgroundBox.Text.Trim();
            _selectedControl.Foreground = PropertyForegroundBox.Text.Trim();
            if (TryDesignerNumber(PropertyFontSizeBox, 6, 96, out var fontSize))
            {
                _selectedControl.FontSize = fontSize;
            }

            _selectedControl.ClickEvent = string.IsNullOrWhiteSpace(PropertyEventBox.Text)
                ? null
                : PropertyEventBox.Text.Trim();
            _selectedControl.TextChangedEvent = string.IsNullOrWhiteSpace(PropertyTextChangedEventBox.Text)
                ? null
                : PropertyTextChangedEventBox.Text.Trim();
            _selectedControl.SelectionChangedEvent =
                string.IsNullOrWhiteSpace(PropertySelectionChangedEventBox.Text)
                    ? null
                    : PropertySelectionChangedEventBox.Text.Trim();
            _selectedControl.CheckedChangedEvent =
                string.IsNullOrWhiteSpace(PropertyCheckedChangedEventBox.Text)
                    ? null
                    : PropertyCheckedChangedEventBox.Text.Trim();
            RefreshSelectedControlElement();
        }

        DesignerChanged();
    }

    private bool TryDesignerNumber(TextBox box, double minimum, double maximum, out double value)
    {
        return double.TryParse(box.Text, out value) &&
               double.IsFinite(value) &&
               value >= minimum &&
               value <= maximum;
    }

    private void OnDesignerCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingDesignerProperties || _selectedControl is null)
        {
            return;
        }

        _selectedControl.IsEnabled = PropertyEnabledCheck.IsChecked == true;
        _selectedControl.IsVisible = PropertyVisibleCheck.IsChecked == true;
        _selectedControl.IsChecked = PropertyCheckedCheck.IsChecked == true;
        RefreshSelectedControlElement();
        DesignerChanged();
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
            Title = isForm ? "保存窗体设计" : "保存易语源码",
            Filter = isForm
                ? "窗体设计 (*.yyx)|*.yyx|所有文件 (*.*)|*.*"
                : "易语源码 (*.ey)|*.ey|所有文件 (*.*)|*.*",
            DefaultExt = isForm ? ".yyx" : ".ey",
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

            SetStatus($"已保存 {Path.GetFileName(path)}", ReadyBrush);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
            AddNewDocument(string.Empty);
        }
        else
        {
            DocumentTabs.SelectedIndex = Math.Clamp(index - 1, 0, _documents.Count - 1);
        }

        SetStatus($"已关闭 {document.DisplayName}", ReadyBrush);
        return true;
    }

    private async Task<bool> RunDocumentAsync()
    {
        if (_runCancellation is not null)
        {
            return false;
        }

        var source = _currentDocument?.Text ?? Editor.Text;
        var sourcePath = _currentDocument?.FilePath;
        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        OutputBox.Clear();
        RunInfoText.Text = "正在运行…";
        SetRunningState(true);
        SetStatus("程序运行中", WarningBrush);
        var succeeded = false;

        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                sourcePath,
                line => Dispatcher.Invoke(() => AppendOutput(line)),
                cancellation.Token));

            RefreshOutline(silent: true);
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
            InspectorTabs.SelectedIndex = 1;
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

    private void RefreshOutline(bool silent = false)
    {
        try
        {
            var program = YyRunner.CompileSource(Editor.Text, _currentDocument?.FilePath);
            OutlineTree.Items.Clear();
            var main = new TreeViewItem
            {
                Header = $"入口语句  ·  {program.Main.Statements.Count} 项",
                IsExpanded = true
            };
            OutlineTree.Items.Add(main);

            foreach (var function in program.Functions)
            {
                var parameters = string.Join(", ", function.Parameters.Select(parameter => parameter.Name));
                OutlineTree.Items.Add(new TreeViewItem
                {
                    Header = $"{function.Name}({parameters})  ·  第 {function.Line} 行",
                    Tag = function.Line
                });
            }
        }
        catch (YyDiagnostic)
        {
            if (!silent)
            {
                OutlineTree.Items.Clear();
            }
        }
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
        var fileName = _currentDocument?.DisplayName ?? "未命名.ey";
        var dirty = _currentDocument?.IsDirty == true;
        Title = $"{(dirty ? "● " : string.Empty)}{fileName} — 易语 Studio";
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
            模块：引入 "工具.ey"
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
            ExplorerActivityButton.Background = Brushes.Transparent;
            ExplorerActivityButton.BorderBrush = Brushes.Transparent;
        }
        else
        {
            ExplorerPanel.Visibility = Visibility.Visible;
            ExplorerSplitter.Visibility = Visibility.Visible;
            ExplorerColumn.MinWidth = 180;
            ExplorerColumn.Width = _explorerWidth.Value > 0 ? _explorerWidth : new GridLength(240);
            ExplorerActivityButton.Background = YyFormRenderer.Brush("#2D2D30");
            ExplorerActivityButton.BorderBrush = YyFormRenderer.Brush("#007ACC");
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

    private sealed record CodeSnippet(
        string InsertText,
        int CaretOffset,
        int SelectionLength);

    private sealed record ResizeHandleTag(ResizeDirection Direction);

    private sealed record ProjectControlLink(string FormPath, string ControlName);

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
