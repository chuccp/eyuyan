using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Studio.Avalonia.Features.Projects;

namespace Yy.Studio.Avalonia.Features.Data;

/// <summary>数据库设计器：定义数据表结构（表名与列），修改后由回调写回 .yyproj。</summary>
public sealed class DatabaseDesignerDialog : Window
{
    public DatabaseDesignerDialog(List<YyDbTable> tables, Action? changed = null)
    {
        Title = "数据库设计器";
        Width = 860;
        Height = 560;
        MinWidth = 680;
        MinHeight = 440;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(16)
        };

        var heading = new StackPanel { Spacing = 4, Margin = new Thickness(2, 0, 2, 10) };
        heading.Children.Add(ProjectTheme.Heading("数据库设计器", 18));
        heading.Children.Add(ProjectTheme.Muted(
            "定义数据表与列；每次修改会立即保存到项目清单。"));
        root.Children.Add(heading);

        var card = ProjectTheme.Card(new DatabaseTableEditor(tables, changed), padding: 16);
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var close = ProjectTheme.SecondaryButton("关闭", Close);
        close.IsCancel = true;
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    public static async Task ShowDialog(
        Window owner,
        List<YyDbTable> tables,
        Action? changed = null)
    {
        await new DatabaseDesignerDialog(tables, changed).ShowDialog(owner);
    }

    public static async Task ShowDialog(Window owner, YyProject project)
    {
        await new DatabaseDesignerDialog(
            project.DatabaseTables,
            project.SaveManifest).ShowDialog(owner);
    }
}

/// <summary>数据绑定：把控件文本绑定到数据表列，预览时从工作区“数据.json”读取首行。</summary>
public sealed class DataBindDialog : Window
{
    private readonly ComboBox _tableCombo;
    private readonly ComboBox _columnCombo;
    private readonly List<YyDbTable> _tables;
    private readonly YyControlModel _control;

    public DataBindDialog(YyControlModel control, List<YyDbTable> tables)
    {
        _control = control;
        _tables = tables;
        Title = "数据绑定 - " + control.Name;
        Width = 470;
        Height = 300;
        MinWidth = 420;
        MinHeight = 260;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;

        _tableCombo = ProjectTheme.Combo(Array.Empty<object>());
        _columnCombo = ProjectTheme.Combo(Array.Empty<object>());
        _tableCombo.Items.Add(string.Empty);
        foreach (var table in _tables)
        {
            _tableCombo.Items.Add(table.Name);
        }

        _tableCombo.SelectionChanged += (_, _) => RefreshColumns();
        Content = BuildLayout();

        if (!string.IsNullOrWhiteSpace(control.DataBindingTable))
        {
            _tableCombo.SelectedItem = control.DataBindingTable;
            RefreshColumns();
            _columnCombo.SelectedItem = control.DataBindingColumn;
        }
    }

    public static async Task<bool> ShowDialog(
        Window owner,
        YyControlModel control,
        List<YyDbTable> tables)
    {
        var dialog = new DataBindDialog(control, tables);
        return await dialog.ShowDialog<bool>(owner);
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(18)
        };
        var hint = ProjectTheme.Muted(
            "把该控件的文本绑定到数据表列；\n窗体预览时从工作区“数据.json”读取首行填充。");
        hint.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(hint);

        var tableRow = BuildRow("数据表", _tableCombo);
        Grid.SetRow(tableRow, 1);
        root.Children.Add(tableRow);

        var columnRow = BuildRow("数据列", _columnCombo);
        Grid.SetRow(columnRow, 2);
        root.Children.Add(columnRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 8
        };
        var cancel = ProjectTheme.SecondaryButton("取消", () => Close(false));
        cancel.IsCancel = true;
        var clear = ProjectTheme.SecondaryButton("清除绑定", () =>
        {
            ClearBinding();
            Close(true);
        });
        var ok = ProjectTheme.PrimaryButton("确定", () =>
        {
            ApplyBinding();
            Close(true);
        });
        ok.IsDefault = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        return new Border
        {
            Background = ProjectTheme.CardBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Margin = new Thickness(16),
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            Child = root
        };
    }

    private static Control BuildRow(string label, ComboBox combo)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("70,*"),
            Margin = new Thickness(0, 3)
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ProjectTheme.TextBrush
        });
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        return row;
    }

    private void RefreshColumns()
    {
        var keep = _columnCombo.SelectedItem?.ToString();
        _columnCombo.Items.Clear();
        _columnCombo.Items.Add(string.Empty);
        if (_tableCombo.SelectedItem is string tableName)
        {
            var table = _tables.FirstOrDefault(item =>
                string.Equals(item.Name, tableName, StringComparison.Ordinal));
            if (table is not null)
            {
                foreach (var column in table.Columns)
                {
                    _columnCombo.Items.Add(column.Name);
                }
            }
        }

        if (keep is not null)
        {
            _columnCombo.SelectedItem = keep;
        }
    }

    private void ApplyBinding()
    {
        var table = _tableCombo.SelectedItem as string;
        var column = _columnCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
        {
            table = null;
            column = null;
        }

        _control.DataBindingTable = table;
        _control.DataBindingColumn = column;
    }

    private void ClearBinding()
    {
        _control.DataBindingTable = null;
        _control.DataBindingColumn = null;
    }
}
