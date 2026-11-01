using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Yy.Compiler;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>易模块管理：浏览、导入和移除 %AppData%\YyStudio\modules 与工作区 .yymodules。</summary>
public sealed class ModuleManagerDialog : Window
{
    private readonly string _workspaceDirectory;
    private readonly YyProject? _project;
    private readonly ListBox _moduleList = new();
    private readonly TextBlock _detailText = new();
    private readonly List<string> _paths = [];
    private readonly List<string> _referencePaths = [];
    private ListBox? _referenceList;

    public ModuleManagerDialog(string? workspaceDirectory = null, YyProject? project = null)
    {
        _workspaceDirectory = workspaceDirectory ?? string.Empty;
        _project = project;
        Title = "易模块管理";
        Width = 740;
        Height = 500;
        MinWidth = 620;
        MinHeight = 400;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;

        Content = BuildLayout();
        Reload();
    }

    public static async Task ShowDialog(
        Window owner,
        string? workspaceDirectory = null,
        YyProject? project = null)
    {
        var dialog = new ModuleManagerDialog(workspaceDirectory, project);
        await dialog.ShowAsync(owner);
    }

    public Task ShowAsync(Window owner) => base.ShowDialog(owner);

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(16)
        };

        var tabs = new TabControl
        {
            Background = ProjectTheme.CardBrush,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8)
        };
        tabs.Items.Add(new TabItem
        {
            Header = "已安装模块",
            Content = BuildModuleBrowser()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "项目引用",
            Content = BuildReferencePanel()
        });
        root.Children.Add(tabs);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        footer.Children.Add(ProjectTheme.SecondaryButton("关闭", Close));
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildModuleBrowser()
    {
        var browser = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,12,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(10)
        };

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        left.Children.Add(new TextBlock
        {
            Text = "易模块",
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _moduleList.Background = Brushes.Transparent;
        _moduleList.BorderBrush = ProjectTheme.SoftBorderBrush;
        _moduleList.BorderThickness = new Thickness(1);
        _moduleList.CornerRadius = new CornerRadius(6);
        _moduleList.SelectionChanged += (_, _) => ShowDetail();
        Grid.SetRow(_moduleList, 1);
        left.Children.Add(_moduleList);
        browser.Children.Add(left);

        _detailText.FontSize = 12;
        _detailText.Foreground = ProjectTheme.TextBrush;
        _detailText.TextWrapping = TextWrapping.Wrap;
        _detailText.Margin = new Thickness(12);
        var detailHost = new Border
        {
            Background = ProjectTheme.FaceBrush,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _detailText
            }
        };
        Grid.SetColumn(detailHost, 2);
        browser.Children.Add(detailHost);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };
        buttons.Children.Add(ProjectTheme.PrimaryButton("导入…", ImportModule));
        buttons.Children.Add(ProjectTheme.SecondaryButton("移除", RemoveModule));
        buttons.Children.Add(ProjectTheme.SecondaryButton("刷新", Reload));
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        browser.Children.Add(buttons);
        return browser;
    }

    private Control BuildReferencePanel()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8
        };
        panel.Children.Add(ProjectTheme.Heading("项目引用", 16));
        if (_project is null)
        {
            panel.Children.Add(ProjectTheme.Muted(
                "当前没有打开项目。项目引用需要在打开 .yyproj 后查看。"));
            return panel;
        }

        panel.Children.Add(ProjectTheme.Muted(
            "引用保存在 .yyproj 清单中。添加或移除后会立即写入项目清单。"));
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinHeight = 180
        };
        _referencePaths.Clear();
        _referencePaths.AddRange(_project.References);
        if (_referencePaths.Count == 0)
        {
            list.Items.Add("（项目没有声明引用）");
        }
        else
        {
            foreach (var reference in _referencePaths)
            {
                list.Items.Add(Relative(_project.DirectoryPath, reference));
            }
        }

        _referenceList = list;
        panel.Children.Add(list);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var add = ProjectTheme.PrimaryButton("添加引用…", async () => await AddReferenceAsync());
        var remove = ProjectTheme.SecondaryButton("移除引用", () =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= _referencePaths.Count)
            {
                return;
            }

            _project.RemoveReference(_referencePaths[list.SelectedIndex]);
            _referencePaths.RemoveAt(list.SelectedIndex);
            list.Items.RemoveAt(list.SelectedIndex);
            if (list.Items.Count == 0)
            {
                list.Items.Add("（项目没有声明引用）");
            }
        });
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        return panel;
    }

    private async Task AddReferenceAsync()
    {
        if (_project is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "添加项目引用",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("易模块")
                {
                    Patterns = ["*.ec", "*.yymod"]
                },
                FilePickerFileTypes.All
            ]
        });
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            _project.AddReference(path);
            if (!_referencePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _referencePaths.Add(Path.GetFullPath(path));
            }
        }

        if (files.Count > 0)
        {
            ReloadReferenceList();
        }
    }

    private void ReloadReferenceList()
    {
        if (_referenceList is null || _project is null)
        {
            return;
        }

        _referenceList.Items.Clear();
        foreach (var reference in _referencePaths)
        {
            _referenceList.Items.Add(Relative(_project.DirectoryPath, reference));
        }

        if (_referenceList.Items.Count == 0)
        {
            _referenceList.Items.Add("（项目没有声明引用）");
        }
    }

    private IEnumerable<string> ModuleDirectories()
    {
        yield return YyModuleMetadata.ModulesDirectory;
        if (!string.IsNullOrWhiteSpace(_workspaceDirectory))
        {
            yield return Path.Combine(_workspaceDirectory, ".yymodules");
        }
    }

    private void Reload()
    {
        _moduleList.Items.Clear();
        _paths.Clear();
        foreach (var directory in ModuleDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory
                         .EnumerateFiles(directory, "*.ec", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                _paths.Add(path);
                _moduleList.Items.Add(Path.GetFileName(path));
            }
        }

        if (_moduleList.Items.Count == 0)
        {
            _moduleList.Items.Add("（未安装易模块）");
        }

        _detailText.Text = _paths.Count == 0
            ? "使用“程序 → 编译为易模块”导出模块，\n或点击“导入…”安装 .ec 模块文件。"
            : string.Empty;
    }

    private void ShowDetail()
    {
        if (_moduleList.SelectedIndex < 0 ||
            _moduleList.SelectedIndex >= _paths.Count)
        {
            return;
        }

        try
        {
            var module = YyModuleMetadata.Load(_paths[_moduleList.SelectedIndex]);
            var builder = new StringBuilder();
            builder.Append("名称:").AppendLine(module.Name);
            if (!string.IsNullOrWhiteSpace(module.Remark))
            {
                builder.Append("说明:").AppendLine(module.Remark);
            }

            if (!string.IsNullOrWhiteSpace(module.CreatedAt))
            {
                builder.Append("创建时间:").AppendLine(module.CreatedAt);
            }

            builder.AppendLine()
                .Append("公开子程序(")
                .Append(module.Functions.Count)
                .AppendLine("):");
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
                    .Append(function.Name)
                    .Append('(')
                    .Append(parameters)
                    .Append(')');
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

            _detailText.Text = builder.ToString();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or InvalidDataException)
        {
            _detailText.Text = "无法读取模块:" + exception.Message;
        }
    }

    private async void ImportModule()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入易模块",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("易模块")
                    {
                        Patterns = ["*.ec"]
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            });
            if (files.Count == 0)
            {
                return;
            }

            var source = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(source))
            {
                await ProjectTheme.ShowMessageAsync(
                    this,
                    "易模块管理",
                    "无法读取所选模块的本地路径。",
                    error: true);
                return;
            }

            Directory.CreateDirectory(YyModuleMetadata.ModulesDirectory);
            var target = Path.Combine(
                YyModuleMetadata.ModulesDirectory,
                Path.GetFileName(source));
            if (!string.Equals(
                    Path.GetFullPath(source),
                    Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, target, overwrite: true);
            }

            Reload();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ProjectTheme.ShowMessageAsync(
                this,
                "易模块管理",
                "导入失败:" + exception.Message,
                error: true);
        }
    }

    private async void RemoveModule()
    {
        if (_moduleList.SelectedIndex < 0 ||
            _moduleList.SelectedIndex >= _paths.Count)
        {
            return;
        }

        var path = _paths[_moduleList.SelectedIndex];
        var confirmed = await ProjectTheme.ConfirmAsync(
            this,
            "易模块管理",
            $"确定移除易模块“{Path.GetFileName(path)}”吗?\n文件将从模块目录删除。");
        if (!confirmed)
        {
            return;
        }

        try
        {
            File.Delete(path);
            Reload();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            await ProjectTheme.ShowMessageAsync(
                this,
                "易模块管理",
                "移除失败:" + exception.Message,
                error: true);
        }
    }

    private static string Relative(string baseDirectory, string path) =>
        Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
}
