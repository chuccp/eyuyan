using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yy.Studio;

/// <summary>与主窗口一致的对话框标题栏控件。</summary>
public static class IdeDialogChrome
{
    public static Button CreateCloseButton(Action close)
    {
        var icon = new Path
        {
            Width = 10,
            Height = 10,
            Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
            StrokeThickness = 1.2,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetBinding(
            Shape.StrokeProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(Button),
                    1)
            });

        var button = new Button
        {
            Width = 46,
            Height = 38,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Application.Current.TryFindResource("MutedBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(111, 115, 122)),
            Content = icon,
            ToolTip = "关闭",
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = CreateTemplate()
        };
        AutomationProperties.SetName(button, "关闭对话框");
        button.Click += (_, _) => close();
        return button;
    }

    private static ControlTemplate CreateTemplate()
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        chrome.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = chrome
        };
        AddTrigger(template, "IsMouseOver", Color.FromRgb(196, 43, 28), Brushes.White);
        AddTrigger(template, "IsPressed", Color.FromRgb(165, 38, 24), Brushes.White);
        return template;
    }

    private static void AddTrigger(
        ControlTemplate template,
        string propertyName,
        Color background,
        Brush foreground)
    {
        var trigger = new Trigger
        {
            Property = propertyName == "IsMouseOver"
                ? UIElement.IsMouseOverProperty
                : ButtonBase.IsPressedProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter
        {
            TargetName = "Chrome",
            Property = Border.BackgroundProperty,
            Value = new SolidColorBrush(background)
        });
        trigger.Setters.Add(new Setter
        {
            Property = Control.ForegroundProperty,
            Value = foreground
        });
        template.Triggers.Add(trigger);
    }
}
