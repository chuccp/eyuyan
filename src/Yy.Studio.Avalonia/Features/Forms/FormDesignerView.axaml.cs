using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>
/// 窗体设计器:可滚动点阵画布、拖放控件、选择/多选、拖动、8 手柄缩放、
/// 键盘微调、删除和常用属性夹。所有修改只发生在内存模型，事件持久化交给主窗口。
/// </summary>
public partial class FormDesignerView : UserControl
{
    private readonly Dictionary<YyControlModel, Control> _visuals = [];
    private readonly List<YyControlModel> _selection = [];
    private readonly List<FormPropertyRow> _propertyRows = [];
    private readonly List<(FormPropertyRow Row, Control Editor)> _propertyEditors = [];
    private readonly Dictionary<YyControlModel, Rect> _dragBounds = [];

    private FormEditorModel? _editor;
    private YyControlModel? _selectedControl;
    private DragMode _dragMode;
    private ResizeDirection _resizeDirection;
    private YyFormModel? _dragSnapshot;
    private Point _dragStart;
    private Rect _dragOriginalBounds;
    private bool _dragChanged;
    private bool _updatingUi;

    public FormDesignerView()
    {
        InitializeComponent();
        PopulateToolbox();
        WireCanvas();
        WireCanvasContextMenu();
        RebuildCanvas();
        SelectForm();
    }

    /// <summary>窗体模型或界面属性发生变化(可序列化 .yyx 后由主窗口保存)。</summary>
    public event EventHandler? FormChanged;

    public event EventHandler? SelectionChanged;

    /// <summary>用户选择事件入口；主窗口据此生成 .yyc 子程序与 .Designer.yyc 关系。</summary>
    public event EventHandler<FormDesignerEventRequestedEventArgs>? EventRequested;

    public event Action<string>? StatusChanged;

    /// <summary>当前内存模型(只读用途；编辑请通过设计器操作)。</summary>
    public YyFormModel? CurrentForm => _editor?.Form;

    public YyControlModel? SelectedControl => _selectedControl;

    public IReadOnlyList<YyControlModel> SelectedControls => _selection;

    /// <summary>组件箱面板可见性(布局菜单“窗口组件箱”)。</summary>
    public bool IsToolboxVisible
    {
        get => ToolboxPanel.IsVisible;
        set => ToolboxPanel.IsVisible = value;
    }

    /// <summary>预览被设计窗体(右键菜单/快捷键)。</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>查看用户代码(右键菜单)。</summary>
    public event EventHandler? ViewCodeRequested;

    private bool _snapToGrid = true;

    public void RequestPreview() => PreviewRequested?.Invoke(this, EventArgs.Empty);

    public void RequestViewCode() => ViewCodeRequested?.Invoke(this, EventArgs.Empty);

    public string? ProjectDirectory => _editor?.ProjectDirectory;

    public string? FormFilePath => _editor?.FormFilePath;

    public bool SnapToGrid
    {
        get => _editor?.SnapToGrid ?? _snapToGrid;
        set
        {
            _snapToGrid = value;
            if (_editor is not null)
            {
                _editor.SnapToGrid = value;
            }
        }
    }

    /// <summary>返回当前窗体的深复制，供主窗口序列化或重建关系文件。</summary>
    public YyFormModel BuildForm() => _editor?.Form.Clone() ?? new YyFormModel();

    public string? SerializeForm() => _editor?.Serialize();

    // ---------- 载入 / 保存 ----------

    public void LoadForm(YyFormModel form, string? projectDirectory = null) =>
        LoadForm(form, projectDirectory, null);

    public void LoadForm(
        YyFormModel form,
        string? projectDirectory,
        string? formPath)
    {
        _editor = FormEditorModel.Load(form, projectDirectory, formPath);
        _editor.SnapToGrid = _snapToGrid;
        ResetSelection();
        RebuildCanvas();
        SelectForm();
    }

    /// <summary>从 .yyx 载入并恢复同目录 .Designer.yyc 中仍兼容的事件关系。</summary>
    public void LoadFormFile(string formPath, string? projectDirectory = null)
    {
        _editor = FormEditorModel.LoadFile(formPath, projectDirectory);
        _editor.SnapToGrid = _snapToGrid;
        ResetSelection();
        RebuildCanvas();
        SelectForm();
        SetStatus(_editor.TotalEventCount > 0
            ? $"已载入窗体；恢复 {_editor.PreservedEventCount}/{_editor.TotalEventCount} 个事件关系。"
            : "已载入窗体。");
    }

    /// <summary>保存 .yyx(只写界面属性，绝不写事件处理程序名)。</summary>
    public bool SaveForm(string? path = null)
    {
        if (_editor is null)
        {
            SetStatus("尚未载入窗体。");
            return false;
        }

        try
        {
            _editor.SaveForm(path);
            SetStatus($"已保存 {Path.GetFileName(path ?? _editor.FormFilePath ?? string.Empty)}");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or YyFormFormatException)
        {
            SetStatus("保存窗体失败：" + exception.Message);
            return false;
        }
    }

    /// <summary>重建自动关系文件 .Designer.yyc；事件映射以该文件为唯一事实。</summary>
    public bool WriteBinding(string? formPath = null, string? userCodePath = null)
    {
        if (_editor is null)
        {
            SetStatus("尚未载入窗体。");
            return false;
        }

        try
        {
            _editor.WriteBinding(formPath, userCodePath);
            SetStatus("已重建自动关系文件。");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or YyFormFormatException)
        {
            SetStatus("生成关系文件失败：" + exception.Message);
            return false;
        }
    }

    // ---------- 选择 ----------

    public void SelectControl(YyControlModel control, bool additive = false)
    {
        if (additive)
        {
            if (!_selection.Remove(control))
            {
                _selection.Add(control);
            }
        }
        else
        {
            _selection.Clear();
            _selection.Add(control);
        }

        if (_selection.Count == 0)
        {
            SelectForm();
            return;
        }

        _selectedControl = _selection.Contains(control) ? control : _selection[^1];
        PopulatePropertyPanel();
        UpdateSelectionVisuals();
        DesignerCanvas.Focus();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(_selection.Count == 1
            ? $"已选择控件“{_selectedControl.Name}”"
            : $"已选择 {_selection.Count} 个控件");
    }

    public void SelectForm()
    {
        _selection.Clear();
        _selectedControl = null;
        PopulatePropertyPanel();
        UpdateSelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("窗口用来设计程序的用户界面及对界面事件进行处理。");
    }

    public void SelectAll()
    {
        if (_editor is null)
        {
            return;
        }

        SetSelection(_editor.Form.Controls);
        DesignerCanvas.Focus();
    }

    public void SetSelection(IEnumerable<YyControlModel> controls)
    {
        _selection.Clear();
        _selection.AddRange(controls.Distinct());
        if (_selection.Count == 0)
        {
            SelectForm();
            return;
        }

        _selectedControl = _selection[^1];
        PopulatePropertyPanel();
        UpdateSelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        SetStatus($"已选择 {_selection.Count} 个控件");
    }

    // ---------- 添加 / 删除 / 层级 / 剪贴板 ----------

    public YyControlModel? AddControl(string type, Point? position = null)
    {
        if (_editor is null)
        {
            SetStatus("尚未载入窗体。");
            return null;
        }

        var point = position ?? new Point(
            44 + _editor.Form.Controls.Count % 6 * 22,
            44);
        var before = _editor.BeginEdit();
        var control = _editor.AddControl(type, point.X, point.Y);
        _editor.CommitEdit(before);
        RebuildCanvas();
        SelectControl(control);
        NotifyFormChanged();
        SetStatus($"已添加{type}“{control.Name}”");
        return control;
    }

    public void DeleteSelection()
    {
        if (_editor is null || _selection.Count == 0)
        {
            return;
        }

        var before = _editor.BeginEdit();
        var count = _editor.RemoveControls(_selection);
        _editor.CommitEdit(before);
        ResetSelection();
        RebuildCanvas();
        SelectForm();
        NotifyFormChanged();
        SetStatus($"已删除 {count} 个控件");
    }

    public void CopySelection(bool cut)
    {
        if (_editor is null || _selection.Count == 0)
        {
            return;
        }

        var count = _editor.CopyControls(_selection);
        if (!cut)
        {
            SetStatus($"已复制 {count} 个控件");
            return;
        }

        var before = _editor.BeginEdit();
        _editor.RemoveControls(_selection);
        _editor.CommitEdit(before);
        ResetSelection();
        RebuildCanvas();
        SelectForm();
        NotifyFormChanged();
        SetStatus($"已剪切 {count} 个控件");
    }

    public void PasteSelection()
    {
        if (_editor is null || _editor.ClipboardCount == 0)
        {
            return;
        }

        var before = _editor.BeginEdit();
        var pasted = _editor.PasteControls();
        if (pasted.Count == 0)
        {
            return;
        }

        _editor.CommitEdit(before);
        RebuildCanvas();
        SetSelection(pasted);
        NotifyFormChanged();
        SetStatus($"已粘贴 {pasted.Count} 个控件");
    }

    public void BringSelectionToFront() => ReorderSelection(toFront: true);

    public void SendSelectionToBack() => ReorderSelection(toFront: false);

    private void ReorderSelection(bool toFront)
    {
        if (_editor is null || _selection.Count == 0)
        {
            return;
        }

        var before = _editor.BeginEdit();
        _editor.ReorderControls(_selection, toFront);
        _editor.CommitEdit(before);
        RebuildCanvas();
        UpdateSelectionVisuals();
        NotifyFormChanged();
        SetStatus(toFront ? "已置前" : "已置后");
    }

    // ---------- 撤销 / 重做 ----------

    public void Undo()
    {
        if (_editor is null || !_editor.Undo())
        {
            return;
        }

        RestoreAfterHistory();
        SetStatus("已撤销窗体设计操作");
    }

    public void Redo()
    {
        if (_editor is null || !_editor.Redo())
        {
            return;
        }

        RestoreAfterHistory();
        SetStatus("已重做窗体设计操作");
    }

    private void RestoreAfterHistory()
    {
        var selectedNames = _selection.Select(control => control.Name).ToArray();
        RebuildCanvas();
        if (_editor is null)
        {
            return;
        }

        var restored = _editor.Form.Controls
            .Where(control => selectedNames.Contains(control.Name, StringComparer.Ordinal))
            .ToList();
        if (restored.Count == 0)
        {
            SelectForm();
        }
        else
        {
            SetSelection(restored);
        }

        RefreshEventSelector();
        NotifyFormChanged();
    }

    // ---------- 事件入口(持久化由主窗口执行) ----------

    /// <summary>
    /// 主窗口生成处理子程序后回填事件名。只修改内存模型，
    /// 随后由主窗口调用 <see cref="WriteBinding"/> 写入 .Designer.yyc。
    /// </summary>
    public void ApplyEventHandler(
        YyControlModel? control,
        FormDesignerEventKind kind,
        string? handlerName)
    {
        if (_editor is null ||
            (control is not null && !_editor.Form.Controls.Contains(control)))
        {
            return;
        }

        var before = _editor.BeginEdit();
        if (!_editor.SetEventHandler(control, kind, handlerName))
        {
            return;
        }

        _editor.CommitEdit(before);
        RefreshEventSelector();
        NotifyFormChanged();
        SetStatus(string.IsNullOrWhiteSpace(handlerName)
            ? "已清除事件绑定"
            : $"已绑定事件 {handlerName}()");
    }

    // ---------- 辅助 ----------

    private void NotifyFormChanged() =>
        FormChanged?.Invoke(this, EventArgs.Empty);

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusChanged?.Invoke(text);
    }

    private static bool IsCommandModifier(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    private enum DragMode
    {
        None,
        Move,
        Resize
    }
}

/// <summary>设计器请求主窗口为某个事件创建/打开处理子程序。</summary>
public sealed class FormDesignerEventRequestedEventArgs : EventArgs
{
    public FormDesignerEventRequestedEventArgs(
        YyControlModel? control,
        FormDesignerEventKind kind,
        string suggestedHandlerName,
        string? existingHandlerName)
    {
        Control = control;
        Kind = kind;
        SuggestedHandlerName = suggestedHandlerName;
        ExistingHandlerName = existingHandlerName;
    }

    /// <summary>null 表示窗体级事件(载入/打开/关闭)。</summary>
    public YyControlModel? Control { get; }

    public FormDesignerEventKind Kind { get; }

    /// <summary>按官方规则生成的处理程序基名；主窗口负责唯一化。</summary>
    public string SuggestedHandlerName { get; }

    public string? ExistingHandlerName { get; }
}
