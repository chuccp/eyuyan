using System.IO;
using System.Text;
using Yy.Language;
using Yy.Language.Syntax;

namespace Yy.Compiler;

/// <summary>项目源码、窗体和事件的语义汇总。实际 EXE 由 Yy.Native 生成。</summary>
public sealed class ProjectCompiler
{
    public void Validate(YyProject project) => _ = LoadProgram(project);

    public static YyProgram LoadProgram(YyProject project)
    {
        var entryProgram = ReadProgram(project.EntryPath);
        var functions = entryProgram.Functions.ToList();
        var names = functions
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
        var packageNames = entryProgram.PackageImports
            .Select(package => package.Name)
            .ToHashSet(StringComparer.Ordinal);
        var packageImports = entryProgram.PackageImports.ToList();

        foreach (var form in project.Forms)
        {
            if (form.CodePath is null)
            {
                continue;
            }

            var codeProgram = ReadProgram(form.CodePath);
            foreach (var package in codeProgram.PackageImports)
            {
                if (packageNames.Add(package.Name))
                {
                    packageImports.Add(package);
                }
            }

            foreach (var function in codeProgram.Functions)
            {
                if (!names.Add(function.Name))
                {
                    throw new InvalidDataException(
                        $"子程序“{function.Name}”在项目源码与窗体用户代码中重复定义。");
                }

                functions.Add(function);
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
                foreach (var eventName in new[]
                         {
                             control.ClickEvent,
                             control.TextChangedEvent,
                             control.SelectionChangedEvent,
                             control.CheckedChangedEvent
                         })
                {
                    if (!string.IsNullOrWhiteSpace(eventName) && !names.Contains(eventName))
                    {
                        throw new InvalidDataException(
                            $"窗体“{form.Model.Title}”的控件“{control.Name}”绑定了不存在的事件“{eventName}”。");
                    }
                }
            }
        }

        return new YyProgram(
            entryProgram.Line,
            entryProgram.Column,
            entryProgram.Metadata,
            functions,
            [],
            entryProgram.Main)
        {
            PackageImports = packageImports,
            Structs = entryProgram.Structs,
            Interfaces = entryProgram.Interfaces
        };
    }

    private static YyProgram ReadProgram(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"项目源码必须使用 .yyc 格式：{path}");
        }

        var content = File.ReadAllText(path, new UTF8Encoding(false, true));
        return YyRunner.CompileSource(content, path);
    }
}
