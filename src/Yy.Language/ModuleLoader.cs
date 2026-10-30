using System.Text;
using Yy.Language.Structured;
using Yy.Language.Syntax;

namespace Yy.Language;

internal static class ModuleLoader
{
    public static YyProgram CompileFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到易语源码文件。", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("易语源码必须使用 .yyc 结构化代码格式。");
        }

        var source = ReadSource(
            fullPath,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        return CompileSource(source, fullPath);
    }

    public static YyProgram CompileSource(string source, string? sourcePath)
    {
        var rootPath = string.IsNullOrWhiteSpace(sourcePath)
            ? null
            : Path.GetFullPath(sourcePath);
        var rootProgram = new Parser(source, rootPath).Parse();
        if (rootProgram.Imports.Count > 0 && rootPath is null)
        {
            var first = rootProgram.Imports[0];
            throw new YyDiagnostic(
                "未保存的源码无法解析“引入”路径",
                first.Line,
                first.Column,
                "请先保存当前文件，或从已有文件运行项目。");
        }

        var functions = new List<FunctionDeclaration>();
        var functionsBySource = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
        var loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsed = new Dictionary<string, YyProgram>(StringComparer.OrdinalIgnoreCase);
        var packageImports = new List<PackageImportDeclaration>();
        var packageNames = new HashSet<string>(StringComparer.Ordinal);
        IncludePackages(rootProgram);

        if (rootPath is not null)
        {
            loading.Add(rootPath);
            parsed[rootPath] = rootProgram;
            IncludeImports(rootProgram, rootPath);
            loaded.Add(rootPath);
            loading.Remove(rootPath);
        }

        AddFunctions(rootProgram.Functions, rootPath);

        return new YyProgram(
            rootProgram.Line,
            rootProgram.Column,
            rootProgram.Metadata,
            functions,
            [],
            rootProgram.Main)
        {
            PackageImports = packageImports
        };

        void IncludeImports(YyProgram program, string modulePath)
        {
            IncludePackages(program);
            var baseDirectory = Path.GetDirectoryName(modulePath)
                ?? throw new YyDiagnostic("无法确定模块所在目录", 1, 1, sourceName: modulePath);

            foreach (var import in program.Imports)
            {
                var rawPath = import.Path.Trim();
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    throw new YyDiagnostic(
                        "模块路径不能为空",
                        import.Line,
                        import.Column,
                        sourceName: modulePath);
                }

                var importPath = Path.IsPathRooted(rawPath)
                    ? rawPath
                    : Path.Combine(baseDirectory, rawPath);
                if (string.IsNullOrWhiteSpace(Path.GetExtension(importPath)))
                {
                    importPath += ".yyc";
                }

                importPath = Path.GetFullPath(importPath);
                if (!string.Equals(Path.GetExtension(importPath), ".yyc", StringComparison.OrdinalIgnoreCase))
                {
                    throw new YyDiagnostic(
                        "模块必须使用 .yyc 结构化代码格式",
                        import.Line,
                        import.Column,
                        $"旧版源码格式已不再支持：{importPath}",
                        modulePath);
                }

                if (!File.Exists(importPath))
                {
                    throw new YyDiagnostic(
                        $"找不到模块“{rawPath}”",
                        import.Line,
                        import.Column,
                        $"解析后的路径：{importPath}",
                        modulePath);
                }

                if (loaded.Contains(importPath))
                {
                    continue;
                }

                if (loading.Contains(importPath))
                {
                    throw new YyDiagnostic(
                        $"检测到循环模块引用：{Path.GetFileName(importPath)}",
                        import.Line,
                        import.Column,
                        "请移除文件之间的循环“引入”。",
                        modulePath);
                }

                if (!parsed.TryGetValue(importPath, out var imported))
                {
                    string importedSource;
                    try
                    {
                        importedSource = ReadSource(importPath, new UTF8Encoding(false, true));
                    }
                    catch (DecoderFallbackException)
                    {
                        throw new YyDiagnostic(
                            "模块不是有效的 UTF-8 文本",
                            import.Line,
                            import.Column,
                            "请先将模块转换为 UTF-8。",
                            modulePath);
                    }

                    imported = new Parser(importedSource, importPath).Parse();
                    parsed[importPath] = imported;
                }

                loading.Add(importPath);
                IncludeImports(imported, importPath);
                loaded.Add(importPath);
                loading.Remove(importPath);
                AddFunctions(imported.Functions, importPath);
            }
        }

        void IncludePackages(YyProgram program)
        {
            foreach (var package in program.PackageImports)
            {
                if (packageNames.Add(package.Name))
                {
                    packageImports.Add(package);
                }
            }
        }

        void AddFunctions(
            IReadOnlyList<FunctionDeclaration> moduleFunctions,
            string? modulePath)
        {
            foreach (var function in moduleFunctions)
            {
                if (functionsBySource.TryGetValue(function.Name, out var existing))
                {
                    var existingSource = string.IsNullOrWhiteSpace(existing.SourceName)
                        ? "当前源码"
                        : Path.GetFileName(existing.SourceName);
                    var currentSource = string.IsNullOrWhiteSpace(modulePath)
                        ? "当前源码"
                        : Path.GetFileName(modulePath);
                    throw new YyDiagnostic(
                        $"子程序“{function.Name}”重复定义",
                        function.Line,
                        function.Column,
                        $"已定义于 {existingSource}（第 {existing.Line} 行），当前文件为 {currentSource}。",
                        modulePath);
                }

                functionsBySource[function.Name] = function;
                functions.Add(function);
            }
        }
    }

    private static string ReadSource(string path, Encoding encoding)
    {
        var content = File.ReadAllText(path, encoding);
        return YyCodeDocumentConverter.ToSource(YyCodeSerializer.Deserialize(content));
    }
}
