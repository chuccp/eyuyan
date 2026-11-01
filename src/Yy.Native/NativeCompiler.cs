using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Native.Backends;

namespace Yy.Native;

public sealed record NativeBuildResult(
    string OutputPath,
    int ImageSize,
    int EntryRva,
    YyBuildTarget Target);

public static class NativeCompiler
{
    public static NativeBuildResult CompileFile(
        string sourcePath,
        string outputPath,
        string target = YyBuildTargets.Auto)
    {
        var resolvedTarget = YyBuildTargets.Resolve(target);
        var fullSource = Path.GetFullPath(sourcePath);
        var program = YyRunner.ValidateFile(fullSource);
        return Compile(
            program,
            fullSource,
            [],
            null,
            null,
            outputPath,
            resolvedTarget);
    }

    public static NativeBuildResult CompileSource(
        string source,
        string sourceName,
        string outputPath,
        string target = YyBuildTargets.Auto)
    {
        var resolvedTarget = YyBuildTargets.Resolve(target);
        var program = YyRunner.ValidateSource(source, sourceName);
        return Compile(
            program,
            sourceName,
            [],
            null,
            null,
            outputPath,
            resolvedTarget);
    }

    public static NativeBuildResult CompileProject(
        YyProject project,
        string outputPath,
        string? target = null)
    {
        var resolvedTarget = YyBuildTargets.Resolve(target ?? project.TargetPlatform);
        var program = ProjectCompiler.LoadProgram(project);
        var startupForm = project.Forms.FirstOrDefault(item =>
                string.Equals(
                    item.Model.Name,
                    project.StartupForm,
                    StringComparison.Ordinal))
            ?? project.Forms.FirstOrDefault();
        var forms = project.Forms.Select(item => item.Model).ToArray();
        return Compile(
            program,
            project.EntryPath,
            forms,
            startupForm?.Model,
            project.DirectoryPath,
            outputPath,
            resolvedTarget);
    }

    private static NativeBuildResult Compile(
        YyProgram program,
        string sourceName,
        IReadOnlyList<YyFormModel> forms,
        YyFormModel? startupForm,
        string? projectDirectory,
        string outputPath,
        YyBuildTarget target)
    {
        var backend = NativeBackendRegistry.Resolve(target);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        return backend.Compile(new NativeBackendRequest(
            program,
            sourceName,
            target,
            fullOutput,
            forms,
            startupForm,
            projectDirectory));
    }
}
