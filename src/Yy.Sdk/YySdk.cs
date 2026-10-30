namespace Yy.Sdk;

public enum YyPackageKind
{
    LanguageCore,
    Platform
}

public sealed record YyPackageDefinition(
    string Name,
    YyPackageKind Kind,
    string Description,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Builtins);

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

    public static IReadOnlyList<YyPackageDefinition> Packages { get; } =
    [
        new(
            CorePackage,
            YyPackageKind.LanguageCore,
            "语言基础类型、变量、流程控制和通用转换。",
            ["文本", "数字", "逻辑", "日期", "如果", "计次循环", "遍历", "转换"],
            [
                "输出", "打印", "提示", "断言", "类型", "数字", "整数", "小数",
                "绝对值", "平方根", "幂", "最大", "最小", "随机数",
                "当前时间", "格式化时间", "取环境变量", "取运行目录", "取启动时间",
                "延时", "断点"
            ]),
        new(
            CollectionPackage,
            YyPackageKind.LanguageCore,
            "数组、对象、序列和集合算法。",
            ["数组", "对象", "长度", "范围", "追加", "插入", "移除", "排序", "倒序"],
            ["长度", "范围", "键集", "值集", "追加", "插入", "移除", "排序", "倒序"]),
        new(
            TextPackage,
            YyPackageKind.LanguageCore,
            "语言原生文本处理。",
            ["文本", "包含", "分割", "连接", "替换", "修剪", "取子文本"],
            [
                "文本", "包含", "分割", "连接", "替换", "修剪", "转大写", "转小写",
                "取子文本", "取文本左边", "取文本右边", "取文本中间",
                "UTF8编码", "UTF8解码"
            ]),
        new(
            DataPackage,
            YyPackageKind.LanguageCore,
            "JSON 与结构化数据转换。",
            ["JSON解析", "JSON生成"],
            ["JSON解析", "JSON生成"]),
        new(
            UiPackage,
            YyPackageKind.Platform,
            "平台窗口、控件和基于资源 ID 的界面上下文。",
            ["窗口", "控件", "按钮", "文本", "输入框", "图片框", "取控件"],
            ["取控件"]),
        new(
            FilePackage,
            YyPackageKind.Platform,
            "操作系统文件、目录和路径操作。",
            ["文件存在", "取目录文件", "读文件", "写文件", "删除文件"],
            ["文件存在", "取目录文件", "读文件", "写文件", "删除文件"])
    ];

    public static YyPackageDefinition? FindPackage(string name) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.Name, name.Trim(), StringComparison.Ordinal));

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

/// <summary>窗口和控件访问的上下文契约。</summary>
public interface IYyContext
{
    string FormId { get; }
    object? GetControl(string id);
    bool TryGetControl(string id, out object? control);
    T? GetService<T>() where T : class;
}

/// <summary>与具体 UI 技术无关的控件外观。</summary>
public interface IYyUiElement
{
    string Id { get; }
    string Name { get; }
    string TypeName { get; }
    object? GetProperty(string name);
    void SetProperty(string name, object? value);
}

public interface IYyWindow : IYyUiElement
{
    string Title { get; set; }
    double Width { get; set; }
    double Height { get; set; }
}

public interface IYyControl : IYyUiElement
{
    string Text { get; set; }
    bool Visible { get; set; }
    bool Enabled { get; set; }
}
