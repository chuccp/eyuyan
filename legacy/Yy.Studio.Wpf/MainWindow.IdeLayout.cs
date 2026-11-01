using System.Windows;
using System.Windows.Controls;

namespace Yy.Studio;

/// <summary>把底部输出工具窗放入中央工作区，使左右工具窗贯穿整个 IDE 高度。</summary>
public partial class MainWindow
{
    private void ConfigureIdeWorkspaceLayout()
    {
        if (ReferenceEquals(BottomToolWindowHost.Content, BottomToolWindowPanel))
        {
            return;
        }

        if (BottomToolWindowPanel.Parent is DockPanel host)
        {
            host.Children.Remove(BottomToolWindowPanel);
        }

        BottomToolWindowPanel.ClearValue(FrameworkElement.HeightProperty);
        BottomToolWindowPanel.ClearValue(DockPanel.DockProperty);
        BottomToolWindowPanel.VerticalAlignment = VerticalAlignment.Stretch;
        BottomToolWindowHost.Content = BottomToolWindowPanel;
    }
}
