namespace Yy.Language.Structured;

/// <summary>
/// 用户代码的源码转换入口。`.yyc` 磁盘格式始终是可直接编辑的易语源码；
/// 结构化对象仅作为 IDE 内存模型，不再序列化为 XML。
/// </summary>
public static class YyCodeSerializer
{
    public static string Serialize(YyCodeDocument document) =>
        YyCodeDocumentConverter.ToSource(document);

    public static YyCodeDocument Deserialize(string text, string? sourceName = null) =>
        YyCodeDocumentConverter.FromSource(text, sourceName);
}

public sealed class YyCodeFormatException(string message, int line, int column) : Exception(message)
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
}
