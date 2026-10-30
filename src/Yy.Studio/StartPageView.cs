using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>
/// 起始页(对齐官方第一步):蓝色横幅 + 「最近的项目」面板(新建|打开…|最近|清空 + 最近列表) + 快速入门。
/// 启动先到起始页,由用户选择新建/打开;关闭全部文档后回到起始页。
/// </summary>
public sealed class StartPageView : UserControl
{
    private readonly ListBox _recentList = new();
    private readonly TextBlock _recentEmpty = new()
    {
        Text = "（暂无最近项目。使用“新建”或“打开…”开始。）",
        Foreground = new SolidColorBrush(Color.FromRgb(120, 130, 145)),
        Margin = new Thickness(2, 8, 2, 2)
    };

    public event Action? NewRequested;
    public event Action? OpenRequested;
    public event Action<string>? RecentSelected;
    public event Action? ClearRequested;

    public StartPageView()
    {
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 251));
        var root = new DockPanel { LastChildFill = true };

        // 蓝色横幅(官方样式)
        var banner = new Border
        {
            Height = 72,
            Padding = new Thickness(18, 10, 18, 10)
        };
        banner.Background = new LinearGradientBrush(
            Color.FromRgb(11, 62, 138), Color.FromRgb(52, 116, 200), 0);
        var bannerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        bannerPanel.Children.Add(new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(217, 48, 37)),
            Child = new TextBlock
            {
                Text = "易",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Margin = new Thickness(0, 0, 14, 0)
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "易语 Studio",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Bold
        });
        titles.Children.Add(new TextBlock
        {
            Text = "全中文全可视结构化编程语言",
            Foreground = new SolidColorBrush(Color.FromRgb(196, 216, 242)),
            FontSize = 12.5,
            Margin = new Thickness(2, 2, 0, 0)
        });
        bannerPanel.Children.Add(titles);
        banner.Child = bannerPanel;
        DockPanel.SetDock(banner, Dock.Top);
        root.Children.Add(banner);

        var body = new Grid { Margin = new Thickness(14) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左:最近的项目
        var recentPanel = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White
        };
        var recentGrid = new Grid { Margin = new Thickness(10) };
        recentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        recentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        recentGrid.Children.Add(new TextBlock
        {
            Text = "最近的项目",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5
        });

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 8)
        };
        links.Children.Add(MakeLink("新建", () => NewRequested?.Invoke()));
        links.Children.Add(MakeLink("打开…", () => OpenRequested?.Invoke()));
        links.Children.Add(MakeLink("最近", () => _recentList.Focus()));
        links.Children.Add(MakeLink("清空", () => ClearRequested?.Invoke()));
        Grid.SetRow(links, 1);
        recentGrid.Children.Add(links);

        var recentHost = new Grid();
        recentHost.Children.Add(_recentEmpty);
        _recentList.Background = Brushes.White;
        _recentList.BorderThickness = new Thickness(0);
        _recentList.MouseDoubleClick += (_, _) =>
        {
            if (_recentList.SelectedItem is string path)
            {
                RecentSelected?.Invoke(path);
            }
        };
        recentHost.Children.Add(_recentList);
        Grid.SetRow(recentHost, 2);
        recentGrid.Children.Add(recentHost);
        recentPanel.Child = recentGrid;
        Grid.SetColumn(recentPanel, 0);
        body.Children.Add(recentPanel);

        // 右:快速入门(替代官方资讯区的本地内容)
        var help = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White
        };
        var helpText = new TextBlock
        {
            Margin = new Thickness(14, 10, 14, 10),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 21,
            Text =
                "快速入门\n\n" +
                "· “新建”创建一份新的易语源码(含入门示例);“打开…”载入 .yyc/.yyx/.yyproj 文件。\n" +
                "· 表格编辑器中:空格/双击编辑行,回车确认,Esc 取消,+ / − 折叠。\n" +
                "· F5 运行,F8 单步,F9 切换断点,F7 编译。\n" +
                "· 窗体设计:从右侧组件箱拖入控件,双击控件生成事件代码。\n" +
                "· 底部“提示/输出/调用表/监视表/变量表/搜寻/剪辑历史”随时查看运行信息。"
        };
        help.Child = helpText;
        Grid.SetColumn(help, 2);
        body.Children.Add(help);

        root.Children.Add(body);
        Content = root;
    }

    private static TextBlock MakeLink(string text, Action onClick)
    {
        var link = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 90, 180)),
            TextDecorations = TextDecorations.Underline,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 16, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        link.MouseLeftButtonDown += (_, _) => onClick();
        return link;
    }

    /// <summary>刷新最近列表。</summary>
    public void SetRecent(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        _recentList.ItemsSource = list;
        _recentEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _recentList.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
