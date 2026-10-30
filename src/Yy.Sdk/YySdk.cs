namespace Yy.Sdk;

public sealed record YyPackageDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> Members);

/// <summary>
/// 易语系统 SDK 的公开入口。包是稳定的能力边界，后续可在其上增加
/// 类型、命令、界面组件和资源服务。
/// </summary>
public static class YySdk
{
    public const int Version = 1;

    public const string CorePackage = "系统.基础";
    public const string UiPackage = "系统.界面";
    public const string FilePackage = "系统.文件";
    public const string DataPackage = "系统.数据";

    public static IReadOnlyList<YyPackageDefinition> Packages { get; } =
    [
        new(
            CorePackage,
            "基础类型、变量、流程控制和通用转换。",
            ["文本", "数字", "逻辑", "数组", "对象", "日期", "如果", "计次循环", "遍历"]),
        new(
            UiPackage,
            "窗口、控件和基于资源 ID 的界面上下文。",
            ["窗口", "控件", "按钮", "文本", "输入框", "图片框", "取控件"]),
        new(
            FilePackage,
            "UTF-8 文件、目录和路径操作。",
            ["文件存在", "取目录文件", "读文件", "写文件", "删除文件"]),
        new(
            DataPackage,
            "JSON、数组、对象和数据转换。",
            ["JSON解析", "JSON生成", "键集", "值集", "追加", "插入", "移除", "排序"])
    ];

    public static YyPackageDefinition? FindPackage(string name) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.Name, name.Trim(), StringComparison.Ordinal));
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
