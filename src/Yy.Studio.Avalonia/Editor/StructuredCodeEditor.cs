using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Language.Structured;
using Yy.Studio.Avalonia.Highlighting;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 易语言经典代码视图：每个子程序由紧凑方法声明表和其下的连续代码区组成。
/// 执行语句不进入表格，也不绘制逐行分隔线。
/// </summary>
public sealed partial class StructuredCodeEditor : UserControl
{
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(232, 238, 232));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.FromRgb(122, 132, 122));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.FromRgb(246, 250, 255));
    private static readonly IBrush CodeTextBrush = Brushes.Black;
    private static readonly FontFamily CodeFont =
        new("Menlo, Consolas, Microsoft YaHei UI, monospace");

    private readonly StackPanel _content = new();
    private readonly ScrollViewer _scroll = new();
    private readonly DispatcherTimer _editTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly HashSet<YyCodeFunction> _dirtyFunctions = [];
    private readonly List<BodyEditorEntry> _bodyEditors = [];
    private IReadOnlyDictionary<string, string>? _eventAssociations;
    private YyCodeDocument? _document;
    private YyFormModel? _form;
    private bool _building;
    private bool _enableBuiltInCompletion = true;

    public StructuredCodeEditor()
    {
        Background = CodeBrush;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _content.Background = CodeBrush;
        _content.Margin = new Thickness(28, 10, 18, 24);
        _scroll.Content = _content;
        Content = _scroll;
        _editTimer.Tick += (_, _) =>
        {
            _editTimer.Stop();
            FlushPendingEdits();
        };
    }

    private sealed record BodyEditorEntry(
        YyCodeFunction Function,
        TextEditor Editor,
        CodeEditorSession Session);

    public event EventHandler? ModelChanged;

    public event Action<string>? HintRequested;

    public event Action<TextEditor, YyCodeFunction>? CompletionRequested;

    public event Action<TextEditor, YyCodeFunction>? ParameterHintRequested;

    public event Action<TextEditor, YyCodeFunction, YyParameterHint?>? ParameterHintChanged;

    public Func<TextEditor, YyCodeFunction, KeyEventArgs, bool>? ShortcutHandler { get; set; }

    public Func<TextEditor, SearchPanel, ContextMenu>? ContextMenuFactory { get; set; }

    public YyCodeDocument? Document => _document;

    public YyFormModel? Form => _form;

    /// <summary>
    /// 默认由每个正文编辑器自行弹出补全。宿主若通过 CompletionRequested
    /// 自行管理补全，可在 LoadDocument 前设为 false。
    /// </summary>
    public bool EnableBuiltInCompletion
    {
        get => _enableBuiltInCompletion;
        set
        {
            _enableBuiltInCompletion = value;
            foreach (var entry in _bodyEditors)
            {
                entry.Session.Completion.Enabled = value;
            }
        }
    }

    public IReadOnlyList<YyCodeFunction> Functions =>
        _document?.Functions ?? (IReadOnlyList<YyCodeFunction>)Array.Empty<YyCodeFunction>();

    public void SetEventAssociations(IReadOnlyDictionary<string, string>? associations)
    {
        _eventAssociations = associations;
        Rebuild();
    }

    public void SetEventAssociations(YyCodeAssociation? association) =>
        SetEventAssociations(association?.ToRemarkMap());

    public void SetForm(YyFormModel? form)
    {
        _form = form;
        foreach (var entry in _bodyEditors)
        {
            entry.Session.Completion.Environment.Form = form;
        }
    }

    public void LoadDocument(YyCodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        FlushPendingEdits();
        _document = document;
        Rebuild();
    }

    public void LoadDocument(YyCodeDocument document, YyFormModel? form)
    {
        _form = form;
        LoadDocument(document);
    }

    public void Refresh() => Rebuild();

    public void SelectFunction(string functionName)
    {
        var entry = _bodyEditors.FirstOrDefault(pair =>
            string.Equals(pair.Function.Name, functionName, StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        entry.Editor.BringIntoView();
        entry.Editor.Focus();
        entry.Editor.CaretOffset = 0;
    }

    public TextEditor? GetFunctionEditor(string functionName) =>
        _bodyEditors.FirstOrDefault(pair =>
            string.Equals(pair.Function.Name, functionName, StringComparison.Ordinal))?.Editor;

    /// <summary>在保存、切换模式或离开窗口前，把尚未防抖提交的代码写回模型。</summary>
    public void FlushPendingEdits()
    {
        if (_building || _dirtyFunctions.Count == 0)
        {
            return;
        }

        var changed = false;
        var errors = new List<string>();
        foreach (var entry in _bodyEditors
                     .Where(pair => _dirtyFunctions.Contains(pair.Function))
                     .ToArray())
        {
            try
            {
                var body = YyCodeDocumentConverter.FromSource(
                    ".程序集 _方法体\n子程序 _方法体()\n" +
                    entry.Editor.Text +
                    "\n结束子程序\n",
                    "_方法体");
                var locals = entry.Function.Statements
                    .TakeWhile(statement =>
                        statement.Kind == YyCodeStatementKinds.Variable)
                    .ToList();
                entry.Function.Statements.Clear();
                entry.Function.Statements.AddRange(locals);
                entry.Function.Statements.AddRange(
                    body.Functions.Single().Statements);
                _dirtyFunctions.Remove(entry.Function);
                changed = true;
            }
            catch (Exception exception) when (
                exception is YyDiagnostic or YyCodeFormatException)
            {
                errors.Add($"{entry.Function.Name}：{exception.Message}");
            }
        }

        if (errors.Count > 0)
        {
            HintRequested?.Invoke("代码暂未同步：" + string.Join("；", errors));
        }

        if (changed)
        {
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Rebuild()
    {
        if (_document is null)
        {
            return;
        }

        FlushPendingEdits();
        _building = true;
        try
        {
            foreach (var entry in _bodyEditors)
            {
                entry.Session.Detach();
            }

            _bodyEditors.Clear();
            _dirtyFunctions.Clear();
            _content.Children.Clear();
            AddAssemblyBlock();
            foreach (var function in _document.Functions)
            {
                AddFunctionBlock(function);
            }
        }
        finally
        {
            _building = false;
        }
    }

    private void AddFunctionBlock(YyCodeFunction function)
    {
        var widths = new[] { 156d, 86d, 42d, 42d, 112d };
        var table = CreateTable();
        AddTableRow(table, ["子程序名", "返回值类型", "公 开", "易 包", "备 注"], widths, true);

        var methodRow = CreateTableRow();
        var cells = new List<Control>
        {
            EditableCell(function.Name, widths[0], value =>
            {
                function.Name = value.Trim();
                NotifyChanged();
            }, blue: true),
            EditableCell(function.ReturnType, widths[1], value =>
            {
                function.ReturnType = value.Trim();
                NotifyChanged();
            }),
            BooleanCell(function.IsPublic, widths[2], value =>
            {
                function.IsPublic = value;
                NotifyChanged();
            }),
            BooleanCell(function.IsExport, widths[3], value =>
            {
                function.IsExport = value;
                NotifyChanged();
            })
        };
        var remark = function.Remark;
        if (_eventAssociations is not null &&
            _eventAssociations.TryGetValue(function.Name, out var association))
        {
            remark = string.IsNullOrWhiteSpace(remark) ? association : remark + " · " + association;
        }

        cells.Add(EditableCell(remark, widths[4], value =>
        {
            function.Remark = value;
            NotifyChanged();
        }));
        AddCells(methodRow, cells);
        table.Children.Add(methodRow);
        AddWithSpacing(table, 0);

        if (function.Parameters.Count > 0)
        {
            AddParameterTable(function);
        }

        var locals = function.Statements
            .TakeWhile(statement => statement.Kind == YyCodeStatementKinds.Variable)
            .ToList();
        if (locals.Count > 0)
        {
            AddLocalVariableTable(locals);
        }

        var body = CreateBodyEditor(function, locals.Count);
        _content.Children.Add(new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 8, 4),
            Child = body
        });
        _content.Children.Add(new Border { Height = 7, Background = Brushes.Transparent });
    }

    private TextEditor CreateBodyEditor(YyCodeFunction function, int localVariableCount)
    {
        var editor = new TextEditor
        {
            Text = StatementsToSource(function.Statements, localVariableCount),
            Background = Brushes.Transparent,
            Foreground = CodeTextBrush,
            FontFamily = CodeFont,
            FontSize = 13,
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
            MinHeight = 44,
            SyntaxHighlighting = YyHighlighting.Yy,
            WordWrap = false
        };
        var session = CodeEditorSession.Attach(
            editor,
            new CodeEditorSessionOptions
            {
                EnableCompletion = _enableBuiltInCompletion,
                EnableAutoPairing = true,
                EnableFolding = true,
                EnableParameterHint = true,
                EnableFullWidthNormalization = true,
                ShowFoldingMargin = false,
                InstallSearchPanel = true,
                ConfigureEditorOptions = true,
                ShowLineNumbers = false,
                SyntaxHighlighting = YyHighlighting.Yy
            });
        session.SetEnvironment(new CompletionEnvironment
        {
            Form = _form,
            CurrentFunction = function,
            DocumentFunctions = _document?.Functions
        });
        session.ShortcutHandler = (bodyEditor, keyEvent) =>
            ShortcutHandler?.Invoke(bodyEditor, function, keyEvent) ?? false;
        session.HintRequested += text => HintRequested?.Invoke(text);
        session.ParameterHintChanged += hint =>
        {
            ParameterHintChanged?.Invoke(editor, function, hint);
            ParameterHintRequested?.Invoke(editor, function);
        };
        if (ContextMenuFactory is not null && session.SearchPanel is not null)
        {
            editor.ContextMenu = ContextMenuFactory(editor, session.SearchPanel);
        }

        editor.TextArea.TextEntered += (_, e) =>
        {
            if (!_building &&
                !string.IsNullOrEmpty(e.Text) &&
                (char.IsLetter(e.Text[0]) || e.Text[0] is '_' or '.'))
            {
                CompletionRequested?.Invoke(editor, function);
            }
        };
        editor.TextChanged += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            _dirtyFunctions.Add(function);
            editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            UpdateBodyHeight(editor);
            _editTimer.Stop();
            _editTimer.Start();
        };
        editor.TextArea.TextView.BackgroundRenderers.Add(new EasyLanguageFlowRenderer());
        UpdateBodyHeight(editor);
        _bodyEditors.Add(new BodyEditorEntry(function, editor, session));
        return editor;
    }

    private static void UpdateBodyHeight(TextEditor editor)
    {
        editor.Height = Math.Max(52, editor.Document.LineCount * 20 + 18);
    }

    private static string StatementsToSource(
        IReadOnlyCollection<YyCodeStatement> statements,
        int leadingLocalCount)
    {
        if (statements.Count <= leadingLocalCount)
        {
            return string.Empty;
        }

        var document = new YyCodeDocument { Name = "_方法体" };
        document.MainStatements.AddRange(statements.Skip(leadingLocalCount));
        var lines = YyCodeDocumentConverter.ToSource(document)
            .Replace("\r\n", "\n")
            .Split('\n');
        var start = 0;
        while (start < lines.Length &&
               (string.IsNullOrWhiteSpace(lines[start]) ||
                lines[start].StartsWith('.')))
        {
            start++;
        }

        return RemoveCommonIndent(string.Join('\n', lines.Skip(start)).TrimEnd());
    }

    private static string RemoveCommonIndent(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var nonEmpty = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (nonEmpty.Length == 0)
        {
            return string.Empty;
        }

        var indent = nonEmpty.Min(line => line.TakeWhile(char.IsWhiteSpace).Count());
        return string.Join(
            '\n',
            lines.Select(line => line.Length >= indent ? line[indent..] : string.Empty));
    }

    private void NotifyChanged()
    {
        if (!_building)
        {
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
