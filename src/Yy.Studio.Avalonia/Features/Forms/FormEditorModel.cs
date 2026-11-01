using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Yy.Compiler;
using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>
/// 窗体设计器的内存编辑模型。所有编辑都只修改 <see cref="YyFormModel"/>，
/// 不写磁盘；序列化 / 关系文件生成由显式方法完成，便于主窗口统一协调三文件。
/// </summary>
public sealed class FormEditorModel
{
    private const int MaximumHistory = 100;
    private readonly List<YyFormModel> _undo = [];
    private readonly List<YyFormModel> _redo = [];
    private readonly List<YyControlModel> _clipboard = [];

    private FormEditorModel(
        YyFormModel form,
        string? projectDirectory,
        string? formPath)
    {
        Form = form;
        FormFilePath = formPath;
        ProjectDirectory = projectDirectory
            ?? YyResourceIndex.FindProjectDirectory(formPath);
    }

    public YyFormModel Form { get; private set; }

    public string? ProjectDirectory { get; }

    public string? FormFilePath { get; }

    /// <summary>载入 .Designer.yyc 时成功恢复的事件数量。</summary>
    public int PreservedEventCount { get; private set; }

    /// <summary>载入 .Designer.yyc 中登记的事件总数。</summary>
    public int TotalEventCount { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int ClipboardCount => _clipboard.Count;

    public bool SnapToGrid { get; set; } = true;

    public double GridSize { get; set; } = 8;

    // ---------- 载入 / 序列化 ----------

    /// <summary>在内存中载入窗体(复制一份，不修改调用方模型)。</summary>
    public static FormEditorModel Load(
        YyFormModel form,
        string? projectDirectory = null,
        string? formPath = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new FormEditorModel(form.Clone(), projectDirectory, formPath);
    }

    /// <summary>
    /// 从 .yyx 载入窗体；同目录存在 .Designer.yyc 时按稳定标识恢复事件关系。
    /// 事件只进入内存模型，不写回 .yyx。
    /// </summary>
    public static FormEditorModel LoadFile(
        string formPath,
        string? projectDirectory = null)
    {
        var text = File.ReadAllText(formPath, new UTF8Encoding(false, true));
        var form = DeserializeForm(text);
        var editor = new FormEditorModel(form, projectDirectory, formPath);
        var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
        if (!File.Exists(bindingPath))
        {
            return editor;
        }

        var binding = DeserializeBinding(
            File.ReadAllText(bindingPath, new UTF8Encoding(false, true)));
        form.CodeFile = binding.CodeFile;
        editor.TotalEventCount = binding.Events.Count;
        editor.PreservedEventCount = YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
        return editor;
    }

    public static string SerializeForm(YyFormModel form) =>
        YyFormSerializer.Serialize(form);

    public static YyFormModel DeserializeForm(string text) =>
        YyFormSerializer.Deserialize(text);

    public static YyFormBindingDocument DeserializeBinding(string text) =>
        YyFormBindingSerializer.Deserialize(text);

    public string Serialize() => SerializeForm(Form);

    public YyFormBindingDocument BuildBindingDocument(
        string uiFile,
        string codeFile) => YyFormBindingSerializer.Create(Form, uiFile, codeFile);

    public string GenerateBinding(string uiFile, string codeFile) =>
        YyFormBindingSerializer.Generate(Form, uiFile, codeFile);

    /// <summary>按稳定标识恢复兼容事件；返回恢复数量。</summary>
    public int ApplyBinding(YyFormBindingDocument binding)
    {
        var applied = YyFormBindingSerializer.ApplyCompatibleTo(Form, binding);
        PreservedEventCount = applied;
        TotalEventCount = binding.Events.Count;
        return applied;
    }

    /// <summary>保存 .yyx(只含界面属性，事件名永远不会写入)。</summary>
    public void SaveForm(string? path = null)
    {
        var target = path ?? FormFilePath
            ?? throw new InvalidOperationException("窗体尚未关联文件路径。");
        ValidateForSave();
        WriteAtomically(target, Serialize());
    }

    /// <summary>重建自动关系文件 .Designer.yyc；用户代码内容不受影响。</summary>
    public void WriteBinding(string? formPath = null, string? userCodePath = null)
    {
        var target = formPath ?? FormFilePath
            ?? throw new InvalidOperationException("窗体尚未关联文件路径。");
        var codePath = userCodePath ?? YyFormCompanionFiles.UserCodePath(target);
        YyFormCompanionFiles.WriteGeneratedBinding(target, Form, codePath);
    }

    private void ValidateForSave()
    {
        if (!YyFormSerializer.IsIdentifier(Form.Name))
        {
            throw new InvalidDataException($"窗体名称“{Form.Name}”不是有效标识符。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in Form.Controls)
        {
            if (!YyFormSerializer.IsIdentifier(control.Name))
            {
                throw new InvalidDataException($"控件名称“{control.Name}”不是有效标识符。");
            }

            if (!names.Add(control.Name))
            {
                throw new InvalidDataException($"控件名称“{control.Name}”重复。");
            }
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // ---------- 撤销 / 重做 ----------

    /// <summary>在连续编辑(拖动/缩放)开始时取得快照。</summary>
    public YyFormModel BeginEdit() => Form.Clone();

    /// <summary>编辑结束时提交快照；内容未变化则不产生历史记录。</summary>
    public bool CommitEdit(YyFormModel before)
    {
        if (string.Equals(SerializeForm(before), Serialize(), StringComparison.Ordinal))
        {
            return false;
        }

        _undo.Add(before);
        if (_undo.Count > MaximumHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(Form.Clone());
        Form = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(Form.Clone());
        Form = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    // ---------- 几何编辑 ----------

    public double Snap(double value) =>
        SnapToGrid ? Math.Round(value / GridSize) * GridSize : value;

    public YyControlModel AddControl(string type, double x, double y)
    {
        if (!YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的窗体控件类型。");
        }

        var sequence = 1;
        while (Form.Controls.Any(control =>
                   string.Equals(
                       control.Name,
                       YyControlTypes.GetBaseName(type) + sequence,
                       StringComparison.Ordinal)))
        {
            sequence++;
        }

        var control = YyControlTypes.Create(type, sequence);
        control.Name = YyControlTypes.GetBaseName(type) + sequence;
        control.X = Clamp(Snap(x), 0, Math.Max(0, Form.Width - control.Width));
        control.Y = Clamp(Snap(y), 0, Math.Max(0, Form.Height - control.Height));
        Form.Controls.Add(control);
        return control;
    }

    public int RemoveControls(IEnumerable<YyControlModel> controls)
    {
        var removed = 0;
        foreach (var control in controls.Distinct().ToArray())
        {
            if (Form.Controls.Remove(control))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>按组边界整体移动选中控件，保证不超出窗体。</summary>
    public void MoveControls(
        IReadOnlyCollection<YyControlModel> controls,
        double deltaX,
        double deltaY)
    {
        if (controls.Count == 0)
        {
            return;
        }

        var left = controls.Min(control => control.X);
        var top = controls.Min(control => control.Y);
        var right = controls.Max(control => control.X + control.Width);
        var bottom = controls.Max(control => control.Y + control.Height);
        var appliedX = Clamp(deltaX, -left, Form.Width - right);
        var appliedY = Clamp(deltaY, -top, Form.Height - bottom);
        foreach (var control in controls)
        {
            control.X += appliedX;
            control.Y += appliedY;
        }
    }

    public void ResizeControl(
        YyControlModel control,
        ResizeDirection direction,
        Rect original,
        double deltaX,
        double deltaY)
    {
        const double minimumWidth = 24;
        const double minimumHeight = 20;
        var left = original.Left;
        var top = original.Top;
        var right = original.Right;
        var bottom = original.Bottom;

        if (direction is ResizeDirection.West
            or ResizeDirection.NorthWest
            or ResizeDirection.SouthWest)
        {
            left = Clamp(Snap(original.Left + deltaX), 0, right - minimumWidth);
        }

        if (direction is ResizeDirection.East
            or ResizeDirection.NorthEast
            or ResizeDirection.SouthEast)
        {
            right = Clamp(
                Snap(original.Right + deltaX),
                left + minimumWidth,
                Form.Width);
        }

        if (direction is ResizeDirection.North
            or ResizeDirection.NorthWest
            or ResizeDirection.NorthEast)
        {
            top = Clamp(Snap(original.Top + deltaY), 0, bottom - minimumHeight);
        }

        if (direction is ResizeDirection.South
            or ResizeDirection.SouthWest
            or ResizeDirection.SouthEast)
        {
            bottom = Clamp(
                Snap(original.Bottom + deltaY),
                top + minimumHeight,
                Form.Height);
        }

        control.X = left;
        control.Y = top;
        control.Width = right - left;
        control.Height = bottom - top;
    }

    public void ReorderControls(
        IReadOnlyCollection<YyControlModel> controls,
        bool toFront)
    {
        var ordered = Form.Controls.Where(controls.Contains).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        foreach (var control in ordered)
        {
            Form.Controls.Remove(control);
        }

        if (toFront)
        {
            Form.Controls.AddRange(ordered);
        }
        else
        {
            Form.Controls.InsertRange(0, ordered);
        }
    }

    // ---------- 剪贴板 ----------

    public int CopyControls(IEnumerable<YyControlModel> controls)
    {
        _clipboard.Clear();
        _clipboard.AddRange(controls.Select(control => control.Clone()));
        return _clipboard.Count;
    }

    /// <summary>粘贴副本；新控件清除事件绑定，偏移 12px 并避免重名。</summary>
    public IReadOnlyList<YyControlModel> PasteControls()
    {
        if (_clipboard.Count == 0)
        {
            return [];
        }

        var pasted = new List<YyControlModel>();
        foreach (var prototype in _clipboard)
        {
            var model = prototype.Clone();
            var sequence = 1;
            while (Form.Controls.Any(control =>
                       string.Equals(
                           control.Name,
                           YyControlTypes.GetBaseName(model.Type) + sequence,
                           StringComparison.Ordinal)))
            {
                sequence++;
            }

            model.Name = YyControlTypes.GetBaseName(model.Type) + sequence;
            model.ClickEvent = null;
            model.TextChangedEvent = null;
            model.SelectionChangedEvent = null;
            model.CheckedChangedEvent = null;
            model.X = Clamp(model.X + 12, 0, Math.Max(0, Form.Width - model.Width));
            model.Y = Clamp(model.Y + 12, 0, Math.Max(0, Form.Height - model.Height));
            Form.Controls.Add(model);
            pasted.Add(model);
        }

        return pasted;
    }

    // ---------- 事件关系(仅内存) ----------

    public bool SetEventHandler(
        YyControlModel? control,
        FormDesignerEventKind kind,
        string? handlerName)
    {
        if (control is null)
        {
            if (!FormDesignerEvents.IsFormEvent(kind) ||
                string.Equals(
                    FormDesignerEvents.GetHandler(Form, kind),
                    handlerName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            FormDesignerEvents.SetHandler(Form, kind, handlerName);
            return true;
        }

        if (FormDesignerEvents.IsFormEvent(kind) ||
            !FormDesignerEvents.GetKinds(control.Type).Contains(kind) ||
            string.Equals(
                FormDesignerEvents.GetHandler(control, kind),
                handlerName,
                StringComparison.Ordinal))
        {
            return false;
        }

        FormDesignerEvents.SetHandler(control, kind, handlerName);
        return true;
    }

    // ---------- 属性夹 ----------

    public IReadOnlyList<FormPropertyRow> BuildPropertyRows(YyControlModel? control)
    {
        var rows = new List<FormPropertyRow>();
        if (control is null)
        {
            rows.Add(new FormPropertyRow("名称", Form.Name, FormPropertyKind.Identifier, FormPropertyCatalog.HintName));
            rows.Add(new FormPropertyRow("备注", Form.Remark, FormPropertyKind.Text, FormPropertyCatalog.HintRemark));
            rows.Add(new FormPropertyRow("标题", Form.Title, FormPropertyKind.Text, FormPropertyCatalog.HintTitle));
            rows.Add(new FormPropertyRow("图标", Form.Icon, FormPropertyKind.Text, FormPropertyCatalog.HintIcon));
            rows.Add(new FormPropertyRow("宽度", FormPropertyCatalog.FormatNumber(Form.Width), FormPropertyKind.Integer, FormPropertyCatalog.HintWidth));
            rows.Add(new FormPropertyRow("高度", FormPropertyCatalog.FormatNumber(Form.Height), FormPropertyKind.Integer, FormPropertyCatalog.HintHeight));
            rows.Add(new FormPropertyRow("背景", Form.Background, FormPropertyKind.Color, FormPropertyCatalog.HintBackground));
            rows.Add(new FormPropertyRow("边框", Form.BorderStyle, FormPropertyKind.Text, FormPropertyCatalog.HintBorderStyle));
            rows.Add(new FormPropertyRow("可否移动", FormPropertyCatalog.BoolText(Form.Movable), FormPropertyKind.Bool, FormPropertyCatalog.HintMovable));
            rows.Add(new FormPropertyRow("初始位置", Form.StartPosition, FormPropertyKind.Text, FormPropertyCatalog.HintStartPosition));
            rows.Add(new FormPropertyRow("最小化按钮", FormPropertyCatalog.BoolText(Form.MinimizeBox), FormPropertyKind.Bool, FormPropertyCatalog.HintMinBox));
            rows.Add(new FormPropertyRow("最大化按钮", FormPropertyCatalog.BoolText(Form.MaximizeBox), FormPropertyKind.Bool, FormPropertyCatalog.HintMaxBox));
            return rows;
        }

        rows.Add(new FormPropertyRow("名称", control.Name, FormPropertyKind.Identifier, FormPropertyCatalog.HintName));
        rows.Add(new FormPropertyRow("备注", control.Remark, FormPropertyKind.Text, FormPropertyCatalog.HintRemark));
        rows.Add(new FormPropertyRow("左边", FormPropertyCatalog.FormatNumber(control.X), FormPropertyKind.Integer, FormPropertyCatalog.HintLeft));
        rows.Add(new FormPropertyRow("顶边", FormPropertyCatalog.FormatNumber(control.Y), FormPropertyKind.Integer, FormPropertyCatalog.HintTop));
        rows.Add(new FormPropertyRow("宽度", FormPropertyCatalog.FormatNumber(control.Width), FormPropertyKind.Integer, FormPropertyCatalog.HintWidth));
        rows.Add(new FormPropertyRow("高度", FormPropertyCatalog.FormatNumber(control.Height), FormPropertyKind.Integer, FormPropertyCatalog.HintHeight));
        rows.Add(new FormPropertyRow("可视", FormPropertyCatalog.BoolText(control.IsVisible), FormPropertyKind.Bool, FormPropertyCatalog.HintVisible));
        rows.Add(new FormPropertyRow("禁止", FormPropertyCatalog.BoolText(!control.IsEnabled), FormPropertyKind.Bool, FormPropertyCatalog.HintForbidden));
        rows.Add(new FormPropertyRow("标题", control.Text, FormPropertyKind.Text, FormPropertyCatalog.HintTitle));
        rows.Add(new FormPropertyRow("字号", FormPropertyCatalog.FormatNumber(control.FontSize), FormPropertyKind.Integer, FormPropertyCatalog.HintFontSize));
        rows.Add(new FormPropertyRow("前景", control.Foreground, FormPropertyKind.Color, FormPropertyCatalog.HintForeground));
        rows.Add(new FormPropertyRow("背景", control.Background, FormPropertyKind.Color, FormPropertyCatalog.HintBackground));

        if (control.Type is YyControlTypes.TextBox)
        {
            rows.Add(new FormPropertyRow("只读", FormPropertyCatalog.BoolText(control.ReadOnly), FormPropertyKind.Bool, FormPropertyCatalog.HintReadOnly));
            rows.Add(new FormPropertyRow("多行", FormPropertyCatalog.BoolText(control.MultiLine), FormPropertyKind.Bool, FormPropertyCatalog.HintMultiLine));
            rows.Add(new FormPropertyRow("密码", control.PasswordChar, FormPropertyKind.Text, FormPropertyCatalog.HintPassword));
            rows.Add(new FormPropertyRow("最大长度", FormPropertyCatalog.FormatNumber(control.MaxLength), FormPropertyKind.Integer, FormPropertyCatalog.HintMaxLength));
        }

        if (control.Type is YyControlTypes.PictureBox)
        {
            rows.Add(new FormPropertyRow("图片", control.Picture, FormPropertyKind.Text, FormPropertyCatalog.HintPicture));
        }

        rows.Add(new FormPropertyRow(
            "数据绑定",
            string.IsNullOrWhiteSpace(control.DataBindingTable)
                ? string.Empty
                : control.DataBindingTable + "." + control.DataBindingColumn,
            FormPropertyKind.Text,
            FormPropertyCatalog.HintDataBinding));

        if (control.Type is YyControlTypes.CheckBox or YyControlTypes.RadioButton)
        {
            rows.Add(new FormPropertyRow("选中", FormPropertyCatalog.BoolText(control.IsChecked), FormPropertyKind.Bool, FormPropertyCatalog.HintChecked));
        }

        return rows;
    }

    /// <summary>
    /// 提交一行属性。失败时返回 false 并给出中文诊断，调用方应把编辑框恢复为模型值。
    /// </summary>
    public bool TryApplyProperty(
        YyControlModel? control,
        string name,
        string valueText,
        out string error)
    {
        error = string.Empty;
        return control is null
            ? TryApplyFormProperty(name, valueText, out error)
            : TryApplyControlProperty(control, name, valueText, out error);
    }

    private bool TryApplyFormProperty(string name, string valueText, out string error)
    {
        error = string.Empty;
        switch (name)
        {
            case "名称":
                var formName = valueText.Trim();
                if (!YyFormSerializer.IsIdentifier(formName))
                {
                    error = "名称必须是中文、英文、数字或下划线组成的标识符。";
                    return false;
                }

                Form.Name = formName;
                return true;
            case "备注":
                Form.Remark = valueText;
                return true;
            case "标题":
                Form.Title = valueText;
                return true;
            case "图标":
                Form.Icon = valueText.Trim();
                return true;
            case "宽度":
                return TryNumber(
                    valueText, 320, 3840, "宽度必须是 320 到 3840 之间的数字。",
                    out var width, out error, value => Form.Width = value);
            case "高度":
                return TryNumber(
                    valueText, 240, 2160, "高度必须是 240 到 2160 之间的数字。",
                    out var height, out error, value => Form.Height = value);
            case "背景":
                Form.Background = valueText.Trim();
                return true;
            case "边框":
                var borderStyle = valueText.Trim();
                if (borderStyle is not ("可调边框" or "对话框边框" or "无边框"))
                {
                    error = "边框只能是:可调边框/对话框边框/无边框。";
                    return false;
                }

                Form.BorderStyle = borderStyle;
                return true;
            case "可否移动":
                Form.Movable = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "初始位置":
                var position = valueText.Trim();
                if (position is not ("默认" or "居中显示" or "最小化" or "最大化"))
                {
                    error = "初始位置只能是:默认/居中显示/最小化/最大化。";
                    return false;
                }

                Form.StartPosition = position;
                return true;
            case "最小化按钮":
                Form.MinimizeBox = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "最大化按钮":
                Form.MaximizeBox = FormPropertyCatalog.ParseBool(valueText);
                return true;
            default:
                error = $"未知窗体属性“{name}”。";
                return false;
        }
    }

    private bool TryApplyControlProperty(
        YyControlModel control,
        string name,
        string valueText,
        out string error)
    {
        error = string.Empty;
        switch (name)
        {
            case "名称":
                var controlName = valueText.Trim();
                if (!YyFormSerializer.IsIdentifier(controlName))
                {
                    error = "名称必须是中文、英文、数字或下划线组成的标识符。";
                    return false;
                }

                if (Form.Controls.Any(other =>
                        !ReferenceEquals(other, control) &&
                        string.Equals(other.Name, controlName, StringComparison.Ordinal)))
                {
                    error = $"名称“{controlName}”已被其他控件使用。";
                    return false;
                }

                control.Name = controlName;
                return true;
            case "备注":
                control.Remark = valueText;
                return true;
            case "标题":
                control.Text = valueText;
                return true;
            case "左边":
                return TryNumber(
                    valueText, -10_000, 10_000, "左边必须是 -10000 到 10000 之间的数字。",
                    out var x, out error, value => control.X = value);
            case "顶边":
                return TryNumber(
                    valueText, -10_000, 10_000, "顶边必须是 -10000 到 10000 之间的数字。",
                    out var y, out error, value => control.Y = value);
            case "宽度":
                return TryNumber(
                    valueText, 24, 3840, "宽度必须是 24 到 3840 之间的数字。",
                    out var width, out error, value => control.Width = value);
            case "高度":
                return TryNumber(
                    valueText, 20, 2160, "高度必须是 20 到 2160 之间的数字。",
                    out var height, out error, value => control.Height = value);
            case "字号":
                return TryNumber(
                    valueText, 6, 96, "字号必须是 6 到 96 之间的数字。",
                    out var fontSize, out error, value => control.FontSize = value);
            case "可视":
                control.IsVisible = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "禁止":
                control.IsEnabled = !FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "选中":
                control.IsChecked = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "前景":
                control.Foreground = valueText.Trim();
                return true;
            case "背景":
                control.Background = valueText.Trim();
                return true;
            case "只读":
                control.ReadOnly = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "多行":
                control.MultiLine = FormPropertyCatalog.ParseBool(valueText);
                return true;
            case "密码":
                control.PasswordChar = valueText.Length > 0 ? valueText[..1] : string.Empty;
                return true;
            case "最大长度":
                return TryNumber(
                    valueText, 0, 100_000, "最大长度必须是 0 到 100000 之间的数字。",
                    out var maxLength, out error, value => control.MaxLength = (int)value);
            case "图片":
                control.Picture = valueText.Trim();
                return true;
            case "数据绑定":
                return TryApplyDataBinding(control, valueText, out error);
            default:
                error = $"未知控件属性“{name}”。";
                return false;
        }
    }

    private static bool TryApplyDataBinding(
        YyControlModel control,
        string valueText,
        out string error)
    {
        error = string.Empty;
        var binding = valueText.Trim();
        if (binding.Length == 0)
        {
            control.DataBindingTable = null;
            control.DataBindingColumn = null;
            return true;
        }

        var parts = binding.Split('.', 2);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            error = "数据绑定格式为 表名.列名。";
            return false;
        }

        control.DataBindingTable = parts[0].Trim();
        control.DataBindingColumn = parts[1].Trim();
        return true;
    }

    private static bool TryNumber(
        string text,
        double minimum,
        double maximum,
        string message,
        out double value,
        out string error,
        Action<double> apply)
    {
        error = string.Empty;
        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value) ||
            !double.IsFinite(value) ||
            value < minimum ||
            value > maximum)
        {
            error = message;
            return false;
        }

        apply(value);
        return true;
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));
}
