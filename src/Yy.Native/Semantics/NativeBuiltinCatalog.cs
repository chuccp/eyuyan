namespace Yy.Native.Semantics;

/// <summary>
/// 语言核心内置命令的公共目录。后端按名称分派实现，
/// 新增命令时必须同时更新目录、补全、文档和测试。
/// </summary>
internal static class NativeBuiltinCatalog
{
    public static bool IsOutput(string name) => name is "输出" or "打印" or "提示";

    public static bool IsWindowCommand(string name) =>
        name is "打开窗口" or "显示窗口" or "关闭窗口";

    public static bool IsControlLookup(string name) => name is "取控件";

    public static bool IsFileBuiltin(string name) =>
        name is "文件存在" or "删除文件" or "读文件" or "写文件" or "取目录文件";

    public static bool IsProcessBuiltin(string name) => name is "运行程序";

    public static NativeValueKind? TryGetResultKind(string name) => name switch
    {
        "长度" or "数字" or "运行程序" => NativeValueKind.Integer,
        "文本" or "读文件" => NativeValueKind.Text,
        "追加" or "取目录文件" => NativeValueKind.Array,
        "文件存在" or "删除文件" or "写文件" => NativeValueKind.Boolean,
        "打开窗口" or "显示窗口" or "关闭窗口" => NativeValueKind.Boolean,
        _ => null
    };
}
