using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Layout;

/// <summary>最小化状态下的边缘恢复标签。</summary>
public partial class ToolWindowLayoutController
{
    private Button CreateRail()
    {
        var rail = new Button
        {
            Background = new SolidColorBrush(Color.Parse("#F7F7F8")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 6),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#506070")),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        rail.Click += OnRailClick;
        return rail;
    }

    private void UpdateMinimizedRails(bool toolboxActive)
    {
        _leftRail.IsVisible = false;
        _rightRail.IsVisible = false;
        _bottomRail.IsVisible = false;
        _leftRail.Tag = null;
        _rightRail.Tag = null;
        _bottomRail.Tag = null;
        ToolTip.SetTip(_leftRail, null);
        ToolTip.SetTip(_rightRail, null);
        ToolTip.SetTip(_bottomRail, null);

        if (_startPageMode)
        {
            return;
        }

        _rightRail.Margin = new Thickness(
            0,
            0,
            toolboxActive ? _toolboxWidth : 0,
            0);

        foreach (var kind in AllKinds)
        {
            var configuration = ConfigFor(kind);
            if (!ToolWindowLayoutProtocol.IsMinimized(configuration.State))
            {
                continue;
            }

            var rail = RailFor(configuration.Dock);
            var title = GetTitle(kind);
            rail.Tag = kind;
            rail.Content = BuildRailContent(configuration.Dock, title);
            ToolTip.SetTip(rail, "恢复" + title);
            rail.IsVisible = true;
        }
    }

    private Button RailFor(string dock) => dock switch
    {
        StudioToolWindowConfiguration.RightDock => _rightRail,
        StudioToolWindowConfiguration.BottomDock => _bottomRail,
        _ => _leftRail
    };

    private static Control BuildRailContent(string dock, string title)
    {
        if (string.Equals(
                dock,
                StudioToolWindowConfiguration.BottomDock,
                StringComparison.Ordinal))
        {
            return new TextBlock
            {
                Text = "▲ " + title,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var vertical = string.Join(
            '\n',
            title.EnumerateRunes().Select(rune => rune.ToString()));
        return new TextBlock
        {
            Text = vertical,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void OnRailClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToolWindowKind kind })
        {
            Show(kind);
        }
    }
}
