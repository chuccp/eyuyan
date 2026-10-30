using System.Windows;
using System.Windows.Shell;

namespace Yy.Studio;

/// <summary>自定义顶栏的最小化、最大化、还原和关闭操作。</summary>
public partial class MainWindow
{
    private void OnMinimizeWindowClick(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreWindowClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseWindowClick(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (CaptionMaximizeIcon is null || CaptionRestoreIcon is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        CaptionMaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        CaptionRestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        CaptionMaximizeButton.ToolTip = maximized ? "还原" : "最大化";
    }
}
