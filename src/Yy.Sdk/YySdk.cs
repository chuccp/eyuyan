namespace Yy.Sdk;

public enum YyPackageKind
{
    LanguageCore,
    Platform,
    Tooling
}

public enum YyPackageStatus
{
    Preview,
    Planned
}

public sealed record YyPackageDefinition(
    string Name,
    YyPackageKind Kind,
    YyPackageStatus Status,
    string Description,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Builtins,
    IReadOnlyList<string> Dependencies)
{
    public bool IsImportable => Status != YyPackageStatus.Planned;

    public string StatusText => Status switch
    {
        YyPackageStatus.Preview => "预览",
        YyPackageStatus.Planned => "规划中",
        _ => Status.ToString()
    };
}

/// <summary>
/// 易语系统 SDK 的公开入口。SDK 只封装平台组件；语言语义由编译器与运行时自身实现。
/// </summary>
public static class YySdk
{
    public const int Version = 1;

    public const string CorePackage = "系统.基础";
    public const string CollectionPackage = "系统.集合";
    public const string TextPackage = "系统.文本";
    public const string DataPackage = "系统.数据";
    public const string UiPackage = "系统.界面";
    public const string FilePackage = "系统.文件";
    public const string MemoryPackage = "系统.内存";
    public const string EncodingPackage = "系统.编码";
    public const string ProcessPackage = "系统.进程";
    public const string DiagnosticPackage = "系统.诊断";
    public const string SerializationPackage = "系统.序列化";
    public const string InteropPackage = "系统.动态库";
    public const string SecurityPackage = "系统.安全";
    public const string NetworkPackage = "系统.网络";
    public const string HttpPackage = "系统.网络.HTTP";
    public const string TlsPackage = "系统.网络.TLS";
    public const string SocketPackage = "系统.网络.Socket";
    public const string WebSocketPackage = "系统.网络.WebSocket";
    public const string PackageManagerPackage = "系统.包";
    public const string BuildPackage = "系统.构建";
    public const string CompilerToolchainPackage = "工具链.编译";

    public static IReadOnlyList<YyPackageDefinition> Packages { get; } =
    [
        new(
            CorePackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Preview,
            "语言基础类型、变量、流程控制和通用转换。",
            ["文本", "数字", "逻辑", "日期", "如果", "计次循环", "遍历", "转换"],
            [
                "输出", "打印", "提示", "断言", "类型", "数字", "整数", "小数",
                "绝对值", "平方根", "幂", "最大", "最小", "随机数",
                "当前时间", "格式化时间", "取环境变量", "取运行目录", "取启动时间",
                "延时", "断点"
            ],
            []),
        new(
            CollectionPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Preview,
            "数组、对象、序列和集合算法。",
            ["数组", "对象", "长度", "范围", "追加", "插入", "移除", "排序", "倒序"],
            ["长度", "范围", "键集", "值集", "追加", "插入", "移除", "排序", "倒序"],
            [CorePackage]),
        new(
            TextPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Preview,
            "语言原生文本处理。",
            ["文本", "包含", "分割", "连接", "替换", "修剪", "取子文本"],
            [
                "文本", "包含", "分割", "连接", "替换", "修剪", "转大写", "转小写",
                "取子文本", "取文本左边", "取文本右边", "取文本中间",
                "UTF8编码", "UTF8解码"
            ],
            [CorePackage]),
        new(
            DataPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Preview,
            "JSON 与结构化数据转换。",
            ["JSON解析", "JSON生成"],
            ["JSON解析", "JSON生成"],
            [CorePackage]),
        new(
            UiPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Preview,
            "平台窗口、控件和基于资源 ID 的界面上下文。",
            ["窗口", "控件", "按钮", "文本", "输入框", "图片框", "取控件"],
            ["取控件"],
            [CorePackage]),
        new(
            FilePackage,
            YyPackageKind.Platform,
            YyPackageStatus.Preview,
            "操作系统文件、目录和路径操作。",
            ["文件存在", "取目录文件", "读文件", "写文件", "删除文件"],
            ["文件存在", "取目录文件", "读文件", "写文件", "删除文件"],
            [CorePackage]),
        new(
            MemoryPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Planned,
            "字节、缓冲区、内存分配和安全指针。",
            ["字节", "字节数组", "缓冲区", "分配", "释放", "复制", "填充", "指针"],
            [],
            [CorePackage]),
        new(
            EncodingPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Planned,
            "UTF-8、UTF-16、码点与字符集转换。",
            ["UTF8", "UTF16", "码点", "字符分类", "BOM"],
            [],
            [CorePackage, MemoryPackage]),
        new(
            ProcessPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Preview,
            "命令行、环境、标准流、子进程和退出码。",
            ["运行程序", "等待进程", "退出码"],
            ["运行程序"],
            [CorePackage, FilePackage]),
        new(
            DiagnosticPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Planned,
            "源码位置、诊断、错误码和调用栈。",
            ["源位置", "源码范围", "诊断", "诊断级别", "调用栈"],
            [],
            [CorePackage, TextPackage, EncodingPackage]),
        new(
            SerializationPackage,
            YyPackageKind.LanguageCore,
            YyPackageStatus.Planned,
            "JSON、XML 和二进制序列化。",
            ["JSON读取器", "JSON写入器", "XML读取器", "XML写入器", "二进制读取器", "二进制写入器"],
            [],
            [CorePackage, MemoryPackage, EncodingPackage, TextPackage]),
        new(
            InteropPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "动态库声明、函数指针和 Win32 互操作。",
            ["动态库", "函数指针", "调用约定", "句柄", "LastError"],
            [],
            [CorePackage, MemoryPackage]),
        new(
            SecurityPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "哈希、安全随机数、签名和证书验证。",
            ["SHA256", "安全随机数", "签名验证", "证书"],
            [],
            [CorePackage, MemoryPackage, EncodingPackage, InteropPackage]),
        new(
            NetworkPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "网络地址、端点、超时和通用错误。",
            ["IP地址", "主机名", "端口", "网络端点", "连接超时"],
            [],
            [CorePackage, MemoryPackage, EncodingPackage, TextPackage]),
        new(
            TlsPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "基于 Windows 系统能力的 TLS 与证书校验。",
            ["TLS连接", "证书验证", "SNI", "ALPN"],
            [],
            [NetworkPackage, SecurityPackage, InteropPackage]),
        new(
            HttpPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "HTTP 客户端、代理、重定向和流式下载。",
            ["HTTP请求", "HTTP响应", "下载文件", "代理", "重定向"],
            [],
            [NetworkPackage, TlsPackage]),
        new(
            SocketPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "TCP、UDP、DNS 和非阻塞网络通信。",
            ["TCP客户端", "TCP服务端", "UDP套接字", "DNS查询"],
            [],
            [NetworkPackage, ProcessPackage]),
        new(
            WebSocketPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "WebSocket 握手、帧、心跳与重连。",
            ["WebSocket连接", "WebSocket帧", "Ping", "Pong"],
            [],
            [HttpPackage, SocketPackage]),
        new(
            BuildPackage,
            YyPackageKind.Tooling,
            YyPackageStatus.Planned,
            "项目构建、临时文件、原子替换和增量缓存。",
            ["构建目标", "构建任务", "临时目录", "原子替换", "增量缓存"],
            [],
            [CorePackage, DiagnosticPackage, FilePackage, ProcessPackage, SerializationPackage]),
        new(
            PackageManagerPackage,
            YyPackageKind.Platform,
            YyPackageStatus.Planned,
            "SDK 包索引、下载、版本解析、完整性校验和锁文件。",
            ["包清单", "版本约束", "依赖解析", "锁文件", "离线包"],
            [],
            [BuildPackage, HttpPackage, TlsPackage, SecurityPackage]),
        new(
            CompilerToolchainPackage,
            YyPackageKind.Tooling,
            YyPackageStatus.Planned,
            "可自举的易语词法、语法、语义、IR、Win64 和 PE 工具链。",
            ["词法分析", "语法分析", "语义分析", "中间表示", "Win64生成", "PE写出", "项目编译"],
            [],
            [BuildPackage, DiagnosticPackage, SerializationPackage])
    ];

    /// <summary>语言核心包默认可见；平台包仍需显式导入。</summary>
    public static IReadOnlyList<string> DefaultPackages { get; } =
        Packages
            .Where(package =>
                package.Status == YyPackageStatus.Preview &&
                package.Kind == YyPackageKind.LanguageCore)
            .Select(package => package.Name)
            .ToArray();

    public static YyPackageDefinition? FindPackage(string name) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.Name, name.Trim(), StringComparison.Ordinal));

    public static bool IsImportable(string name) =>
        FindPackage(name)?.IsImportable == true;

    public static IReadOnlySet<string> ResolveEnabledPackages(
        IEnumerable<string> imports) =>
        DefaultPackages
            .Concat(imports)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>包名使用点分隔标识符，例如 app、demo.app、公司.产品.模块。</summary>
    public static bool IsPackageName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value
            .Split('.', StringSplitOptions.None)
            .All(IsPackageSegment);
    }

    public static bool IsBuiltinAvailable(
        string functionName,
        IEnumerable<string> importedPackages)
    {
        var imports = importedPackages.ToHashSet(StringComparer.Ordinal);
        return Packages.Any(package =>
            imports.Contains(package.Name) &&
            package.Builtins.Contains(functionName, StringComparer.Ordinal));
    }

    public static IReadOnlyList<string> PackagesProvidingBuiltin(string functionName) =>
        Packages
            .Where(package => package.Builtins.Contains(functionName, StringComparer.Ordinal))
            .Select(package => package.Name)
            .ToArray();

    private static bool IsPackageSegment(string value) =>
        value.Length > 0 &&
        (char.IsLetter(value[0]) || value[0] == '_') &&
        value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
}

/// <summary>原生窗口句柄。零值表示无效句柄。</summary>
public readonly record struct YyNativeHandle(nint Value)
{
    public bool IsValid => Value != 0;
}

public enum YyUiValueKind
{
    Empty,
    Boolean,
    Integer,
    Text,
    Handle
}

/// <summary>不装箱的 SDK 值，供界面、诊断和跨平台服务共享。</summary>
public readonly record struct YyUiValue(
    YyUiValueKind Kind,
    long IntegerValue = 0,
    string TextValue = "",
    YyNativeHandle HandleValue = default,
    bool BooleanValue = false)
{
    public static YyUiValue Empty => new(YyUiValueKind.Empty);
    public static YyUiValue From(bool value) =>
        new(YyUiValueKind.Boolean, BooleanValue: value);
    public static YyUiValue From(long value) =>
        new(YyUiValueKind.Integer, IntegerValue: value);
    public static YyUiValue From(string value) =>
        new(YyUiValueKind.Text, TextValue: value);
    public static YyUiValue From(YyNativeHandle value) =>
        new(YyUiValueKind.Handle, HandleValue: value);
}

public enum YyUiElementType
{
    Window,
    Button,
    Label,
    TextBox,
    CheckBox,
    RadioButton,
    ComboBox,
    ListBox,
    GroupBox,
    PictureBox
}

public enum YyUiProperty
{
    Text,
    Title,
    X,
    Y,
    Width,
    Height,
    Visible,
    Enabled,
    Selected,
    Picture
}

/// <summary>界面元素描述。标识是跨设计器、预览和原生编译稳定的协议。</summary>
public readonly record struct YyUiElement(
    string Id,
    string Name,
    YyUiElementType Type,
    YyNativeHandle Handle);

/// <summary>原生界面上下文，不依赖 WPF、CLR 对象或反射。</summary>
public interface IYyContext
{
    string FormId { get; }
    YyNativeHandle Find(string id);
    bool TryFind(string id, out YyNativeHandle handle);
    YyUiValue GetProperty(YyNativeHandle handle, YyUiProperty property);
    void SetProperty(YyNativeHandle handle, YyUiProperty property, YyUiValue value);
}

/// <summary>窗口和消息循环的最小宿主契约。</summary>
public interface IYyUiHost : IYyContext
{
    void Show();
    int RunMessageLoop();
    void Close();
}
