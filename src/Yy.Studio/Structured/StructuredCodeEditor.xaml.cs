using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Yy.Language.Structured;
using Yy.Studio.Forms;

namespace Yy.Studio.Structured;

/// <summary>
/// 表格化代码编辑器(对齐官方 img056):
/// 程序集表(窗口程序集名|备注)、子程序表(子程序名|返回值类型|公开|备注,可折叠),
/// 展开区含参数表(变量名|类型|参考|可空|数组|默认值|备注)、局部变量表(变量名|类型|静态)与代码体。
/// 代码行空格/双击编辑,未确认显示“草稿”,回车确认(FullWidthText 归一化后解析),F1 提示。
/// </summary>
public partial class StructuredCodeEditor : UserControl
{
    private static readonly Brush FaceBrush = new SolidColorBrush(Color.FromRgb(236, 233, 216));
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(212, 208, 200));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));
    private static readonly Brush DraftBrush = new SolidColorBrush(Color.FromRgb(160, 0, 0));
    private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(0, 0, 128));

    private YyCodeDocument? _document;
    private YyFormModel? _designForm;
    private readonly HashSet<object> _collapsed = [];
    private YyCodeFunction? _selectedFunction;
    private YyCodeStatement? _editing;
    private bool _editingIsNew;
    private TextBox? _editBox;
    private string? _editText;
    private bool _committingEdit;
    private bool _rebuilding;
    private bool _rebuildPending;

    private readonly StackPanel _table = new();
    private readonly ScrollViewer _scroll = new();
    private TextBlock _associationText = new();
    private Button _openDesignerButton = new();

    public StructuredCodeEditor()
    {
        InitializeComponent();
        FontFamily = new FontFamily("SimSun, Microsoft YaHei UI");
        foreach (var brush in new Brush[] { FaceBrush, HeaderBrush, LineBrush, TextBrush, DraftBrush, KeywordBrush })
        {
            brush.Freeze();
        }

        BuildShell();
    }

    public event EventHandler? ModelChanged;

    public event EventHandler<DesignerActivationEventArgs>? DesignerActivationRequested;

    /// <summary>请求在提示夹显示帮助文本(F1 等)。</summary>
    public event Action<string>? HintRequested;

    public YyCodeDocument? Document => _document;

    /// <summary>最近单击的语句行(调试“到光标处”用)。</summary>
    public YyCodeStatement? SelectedStatement { get; private set; }

    /// <summary>当前文档的生成源码文本(搜寻用)。</summary>
    public string PlainText => _document is null
        ? string.Empty
        : YyCodeDocumentConverter.ToSource(_document);

    /// <summary>定位并选中指定语句所在行(搜寻结果跳转用)。</summary>
    public void SelectStatement(YyCodeStatement statement)
    {
        if (_rows.TryGetValue(statement, out var entry))
        {
            SelectedStatement = statement;
            _scroll.ScrollToVerticalOffset(Math.Max(0, entry.Offset - 40));
        }
    }

    private IReadOnlySet<YyCodeStatement>? _breakpointStatements;
    private Border? _selectedRowBorder;
    private IReadOnlyDictionary<string, string>? _eventAssociations;

    /// <summary>
    /// 设置 事件子程序名 → 组件关联描述(如“按钮1 · 被单击”)的映射,
    /// 子程序行显示关联徽标,让代码与组件的对应关系一目了然。
    /// </summary>
    public void SetEventAssociations(IReadOnlyDictionary<string, string>? associations)
    {
        _eventAssociations = associations;
    }

    /// <summary>行选中高亮(官方:单击行出现选中效果,直到点击其他行)。</summary>
    private void HighlightRow(Border host)
    {
        if (ReferenceEquals(_selectedRowBorder, host))
        {
            return;
        }

        if (_selectedRowBorder is not null)
        {
            _selectedRowBorder.Background = Brushes.Transparent;
        }

        _selectedRowBorder = host;
        host.Background = new SolidColorBrush(Color.FromRgb(221, 238, 255));
    }

    /// <summary>设置带断点的语句集合,行首显示红点标记。</summary>
    public void SetBreakpointStatements(IReadOnlySet<YyCodeStatement>? statements)
    {
        _breakpointStatements = statements;
        RebuildRows();
    }

    public void SetDesignContext(YyFormModel? form)
    {
        _designForm = form;
        _associationText.Text = form is null
            ? "关联窗体：(无)"
            : "关联窗体：" + form.Name;
        _openDesignerButton.IsEnabled = form is not null;
    }

    public void LoadDocument(YyCodeDocument document)
    {
        _document = document;
        _editing = null;
        _editingIsNew = false;
        _selectedFunction = document.Functions.FirstOrDefault();
        RebuildRows();
    }

    public void SelectFunction(string functionName)
    {
        if (_document is null)
        {
            return;
        }

        var function = _document.Functions.FirstOrDefault(item =>
            string.Equals(item.Name, functionName, StringComparison.Ordinal));
        if (function is null)
        {
            return;
        }

        _selectedFunction = function;
        _collapsed.Remove(function);
        RebuildRows();
        if (_rows.TryGetValue(function, out var row))
        {
            _scroll.ScrollToVerticalOffset(row.Item1);
        }
    }

    private void BuildShell()
    {
        var root = (Grid)Root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 顶部窄条:关联窗体 + 建模命令
        var strip = new Border
        {
            Background = FaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4)
        };
        var stripPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _associationText = new TextBlock
        {
            Text = "关联窗体：(无)",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        stripPanel.Children.Add(_associationText);
        _openDesignerButton = MakeButton("打开窗体设计器", (_, _) =>
            DesignerActivationRequested?.Invoke(this, new DesignerActivationEventArgs(null)));
        _openDesignerButton.IsEnabled = false;
        stripPanel.Children.Add(_openDesignerButton);
        stripPanel.Children.Add(Separator());
        stripPanel.Children.Add(MakeButton("＋ 子程序", (_, _) => AddFunction()));
        stripPanel.Children.Add(MakeButton("＋ 步骤", (_, _) => AddStatement()));
        stripPanel.Children.Add(MakeButton("＋ 局部变量", (_, _) => AddLocalVariable()));
        var hint = new TextBlock
        {
            Text = "空格/双击编辑行 · 回车确认 · Esc 取消 · F1 帮助 · ＋/－ 折叠",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        stripPanel.Children.Add(hint);
        strip.Child = stripPanel;
        Grid.SetRow(strip, 0);
        root.Children.Add(strip);

        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _table.Orientation = Orientation.Vertical;
        _scroll.Content = _table;
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 18,
        Background = LineBrush,
        Margin = new Thickness(8, 0, 8, 0)
    };

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 11.5
        };
        button.Click += onClick;
        return button;
    }

    // =============== 行渲染 ===============

    private readonly Dictionary<object, (double Offset, FrameworkElement Row)> _rows = [];

    private void RebuildRows()
    {
        // 清空表格会摘除持有焦点的草稿框并触发其 LostFocus 提交,提交又会请求重建;
        // 这里合并重入的重建请求,避免嵌套重建把行渲染两遍或递归。
        if (_rebuilding)
        {
            _rebuildPending = true;
            return;
        }

        _rebuilding = true;
        try
        {
            do
            {
                _rebuildPending = false;
                RebuildRowsCore();
            }
            while (_rebuildPending);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void RebuildRowsCore()
    {
        _table.Children.Clear();
        _rows.Clear();
        _selectedRowBorder = null;
        if (_document is null)
        {
            return;
        }

        AddSectionHeader("窗口程序集");
        AddHeaderRow(["窗口程序集名", "保留", "保留", "备注"], [220, 56, 56, 320]);
        _table.Children.Add(BuildAssemblyRow());

        // 程序集变量表(官方列:变量名|类型|数组|备注),取程序入口开头的变量/常量语句
        var assemblyVars = LeadingAssemblyVars();
        AddHeaderRow(["变量名", "类型", "数组", "备注"], [160, 120, 56, 320]);
        foreach (var variable in assemblyVars)
        {
            AddAssemblyVarRow(variable);
        }

        AddSectionHeader("子程序");
        AddHeaderRow(["", "子程序名", "返回值类型", "公开", "易包", "备注"], [28, 380, 110, 56, 56, 160]);
        foreach (var function in _document.Functions)
        {
            AddFunctionRows(function);
        }

        if (_document.MainStatements.Count > assemblyVars.Count || _selectedFunction is null)
        {
            AddSectionHeader("程序入口");
            foreach (var statement in _document.MainStatements.Where(statement => !assemblyVars.Contains(statement)))
            {
                AddBodyRow(statement, _document.MainStatements, 0);
            }
        }

        // 记录行偏移用于 SelectFunction 定位
        _table.UpdateLayout();
        var offset = 0.0;
        foreach (var child in _table.Children)
        {
            if (child is FrameworkElement element)
            {
                foreach (var key in _rows.Keys.ToList())
                {
                    if (ReferenceEquals(_rows[key].Row, element))
                    {
                        _rows[key] = (offset, element);
                    }
                }

                offset += element.ActualHeight;
            }
        }
    }

    private void AddSectionHeader(string title)
    {
        _table.Children.Add(new Border
        {
            Background = HeaderBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        });
    }

    private void AddHeaderRow(string[] headers, double[] widths)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < headers.Length; i++)
        {
            row.Children.Add(Cell(new TextBlock
            {
                Text = headers[i],
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }, widths[i], HeaderBrush, bold: true));
        }

        _table.Children.Add(new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = row
        });
    }

    private static Border Cell(
        FrameworkElement content,
        double width,
        Brush background,
        bool bold = false,
        bool hasRightBorder = true)
    {
        return new Border
        {
            Width = width,
            Background = background,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, hasRightBorder ? 1 : 0, 1),
            Padding = new Thickness(5, 3, 5, 3),
            Child = content
        };
    }

    private FrameworkElement EditableCell(
        string text,
        double width,
        Action<string> onCommit,
        bool isBool = false)
    {
        if (isBool)
        {
            var combo = new ComboBox
            {
                Width = width - 2,
                FontSize = 12,
                Padding = new Thickness(2, 1, 2, 1)
            };
            combo.Items.Add("真");
            combo.Items.Add("假");
            combo.SelectedItem = text == "真" ? "真" : "假";
            combo.SelectionChanged += (_, _) =>
            {
                if (!_loading)
                {
                    onCommit(combo.SelectedItem as string ?? "假");
                }
            };
            return Cell(combo, width, Brushes.White);
        }

        var box = new TextBox
        {
            Text = text,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
            Padding = new Thickness(0, 1, 0, 1)
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                onCommit(box.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Esc 还原单元格内容(官方行为)
                box.Text = text;
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => onCommit(box.Text);
        return Cell(box, width, Brushes.White);
    }

    private bool _loading;

    private FrameworkElement BuildAssemblyRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _loading = true;
        row.Children.Add(EditableCell(_document!.Name, 220, text =>
        {
            _document.Name = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(new Border
        {
            Width = 56,
            Background = Brushes.White,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1)
        });
        row.Children.Add(new Border
        {
            Width = 56,
            Background = Brushes.White,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1)
        });
        row.Children.Add(EditableCell(_document.Remark, 320, text =>
        {
            _document.Remark = text;
            NotifyChanged();
        }));
        _loading = false;
        return WrapRow(row);
    }

    /// <summary>程序入口开头连续的变量/常量语句(显示于程序集变量表)。</summary>
    private List<YyCodeStatement> LeadingAssemblyVars()
    {
        var variables = new List<YyCodeStatement>();
        foreach (var statement in _document!.MainStatements)
        {
            if (statement.Kind is not (YyCodeStatementKinds.Variable or YyCodeStatementKinds.Constant))
            {
                break;
            }

            variables.Add(statement);
        }

        return variables;
    }

    private void AddAssemblyVarRow(YyCodeStatement variable)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(EditableCell(variable.Name, 160, text =>
        {
            variable.Name = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(variable.TypeName, 120, text =>
        {
            variable.TypeName = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(variable.IsArray ? "真" : "假", 56, text =>
        {
            variable.IsArray = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(variable.Remark, 320, text =>
        {
            variable.Remark = text;
            NotifyChanged();
        }));
        _table.Children.Add(WrapRow(row));
    }

    private void AddFunctionRows(YyCodeFunction function)
    {
        var collapsed = _collapsed.Contains(function);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(FoldGutter(collapsed, () =>
        {
            if (collapsed)
            {
                _collapsed.Remove(function);
            }
            else
            {
                _collapsed.Add(function);
            }

            RebuildRows();
        }));
        var nameLink = new TextBlock
        {
            Text = function.Name,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 200)),
            TextDecorations = TextDecorations.Underline,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "单击定位到该子程序"
        };
        nameLink.MouseLeftButtonDown += (_, _) =>
        {
            _selectedFunction = function;
            _collapsed.Remove(function);
            RebuildRows();
            if (_rows.TryGetValue(function, out var entry))
            {
                _scroll.ScrollToVerticalOffset(Math.Max(0, entry.Offset - 40));
            }
        };
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal };
        nameCell.Children.Add(nameLink);
        if (_eventAssociations is not null &&
            _eventAssociations.TryGetValue(function.Name, out var association))
        {
            nameCell.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(220, 232, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(120, 150, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "▣ " + association,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 80, 160))
                }
            });
        }

        header.Children.Add(Cell(nameCell, 380, Brushes.White));
        header.Children.Add(EditableCell(function.ReturnType, 110, text =>
        {
            function.ReturnType = text.Trim();
            NotifyChanged();
        }));
        header.Children.Add(EditableCell(function.IsPublic ? "真" : "假", 56, text =>
        {
            function.IsPublic = text == "真";
            NotifyChanged();
        }, isBool: true));
        header.Children.Add(EditableCell(function.IsExport ? "真" : "假", 56, text =>
        {
            function.IsExport = text == "真";
            NotifyChanged();
        }, isBool: true));
        header.Children.Add(EditableCell(function.Remark, 160, text =>
        {
            function.Remark = text;
            NotifyChanged();
        }));
        var headerRow = WrapRow(header, select: () => _selectedFunction = function);
        _rows[function] = (0, headerRow);
        _table.Children.Add(headerRow);

        if (collapsed)
        {
            return;
        }

        // 参数表
        AddSubHeader(["变量名", "类型", "参考", "可空", "数组", "默认值", "备注"],
            [160, 110, 50, 50, 50, 130, 240]);
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            AddParameterRow(function, index);
        }

        AddInlineActionRow("＋ 插入参数", () =>
        {
            function.Parameters.Add(new YyCodeParameter
            {
                Name = "参数" + (function.Parameters.Count + 1)
            });
            RebuildRows();
            NotifyChanged();
        });

        // 局部变量表(官方列:变量名|类型|静态|数组|备注)
        var locals = LeadingLocals(function);
        AddSubHeader(["变量名", "类型", "静态", "数组", "备注"], [160, 120, 56, 56, 240]);
        foreach (var local in locals)
        {
            AddLocalRow(local);
        }

        AddInlineActionRow("＋ 插入局部变量 (Ctrl+L)", () => AddLocalVariable());

        // 代码体
        foreach (var statement in function.Statements.Where(statement => !locals.Contains(statement)))
        {
            AddBodyRow(statement, function.Statements, 0);
        }
    }

    private static List<YyCodeStatement> LeadingLocals(YyCodeFunction function)
    {
        var locals = new List<YyCodeStatement>();
        foreach (var statement in function.Statements)
        {
            if (statement.Kind != YyCodeStatementKinds.Variable)
            {
                break;
            }

            locals.Add(statement);
        }

        return locals;
    }

    private void AddParameterRow(YyCodeFunction function, int index)
    {
        var parameter = function.Parameters[index];
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(EditableCell(parameter.Name, 160, text =>
        {
            parameter.Name = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(parameter.TypeName, 110, text =>
        {
            parameter.TypeName = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(parameter.ByRef ? "真" : "假", 50, text =>
        {
            parameter.ByRef = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(parameter.Nullable ? "真" : "假", 50, text =>
        {
            parameter.Nullable = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(parameter.IsArray ? "真" : "假", 50, text =>
        {
            parameter.IsArray = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(parameter.DefaultExpression, 130, text =>
        {
            parameter.DefaultExpression = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(parameter.Remark, 240, text =>
        {
            parameter.Remark = text;
            NotifyChanged();
        }));
        _table.Children.Add(WrapRow(row, indent: 28));
    }

    private void AddLocalRow(YyCodeStatement local)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(EditableCell(local.Name, 160, text =>
        {
            local.Name = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(local.TypeName, 120, text =>
        {
            local.TypeName = text.Trim();
            NotifyChanged();
        }));
        row.Children.Add(EditableCell(local.IsStatic ? "真" : "假", 56, text =>
        {
            local.IsStatic = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(local.IsArray ? "真" : "假", 56, text =>
        {
            local.IsArray = text == "真";
            NotifyChanged();
        }, isBool: true));
        row.Children.Add(EditableCell(local.Remark, 240, text =>
        {
            local.Remark = text;
            NotifyChanged();
        }));
        _table.Children.Add(WrapRow(row, indent: 28));
    }

    private void AddSubHeader(string[] headers, double[] widths)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Width = 28, Height = 1 });
        for (var i = 0; i < headers.Length; i++)
        {
            row.Children.Add(Cell(new TextBlock
            {
                Text = headers[i],
                FontSize = 12
            }, widths[i], HeaderBrush));
        }

        _table.Children.Add(new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = row
        });
    }

    private void AddInlineActionRow(string text, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(28, 1, 0, 1) };
        var button = new Button
        {
            Content = text,
            FontSize = 10.5,
            Padding = new Thickness(8, 1, 8, 1),
            Background = FaceBrush
        };
        button.Click += (_, _) => action();
        row.Children.Add(button);
        _table.Children.Add(row);
    }

    private void AddBodyRow(YyCodeStatement statement, List<YyCodeStatement> siblings, int depth)
    {
        if (ReferenceEquals(_editing, statement))
        {
            AddEditRow(siblings, statement);
            return;
        }

        var isContainer = YyCodeStatementKinds.IsContainer(statement.Kind);
        var collapsed = _collapsed.Contains(statement);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = _breakpointStatements?.Contains(statement) == true ? "●" : " ",
            Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0)),
            Width = 14,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        row.Children.Add(FoldGutter(isContainer && collapsed, () =>
        {
            if (!isContainer)
            {
                return;
            }

            if (collapsed)
            {
                _collapsed.Remove(statement);
            }
            else
            {
                _collapsed.Add(statement);
            }

            RebuildRows();
        }, visible: isContainer));

        var indent = new Border
        {
            Width = 28 + depth * 18,
            Height = 1
        };
        row.Children.Add(indent);

        var display = FullWidthText.ToDisplay(YyCodeDocumentConverter.StatementToSource(statement));
        var text = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Inlines.Add(new Run(display));
        if (!string.IsNullOrWhiteSpace(statement.Remark))
        {
            text.Inlines.Add(new Run("    ' " + statement.Remark)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0))
            });
        }

        row.Children.Add(Cell(text, 720, Brushes.White, hasRightBorder: false));

        var border = WrapRow(row, onDoubleClick: () => BeginEdit(statement, siblings),
            onKeyDown: e => HandleRowKey(e, statement, siblings),
            select: () => SelectedStatement = statement);
        if (ReferenceEquals(SelectedStatement, statement))
        {
            HighlightRow(border);
        }

        _rows[statement] = (0, border);
        _table.Children.Add(border);

        if (isContainer && !collapsed)
        {
            foreach (var child in statement.Children)
            {
                AddBodyRow(child, statement.Children, depth + 1);
            }

            if (statement.ElseChildren.Count > 0)
            {
                var elseRow = new StackPanel { Orientation = Orientation.Horizontal };
                elseRow.Children.Add(new Border { Width = 28 + (depth + 1) * 18, Height = 1 });
                elseRow.Children.Add(Cell(new TextBlock
                {
                    Text = statement.Kind == YyCodeStatementKinds.If ? "否则" : "捕获",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = KeywordBrush
                }, 720, Brushes.White, hasRightBorder: false));
                _table.Children.Add(WrapRow(elseRow));
                foreach (var child in statement.ElseChildren)
                {
                    AddBodyRow(child, statement.ElseChildren, depth + 1);
                }
            }
        }
    }

    private void AddEditRow(List<YyCodeStatement> siblings, YyCodeStatement statement)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Cell(new TextBlock
        {
            Text = "草稿",
            FontSize = 10.5,
            Foreground = DraftBrush,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        }, 46, Brushes.White));
        _editBox = new TextBox
        {
            Text = _editText ?? FullWidthText.ToDisplay(YyCodeDocumentConverter.StatementToSource(statement)),
            FontSize = 12,
            Width = 700,
            BorderThickness = new Thickness(0),
            Background = Brushes.White
        };
        _editBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit(siblings, statement, _editBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEdit(siblings, statement);
                e.Handled = true;
            }
        };
        _editBox.LostFocus += (_, _) =>
        {
            if (ReferenceEquals(_editing, statement))
            {
                CommitEdit(siblings, statement, _editBox.Text);
            }
        };
        row.Children.Add(Cell(_editBox, 700, Brushes.White, hasRightBorder: false));
        _table.Children.Add(WrapRow(row));
        _editBox.Focus();
        _editBox.CaretIndex = _editBox.Text.Length;
    }

    private void HandleRowKey(KeyEventArgs e, YyCodeStatement statement, List<YyCodeStatement> siblings)
    {
        switch (e.Key)
        {
            case Key.Space:
                BeginEdit(statement, siblings);
                e.Handled = true;
                break;
            case Key.Right when YyCodeStatementKinds.IsContainer(statement.Kind):
                _collapsed.Remove(statement);
                RebuildRows();
                e.Handled = true;
                break;
            case Key.Left when YyCodeStatementKinds.IsContainer(statement.Kind):
                _collapsed.Add(statement);
                RebuildRows();
                e.Handled = true;
                break;
            case Key.F1:
                HintRequested?.Invoke("语句：" + FullWidthText.ToDisplay(
                    YyCodeDocumentConverter.StatementToSource(statement)));
                e.Handled = true;
                break;
            case Key.Delete:
                siblings.Remove(statement);
                RebuildRows();
                NotifyChanged();
                e.Handled = true;
                break;
        }
    }

    private void BeginEdit(YyCodeStatement statement, List<YyCodeStatement> siblings)
    {
        _editing = statement;
        _editingIsNew = false;
        _editText = null;
        RebuildRows();
    }

    private void CommitEdit(List<YyCodeStatement> siblings, YyCodeStatement statement, string displayText)
    {
        if (_committingEdit)
        {
            return;
        }

        _committingEdit = true;
        try
        {
            YyCodeStatement parsed;
            try
            {
                parsed = YyCodeDocumentConverter.ParseStatementText(FullWidthText.FromDisplay(displayText));
            }
            catch (YyCodeFormatException exception)
            {
                // 解析失败时绝不能在 LostFocus 处理中同步抢回焦点:
                // 那会与点击目标(菜单、按钮等)的焦点获取互相触发,递归到栈溢出。
                // 草稿行保持原样,单击该行可继续编辑。
                _editText = displayText;
                HintRequested?.Invoke("草稿未确认：" + exception.Message);
                return;
            }

            if (YyCodeStatementKinds.IsContainer(parsed.Kind) &&
                YyCodeStatementKinds.IsContainer(statement.Kind))
            {
                parsed.Children.AddRange(statement.Children);
                parsed.ElseChildren.AddRange(statement.ElseChildren);
            }

            var index = siblings.IndexOf(statement);
            if (index >= 0)
            {
                siblings[index] = parsed;
            }

            _editing = null;
            _editingIsNew = false;
            _editBox = null;
            _editText = null;
            RebuildRows();
            NotifyChanged();
        }
        finally
        {
            _committingEdit = false;
        }
    }

    private void CancelEdit(List<YyCodeStatement> siblings, YyCodeStatement statement)
    {
        if (_editingIsNew)
        {
            siblings.Remove(statement);
        }

        _editing = null;
        _editingIsNew = false;
        _editBox = null;
        _editText = null;
        RebuildRows();
    }

    private FrameworkElement FoldGutter(bool collapsed, Action toggle, bool visible = true)
    {
        var button = new Button
        {
            Content = !visible ? "·" : collapsed ? "▸" : "▾",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 1, 2, 1),
            FontSize = 10,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            Visibility = visible ? Visibility.Visible : Visibility.Visible
        };
        button.Click += (_, _) => toggle();
        return button;
    }

    private Border WrapRow(
        FrameworkElement content,
        Action? onDoubleClick = null,
        Action<KeyEventArgs>? onKeyDown = null,
        Action? select = null,
        double indent = 0)
    {
        var host = new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Focusable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (indent > 0)
        {
            host.Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border { Width = indent, Height = 1 },
                    content
                }
            };
        }
        else
        {
            host.Child = content;
        }

        host.MouseLeftButtonUp += (_, _) =>
        {
            host.Focus();
            select?.Invoke();
            HighlightRow(host);
        };
        if (onDoubleClick is not null)
        {
            host.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    onDoubleClick();
                }
            };
        }

        if (onKeyDown is not null)
        {
            host.KeyDown += (_, e) => onKeyDown(e);
        }

        return host;
    }

    // =============== 建模命令 ===============

    /// <summary>菜单“新子程序”。</summary>
    public void InsertFunction() => AddFunction();

    /// <summary>菜单/Ctrl+L“插入局部变量”。</summary>
    public void InsertLocalVariable() => AddLocalVariable();

    /// <summary>
    /// 支持库夹双击命令:草稿行打开时插入到光标处,否则在当前位置新建一条命令草稿
    /// (对齐官方“双击命令即插入代码”)。
    /// </summary>
    public void InsertCommand(string commandName)
    {
        if (_document is null)
        {
            return;
        }

        var insert = commandName + "()";
        if (_editBox is not null)
        {
            var caret = Math.Clamp(_editBox.CaretIndex, 0, _editBox.Text.Length);
            _editBox.Text = _editBox.Text.Insert(caret, insert);
            _editBox.CaretIndex = caret + insert.Length;
            _editBox.Focus();
            return;
        }

        var siblings = _selectedFunction?.Statements ?? _document.MainStatements;
        var placeholder = new YyCodeStatement
        {
            Kind = YyCodeStatementKinds.Call,
            Expression = insert
        };
        siblings.Add(placeholder);
        _editing = placeholder;
        _editingIsNew = true;
        _editText = insert;
        RebuildRows();
    }

    private void AddFunction()
    {
        if (_document is null)
        {
            return;
        }

        var names = _document.Functions.Select(function => function.Name).ToHashSet(StringComparer.Ordinal);
        var name = "子程序1";
        var sequence = 1;
        while (names.Contains(name))
        {
            name = "子程序" + ++sequence;
        }

        var function = new YyCodeFunction { Name = name };
        _document.Functions.Add(function);
        _selectedFunction = function;
        RebuildRows();
        NotifyChanged();
    }

    private void AddStatement()
    {
        if (_document is null)
        {
            return;
        }

        var siblings = _selectedFunction?.Statements ?? _document.MainStatements;
        var placeholder = new YyCodeStatement
        {
            Kind = YyCodeStatementKinds.Call,
            Expression = string.Empty
        };
        siblings.Add(placeholder);
        _editing = placeholder;
        _editingIsNew = true;
        _editText = null;
        RebuildRows();
    }

    private void AddLocalVariable()
    {
        if (_document is null)
        {
            return;
        }

        var function = _selectedFunction;
        if (function is null)
        {
            HintRequested?.Invoke("请先在子程序表中选择一个子程序，再插入局部变量。");
            return;
        }

        var locals = LeadingLocals(function);
        var name = "变量" + (locals.Count + 1);
        var existing = function.Statements.Select(statement => statement.Name).ToHashSet(StringComparer.Ordinal);
        while (existing.Contains(name))
        {
            name += "_";
        }

        var local = new YyCodeStatement
        {
            Kind = YyCodeStatementKinds.Variable,
            Name = name,
            TypeName = "文本",
            Expression = "\"\""
        };
        function.Statements.Insert(locals.Count, local);
        RebuildRows();
        NotifyChanged();
    }

    private void NotifyChanged() => ModelChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DesignerActivationEventArgs(string? controlName) : EventArgs
{
    public string? ControlName { get; } = controlName;
}
