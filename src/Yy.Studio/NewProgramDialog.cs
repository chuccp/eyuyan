using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>
/// “新建”程序类型对话框(对齐官方第一步):
/// 横幅 + 左侧新建向导树(空白程序/向导/易语例程) + 右侧程序类型图标 + 启动后不再显示 + 打开…/确定/取消。
/// </summary>
public sealed class NewProgramDialog : Window
{
    /// <summary>程序类型。</summary>
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

    /// <summary>本次选择的程序类型。</summary>
    public ProgramKind SelectedKind => _kind;

    /// <summary>选择的例程文件路径(仅 Sample)。</summary>
    public string? SelectedSamplePath => _samplePath;

    /// <summary>用户勾选了“启动后不再显示此对话框”。</summary>
    public bool SuppressOnStartup => _suppressBox.IsChecked == true;

    public NewProgramDialog()
    {
        Title = "新建:";
        Width = 500;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 横幅
        var banner = new Border { Padding = new Thickness(14, 8, 14, 8) };
        banner.Background = new LinearGradientBrush(
            Color.FromRgb(11, 62, 138), Color.FromRgb(70, 130, 210), 0);
        var bannerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        bannerPanel.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(217, 48, 37)),
            Child = new TextBlock
            {
                Text = "易",
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Margin = new Thickness(0, 0, 12, 0)
        });
        var bannerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bannerText.Children.Add(new TextBlock
        {
            Text = "易语 Studio",
            Foreground = Brushes.White,
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        bannerText.Children.Add(new TextBlock
        {
            Text = "全中文全可视结构化编程语言",
            Foreground = new SolidColorBrush(Color.FromRgb(196, 216, 242)),
            FontSize = 11.5
        });
        bannerPanel.Children.Add(bannerText);
        banner.Child = bannerPanel;
        Grid.SetRow(banner, 0);
        root.Children.Add(banner);

        // 主体:左向导树 + 右类型图标
        var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tree = new TreeView { BorderThickness = new Thickness(1) };
        var windowsBlank = new TreeViewItem { Header = "Windows 空白程序", Tag = ProgramKind.Window, IsExpanded = true };
        windowsBlank.Items.Add(new TreeViewItem { Header = "易语窗口程序", Tag = ProgramKind.Window });
        var linuxBlank = new TreeViewItem { Header = "Linux 空白程序", Tag = "linux" };
        linuxBlank.Items.Add(new TreeViewItem { Header = "易语控制台程序(Linux)", Tag = "linux" });
        var wizards = new TreeViewItem { Header = "向导", Tag = "wizards", IsExpanded = true };
        wizards.Items.Add(new TreeViewItem { Header = "新建项目(项目向导)", Tag = ProgramKind.Project });
        wizards.Items.Add(new TreeViewItem { Header = "常用向导", Tag = "wizards" });
        tree.Items.Add(windowsBlank);
        tree.Items.Add(linuxBlank);
        tree.Items.Add(wizards);

        var samples = new TreeViewItem { Header = "打开易语例程", Tag = "samples" };
        var sampleDirectory = FindSamplesDirectory();
        if (sampleDirectory is not null)
        {
            foreach (var file in Directory.EnumerateFiles(sampleDirectory, "*.yyc", SearchOption.TopDirectoryOnly)
                         .Concat(Directory.EnumerateFiles(sampleDirectory, "*.yyproj", SearchOption.TopDirectoryOnly))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                samples.Items.Add(new TreeViewItem { Header = Path.GetFileName(file), Tag = file });
            }
        }

        if (samples.Items.Count == 0)
        {
            samples.Items.Add(new TreeViewItem { Header = "（未找到例程）", Tag = "samples" });
        }

        tree.Items.Add(samples);
        tree.SelectedItemChanged += (_, e) =>
        {
            if (e.NewValue is not TreeViewItem { Tag: var tag })
            {
                return;
            }

            switch (tag)
            {
                case ProgramKind kind:
                    SelectKind(kind);
                    break;
                case string path when path.EndsWith(".yyc", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".yyproj", StringComparison.OrdinalIgnoreCase):
                    _kind = ProgramKind.Sample;
                    _samplePath = path;
                    _typeList.SelectedItem = null;
                    _okButton.Content = "打开(O)";
                    break;
                case "linux":
                    _kind = ProgramKind.None;
                    _okButton.Content = "确定(O)";
                    break;
            }
        };
        Grid.SetColumn(tree, 0);
        body.Children.Add(tree);

        _typeList.Margin = new Thickness(10, 0, 0, 0);
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
                _okButton.Content = "确定(O)";
            }
        };
        Grid.SetColumn(_typeList, 1);
        body.Children.Add(_typeList);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // 底部:不再显示 + 按钮
        var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _suppressBox.Content = "启动后不再显示此对话框";
        _suppressBox.VerticalAlignment = VerticalAlignment.Center;
        _suppressBox.FontSize = 11.5;
        Grid.SetColumn(_suppressBox, 0);
        bottom.Children.Add(_suppressBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _openButton.Content = "打开…(O)";
        _openButton.Width = 86;
        _openButton.Margin = new Thickness(0, 0, 8, 0);
        _openButton.Click += (_, _) =>
        {
            _kind = ProgramKind.OpenFile;
            DialogResult = true;
        };
        _okButton.Content = "确定(O)";
        _okButton.Width = 86;
        _okButton.Margin = new Thickness(0, 0, 8, 0);
        _okButton.IsDefault = true;
        _okButton.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "取消(C)", Width = 86, IsCancel = true };
        buttons.Children.Add(_openButton);
        buttons.Children.Add(_okButton);
        buttons.Children.Add(cancel);
        Grid.SetColumn(buttons, 1);
        bottom.Children.Add(buttons);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);

        Content = root;
    }

    private void SelectKind(ProgramKind kind)
    {
        _kind = kind;
        _samplePath = null;
        _okButton.Content = "确定(O)";
    }

    private static ListBoxItem MakeTypeTile(string glyph, string name, object tag)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 4, 6, 4)
        };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 80, 160)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new ListBoxItem { Content = panel, Tag = tag };
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
