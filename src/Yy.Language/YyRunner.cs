using System.Collections.ObjectModel;
using Yy.Language.Runtime;
using Yy.Language.Syntax;

namespace Yy.Language;

public sealed record YyRunResult(
    object? Value,
    IReadOnlyDictionary<string, object?> Variables,
    TimeSpan Elapsed);

public static class YyRunner
{
    public static YyProgram Compile(string source) => new Parser(source).Parse();

    public static YyProgram CompileFile(string path) =>
        ModuleLoader.CompileFile(Path.GetFullPath(path));

    public static YyProgram CompileSource(string source, string? sourcePath) =>
        ModuleLoader.CompileSource(source, sourcePath);

    public static YyProgram ValidateFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var program = CompileFile(fullPath);
        ThrowFirstDiagnostic(program, fullPath);
        return program;
    }

    public static YyProgram ValidateSource(string source, string? sourcePath)
    {
        var program = CompileSource(source, sourcePath);
        ThrowFirstDiagnostic(program, sourcePath);
        return program;
    }

    public static YyRunResult Run(
        string source,
        Action<string>? output = null,
        CancellationToken cancellationToken = default,
        YyDebugProbe? debugger = null,
        Func<string, object?>? controlResolver = null,
        IReadOnlyDictionary<string, object?>? initialVariables = null)
    {
        return RunProgram(
            ModuleLoader.CompileSource(source, null),
            output,
            cancellationToken,
            sourcePath: null,
            debugger,
            controlResolver,
            initialVariables);
    }

    public static YyRunResult RunSource(
        string source,
        string? sourcePath,
        Action<string>? output = null,
        CancellationToken cancellationToken = default,
        YyDebugProbe? debugger = null,
        Func<string, object?>? controlResolver = null,
        IReadOnlyDictionary<string, object?>? initialVariables = null)
    {
        return RunProgram(
            ModuleLoader.CompileSource(source, sourcePath),
            output,
            cancellationToken,
            sourcePath,
            debugger,
            controlResolver,
            initialVariables);
    }

    public static YyRunResult RunFile(
        string path,
        Action<string>? output = null,
        CancellationToken cancellationToken = default,
        YyDebugProbe? debugger = null,
        Func<string, object?>? controlResolver = null,
        IReadOnlyDictionary<string, object?>? initialVariables = null)
    {
        return RunProgram(
            ModuleLoader.CompileFile(Path.GetFullPath(path)),
            output,
            cancellationToken,
            Path.GetFullPath(path),
            debugger,
            controlResolver,
            initialVariables);
    }

    private static YyRunResult RunProgram(
        YyProgram program,
        Action<string>? output,
        CancellationToken cancellationToken,
        string? sourcePath,
        YyDebugProbe? debugger = null,
        Func<string, object?>? controlResolver = null,
        IReadOnlyDictionary<string, object?>? initialVariables = null)
    {
        var started = DateTime.UtcNow;
        ThrowFirstDiagnostic(program, sourcePath);

        var interpreter = new Interpreter(
            output,
            cancellationToken,
            debugger,
            controlResolver,
            initialVariables);
        var value = interpreter.Execute(program);
        return new YyRunResult(
            value,
            new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(interpreter.Globals, StringComparer.Ordinal)),
            DateTime.UtcNow - started);
    }

    private static void ThrowFirstDiagnostic(YyProgram program, string? sourcePath)
    {
        var diagnostics = YySemanticAnalyzer.Analyze(program, sourcePath);
        if (diagnostics.Count > 0)
        {
            throw diagnostics[0];
        }
    }
}

