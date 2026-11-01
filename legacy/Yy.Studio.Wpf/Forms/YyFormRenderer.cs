using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yy.Studio.Forms;

public static class YyFormRenderer
{
    public static FrameworkElement Create(
        YyControlModel model,
        string? projectDirectory = null)
    {
        FrameworkElement element = model.Type switch
        {
            YyControlTypes.Button => new Button { Content = model.Text },
            YyControlTypes.Label => new TextBlock { Text = model.Text },
            YyControlTypes.TextBox when !string.IsNullOrEmpty(model.PasswordChar) =>
                new PasswordBox { Password = model.Text, PasswordChar = model.PasswordChar[0] },
            YyControlTypes.TextBox => new TextBox { Text = model.Text },
            YyControlTypes.CheckBox => new CheckBox { Content = model.Text },
            YyControlTypes.RadioButton => new RadioButton { Content = model.Text },
            YyControlTypes.ComboBox => CreateComboBox(model.Text),
            YyControlTypes.ListBox => CreateListBox(model.Text),
            YyControlTypes.GroupBox => new GroupBox { Header = model.Text },
            YyControlTypes.PictureBox => CreatePictureBox(model, projectDirectory),
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

        if (element is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            toggle.IsChecked = model.IsChecked;
        }

        if (element is TextBox textBox)
        {
            textBox.IsReadOnly = model.ReadOnly;
            textBox.AcceptsReturn = model.MultiLine;
            textBox.TextWrapping = model.MultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap;
            textBox.VerticalScrollBarVisibility = model.MultiLine
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
            if (model.MaxLength > 0)
            {
                textBox.MaxLength = model.MaxLength;
            }
        }
        else if (element is PasswordBox passwordBox && model.MaxLength > 0)
        {
            passwordBox.MaxLength = model.MaxLength;
        }

        return element;
    }

    /// <summary>图片框:等比缩放显示图片;未载入图片时显示浅色占位。</summary>
    public static FrameworkElement CreatePictureBox(
        YyControlModel model,
        string? projectDirectory = null)
    {
        var host = new Grid();
        host.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
            Child = new TextBlock
            {
                Text = "图片框",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(1)
        };
        host.Children.Add(image);
        SetElementPicture(host, model.Picture, projectDirectory);
        return host;
    }

    /// <summary>按路径为图片框/元素载入图片(空路径=清空)。</summary>
    public static void SetElementPicture(
        FrameworkElement element,
        string? path,
        string? projectDirectory = null)
    {
        if (element is not Grid { Children.Count: > 0 } host)
        {
            return;
        }

        var image = host.Children.OfType<Image>().FirstOrDefault();
        if (image is null)
        {
            return;
        }

        image.Source = LoadImageSource(path, projectDirectory);
    }

    public static ImageSource? LoadImageSource(
        string? reference,
        string? projectDirectory = null)
    {
        var path = YyResourceIndex.ResolveResourcePath(projectDirectory, reference);
        if (path is null)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or System.IO.FileFormatException or NotSupportedException or UriFormatException)
        {
            return null;
        }
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

