using System.Diagnostics;
using System.IO;
using System.Text;
using Yy.Language;
using Yy.Language.Syntax;

namespace Yy.Studio;

public sealed class ProjectCompiler
{
    public async Task<string> CompileAsync(
        YyProject project,
        string outputPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = FindRepositoryRoot()
            ?? throw new InvalidOperationException(
                "找不到易语源码仓库。编译需要项目编译器源码，请从仓库运行 IDE。");
        var runtimeProject = Path.Combine(
            repositoryRoot,
            "src",
            "Yy.CompilerRuntime",
            "Yy.CompilerRuntime.csproj");
        if (!File.Exists(runtimeProject))
        {
            throw new InvalidOperationException($"找不到编译运行时项目：{runtimeProject}");
        }

        progress?.Report("编译项目代码与窗体…");
        var program = BuildProgram(project);
        var generatedSource = GeneratedCodeEmitter.Generate(project, program);
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "yy-compile-" + Guid.NewGuid().ToString("N"));
        var generatedDirectory = Path.Combine(temporaryRoot, "GeneratedApp");
        var publishDirectory = Path.Combine(temporaryRoot, "publish");
        Directory.CreateDirectory(generatedDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(generatedDirectory, "GeneratedApp.cs"),
                generatedSource,
                new UTF8Encoding(false));
            var generatedProject = Path.Combine(generatedDirectory, "GeneratedApp.csproj");
            File.WriteAllText(generatedProject, CreateGeneratedProject(runtimeProject), new UTF8Encoding(false));
            Directory.CreateDirectory(publishDirectory);

            progress?.Report("正在把生成代码编译为独立 Windows 二进制…");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("publish");
            startInfo.ArgumentList.Add(generatedProject);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add("win-x64");
            startInfo.ArgumentList.Add("--self-contained");
            startInfo.ArgumentList.Add("true");
            startInfo.ArgumentList.Add("-p:PublishSingleFile=true");
            startInfo.ArgumentList.Add("-p:IncludeNativeLibrariesForSelfExtract=true");
            startInfo.ArgumentList.Add("-p:EnableCompressionInSingleFile=true");
            startInfo.ArgumentList.Add("-p:DebugType=None");
            startInfo.ArgumentList.Add("-p:DebugSymbols=false");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(publishDirectory);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 dotnet 编译器。");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
                throw new InvalidOperationException(
                    $"二进制编译失败（退出码 {process.ExitCode}）：\n{details}");
            }

            var compiledExe = Path.Combine(publishDirectory, "YyCompiledApplication.exe");
            if (!File.Exists(compiledExe))
            {
                throw new InvalidOperationException("编译命令完成，但没有找到输出 EXE。");
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))
                ?? throw new IOException("无法确定 EXE 输出目录。");
            Directory.CreateDirectory(outputDirectory);
            File.Copy(compiledExe, outputPath, overwrite: true);
            progress?.Report($"编译完成：{outputPath}");
            return Path.GetFullPath(outputPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Output has already been copied; cleanup is best effort.
            }
        }
    }

    private static YyProgram BuildProgram(YyProject project)
    {
        var entrySource = File.ReadAllText(project.EntryPath, new UTF8Encoding(false, true));
        var entryProgram = YyRunner.CompileSource(entrySource, project.EntryPath);
        var functions = entryProgram.Functions.ToList();
        var names = functions
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var form in project.Forms)
        {
            if (form.CodePath is not null)
            {
                var codeSource = File.ReadAllText(form.CodePath, new UTF8Encoding(false, true));
                var codeProgram = YyRunner.CompileSource(codeSource, form.CodePath);
                foreach (var function in codeProgram.Functions)
                {
                    if (!names.Add(function.Name))
                    {
                        throw new InvalidDataException(
                            $"子程序“{function.Name}”在项目源码与窗体代码后置中重复定义。");
                    }

                    functions.Add(function);
                }
            }

        }

        foreach (var form in project.Forms)
        {
            foreach (var (eventName, kind) in new[]
                     {
                         (form.Model.LoadEvent, "载入"),
                         (form.Model.CloseEvent, "关闭")
                     })
            {
                if (!string.IsNullOrWhiteSpace(eventName) && !names.Contains(eventName))
                {
                    throw new InvalidDataException(
                        $"窗体“{form.Model.Title}”的{kind}事件“{eventName}”没有对应子程序。");
                }
            }

            foreach (var control in form.Model.Controls)
            {
                if (!string.IsNullOrWhiteSpace(control.ClickEvent) &&
                    !names.Contains(control.ClickEvent))
                {
                    throw new InvalidDataException(
                        $"窗体“{form.Model.Title}”的控件“{control.Name}”绑定了不存在的事件“{control.ClickEvent}”。");
                }
            }
        }

        return new YyProgram(
            entryProgram.Line,
            entryProgram.Column,
            entryProgram.Metadata,
            functions,
            [],
            entryProgram.Main);
    }

    private static string CreateGeneratedProject(string runtimeProject)
    {
        var escapedRuntimeProject = System.Security.SecurityElement.Escape(runtimeProject)
            ?? runtimeProject;
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>YyCompiledApplication</AssemblyName>
                <RootNamespace>YyCompiledApplication</RootNamespace>
                <ApplicationIcon>{System.Security.SecurityElement.Escape(
                    Path.Combine(FindRepositoryRoot()!, "assets", "YyStudio.ico"))}</ApplicationIcon>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{escapedRuntimeProject}" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string? FindRepositoryRoot()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            for (var depth = 0; directory is not null && depth < 10; depth++)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Eyuyan.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
