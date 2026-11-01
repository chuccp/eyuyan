using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Yy.Studio;

/// <summary>IDEA 式欢迎页：最近项目与快速开始。</summary>
public sealed class StartPageView : UserControl
{
    private readonly ListBox _recentList = new();
    private readonly TextBlock _recentEmpty;

    public event Action? NewRequested;
    public event Action? OpenRequested;
    public event Action<string>? RecentSelected;
    public event Action? ClearRequested;

    public StartPageView()
    {
        Background = Brushes.Transparent;
        _recentEmpty = new TextBlock
        {
            Text = "暂无最近项目",
            Foreground = ResourceBrush("MutedBrush", Color.FromRgb(111, 115, 122)),
            Margin = new Thickness(4, 18, 4, 4)
        };

        var root = new Grid { Margin = new Thickness(28, 24, 28, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateHeader());

        var cards = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var recentCard = CreateCard(BuildRecentCard());
        var quickCard = CreateCard(BuildQuickStartCard());
        Grid.SetColumn(recentCard, 0);
        Grid.SetColumn(quickCard, 2);
        cards.Children.Add(recentCard);
        cards.Children.Add(quickCard);
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);
        Content = root;
    }

    private UIElement CreateHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var mark = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("AccentBrush", Color.FromRgb(53, 116, 240)),
            Margin = new Thickness(0, 0, 16, 0),
            Child = new TextBlock
            {
                Text = "易",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(mark);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "欢迎使用 易语 Studio",
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush", Color.FromRgb(31, 35, 40))
        });
        titles.Children.Add(new TextBlock
        {
            Text = "创建、打开或继续最近的易语项目",
            Margin = new Thickness(1, 5, 0, 0),
            FontSize = 12.5,
            Foreground = ResourceBrush("MutedBrush", Color.FromRgb(111, 115, 122))
        });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        return header;
    }

    private UIElement BuildRecentCard()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = "最近的项目",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush", Color.FromRgb(31, 35, 40))
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(MakeActionButton("新建", () => NewRequested?.Invoke()));
        actions.Children.Add(MakeActionButton("打开…", () => OpenRequested?.Invoke()));
        actions.Children.Add(MakeActionButton("清空", () => ClearRequested?.Invoke()));
        Grid.SetColumn(actions, 1);
        heading.Children.Add(actions);
        panel.Children.Add(heading);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 6),
            Background = ResourceBrush("BorderBrush", Color.FromRgb(216, 219, 223))
        };
        Grid.SetRow(separator, 1);
        panel.Children.Add(separator);

        var recentHost = new Grid();
        recentHost.Children.Add(_recentEmpty);
        _recentList.Background = Brushes.Transparent;
        _recentList.BorderThickness = new Thickness(0);
        _recentList.Padding = new Thickness(0, 2, 0, 0);
        _recentList.MouseDoubleClick += (_, _) =>
        {
            if (_recentList.SelectedItem is string path)
            {
                RecentSelected?.Invoke(path);
            }
        };
        recentHost.Children.Add(_recentList);
        Grid.SetRow(recentHost, 2);
        panel.Children.Add(recentHost);
        return panel;
    }

    private static UIElement BuildQuickStartCard()
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "快速开始",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush", Color.FromRgb(31, 35, 40)),
            Margin = new Thickness(0, 0, 0, 14)
        });
        content.Children.Add(CreateTip("文件", "新建或打开 .yyc、.yyx 与 .yyproj。"));
        content.Children.Add(CreateTip("编辑", "经典视图与纯代码模式共享同一份源码。"));
        content.Children.Add(CreateTip("运行", "F5 运行，F7 编译，F8 单步，F9 切换断点。"));
        content.Children.Add(CreateTip("窗体", "从组件箱拖入控件，双击生成事件入口。"));
        content.Children.Add(CreateTip("帮助", "F1 查看当前命令、参数和 SDK 文档。"));
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private static Border CreateTip(string title, string description)
    {
        var text = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextBrush", Color.FromRgb(31, 35, 40))
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("MutedBrush", Color.FromRgb(111, 115, 122))
        });
        return new Border { Child = text };
    }

    private static Border CreateCard(UIElement child) =>
        new()
        {
            Padding = new Thickness(20, 18, 20, 18),
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("PanelRaisedBrush", Colors.White),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(70, 78, 88),
                BlurRadius = 14,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.10
            },
            Child = child
        };

    private static Button MakeActionButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ResourceBrush("AccentBrush", Color.FromRgb(53, 116, 240)),
            FontSize = 11.5,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Brush ResourceBrush(string key, Color fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    /// <summary>刷新最近列表。</summary>
    public void SetRecent(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        _recentList.ItemsSource = list;
        _recentEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _recentList.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
