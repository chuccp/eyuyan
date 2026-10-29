using System.IO;
using Yy.Studio;
using Yy.Language.Structured;

if (args.Length >= 3 &&
    string.Equals(args[0], "--convert", StringComparison.OrdinalIgnoreCase))
{
    var input = Path.GetFullPath(args[1]);
    var output = Path.GetFullPath(args[2]);
    var source = await File.ReadAllTextAsync(input);
    var document = YyCodeDocumentConverter.FromSource(source, input);
    await File.WriteAllTextAsync(output, YyCodeSerializer.Serialize(document));
    Console.WriteLine($"已转换：{output}");
    return 0;
}

if (args.Length < 2)
{
    Console.WriteLine("用法：YyCompiler <项目.yyproj> <输出.exe>");
    Console.WriteLine("      YyCompiler --convert <输入.ey> <输出.yyc>");
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
