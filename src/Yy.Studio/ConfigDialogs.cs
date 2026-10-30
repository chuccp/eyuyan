using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>“支持库配置”对话框:勾选启用的支持库分类。</summary>
public sealed class SupportConfigDialog : Window
{
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.Ordinal);

    public SupportConfigDialog(IEnumerable<string> libraryNames, ISet<string> disabled)
    {
        Title = "支持库配置";
        Width = 420;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "系统核心支持库",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 2, 2, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = StudioCatalog.CoreLibraryDescription,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
            Margin = new Thickness(2, 0, 2, 10)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "勾选需要在“命令夹”中显示的命令分类:",
            Margin = new Thickness(2, 2, 2, 10)
        });
        foreach (var name in libraryNames)
        {
            var check = new CheckBox
            {
                Content = name,
                IsChecked = !disabled.Contains(name),
                Margin = new Thickness(2, 3, 2, 3)
            };
            _checks[name] = check;
            panel.Children.Add(check);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = "确定", Width = 84, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "取消", Width = 84, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
    }

    public IEnumerable<string> CollectDisabled() =>
        _checks.Where(pair => pair.Value.IsChecked != true).Select(pair => pair.Key);
}
