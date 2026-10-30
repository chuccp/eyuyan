using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>常量表/资源表/全局变量/Dll命令 数据表视图(对齐官方数据表,经典表格样式)。</summary>
public sealed class DataTableView : UserControl
{
    private readonly StackPanel _rowsPanel = new();
    private readonly TextBlock _titleText = new();
    private readonly List<(string Header, double Width)> _columns = [];
    private readonly List<Action<object, TextBox>> _binders = [];
    private Func<object>? _factory;
    private Action<object>? _remover;
    private System.Collections.IList? _items;
    private Action? _changed;

    public DataTableView()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _titleText.FontSize = 12;
        _titleText.FontWeight = FontWeights.SemiBold;
        _titleText.Margin = new Thickness(10, 8, 10, 2);
        Grid.SetRow(_titleText, 0);
        root.Children.Add(_titleText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 2, 10, 6)
        };
        buttons.Children.Add(MakeButton("＋ 加入行", OnAddRow));
        buttons.Children.Add(MakeButton("删除末行", OnRemoveRow));
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        var headerHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        headerHost.Content = header;
        Grid.SetRow(headerHost, 2);
        root.Children.Add(headerHost);

        var rowsHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        rowsHost.Content = _rowsPanel;
        Grid.SetRow(rowsHost, 3);
        root.Children.Add(rowsHost);

        Content = root;
        Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));

        _header = header;
    }

    private readonly StackPanel _header;

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 0)
        };
        button.Click += onClick;
        return button;
    }

    public void Load(YyProject project, string kind, Action? changed)
    {
        _changed = changed;
        _header.Children.Clear();
        _rowsPanel.Children.Clear();
        _columns.Clear();
        _binders.Clear();

        switch (kind)
        {
            case "全局变量":
                _titleText.Text = "全局变量表";
                Define("名称", 150);
                Define("类型", 110);
                Define("备注", 260);
                _items = project.Globals;
                _factory = () => new YyGlobalVarEntry();
                _remover = item => project.Globals.Remove((YyGlobalVarEntry)item);
                _binders.Add((item, box) => Bind(box, ((YyGlobalVarEntry)item).Name, text => ((YyGlobalVarEntry)item).Name = text));
                _binders.Add((item, box) => Bind(box, ((YyGlobalVarEntry)item).TypeName, text => ((YyGlobalVarEntry)item).TypeName = text));
                _binders.Add((item, box) => Bind(box, ((YyGlobalVarEntry)item).Remark, text => ((YyGlobalVarEntry)item).Remark = text));
                break;
            case "常量表":
                _titleText.Text = "常量表";
                Define("名称", 150);
                Define("常量值", 220);
                Define("备注", 260);
                _items = project.Constants;
                _factory = () => new YyConstantEntry();
                _remover = item => project.Constants.Remove((YyConstantEntry)item);
                _binders.Add((item, box) => Bind(box, ((YyConstantEntry)item).Name, text => ((YyConstantEntry)item).Name = text));
                _binders.Add((item, box) => Bind(box, ((YyConstantEntry)item).Value, text => ((YyConstantEntry)item).Value = text));
                _binders.Add((item, box) => Bind(box, ((YyConstantEntry)item).Remark, text => ((YyConstantEntry)item).Remark = text));
                break;
            case "资源表":
                _titleText.Text = "资源表";
                Define("名称", 150);
                Define("资源类型", 110);
                Define("文件", 260);
                Define("备注", 220);
                _items = project.ResourceTable;
                _factory = () => new YyResourceEntry();
                _remover = item => project.ResourceTable.Remove((YyResourceEntry)item);
                _binders.Add((item, box) => Bind(box, ((YyResourceEntry)item).Name, text => ((YyResourceEntry)item).Name = text));
                _binders.Add((item, box) => Bind(box, ((YyResourceEntry)item).TypeName, text => ((YyResourceEntry)item).TypeName = text));
                _binders.Add((item, box) => Bind(box, ((YyResourceEntry)item).File, text => ((YyResourceEntry)item).File = text));
                _binders.Add((item, box) => Bind(box, ((YyResourceEntry)item).Remark, text => ((YyResourceEntry)item).Remark = text));
                break;
            case "Dll命令":
                _titleText.Text = "Dll命令定义表";
                Define("名称", 140);
                Define("Dll文件", 150);
                Define("命令名", 140);
                Define("参数", 170);
                Define("返回类型", 100);
                Define("备注", 180);
                _items = project.DllCommands;
                _factory = () => new YyDllCommandEntry();
                _remover = item => project.DllCommands.Remove((YyDllCommandEntry)item);
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).Name, text => ((YyDllCommandEntry)item).Name = text));
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).DllFile, text => ((YyDllCommandEntry)item).DllFile = text));
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).CommandName, text => ((YyDllCommandEntry)item).CommandName = text));
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).Parameters, text => ((YyDllCommandEntry)item).Parameters = text));
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).ReturnType, text => ((YyDllCommandEntry)item).ReturnType = text));
                _binders.Add((item, box) => Bind(box, ((YyDllCommandEntry)item).Remark, text => ((YyDllCommandEntry)item).Remark = text));
                break;
            default:
                _titleText.Text = kind;
                _items = null;
                return;
        }

        foreach (var (header, width) in _columns)
        {
            _header.Children.Add(new Border
            {
                Width = width,
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = new SolidColorBrush(Color.FromRgb(236, 233, 216)),
                Padding = new Thickness(5, 2, 5, 2),
                Child = new TextBlock
                {
                    Text = header,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5
                }
            });
        }

        RebuildRows();
    }

    private void Define(string header, double width)
    {
        _columns.Add((header, width));
    }

    private void Bind(TextBox box, string value, Action<string> setter)
    {
        box.Text = value;
        box.TextChanged += (_, _) =>
        {
            setter(box.Text);
            _changed?.Invoke();
        };
    }

    private void RebuildRows()
    {
        _rowsPanel.Children.Clear();
        if (_items is null)
        {
            return;
        }

        foreach (var item in _items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (var column = 0; column < _columns.Count; column++)
            {
                var box = new TextBox
                {
                    Width = _columns[column].Width,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    Padding = new Thickness(4, 1, 4, 1),
                    FontSize = 11.5
                };
                _binders[column](item, box);
                row.Children.Add(box);
            }

            _rowsPanel.Children.Add(row);
        }
    }

    private void OnAddRow(object sender, RoutedEventArgs e)
    {
        if (_items is null || _factory is null)
        {
            return;
        }

        _items.Add(_factory());
        RebuildRows();
        _changed?.Invoke();
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if (_items is null || _remover is null || _items.Count == 0)
        {
            return;
        }

        _remover(_items[_items.Count - 1]!);
        RebuildRows();
        _changed?.Invoke();
    }
}
