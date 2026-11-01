namespace Yy.Native.Semantics;

/// <summary>
/// 平台无关的原生值布局。所有后端必须遵守这一约定，使文件、字体、
/// 控件和第三方绑定的互操作只需要一套内存模型。
/// </summary>
internal static class NativeRuntimeLayout
{
    public const int ValueSize = 8;
    public const int CountOffset = 0;
    public const int PayloadOffset = 8;

    public static string Newline(Yy.Compiler.YyBuildTarget target) =>
        target.IsWindows ? "\r\n" : "\n";
}
