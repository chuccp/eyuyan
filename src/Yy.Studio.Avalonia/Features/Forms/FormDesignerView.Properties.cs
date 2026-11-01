using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>窗体设计器属性夹、事件选择与工具栏入口。</summary>
public partial class FormDesignerView
{
    private void PopulatePropertyPanel()
    {
        var previous = _updatingUi;
        _updatingUi = true;
        try
        {
            BuildPropertyObjectCombo();
            PropertyRowsHost.Children.Clear();
            _propertyRows.Clear();
            _propertyEditors.Clear();

            if (_editor is null)
            {
                ResetEventSelectorCore();
                PropertyHintText.Text = string.Empty;
                return;
            }

            if (_selection.Count > 1)
            {
                ResetEventSelectorCore();
                PropertyHintText.Text =
                    $"已选择 {_selection.Count} 个控件；多选时支持拖动、缩放、删除和键盘微调。";
                return;
            }

            foreach (var row in _editor.BuildPropertyRows(_selectedControl))
            {
                _propertyRows.Add(row);
                PropertyRowsHost.Children.Add(CreatePropertyEditor(row));
            }

            BuildEventSelectorItems(_selectedControl);
            PropertyHintText.Text = _selectedControl is null
                ? "窗口用来设计程序的用户界面及对界面事件进行处理。"
                : string.Empty;
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private void BuildPropertyObjectCombo()
    {
        PropertyObjectCombo.Items.Clear();
        if (_editor is null)
        {
            return;
        }

        var formItem = new ComboBoxItem
        {
            Content = _editor.Form.Name + "（窗口）",
            Tag = null
        };
        PropertyObjectCombo.Items.Add(formItem);
        ComboBoxItem? selectedItem = formItem;
        foreach (var control in _editor.Form.Controls)
        {
            var item = new ComboBoxItem
            {
                Content = control.Name + "（" + YyControlTypes.GetDisplayName(control.Type) + "）",
                Tag = control
            };
            PropertyObjectCombo.Items.Add(item);
            if (ReferenceEquals(control, _selectedControl))
            {
                selectedItem = item;
            }
        }

        PropertyObjectCombo.SelectedItem = selectedItem;
    }

    private void RefreshPropertyObjectCombo()
    {
        var previous = _updatingUi;
        _updatingUi = true;
        try
        {
            BuildPropertyObjectCombo();
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private Control CreatePropertyEditor(FormPropertyRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(row.IsColor ? "88,*,20" : "88,*"),
            Margin = new Thickness(0, 1)
        };
        grid.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 71, 90)),
            VerticalAlignment = VerticalAlignment.Center
        });

        Control editor;
        if (row.IsBool)
        {
            var combo = new ComboBox
            {
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = row
            };
            combo.Items.Add(FormPropertyCatalog.BoolText(true));
            combo.Items.Add(FormPropertyCatalog.BoolText(false));
            combo.SelectedIndex = FormPropertyCatalog.ParseBool(row.ValueText) ? 0 : 1;
            combo.SelectionChanged += (_, _) =>
            {
                if (_updatingUi)
                {
                    return;
                }

                CommitBoolRow(row, combo);
            };
            combo.GotFocus += (_, _) => ShowRowHint(row);
            editor = combo;
        }
        else
        {
            var box = new TextBox
            {
                Text = row.ValueText,
                FontSize = 12,
                MinHeight = 24,
                Tag = row
            };
            box.LostFocus += (_, _) => CommitTextRow(row, box);
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTextRow(row, box);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    box.Text = row.InitialValue;
                }
            };
            box.GotFocus += (_, _) => ShowRowHint(row);
            editor = box;

            if (row.IsColor)
            {
                var swatch = new Border
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(4, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(181, 192, 205)),
                    BorderThickness = new Thickness(1),
                    Background = YyFormRenderer.Brush(row.ValueText),
                    VerticalAlignment = VerticalAlignment.Center
                };
                box.TextChanged += (_, _) =>
                    swatch.Background = YyFormRenderer.Brush(box.Text);
                Grid.SetColumn(swatch, 2);
                grid.Children.Add(swatch);
            }
        }

        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        _propertyEditors.Add((row, editor));
        return grid;
    }

    private void CommitTextRow(FormPropertyRow row, TextBox box)
    {
        if (_updatingUi || _editor is null)
        {
            return;
        }

        var text = box.Text ?? string.Empty;
        if (string.Equals(text, row.InitialValue, StringComparison.Ordinal))
        {
            return;
        }

        CommitRow(row, text, () => box.Text = row.InitialValue);
    }

    private void CommitBoolRow(FormPropertyRow row, ComboBox combo)
    {
        if (_updatingUi || _editor is null)
        {
            return;
        }

        var text = combo.SelectedIndex == 0
            ? FormPropertyCatalog.BoolText(true)
            : FormPropertyCatalog.BoolText(false);
        if (string.Equals(text, row.InitialValue, StringComparison.Ordinal))
        {
            return;
        }

        CommitRow(
            row,
            text,
            () => combo.SelectedIndex =
                FormPropertyCatalog.ParseBool(row.InitialValue) ? 0 : 1);
    }

    private void CommitRow(FormPropertyRow row, string text, Action resetEditor)
    {
        if (_editor is null)
        {
            return;
        }

        var before = _editor.BeginEdit();
        if (!_editor.TryApplyProperty(_selectedControl, row.Name, text, out var error))
        {
            resetEditor();
            PropertyHintText.Text = error;
            SetStatus(error);
            return;
        }

        _editor.CommitEdit(before);
        row.ValueText = text;
        row.InitialValue = text;
        RebuildCanvas();
        RefreshPropertyObjectCombo();
        PropertyHintText.Text = row.Description;
        SetStatus("窗体布局已更新");
        NotifyFormChanged();
    }

    private void UpdatePropertyEditorsFromModel()
    {
        if (_editor is null || _selectedControl is null || _propertyEditors.Count == 0)
        {
            return;
        }

        var values = _editor.BuildPropertyRows(_selectedControl)
            .ToDictionary(row => row.Name, row => row.ValueText, StringComparer.Ordinal);
        var previous = _updatingUi;
        _updatingUi = true;
        try
        {
            foreach (var (row, editor) in _propertyEditors)
            {
                if (!values.TryGetValue(row.Name, out var value))
                {
                    continue;
                }

                row.ValueText = value;
                row.InitialValue = value;
                switch (editor)
                {
                    case TextBox box:
                        box.Text = value;
                        break;
                    case ComboBox combo when row.IsBool:
                        combo.SelectedIndex = FormPropertyCatalog.ParseBool(value) ? 0 : 1;
                        break;
                }
            }
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private void BuildEventSelectorItems(YyControlModel? control)
    {
        ResetEventSelectorCore();
        if (_editor is null)
        {
            return;
        }

        IReadOnlyList<FormDesignerEventKind> kinds = control is null
            ? [FormDesignerEventKind.FormLoad, FormDesignerEventKind.FormOpen, FormDesignerEventKind.FormClose]
            : FormDesignerEvents.GetKinds(control.Type);
        foreach (var kind in kinds)
        {
            var handler = control is null
                ? FormDesignerEvents.GetHandler(_editor.Form, kind)
                : FormDesignerEvents.GetHandler(control, kind);
            EventSelectorCombo.Items.Add(new ComboBoxItem
            {
                Content = (string.IsNullOrWhiteSpace(handler) ? string.Empty : "✓ ") +
                          FormDesignerEvents.DisplayName(kind),
                Tag = kind
            });
        }

        EventSelectorCombo.SelectedIndex = 0;
        EventSelectorCombo.IsEnabled = kinds.Count > 0;
    }

    private void ResetEventSelectorCore()
    {
        EventSelectorCombo.Items.Clear();
        EventSelectorCombo.Items.Add(new ComboBoxItem
        {
            Content = "在此处选择加入事件处理子程序",
            Tag = null
        });
        EventSelectorCombo.SelectedIndex = 0;
        EventSelectorCombo.IsEnabled = false;
    }

    private void RefreshEventSelector()
    {
        if (_selection.Count > 1)
        {
            return;
        }

        var previous = _updatingUi;
        _updatingUi = true;
        try
        {
            BuildEventSelectorItems(_selectedControl);
        }
        finally
        {
            _updatingUi = previous;
        }
    }

    private void RequestEvent(YyControlModel control, FormDesignerEventKind kind)
    {
        if (_editor is null)
        {
            return;
        }

        var args = new FormDesignerEventRequestedEventArgs(
            control,
            kind,
            FormDesignerEvents.SuggestedHandlerName(_editor.Form, control, kind),
            FormDesignerEvents.GetHandler(control, kind));
        SetStatus($"事件：{args.SuggestedHandlerName}");
        EventRequested?.Invoke(this, args);
    }

    // ---------- XAML 事件 ----------

    private void OnAddControlClick(object? sender, RoutedEventArgs e)
    {
        if (ToolboxCombo.SelectedItem is ComboBoxItem { Tag: string type })
        {
            AddControl(type);
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) => DeleteSelection();

    private void OnBringFrontClick(object? sender, RoutedEventArgs e) =>
        BringSelectionToFront();

    private void OnSendBackClick(object? sender, RoutedEventArgs e) =>
        SendSelectionToBack();

    private void OnSnapClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is not null)
        {
            _editor.SnapToGrid = SnapCheckBox.IsChecked == true;
        }

        SetStatus(SnapCheckBox.IsChecked == true ? "已启用 8px 网格吸附。" : "已关闭网格吸附。");
    }

    private void OnPropertyObjectChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        if (PropertyObjectCombo.SelectedItem is ComboBoxItem { Tag: YyControlModel control })
        {
            SelectControl(control);
        }
        else
        {
            SelectForm();
        }
    }

    private void OnEventSelectorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _editor is null)
        {
            return;
        }

        if (EventSelectorCombo.SelectedItem is not ComboBoxItem
            {
                Tag: FormDesignerEventKind kind
            })
        {
            return;
        }

        var control = _selectedControl;
        if (control is null && !FormDesignerEvents.IsFormEvent(kind))
        {
            return;
        }

        if (control is not null && FormDesignerEvents.IsFormEvent(kind))
        {
            return;
        }

        var existing = control is null
            ? FormDesignerEvents.GetHandler(_editor.Form, kind)
            : FormDesignerEvents.GetHandler(control, kind);
        var args = new FormDesignerEventRequestedEventArgs(
            control,
            kind,
            FormDesignerEvents.SuggestedHandlerName(_editor.Form, control, kind),
            existing);
        SetStatus($"事件：{args.SuggestedHandlerName}");
        EventRequested?.Invoke(this, args);
        RefreshEventSelector();
    }

    private void ShowRowHint(FormPropertyRow row) =>
        PropertyHintText.Text = row.Description;
}
