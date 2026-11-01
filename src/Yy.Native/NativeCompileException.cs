namespace Yy.Native;

public sealed class NativeCompileException(
    string message,
    int line,
    int column,
    string? sourceName) : Exception(message)
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
    public string? SourceName { get; } = sourceName;
}
