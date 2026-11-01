using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace Yy.Studio;

/// <summary>IDEA 式“新建”对话框。</summary>
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
    private readonly Button _okButton = new();
    private readonly Button _openButton = new();
    private ProgramKind _kind = ProgramKind.Window;
    private string? _samplePath;

    public ProgramKind SelectedKind => _kind;
    public string? SelectedSamplePath => _samplePath;
    public bool SuppressOnStartup => _suppressBox.IsChecked == true;

    public NewProgramDialog()
    {
        Title = "新建";
        Width = 680;
        Height = 520;
        MinWidth = 600;
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
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                UseAeroCaptionButtons = false
            });

        var contentSurface = new Border
        {
            Margin = new Thickness(1),
            Background = Brush("WindowBrush", Color.FromRgb(247, 247, 248)),
            CornerRadius = new CornerRadius(11),
            Child = BuildLayout()
        };
        RoundedClipBehavior.SetRadius(contentSurface, 11);

        var shell = new Border
        {
            Margin = new Thickness(12),
            Background = Brush("WindowBrush", Color.FromRgb(247, 247, 248)),
            BorderBrush = Brush("BorderBrush", Color.FromRgb(174, 180, 188)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
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
        Content = shell;
    }

    private Grid BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

        root.Children.Add(BuildCaption());
        var body = BuildBody();
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private UIElement BuildCaption()
    {
        var header = new Grid { Background = Brush("PanelBrush", Color.FromRgb(252, 252, 253)) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(14, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brush("AccentBrush", Color.FromRgb(53, 116, 240)),
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
            Text = "新建",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush", Color.FromRgb(31, 35, 40)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var close = IdeDialogChrome.CreateCloseButton(Close);
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        return header;
    }

    private UIElement BuildBody()
    {
        var body = new Grid { Margin = new Thickness(18, 10, 18, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var treeCard = DialogCard(CreateNavigationTree());
        Grid.SetColumn(treeCard, 0);
        body.Children.Add(treeCard);

        _typeList.Background = Brushes.Transparent;
        _typeList.BorderThickness = new Thickness(0);
        _typeList.Padding = new Thickness(4);
        _typeList.ItemContainerStyle = CreateTypeItemStyle();
        _typeList.Items.Add(MakeTypeTile("▣", "易语窗口程序", ProgramKind.Window));
        _typeList.Items.Add(MakeTypeTile(">_", "易语控制台程序", ProgramKind.Console));
        _typeList.Items.Add(MakeTypeTile("⇪", "易语动态链接库", "dll"));
        _typeList.Items.Add(MakeTypeTile("▤", "易语模块", ProgramKind.Module));
        _typeList.SelectedIndex = 0;
        _typeList.SelectionChanged += (_, _) =>
        {
            if (_typeList.SelectedItem is ListBoxItem { Tag: ProgramKind kind })
            {
                SelectKind(kind);
            }
            else if (_typeList.SelectedItem is ListBoxItem { Tag: "dll" })
            {
                _kind = ProgramKind.None;
                _okButton.Content = "确定";
            }
        };
        var typeCard = DialogCard(_typeList);
        Grid.SetColumn(typeCard, 2);
        body.Children.Add(typeCard);
        return body;
    }

    private TreeView CreateNavigationTree()
    {
        var tree = new TreeView
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2)
        };
        var windowsBlank = new TreeViewItem
        {
            Header = "空白程序",
            Tag = ProgramKind.Window,
            IsExpanded = true
        };
        windowsBlank.Items.Add(new TreeViewItem
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
        tree.Items.Add(windowsBlank);
        tree.Items.Add(wizards);

        var samples = new TreeViewItem { Header = "示例", Tag = "samples" };
        var sampleDirectory = FindSamplesDirectory();
        if (sampleDirectory is not null)
        {
            foreach (var file in Directory.EnumerateFiles(
                         sampleDirectory,
                         "*.yyc",
                         SearchOption.TopDirectoryOnly)
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
        tree.SelectedItemChanged += (_, e) =>
        {
            if (e.NewValue is not TreeViewItem { Tag: var tag })
            {
                return;
            }

            if (tag is ProgramKind kind)
            {
                SelectKind(kind);
            }
            else if (tag is string path &&
                     (path.EndsWith(".yyc", StringComparison.OrdinalIgnoreCase) ||
                      path.EndsWith(".yyproj", StringComparison.OrdinalIgnoreCase)))
            {
                _kind = ProgramKind.Sample;
                _samplePath = path;
                _typeList.SelectedItem = null;
                _okButton.Content = "打开";
            }
        };
        return tree;
    }

    private UIElement BuildFooter()
    {
        var footer = new Grid
        {
            Margin = new Thickness(18, 6, 18, 14),
            Background = Brush("PanelBrush", Color.FromRgb(252, 252, 253))
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _suppressBox.Content = "启动后不再显示此对话框";
        _suppressBox.FontSize = 11.5;
        _suppressBox.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_suppressBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _openButton.Content = "打开…";
        ConfigureButton(_openButton, primary: false);
        _openButton.Click += (_, _) =>
        {
            _kind = ProgramKind.OpenFile;
            DialogResult = true;
        };
        _okButton.Content = "确定";
        ConfigureButton(_okButton, primary: true);
        _okButton.IsDefault = true;
        _okButton.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "取消", IsCancel = true };
        ConfigureButton(cancel, primary: false);
        buttons.Children.Add(_openButton);
        buttons.Children.Add(_okButton);
        buttons.Children.Add(cancel);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        return footer;
    }

    private void SelectKind(ProgramKind kind)
    {
        _kind = kind;
        _samplePath = null;
        _okButton.Content = "确定";
    }

    private static ListBoxItem MakeTypeTile(string glyph, string name, object tag)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 7, 10, 7)
        };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 28,
            FontSize = 18,
            Foreground = Brush("AccentBrush", Color.FromRgb(53, 116, 240)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new ListBoxItem { Content = panel, Tag = tag };
    }

    private static Style CreateTypeItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 4)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateTypeItemTemplate()));
        return style;
    }

    private static ControlTemplate CreateTypeItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem))
        {
            VisualTree = border
        };
        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter
        {
            Property = Border.BackgroundProperty,
            Value = Brush("FaceBrush", Color.FromRgb(242, 243, 245)),
            TargetName = "Chrome"
        });
        template.Triggers.Add(hover);
        var selected = new Trigger
        {
            Property = ListBoxItem.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter
        {
            Property = Border.BackgroundProperty,
            Value = new SolidColorBrush(Color.FromRgb(232, 238, 247)),
            TargetName = "Chrome"
        });
        template.Triggers.Add(selected);
        return template;
    }

    private static Border DialogCard(UIElement child) =>
        new()
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = Brush("PanelRaisedBrush", Colors.White),
            Child = child
        };

    private static void ConfigureButton(Button button, bool primary)
    {
        button.MinWidth = 84;
        button.Height = 30;
        button.Padding = new Thickness(16, 4, 16, 4);
        button.Margin = new Thickness(8, 0, 0, 0);
        button.Cursor = Cursors.Hand;
        button.Background = primary
            ? Brush("AccentBrush", Color.FromRgb(53, 116, 240))
            : Brush("FaceBrush", Color.FromRgb(242, 243, 245));
        button.Foreground = primary
            ? Brushes.White
            : Brush("TextBrush", Color.FromRgb(31, 35, 40));
        button.BorderBrush = primary
            ? Brush("AccentBrush", Color.FromRgb(53, 116, 240))
            : Brush("BorderBrush", Color.FromRgb(216, 219, 223));
    }

    private static SolidColorBrush Brush(string key, Color fallback) =>
        Application.Current.TryFindResource(key) as SolidColorBrush
        ?? new SolidColorBrush(fallback);

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
