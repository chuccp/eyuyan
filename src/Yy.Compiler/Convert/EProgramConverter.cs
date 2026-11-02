using System.Text;
using System.Text.RegularExpressions;
using Yy.Language;
using Yy.Language.Syntax;

namespace Yy.Compiler.Convert;

/// <summary>老程序(.e)转换结果。</summary>
public sealed record EConversionResult(
    string ProjectDirectory,
    string ProjectFile,
    string SourceFile,
    string ReportFile,
    IReadOnlyList<string> RecoveredProcedures,
    int RecoveredStatements,
    int UnmappedStrings);

/// <summary>
/// 老易语言程序(.e,CNWTEPRG)转换器。
/// 按帧类型码解译记录流(子程序/程序集/变量行/文本串),
/// 还原为老语法 .yyc 源码;语句行按文本特征归类,解析兜底保证产物可编译。
/// </summary>
public static class EProgramConverter
{
    private static readonly string[] BlockOpeners =
    [
        "如果真", "如果", "判断开始", "计次循环首", "变量循环首", "判断循环首",
        "循环判断首", "遍历循环首", "尝试"
    ];

    private static readonly string[] BlockClosers =
    [
        "如果真结束", "如果结束", "结束如果", "判断结束", "结束判断",
        "计次循环尾", "变量循环尾", "判断循环尾", "遍历循环尾", "循环判断尾",
        "结束循环", "循环结束", "结束遍历", "遍历结束", "结束尝试", "尝试结束"
    ];

    private static readonly string[] StatementKeywords =
    [
        "如果真", "判断开始", "判断", "默认", "否则", "否则如果", "如果",
        "计次循环首", "变量循环首", "判断循环首", "循环判断首", "遍历循环首",
        "返回", "跳出循环", "继续循环", "跳出", "继续", "尝试", "捕获",
        "变量", "常量", "定义"
    ];

    private static readonly Regex SupportLibraryRegex =
        new(@"[一-鿿A-Za-z]{2,14}支持库", RegexOptions.Compiled);

    private static readonly Regex CallLikeRegex =
        new(@"^[A-Za-z一-鿿_][A-Za-z0-9一-鿿_.]*\s*[（(].*[）)]\s*$",
            RegexOptions.Compiled);

    /// <summary>转换 .e 到易语工程目录;目标目录已存在工程时覆盖入口源码。</summary>
    public static EConversionResult Convert(
        string eFilePath,
        string targetDirectory,
        string? projectName = null,
        string? packageName = null)
    {
        var fullPath = Path.GetFullPath(eFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要转换的 .e 程序。", fullPath);
        }

        var data = File.ReadAllBytes(fullPath);
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual("CNWTEPRG"u8))
        {
            throw new InvalidDataException("不是易语言程序文件(缺少 CNWTEPRG 文件头)。");
        }

        var records = ERecordReader.Read(data);
        var report = new List<string>();
        var source = BuildSource(records, report, out var procedures, out var statements, out var unmapped);

        var root = Path.GetFullPath(targetDirectory);
        var name = string.IsNullOrWhiteSpace(projectName)
            ? SanitizeName(Path.GetFileNameWithoutExtension(fullPath))
            : projectName.Trim();
        var package = string.IsNullOrWhiteSpace(packageName)
            ? "imported." + name.ToLowerInvariant()
            : packageName.Trim();

        var projectFile = Path.Combine(root, name + ".yyproj");
        if (!File.Exists(projectFile))
        {
            YyProject.CreateProject(projectFile, name, package, YyProjectKind.Console);
        }

        var sourceFile = Path.Combine(root, "Sources", "Program.yyc");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, source, new UTF8Encoding(false));

        var reportFile = Path.Combine(root, "导入报告.md");
        File.WriteAllLines(reportFile, report, new UTF8Encoding(false));
        return new EConversionResult(
            root,
            projectFile,
            sourceFile,
            reportFile,
            procedures,
            statements,
            unmapped);
    }

    /// <summary>按记录类型码重建老语法源码。</summary>
    private static string BuildSource(
        IReadOnlyList<ERecord> records,
        List<string> report,
        out List<string> procedures,
        out int statements,
        out int unmapped)
    {
        procedures = [];
        statements = 0;
        unmapped = 0;
        var supportLibraries = new List<string>();
        var builder = new StringBuilder();
        var depth = 0;
        var inProcedure = false;

        foreach (var record in records)
        {
            switch (record.Kind)
            {
                case ERecordKind.Assembly:
                    builder.AppendLine(".程序集 " + record.Text);
                    inProcedure = false;
                    depth = 0;
                    break;

                case ERecordKind.Procedure:
                    if (inProcedure)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(".子程序 ").AppendLine(record.Text);
                    procedures.Add(record.Text);
                    inProcedure = true;
                    depth = 0;
                    break;

                case ERecordKind.Variable:
                    if (!inProcedure)
                    {
                        builder.Append(".程序集变量 ").Append(record.Text)
                            .AppendLine(", 任意");
                    }
                    else
                    {
                        builder.Append(".局部变量 ").Append(record.Text)
                            .AppendLine(", 任意");
                    }

                    break;

                default:
                    var text = record.Text.Trim();
                    if (text.Length == 0)
                    {
                        break;
                    }

                    if (text.Contains("支持库", StringComparison.Ordinal))
                    {
                        foreach (Match match in SupportLibraryRegex.Matches(text))
                        {
                            if (!supportLibraries.Contains(match.Value, StringComparer.Ordinal))
                            {
                                supportLibraries.Add(match.Value);
                            }
                        }

                        break;
                    }

                    foreach (var piece in SplitStatements(text))
                    {
                        if (IsCodeLine(piece))
                        {
                            if (!inProcedure)
                            {
                                builder.AppendLine(".子程序 _导入片段");
                                inProcedure = true;
                                depth = 0;
                            }

                            var trimmed = piece.Trim().Trim('　');
                            if (BlockClosers.Any(word => trimmed.StartsWith(word, StringComparison.Ordinal)))
                            {
                                depth = Math.Max(0, depth - 1);
                            }

                            builder.Append(new string(' ', 4 + depth * 4)).AppendLine(trimmed);
                            if (BlockOpeners.Any(word => trimmed.StartsWith(word, StringComparison.Ordinal)))
                            {
                                depth++;
                            }

                            statements++;
                        }
                        else
                        {
                            if (inProcedure)
                            {
                                builder.Append("    ' ").AppendLine(piece);
                            }

                            report.Add("- " + piece);
                            unmapped++;
                        }
                    }

                    break;
            }
        }

        var header = new StringBuilder();
        header.AppendLine(".版本 2");
        foreach (var library in supportLibraries)
        {
            header.Append(".支持库 ").AppendLine(library);
        }

        header.AppendLine();
        report.Insert(0, "# 老程序导入报告");
        report.Insert(1, string.Empty);
        report.Insert(2, $"- 还原子程序:{procedures.Count} 个");
        report.Insert(3, $"- 还原语句:{statements} 行");
        report.Insert(4, $"- 未归类文本:{unmapped} 条");
        report.Insert(5, string.Empty);
        report.Insert(6, "## 未归类文本(标题、提示、备注等,供人工核对)");
        report.Insert(7, string.Empty);
        return EnsureParsable(header + builder.ToString().TrimEnd() + Environment.NewLine);
    }

    /// <summary>文本帧内的语句行判定:只认带代码标记的行,散文归备注。</summary>
    private static bool IsCodeLine(string text)
    {
        if (text.StartsWith(".", StringComparison.Ordinal) && text.Length > 1)
        {
            return true;
        }

        if (BlockClosers.Any(word => text.StartsWith(word, StringComparison.Ordinal)))
        {
            return true;
        }

        var prose = text.Contains('。') || text.Contains('、') || text.Contains('|') ||
                    text.Contains('｜') || text.Contains("(&") || text.Contains("（&");
        if (prose)
        {
            return false;
        }

        var hasCodeMarker = text.Contains('（') || text.Contains('(') ||
            text.Contains('＝') || text.Contains(" = ");
        if (hasCodeMarker &&
            StatementKeywords.Any(word => text.StartsWith(word, StringComparison.Ordinal)))
        {
            return true;
        }

        return CallLikeRegex.IsMatch(text);
    }

    /// <summary>把可能粘连的文本按语句起始词切开。</summary>
    private static IEnumerable<string> SplitStatements(string text)
    {
        if (!text.Contains(')') && !text.Contains('）'))
        {
            yield return text;
            yield break;
        }

        var pieces = new List<string>();
        var start = 0;
        for (var index = 1; index < text.Length; index++)
        {
            if (text[index - 1] is not (')' or '）'))
            {
                continue;
            }

            var rest = text[index..].TrimStart();
            if (StatementKeywords.Any(word => rest.StartsWith(word, StringComparison.Ordinal)) ||
                rest.StartsWith(".", StringComparison.Ordinal))
            {
                pieces.Add(text[start..index].Trim());
                start = index;
            }
        }

        pieces.Add(text[start..].Trim());
        foreach (var piece in pieces.Where(piece => piece.Length > 0))
        {
            yield return piece;
        }
    }

    /// <summary>保证转换产物可被解析:无法解析的行降级为备注。</summary>
    private static string EnsureParsable(string source)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                new Parser(source).Parse();
                return source;
            }
            catch (YyDiagnostic diagnostic)
            {
                var lines = source.Replace("\r\n", "\n").Split('\n');
                var index = Math.Clamp(diagnostic.Line - 1, 0, lines.Length - 1);
                if (lines[index].TrimStart().StartsWith("'"))
                {
                    return source;
                }

                lines[index] = "    '未能解析: " + lines[index].Trim();
                source = string.Join(Environment.NewLine, lines);
            }
        }

        return source;
    }

    private static string SanitizeName(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? "导入程序" : builder.ToString();
    }
}
