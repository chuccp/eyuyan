using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Yy.Language;
using Yy.Language.Structured;

namespace Yy.Studio.Structured;

/// <summary>
/// 重新实现的旧版易语言代码窗口：每个子程序由一张方法声明表和其下的连续代码区组成。
/// 执行语句不进入表格，也不绘制逐行分隔线。
/// </summary>
public sealed class EasyLanguageCodeEditor : UserControl
{
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(232, 238, 232));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(122, 132, 122));
    private static readonly Brush CodeBrush = new SolidColorBrush(Color.FromRgb(246, 250, 255));
    private static readonly Brush CodeTextBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));
    private static readonly FontFamily CodeFont = new("SimSun, Microsoft YaHei UI");
    private static readonly Lazy<IHighlightingDefinition?> Highlighting =
        new(LoadHighlighting);

    private readonly StackPanel _content = new();
    private readonly ScrollViewer _scroll = new();
    private readonly DispatcherTimer _editTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly HashSet<YyCodeFunction> _dirtyFunctions = [];
    private readonly List<(YyCodeFunction Function, TextEditor Editor)> _bodyEditors = [];
    private readonly List<(TextEditor Editor, FoldingManager Manager)> _foldings = [];
    private IReadOnlyDictionary<string, string>? _eventAssociations;
    private YyCodeDocument? _document;
    private bool _building;

    public EasyLanguageCodeEditor()
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

    public event EventHandler? ModelChanged;

    public event Action<string>? HintRequested;

    public event Action<TextEditor, YyCodeFunction>? CompletionRequested;

    public event Action<TextEditor, YyCodeFunction>? ParameterHintRequested;

    public Func<TextEditor, YyCodeFunction, KeyEventArgs, bool>? ShortcutHandler { get; set; }

    public Func<TextEditor, SearchPanel, ContextMenu>? ContextMenuFactory { get; set; }

    public YyCodeDocument? Document => _document;

    public void SetEventAssociations(IReadOnlyDictionary<string, string>? associations)
    {
        _eventAssociations = associations;
        Rebuild();
    }

    public void LoadDocument(YyCodeDocument document)
    {
        FlushPendingEdits();
        _document = document;
        Rebuild();
    }

    public void SelectFunction(string functionName)
    {
        var entry = _bodyEditors.FirstOrDefault(pair =>
            string.Equals(
                pair.Function.Name,
                functionName,
                StringComparison.Ordinal));
        if (entry.Editor is null)
        {
            return;
        }

        entry.Editor.BringIntoView();
        entry.Editor.Focus();
        entry.Editor.CaretOffset = 0;
    }

    /// <summary>在保存、切换模式或离开窗口前，把尚未防抖提交的代码写回模型。</summary>
    public void FlushPendingEdits()
    {
        if (_building || _dirtyFunctions.Count == 0)
        {
            return;
        }

        var changed = false;
        var errors = new List<string>();
        foreach (var pair in _bodyEditors
                     .Where(pair => _dirtyFunctions.Contains(pair.Function))
                     .ToArray())
        {
            try
            {
                var body = YyCodeDocumentConverter.FromSource(
                    ".程序集 _方法体\n子程序 _方法体()\n" +
                    pair.Editor.Text +
                    "\n结束子程序\n",
                    "_方法体");
                var locals = pair.Function.Statements
                    .TakeWhile(statement =>
                        statement.Kind == YyCodeStatementKinds.Variable)
                    .ToList();
                pair.Function.Statements.Clear();
                pair.Function.Statements.AddRange(locals);
                pair.Function.Statements.AddRange(
                    body.Functions.Single().Statements);
                _dirtyFunctions.Remove(pair.Function);
                changed = true;
            }
            catch (Exception exception) when (
                exception is YyDiagnostic or YyCodeFormatException)
            {
                errors.Add($"{pair.Function.Name}：{exception.Message}");
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
            foreach (var (editor, manager) in _foldings)
            {
                FoldingManager.Uninstall(manager);
                editor.TextArea.LeftMargins.Clear();
            }

            _foldings.Clear();
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

    private void AddAssemblyBlock()
    {
        var assemblyWidths = new[] { 140d, 38d, 38d, 112d };
        var table = CreateTable(assemblyWidths);
        AddTableRow(
            table,
            ["窗口程序集名", "保 留", "保 留", "备 注"],
            assemblyWidths,
            header: true);
        var assemblyRow = CreateTableRow(assemblyWidths);
        AddCells(assemblyRow, EditableCell(
            _document!.Name,
            assemblyWidths[0],
            value =>
            {
                _document.Name = value.Trim();
                NotifyChanged();
            }), BlankCell(assemblyWidths[1]), BlankCell(assemblyWidths[2]), EditableCell(
            _document.Remark,
            assemblyWidths[3],
            value =>
            {
                _document.Remark = value;
                NotifyChanged();
            }));
        table.Children.Add(assemblyRow);
        AddWithSpacing(table, 4);
    }

    private void AddFunctionBlock(YyCodeFunction function)
    {
        var widths = new[] { 156d, 86d, 42d, 42d, 112d };
        var table = CreateTable(widths);
        AddTableRow(table, ["子程序名", "返回值类型", "公 开", "易 包", "备 注"], widths, true);

        var methodRow = CreateTableRow(widths);
        var methodCells = new List<FrameworkElement>
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

        methodCells.Add(EditableCell(remark, widths[4], value =>
        {
            function.Remark = value;
            NotifyChanged();
        }));
        AddCells(methodRow, methodCells.ToArray());
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
            AddLocalVariableTable(function, locals);
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

    private void AddParameterTable(YyCodeFunction function)
    {
        var widths = new[] { 108d, 66d, 36d, 36d, 36d, 92d, 112d };
        AddSubTable(
            widths,
            ["参数名", "类 型", "参 考", "可 空", "数 组", "默认值", "备 注"],
            function.Parameters.Select(parameter => new FrameworkElement[]
            {
                EditableCell(parameter.Name, widths[0], value =>
                {
                    parameter.Name = value.Trim();
                    NotifyChanged();
                }),
                EditableCell(parameter.TypeName, widths[1], value =>
                {
                    parameter.TypeName = value.Trim();
                    NotifyChanged();
                }),
                BooleanCell(parameter.ByRef, widths[2], value =>
                {
                    parameter.ByRef = value;
                    NotifyChanged();
                }),
                BooleanCell(parameter.Nullable, widths[3], value =>
                {
                    parameter.Nullable = value;
                    NotifyChanged();
                }),
                BooleanCell(parameter.IsArray, widths[4], value =>
                {
                    parameter.IsArray = value;
                    NotifyChanged();
                }),
                EditableCell(parameter.DefaultExpression, widths[5], value =>
                {
                    parameter.DefaultExpression = value.Trim();
                    NotifyChanged();
                }),
                EditableCell(parameter.Remark, widths[6], value =>
                {
                    parameter.Remark = value;
                    NotifyChanged();
                })
            }));
    }

    private void AddLocalVariableTable(
        YyCodeFunction function,
        IReadOnlyList<YyCodeStatement> locals)
    {
        var widths = new[] { 124d, 72d, 40d, 40d, 132d };
        AddSubTable(
            widths,
            ["变量名", "类 型", "静 态", "数 组", "备 注"],
            locals.Select(local => new FrameworkElement[]
            {
                EditableCell(local.Name, widths[0], value =>
                {
                    local.Name = value.Trim();
                    NotifyChanged();
                }),
                EditableCell(local.TypeName, widths[1], value =>
                {
                    local.TypeName = value.Trim();
                    NotifyChanged();
                }),
                BooleanCell(local.IsStatic, widths[2], value =>
                {
                    local.IsStatic = value;
                    NotifyChanged();
                }),
                BooleanCell(local.IsArray, widths[3], value =>
                {
                    local.IsArray = value;
                    NotifyChanged();
                }),
                EditableCell(local.Remark, widths[4], value =>
                {
                    local.Remark = value;
                    NotifyChanged();
                })
            }));
    }

    private void AddSubTable(
        double[] widths,
        string[] headers,
        IEnumerable<FrameworkElement[]> rows)
    {
        var table = CreateTable(widths);
        AddTableRow(table, headers, widths, true);
        foreach (var cells in rows)
        {
            var row = CreateTableRow(widths);
            for (var index = 0; index < cells.Length; index++)
            {
                Grid.SetColumn(cells[index], index);
                row.Children.Add(cells[index]);
            }

            table.Children.Add(row);
        }

        AddWithSpacing(table, 0);
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
            SyntaxHighlighting = Highlighting.Value
        };
        editor.TextArea.IndentationStrategy = new YyIndentationStrategy();
        editor.TextArea.TextEntering += (_, e) =>
        {
            if (!_building)
            {
                YyTextEditing.HandleTextEntering(editor, e);
            }
        };
        editor.TextArea.TextEntered += (_, e) =>
        {
            if (!_building &&
                !string.IsNullOrEmpty(e.Text) &&
                (char.IsLetter(e.Text[0]) || e.Text[0] is '_' or '.'))
            {
                CompletionRequested?.Invoke(editor, function);
            }
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
            ParameterHintRequested?.Invoke(editor, function);
        var searchPanel = SearchPanel.Install(editor.TextArea);
        editor.TextArea.PreviewMouseRightButtonDown += (_, e) =>
        {
            Yy.Studio.CodeEditorContextMenu.MoveCaretToPointer(editor, e);
            editor.TextArea.ContextMenu = ContextMenuFactory?.Invoke(editor, searchPanel);
        };
        editor.TextChanged += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            _dirtyFunctions.Add(function);
            editor.TextArea.TextView.InvalidateLayer(
                ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
            UpdateBodyHeight(editor);
            foreach (var (bodyEditor, manager) in _foldings
                         .Where(pair => ReferenceEquals(pair.Editor, editor)))
            {
                manager.UpdateFoldings(
                    EasyLanguageFoldingStrategy.CreateFoldings(bodyEditor.Document),
                    -1);
            }

            _editTimer.Stop();
            _editTimer.Start();
            ParameterHintRequested?.Invoke(editor, function);
        };
        editor.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (ShortcutHandler?.Invoke(editor, function, e) == true)
            {
                return;
            }
        };
        var foldingManager = FoldingManager.Install(editor.TextArea);
        var foldingMargin = editor.TextArea.LeftMargins
            .OfType<FoldingMargin>()
            .LastOrDefault();
        if (foldingMargin is not null)
        {
            editor.TextArea.LeftMargins.Remove(foldingMargin);
        }

        editor.TextArea.TextView.BackgroundRenderers.Add(new EasyLanguageFlowRenderer());
        foldingManager.UpdateFoldings(
            EasyLanguageFoldingStrategy.CreateFoldings(editor.Document),
            -1);
        _foldings.Add((editor, foldingManager));
        _bodyEditors.Add((function, editor));
        UpdateBodyHeight(editor);
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

    private StackPanel CreateTable(IReadOnlyList<double> widths)
    {
        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private StackPanel CreateTableRow(IReadOnlyList<double> widths)
    {
        return new StackPanel { Orientation = Orientation.Horizontal };
    }

    private static void AddCells(StackPanel row, params FrameworkElement[] cells)
    {
        foreach (var cell in cells)
        {
            row.Children.Add(cell);
        }
    }

    private void AddTableRow(
        StackPanel table,
        IReadOnlyList<string> headers,
        IReadOnlyList<double> widths,
        bool header)
    {
        var row = CreateTableRow(widths);
        for (var index = 0; index < headers.Count; index++)
        {
            var cell = new Border
            {
                Width = widths[index],
                Background = header ? HeaderBrush : Brushes.White,
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = headers[index],
                    FontSize = 12,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
                }
            };
            row.Children.Add(cell);
        }

        table.Children.Add(row);
    }

    private FrameworkElement EditableCell(
        string text,
        double width,
        Action<string> changed,
        bool blue = false)
    {
        var box = new TextBox
        {
            Text = text,
            Width = Math.Max(1, width - 1),
            FontFamily = CodeFont,
            FontSize = 12,
            ToolTip = text,
            Foreground = blue
                ? new SolidColorBrush(Color.FromRgb(0, 0, 180))
                : CodeTextBrush,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 1, 4, 1)
        };
        box.LostFocus += (_, _) => changed(box.Text);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                changed(box.Text);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        if (!blue)
        {
            return CellHost(box, width, Brushes.White);
        }

        var content = new Grid();
        content.Children.Add(box);
        content.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(5, 0, 5, 2),
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 180)),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        });
        return CellHost(content, width, Brushes.White);
    }

    private FrameworkElement BooleanCell(bool value, double width, Action<bool> changed)
    {
        var combo = new ComboBox
        {
            Width = width - 1,
            FontSize = 11,
            Height = 21,
            Padding = new Thickness(1, 0, 1, 0)
        };
        combo.Items.Add("真");
        combo.Items.Add("假");
        combo.SelectedItem = value ? "真" : "假";
        combo.SelectionChanged += (_, _) =>
        {
            if (!_building)
            {
                changed(combo.SelectedItem as string == "真");
            }
        };
        return CellHost(combo, width, Brushes.White);
    }

    private static FrameworkElement CellHost(
        FrameworkElement content,
        double width,
        Brush background) =>
        new Border
        {
            Width = width,
            Background = background,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = content
        };

    private static FrameworkElement BlankCell(double width) =>
        new Border
        {
            Width = width,
            Background = Brushes.White,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1)
        };

    private void AddWithSpacing(UIElement element, double bottom)
    {
        _content.Children.Add(new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = element
        });
        if (bottom > 0)
        {
            _content.Children.Add(new Border
            {
                Height = bottom,
                Background = Brushes.Transparent
            });
        }
    }

    private void NotifyChanged()
    {
        if (!_building)
        {
            ModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IHighlightingDefinition? LoadHighlighting()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(
            "Yy.Studio.Highlighting.Yy.xshd");
        if (stream is null)
        {
            return null;
        }

        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(
            reader,
            HighlightingManager.Instance);
    }
}
