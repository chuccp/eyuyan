using System.Runtime.CompilerServices;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>创建代码编辑器会话时可调整的能力开关。</summary>
public sealed class CodeEditorSessionOptions
{
    public bool EnableCompletion { get; init; } = true;
    public bool EnableAutoPairing { get; init; } = true;
    public bool EnableFolding { get; init; } = true;
    public bool EnableParameterHint { get; init; } = true;
    public bool EnableFullWidthNormalization { get; init; } = true;
    public bool ShowFoldingMargin { get; init; } = true;
    public bool InstallSearchPanel { get; init; } = true;
    public bool ConfigureEditorOptions { get; init; } = true;
    public bool? ShowLineNumbers { get; init; } = true;
    public IHighlightingDefinition? SyntaxHighlighting { get; init; }
}

/// <summary>
/// 包装 AvaloniaEdit.TextEditor：统一配置语法着色、行号、折叠、搜索、缩进、
/// 自动配对、全角归一化和补全，并向宿主发布参数提示与帮助事件。
/// </summary>
public sealed class CodeEditorSession
{
    private static readonly ConditionalWeakTable<TextEditor, CodeEditorSession> Sessions = new();

    private readonly CodeEditorSessionOptions _options;
    private readonly DispatcherTimer? _foldingTimer;
    private bool _detached;

    private CodeEditorSession(
        TextEditor editor,
        CodeEditorSessionOptions options)
    {
        Editor = editor;
        _options = options;
        Completion = new CompletionService(editor)
        {
            Enabled = options.EnableCompletion
        };
        if (options.EnableFolding)
        {
            _foldingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _foldingTimer.Tick += (_, _) =>
            {
                _foldingTimer.Stop();
                RefreshFoldings();
            };
        }
    }

    public TextEditor Editor { get; }

    public CompletionService Completion { get; }

    public SearchPanel? SearchPanel { get; private set; }

    public FoldingManager? FoldingManager { get; private set; }

    public Func<TextEditor, KeyEventArgs, bool>? ShortcutHandler { get; set; }

    /// <summary>F1 或 Ctrl+Space 之外的上下文帮助文本。</summary>
    public event Action<string>? HintRequested;

    public event Action<YyParameterHint?>? ParameterHintChanged;

    public YyParameterHint? CurrentParameterHint { get; private set; }

    /// <summary>为编辑器安装全部编辑辅助；重复调用返回同一会话。</summary>
    public static CodeEditorSession Attach(
        TextEditor editor,
        CodeEditorSessionOptions? options = null)
    {
        if (Sessions.TryGetValue(editor, out var existing))
        {
            return existing;
        }

        var session = new CodeEditorSession(editor, options ?? new CodeEditorSessionOptions());
        Sessions.Add(editor, session);
        session.Initialize();
        return session;
    }

    public static CodeEditorSession? TryGet(TextEditor editor) =>
        Sessions.TryGetValue(editor, out var session) ? session : null;

    /// <summary>更新补全上下文（窗体、当前子程序、用户子程序列表等）。</summary>
    public void SetEnvironment(CompletionEnvironment environment)
    {
        Completion.Environment = environment;
        PublishParameterHint();
    }

    public void ShowCompletion()
    {
        Completion.ShowCompletion();
    }

    public void RefreshFoldings()
    {
        if (_detached || FoldingManager is null)
        {
            return;
        }

        try
        {
            FoldingManager.UpdateFoldings(
                YyFoldingStrategy.CreateFoldings(Editor.Document),
                firstErrorOffset: -1);
        }
        catch (InvalidOperationException)
        {
            // 视图尚未完成一次布局时跳过；后续文本变化会重试。
        }
    }

    public void ScheduleFoldingUpdate()
    {
        if (_detached || _foldingTimer is null)
        {
            return;
        }

        _foldingTimer.Stop();
        _foldingTimer.Start();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        Sessions.Remove(Editor);
        _foldingTimer?.Stop();
        Editor.TextArea.TextEntering -= OnTextEntering;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        Editor.TextChanged -= OnTextChanged;
        Editor.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        Completion.Detach();
        if (FoldingManager is not null)
        {
            FoldingManager.Uninstall(FoldingManager);
            FoldingManager = null;
        }
    }

    private void Initialize()
    {
        if (_options.ConfigureEditorOptions)
        {
            Editor.Options.ConvertTabsToSpaces = true;
            Editor.Options.IndentationSize = 4;
            Editor.Options.EnableHyperlinks = false;
            Editor.Options.EnableEmailHyperlinks = false;
            Editor.TextArea.IndentationStrategy = new YyIndentationStrategy();
        }

        if (_options.ShowLineNumbers is { } showLineNumbers)
        {
            Editor.ShowLineNumbers = showLineNumbers;
        }

        if (_options.SyntaxHighlighting is not null)
        {
            Editor.SyntaxHighlighting = _options.SyntaxHighlighting;
        }

        if (_options.InstallSearchPanel)
        {
            SearchPanel = SearchPanel.Install(Editor);
        }

        if (_options.EnableFolding)
        {
            FoldingManager = FoldingManager.Install(Editor.TextArea);
            if (!_options.ShowFoldingMargin)
            {
                var margin = Editor.TextArea.LeftMargins
                    .OfType<FoldingMargin>()
                    .LastOrDefault();
                if (margin is not null)
                {
                    Editor.TextArea.LeftMargins.Remove(margin);
                }
            }

            RefreshFoldings();
        }

        Completion.Attach();
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextChanged += OnTextChanged;
        Editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (Editor.IsReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (_options.EnableFullWidthNormalization)
        {
            var normalized = YyTextEditing.NormalizeFullWidthInput(e.Text);
            if (normalized != e.Text)
            {
                e.Text = normalized;
            }
        }

        if (_options.EnableAutoPairing)
        {
            YyTextEditing.HandleTextEntering(Editor, e);
        }
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        ScheduleFoldingUpdate();
        PublishParameterHint();
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) =>
        PublishParameterHint();

    private void PublishParameterHint()
    {
        if (_detached || !_options.EnableParameterHint)
        {
            return;
        }

        CurrentParameterHint = Completion.GetParameterHint();
        ParameterHintChanged?.Invoke(CurrentParameterHint);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ShortcutHandler?.Invoke(Editor, e) == true || e.Handled)
        {
            return;
        }

        if (Editor.IsReadOnly)
        {
            if (e.Key == Key.F1)
            {
                HintRequested?.Invoke(BuildHelpText());
                e.Handled = true;
            }

            return;
        }

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                      e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command && e.Key == Key.Space)
        {
            ShowCompletion();
            e.Handled = true;
            return;
        }

        if (command && e.Key is Key.Oem2 or Key.OemQuestion)
        {
            YyTextEditing.ToggleComment(Editor);
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.D)
        {
            YyTextEditing.DuplicateLine(Editor);
            e.Handled = true;
            return;
        }

        if (command && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.K)
        {
            YyTextEditing.DeleteLine(Editor);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            HintRequested?.Invoke(BuildHelpText());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            YyTextEditing.HandleBackspace(Editor, e);
        }
    }

    private string BuildHelpText()
    {
        const string footer = "\n\nF1：上下文帮助；Ctrl+Space：命令补全。";
        var text = Editor.Text;
        var caret = Editor.CaretOffset;
        var word = YyTextEditing.GetWordAtOffset(text, caret);
        var form = Completion.Environment.Form;
        var owner = CompletionService.TryGetMemberOwnerName(text, caret);

        if (form is not null && owner is not null && !string.IsNullOrWhiteSpace(word))
        {
            var control = form.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, owner, StringComparison.Ordinal));
            if (control is not null && IsControlProperty(word))
            {
                return $"{control.Name}.{word}\n\n" +
                    $"组件类型：{control.Type}\n" +
                    $"当前值：{GetControlPropertySummary(control, word)}\n\n" +
                    "可使用赋值语句直接修改属性，例如：\n" +
                    $"{control.Name}.{word} = 新值" +
                    footer;
            }

            if ((string.Equals(owner, form.Name, StringComparison.Ordinal) ||
                 string.Equals(owner, "窗口", StringComparison.Ordinal)) &&
                IsFormProperty(word))
            {
                return $"{form.Name}.{word}\n\n当前值：{GetFormPropertySummary(form, word)}" + footer;
            }
        }

        if (form is not null && !string.IsNullOrWhiteSpace(word))
        {
            var control = form.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, word, StringComparison.Ordinal));
            if (control is not null)
            {
                return $"组件：{control.Name}\n" +
                    $"类型：{control.Type}\n" +
                    $"位置：左 {control.X}，上 {control.Y}；尺寸：{control.Width} × {control.Height}\n\n" +
                    "可引用属性：文本、名称、可见、启用、选中、左、上、宽、高。\n" +
                    $"示例：{control.Name}.文本 = \"新值\"" +
                    footer;
            }
        }

        var callable = Completion.DescribeCallable(word);
        if (callable is not null)
        {
            return callable + footer;
        }

        var keyword = CompletionCatalog.FindKeyword(word);
        if (keyword is { Text: not null })
        {
            return keyword.Value.Text + "\n\n" + keyword.Value.Description + footer;
        }

        var hint = Completion.GetParameterHint();
        return hint is not null
            ? $"{hint.Signature}    当前参数 {hint.ActiveParameter}    {hint.Description}"
            : "将光标移到命令、函数或组件名上查看帮助。按 F1 打开提示，按 Ctrl+Space 打开补全。";
    }

    private static bool IsControlProperty(string word) =>
        word is "文本" or "名称" or "可见" or "启用" or "选中" or "左" or "上" or "宽" or "高";

    private static bool IsFormProperty(string word) =>
        word is "标题" or "宽" or "高" or "背景" or "可见";

    private static string GetControlPropertySummary(
        Yy.Compiler.Forms.YyControlModel control,
        string property) => property switch
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

    private static string GetFormPropertySummary(
        Yy.Compiler.Forms.YyFormModel form,
        string property) => property switch
    {
        "标题" => form.Title,
        "宽" => form.Width.ToString(),
        "高" => form.Height.ToString(),
        "背景" => form.Background,
        "可见" => "真",
        _ => "可通过“窗体名.属性名”访问"
    };
}
