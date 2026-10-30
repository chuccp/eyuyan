using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>“易模块管理”对话框:浏览/导入/移除 %AppData%\YyStudio\modules 与工作区 .yymodules 下的 .ec 模块。</summary>
public sealed class ModuleManagerDialog : Window
{
    private readonly ListBox _list = new();
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly string _workspaceDirectory;
    private readonly List<string> _paths = [];

    public ModuleManagerDialog(string? workspaceDirectory)
    {
        _workspaceDirectory = workspaceDirectory ?? string.Empty;
        Title = "易模块管理";
        Width = 640;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _list.SelectionChanged += (_, _) => ShowDetail();
        Grid.SetRow(_list, 0);
        Grid.SetColumn(_list, 0);
        root.Children.Add(_list);

        var detailHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10, 0, 0, 0)
        };
        var detailScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _detail
        };
        detailHost.Child = detailScroll;
        Grid.SetRow(detailHost, 0);
        Grid.SetColumn(detailHost, 1);
        root.Children.Add(detailHost);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttons.Children.Add(MakeButton("导入…", OnImport));
        buttons.Children.Add(MakeButton("移除", OnRemove));
        var closeButton = MakeButton("关闭", (_, _) => Close());
        closeButton.Margin = new Thickness(16, 0, 0, 0);
        buttons.Children.Add(closeButton);
        Grid.SetRow(buttons, 1);
        Grid.SetColumn(buttons, 0);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        Reload();
    }

    private static Button MakeButton(string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0)
        };
        button.Click += onClick;
        return button;
    }

    private IEnumerable<string> ModuleDirectories()
    {
        yield return YyModule.ModulesDirectory;
        if (!string.IsNullOrWhiteSpace(_workspaceDirectory))
        {
            yield return Path.Combine(_workspaceDirectory, ".yymodules");
        }
    }

    private void Reload()
    {
        _list.Items.Clear();
        _paths.Clear();
        foreach (var directory in ModuleDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.ec", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                _paths.Add(path);
                _list.Items.Add(Path.GetFileName(path));
            }
        }

        if (_list.Items.Count == 0)
        {
            _list.Items.Add("（未安装易模块）");
        }

        _detail.Text = _paths.Count == 0
            ? "使用“程序 → 编译为易模块”导出模块,\n或点击“导入…”安装 .ec 模块文件。"
            : string.Empty;
    }

    private void ShowDetail()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _paths.Count)
        {
            return;
        }

        try
        {
            var module = YyModule.Load(_paths[_list.SelectedIndex]);
            var builder = new System.Text.StringBuilder();
            builder.Append("名称:").AppendLine(module.Name);
            if (!string.IsNullOrWhiteSpace(module.Remark))
            {
                builder.Append("说明:").AppendLine(module.Remark);
            }

            if (!string.IsNullOrWhiteSpace(module.CreatedAt))
            {
                builder.Append("创建时间:").AppendLine(module.CreatedAt);
            }

            builder.AppendLine().Append("公开子程序(").Append(module.Functions.Count).AppendLine("):");
            foreach (var function in module.Functions)
            {
                var parameters = string.Join(
                    ", ",
                    function.Parameters.Select(parameter =>
                        (parameter.ByRef ? "参考 " : string.Empty) +
                        parameter.Name +
                        (string.IsNullOrWhiteSpace(parameter.TypeName)
                            ? string.Empty
                            : ": " + parameter.TypeName)));
                builder.Append("  ")
                    .Append(function.Name).Append('(').Append(parameters).Append(')');
                if (!string.IsNullOrWhiteSpace(function.ReturnType))
                {
                    builder.Append(" : ").Append(function.ReturnType);
                }

                if (!string.IsNullOrWhiteSpace(function.Remark))
                {
                    builder.Append("    // ").Append(function.Remark);
                }

                builder.AppendLine();
            }

            _detail.Text = builder.ToString();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _detail.Text = "无法读取模块:" + exception.Message;
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入易模块",
            Filter = "易模块 (*.ec)|*.ec|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(YyModule.ModulesDirectory);
            var target = Path.Combine(YyModule.ModulesDirectory, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, target, overwrite: true);
            Reload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "导入失败:" + exception.Message, "易模块管理",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _paths.Count)
        {
            return;
        }

        var path = _paths[_list.SelectedIndex];
        var result = MessageBox.Show(
            this,
            $"确定移除易模块“{Path.GetFileName(path)}”吗?\n文件将从模块目录删除。",
            "易模块管理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(path);
            Reload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "移除失败:" + exception.Message, "易模块管理",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
