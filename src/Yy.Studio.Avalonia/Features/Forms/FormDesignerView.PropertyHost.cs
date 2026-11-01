using Avalonia.Controls;

namespace Yy.Studio.Avalonia.Features.Forms;

public sealed partial class FormDesignerView
{
    /// <summary>把属性夹移交给宿主的右侧工具窗口，设计画布获得完整宽度。</summary>
    public Control TakePropertyPanel()
    {
        if (DesignerPropertyPanel.Parent is Grid grid)
        {
            grid.Children.Remove(DesignerPropertyPanel);
            if (grid.ColumnDefinitions.Count > 1)
            {
                grid.ColumnDefinitions[1].Width = new GridLength(0);
            }
        }

        return DesignerPropertyPanel;
    }
}
