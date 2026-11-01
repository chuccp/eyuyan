using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Yy.Compiler;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>
/// 把 <see cref="YyFormModel"/> 渲染为 Avalonia 控件树。窗体设计器、设计预览和
/// 代码预览共用同一份渲染逻辑，保证 .yyx 的三文件模型在两种模式下语义一致。
/// </summary>
public static class YyFormRenderer
{
    private static readonly IBrush ControlBorderBrush =
        new SolidColorBrush(Color.FromRgb(181, 192, 205));

    private static readonly IBrush PicturePlaceholderBackground =
        new SolidColorBrush(Color.FromRgb(245, 245, 245));

    private static readonly IBrush PicturePlaceholderForeground =
        new SolidColorBrush(Color.FromRgb(150, 150, 150));

    /// <summary>按控件模型创建 Avalonia 控件；位置由调用方放入 Canvas。</summary>
    public static Control Create(YyControlModel model, string? projectDirectory = null)
    {
        var element = model.Type switch
        {
            YyControlTypes.Button => new Button { Content = model.Text },
            YyControlTypes.Label => new TextBlock
            {
                Text = model.Text,
                TextWrapping = TextWrapping.Wrap
            },
            YyControlTypes.TextBox => CreateTextBox(model),
            YyControlTypes.CheckBox => new CheckBox { Content = model.Text },
            YyControlTypes.RadioButton => new RadioButton { Content = model.Text },
            YyControlTypes.ComboBox => CreateComboBox(model.Text),
            YyControlTypes.ListBox => CreateListBox(model.Text),
            YyControlTypes.GroupBox => new FormGroupBox { Header = model.Text },
            YyControlTypes.PictureBox => CreatePictureBox(model, projectDirectory),
            _ => throw new ArgumentOutOfRangeException(
                nameof(model),
                model.Type,
                "不支持的控件类型。")
        };

        element.Tag = model;
        element.Width = model.Width;
        element.Height = model.Height;
        element.IsEnabled = model.IsEnabled;
        element.IsVisible = model.IsVisible;

        ApplyAppearance(element, model);

        if (element is CheckBox or RadioButton)
        {
            ((ToggleButton)element).IsChecked = model.IsChecked;
        }

        if (element is TextBox textBox)
        {
            textBox.IsReadOnly = model.ReadOnly;
            textBox.AcceptsReturn = model.MultiLine;
            textBox.TextWrapping = model.MultiLine ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (model.MaxLength > 0)
            {
                textBox.MaxLength = model.MaxLength;
            }
        }

        return element;
    }

    private static TextBox CreateTextBox(YyControlModel model)
    {
        var textBox = new TextBox { Text = model.Text };
        if (!string.IsNullOrEmpty(model.PasswordChar))
        {
            textBox.PasswordChar = model.PasswordChar[0];
        }

        return textBox;
    }

    private static void ApplyAppearance(Control element, YyControlModel model)
    {
        switch (element)
        {
            case FormGroupBox groupBox:
                groupBox.HeaderFontSize = model.FontSize;
                groupBox.HeaderForeground = Brush(model.Foreground);
                groupBox.Background = Brush(model.Background);
                groupBox.BorderBrush = ControlBorderBrush;
                groupBox.BorderThickness = new Thickness(1);
                groupBox.Padding = new Thickness(8, 4, 8, 4);
                break;
            case TextBlock textBlock:
                textBlock.FontSize = model.FontSize;
                textBlock.Foreground = Brush(model.Foreground);
                break;
            case TemplatedControl control:
                control.FontSize = model.FontSize;
                control.Foreground = Brush(model.Foreground);
                control.Background = Brush(model.Background);
                control.BorderBrush = ControlBorderBrush;
                control.Padding = new Thickness(8, 4, 8, 4);
                break;
        }
    }

    /// <summary>图片框:等比缩放显示图片;未载入图片时显示浅色占位。</summary>
    public static Control CreatePictureBox(
        YyControlModel model,
        string? projectDirectory = null)
    {
        var host = new Grid();
        host.Children.Add(new Border
        {
            Background = PicturePlaceholderBackground,
            Child = new TextBlock
            {
                Text = "图片框",
                Foreground = PicturePlaceholderForeground,
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

    /// <summary>按路径为图片框载入图片(空路径=清空)。</summary>
    public static void SetElementPicture(
        Control element,
        string? path,
        string? projectDirectory = null)
    {
        if (element is not Grid host)
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

    /// <summary>把 @资源/... 标识或工程相对路径解析为位图;失败时返回 null。</summary>
    public static Bitmap? LoadImageSource(
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
            return new Bitmap(path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>易语颜色文本(#RRGGBB/命名色/Transparent)转 Avalonia 画刷。</summary>
    public static IBrush Brush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Brushes.Transparent;
        }

        try
        {
            return global::Avalonia.Media.Brush.Parse(value);
        }
        catch (Exception exception) when (
            exception is FormatException
                or ArgumentException
                or NotSupportedException)
        {
            return Brushes.Transparent;
        }
    }

    /// <summary>读取控件当前展示的文本(预览事件运行时使用)。</summary>
    public static string GetElementText(Control element) => element switch
    {
        TextBox textBox => textBox.Text ?? string.Empty,
        TextBlock textBlock => textBlock.Text ?? string.Empty,
        FormGroupBox groupBox => groupBox.Header,
        ComboBox comboBox => comboBox.SelectedItem?.ToString() ?? string.Empty,
        ListBox listBox => listBox.SelectedItem?.ToString() ?? string.Empty,
        // CheckBox/RadioButton 在 Avalonia 中派生自 Button，Button 分支覆盖其 Content。
        Button button => button.Content?.ToString() ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>把文本写回控件(预览事件运行时使用)。</summary>
    public static void SetElementText(Control element, string text)
    {
        switch (element)
        {
            case TextBox textBox:
                textBox.Text = text;
                break;
            case TextBlock textBlock:
                textBlock.Text = text;
                break;
            case FormGroupBox groupBox:
                groupBox.Header = text;
                break;
            // CheckBox/RadioButton 在 Avalonia 中派生自 Button，Button 分支覆盖其 Content。
            case Button button:
                button.Content = text;
                break;
        }
    }

    /// <summary>按逗号(中英文)拆分下拉框/列表框的选项文本。</summary>
    public static string[] SplitItems(string text) =>
        text.Split(
            [',', '，'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
}

/// <summary>
/// 分组框: Avalonia 没有内置 GroupBox，用带标题的 Border 复刻经典窗体观感。
/// </summary>
public sealed class FormGroupBox : Border
{
    private readonly TextBlock _header = new()
    {
        VerticalAlignment = VerticalAlignment.Top
    };

    public FormGroupBox()
    {
        var host = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        host.Children.Add(_header);
        Child = host;
    }

    public string Header
    {
        get => _header.Text ?? string.Empty;
        set => _header.Text = value;
    }

    public double HeaderFontSize
    {
        get => _header.FontSize;
        set => _header.FontSize = value;
    }

    public IBrush? HeaderForeground
    {
        get => _header.Foreground;
        set => _header.Foreground = value;
    }
}
