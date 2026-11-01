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
            })
        ]);
        table.Children.Add(row);
        AddWithSpacing(table, 4);
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
        bool blue = false)
    {
        var box = new TextBox
        {
            Text = text,
            Width = Math.Max(1, width - 1),
            Height = 22,
            FontFamily = CodeFont,
            FontSize = 12,
            Foreground = blue
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
        var combo = new ComboBox
        {
            Width = Math.Max(1, width - 1),
            FontSize = 11,
            Height = 21,
            MinHeight = 0,
            Padding = new Thickness(1, 0, 1, 0),
            ItemsSource = new[] { "真", "假" },
            SelectedIndex = value ? 0 : 1,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_building && (combo.SelectedIndex == 0) != value)
            {
                changed(combo.SelectedIndex == 0);
            }
        };
        return CellHost(combo, width, Brushes.White);
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
