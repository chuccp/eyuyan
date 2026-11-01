using System.Text;
using Yy.Compiler;
using Yy.Language;

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
        var resolvedTarget = EnsureSupportedTarget(target);
        var fullSource = Path.GetFullPath(sourcePath);
        var program = YyRunner.ValidateFile(fullSource);
        var generated = new NativeEmitter(program, fullSource).Generate();
        var image = NativePeWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.GuiSubsystem);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllBytes(fullOutput, image);
        return new NativeBuildResult(fullOutput, image.Length, 0x1000, resolvedTarget);
    }

    public static NativeBuildResult CompileSource(
        string source,
        string sourceName,
        string outputPath,
        string target = YyBuildTargets.Auto)
    {
        var resolvedTarget = EnsureSupportedTarget(target);
        var program = YyRunner.ValidateSource(source, sourceName);
        var generated = new NativeEmitter(program, sourceName).Generate();
        var image = NativePeWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.GuiSubsystem);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllBytes(fullOutput, image);
        return new NativeBuildResult(fullOutput, image.Length, 0x1000, resolvedTarget);
    }

    public static NativeBuildResult CompileProject(
        YyProject project,
        string outputPath,
        string? target = null)
    {
        var resolvedTarget = EnsureSupportedTarget(target ?? project.TargetPlatform);
        var program = ProjectCompiler.LoadProgram(project);
        var form = project.Forms.FirstOrDefault(item =>
                string.Equals(
                    item.Model.Name,
                    project.StartupForm,
                    StringComparison.Ordinal))
            ?? project.Forms.FirstOrDefault();
        var generated = new NativeEmitter(
            program,
            project.EntryPath,
            form?.Model,
            project.Forms.Select(item => item.Model).ToArray()).Generate();
        var image = NativePeWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.GuiSubsystem);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllBytes(fullOutput, image);
        return new NativeBuildResult(fullOutput, image.Length, 0x1000, resolvedTarget);
    }

    private static YyBuildTarget EnsureSupportedTarget(string target)
    {
        var resolved = YyBuildTargets.Resolve(target);
        if (!resolved.IsWindows)
        {
            throw new PlatformNotSupportedException(
                $"目标“{resolved.Id}”需要 macOS AppKit/Mach-O 后端；" +
                "当前程序集只包含 Windows Win32/PE 后端。");
        }

        return resolved;
    }
}

internal static class NativeText
{
    public static byte[] Utf8Z(string value) =>
        Encoding.UTF8.GetBytes(value + "\0");
}
