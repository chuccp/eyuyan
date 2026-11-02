using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia.Editor;

public sealed partial class StructuredCodeEditor
{
    private void AddAssemblyBlock()
    {
        var widths = new[] { 140d, 38d, 38d, 112d };
        var table = CreateTable();
        AddTableRow(table, ["窗口程序集名", "保 留", "保 留", "备 注"], widths, header: true);
        var row = CreateTableRow();
        AddCells(row,
        [
            EditableCell(_document!.Name, widths[0], value =>
            {
                _document.Name = value.Trim();
                NotifyChanged();
            }),
            BlankCell(widths[1]),
            BlankCell(widths[2]),
            EditableCell(_document.Remark, widths[3], value =>
            {
                _document.Remark = value;
                NotifyChanged();
            }, comment: true)
        ]);
        table.Children.Add(row);
        AddWithSpacing(table, 0);

        // 程序集变量表(官方:窗口程序集表下方的变量名/类型表)。
        var assemblyVars = _document!.MainStatements
            .Where(statement => statement.Kind == YyCodeStatementKinds.Variable)
            .ToList();
        if (assemblyVars.Count > 0)
        {
            AddSubTable(
                [124d, 72d, 40d, 40d, 132d],
                ["变量名", "类 型", "静 态", "数 组", "备 注"],
                assemblyVars.Select(statement => new Control[]
                {
                    EditableCell(statement.Name, 124d, value =>
                    {
                        statement.Name = value.Trim();
                        NotifyChanged();
                    }, blue: true),
                    EditableCell(statement.TypeName, 72d, value =>
                    {
                        statement.TypeName = value.Trim();
                        NotifyChanged();
                    }, blue: true),
                    BooleanCell(statement.IsStatic, 40d, value =>
                    {
                        statement.IsStatic = value;
                        NotifyChanged();
                    }),
                    BooleanCell(statement.IsArray, 40d, value =>
                    {
                        statement.IsArray = value;
                        NotifyChanged();
                    }),
                    EditableCell(statement.Remark, 132d, value =>
                    {
                        statement.Remark = value;
                        NotifyChanged();
                    }, comment: true)
                }));
        }
    }

    private void AddConstantsTable()
    {
        var constants = _document!.MainStatements
            .Where(statement => statement.Kind == YyCodeStatementKinds.Constant)
            .ToList();
        var widths = new[] { 140d, 80d, 180d, 132d };
        AddSubTable(
            widths,
            ["常量名", "类 型", "常量值", "备 注"],
            constants.Select(statement => new Control[]
            {
                EditableCell(statement.Name, widths[0], value =>
                {
                    statement.Name = value.Trim();
                    NotifyChanged();
                }, blue: true),
                EditableCell(statement.TypeName, widths[1], value =>
                {
                    statement.TypeName = value.Trim();
                    NotifyChanged();
                }, blue: true),
                EditableCell(statement.Expression, widths[2], value =>
                {
                    statement.Expression = value.Trim();
                    NotifyChanged();
                }),
                EditableCell(statement.Remark, widths[3], value =>
                {
                    statement.Remark = value;
                    NotifyChanged();
                }, comment: true)
            }));

        AddTableAction("＋ 添加常量", () =>
        {
            var index = _document!.MainStatements.FindLastIndex(
                statement => statement.Kind == YyCodeStatementKinds.Constant);
            _document.MainStatements.Insert(index + 1, new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Constant,
                Name = "新常量",
                TypeName = "文本",
                Expression = "\"\""
            });
            NotifyChanged();
            Rebuild();
        });
    }

    private void AddStructsTables()
    {
        foreach (var structure in _document!.Structs)
        {
            var widths = new[] { 140d, 124d, 96d, 160d };
            var header = CreateTable();
            AddTableRow(
                header,
                ["数据类型名", structure.Name, string.Empty, string.Empty],
                widths,
                header: true);
            AddWithSpacing(header, 0);
            AddSubTable(
                widths.Skip(1).ToArray(),
                ["字段名", "类 型", "默认值"],
                structure.Fields.Select(field => new Control[]
                {
                    EditableCell(field.Name, widths[1], value =>
                    {
                        field.Name = value.Trim();
                        NotifyChanged();
                    }, blue: true),
                    EditableCell(field.TypeName, widths[2], value =>
                    {
                        field.TypeName = value.Trim();
                        NotifyChanged();
                    }, blue: true),
                    EditableCell(field.DefaultExpression, widths[3], value =>
                    {
                        field.DefaultExpression = value.Trim();
                        NotifyChanged();
                    })
                }));
            AddTableAction("＋ 添加字段", () =>
            {
                structure.Fields.Add(new YyCodeStructField
                {
                    Name = "新字段",
                    TypeName = "文本"
                });
                NotifyChanged();
                Rebuild();
            });
        }

        if (_document.Structs.Count == 0)
        {
            _content.Children.Add(new TextBlock
            {
                Text = "暂无自定义数据类型。",
                Margin = new Thickness(6, 8, 0, 0),
                FontSize = 12,
                Foreground = Brush.Parse("#6C707E")
            });
        }

        AddTableAction("＋ 添加数据类型", () =>
        {
            _document.Structs.Add(new YyCodeStruct { Name = "新数据类型" });
            NotifyChanged();
            Rebuild();
        });
    }

    private void AddTableAction(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 6, 0, 0),
            Padding = new Thickness(10, 3)
        };
        button.Click += (_, _) => action();
        _content.Children.Add(button);
    }

    private void RebuildTabStrip()
    {
        _tabStrip.Children.Clear();
        if (_document is null)
        {
            return;
        }

        void AddTab(string header, string mode, bool enabled = true)
        {
            var selected = string.Equals(_viewMode, mode, StringComparison.Ordinal);
            var button = new Button
            {
                Content = header,
                FontFamily = CodeFont,
                FontSize = 12,
                Padding = new Thickness(10, 2),
                Margin = new Thickness(1, 0),
                IsEnabled = enabled,
                Background = selected ? Brushes.White : Brushes.Transparent,
                BorderBrush = selected ? LineBrush : Brushes.Transparent,
                BorderThickness = new Thickness(1, 1, 1, selected ? 0 : 1),
                FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal
            };
            button.Click += (_, _) =>
            {
                _viewMode = mode;
                Rebuild();
            };
            _tabStrip.Children.Add(button);
        }

        AddTab("窗口程序集：" + _document.Name, "main");
        AddTab("[常量数据表]", "constants");
        AddTab("[自定义数据类型表]", "types");
    }

    private void AddParameterTable(YyCodeFunction function)
    {
        var widths = new[] { 108d, 66d, 36d, 36d, 36d, 92d, 112d };
        AddSubTable(
            widths,
            ["参数名", "类 型", "参 考", "可 空", "数 组", "默认值", "备 注"],
            function.Parameters.Select(parameter => new Control[]
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
                }, blue: true),
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
                }, comment: true)
            }));
    }

    private void AddLocalVariableTable(IReadOnlyList<YyCodeStatement> locals)
    {
        var widths = new[] { 124d, 72d, 40d, 40d, 132d };
        AddSubTable(
            widths,
            ["变量名", "类 型", "静 态", "数 组", "备 注"],
            locals.Select(local => new Control[]
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
                }, blue: true),
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
                }, comment: true)
            }));
    }

    private void AddSubTable(
        double[] widths,
        string[] headers,
        IEnumerable<Control[]> rows)
    {
        var table = CreateTable();
        AddTableRow(table, headers, widths, header: true);
        foreach (var cells in rows)
        {
            var row = CreateTableRow();
            foreach (var cell in cells)
            {
                row.Children.Add(cell);
            }

            table.Children.Add(row);
        }

        AddWithSpacing(table, 0);
    }

    private StackPanel CreateTable() => new()
    {
        Orientation = Orientation.Vertical,
        Background = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private static StackPanel CreateTableRow() => new()
    {
        Orientation = Orientation.Horizontal
    };

    private static void AddCells(StackPanel row, IEnumerable<Control> cells)
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
        var row = CreateTableRow();
        for (var index = 0; index < headers.Count; index++)
        {
            row.Children.Add(new Border
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
                    FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal
                }
            });
        }

        table.Children.Add(row);
    }

    private Control EditableCell(
        string text,
        double width,
        Action<string> changed,
        bool blue = false,
        bool comment = false)
    {
        var box = new TextBox
        {
            Text = text,
            Width = Math.Max(1, width - 1),
            Height = 22,
            FontFamily = CodeFont,
            FontSize = 12,
            FontStyle = comment ? FontStyle.Italic : FontStyle.Normal,
            Foreground = comment
                ? new SolidColorBrush(Color.FromRgb(0, 128, 0))
                : blue
                    ? new SolidColorBrush(Color.FromRgb(0, 0, 180))
                    : CodeTextBrush,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 1, 4, 1),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(box, text);
        box.LostFocus += (_, _) =>
        {
            if (!_building)
            {
                changed(FullWidthText.FromDisplay(box.Text));
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                changed(FullWidthText.FromDisplay(box.Text));
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

    private Control BooleanCell(bool value, double width, Action<bool> changed)
    {
        // 官方易语言的表格布尔格:居中勾选框(✓/空白)。
        var check = new CheckBox
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 0,
            Padding = new Thickness(0)
        };
        check.IsCheckedChanged += (_, _) =>
        {
            if (!_building && check.IsChecked == true != value)
            {
                changed(check.IsChecked == true);
            }
        };
        return CellHost(check, width, Brushes.White);
    }

    private static Control CellHost(Control content, double width, IBrush background) =>
        new Border
        {
            Width = width,
            Background = background,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = content
        };

    private static Control BlankCell(double width) =>
        new Border
        {
            Width = width,
            Background = Brushes.White,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1)
        };

    private void AddWithSpacing(Control element, double bottom)
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
}
