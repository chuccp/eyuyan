using Avalonia.Controls;

namespace Yy.Studio.Avalonia.Platforms;

/// <summary>共享顶栏中随宿主平台切换的窗口按钮控件。</summary>
internal sealed record WindowChromeControls(
    Control AppBadge,
    Control MacTrafficLights,
    Control WindowsCaptionButtons,
    Control MaximizeIcon,
    Control RestoreIcon);

/// <summary>
/// 顶栏平台适配:Windows/Linux 在右侧显示方形窗口按钮,macOS 在左侧显示红绿灯。
/// 仅本适配层按宿主系统分支,主窗口和 XAML 共享同一套顶栏结构。
/// </summary>
internal static class WindowChromeAdapter
{
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static void Apply(WindowChromeControls controls)
    {
        var mac = IsMacOS;
        controls.AppBadge.IsVisible = !mac;
        controls.MacTrafficLights.IsVisible = mac;
        controls.WindowsCaptionButtons.IsVisible = !mac;
    }

    public static void UpdateMaximizeButtons(
        WindowChromeControls controls,
        bool maximized)
    {
        if (IsMacOS)
        {
            return;
        }

        controls.MaximizeIcon.IsVisible = !maximized;
        controls.RestoreIcon.IsVisible = maximized;
    }
}
