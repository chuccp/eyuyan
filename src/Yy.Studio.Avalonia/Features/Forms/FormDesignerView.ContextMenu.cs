using Avalonia.Controls;
using Avalonia.Input;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>窗体设计器画布右键菜单:添加控件、剪贴、层次、对齐、删除与预览。</summary>
public partial class FormDesignerView
{
    private ContextMenu? _canvasMenu;

    private void WireCanvasContextMenu()
    {
        _canvasMenu = BuildCanvasMenu();
        DesignerCanvas.ContextMenu = _canvasMenu;
        DesignerCanvas.PointerPressed += OnCanvasContextMenuPointer;
    }

    private void OnCanvasContextMenuPointer(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(DesignerCanvas).Properties.IsRightButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(DesignerCanvas);
        var control = HitTestControl(point);
        if (control is not null && !_selection.Contains(control))
        {
            SelectControl(control);
        }

        RefreshCanvasMenuState();
    }

    private void RefreshCanvasMenuState()
    {
        if (_canvasMenu is null)
        {
            return;
        }

        var hasSelection = _selection.Count > 0;
        var multi = _selection.Count > 2;
        foreach (var item in _canvasMenu.Items)
        {
            switch (item)
            {
                case MenuItem { Tag: string tag } menu:
                    switch (tag)
                    {
                        case "剪切":
                        case "复制":
                        case "删除":
                        case "置前":
                        case "置后":
                        case "左对齐":
                        case "顶对齐":
                            menu.IsEnabled = hasSelection;
                            break;
                        case "水平均分":
                        case "垂直均分":
                            menu.IsEnabled = multi;
                            break;
                        case "粘贴":
                            menu.IsEnabled = _editor?.ClipboardCount > 0;
                            break;
                        case "网格吸附":
                            menu.IsChecked = SnapToGrid;
                            break;
                    }

                    break;
            }
        }
    }

    private ContextMenu BuildCanvasMenu()
    {
        var menu = new ContextMenu();

        var insert = new MenuItem { Header = "添加控件" };
        foreach (var (glyph, display, type, supported) in ToolboxCatalog)
        {
            if (type is null || !supported)
            {
                continue;
            }

            var item = new MenuItem { Header = $"{display}" };
            var capturedType = type;
            item.Click += (_, _) => AddControl(capturedType);
            insert.Items.Add(item);
        }

        menu.Items.Add(insert);
        menu.Items.Add(new Separator());

        menu.Items.Add(ActionItem("剪切", "Ctrl+X", () => CopySelection(cut: true), "剪切"));
        menu.Items.Add(ActionItem("复制", "Ctrl+C", () => CopySelection(cut: false), "复制"));
        menu.Items.Add(ActionItem("粘贴", "Ctrl+V", PasteSelection, "粘贴"));
        menu.Items.Add(ActionItem("全选", "Ctrl+A", SelectAll, "全选"));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("置前", string.Empty, BringSelectionToFront, "置前"));
        menu.Items.Add(ActionItem("置后", string.Empty, SendSelectionToBack, "置后"));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("左对齐", string.Empty, () => AlignSelection(FormAlignment.Left), "左对齐"));
        menu.Items.Add(ActionItem("顶对齐", string.Empty, () => AlignSelection(FormAlignment.Top), "顶对齐"));
        menu.Items.Add(ActionItem(
            "水平均分", string.Empty, () => DistributeSelection(horizontal: true), "水平均分"));
        menu.Items.Add(ActionItem(
            "垂直均分", string.Empty, () => DistributeSelection(horizontal: false), "垂直均分"));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("删除所选控件", "Delete", DeleteSelection, "删除"));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("预览窗体", "Ctrl+Enter", RequestPreview, "预览"));
        menu.Items.Add(ActionItem("查看用户代码", string.Empty, RequestViewCode, "用户代码"));
        menu.Items.Add(new Separator());

        var snap = new MenuItem
        {
            Header = "网格吸附",
            Tag = "网格吸附",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = SnapToGrid
        };
        snap.Click += (_, _) =>
        {
            SnapToGrid = !SnapToGrid;
            SetStatus(SnapToGrid ? "已启用 8px 网格吸附。" : "已关闭网格吸附。");
            RefreshCanvasMenuState();
        };
        menu.Items.Add(snap);

        menu.Opening += (_, _) => RefreshCanvasMenuState();
        return menu;
    }

    private static MenuItem ActionItem(
        string header,
        string gesture,
        Action action,
        string tag)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = tag
        };
        if (!string.IsNullOrEmpty(gesture))
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        item.Click += (_, _) => action();
        return item;
    }
}
