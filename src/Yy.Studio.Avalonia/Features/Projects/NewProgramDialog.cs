using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>IDEA 式“新建”选择器：空白窗口程序、控制台程序、模块、示例或打开文件。</summary>
public sealed class NewProgramDialog : Window
{
    public enum ProgramKind
    {
        None,
        Window,
        Console,
        Module,
        Project,
        OpenFile,
        Sample
    }

    private readonly ListBox _typeList = new();
    private readonly CheckBox _suppressBox = new();
    private readonly Button _primaryButton;
    private readonly TextBlock _hintText;
    private ProgramKind _kind = ProgramKind.Window;
    private string? _samplePath;

    public NewProgramDialog()
    {
        Title = "新建";
        Width = 720;
        Height = 560;
        MinWidth = 620;
        MinHeight = 480;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;
        ShowInTaskbar = false;

        _primaryButton = ProjectTheme.PrimaryButton("确定", Accept);
        _primaryButton.IsDefault = true;
        _hintText = ProjectTheme.Muted(string.Empty);
        Content = BuildLayout();

        _typeList.SelectedIndex = 0;
    }

    public ProgramKind SelectedKind => _kind;

    public string? SelectedSamplePath => _samplePath;

    public bool SuppressOnStartup => _suppressBox.IsChecked == true;

    /// <summary>只显示选择器，返回用户选择；项目创建由调用方或 ShowAndCreateAsync 完成。</summary>
    public static async Task<NewProgramResult?> ShowAsync(Window owner)
    {
        var dialog = new NewProgramDialog();
        return await dialog.ShowDialog<NewProgramResult?>(owner);
    }

    /// <summary>选择项目类型后继续显示新建项目对话框，并返回创建结果。</summary>
    public static async Task<NewProgramResult?> ShowAndCreateAsync(Window owner)
    {
        var result = await ShowAsync(owner);
        if (result is null)
        {
            return null;
        }

        if (NeedsProject(result.Kind))
        {
            var project = await CreateProjectAsync(owner, result);
            result = result with { Project = project };
        }

        return result;
    }

    public static async Task<YyProject?> CreateProjectAsync(
        Window owner,
        NewProgramResult result)
    {
        var projectKind = result.Kind switch
        {
            ProgramKind.Window => YyProjectKind.Window,
            ProgramKind.Console => YyProjectKind.Console,
            ProgramKind.Module => YyProjectKind.Module,
            ProgramKind.Project => YyProjectKind.Window,
            _ => (YyProjectKind?)null
        };
        return projectKind is null
            ? null
            : await NewProjectDialog.ShowDialog(owner, projectKind.Value);
    }

    private static bool NeedsProject(ProgramKind kind) =>
        kind is ProgramKind.Window or ProgramKind.Console or ProgramKind.Module or ProgramKind.Project;

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(20, 18, 20, 16)
        };

        var heading = new StackPanel { Spacing = 5, Margin = new Thickness(2, 0, 2, 14) };
        heading.Children.Add(ProjectTheme.Heading("新建", 20));
        heading.Children.Add(ProjectTheme.Muted(
            "选择要创建的易语程序类型，或从示例和已有文件开始。"));
        root.Children.Add(heading);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,12,*")
        };
        var navigation = ProjectTheme.Card(BuildNavigationTree(), padding: 10);
        body.Children.Add(navigation);

        var types = ProjectTheme.Card(BuildTypeList(), padding: 10);
        Grid.SetColumn(types, 2);
        body.Children.Add(types);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildNavigationTree()
    {
        var tree = new TreeView
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2)
        };

        var blank = new TreeViewItem
        {
            Header = "空白程序",
            Tag = ProgramKind.Window,
            IsExpanded = true
        };
        blank.Items.Add(new TreeViewItem
        {
            Header = "易语窗口程序",
            Tag = ProgramKind.Window
        });

        var wizards = new TreeViewItem
        {
            Header = "向导",
            Tag = "wizards",
            IsExpanded = true
        };
        wizards.Items.Add(new TreeViewItem
        {
            Header = "新建项目",
            Tag = ProgramKind.Project
        });
        tree.Items.Add(blank);
        tree.Items.Add(wizards);

        var samples = new TreeViewItem { Header = "示例", Tag = "samples" };
        var sampleDirectory = FindSamplesDirectory();
        if (sampleDirectory is not null)
        {
            foreach (var file in Directory
                         .EnumerateFiles(sampleDirectory, "*.yyc", SearchOption.TopDirectoryOnly)
                         .Concat(Directory.EnumerateFiles(
                             sampleDirectory,
                             "*.yyproj",
                             SearchOption.TopDirectoryOnly))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                samples.Items.Add(new TreeViewItem
                {
                    Header = Path.GetFileName(file),
                    Tag = file
                });
            }
        }

        if (samples.Items.Count == 0)
        {
            samples.Items.Add(new TreeViewItem { Header = "未找到示例", Tag = "samples" });
        }

        tree.Items.Add(samples);
        tree.SelectionChanged += (_, _) => OnNavigationSelected(tree.SelectedItem);
        return tree;
    }

    private Control BuildTypeList()
    {
        _typeList.Background = Brushes.Transparent;
        _typeList.BorderThickness = new Thickness(0);
        _typeList.Padding = new Thickness(4);
        _typeList.Items.Add(MakeTypeTile("▣", "易语窗口程序", ProgramKind.Window));
        _typeList.Items.Add(MakeTypeTile(">_", "易语控制台程序", ProgramKind.Console));
        _typeList.Items.Add(MakeTypeTile("⇪", "易语动态链接库", "dll"));
        _typeList.Items.Add(MakeTypeTile("▤", "易语模块", ProgramKind.Module));
        _typeList.SelectionChanged += (_, _) =>
        {
            if (_typeList.SelectedItem is ListBoxItem { Tag: ProgramKind kind })
            {
                SelectKind(kind);
            }
            else if (_typeList.SelectedItem is ListBoxItem { Tag: "dll" })
            {
                _kind = ProgramKind.None;
                _samplePath = null;
                _primaryButton.Content = "确定";
                _primaryButton.IsEnabled = false;
                _hintText.Text = "易语动态链接库项目当前尚未实现，请选择其他类型。";
            }
        };
        return _typeList;
    }

    private Control BuildFooter()
    {
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 14, 2, 0)
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        _suppressBox.Content = "启动后不再显示此对话框";
        _suppressBox.FontSize = 11.5;
        left.Children.Add(_suppressBox);
        left.Children.Add(_hintText);
        footer.Children.Add(left);

        var open = ProjectTheme.SecondaryButton("打开…", () =>
            Close(new NewProgramResult(ProgramKind.OpenFile, null, SuppressOnStartup, null)));
        var cancel = ProjectTheme.SecondaryButton("取消", Close);
        cancel.IsCancel = true;
        var buttons = ProjectTheme.ActionRow(open, _primaryButton, cancel);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        return footer;
    }

    private void OnNavigationSelected(object? selected)
    {
        if (selected is not TreeViewItem { Tag: var tag })
        {
            return;
        }

        if (tag is ProgramKind kind)
        {
            SelectKind(kind);
            return;
        }

        if (tag is string path &&
            (path.EndsWith(".yyc", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".yyproj", StringComparison.OrdinalIgnoreCase)))
        {
            _kind = ProgramKind.Sample;
            _samplePath = path;
            _typeList.SelectedItem = null;
            _primaryButton.Content = "打开";
            _primaryButton.IsEnabled = true;
            _hintText.Text = "打开示例：" + Path.GetFileName(path);
        }
    }

    private void SelectKind(ProgramKind kind)
    {
        _kind = kind;
        _samplePath = null;
        _primaryButton.Content = "确定";
        _primaryButton.IsEnabled = true;
        _hintText.Text = kind switch
        {
            ProgramKind.Window => "创建带 _启动窗口 的窗口项目。",
            ProgramKind.Console => "创建直接运行的控制台项目。",
            ProgramKind.Module => "创建可导出公开子程序的模块项目。",
            ProgramKind.Project => "打开新建项目向导，选择窗口项目。",
            _ => string.Empty
        };
    }

    private void Accept()
    {
        if (_kind == ProgramKind.None)
        {
            _hintText.Text = "请选择要创建的易语程序类型。";
            return;
        }

        if (_kind == ProgramKind.Sample && string.IsNullOrWhiteSpace(_samplePath))
        {
            _hintText.Text = "请选择一个示例文件。";
            return;
        }

        Close(new NewProgramResult(_kind, _samplePath, SuppressOnStartup, null));
    }

    private static ListBoxItem MakeTypeTile(string glyph, string name, object tag)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(10, 7)
        };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 30,
            FontSize = 18,
            Foreground = ProjectTheme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12.5,
            Foreground = ProjectTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new ListBoxItem
        {
            Content = panel,
            Tag = tag,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private static string? FindSamplesDirectory()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "examples"),
                     Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples"),
                     Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples")
                 })
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}

/// <summary>“新建”选择结果；项目类型会附带已创建的工程。</summary>
public sealed record NewProgramResult(
    NewProgramDialog.ProgramKind Kind,
    string? SamplePath,
    bool SuppressOnStartup,
    YyProject? Project);
