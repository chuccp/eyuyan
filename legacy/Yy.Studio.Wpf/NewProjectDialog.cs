using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using Microsoft.Win32;
using Yy.Compiler;
using Yy.Compiler.Forms;

namespace Yy.Studio;

/// <summary>新建项目：同时确定项目名、模块包名、源码目录和资源目录。</summary>
public sealed class NewProjectDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _packageBox = new();
    private readonly TextBox _locationBox = new();
    private readonly TextBlock _pathPreview = new();
    private readonly TextBlock _validationText = new();
    private readonly YyProjectKind _projectKind;
    private bool _packageEdited;

    public YyProjectKind ProjectKind => _projectKind;
    public string ProjectName => _nameBox.Text.Trim();
    public string PackageName => _packageBox.Text.Trim();
    public string ProjectPath => Path.Combine(
        Path.GetFullPath(_locationBox.Text.Trim()),
        ProjectName,
        ProjectName + ".yyproj");

    public NewProjectDialog(YyProjectKind projectKind = YyProjectKind.Window)
    {
        _projectKind = projectKind;
        Title = "新建易语项目 · " + KindName(projectKind);
        Width = 640;
        Height = 500;
        MinWidth = 580;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                UseAeroCaptionButtons = false
            });

        var root = new Grid { Margin = new Thickness(24, 20, 24, 18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock
        {
            Text = "创建" + KindName(projectKind) + "项目",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        heading.Children.Add(new TextBlock
        {
            Text = "先选择保存位置并填写项目名、包名，确定后才会创建工程。",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("MutedBrush")
        });
        root.Children.Add(heading);

        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(form, 0, "项目名", _nameBox);
        AddRow(form, 1, "包名", _packageBox);
        AddRow(form, 2, "项目位置", CreateLocationRow());
        AddDirectoryRow(form, 3, "源码目录", YyProject.SourceDirectoryName + "/");
        AddDirectoryRow(form, 4, "资源目录", YyProject.ResourceDirectoryName + "/");
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var structure = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Background = (Brush)Application.Current.FindResource("FaceBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "源码与资源分开保存",
                        FontWeight = FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "源码子目录可用“包名.目录”导入，例如 引入 \"demo.tools\"。",
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        Grid.SetRow(structure, 5);
        Grid.SetColumn(structure, 1);
        form.Children.Add(structure);
        var formCard = new Border
        {
            Padding = new Thickness(20, 18, 20, 18),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.FindResource("PanelRaisedBrush"),
            Child = form
        };
        Grid.SetRow(formCard, 1);
        root.Children.Add(formCard);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _pathPreview.Foreground = (Brush)Application.Current.FindResource("MutedBrush");
        _pathPreview.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_pathPreview);

        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _validationText.Foreground = (Brush)Application.Current.FindResource("ErrorBrush");
        _validationText.TextWrapping = TextWrapping.Wrap;
        _validationText.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(_validationText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "取消",
            MinWidth = 86,
            Height = 30,
            Padding = new Thickness(16, 4, 16, 4),
            IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var create = new Button
        {
            Content = "创建",
            MinWidth = 86,
            Height = 30,
            Padding = new Thickness(16, 4, 16, 4),
            Foreground = Brushes.White,
            Background = (Brush)Application.Current.FindResource("AccentBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("AccentBrush"),
            IsDefault = true
        };

        create.Click += OnCreate;
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        Grid.SetColumn(buttons, 1);
        actions.Children.Add(buttons);
        Grid.SetRow(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(CreateCaption());
        Grid.SetRow(root, 1);
        layout.Children.Add(root);
        var contentSurface = new Border
        {
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Background = (Brush)Application.Current.FindResource("WindowBrush"),
            Child = layout
        };
        RoundedClipBehavior.SetRadius(contentSurface, 11);

        var frame = new Border
        {
            Margin = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(174, 180, 188)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.FindResource("WindowBrush"),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(42, 47, 54),
                BlurRadius = 18,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.20
            },
            Child = contentSurface
        };
        Content = frame;
        _nameBox.Text = "我的应用";
        _packageBox.Text = "myapp";
        _locationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _nameBox.TextChanged += OnProjectNameChanged;
        _packageBox.TextChanged += (_, _) => _packageEdited = true;
        _locationBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        Loaded += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    private UIElement CreateCaption()
    {
        var header = new Grid
        {
            Background = (Brush)Application.Current.FindResource("PanelBrush")
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(14, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.FindResource("AccentBrush"),
            Child = new TextBlock
            {
                Text = "易",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var title = new TextBlock
        {
            Text = "新建项目",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = IdeDialogChrome.CreateCloseButton(Close);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        return header;
    }

    private UIElement CreateLocationRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _locationBox.Margin = new Thickness(0);
        grid.Children.Add(_locationBox);
        var browse = new Button
        {
            Content = "浏览…",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5)
        };
        browse.Click += OnBrowse;
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private void AddRow(Grid form, int row, string label, UIElement input)
    {
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 12, 14),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(text, row);
        form.Children.Add(text);
        if (input is FrameworkElement element)
        {
            element.Margin = new Thickness(0, 0, 0, 14);
            if (input is Control control)
            {
                control.Padding = new Thickness(7, 5, 7, 5);
            }
        }

        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        form.Children.Add(input);
    }

    private void AddDirectoryRow(Grid form, int row, string label, string directory)
    {
        var text = new TextBox
        {
            Text = directory,
            IsReadOnly = true,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush")
        };
        AddRow(form, row, label, text);
    }

    private void OnProjectNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_packageEdited)
        {
            _packageBox.Text = ToPackageSegment(ProjectName);
        }

        UpdatePreview();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择项目保存位置",
            InitialDirectory = Directory.Exists(_locationBox.Text)
                ? _locationBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == true)
        {
            _locationBox.Text = dialog.FolderName;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
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

        if (!Directory.Exists(_locationBox.Text.Trim()))
        {
            _validationText.Text = "项目位置必须是已经存在的文件夹。";
            return;
        }

        DialogResult = true;
    }

    private void UpdatePreview()
    {
        if (_pathPreview is null)
        {
            return;
        }

        var location = _locationBox.Text.Trim();
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

        if (!char.IsLetter(characters[0]) && characters[0] != '_')
        {
            return "_" + new string(characters);
        }

        return new string(characters);
    }

    private static string KindName(YyProjectKind kind) => kind switch
    {
        YyProjectKind.Console => "控制台",
        YyProjectKind.Module => "模块",
        _ => "窗口"
    };
}
