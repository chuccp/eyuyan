using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio.Forms;

public static class YyFormRenderer
{
    public static FrameworkElement Create(YyControlModel model)
    {
        FrameworkElement element = model.Type switch
        {
            YyControlTypes.Button => new Button { Content = model.Text },
            YyControlTypes.Label => new TextBlock { Text = model.Text },
            YyControlTypes.TextBox => new TextBox { Text = model.Text },
            YyControlTypes.CheckBox => new CheckBox { Content = model.Text },
            YyControlTypes.ComboBox => CreateComboBox(model.Text),
            YyControlTypes.ListBox => CreateListBox(model.Text),
            YyControlTypes.GroupBox => new GroupBox { Header = model.Text },
            _ => throw new ArgumentOutOfRangeException(nameof(model), model.Type, "不支持的控件类型。")
        };

        element.Tag = model;
        element.Width = model.Width;
        element.Height = model.Height;
        element.IsEnabled = model.IsEnabled;
        element.Visibility = model.IsVisible ? Visibility.Visible : Visibility.Hidden;

        if (element is Control control)
        {
            control.FontSize = model.FontSize;
            control.Foreground = Brush(model.Foreground);
            control.Background = Brush(model.Background);
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 192, 205));
            control.Padding = new Thickness(8, 4, 8, 4);
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.FontSize = model.FontSize;
            textBlock.Foreground = Brush(model.Foreground);
            textBlock.TextWrapping = TextWrapping.Wrap;
        }

        if (element is CheckBox checkbox)
        {
            checkbox.IsChecked = model.IsChecked;
        }

        return element;
    }

    public static Brush Brush(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System.Windows.Media.Brushes.Transparent;
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
        }

        return System.Windows.Media.Brushes.Transparent;
    }

    private static ComboBox CreateComboBox(string text)
    {
        var comboBox = new ComboBox();
        foreach (var item in SplitItems(text))
        {
            comboBox.Items.Add(item);
        }

        comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        return comboBox;
    }

    private static ListBox CreateListBox(string text)
    {
        var listBox = new ListBox();
        foreach (var item in SplitItems(text))
        {
            listBox.Items.Add(item);
        }

        return listBox;
    }

    private static string[] SplitItems(string text) =>
        text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

