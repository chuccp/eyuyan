using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yy.Studio.Avalonia.Platforms;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private WindowChromeControls _chrome = null!;

    /// <summary>在 InitializeComponent 之后按宿主平台装配顶栏窗口按钮。</summary>
    private void InitializePlatformChrome()
    {
        _chrome = new WindowChromeControls(
            WindowsAppBadge,
            MacTrafficLights,
            WindowsCaptionButtons,
            CaptionMaximizeIcon,
            CaptionRestoreIcon);
        WindowChromeAdapter.Apply(_chrome);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) =>
        ToggleMaximize();

    private void OnMinimizeWindow(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreWindow(
        object? sender,
        RoutedEventArgs e) =>
        ToggleMaximize();

    private void OnCloseWindow(object? sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        if (_chrome is null)
        {
            return;
        }

        WindowChromeAdapter.UpdateMaximizeButtons(
            _chrome,
            WindowState == WindowState.Maximized);
    }
}
