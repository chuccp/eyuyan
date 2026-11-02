using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>IDEA 式欢迎页：注入的最近项目列表与快速开始卡片。</summary>
public sealed class StartPageView : UserControl
{
    private readonly ListBox _recentList = new();
    private readonly TextBlock _recentEmpty;
    private readonly List<string> _recentProjects = [];

    public StartPageView()
        : this(null)
    {
    }

    public StartPageView(IEnumerable<string>? recentProjects)
    {
        Background = Brushes.Transparent;
        _recentEmpty = new TextBlock
        {
            Text = "暂无最近项目",
            Foreground = ProjectTheme.MutedBrush,
            Margin = new Thickness(4, 18, 4, 4)
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(28, 24)
        };
        root.Children.Add(BuildHeader());

        var cards = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,18,*"),
            Margin = new Thickness(0, 22, 0, 0)
        };
        var recentCard = CreateCard(BuildRecentCard());
        var quickCard = CreateCard(BuildQuickStartCard());
        Grid.SetColumn(quickCard, 2);
        cards.Children.Add(recentCard);
        cards.Children.Add(quickCard);
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);

        Content = root;
        if (recentProjects is not null)
        {
            SetRecent(recentProjects);
        }
    }

    public event Action? NewRequested;

    public event Action? OpenRequested;

    public event Action<string>? ProjectRequested;

    public event Action? ClearRequested;

    public IReadOnlyList<string> RecentProjects => _recentProjects;

    /// <summary>刷新最近项目；最近列表由主窗口配置注入，组件本身不持久化。</summary>
    public void SetRecent(IEnumerable<string> paths)
    {
        var list = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        _recentProjects.Clear();
        _recentProjects.AddRange(list);
        _recentList.Items.Clear();
        foreach (var path in list)
        {
            _recentList.Items.Add(CreateRecentItem(path));
        }

        _recentEmpty.IsVisible = list.Count == 0;
        _recentList.IsVisible = list.Count > 0;
    }

    private Control BuildHeader()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var mark = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(12),
            Background = ProjectTheme.AccentBrush,
            Margin = new Thickness(0, 0, 16, 0),
            Child = new TextBlock
            {
                Text = "易",
                Foreground = Brushes.White,
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(mark);

        var titles = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 5
        };
        titles.Children.Add(new TextBlock
        {
            Text = "欢迎使用 易语 Studio",
            FontSize = 27,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush
        });
        titles.Children.Add(ProjectTheme.Muted("创建、打开或继续最近的易语项目", 12.5));
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        return header;
    }

    private Control BuildRecentCard()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ClipToBounds = true
        };
        heading.Children.Add(new TextBlock
        {
            Text = "最近的项目",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 2
        };
        actions.Children.Add(ProjectTheme.LinkButton("新建", () => NewRequested?.Invoke()));
        actions.Children.Add(ProjectTheme.LinkButton("打开…", () => OpenRequested?.Invoke()));
        actions.Children.Add(ProjectTheme.LinkButton("清空", () => ClearRequested?.Invoke()));
        Grid.SetColumn(actions, 1);
        heading.Children.Add(actions);
        panel.Children.Add(heading);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 12, 0, 6),
            Background = ProjectTheme.SoftBorderBrush
        };
        Grid.SetRow(separator, 1);
        panel.Children.Add(separator);

        _recentList.Background = Brushes.Transparent;
        _recentList.BorderThickness = new Thickness(0);
        _recentList.Padding = new Thickness(0, 2, 0, 0);
        _recentList.DoubleTapped += (_, _) => OpenSelectedRecent();
        _recentList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedRecent();
                e.Handled = true;
            }
        };

        var recentHost = new Grid();
        recentHost.Children.Add(_recentEmpty);
        recentHost.Children.Add(_recentList);
        Grid.SetRow(recentHost, 2);
        panel.Children.Add(recentHost);
        return panel;
    }

    private static Control BuildQuickStartCard()
    {
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = "快速开始",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 2)
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

    private static Control CreateTip(string title, string description)
    {
        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush
        });
        text.Children.Add(ProjectTheme.Muted(description));
        return text;
    }

    private static Control CreateCard(Control child)
    {
        return new Border
        {
            Padding = new Thickness(20, 18),
            CornerRadius = new CornerRadius(12),
            Background = ProjectTheme.CardBrush,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = 2,
                Blur = 14,
                Color = Color.Parse("#14465058")
            }),
            Child = child
        };
    }

    private static Control CreateRecentItem(string path)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(2, 3) };
        panel.Children.Add(new TextBlock
        {
            Text = DisplayName(path),
            FontSize = 12,
            Foreground = ProjectTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = path,
            FontSize = 10.5,
            Foreground = ProjectTheme.MutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return new ListBoxItem
        {
            Content = panel,
            Tag = path,
            Padding = new Thickness(8, 5),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(6)
        };
    }

    private void OpenSelectedRecent()
    {
        if (_recentList.SelectedItem is ListBoxItem { Tag: string path })
        {
            ProjectRequested?.Invoke(path);
        }
    }

    private static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        return string.Equals(Path.GetExtension(name), ".yyproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }
}
