using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>新建、属性和数据对话框共用的 IDEA 浅色表面与控件工厂。</summary>
internal static class ProjectTheme
{
    public static readonly IBrush Workbench = Brush("#F4F7FB");
    public static readonly IBrush CardBrush = Brushes.White;
    public static readonly IBrush FaceBrush = Brush("#F2F3F5");
    public static readonly IBrush EditorBrush = Brush("#FCFCFD");
    public static readonly IBrush AccentBrush = Brush("#3574F0");
    public static readonly IBrush TextBrush = Brush("#1F2328");
    public static readonly IBrush MutedBrush = Brush("#6F737A");
    public static readonly IBrush BorderBrush = Brush("#D8DBDF");
    public static readonly IBrush SoftBorderBrush = Brush("#E6EAF0");
    public static readonly IBrush ErrorBrush = Brush("#C62828");
    public static readonly IBrush SelectionBrush = Brush("#E8EEF7");

    public static Window CreateWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 520),
            MinHeight = Math.Min(height, 380),
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Workbench
        };
    }

    public static Border Card(Control child, double padding = 20, bool border = true)
    {
        return new Border
        {
            Background = CardBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(padding),
            BorderBrush = border ? SoftBorderBrush : null,
            BorderThickness = border ? new Thickness(1) : default,
            Child = child
        };
    }

    public static TextBlock Heading(string text, double size = 22)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush
        };
    }

    public static TextBlock Muted(string text, double size = 11.5)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static TextBlock FieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 5)
        };
    }

    public static TextBox Input(string? text = null, string? watermark = null)
    {
        return new TextBox
        {
            Text = text ?? string.Empty,
            Watermark = watermark,
            Background = EditorBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6),
            MinHeight = 32,
            Foreground = TextBrush
        };
    }

    public static TextBox ReadOnlyInput(string text)
    {
        return new TextBox
        {
            Text = text,
            IsReadOnly = true,
            Background = FaceBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6),
            MinHeight = 32,
            Foreground = MutedBrush
        };
    }

    public static ComboBox Combo(IEnumerable<object> items, object? selected = null)
    {
        var combo = new ComboBox
        {
            Background = EditorBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5),
            MinHeight = 32,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }

        if (selected is not null)
        {
            combo.SelectedItem = selected;
        }

        return combo;
    }

    public static Button PrimaryButton(string text, Action? onClick = null)
    {
        var button = StyleButton(text, primary: true);
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    public static Button SecondaryButton(string text, Action? onClick = null)
    {
        var button = StyleButton(text, primary: false);
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    public static Button LinkButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = AccentBrush,
            FontSize = 11.5,
            Padding = new Thickness(8, 3),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Grid Form(string labelColumnWidth = "132,*")
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(labelColumnWidth)
        };
    }

    public static void AddFormRow(
        Grid form,
        int row,
        string label,
        Control input,
        double bottomMargin = 14)
    {
        while (form.RowDefinitions.Count <= row)
        {
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, bottomMargin)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        form.Children.Add(text);
        input.Margin = new Thickness(0, 0, 0, bottomMargin);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        form.Children.Add(input);
    }

    public static void AddStackedField(
        Grid form,
        int row,
        string label,
        Control input,
        double bottomMargin = 14)
    {
        while (form.RowDefinitions.Count <= row * 2 + 1)
        {
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var text = FieldLabel(label);
        Grid.SetRow(text, row * 2);
        Grid.SetColumnSpan(text, 2);
        form.Children.Add(text);
        input.Margin = new Thickness(0, 0, 0, bottomMargin);
        Grid.SetRow(input, row * 2 + 1);
        Grid.SetColumnSpan(input, 2);
        form.Children.Add(input);
    }

    public static Grid Footer(Control? left, Control right)
    {
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 15, 0, 0)
        };
        if (left is not null)
        {
            footer.Children.Add(left);
        }

        Grid.SetColumn(right, 1);
        footer.Children.Add(right);
        return footer;
    }

    public static StackPanel ActionRow(params Control[] controls)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    public static Border HintCard(Control child, double padding = 12)
    {
        return new Border
        {
            Background = FaceBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(padding),
            Child = child
        };
    }

    public static async Task ShowMessageAsync(
        Window owner,
        string title,
        string message,
        bool error = false)
    {
        var dialog = new ProjectMessageDialog(title, message, error, confirm: false);
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message)
    {
        var dialog = new ProjectMessageDialog(title, message, error: false, confirm: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static Button StyleButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 86,
            Height = 30,
            Padding = new Thickness(16, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = primary ? AccentBrush : FaceBrush,
            BorderBrush = primary ? AccentBrush : BorderBrush,
            Foreground = primary ? Brushes.White : TextBrush,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}

/// <summary>不依赖平台 MessageBox 的轻量提示/确认窗口。</summary>
internal sealed class ProjectMessageDialog : Window
{
    public ProjectMessageDialog(string title, string message, bool error, bool confirm)
    {
        Title = title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;
        ShowInTaskbar = false;

        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = ProjectTheme.TextBrush
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = error ? ProjectTheme.ErrorBrush : ProjectTheme.TextBrush
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        if (confirm)
        {
            buttons.Children.Add(ProjectTheme.SecondaryButton("取消", () => Close(false)));
            buttons.Children.Add(ProjectTheme.PrimaryButton("确定", () => Close(true)));
        }
        else
        {
            buttons.Children.Add(ProjectTheme.PrimaryButton("确定", () => Close()));
        }

        panel.Children.Add(buttons);
        Content = new Border
        {
            Background = ProjectTheme.CardBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(16),
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }
}
