using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler;
using Yy.Studio.Avalonia.Features.Projects;

namespace Yy.Studio.Avalonia.Features.Data;

/// <summary>数据表结构编辑器：表名与列（名称 | 类型 | 备注），同时供对话框和数据表视图使用。</summary>
public sealed class DatabaseTableEditor : UserControl
{
    private readonly List<YyDbTable> _tables;
    private readonly Action? _changed;
    private readonly ListBox _tableList = new();
    private readonly StackPanel _columnRows = new();
    private readonly TextBox _tableNameBox;
    private bool _updating;

    public DatabaseTableEditor(List<YyDbTable> tables, Action? changed = null)
    {
        _tables = tables;
        _changed = changed;
        _tableNameBox = ProjectTheme.Input();
        _tableNameBox.Watermark = "表名";

        Content = BuildLayout();
        RefreshList();
    }

    public IReadOnlyList<YyDbTable> Tables => _tables;

    private Control BuildLayout()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("230,16,*")
        };

        root.Children.Add(BuildTablePanel());
        var columns = BuildColumnPanel();
        Grid.SetColumn(columns, 2);
        root.Children.Add(columns);
        return root;
    }

    private Control BuildTablePanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        panel.Children.Add(new TextBlock
        {
            Text = "数据表",
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _tableList.Background = Brushes.Transparent;
        _tableList.BorderBrush = ProjectTheme.SoftBorderBrush;
        _tableList.BorderThickness = new Thickness(1);
        _tableList.CornerRadius = new CornerRadius(6);
        _tableList.SelectionChanged += (_, _) =>
        {
            if (!_updating)
            {
                ShowTable();
            }
        };
        Grid.SetRow(_tableList, 1);
        panel.Children.Add(_tableList);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(ProjectTheme.SecondaryButton("＋ 新建表", AddTable));
        buttons.Children.Add(ProjectTheme.SecondaryButton("删除表", RemoveTable));
        Grid.SetRow(buttons, 2);
        panel.Children.Add(buttons);
        return panel;
    }

    private Control BuildColumnPanel()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        var nameRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("60,*"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        nameRow.Children.Add(new TextBlock
        {
            Text = "表名",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ProjectTheme.TextBrush
        });
        _tableNameBox.TextChanged += OnTableNameChanged;
        Grid.SetColumn(_tableNameBox, 1);
        nameRow.Children.Add(_tableNameBox);
        panel.Children.Add(nameRow);

        var columnsLabel = new TextBlock
        {
            Text = "列（名称 | 类型 | 备注）",
            Foreground = ProjectTheme.MutedBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(columnsLabel, 1);
        panel.Children.Add(columnsLabel);

        _columnRows.Spacing = 0;
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _columnRows
        };
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(ProjectTheme.SecondaryButton("＋ 加入列", AddColumn));
        buttons.Children.Add(ProjectTheme.SecondaryButton("删除末列", RemoveColumn));
        Grid.SetRow(buttons, 3);
        panel.Children.Add(buttons);
        return panel;
    }

    private void RefreshList(int select = -1, bool showTable = true)
    {
        _updating = true;
        _tableList.Items.Clear();
        foreach (var table in _tables)
        {
            _tableList.Items.Add(table.Name);
        }

        if (select >= 0 && select < _tableList.Items.Count)
        {
            _tableList.SelectedIndex = select;
        }

        _updating = false;
        if (showTable)
        {
            ShowTable();
        }
    }

    private void ShowTable()
    {
        _updating = true;
        try
        {
            _columnRows.Children.Clear();
            var index = _tableList.SelectedIndex;
            if (index < 0 || index >= _tables.Count)
            {
                _tableNameBox.Text = string.Empty;
                return;
            }

            var table = _tables[index];
            _tableNameBox.Text = table.Name;
            foreach (var column in table.Columns)
            {
                _columnRows.Children.Add(BuildColumnRow(column));
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private Control BuildColumnRow(YyDbColumn column)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,6,120,6,*"),
            Margin = new Thickness(0, 1)
        };
        var nameBox = ProjectTheme.Input(column.Name);
        nameBox.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                column.Name = nameBox.Text?.Trim() ?? string.Empty;
                _changed?.Invoke();
            }
        };
        row.Children.Add(nameBox);

        var typeBox = ProjectTheme.Input(column.TypeName);
        typeBox.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                column.TypeName = typeBox.Text?.Trim() ?? string.Empty;
                _changed?.Invoke();
            }
        };
        Grid.SetColumn(typeBox, 2);
        row.Children.Add(typeBox);

        var remarkBox = ProjectTheme.Input(column.Remark);
        remarkBox.TextChanged += (_, _) =>
        {
            if (!_updating)
            {
                column.Remark = remarkBox.Text ?? string.Empty;
                _changed?.Invoke();
            }
        };
        Grid.SetColumn(remarkBox, 4);
        row.Children.Add(remarkBox);
        return row;
    }

    private void OnTableNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var index = _tableList.SelectedIndex;
        if (index < 0 || index >= _tables.Count)
        {
            return;
        }

        _tables[index].Name = _tableNameBox.Text?.Trim() ?? string.Empty;
        RefreshList(index, showTable: false);
        _changed?.Invoke();
    }

    private void AddTable()
    {
        var name = "表" + (_tables.Count + 1);
        var existing = _tables
            .Select(table => table.Name)
            .ToHashSet(StringComparer.Ordinal);
        while (existing.Contains(name))
        {
            name += "_";
        }

        var table = new YyDbTable { Name = name };
        table.Columns.Add(new YyDbColumn { Name = "编号", TypeName = "整数型" });
        _tables.Add(table);
        RefreshList(select: _tables.Count - 1);
        _changed?.Invoke();
    }

    private void RemoveTable()
    {
        var index = _tableList.SelectedIndex;
        if (index < 0 || index >= _tables.Count)
        {
            return;
        }

        _tables.RemoveAt(index);
        _columnRows.Children.Clear();
        _updating = true;
        _tableNameBox.Text = string.Empty;
        _updating = false;
        RefreshList();
        _changed?.Invoke();
    }

    private void AddColumn()
    {
        var index = _tableList.SelectedIndex;
        if (index < 0 || index >= _tables.Count)
        {
            return;
        }

        var column = new YyDbColumn
        {
            Name = "列" + (_tables[index].Columns.Count + 1)
        };
        _tables[index].Columns.Add(column);
        _columnRows.Children.Add(BuildColumnRow(column));
        _changed?.Invoke();
    }

    private void RemoveColumn()
    {
        var index = _tableList.SelectedIndex;
        if (index < 0 || index >= _tables.Count)
        {
            return;
        }

        var table = _tables[index];
        if (table.Columns.Count == 0)
        {
            return;
        }

        table.Columns.RemoveAt(table.Columns.Count - 1);
        ShowTable();
        _changed?.Invoke();
    }
}
