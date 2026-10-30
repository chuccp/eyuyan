using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yy.Studio.Mcp;

/// <summary>
/// 易语 Studio 的 MCP(Model Context Protocol)服务端:
/// JSON-RPC 2.0,支持 streamable HTTP(TCP 127.0.0.1)与 stdio 两种传输。
/// 协议层(握手/工具目录/封包)在此;工具调用统一分发到宿主在 UI 线程执行,
/// 每个工具都走 IDE 真实的界面操作代码路径。
/// </summary>
public sealed class StudioMcpServer
{
    public const int PreferredPort = 37213;

    private readonly Func<string, JsonNode?, object?> _handler;
    private readonly Action<Action> _dispatch;
    private TcpListener? _listener;

    public StudioMcpServer(Func<string, JsonNode?, object?> handler, Action<Action> dispatch)
    {
        _handler = handler;
        _dispatch = dispatch;
    }

    /// <summary>实际监听端口(0=未启动)。</summary>
    public int Port { get; private set; }

    /// <summary>在回环地址启动 HTTP 传输，并按配置选择端口。写 %TEMP%\yystudio-mcp.port 供发现。</summary>
    public void StartTcp(
        int preferredPort = PreferredPort,
        bool autoFindAvailablePort = true,
        int portAttempts = 10)
    {
        if (preferredPort is < 1024 or > 65535)
        {
            return;
        }

        var attempts = autoFindAvailablePort
            ? Math.Clamp(portAttempts, 1, 65536 - preferredPort)
            : 1;
        for (var port = preferredPort; port < preferredPort + attempts; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                _listener = listener;
                Port = port;
                break;
            }
            catch (SocketException)
            {
            }
        }

        if (_listener is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => AcceptLoop());
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "yystudio-mcp.port"), Port.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>stdio 传输(YyStudio.exe --mcp-stdio):逐行 JSON-RPC,供 MCP 客户端以子进程方式拉起。</summary>
    public void RunStdio()
    {
        try
        {
            var input = Console.In;
            var output = Console.Out;
            while (true)
            {
                var line = input.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var response = HandleMessage(line);
                if (response is not null)
                {
                    output.WriteLine(response);
                    output.Flush();
                }
            }
        }
        catch (Exception)
        {
            // 管道关闭即退出。
        }
    }

    /// <summary>处理一条 JSON-RPC 消息文本;返回响应文本(通知类消息返回 null)。测试可直接调用。</summary>
    public string? HandleMessage(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "JSON 解析错误" }
            }.ToJsonString(WriteOptions);
        }

        if (node is JsonArray batch)
        {
            var results = new JsonArray();
            foreach (var item in batch)
            {
                var response = HandleNode(item);
                if (response is not null)
                {
                    results.Add(response);
                }
            }

            return results.Count == 0 ? null : results.ToJsonString(WriteOptions);
        }

        return HandleNode(node)?.ToJsonString(WriteOptions);
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private JsonNode? HandleNode(JsonNode? node)
    {
        if (node is not JsonObject request)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = null,
                ["error"] = new JsonObject { ["code"] = -32600, ["message"] = "无效请求" }
            };
        }

        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>() ?? string.Empty;

        JsonNode Ok(JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        JsonNode Err(int code, string message) => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };

        switch (method)
        {
            case "initialize":
                var clientVersion = request["params"]?["protocolVersion"]?.GetValue<string>();
                return Ok(new JsonObject
                {
                    ["protocolVersion"] = string.IsNullOrWhiteSpace(clientVersion) ? "2025-03-26" : clientVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "yy-studio", ["version"] = "1.0.0" }
                });
            case "notifications/initialized":
            case "notifications/cancelled":
                return null;
            case "ping":
                return Ok(new JsonObject());
            case "tools/list":
                return Ok(JsonNode.Parse(ToolCatalogJson)!);
            case "tools/call":
                return Ok(ToolsCall(request["params"]));
            default:
                return id is null ? null : Err(-32601, "未知方法: " + method);
        }
    }

    private JsonNode ToolsCall(JsonNode? paramsNode)
    {
        var name = paramsNode?["name"]?.GetValue<string>() ?? string.Empty;
        var arguments = paramsNode?["arguments"];
        JsonNode Result(bool isError, string text) => new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = isError
        };

        try
        {
            object? value = null;
            Exception? failure = null;
            _dispatch(() =>
            {
                try
                {
                    value = _handler(name, arguments);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            if (failure is not null)
            {
                return Result(true, failure.Message);
            }

            var text = value is null ? "null" : JsonSerializer.Serialize(value, WriteOptions);
            return Result(false, text);
        }
        catch (Exception exception)
        {
            return Result(true, "工具执行失败: " + exception.Message);
        }
    }

    private void AcceptLoop()
    {
        while (_listener is { } listener)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var body = ReadHttpBody(stream, out var isPost);
                if (body is null)
                {
                    WriteHttp(stream, 400, "text/plain", "bad request");
                    return;
                }

                if (!isPost)
                {
                    WriteHttp(stream, 405, "text/plain", "use POST for JSON-RPC");
                    return;
                }

                var response = HandleMessage(body);
                if (response is null)
                {
                    WriteHttp(stream, 202, "text/plain", string.Empty);
                }
                else
                {
                    WriteHttp(stream, 200, "application/json; charset=utf-8", response);
                }
            }
            catch (Exception)
            {
                // 连接中断忽略。
            }
        }
    }

    private static string? ReadHttpBody(Stream stream, out bool isPost)
    {
        isPost = false;
        var buffer = new byte[8192];
        var data = new MemoryStream();
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return null;
            }

            data.Write(buffer, 0, read);
            if (data.Length > 64 * 1024)
            {
                return null;
            }

            headerEnd = IndexOfHeaderEnd(data.GetBuffer(), (int)data.Length);
        }

        var all = data.ToArray();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var firstLine = headerText.Split("\r\n")[0];
        isPost = firstLine.StartsWith("POST", StringComparison.OrdinalIgnoreCase);
        var length = 0;
        foreach (var line in headerText.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[15..].Trim(), out length);
            }
        }

        var bodyStart = headerEnd + 4;
        var body = new byte[length];
        var got = Math.Min(all.Length - bodyStart, length);
        if (got > 0)
        {
            Array.Copy(all, bodyStart, body, 0, got);
        }

        while (got < length)
        {
            var read = stream.Read(body, got, length - got);
            if (read <= 0)
            {
                break;
            }

            got += read;
        }

        return Encoding.UTF8.GetString(body, 0, got);
    }

    private static int IndexOfHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' &&
                buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static void WriteHttp(Stream stream, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 202 ? "Accepted" : "Error")}\r\n" +
            $"Content-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        stream.Write(head, 0, head.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>MCP 工具目录(名称/说明/入参 JSON Schema)。</summary>
    private const string ToolCatalogJson = """
        [
          {"name":"state","description":"读取 IDE 当前状态:打开的文档、窗体与控件清单、预览状态。用于在操作前了解现状。","inputSchema":{"type":"object","properties":{},"additionalProperties":false}},
          {"name":"screenshot","description":"对 IDE 主窗口或预览窗口做离屏截图并保存为 PNG,返回文件路径(可用 Read 工具查看)。","inputSchema":{"type":"object","properties":{"target":{"type":"string","enum":["ide","preview"],"description":"ide=主窗口(默认), preview=预览窗口"},"path":{"type":"string","description":"PNG 保存路径;省略则自动生成"}},"additionalProperties":false}},
          {"name":"new_program","description":"新建一个易语窗口程序(等同 程序→新建→易语窗口程序→确定)。","inputSchema":{"type":"object","properties":{},"additionalProperties":false}},
          {"name":"create_project","description":"在指定父目录创建完整工程骨架并打开启动窗体，源码位于 Sources，资源位于 Resources。","inputSchema":{"type":"object","properties":{"path":{"type":"string","description":"工程父目录；实际目录为 path/name"},"name":{"type":"string","description":"工程名称和清单主名"},"package":{"type":"string","description":"项目包名，例如 demo.app；源码子目录通过 包名.目录 导入"}},"required":["path","name","package"],"additionalProperties":false}},
          {"name":"open_file","description":"打开 .yyx 窗体或 .yyc 代码文件(等同 程序→打开)。","inputSchema":{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}},
          {"name":"save_program","description":"保存窗体三件套：界面 .yyx、自动生成关系 .Designer.yyc、用户代码 .yyc。给出 path(如 X:/目录/看图软件.yyx)则按同名规则保存三个文件；不给 path 则保存到各自已有路径。","inputSchema":{"type":"object","properties":{"path":{"type":"string"}},"additionalProperties":false}},
          {"name":"add_resource","description":"复制资源到工程 Resources 分类目录，登记资源表并更新 Resources/ids.xml，返回可引用标识。","inputSchema":{"type":"object","properties":{"source":{"type":"string","description":"源文件绝对路径"},"type":{"type":"string","enum":["图片","声音","文件"]},"name":{"type":"string","description":"可选的新资源标识名"}},"required":["source","type"],"additionalProperties":false}},
          {"name":"add_control","description":"在窗体设计器中添加控件(等同组件箱双击放置),可同时指定名称/标题/位置/大小,返回生成的控件名。","inputSchema":{"type":"object","properties":{"type":{"type":"string","enum":["按钮","文本","输入框","单选框","复选框","组合框","列表框","分组框","图片框"]},"x":{"type":"number"},"y":{"type":"number"},"name":{"type":"string"},"text":{"type":"string"},"width":{"type":"number"},"height":{"type":"number"}},"required":["type","x","y"],"additionalProperties":false}},
          {"name":"set_property","description":"设置窗体或控件属性(等同属性夹编辑)。target 为控件名;窗体本身用“_窗口”。控件属性名:名称/备注/标题/左边/顶边/宽度/高度/字号/前景/背景/可视/禁止/选中/只读/多行/密码/最大长度/图片/数据绑定。窗体属性名:名称/备注/标题/图标/宽度/高度/背景/边框/可否移动/初始位置/最小化按钮/最大化按钮。","inputSchema":{"type":"object","properties":{"target":{"type":"string"},"name":{"type":"string"},"value":{"type":"string"}},"required":["target","name","value"],"additionalProperties":false}},
          {"name":"rename_control","description":"重命名控件(等同属性夹“名称”行,含重名校验)。","inputSchema":{"type":"object","properties":{"old":{"type":"string"},"new":{"type":"string"}},"required":["old","new"],"additionalProperties":false}},
          {"name":"delete_control","description":"从窗体删除指定控件。","inputSchema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}},
          {"name":"create_event","description":"为控件或窗体创建官方命名的事件处理子程序(等同设计器双击/事件下拉),返回子程序名。窗体事件:创建完毕/将被销毁;控件事件:被单击/内容被改变/列表项被选择/选中状态被改变。","inputSchema":{"type":"object","properties":{"control":{"type":"string","description":"控件名;窗体本身用“_窗口”"},"event":{"type":"string","enum":["被单击","内容被改变","列表项被选择","选中状态被改变","创建完毕","将被销毁"]}},"required":["control","event"],"additionalProperties":false}},
          {"name":"get_code","description":"读取用户代码文件的易语源码文本(含 子程序/结束子程序 结构)，不读取自动生成关系文件。","inputSchema":{"type":"object","properties":{},"additionalProperties":false}},
          {"name":"set_subprogram","description":"写入一个事件/子程序的语句体。body 为易语语句行(支持 变量/如果/否则/结束如果/赋值/调用 等),行与行用 \\n 分隔;整段替换该子程序的语句,子程序不存在则创建。","inputSchema":{"type":"object","properties":{"name":{"type":"string"},"body":{"type":"string"}},"required":["name","body"],"additionalProperties":false}},
          {"name":"delete_subprogram","description":"从当前窗体的用户代码删除指定子程序，不修改自动生成关系；随后调用 save_program 持久化。","inputSchema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}},
          {"name":"run_preview","description":"运行窗体预览(等同 F5/运行→预览),预览窗口打开时自动执行窗体载入事件。","inputSchema":{"type":"object","properties":{},"additionalProperties":false}},
          {"name":"preview_click","description":"在预览窗口触发控件的事件(按钮=被单击),等待事件执行完成,返回事件输出/状态文本。","inputSchema":{"type":"object","properties":{"control":{"type":"string"}},"required":["control"],"additionalProperties":false}},
          {"name":"preview_get_text","description":"读取预览窗口中控件的当前文本。","inputSchema":{"type":"object","properties":{"control":{"type":"string"}},"required":["control"],"additionalProperties":false}},
          {"name":"preview_close","description":"关闭预览窗口。","inputSchema":{"type":"object","properties":{},"additionalProperties":false}}
        ]
        """;
}
