using System.IO;
using System.Text;
using Avalonia;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Studio.Avalonia.Features.Forms;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private object? McpFormState()
    {
        var document = CurrentDocument?.FormView is not null
            ? CurrentDocument
            : _tabs.Documents.LastOrDefault(item => item.FormView is not null);
        var form = document?.FormView?.CurrentForm;
        if (document is null || form is null)
        {
            return null;
        }

        return new
        {
            name = form.Name,
            title = form.Title,
            icon = form.Icon,
            width = form.Width,
            height = form.Height,
            codeFile = Path.GetFileName(YyFormCompanionFiles.UserCodePath(document.Path)),
            bindingFile = Path.GetFileName(YyFormCompanionFiles.BindingPath(document.Path)),
            xml = document.FormView!.SerializeForm(),
            loadEvent = form.LoadEvent,
            openEvent = form.OpenEvent,
            closeEvent = form.CloseEvent,
            controls = form.Controls.Select(control => new
            {
                name = control.Name,
                type = control.Type,
                text = control.Text,
                x = control.X,
                y = control.Y,
                width = control.Width,
                height = control.Height,
                visible = control.IsVisible,
                enabled = control.IsEnabled,
                picture = control.Picture,
                click = control.ClickEvent
            }).ToArray()
        };
    }

    private EditorDocument McpFormDocument()
    {
        var document = CurrentDocument;
        if (document?.FormView is not null)
        {
            if (document.FormSourceMode && !SyncFormSource(document))
            {
                throw new InvalidOperationException("窗体源码无效，请先修正后再操作设计器。");
            }

            return document;
        }

        document = _tabs.Documents.LastOrDefault(item => item.FormView is not null)
            ?? throw new InvalidOperationException("请先打开窗体设计器。");
        _tabs.Select(document);
        if (document.FormSourceMode && !SyncFormSource(document))
        {
            throw new InvalidOperationException("窗体源码无效，请先修正后再操作设计器。");
        }

        return document;
    }

    private object McpAddControl(
        string type,
        double x,
        double y,
        string? name,
        string? text,
        double? width,
        double? height)
    {
        if (!YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("未知控件类型：" + type);
        }

        var document = McpFormDocument();
        var view = document.FormView!;
        var control = view.AddControl(type, new Point(x, y))
            ?? throw new InvalidOperationException("添加控件失败。");
        if (!string.IsNullOrWhiteSpace(name))
        {
            control.Name = name;
        }

        if (text is not null)
        {
            control.Text = text;
        }

        if (width is > 0)
        {
            control.Width = width.Value;
        }

        if (height is > 0)
        {
            control.Height = height.Value;
        }

        ReloadDesigner(document, view.BuildForm());
        return new
        {
            name = control.Name,
            type = control.Type,
            x = control.X,
            y = control.Y,
            width = control.Width,
            height = control.Height
        };
    }

    private object McpSetProperty(string target, string name, string value)
    {
        var document = McpFormDocument();
        var view = document.FormView!;
        var form = view.BuildForm();
        var control = string.IsNullOrWhiteSpace(target) ||
                      string.Equals(target, "form", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(target, form.Name, StringComparison.Ordinal)
            ? null
            : form.Controls.FirstOrDefault(item =>
                  string.Equals(item.Name, target, StringComparison.Ordinal) ||
                  string.Equals("@id/" + form.Name + "/" + item.Name, target, StringComparison.Ordinal))
              ?? throw new InvalidOperationException("窗体上没有目标控件：" + target);

        ApplyFormProperty(form, control, name, value);
        ReloadDesigner(document, form);
        return new { target = control?.Name ?? form.Name, name, value };
    }

    private object McpRenameControl(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) ||
            !YyFormSerializer.IsIdentifier(newName))
        {
            throw new InvalidOperationException("新控件名称无效。");
        }

        var document = McpFormDocument();
        var view = document.FormView!;
        var form = view.BuildForm();
        var control = form.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, oldName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("窗体上没有控件：" + oldName);
        if (form.Controls.Any(item =>
                !ReferenceEquals(item, control) &&
                string.Equals(item.Name, newName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("控件名称重复：" + newName);
        }

        control.Name = newName;
        ReloadDesigner(document, form);
        return new { name = newName };
    }

    private object McpDeleteControl(string name)
    {
        var document = McpFormDocument();
        var view = document.FormView!;
        var form = view.BuildForm();
        var removed = form.Controls.RemoveAll(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (removed == 0)
        {
            throw new InvalidOperationException("窗体上没有控件：" + name);
        }

        ReloadDesigner(document, form);
        return new { name, removed };
    }

    private object McpCreateEvent(string controlName, string eventName)
    {
        var document = McpFormDocument();
        var view = document.FormView!;
        var form = view.CurrentForm
            ?? throw new InvalidOperationException("窗体尚未载入。");
        var control = string.IsNullOrWhiteSpace(controlName)
            ? null
            : form.Controls.FirstOrDefault(item =>
                  string.Equals(item.Name, controlName, StringComparison.Ordinal))
              ?? throw new InvalidOperationException("窗体上没有控件：" + controlName);
        var kind = eventName switch
        {
            "被单击" or "单击" or "Click" => FormDesignerEventKind.Click,
            "内容被改变" or "文本改变" or "TextChanged" => FormDesignerEventKind.TextChanged,
            "列表项被选择" or "选择改变" or "SelectionChanged" => FormDesignerEventKind.SelectionChanged,
            "选中状态被改变" or "选中改变" or "CheckedChanged" => FormDesignerEventKind.CheckedChanged,
            "创建完毕" or "载入" or "Load" => FormDesignerEventKind.FormLoad,
            "窗口打开" or "打开" or "Open" => FormDesignerEventKind.FormOpen,
            "将被销毁" or "关闭" or "Close" => FormDesignerEventKind.FormClose,
            _ => throw new InvalidOperationException("未知事件：" + eventName)
        };
        var existing = control is null ? null : FormDesignerEvents.GetHandler(control, kind);
        var suggested = FormDesignerEvents.SuggestedHandlerName(form, control, kind);
        HandleDesignerEvent(
            document,
            new FormDesignerEventRequestedEventArgs(control, kind, suggested, existing));
        return new
        {
            control = control?.Name,
            handler = !string.IsNullOrWhiteSpace(existing) ? existing : suggested
        };
    }

    private object McpRunPreview()
    {
        var document = McpFormDocument();
        if (document.FormView is not { } view)
        {
            throw new InvalidOperationException("请先打开窗体设计器。");
        }

        _lastPreview?.Close();
        var codePath = YyFormCompanionFiles.UserCodePath(document.Path);
        var code = File.Exists(codePath)
            ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
            : null;
        _lastPreview = new FormPreviewWindow(view.BuildForm(), document.Path, code);
        _lastPreview.Closed += (_, _) => _lastPreview = null;
        _lastPreview.Show(this);
        return new { title = _lastPreview.Title, status = _lastPreview.EventStatus };
    }

    private object McpPreviewClick(string controlName)
    {
        var preview = _lastPreview
            ?? throw new InvalidOperationException("预览未打开，请先 run_preview。");
        return new
        {
            control = controlName,
            invoked = preview.InvokeControlEvent(controlName)
        };
    }

    private object McpPreviewGetText(string controlName)
    {
        var preview = _lastPreview
            ?? throw new InvalidOperationException("预览未打开，请先 run_preview。");
        return new { control = controlName, text = preview.GetControlText(controlName) };
    }

    private void ReloadDesigner(EditorDocument document, YyFormModel form)
    {
        document.FormView!.LoadForm(form, _workspace.Project?.DirectoryPath, document.Path);
        document.IsDirty = true;
        UpdateDocumentHeader(document);
    }

    private static void ApplyFormProperty(
        YyFormModel form,
        YyControlModel? control,
        string name,
        string value)
    {
        static double Number(string text) =>
            double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new InvalidOperationException("属性值必须是数字：" + text);

        static bool Bool(string text) =>
            string.Equals(text.Trim(), "真", StringComparison.Ordinal) ||
            string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        if (control is null)
        {
            switch (name)
            {
                case "名称": form.Name = value; break;
                case "标题": form.Title = value; break;
                case "宽度": form.Width = Number(value); break;
                case "高度": form.Height = Number(value); break;
                case "背景": form.Background = value; break;
                case "图标": form.Icon = value; break;
                case "备注": form.Remark = value; break;
                case "边框": form.BorderStyle = value; break;
                case "可否移动": form.Movable = Bool(value); break;
                case "初始位置": form.StartPosition = value; break;
                case "最小化按钮": form.MinimizeBox = Bool(value); break;
                case "最大化按钮": form.MaximizeBox = Bool(value); break;
                default: throw new InvalidOperationException("未知窗体属性：" + name);
            }

            return;
        }

        switch (name)
        {
            case "名称": control.Name = value; break;
            case "标题" or "文本": control.Text = value; break;
            case "备注": control.Remark = value; break;
            case "左边" or "左": control.X = Number(value); break;
            case "顶边" or "上": control.Y = Number(value); break;
            case "宽度": control.Width = Number(value); break;
            case "高度": control.Height = Number(value); break;
            case "字号": control.FontSize = Number(value); break;
            case "前景": control.Foreground = value; break;
            case "背景": control.Background = value; break;
            case "可视" or "可见": control.IsVisible = Bool(value); break;
            case "禁止" or "启用": control.IsEnabled = !Bool(value); break;
            case "选中": control.IsChecked = Bool(value); break;
            case "只读": control.ReadOnly = Bool(value); break;
            case "多行": control.MultiLine = Bool(value); break;
            case "密码": control.PasswordChar = value; break;
            case "最大长度": control.MaxLength = (int)Number(value); break;
            case "图片": control.Picture = value; break;
            default: throw new InvalidOperationException("未知控件属性：" + name);
        }
    }

}
