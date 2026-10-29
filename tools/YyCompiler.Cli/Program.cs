using System.IO;
using Yy.Studio;

if (args.Length < 2)
{
    Console.WriteLine("用法：YyCompiler <项目.yyproj> <输出.exe>");
    return 2;
}

var projectPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!File.Exists(projectPath))
{
    Console.Error.WriteLine($"项目不存在：{projectPath}");
    return 2;
}

var project = YyProject.LoadForDirectory(Path.GetDirectoryName(projectPath)!);
if (project is null)
{
    Console.Error.WriteLine("目录中没有可读取的 .yyproj 项目清单。");
    return 2;
}

try
{
    var compiler = new ProjectCompiler();
    await compiler.CompileAsync(
        project,
        outputPath,
        new Progress<string>(message => Console.WriteLine(message)));
    Console.WriteLine($"已生成：{outputPath}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
