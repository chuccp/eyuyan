using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Yy.Compiler;
using Yy.Studio.Avalonia.Features.Projects;

namespace Yy.Studio.Avalonia.Features.Data;

/// <summary>数据表的独立宿主窗口；主窗口也可直接托管 <see cref="DataTableView"/>。</summary>
public sealed class DataTableViewWindow : Window
{
    public DataTableViewWindow(YyProject project, string kind, Action? changed = null)
    {
        Title = "数据表 · " + kind;
        Width = 980;
        Height = 620;
        MinWidth = 640;
        MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ProjectTheme.Workbench;
        ShowInTaskbar = false;

        View = new DataTableView();
        View.Load(project, kind, changed ?? project.SaveManifest);
        Content = new Border
        {
            Margin = new Thickness(12),
            Background = Brushes.White,
            BorderBrush = ProjectTheme.SoftBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = View
        };
    }

    public DataTableView View { get; }
}
