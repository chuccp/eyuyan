using System.Collections.ObjectModel;
using System.IO;
using Yy.Compiler;
using Yy.Native;

namespace Yy.Studio.Core.Workspace;

/// <summary>IDE 与 CLI 共用的工程编译入口，不依赖任何 UI 框架。</summary>
public sealed class StudioWorkspace
{
    public YyProject? Project { get; private set; }

    public void Load(string projectPathOrDirectory)
    {
        var path = Path.GetFullPath(projectPathOrDirectory);
        var directory = Directory.Exists(path)
            ? path
            : File.Exists(path) &&
              string.Equals(Path.GetExtension(path), ".yyproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path)!
                : throw new InvalidDataException($"找不到工程或目录：{projectPathOrDirectory}");

        Project = YyProject.LoadForDirectory(directory)
            ?? throw new InvalidDataException($"目录中没有 .yyproj：{directory}");
    }

    public void Reload()
    {
        if (Project is null)
        {
            throw new InvalidOperationException("尚未打开工程。");
        }

        Project = YyProject.LoadForDirectory(Project.DirectoryPath)
            ?? throw new InvalidDataException($"目录中没有 .yyproj：{Project.DirectoryPath}");
    }

    public ObservableCollection<ProjectNode> BuildTree()
    {
        if (Project is null)
        {
            return [];
        }

        var root = new ProjectNode(Project.Name, Project.ProjectPath, isDirectory: false);
        AddDirectory(root, Project.SourceRootPath, skipGenerated: false);
        AddDirectory(root, Project.ResourceRootPath, skipGenerated: false);
        return new ObservableCollection<ProjectNode>([root]);
    }

    public NativeBuildResult BuildFile(
        string sourcePath,
        string? outputPath = null,
        string? targetId = null)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource) ||
            !string.Equals(Path.GetExtension(fullSource), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("只能编译 .yyc 源文件。");
        }

        var target = YyBuildTargets.Resolve(targetId ?? YyBuildTargets.Auto);
        var output = outputPath ?? Path.Combine(
            Path.GetTempPath(),
            "yy-studio-file-" + Guid.NewGuid().ToString("N"),
            Path.GetFileNameWithoutExtension(fullSource) + target.ExecutableExtension);
        return NativeCompiler.CompileFile(fullSource, output, target.Id);
    }

    public NativeBuildResult Build(string? outputPath = null, string? targetId = null)
    {
        if (Project is null)
        {
            throw new InvalidOperationException("尚未打开工程。");
        }

        Reload();
        var project = Project!;
        var target = YyBuildTargets.Resolve(targetId ?? project.TargetPlatform);
        var output = outputPath ?? Path.Combine(
            project.DirectoryPath,
            Path.GetFileNameWithoutExtension(project.OutputName) +
            target.ExecutableExtension);
        output = YyBuildTargets.NormalizeOutputPath(output, target);
        return NativeCompiler.CompileProject(project, output, target.Id);
    }

    public void Check()
    {
        if (Project is null)
        {
            throw new InvalidOperationException("尚未打开工程。");
        }

        Reload();
        new ProjectCompiler().Validate(Project!);
    }

    public static bool CanRun(NativeBuildResult result) =>
        YyBuildTargets.CanRunOnCurrentHost(result.Target);

    private static void AddDirectory(
        ProjectNode parent,
        string directory,
        bool skipGenerated)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var node = new ProjectNode(
            Path.GetFileName(directory),
            directory,
            isDirectory: true);
        parent.Children.Add(node);
        AddChildren(node, directory, skipGenerated);
    }

    private static void AddChildren(
        ProjectNode parent,
        string directory,
        bool skipGenerated)
    {
        foreach (var child in Directory
                     .EnumerateDirectories(directory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsIgnoredDirectory(child))
            {
                continue;
            }

            var node = new ProjectNode(
                Path.GetFileName(child),
                child,
                isDirectory: true);
            parent.Children.Add(node);
            AddChildren(node, child, skipGenerated);
        }

        foreach (var file in Directory
                     .EnumerateFiles(directory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (skipGenerated && IsGenerated(name))
            {
                continue;
            }

            parent.Children.Add(new ProjectNode(
                name,
                file,
                isDirectory: false,
                isGenerated: IsGenerated(name)));
        }
    }

    private static bool IsGenerated(string fileName) =>
        fileName.EndsWith(".Designer.yyc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "ids.xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoredDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return name is "bin" or "obj" or ".git" or ".vs";
    }
}
