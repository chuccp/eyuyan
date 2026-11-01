using System.IO;
using System.Windows;
using System.Windows.Controls;
using Yy.Studio.Forms;

namespace Yy.Studio;

/// <summary>“数据库设计器”对话框:定义数据表结构(表名与列),保存进 .yyproj。</summary>
public sealed class DatabaseDesignerDialog : Window
{
    private readonly List<YyDbTable> _tables;
    private readonly ListBox _tableList = new();
    private readonly StackPanel _columnRows = new() { Orientation = Orientation.Vertical };
    private readonly TextBox _tableNameBox = new() { Width = 160 };
    private readonly Action _changed;

    public DatabaseDesignerDialog(List<YyDbTable> tables, Action changed)
    {
        _tables = tables;
        _changed = changed;
        Title = "数据库设计器";
        Width = 680;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(new TextBlock { Text = "数据表", Margin = new Thickness(0, 0, 0, 6) });
        _tableList.SelectionChanged += (_, _) => ShowTable();
        leftPanel.Children.Add(_tableList);
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        leftButtons.Children.Add(MakeButton("＋ 新建表", OnAddTable));
        leftButtons.Children.Add(MakeButton("删除表", OnRemoveTable));
        leftPanel.Children.Add(leftButtons);
        Grid.SetRow(leftPanel, 0);
        Grid.SetColumn(leftPanel, 0);
        root.Children.Add(leftPanel);

        var rightPanel = new StackPanel();
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        nameRow.Children.Add(new TextBlock { Text = "表名", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        _tableNameBox.TextChanged += (_, _) =>
        {
            if (_tableList.SelectedIndex >= 0 && _tableList.SelectedIndex < _tables.Count)
            {
                _tables[_tableList.SelectedIndex].Name = _tableNameBox.Text.Trim();
                RefreshList(select: _tableList.SelectedIndex);
                _changed();
            }
        };
        nameRow.Children.Add(_tableNameBox);
        rightPanel.Children.Add(nameRow);

        rightPanel.Children.Add(new TextBlock { Text = "列（名称 | 类型 | 备注）", Margin = new Thickness(0, 0, 0, 4) });
        var columnScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _columnRows
        };
        rightPanel.Children.Add(columnScroll);
        var columnButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        columnButtons.Children.Add(MakeButton("＋ 加入列", OnAddColumn));
        columnButtons.Children.Add(MakeButton("删除末列", OnRemoveColumn));
        rightPanel.Children.Add(columnButtons);

        var rightHost = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        rightHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(rightHost, 0);
        Grid.SetColumn(rightHost, 1);
        rightHost.Children.Add(rightPanel);
        root.Children.Add(rightHost);

        var close = new Button
        {
            Content = "关闭",
            Width = 88,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 1);
        Grid.SetColumn(close, 1);
        root.Children.Add(close);
        Content = root;

        RefreshList();
    }

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3) };
        button.Click += onClick;
        return button;
    }

    private void RefreshList(int select = -1)
    {
        var names = _tables.Select(table => table.Name).ToList();
        _tableList.Items.Clear();
        foreach (var name in names)
        {
            _tableList.Items.Add(name);
        }

        if (select >= 0 && select < _tableList.Items.Count)
        {
            _tableList.SelectedIndex = select;
        }
    }

    private void ShowTable()
    {
        _columnRows.Children.Clear();
        if (_tableList.SelectedIndex < 0 || _tableList.SelectedIndex >= _tables.Count)
        {
            _tableNameBox.Text = string.Empty;
            return;
        }

        var table = _tables[_tableList.SelectedIndex];
        _tableNameBox.Text = table.Name;
        foreach (var column in table.Columns)
        {
            _columnRows.Children.Add(BuildColumnRow(column));
        }
    }

    private FrameworkElement BuildColumnRow(YyDbColumn column)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        var nameBox = new TextBox { Width = 150, Text = column.Name };
        nameBox.TextChanged += (_, _) => { column.Name = nameBox.Text.Trim(); _changed(); };
        var typeBox = new TextBox { Width = 110, Text = column.TypeName };
        typeBox.TextChanged += (_, _) => { column.TypeName = typeBox.Text.Trim(); _changed(); };
        var remarkBox = new TextBox { Width = 220, Text = column.Remark };
        remarkBox.TextChanged += (_, _) => { column.Remark = remarkBox.Text; _changed(); };
        row.Children.Add(nameBox);
        row.Children.Add(typeBox);
        row.Children.Add(remarkBox);
        return row;
    }

    private void OnAddTable(object sender, RoutedEventArgs e)
    {
        var name = "表" + (_tables.Count + 1);
        var existing = _tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        while (existing.Contains(name))
        {
            name += "_";
        }

        var table = new YyDbTable { Name = name };
        table.Columns.Add(new YyDbColumn { Name = "编号", TypeName = "整数型" });
        _tables.Add(table);
        RefreshList(select: _tables.Count - 1);
        _changed();
    }

    private void OnRemoveTable(object sender, RoutedEventArgs e)
    {
        if (_tableList.SelectedIndex < 0 || _tableList.SelectedIndex >= _tables.Count)
        {
            return;
        }

        _tables.RemoveAt(_tableList.SelectedIndex);
        _columnRows.Children.Clear();
        _tableNameBox.Text = string.Empty;
        RefreshList();
        _changed();
    }

    private void OnAddColumn(object sender, RoutedEventArgs e)
    {
        if (_tableList.SelectedIndex < 0 || _tableList.SelectedIndex >= _tables.Count)
        {
            return;
        }

        var column = new YyDbColumn { Name = "列" + (_tables[_tableList.SelectedIndex].Columns.Count + 1) };
        _tables[_tableList.SelectedIndex].Columns.Add(column);
        _columnRows.Children.Add(BuildColumnRow(column));
        _changed();
    }

    private void OnRemoveColumn(object sender, RoutedEventArgs e)
    {
        if (_tableList.SelectedIndex < 0 || _tableList.SelectedIndex >= _tables.Count)
        {
            return;
        }

        var table = _tables[_tableList.SelectedIndex];
        if (table.Columns.Count == 0)
        {
            return;
        }

        table.Columns.RemoveAt(table.Columns.Count - 1);
        ShowTable();
        _changed();
    }
}

/// <summary>“数据绑定”对话框:把控件绑定到数据表列,预览时从工作区 数据.json 读取首行填充。</summary>
public sealed class DataBindDialog : Window
{
    private readonly ComboBox _tableCombo = new() { Width = 160 };
    private readonly ComboBox _columnCombo = new() { Width = 160 };
    private readonly List<YyDbTable> _tables;

    public DataBindDialog(YyControlModel control, List<YyDbTable> tables)
    {
        _tables = tables;
        Title = $"数据绑定 - {control.Name}";
        Width = 380;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var hint = new TextBlock
        {
            Text = "把该控件的文本绑定到数据表列；\n窗体预览时从工作区“数据.json”读取首行填充。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(hint, 0);
        grid.Children.Add(hint);

        foreach (var table in _tables)
        {
            _tableCombo.Items.Add(table.Name);
        }

        _tableCombo.SelectionChanged += (_, _) => RefreshColumns();
        var tableRow = MakeRow("数据表", _tableCombo, allowNone: true);
        Grid.SetRow(tableRow, 1);
        grid.Children.Add(tableRow);

        var columnRow = MakeRow("数据列", _columnCombo, allowNone: true);
        Grid.SetRow(columnRow, 2);
        grid.Children.Add(columnRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var ok = new Button { Content = "确定", Width = 84, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            control.DataBindingTable = _tableCombo.SelectedItem?.ToString();
            control.DataBindingColumn = _columnCombo.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(control.DataBindingTable) ||
                string.IsNullOrWhiteSpace(control.DataBindingColumn))
            {
                control.DataBindingTable = null;
                control.DataBindingColumn = null;
            }

            DialogResult = true;
        };
        var clear = new Button { Content = "清除绑定", Width = 84, Margin = new Thickness(0, 0, 10, 0) };
        clear.Click += (_, _) =>
        {
            control.DataBindingTable = null;
            control.DataBindingColumn = null;
            DialogResult = true;
        };
        var cancel = new Button { Content = "取消", Width = 84, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);
        Content = grid;

        if (!string.IsNullOrWhiteSpace(control.DataBindingTable))
        {
            _tableCombo.SelectedItem = control.DataBindingTable;
            RefreshColumns();
            _columnCombo.SelectedItem = control.DataBindingColumn;
        }
    }

    private static FrameworkElement MakeRow(string label, ComboBox combo, bool allowNone)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 70,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (allowNone)
        {
            combo.Items.Add(string.Empty);
        }

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
            var table = _tables.FirstOrDefault(item => string.Equals(item.Name, tableName, StringComparison.Ordinal));
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
}
