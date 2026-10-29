namespace Yy.Language;

public sealed class YyDiagnostic : Exception
{
    public YyDiagnostic(
        string message,
        int line,
        int column,
        string? hint = null,
        string? sourceName = null)
        : base(message)
    {
        Line = Math.Max(1, line);
        Column = Math.Max(1, column);
        Hint = hint;
        SourceName = sourceName;
    }

    public int Line { get; }
    public int Column { get; }
    public string? Hint { get; }
    public string? SourceName { get; }
}

public sealed class YyRuntimeError : Exception
{
    public YyRuntimeError(
        string message,
        int line,
        int column,
        string? detail = null,
        string? sourceName = null)
        : base(message)
    {
        Line = Math.Max(1, line);
        Column = Math.Max(1, column);
        Detail = detail;
        SourceName = sourceName;
    }

    public int Line { get; }
    public int Column { get; }
    public string? Detail { get; }
    public string? SourceName { get; }
}

