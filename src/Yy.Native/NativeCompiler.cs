using System.Text;
using Yy.Compiler;
using Yy.Language;

namespace Yy.Native;

public sealed record NativeBuildResult(
    string OutputPath,
    int ImageSize,
    int EntryRva);

public static class NativeCompiler
{
    public static NativeBuildResult CompileFile(string sourcePath, string outputPath)
    {
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
        return new NativeBuildResult(fullOutput, image.Length, 0x1000);
    }

    public static NativeBuildResult CompileSource(
        string source,
        string sourceName,
        string outputPath)
    {
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
        return new NativeBuildResult(fullOutput, image.Length, 0x1000);
    }

    public static NativeBuildResult CompileProject(
        YyProject project,
        string outputPath)
    {
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
            form?.Model).Generate();
        var image = NativePeWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.GuiSubsystem);
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        File.WriteAllBytes(fullOutput, image);
        return new NativeBuildResult(fullOutput, image.Length, 0x1000);
    }
}

internal static class NativeText
{
    public static byte[] Utf8Z(string value) =>
        Encoding.UTF8.GetBytes(value + "\0");
}
