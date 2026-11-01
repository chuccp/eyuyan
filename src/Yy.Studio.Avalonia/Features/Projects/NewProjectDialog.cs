using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Yy.Compiler;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>新建项目：项目名、包名、保存位置、目标平台和 Sources/Resources 预览。</summary>
public sealed class NewProjectDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _packageBox;
    private readonly TextBox _locationBox;
    private readonly ComboBox _targetBox;
    private readonly TextBlock _pathPreview;
    private readonly TextBlock _validationText;
    private bool _updatingPackage;
    private bool _packageEdited;

    public NewProjectDialog(YyProjectKind projectKind = YyProjectKind.Window)
    {
        ProjectKind = projectKind;
        Title = "新建易语项目 · " + KindName(projectKind);
        Width = 680;
        Height = 610;
        MinWidth = 600;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;

        _nameBox = ProjectTheme.Input("我的应用");
        _packageBox = ProjectTheme.Input("myapp");
        _locationBox = ProjectTheme.Input(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _targetBox = ProjectTheme.Combo(ProjectTargetCatalog.All.Cast<object>());
        _targetBox.SelectedIndex = 0;
        _pathPreview = ProjectTheme.Muted(string.Empty);
        _validationText = new TextBlock
        {
            FontSize = 11.5,
            Foreground = ProjectTheme.ErrorBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = BuildLayout();
        _nameBox.TextChanged += OnProjectNameChanged;
        _packageBox.TextChanged += OnPackageChanged;
        _locationBox.TextChanged += (_, _) => UpdatePreview();
        Opened += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
        UpdatePreview();
    }

    public YyProjectKind ProjectKind { get; }

    public YyProject? CreatedProject { get; private set; }

    public string ProjectName => _nameBox.Text?.Trim() ?? string.Empty;

    public string PackageName => _packageBox.Text?.Trim() ?? string.Empty;

    public string TargetPlatform =>
        (_targetBox.SelectedItem as ProjectTargetChoice)?.Id ?? YyBuildTargets.Auto;

    public string ProjectPath
    {
        get
        {
            var location = _locationBox.Text?.Trim() ?? string.Empty;
            var name = ProjectName;
            return string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : Path.Combine(Path.GetFullPath(location), name, name + ".yyproj");
        }
    }

    /// <summary>显示新建项目对话框并返回已创建的项目；取消时返回 null。</summary>
    public static async Task<YyProject?> ShowDialog(
        Window owner,
        YyProjectKind projectKind = YyProjectKind.Window)
    {
        var dialog = new NewProjectDialog(projectKind);
        return await dialog.ShowAsync(owner);
    }

    /// <summary>显示当前实例；成功后返回 <see cref="CreatedProject"/>。</summary>
    public async Task<YyProject?> ShowAsync(Window owner)
    {
        var accepted = await ShowDialog<bool>(owner);
        return accepted ? CreatedProject : null;
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24, 20, 24, 18)
        };

        var heading = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(ProjectTheme.Heading("创建" + KindName(ProjectKind) + "项目"));
        heading.Children.Add(ProjectTheme.Muted(
            "先选择保存位置并填写项目名、包名，确定后才会创建工程。"));
        root.Children.Add(heading);

        var formCard = ProjectTheme.Card(BuildForm(), padding: 20);
        Grid.SetRow(formCard, 1);
        root.Children.Add(formCard);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private Control BuildForm()
    {
        var form = ProjectTheme.Form("128,*");
        ProjectTheme.AddFormRow(form, 0, "项目名", _nameBox);
        ProjectTheme.AddFormRow(form, 1, "包名", _packageBox);
        ProjectTheme.AddFormRow(form, 2, "项目位置", BuildLocationRow());
        ProjectTheme.AddFormRow(form, 3, "源码目录", ProjectTheme.ReadOnlyInput(
            YyProject.SourceDirectoryName + "/"));
        ProjectTheme.AddFormRow(form, 4, "资源目录", ProjectTheme.ReadOnlyInput(
            YyProject.ResourceDirectoryName + "/"));
        ProjectTheme.AddFormRow(form, 5, "目标平台", _targetBox);
        form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var hint = new StackPanel { Spacing = 4 };
        hint.Children.Add(new TextBlock
        {
            Text = "源码与资源分开保存",
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush
        });
        hint.Children.Add(ProjectTheme.Muted(
            "源码子目录可用“包名.目录”导入，例如 引入 \"demo.tools\"。"));
        var structure = new Border
        {
            Background = ProjectTheme.FaceBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 2, 0, 0),
            Child = hint
        };
        Grid.SetRow(structure, 6);
        Grid.SetColumn(structure, 1);
        form.Children.Add(structure);
        return form;
    }

    private Control BuildLocationRow()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(_locationBox);
        var browse = ProjectTheme.SecondaryButton("浏览…", BrowseForFolder);
        browse.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private Control BuildFooter()
    {
        var footer = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        _pathPreview.Margin = new Thickness(0, 0, 0, 8);
        footer.Children.Add(_pathPreview);

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        actions.Children.Add(_validationText);

        var cancel = ProjectTheme.SecondaryButton("取消", () => Close(false));
        cancel.IsCancel = true;
        var create = ProjectTheme.PrimaryButton("创建", OnCreate);
        create.IsDefault = true;
        var buttons = ProjectTheme.ActionRow(cancel, create);
        Grid.SetColumn(buttons, 1);
        actions.Children.Add(buttons);
        Grid.SetRow(actions, 1);
        footer.Children.Add(actions);
        return footer;
    }

    private async void BrowseForFolder()
    {
        try
        {
            if (!StorageProvider.CanPickFolder)
            {
                _validationText.Text = "当前平台不支持文件夹选择，请手动输入项目位置。";
                return;
            }

            IStorageFolder? suggested = null;
            var location = _locationBox.Text?.Trim() ?? string.Empty;
            if (Directory.Exists(location))
            {
                try
                {
                    suggested = await StorageProvider.TryGetFolderFromPathAsync(
                        new Uri(Path.GetFullPath(location)));
                }
                catch (Exception exception) when (
                    exception is UriFormatException or ArgumentException or IOException)
                {
                    suggested = null;
                }
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择项目保存位置",
                AllowMultiple = false,
                SuggestedStartLocation = suggested
            });
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _locationBox.Text = path;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _validationText.Text = "无法打开文件夹选择器：" + exception.Message;
        }
    }

    private void OnProjectNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_packageEdited)
        {
            _updatingPackage = true;
            _packageBox.Text = ToPackageSegment(ProjectName);
            _updatingPackage = false;
        }

        UpdatePreview();
    }

    private void OnPackageChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_updatingPackage)
        {
            _packageEdited = true;
        }
    }

    private void OnCreate()
    {
        _validationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(ProjectName) ||
            !YyFormSerializer.IsIdentifier(ProjectName))
        {
            _validationText.Text = "项目名必须由中文、英文、数字或下划线组成。";
            return;
        }

        if (!Yy.Sdk.YySdk.IsPackageName(PackageName))
        {
            _validationText.Text = "包名格式无效，例如：demo、demo.app、公司.产品。";
            return;
        }

        var location = _locationBox.Text?.Trim() ?? string.Empty;
        if (!Directory.Exists(location))
        {
            _validationText.Text = "项目位置必须是已经存在的文件夹。";
            return;
        }

        try
        {
            var project = YyProject.CreateProject(
                ProjectPath,
                ProjectName,
                PackageName,
                ProjectKind);
            if (!string.Equals(TargetPlatform, YyBuildTargets.Auto, StringComparison.OrdinalIgnoreCase))
            {
                project = ProjectManifestEditor.ApplyTargetPlatform(project, TargetPlatform);
            }

            CreatedProject = project;
            Close(true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidDataException or JsonException)
        {
            _validationText.Text = "无法创建项目：" + exception.Message;
        }
    }

    private void UpdatePreview()
    {
        var location = _locationBox.Text?.Trim() ?? string.Empty;
        var name = ProjectName;
        _pathPreview.Text = string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(name)
            ? "将创建：<项目位置>/<项目名>/<项目名>.yyproj"
            : $"将创建：{Path.Combine(location, name, name + ".yyproj")}";
    }

    private static string ToPackageSegment(string value)
    {
        var characters = value.Trim()
            .Select(character =>
                char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_')
            .ToArray();
        if (characters.Length == 0)
        {
            return "myapp";
        }

        return char.IsLetter(characters[0]) || characters[0] == '_'
            ? new string(characters)
            : "_" + new string(characters);
    }

    private static string KindName(YyProjectKind kind) => kind switch
    {
        YyProjectKind.Console => "控制台",
        YyProjectKind.Module => "模块",
        _ => "窗口"
    };
}
