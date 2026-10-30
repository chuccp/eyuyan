using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;



namespace Yy.Studio;

/// <summary>
/// 设计器选择、剪贴板、层级、撤销重做与历史状态。
/// </summary>
public partial class MainWindow : Window
{
    private void OnDesignerCopy(object sender, ExecutedRoutedEventArgs e) =>
        CopySelectedControls(cut: false);

    private void OnDesignerCut(object sender, ExecutedRoutedEventArgs e) =>
        CopySelectedControls(cut: true);

    private void OnDesignerPaste(object sender, ExecutedRoutedEventArgs e) =>
        PasteDesignerControls();

    private void CopySelectedControls(bool cut)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        _designerClipboard = _selectedControls.Select(control => control.Clone()).ToList();
        if (cut)
        {
            DeleteSelectedControls();
            SetStatus($"已剪切 {_designerClipboard.Count} 个控件", ReadyBrush);
        }
        else
        {
            SetStatus($"已复制 {_designerClipboard.Count} 个控件", ReadyBrush);
        }
    }

    private void PasteDesignerControls()
    {
        if (_designForm is null || _designerClipboard.Count == 0)
        {
            return;
        }

        var pasted = new List<YyControlModel>();
        foreach (var prototype in _designerClipboard)
        {
            var model = prototype.Clone();
            var sequence = 1;
            while (_designForm.Controls.Any(control =>
                       control.Name == GetControlBaseName(model.Type) + sequence))
            {
                sequence++;
            }

            model.Name = GetControlBaseName(model.Type) + sequence;
            model.ClickEvent = null;
            model.TextChangedEvent = null;
            model.SelectionChangedEvent = null;
            model.CheckedChangedEvent = null;
            model.X = Clamp(model.X + 12, 0, Math.Max(0, _designForm.Width - model.Width));
            model.Y = Clamp(model.Y + 12, 0, Math.Max(0, _designForm.Height - model.Height));
            _designForm.Controls.Add(model);
            AddControlVisual(model);
            pasted.Add(model);
        }

        SetDesignerSelection(pasted);
        DesignerChanged();
        SetStatus($"已粘贴 {pasted.Count} 个控件", ReadyBrush);
    }

    private void OnSelectAllDesignerControls(object sender, RoutedEventArgs e)
    {
        if (_designForm is not null)
        {
            SetDesignerSelection(_designForm.Controls);
        }
    }

    private void OnDesignerBringFront(object sender, RoutedEventArgs e) =>
        ReorderSelectedControls(toFront: true);

    private void OnDesignerSendBack(object sender, RoutedEventArgs e) =>
        ReorderSelectedControls(toFront: false);

    private void ReorderSelectedControls(bool toFront)
    {
        if (_designForm is null || _selectedControls.Count == 0)
        {
            return;
        }

        var selected = _designForm.Controls.Where(_selectedControls.Contains).ToList();
        foreach (var control in selected)
        {
            _designForm.Controls.Remove(control);
        }

        if (toFront)
        {
            _designForm.Controls.AddRange(selected);
        }
        else
        {
            _designForm.Controls.InsertRange(0, selected);
        }

        RenderDesignerForm();
        UpdateDesignerHistoryButtons();
        UpdateDesignerTitleChrome();
        DesignerChanged();
        SetStatus(toFront ? "已置前" : "已置后", ReadyBrush);
    }

    private void SelectDesignerControl(YyControlModel control, bool additive = false)
    {
        if (additive)
        {
            if (!_selectedControls.Remove(control))
            {
                _selectedControls.Add(control);
            }
        }
        else
        {
            _selectedControls.Clear();
            _selectedControls.Add(control);
        }

        if (_selectedControls.Count == 0)
        {
            SelectDesignerForm();
            return;
        }

        _selectedControl = _selectedControls.Contains(control)
            ? control
            : _selectedControls[^1];
        _selectedElement = FindControlElement(_selectedControl);
        PopulateDesignerProperties();
        UpdateSelectionVisuals();
        DesignerCanvas.Focus();
        SetStatus(
            _selectedControls.Count == 1
                ? $"已选择控件“{_selectedControl.Name}”"
                : $"已选择 {_selectedControls.Count} 个控件",
            ReadyBrush);
    }

    private void SetDesignerSelection(IEnumerable<YyControlModel> controls)
    {
        _selectedControls.Clear();
        _selectedControls.AddRange(controls.Distinct());
        if (_selectedControls.Count == 0)
        {
            SelectDesignerForm();
            return;
        }

        _selectedControl = _selectedControls[^1];
        _selectedElement = FindControlElement(_selectedControl);
        PopulateDesignerProperties();
        UpdateSelectionVisuals();
        SetStatus($"已选择 {_selectedControls.Count} 个控件", ReadyBrush);
    }

    private FrameworkElement? FindControlElement(YyControlModel control)
    {
        return DesignerCanvas.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => ReferenceEquals(element.Tag, control));
    }

    private void SelectDesignerForm()
    {
        StudioTrace.Write("DBG select-designer-form");
        _selectedControls.Clear();
        _selectedControl = null;
        _selectedElement = null;
        ClearSelectionVisuals();
        PopulateDesignerProperties();
        ShowHint("窗口用来设计程序的用户界面及对界面事件进行处理。");
    }

    private void UpdateSelectedControlVisual()
    {
        if (_selectedControls.Count == 0)
        {
            return;
        }

        foreach (var control in _selectedControls)
        {
            var element = FindControlElement(control);
            if (element is null)
            {
                continue;
            }

            element.Width = control.Width;
            element.Height = control.Height;
            Canvas.SetLeft(element, control.X);
            Canvas.SetTop(element, control.Y);
        }

        UpdateSelectionVisuals();
    }

    private void RefreshSelectedControlElement()
    {
        if (_selectedControl is null)
        {
            return;
        }

        if (_selectedElement is not null)
        {
            DesignerCanvas.Children.Remove(_selectedElement);
        }

        AddControlVisual(_selectedControl);
        _selectedElement = DesignerCanvas.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => ReferenceEquals(element.Tag, _selectedControl));
        UpdateSelectionVisuals();
    }

    private void ClearSelectionVisuals()
    {
        foreach (var visual in _selectionVisuals)
        {
            DesignerCanvas.Children.Remove(visual);
        }

        _selectionVisuals.Clear();
    }

    private void UpdateSelectionVisuals()
    {
        ClearSelectionVisuals();
        if (_selectedControls.Count == 0)
        {
            return;
        }

        foreach (var control in _selectedControls)
        {
            var outline = new Border
            {
                Width = control.Width,
                Height = control.Height,
                BorderBrush = YyFormRenderer.Brush(
                    ReferenceEquals(control, _selectedControl) ? "#000000" : "#808080"),
                BorderThickness = new Thickness(1.5),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, control.X);
            Canvas.SetTop(outline, control.Y);
            Panel.SetZIndex(outline, 1000);
            DesignerCanvas.Children.Add(outline);
            _selectionVisuals.Add(outline);
        }

        if (_selectedControl is null || _selectedControls.Count != 1)
        {
            return;
        }

        foreach (var direction in Enum.GetValues<ResizeDirection>())
        {
            var handle = new Border
            {
                Width = 8,
                Height = 8,
                Background = YyFormRenderer.Brush("#000000"),
                BorderBrush = YyFormRenderer.Brush("#FFFFFF"),
                BorderThickness = new Thickness(1),
                Tag = new ResizeHandleTag(direction)
            };
            var point = GetResizeHandlePoint(direction);
            Canvas.SetLeft(handle, point.X - 4);
            Canvas.SetTop(handle, point.Y - 4);
            Panel.SetZIndex(handle, 1001);
            DesignerCanvas.Children.Add(handle);
            _selectionVisuals.Add(handle);
        }
    }

    private Point GetResizeHandlePoint(ResizeDirection direction)
    {
        if (_selectedControl is null)
        {
            return new Point();
        }

        var left = _selectedControl.X;
        var top = _selectedControl.Y;
        var centerX = left + _selectedControl.Width / 2;
        var centerY = top + _selectedControl.Height / 2;
        var right = left + _selectedControl.Width;
        var bottom = top + _selectedControl.Height;
        return direction switch
        {
            ResizeDirection.NorthWest => new Point(left, top),
            ResizeDirection.North => new Point(centerX, top),
            ResizeDirection.NorthEast => new Point(right, top),
            ResizeDirection.East => new Point(right, centerY),
            ResizeDirection.SouthEast => new Point(right, bottom),
            ResizeDirection.South => new Point(centerX, bottom),
            ResizeDirection.SouthWest => new Point(left, bottom),
            ResizeDirection.West => new Point(left, centerY),
            _ => new Point()
        };
    }

    private void DesignerChanged()
    {
        // 快照永远写回窗体自己的文档:创建事件后当前文档可能已是 .yyc 代码,
        // 若盲目写 _currentDocument,窗体 XML 会覆盖代码文档(根元素校验失败)。
        var target = _designerDocument ?? _currentDocument;
        if (_designForm is null || target is null)
        {
            return;
        }

        var snapshot = YyFormSerializer.Serialize(_designForm);
        var previous = target.LastDesignerSnapshot;
        if (previous is not null && !string.Equals(previous, snapshot, StringComparison.Ordinal))
        {
            target.DesignerUndo.Push(previous);
            target.DesignerRedo.Clear();
        }

        target.LastDesignerSnapshot = snapshot;
        target.Text = snapshot;
        UpdateDesignerHistoryButtons();
        UpdateDocumentIdentity();
    }

    private void OnDesignerUndoClick(object sender, RoutedEventArgs e) => UndoDesigner();

    private void OnDesignerRedoClick(object sender, RoutedEventArgs e) => RedoDesigner();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1)
        {
            BottomTabs.SelectedIndex = 0;
            UpdateEditorHelp();
            e.Handled = true;
            return;
        }

        // 官方快捷键:Ctrl+E 菜单编辑器、Ctrl+L 插入局部变量、Ctrl+Enter 预览被设计窗口、Alt+1..4 面板
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.E)
        {
            OnMenuEditorClick(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
        {
            OnInsertLocalVariableClick(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Enter)
        {
            PreviewCurrentForm();
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && key is Key.D1 or Key.D2 or Key.D3 or Key.D4)
        {
            switch (key)
            {
                case Key.D1:
                    ProjectExplorerPanel.Visibility = Visibility.Visible;
                    WorkTabs.SelectedIndex = 1;
                    break;
                case Key.D2:
                    ProjectExplorerPanel.Visibility = Visibility.Visible;
                    WorkTabs.SelectedIndex = 0;
                    break;
                case Key.D3:
                    OnToggleExplorer(sender, e);
                    break;
                case Key.D4:
                    OnToggleToolboxPanel(sender, e);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (_currentDocument?.IsForm != true ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            e.Key is not (Key.Z or Key.Y))
        {
            return;
        }

        if (e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            RedoDesigner();
        }
        else
        {
            UndoDesigner();
        }

        e.Handled = true;
    }

    private void UndoDesigner()
    {
        if (_currentDocument is null || _currentDocument.DesignerUndo.Count == 0)
        {
            return;
        }

        var document = _designerDocument ?? _currentDocument!;
        var current = document.LastDesignerSnapshot;
        var undoTarget = document.DesignerUndo.Pop();
        if (current is not null)
        {
            document.DesignerRedo.Push(current);
        }

        RestoreDesignerSnapshot(undoTarget);
        SetStatus("已撤销窗体设计操作", ReadyBrush);
    }

    private void RedoDesigner()
    {
        if (_currentDocument is null || _currentDocument.DesignerRedo.Count == 0)
        {
            return;
        }

        var document = _designerDocument ?? _currentDocument!;
        var current = document.LastDesignerSnapshot;
        var redoTarget = document.DesignerRedo.Pop();
        if (current is not null)
        {
            document.DesignerUndo.Push(current);
        }

        RestoreDesignerSnapshot(redoTarget);
        SetStatus("已重做窗体设计操作", ReadyBrush);
    }

    private void RestoreDesignerSnapshot(string snapshot)
    {
        var document = _designerDocument ?? _currentDocument;
        if (document is null)
        {
            return;
        }

        var selectedName = _selectedControl?.Name;
        _designForm = LoadFormDocument(snapshot, document.FilePath);
        document.LastDesignerSnapshot = snapshot;
        document.Text = snapshot;
        RenderDesignerForm();
        var selected = string.IsNullOrWhiteSpace(selectedName)
            ? null
            : _designForm.Controls.FirstOrDefault(control =>
                string.Equals(control.Name, selectedName, StringComparison.Ordinal));
        if (selected is not null)
        {
            SelectDesignerControl(selected);
        }

        UpdateDesignerHistoryButtons();
        UpdateDocumentIdentity();
    }

    private void UpdateDesignerHistoryButtons()
    {
        DesignerUndoButton.IsEnabled = _currentDocument?.DesignerUndo.Count > 0;
        DesignerRedoButton.IsEnabled = _currentDocument?.DesignerRedo.Count > 0;
    }

}
