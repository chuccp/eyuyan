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
/// 属性夹、事件选择与窗体事件入口协调。
/// </summary>
public partial class MainWindow : Window
{
    private void PopulateDesignerProperties()
    {
        StudioTrace.Write($"DBG populate-props guard={_updatingDesignerProperties}");
        if (_designForm is null)
        {
            return;
        }

        _updatingDesignerProperties = true;
        _suppressPropertyCommit = true;
        try
        {
            BuildPropertyObjectCombo();

            if (_selectedControls.Count > 1)
            {
                PropertyRows.ItemsSource = Array.Empty<PropertyRow>();
                ResetEventSelectorPlaceholder();
                EventSelectorCombo.IsEnabled = false;
                return;
            }

            var rows = new List<PropertyRow>();
            if (_selectedControl is null)
            {
                rows.Add(new PropertyRow("名称", _designForm.Name, PropertyCellKind.Identifier, PropertyRowCatalog.HintName));
                rows.Add(new PropertyRow("备注", _designForm.Remark, PropertyCellKind.Text, PropertyRowCatalog.HintRemark));
                rows.Add(new PropertyRow("标题", _designForm.Title, PropertyCellKind.Text, PropertyRowCatalog.HintTitle));
                rows.Add(new PropertyRow("图标", _designForm.Icon, PropertyCellKind.Text, "资源标识，例如 @资源/图片/应用图标"));
                rows.Add(new PropertyRow("宽度", PropertyRowCatalog.FormatNumber(_designForm.Width), PropertyCellKind.Integer, PropertyRowCatalog.HintWidth));
                rows.Add(new PropertyRow("高度", PropertyRowCatalog.FormatNumber(_designForm.Height), PropertyCellKind.Integer, PropertyRowCatalog.HintHeight));
                rows.Add(new PropertyRow("背景", _designForm.Background, PropertyCellKind.Color, PropertyRowCatalog.HintBackground));
                rows.Add(new PropertyRow("边框", _designForm.BorderStyle, PropertyCellKind.Text, PropertyRowCatalog.HintBorderStyle));
                rows.Add(new PropertyRow("可否移动", PropertyRowCatalog.BoolText(_designForm.Movable), PropertyCellKind.Bool, PropertyRowCatalog.HintMovable));
                rows.Add(new PropertyRow("初始位置", _designForm.StartPosition, PropertyCellKind.Text, PropertyRowCatalog.HintStartPosition));
                rows.Add(new PropertyRow("最小化按钮", PropertyRowCatalog.BoolText(_designForm.MinimizeBox), PropertyCellKind.Bool, PropertyRowCatalog.HintMinBox));
                rows.Add(new PropertyRow("最大化按钮", PropertyRowCatalog.BoolText(_designForm.MaximizeBox), PropertyCellKind.Bool, PropertyRowCatalog.HintMaxBox));
                PropertyRows.ItemsSource = rows;
                BuildFormEventSelector();
            }
            else
            {
                var control = _selectedControl;
                rows.Add(new PropertyRow("名称", control.Name, PropertyCellKind.Identifier, PropertyRowCatalog.HintName));
                rows.Add(new PropertyRow("备注", control.Remark, PropertyCellKind.Text, PropertyRowCatalog.HintRemark));
                rows.Add(new PropertyRow("左边", PropertyRowCatalog.FormatNumber(control.X), PropertyCellKind.Integer, PropertyRowCatalog.HintLeft));
                rows.Add(new PropertyRow("顶边", PropertyRowCatalog.FormatNumber(control.Y), PropertyCellKind.Integer, PropertyRowCatalog.HintTop));
                rows.Add(new PropertyRow("宽度", PropertyRowCatalog.FormatNumber(control.Width), PropertyCellKind.Integer, PropertyRowCatalog.HintWidth));
                rows.Add(new PropertyRow("高度", PropertyRowCatalog.FormatNumber(control.Height), PropertyCellKind.Integer, PropertyRowCatalog.HintHeight));
                rows.Add(new PropertyRow("可视", PropertyRowCatalog.BoolText(control.IsVisible), PropertyCellKind.Bool, PropertyRowCatalog.HintVisible));
                rows.Add(new PropertyRow("禁止", PropertyRowCatalog.BoolText(!control.IsEnabled), PropertyCellKind.Bool, PropertyRowCatalog.HintForbidden));
                rows.Add(new PropertyRow("标题", control.Text, PropertyCellKind.Text, PropertyRowCatalog.HintTitle));
                rows.Add(new PropertyRow("字号", PropertyRowCatalog.FormatNumber(control.FontSize), PropertyCellKind.Integer, PropertyRowCatalog.HintFontSize));
                rows.Add(new PropertyRow("前景", control.Foreground, PropertyCellKind.Color, PropertyRowCatalog.HintForeground));
                rows.Add(new PropertyRow("背景", control.Background, PropertyCellKind.Color, PropertyRowCatalog.HintBackground));
                if (control.Type is YyControlTypes.TextBox)
                {
                    rows.Add(new PropertyRow("只读", PropertyRowCatalog.BoolText(control.ReadOnly), PropertyCellKind.Bool, PropertyRowCatalog.HintReadOnly));
                    rows.Add(new PropertyRow("多行", PropertyRowCatalog.BoolText(control.MultiLine), PropertyCellKind.Bool, PropertyRowCatalog.HintMultiLine));
                    rows.Add(new PropertyRow("密码", control.PasswordChar, PropertyCellKind.Text, PropertyRowCatalog.HintPassword));
                    rows.Add(new PropertyRow("最大长度", PropertyRowCatalog.FormatNumber(control.MaxLength), PropertyCellKind.Integer, PropertyRowCatalog.HintMaxLength));
                }

                if (control.Type is YyControlTypes.PictureBox)
                {
                    rows.Add(new PropertyRow("图片", control.Picture, PropertyCellKind.Text, PropertyRowCatalog.HintPicture));
                }

                rows.Add(new PropertyRow(
                    "数据绑定",
                    string.IsNullOrWhiteSpace(control.DataBindingTable)
                        ? string.Empty
                        : control.DataBindingTable + "." + control.DataBindingColumn,
                    PropertyCellKind.Text,
                    PropertyRowCatalog.HintDataBinding));
                if (control.Type is YyControlTypes.CheckBox or YyControlTypes.RadioButton)
                {
                    rows.Add(new PropertyRow("选中", PropertyRowCatalog.BoolText(control.IsChecked), PropertyCellKind.Bool, PropertyRowCatalog.HintChecked));
                }

                PropertyRows.ItemsSource = rows;
                BuildControlEventSelector(control);
            }
        }
        finally
        {
            _updatingDesignerProperties = false;
            // 属性行模板内的组合框在页签可见时才布局实现,其 SelectionChanged 在本方法
            // 返回后才补发;延迟到布局之后再放开提交,抑制这批伪事件。
            Dispatcher.BeginInvoke(
                new Action(() => _suppressPropertyCommit = false),
                DispatcherPriority.Background);
        }
    }

    private void BuildPropertyObjectCombo()
    {
        PropertyObjectCombo.Items.Clear();
        var formItem = new ComboBoxItem
        {
            Content = _designForm!.Name + "（窗口）",
            Tag = null
        };
        PropertyObjectCombo.Items.Add(formItem);
        foreach (var control in _designForm.Controls)
        {
            PropertyObjectCombo.Items.Add(new ComboBoxItem
            {
                Content = control.Name + "（" + YyControlTypes.GetDisplayName(control.Type) + "）",
                Tag = control
            });
        }

        PropertyObjectCombo.SelectedItem = _selectedControl is null
            ? formItem
            : PropertyObjectCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, _selectedControl))
              ?? formItem;
    }

    private void OnPropertyObjectChanged(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG prop-object-changed guard={_updatingDesignerProperties} item={(PropertyObjectCombo.SelectedItem is ComboBoxItem i ? (i.Content?.ToString() ?? "?") : "null")}");
        if (_updatingDesignerProperties || PropertyObjectCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is YyControlModel control)
        {
            SelectDesignerControl(control);
        }
        else
        {
            SelectDesignerForm();
        }
    }

    private void BuildControlEventSelector(YyControlModel control)
    {
        EventSelectorCombo.Items.Clear();
        ResetEventSelectorPlaceholder();
        foreach (var kind in GetControlEventKinds(control.Type))
        {
            var bound = GetControlEvent(control, kind);
            EventSelectorCombo.Items.Add(new ComboBoxItem
            {
                Content = (string.IsNullOrWhiteSpace(bound) ? string.Empty : "✓ ") + GetEventDisplayName(kind),
                Tag = kind
            });
        }

        EventSelectorCombo.IsEnabled = true;
    }

    private void BuildFormEventSelector()
    {
        EventSelectorCombo.Items.Clear();
        ResetEventSelectorPlaceholder();
        EventSelectorCombo.Items.Add(new ComboBoxItem
        {
            Content = (string.IsNullOrWhiteSpace(_designForm?.LoadEvent) ? string.Empty : "✓ ") + EventHandlerNaming.FormLoad,
            Tag = "Load"
        });
        EventSelectorCombo.Items.Add(new ComboBoxItem
        {
            Content = (string.IsNullOrWhiteSpace(_designForm?.CloseEvent) ? string.Empty : "✓ ") + EventHandlerNaming.FormClose,
            Tag = "Close"
        });
        EventSelectorCombo.IsEnabled = true;
    }

    private void ResetEventSelectorPlaceholder()
    {
        var placeholder = new ComboBoxItem
        {
            Content = "在此处选择加入事件处理子程序",
            IsSelected = true
        };
        EventSelectorCombo.Items.Insert(0, placeholder);
        EventSelectorCombo.SelectedIndex = 0;
    }

    private static IEnumerable<ControlEventKind> GetControlEventKinds(string controlType) => controlType switch
    {
        YyControlTypes.Button => [ControlEventKind.Click],
        YyControlTypes.TextBox => [ControlEventKind.TextChanged],
        YyControlTypes.ComboBox or YyControlTypes.ListBox => [ControlEventKind.SelectionChanged],
        YyControlTypes.CheckBox or YyControlTypes.RadioButton => [ControlEventKind.CheckedChanged],
        _ => []
    };

    private void OnEventSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG event-selector-changed guard={_updatingDesignerProperties} selected={(EventSelectorCombo.SelectedItem is ComboBoxItem ei ? (ei.Content?.ToString() ?? "?") : "null")}");
        if (_updatingDesignerProperties || _designForm is null)
        {
            return;
        }

        if (EventSelectorCombo.SelectedItem is not ComboBoxItem { Tag: var tag } || tag is null)
        {
            return;
        }

        if (tag is ControlEventKind eventKind && _selectedControl is not null)
        {
            CreateOrOpenControlEventHandler(_selectedControl, eventKind);
        }
        else if (tag is string formKind)
        {
            CreateOrOpenFormEventHandler(formKind == "Load");
        }

        _updatingDesignerProperties = true;
        ResetEventSelectorPlaceholder();
        _updatingDesignerProperties = false;
        RefreshPropertyObjectLabel();
    }

    private void OnPropertyGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PropertyRows.SelectedItem is PropertyRow row)
        {
            ShowHint(row.Description);
        }
    }

    private void ShowHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        EditorHelpBox.Text = text;
        BottomTabs.SelectedIndex = 0;
    }

    private void OnPropertyRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box)
        {
            return;
        }

        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (box.DataContext is PropertyRow row)
        {
            CommitPropertyRow(row);
        }

        e.Handled = true;
    }

    private void OnPropertyRowCommit(object sender, RoutedEventArgs e)
    {
        StudioTrace.Write($"DBG prop-row-commit guard={_updatingDesignerProperties} sender={((sender as TextBox)?.DataContext is PropertyRow pr ? pr.Name : "other")}");
        if (_updatingDesignerProperties || _suppressPropertyCommit)
        {
            return;
        }

        if (sender is TextBox { DataContext: PropertyRow row })
        {
            if (string.Equals(row.ValueText, row.InitialValue, StringComparison.Ordinal))
            {
                return;
            }

            CommitPropertyRow(row);
            row.InitialValue = row.ValueText;
        }
    }

    private void OnPropertyBoolCommit(object sender, SelectionChangedEventArgs e)
    {
        StudioTrace.Write($"DBG bool-commit guard={_updatingDesignerProperties} suppress={_suppressPropertyCommit}");
        if (_updatingDesignerProperties || _suppressPropertyCommit)
        {
            return;
        }

        if (sender is ComboBox { DataContext: PropertyRow row })
        {
            if (string.Equals(row.ValueText, row.InitialValue, StringComparison.Ordinal))
            {
                return;
            }

            CommitPropertyRow(row);
            row.InitialValue = row.ValueText;
        }
    }

    private void CommitPropertyRow(PropertyRow row)
    {
        if (_updatingDesignerProperties || _designForm is null)
        {
            return;
        }

        if (_selectedControl is null)
        {
            CommitFormProperty(row);
        }
        else
        {
            CommitControlProperty(row);
        }
    }

    private void CommitFormProperty(PropertyRow row)
    {
        switch (row.Name)
        {
            case "名称":
                var name = row.ValueText.Trim();
                if (YyFormSerializer.IsIdentifier(name))
                {
                    _designForm!.Name = name;
                }
                else
                {
                    row.ValueText = _designForm!.Name;
                    ShowHint("名称必须是中文、英文、数字或下划线组成的标识符。");
                    return;
                }

                break;
            case "备注":
                _designForm!.Remark = row.ValueText;
                break;
            case "标题":
                _designForm!.Title = row.ValueText;
                DesignerTitleText.Text = string.IsNullOrWhiteSpace(_designForm.Title)
                    ? _designForm.Name
                    : _designForm.Title;
                break;
            case "图标":
                var icon = row.ValueText.Trim();
                var projectDirectory = _project?.DirectoryPath ??
                    YyResourceIndex.FindProjectDirectory(
                        _designerDocument?.FilePath ?? _currentDocument?.FilePath);
                if (!string.IsNullOrWhiteSpace(icon) &&
                    YyResourceIndex.ResolveResourcePath(projectDirectory, icon) is null)
                {
                    row.ValueText = _designForm!.Icon;
                    ShowHint("图标必须引用 Resources/ids.xml 中已登记的图片资源。");
                    return;
                }

                _designForm!.Icon = icon;
                break;
            case "宽度":
                if (TryDesignerNumber(row.ValueText, 320, 3840, out var width))
                {
                    _designForm!.Width = width;
                }

                break;
            case "高度":
                if (TryDesignerNumber(row.ValueText, 240, 2160, out var height))
                {
                    _designForm!.Height = height;
                }

                break;
            case "背景":
                _designForm!.Background = row.ValueText.Trim();
                break;
            case "边框":
                var borderStyle = row.ValueText.Trim();
                if (borderStyle is "可调边框" or "对话框边框" or "无边框")
                {
                    _designForm!.BorderStyle = borderStyle;
                }
                else
                {
                    row.ValueText = _designForm!.BorderStyle;
                    ShowHint("边框只能是:可调边框/对话框边框/无边框。");
                    return;
                }

                break;
            case "可否移动":
                _designForm!.Movable = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "初始位置":
                var position = row.ValueText.Trim();
                if (position is "默认" or "居中显示" or "最小化" or "最大化")
                {
                    _designForm!.StartPosition = position;
                }
                else
                {
                    row.ValueText = _designForm!.StartPosition;
                    ShowHint("初始位置只能是:默认/居中显示/最小化/最大化。");
                    return;
                }

                break;
            case "最小化按钮":
                _designForm!.MinimizeBox = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "最大化按钮":
                _designForm!.MaximizeBox = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
        }

        DesignerCanvas.Width = _designForm!.Width;
        DesignerCanvas.Height = _designForm.Height;
        DesignerCanvas.Background = CreateDesignerBackground(_designForm.Background);
        UpdateDesignerTitleChrome();
        RefreshPropertyObjectLabel();
        DesignerChanged();
    }

    private void CommitControlProperty(PropertyRow row)
    {
        var control = _selectedControl!;
        switch (row.Name)
        {
            case "名称":
                var name = row.ValueText.Trim();
                if (!YyFormSerializer.IsIdentifier(name))
                {
                    row.ValueText = control.Name;
                    ShowHint("名称必须是中文、英文、数字或下划线组成的标识符。");
                    return;
                }

                if (_designForm!.Controls.Any(other =>
                        !ReferenceEquals(other, control) &&
                        string.Equals(other.Name, name, StringComparison.Ordinal)))
                {
                    row.ValueText = control.Name;
                    ShowHint("名称“" + name + "”已被其他控件使用。");
                    return;
                }

                control.Name = name;
                break;
            case "备注":
                control.Remark = row.ValueText;
                break;
            case "标题":
                control.Text = row.ValueText;
                break;
            case "左边":
                if (TryDesignerNumber(row.ValueText, -10_000, 10_000, out var x))
                {
                    control.X = x;
                }

                break;
            case "顶边":
                if (TryDesignerNumber(row.ValueText, -10_000, 10_000, out var y))
                {
                    control.Y = y;
                }

                break;
            case "宽度":
                if (TryDesignerNumber(row.ValueText, 24, 3840, out var width))
                {
                    control.Width = width;
                }

                break;
            case "高度":
                if (TryDesignerNumber(row.ValueText, 20, 2160, out var height))
                {
                    control.Height = height;
                }

                break;
            case "字号":
                if (TryDesignerNumber(row.ValueText, 6, 96, out var fontSize))
                {
                    control.FontSize = fontSize;
                }

                break;
            case "可视":
                control.IsVisible = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "禁止":
                control.IsEnabled = !PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "选中":
                control.IsChecked = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "前景":
                control.Foreground = row.ValueText.Trim();
                break;
            case "背景":
                control.Background = row.ValueText.Trim();
                break;
            case "只读":
                control.ReadOnly = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "多行":
                control.MultiLine = PropertyRowCatalog.ParseBool(row.ValueText);
                break;
            case "密码":
                control.PasswordChar = row.ValueText.Length > 0 ? row.ValueText[..1] : string.Empty;
                break;
            case "最大长度":
                if (TryDesignerNumber(row.ValueText, 0, 100_000, out var maxLength))
                {
                    control.MaxLength = (int)maxLength;
                }

                break;
            case "图片":
                control.Picture = row.ValueText.Trim();
                break;
            case "数据绑定":
                var binding = row.ValueText.Trim();
                if (string.IsNullOrEmpty(binding))
                {
                    control.DataBindingTable = null;
                    control.DataBindingColumn = null;
                }
                else
                {
                    var parts = binding.Split('.', 2);
                    if (parts.Length != 2 ||
                        string.IsNullOrWhiteSpace(parts[0]) ||
                        string.IsNullOrWhiteSpace(parts[1]))
                    {
                        row.ValueText = string.IsNullOrWhiteSpace(control.DataBindingTable)
                            ? string.Empty
                            : control.DataBindingTable + "." + control.DataBindingColumn;
                        ShowHint("数据绑定格式为 表名.列名。");
                        return;
                    }

                    control.DataBindingTable = parts[0].Trim();
                    control.DataBindingColumn = parts[1].Trim();
                }

                break;
            default:
                return;
        }

        RefreshSelectedControlElement();
        RefreshPropertyObjectLabel();
        DesignerChanged();
    }

    /// <summary>
    /// 仅刷新对象下拉标签,不重建属性行。
    /// 属性行模板里的真假/颜色组合框在页签可见时才布局实现,
    /// 重建触发的 SelectionChanged 会在守卫外补发;提交路径若整表重建,
    /// 会形成 提交→重建→布局→SelectionChanged→提交 的无限递归(栈溢出)。
    /// </summary>
    private void RefreshPropertyObjectLabel()
    {
        _updatingDesignerProperties = true;
        try
        {
            BuildPropertyObjectCombo();
        }
        finally
        {
            _updatingDesignerProperties = false;
        }
    }

    private void CreateOrOpenFormEventHandler(bool isLoad)
    {
        if (_designForm is null)
        {
            return;
        }

        // 对齐官方:未保存的内存窗体也能直接生成窗体事件代码。
        var isFileBacked = !string.IsNullOrWhiteSpace(_currentDocument?.FilePath);
        string? formDirectory = isFileBacked ? Path.GetDirectoryName(_currentDocument!.FilePath)! : null;
        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? (isFileBacked
                ? Path.GetFileNameWithoutExtension(_currentDocument!.FilePath!)
                : Path.GetFileNameWithoutExtension(_currentDocument?.DisplayName ?? "未命名窗体")) + ".yyc"
            : _designForm.CodeFile;
        string? codePath = isFileBacked ? Path.GetFullPath(Path.Combine(formDirectory!, codeFileName)) : null;
        var codeDocument = codePath is not null
            ? _documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, codePath, StringComparison.OrdinalIgnoreCase))
            : _documents.FirstOrDefault(document =>
                document.FilePath is null &&
                string.Equals(document.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));

        string? handlerName;
        try
        {
            var existingText = codeDocument?.Text
                ?? (codePath is not null && File.Exists(codePath)
                    ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                    : null);
            var model = string.IsNullOrWhiteSpace(existingText)
                ? new YyCodeDocument { Name = _designForm.Name + "代码" }
                : YyCodeSerializer.Deserialize(existingText);
            handlerName = isLoad ? _designForm.LoadEvent : _designForm.CloseEvent;
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                var baseName = EventHandlerNaming.ForForm(
                    _designForm.Name,
                    isLoad ? EventHandlerNaming.FormLoad : EventHandlerNaming.FormClose);
                var existingNames = model.Functions
                    .Select(function => function.Name)
                    .ToHashSet(StringComparer.Ordinal);
                handlerName = EventHandlerNaming.Unique(baseName, existingNames.Contains);
                if (isLoad)
                {
                    _designForm.LoadEvent = handlerName;
                }
                else
                {
                    _designForm.CloseEvent = handlerName;
                }
            }

            if (model.Functions.All(function =>
                    !string.Equals(function.Name, handlerName, StringComparison.Ordinal)))
            {
                var function = new YyCodeFunction
                {
                    Name = handlerName,
                    Remark = $"{_designForm.Name} · {(isLoad ? "载入" : "关闭")}"
                };
                function.Statements.Add(new YyCodeStatement
                {
                    Kind = YyCodeStatementKinds.Call,
                    Expression = $"' 在这里编写窗体{(isLoad ? "载入" : "关闭")}逻辑。"
                });
                model.Functions.Add(function);
            }

            var serialized = YyCodeSerializer.Serialize(model);
            var changed = !string.Equals(existingText, serialized, StringComparison.Ordinal);
            if (codeDocument is null)
            {
                codeDocument = codePath is not null
                    ? new EditorDocument(codePath, Path.GetFileName(codePath), serialized)
                    : new EditorDocument(null, codeFileName, serialized);
                _documents.Add(codeDocument);
                if (changed || codePath is null || !File.Exists(codePath))
                {
                    codeDocument.MarkModified();
                }
            }
            else
            {
                codeDocument.Text = serialized;
            }

            if (formDirectory is not null && codePath is not null)
            {
                _designForm.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
            }

            DesignerChanged();
            RefreshCurrentFormBinding();
            DocumentTabs.SelectedItem = codeDocument;
            SelectPlainFunction(handlerName);
            SetStatus("已绑定窗体事件 " + handlerName + "()", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException)
        {
            MessageBox.Show(
                "无法创建事件代码：\n" + exception.Message,
                "生成事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool TryDesignerNumber(string text, double minimum, double maximum, out double value)
    {
        return double.TryParse(text, out value) &&
               double.IsFinite(value) &&
               value >= minimum &&
               value <= maximum;
    }

}
