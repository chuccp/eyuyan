using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language.Structured;
using Yy.Studio.Avalonia.Features.Forms;
using Yy.Studio.Core.Mcp;

namespace Yy.Studio.Avalonia;

/// <summary>MCP 服务端接入。工具调用统一回到 Avalonia UI 线程，复用真实 IDE 操作。</summary>
public sealed partial class MainWindow
{
    private StudioMcpServer? _mcpServer;

    public void McpAttach(string[] args)
    {
        if (_mcpServer is not null)
        {
            return;
        }

        if (!Configuration.Mcp.Enabled)
        {
            SetMcpEndpoint(null);
            AppendConsole("MCP 服务已在 Studio 配置中停用。");
            return;
        }

        var port = ReadMcpPortOverride(args) ?? Configuration.Mcp.Port;
        _mcpServer = new StudioMcpServer(McpExecute, action => Dispatcher.UIThread.Invoke(action));
        _mcpServer.StartTcp(
            port,
            Configuration.Mcp.AutoFindAvailablePort,
            Configuration.Mcp.PortAttempts);
        if (_mcpServer.Port == 0)
        {
            _mcpServer = null;
            SetMcpEndpoint(null);
            AppendConsole($"MCP 无法在端口 {port} 启动。");
            return;
        }

        if (args.Contains("--mcp-stdio", StringComparer.OrdinalIgnoreCase))
        {
            var server = _mcpServer;
            _ = Task.Run(server.RunStdio);
        }

        if (_mcpServer.Port != port)
        {
            AppendConsole(
                $"MCP 端口 {port} 已被占用（可能已有 Studio 在运行），" +
                $"已自动切换到 {_mcpServer.Port}。");
        }

        SetMcpEndpoint(_mcpServer.Port);
        AppendConsole($"MCP：http://127.0.0.1:{_mcpServer.Port}/mcp");
    }

    private void SetMcpEndpoint(int? port)
    {
        if (port is null)
        {
            McpEndpointButton.Content = "MCP 未启用";
            McpEndpointButton.Tag = null;
            return;
        }

        var endpoint = $"http://127.0.0.1:{port}/mcp";
        McpEndpointButton.Content = $"MCP {endpoint}";
        McpEndpointButton.Tag = endpoint;
    }

    private async void OnCopyMcpEndpoint(object? sender, RoutedEventArgs e)
    {
        if (McpEndpointButton.Tag is not string endpoint)
        {
            SetStatus("MCP 服务未启用。");
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(endpoint);
            SetStatus("已复制 MCP 地址：" + endpoint);
        }
    }

    private static int? ReadMcpPortOverride(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--mcp-port", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                int.TryParse(args[index + 1], out var value))
            {
                return value;
            }

            const string prefix = "--mcp-port=";
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[index][prefix.Length..], out value))
            {
                return value;
            }
        }

        return null;
    }

    private object? McpExecute(string tool, JsonNode? args)
    {
        string? Arg(string name) => args?[name]?.GetValue<string>();
        string ArgOr(string name, string fallback) => Arg(name) ?? fallback;

        switch (tool)
        {
            case "state":
                return McpState();
            case "screenshot":
                return McpScreenshot(ArgOr("target", "ide"), Arg("path"));
            case "new_program":
                return McpNewProgram();
            case "create_project":
                return McpCreateProject(
                    ArgOr("path", string.Empty),
                    ArgOr("name", string.Empty),
                    ArgOr("package", string.Empty));
            case "open_file":
                return McpOpenFile(ArgOr("path", string.Empty));
            case "save_program":
                return McpSaveProgram(Arg("path"));
            case "build_project":
                var result = _workspace.Project is null
                    ? throw new InvalidOperationException("请先打开工程。")
                    : _workspace.Build(targetId: TargetBox.SelectedItem as string);
                return new
                {
                    path = result.OutputPath,
                    size = result.ImageSize,
                    target = result.Target.Id
                };
            case "add_resource":
                return McpAddResource(
                    ArgOr("source", string.Empty),
                    ArgOr("type", string.Empty),
                    Arg("name"));
            case "add_control":
                return McpAddControl(
                    ArgOr("type", string.Empty),
                    args?["x"]?.GetValue<double>() ?? 40,
                    args?["y"]?.GetValue<double>() ?? 40,
                    Arg("name"),
                    Arg("text"),
                    args?["width"]?.GetValue<double>(),
                    args?["height"]?.GetValue<double>());
            case "set_property":
                return McpSetProperty(
                    ArgOr("target", string.Empty),
                    ArgOr("name", string.Empty),
                    ArgOr("value", string.Empty));
            case "rename_control":
                return McpRenameControl(ArgOr("old", string.Empty), ArgOr("new", string.Empty));
            case "delete_control":
                return McpDeleteControl(ArgOr("name", string.Empty));
            case "create_event":
                return McpCreateEvent(ArgOr("control", string.Empty), ArgOr("event", string.Empty));
            case "run_preview":
                return McpRunPreview();
            case "preview_click":
                return McpPreviewClick(ArgOr("control", string.Empty));
            case "preview_get_text":
                return McpPreviewGetText(ArgOr("control", string.Empty));
            case "preview_close":
                _lastPreview?.Close();
                _lastPreview = null;
                return new { closed = true };
            case "terminal_run":
                var terminalCommand = ArgOr("command", string.Empty);
                return new
                {
                    command = terminalCommand,
                    sent = _commandLine?.RunCommand(terminalCommand) == true
                };
            case "terminal_state":
                return _commandLine?.CaptureState();
            case "terminal_dump":
                return _commandLine?.Dump();
            case "get_code":
                return new { source = CurrentCodeSource(CurrentCodeDocument()) };
            case "set_subprogram":
                return McpSetSubprogram(ArgOr("name", string.Empty), ArgOr("body", string.Empty));
            case "delete_subprogram":
                return McpDeleteSubprogram(ArgOr("name", string.Empty));
            default:
                throw new InvalidOperationException(
                    "该 MCP 工具仍在从 WPF 迁移到 Avalonia：" + tool);
        }
    }

    private object McpState()
    {
        var project = _workspace.Project;
        return new
        {
            project = project is null
                ? null
                : new
                {
                    name = project.Name,
                    package = project.PackageName,
                    path = project.ProjectPath,
                    target = project.TargetPlatform,
                    forms = project.Forms.Count,
                    resources = project.ResourceTable.Select(item => new
                    {
                        name = item.Name,
                        type = item.TypeName,
                        file = item.File
                    }).ToArray()
                },
            documents = _tabs.Documents.Select(document => new
            {
                name = document.FileName,
                path = document.Path,
                dirty = document.IsDirty,
                generated = document.IsReadOnly
            }).ToArray(),
            current = CurrentDocument?.Path,
            form = McpFormState(),
            preview = _lastPreview is { } preview
                ? new { title = preview.Title, running = preview.EventRunning, status = preview.EventStatus }
                : null,
            buildTarget = TargetBox.SelectedItem as string,
            runningPid = _process?.Id,
            mcpPort = _mcpServer?.Port ?? 0,
            configuration = new
            {
                path = Configuration.ConfigurationPath,
                mcpEnabled = Configuration.Mcp.Enabled,
                shortcuts = Configuration.Shortcuts
            }
        };
    }

    private static string CurrentCodeSource(EditorDocument document)
    {
        if (document.StructuredView is not { } structured)
        {
            return document.Editor.Text ?? string.Empty;
        }

        // 纯代码视图以编辑器文本为准;经典视图先同步代码区再输出模型。
        if (!document.UseClassicView)
        {
            return document.Editor.Text ?? string.Empty;
        }

        if (!structured.FlushPendingEdits())
        {
            throw new InvalidOperationException(
                "经典视图中有未同步的代码，请先修正语法错误。");
        }

        return structured.Document is { } model
            ? YyCodeDocumentConverter.ToSource(model)
            : document.Editor.Text ?? string.Empty;
    }

    private void SetCodeModel(EditorDocument document, YyCodeDocument model)
    {
        var source = YyCodeDocumentConverter.ToSource(model);
        document.SuppressDirtyTracking = true;
        try
        {
            document.Editor.Text = source;
        }
        finally
        {
            document.SuppressDirtyTracking = false;
        }

        document.StructuredView?.LoadDocument(model);
        document.IsDirty = true;
        UpdateDocumentHeader(document);
    }

    private object McpSaveProgram(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var document = CurrentDocument
                ?? throw new InvalidOperationException("当前没有打开的文档。");
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (document.FormView is { } formView)
            {
                var oldCodePath = YyFormCompanionFiles.UserCodePath(document.Path);
                var codePath = YyFormCompanionFiles.UserCodePath(fullPath);
                if (File.Exists(oldCodePath) &&
                    !string.Equals(oldCodePath, codePath, StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(codePath)!);
                    File.Copy(oldCodePath, codePath, overwrite: true);
                }

                var form = formView.BuildForm();
                form.CodeFile = Path.GetFileName(codePath);
                formView.LoadForm(
                    form,
                    _workspace.Project?.DirectoryPath,
                    fullPath);
                if (!formView.SaveForm(fullPath) ||
                    !formView.WriteBinding(fullPath, codePath))
                {
                    throw new InvalidOperationException("窗体三件套保存失败。");
                }

                document.Path = fullPath;
                document.FileName = Path.GetFileName(fullPath);
                document.Tab.Tag = fullPath;
                document.SavedText = formView.SerializeForm() ?? string.Empty;
                document.Editor.Text = document.SavedText;
                document.IsDirty = false;
                document.HeaderText.Text = document.FileName;
                return new
                {
                    saved = new[] { fullPath, codePath, YyFormCompanionFiles.BindingPath(fullPath) }
                };
            }

            if (document.IsReadOnly)
            {
                throw new InvalidOperationException("生成文件不能直接保存。");
            }

            var text = document.Editor.Text ?? string.Empty;
            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            document.Path = fullPath;
            document.FileName = Path.GetFileName(fullPath);
            document.Tab.Tag = fullPath;
            document.SavedText = text;
            document.IsDirty = false;
            document.HeaderText.Text = document.FileName;
            return new { saved = new[] { fullPath } };
        }

        if (!SaveAll())
        {
            throw new InvalidOperationException("保存失败。");
        }

        return new { saved = _tabs.Documents.Select(document => document.Path).ToArray() };
    }

    private object McpNewProgram()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "yy-studio-program-" + Guid.NewGuid().ToString("N"));
        var project = YyProject.CreateProject(
            Path.Combine(directory, "易语程序.yyproj"),
            "易语程序",
            "demo.程序");
        OpenProjectPath(project.DirectoryPath);
        if (project.Forms.FirstOrDefault() is { } form)
        {
            OpenDocument(form.FormPath);
        }

        return McpState();
    }

    private EditorDocument CurrentCodeDocument()
    {
        var document = CurrentDocument;
        if (document is null ||
            !string.Equals(Path.GetExtension(document.Path), ".yyc", StringComparison.OrdinalIgnoreCase) ||
            document.IsReadOnly)
        {
            throw new InvalidOperationException("请先打开可编辑的 .yyc 源码。");
        }

        return document;
    }

    private object McpSetSubprogram(string name, string body)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("缺少子程序名称。");
        }

        var document = CurrentCodeDocument();
        var model = YyCodeDocumentConverter.FromSource(
            CurrentCodeSource(document),
            document.Path);
        var stub = YyCodeDocumentConverter.FromSource(
            "子程序 " + name + "()\n" + body + "\n结束子程序\n");
        var stubFunction = stub.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("语句体解析失败，请检查语句行。");
        var function = model.Functions.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (function is null)
        {
            function = new YyCodeFunction { Name = name };
            model.Functions.Add(function);
        }

        function.Statements.Clear();
        function.Statements.AddRange(stubFunction.Statements);
        SetCodeModel(document, model);
        return new { name, statements = function.Statements.Count };
    }

    private object McpDeleteSubprogram(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("缺少子程序名称。");
        }

        var document = CurrentCodeDocument();
        var model = YyCodeDocumentConverter.FromSource(
            CurrentCodeSource(document),
            document.Path);
        var removed = model.Functions.RemoveAll(function =>
            string.Equals(function.Name, name, StringComparison.Ordinal));
        if (removed == 0)
        {
            throw new InvalidOperationException("用户代码中没有子程序“" + name + "”。");
        }

        SetCodeModel(document, model);
        return new { name, removed };
    }

    private object McpCreateProject(string path, string name, string package)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(package))
        {
            throw new InvalidOperationException("create_project 需要 path、name 和 package。");
        }

        var directory = Path.Combine(Path.GetFullPath(path), name);
        var project = YyProject.CreateProject(
            Path.Combine(directory, name + ".yyproj"),
            name,
            package);
        OpenProjectPath(project.DirectoryPath);
        if (project.Forms.FirstOrDefault() is { } form)
        {
            OpenDocument(form.FormPath);
        }

        return McpState();
    }

    private object McpOpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("open_file 需要 path。");
        }

        var full = Path.GetFullPath(path);
        OpenProjectPath(full);

        return McpState();
    }

    private object McpAddResource(string source, string type, string? name)
    {
        var project = _workspace.Project
            ?? throw new InvalidOperationException("请先创建或打开工程。");
        var entry = project.AddResource(Path.GetFullPath(source), type, name);
        _workspace.Reload();
        RefreshProjectTree();
        return new
        {
            name = entry.Name,
            type = entry.TypeName,
            file = entry.File
        };
    }

    private object McpScreenshot(string target, string? path)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("截图必须在 UI 线程执行。");
        }

        Window window = string.Equals(target, "preview", StringComparison.OrdinalIgnoreCase)
            ? _lastPreview ?? throw new InvalidOperationException("预览未打开，请先 run_preview。")
            : this;
        var width = Math.Max(1, (int)window.Bounds.Width);
        var height = Math.Max(1, (int)window.Bounds.Height);
        var file = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Path.GetTempPath(), "yy-studio-" + Guid.NewGuid().ToString("N") + ".png")
            : Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);
        using (var stream = File.Create(file))
        {
            bitmap.Save(stream);
        }

        return new { path = file, width, height };
    }
}
