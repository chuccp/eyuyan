using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>项目属性：编辑项目名、包名、目标平台、入口、输出和启动窗体。</summary>
public sealed class ProjectPropertiesWindow : Window
{
    private readonly YyProject _project;
    private readonly TextBox _nameBox;
    private readonly TextBox _packageBox;
    private readonly ComboBox _entryBox;
    private readonly ComboBox _startupBox;
    private readonly ComboBox _targetBox;
    private readonly TextBox _outputBox;
    private readonly TextBlock _validationText;

    public ProjectPropertiesWindow(YyProject project)
    {
        _project = project;
        Title = "项目属性";
        Width = 680;
        Height = 650;
        MinWidth = 560;
        MinHeight = 480;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;

        _nameBox = ProjectTheme.Input(project.Name);
        _packageBox = ProjectTheme.Input(project.PackageName);

        var entryItems = project.SourcePaths
            .Select(path => (object)Relative(project.DirectoryPath, path))
            .ToList();
        var entry = Relative(project.DirectoryPath, project.EntryPath);
        if (!entryItems.Contains(entry))
        {
            entryItems.Insert(0, entry);
        }

        _entryBox = ProjectTheme.Combo(entryItems, entry);
        _startupBox = ProjectTheme.Combo(project.Forms
            .Select(form => (object)form.Model.Name)
            .ToList());
        _startupBox.IsEditable = true;
        _startupBox.Text = project.StartupForm;
        _targetBox = ProjectTheme.Combo(ProjectTargetCatalog.All.Cast<object>());
        _targetBox.SelectedItem = ProjectTargetCatalog.Resolve(
            ProjectManifestEditor.ReadTargetPlatform(project));
        _outputBox = ProjectTheme.Input(project.OutputName);
        _validationText = new TextBlock
        {
            FontSize = 11.5,
            Foreground = ProjectTheme.ErrorBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        Content = BuildLayout();
    }

    public YyProject? UpdatedProject { get; private set; }

    /// <summary>显示项目属性并返回重新加载后的项目；取消时返回 null。</summary>
    public static async Task<YyProject?> ShowDialog(Window owner, YyProject project)
    {
        var dialog = new ProjectPropertiesWindow(project);
        return await dialog.ShowAsync(owner);
    }

    public async Task<YyProject?> ShowAsync(Window owner)
    {
        var accepted = await ShowDialog<bool>(owner);
        return accepted ? UpdatedProject : null;
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("46,*,54") };
        root.Children.Add(BuildHeader());

        var body = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = BuildBody()
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildHeader()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0),
            Spacing = 10
        };
        header.Children.Add(new TextBlock
        {
            Text = "项目属性",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush
        });
        header.Children.Add(new TextBlock
        {
            Text = "项目清单 .yyproj",
            FontSize = 10,
            Foreground = ProjectTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = ProjectTheme.CardBrush,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header
        };
    }

    private Control BuildBody()
    {
        var stack = new StackPanel { Margin = new Thickness(20, 16), Spacing = 0 };

        stack.Children.Add(ProjectTheme.Card(BuildFields(), padding: 18));
        return stack;
    }

    private Control BuildFields()
    {
        var fields = new StackPanel { Spacing = 0 };
        AddLabeled(fields, "项目名称", _nameBox);
        AddLabeled(fields, "包名", _packageBox);
        AddLabeled(fields, "项目文件", ProjectTheme.ReadOnlyInput(_project.ProjectPath));

        var directories = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,14,*"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        directories.Children.Add(FieldBlock("源码目录", ProjectTheme.ReadOnlyInput(
            YyProject.SourceDirectoryName + "/")));
        var resources = FieldBlock("资源目录", ProjectTheme.ReadOnlyInput(
            YyProject.ResourceDirectoryName + "/"));
        Grid.SetColumn(resources, 2);
        directories.Children.Add(resources);
        fields.Children.Add(directories);

        var entries = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,14,*"),
            Margin = new Thickness(0, 0, 0, 0)
        };
        var entryBlock = FieldBlock("入口源码", _entryBox);
        entryBlock.Margin = new Thickness(0, 0, 0, 14);
        entries.Children.Add(entryBlock);
        var startupBlock = FieldBlock("启动窗体", _startupBox);
        Grid.SetColumn(startupBlock, 2);
        startupBlock.Margin = new Thickness(0, 0, 0, 14);
        entries.Children.Add(startupBlock);
        fields.Children.Add(entries);

        AddLabeled(fields, "目标平台", _targetBox);
        AddLabeled(fields, "输出文件名", _outputBox);

        var summary = new StackPanel { Spacing = 3 };
        summary.Children.Add(new TextBlock
        {
            Text = $"包含 {_project.SourcePaths.Count} 个源码文件、" +
                   $"{_project.Forms.Count} 个窗体和 {_project.ResourceTable.Count} 个资源。",
            FontSize = 10.5,
            Foreground = ProjectTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        summary.Children.Add(new TextBlock
        {
            Text = $"项目包：{_project.PackageName}；命名导入格式：{_project.PackageName}.子目录。",
            FontSize = 10.5,
            Foreground = ProjectTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap
        });
        fields.Children.Add(ProjectTheme.HintCard(summary, padding: 12));
        fields.Children.Add(_validationText);
        return fields;
    }

    private Control BuildFooter()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0),
            Spacing = 8
        };
        var cancel = ProjectTheme.SecondaryButton("取消", () => Close(false));
        cancel.IsCancel = true;
        var save = ProjectTheme.PrimaryButton("保存", OnSave);
        save.IsDefault = true;
        panel.Children.Add(cancel);
        panel.Children.Add(save);
        return new Border
        {
            Background = ProjectTheme.CardBrush,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = panel
        };
    }

    private void OnSave()
    {
        _validationText.Text = string.Empty;
        try
        {
            var entry = _entryBox.SelectedItem as string ?? _entryBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new InvalidOperationException("请选择项目入口源码。");
            }

            _project.UpdateSettings(
                _nameBox.Text ?? string.Empty,
                _packageBox.Text ?? string.Empty,
                entry,
                _outputBox.Text ?? string.Empty,
                _startupBox.Text?.Trim() ?? string.Empty);
            var target = (_targetBox.SelectedItem as ProjectTargetChoice)?.Id
                         ?? YyBuildTargets.Auto;
            var project = ProjectManifestEditor.ApplyTargetPlatform(_project, target);
            UpdatedProject = YyProject.LoadForDirectory(project.DirectoryPath)
                ?? throw new InvalidDataException("项目属性已保存，但项目清单无法重新读取。");
            Close(true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidDataException or InvalidOperationException or JsonException)
        {
            _validationText.Text = exception.Message;
        }
    }

    private static StackPanel FieldBlock(string label, Control input)
    {
        var block = new StackPanel { Spacing = 0 };
        block.Children.Add(ProjectTheme.FieldLabel(label));
        block.Children.Add(input);
        return block;
    }

    private static void AddLabeled(StackPanel fields, string label, Control input)
    {
        fields.Children.Add(ProjectTheme.FieldLabel(label));
        input.Margin = new Thickness(0, 0, 0, 14);
        fields.Children.Add(input);
    }

    private static string Relative(string baseDirectory, string path) =>
        Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
}
