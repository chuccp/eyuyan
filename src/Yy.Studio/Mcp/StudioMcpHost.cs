using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Yy.Language.Structured;
using Yy.Studio.Forms;
using Yy.Studio.Mcp;
using Yy.Studio.Properties;

namespace Yy.Studio;

/// <summary>
/// MCP 工具宿主:每个工具都调用 MainWindow 的真实界面操作路径
/// (新建/组件箱/属性夹/事件创建/预览),与用户点击等价。
/// </summary>
public partial class MainWindow
{
    private StudioMcpServer? _mcpServer;
    private YyFormPreviewWindow? _lastPreview;
    private int _mcpShotSeq;

    /// <summary>启动 MCP 服务(TCP 127.0.0.1,端口见 %TEMP%\yystudio-mcp.port);args 含 --mcp-stdio 时同时启用 stdio。</summary>
    public void McpAttach(string[] args)
    {
        if (_mcpServer is not null)
        {
            return;
        }

        _mcpServer = new StudioMcpServer(McpExecute, action => Dispatcher.Invoke(action));
        _mcpServer.StartTcp();
        if (Array.Exists(args, argument =>
                string.Equals(argument, "--mcp-stdio", StringComparison.OrdinalIgnoreCase)))
        {
            var server = _mcpServer;
            System.Threading.Tasks.Task.Run(server.RunStdio);
        }

        StudioTrace.Write($"MCP server started on port {_mcpServer.Port}");
    }

    private object? McpExecute(string tool, JsonNode? args)
    {
        string? Arg(string name) => args?[name]?.GetValue<string>();
        string ArgOr(string name, string fallback) => Arg(name) ?? fallback;
        double? Num(string name) => args?[name] is { } value ? value.GetValue<double>() : null;

        switch (tool)
        {
            case "state":
                return McpState();
            case "screenshot":
                return McpScreenshot(ArgOr("target", "ide"), Arg("path"));
            case "new_program":
                McpEnsureIdle();
                CreateBlankWindowProgram();
                return McpState();
            case "open_file":
                OpenDocument(Path.GetFullPath(ArgOr("path", string.Empty)));
                return McpState();
            case "save_program":
                return McpSave(Arg("path"));
            case "add_control":
                return McpAddControl(
                    ArgOr("type", string.Empty),
                    Num("x") ?? 40,
                    Num("y") ?? 40,
                    Arg("name"),
                    Arg("text"),
                    Num("width"),
                    Num("height"));
            case "set_property":
                return McpSetProperty(ArgOr("target", string.Empty), ArgOr("name", string.Empty), ArgOr("value", string.Empty));
            case "rename_control":
                return McpRenameControl(ArgOr("old", string.Empty), ArgOr("new", string.Empty));
            case "delete_control":
                return McpDeleteControl(ArgOr("name", string.Empty));
            case "create_event":
                return McpCreateEvent(ArgOr("control", string.Empty), ArgOr("event", string.Empty));
            case "get_code":
                return McpGetCode();
            case "set_subprogram":
                return McpSetSubprogram(ArgOr("name", string.Empty), ArgOr("body", string.Empty));
            case "run_preview":
                McpEnsureDesigner();
                PreviewCurrentForm();
                return new { title = _designForm!.Title, status = _lastPreview?.EventStatus };
            case "preview_click":
                return McpPreviewClick(ArgOr("control", string.Empty));
            case "preview_get_text":
                return new
                {
                    control = Arg("control"),
                    text = (_lastPreview ?? throw new InvalidOperationException("预览未打开,请先 run_preview。"))
                        .GetControlText(ArgOr("control", string.Empty))
                };
            case "preview_close":
                _lastPreview?.Close();
                _lastPreview = null;
                return new { closed = true };
            default:
                throw new InvalidOperationException("未知工具: " + tool);
        }
    }

    private object McpState()
    {
        object? form = null;
        if (_designForm is { } designForm)
        {
            form = new
            {
                name = designForm.Name,
                title = designForm.Title,
                width = designForm.Width,
                height = designForm.Height,
                codeFile = designForm.CodeFile,
                loadEvent = designForm.LoadEvent,
                closeEvent = designForm.CloseEvent,
                controls = designForm.Controls.Select(control => new
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

        return new
        {
            documents = _documents.Select(document => new
            {
                name = document.DisplayName,
                path = document.FilePath,
                dirty = document.IsDirty,
                isForm = document.IsForm
            }).ToArray(),
            current = _currentDocument?.DisplayName,
            form,
            preview = _lastPreview is { } preview
                ? new { title = preview.Title, running = preview.EventRunning, status = preview.EventStatus }
                : null
        };
    }

    private object McpScreenshot(string target, string? path)
    {
        Window? window = target switch
        {
            "preview" => _lastPreview,
            _ => this
        };
        if (window is null)
        {
            throw new InvalidOperationException("预览窗口未打开,请先 run_preview。");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            var directory = Path.Combine(Path.GetTempPath(), "yystudio-mcp");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, $"shot-{++_mcpShotSeq:000}.png");
        }

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        return new { path, width, height };
    }

    private object McpSave(string? path)
    {
        var saved = new List<string>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path)!;
            var baseName = Path.GetFileNameWithoutExtension(path);
            McpEnsureDesigner();
            var formDocument = _currentDocument!.IsForm
                ? _currentDocument
                : McpFormDocument()!;
            // 先按旧 CodeFile 解析代码后置,再改名保存:顺序反了会匹配失败并另建空文档。
            var codeDocument = McpResolveCodeDocument(createIfMissing: true);
            _designForm!.CodeFile = baseName + ".yyc";
            DesignerChanged();
            if (!WriteDocument(formDocument, path))
            {
                throw new InvalidOperationException("窗体保存失败: " + path);
            }

            formDocument.MarkSaved(path);
            saved.Add(path);
            if (codeDocument is not null)
            {
                var codePath = Path.Combine(directory, baseName + ".yyc");
                if (!WriteDocument(codeDocument, codePath))
                {
                    throw new InvalidOperationException("代码后置保存失败: " + codePath);
                }

                codeDocument.MarkSaved(codePath);
                saved.Add(codePath);
            }
        }
        else
        {
            foreach (var document in _documents.Where(item =>
                         !item.IsStartPage && (item.IsDirty || item.FilePath is null)).ToList())
            {
                if (document.FilePath is null)
                {
                    throw new InvalidOperationException($"文档“{document.DisplayName}”尚未保存过,请提供 path 参数。");
                }

                if (!WriteDocument(document, document.FilePath))
                {
                    throw new InvalidOperationException("保存失败: " + document.FilePath);
                }

                document.MarkSaved(document.FilePath);
                saved.Add(document.FilePath);
            }
        }

        return new { saved };
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
        McpEnsureDesigner();
        if (!YyControlTypes.All.Contains(type, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("不支持的控件类型: " + type);
        }

        AddDesignerControl(type, new Point(x, y));
        var control = _selectedControl!;
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, control.Name, StringComparison.Ordinal))
        {
            McpSelectControl(control);
            McpCommitProperty("名称", name);
        }

        if (text is not null)
        {
            McpSelectControl(control);
            McpCommitProperty("标题", text);
        }

        if (width is { } widthValue)
        {
            McpSelectControl(control);
            McpCommitProperty("宽度", widthValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (height is { } heightValue)
        {
            McpSelectControl(control);
            McpCommitProperty("高度", heightValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        return new { name = control.Name };
    }

    private object McpSetProperty(string target, string name, string value)
    {
        McpEnsureDesigner();
        if (target is "_窗口" or "窗口")
        {
            _selectedControl = null;
            _selectedControls.Clear();
            ClearSelectionVisuals();
        }
        else
        {
            McpSelectControl(McpFindControl(target));
        }

        McpCommitProperty(name, value);
        return McpState();
    }

    private object McpRenameControl(string oldName, string newName)
    {
        McpEnsureDesigner();
        McpSelectControl(McpFindControl(oldName));
        McpCommitProperty("名称", newName);
        return new { name = newName };
    }

    private object McpDeleteControl(string name)
    {
        McpEnsureDesigner();
        var control = McpFindControl(name);
        _designForm!.Controls.Remove(control);
        _selectedControl = null;
        _selectedControls.Clear();
        var formDocument = McpFormDocument()!;
        DesignerChanged();
        ActivateDocument(formDocument);
        return McpState();
    }

    private object McpCreateEvent(string controlName, string eventName)
    {
        McpEnsureDesigner();
        if (controlName is "_窗口" or "窗口")
        {
            var isLoad = eventName switch
            {
                "创建完毕" => true,
                "将被销毁" => false,
                _ => throw new InvalidOperationException("窗体事件只支持:创建完毕/将被销毁。")
            };
            CreateOrOpenFormEventHandler(isLoad);
            return new { handler = isLoad ? _designForm!.LoadEvent : _designForm!.CloseEvent };
        }

        var control = McpFindControl(controlName);
        var kind = eventName switch
        {
            "被单击" => ControlEventKind.Click,
            "内容被改变" => ControlEventKind.TextChanged,
            "列表项被选择" => ControlEventKind.SelectionChanged,
            "选中状态被改变" => ControlEventKind.CheckedChanged,
            _ => throw new InvalidOperationException("控件事件只支持:被单击/内容被改变/列表项被选择/选中状态被改变。")
        };
        CreateOrOpenControlEventHandler(control, kind);
        return new { handler = GetControlEvent(control, kind) };
    }

    private object McpGetCode()
    {
        var codeDocument = McpResolveCodeDocument(createIfMissing: false);
        if (codeDocument is null)
        {
            return new { source = string.Empty };
        }

        var model = YyCodeSerializer.Deserialize(codeDocument.Text);
        return new { source = YyCodeDocumentConverter.ToSource(model) };
    }

    private object McpSetSubprogram(string name, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("缺少子程序名称。");
        }

        McpEnsureDesigner();
        var codeDocument = McpResolveCodeDocument(createIfMissing: true)!;
        var model = YyCodeSerializer.Deserialize(codeDocument.Text);
        var stub = YyCodeDocumentConverter.FromSource(
            "子程序 " + name + "()\n" + body + "\n结束子程序\n");
        var stubFunction = stub.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("语句体解析失败,请检查语句行。");
        var function = model.Functions.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (function is null)
        {
            function = new YyCodeFunction { Name = name };
            model.Functions.Add(function);
        }

        function.Statements.Clear();
        function.Statements.AddRange(stubFunction.Statements);
        codeDocument.Text = YyCodeSerializer.Serialize(model);
        if (ReferenceEquals(_currentDocument, codeDocument))
        {
            ActivateDocument(codeDocument);
        }

        return new { name, statements = function.Statements.Count };
    }

    private object McpPreviewClick(string controlName)
    {
        if (_lastPreview is not { } preview)
        {
            throw new InvalidOperationException("预览未打开,请先 run_preview。");
        }

        if (!preview.InvokeControlEvent(controlName))
        {
            throw new InvalidOperationException($"预览中没有名为“{controlName}”的按钮控件。");
        }

        var frame = new DispatcherFrame();
        var waited = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            waited += 100;
            if (!preview.EventRunning || waited >= 12000)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return new { control = controlName, status = preview.EventStatus };
    }

    /// <summary>确保设计器可用(当前停在代码页时切回窗体页)。</summary>
    private void McpEnsureDesigner()
    {
        if (_designForm is not null)
        {
            return;
        }

        var formDocument = McpFormDocument()
            ?? throw new InvalidOperationException("请先新建或打开窗体(new_program / open_file)。");
        ActivateDocument(formDocument);
    }

    /// <summary>新建前若已有窗体,先激活到窗体页(避免旧内存文档混淆)。</summary>
    private void McpEnsureIdle()
    {
    }

    private EditorDocument? McpFormDocument() =>
        _documents.FirstOrDefault(document => document.IsForm);

    private YyControlModel McpFindControl(string name) =>
        _designForm?.Controls.FirstOrDefault(control =>
                string.Equals(control.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"窗体上没有名为“{name}”的控件。");

    private void McpSelectControl(YyControlModel control) => SelectDesignerControl(control);

    /// <summary>走属性夹提交路径(PropertyRow→CommitPropertyRow),校验失败会抛出。</summary>
    private void McpCommitProperty(string name, string value)
    {
        var row = new PropertyRow(name, value, PropertyCellKind.Text, string.Empty);
        CommitPropertyRow(row);
        if (!string.Equals(row.ValueText, value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"属性“{name}”拒绝了值“{value}”。");
        }
    }

    private EditorDocument? McpResolveCodeDocument(bool createIfMissing)
    {
        var codeFileName = string.IsNullOrWhiteSpace(_designForm?.CodeFile)
            ? "未命名窗体.yyc"
            : _designForm!.CodeFile;
        var codeDocument = _documents.FirstOrDefault(document =>
                document.FilePath is null &&
                string.Equals(document.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase))
            ?? _documents.FirstOrDefault(document =>
                document.FilePath is not null &&
                string.Equals(
                    Path.GetFileName(document.FilePath),
                    Path.GetFileName(codeFileName),
                    StringComparison.OrdinalIgnoreCase));
        if (codeDocument is null && createIfMissing)
        {
            var model = new YyCodeDocument { Name = (_designForm?.Name ?? "窗体") + "代码" };
            codeDocument = new EditorDocument(null, codeFileName, YyCodeSerializer.Serialize(model));
            _documents.Add(codeDocument);
        }

        return codeDocument;
    }
}
