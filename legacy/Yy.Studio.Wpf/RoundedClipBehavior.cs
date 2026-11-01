using System.Windows;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>按统一半径裁剪面板内容，形成 IDEA 式内圆角。</summary>
public static class RoundedClipBehavior
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached(
            "Radius",
            typeof(double),
            typeof(RoundedClipBehavior),
            new FrameworkPropertyMetadata(0d, OnRadiusChanged));

    public static double GetRadius(DependencyObject element) =>
        (double)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, double value) =>
        element.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnElementSizeChanged;
        var radius = args.NewValue is double value ? Math.Max(0, value) : 0;
        if (radius <= 0)
        {
            element.Clip = null;
            return;
        }

        element.SizeChanged += OnElementSizeChanged;
        ApplyClip(element, radius);
    }

    private static void OnElementSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is FrameworkElement element)
        {
            ApplyClip(element, GetRadius(element));
        }
    }

    private static void ApplyClip(FrameworkElement element, double radius)
    {
        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }
}
