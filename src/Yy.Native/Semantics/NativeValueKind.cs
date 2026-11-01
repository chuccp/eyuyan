namespace Yy.Native.Semantics;

/// <summary>
/// 平台无关的原生值类别。各平台后端使用同一套值布局约定：
/// 文本为 [长度][UTF-8 字节][0]，数组为 [元素数][元素...]，
/// 对象为固定形状的 [字段数][字段...]。
/// </summary>
internal enum NativeValueKind
{
    Integer,
    Text,
    Boolean,
    Array,
    Object
}
