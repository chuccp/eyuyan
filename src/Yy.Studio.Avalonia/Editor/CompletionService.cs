using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Language.Structured;
using Yy.Sdk;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>项目源码目录包候选项。</summary>
public sealed record YyPackageCompletion(string Name, string Description);

/// <summary>当前调用位置的参数签名提示数据。</summary>
public sealed record YyParameterHint(
    string Name,
    string Signature,
    int ActiveParameter,
    string Description);

/// <summary>补全和参数提示使用的上下文；主窗口可在文档切换时整体替换。</summary>
public sealed class CompletionEnvironment
{
    public YyFormModel? Form { get; set; }

    /// <summary>经典视图当前子程序；纯代码视图留空时按光标位置推断。</summary>
    public YyCodeFunction? CurrentFunction { get; set; }

    /// <summary>整个文档的子程序列表；经典视图正文只有方法体时必须提供。</summary>
    public IReadOnlyList<YyCodeFunction>? DocumentFunctions { get; set; }

    public IReadOnlyList<YyPackageCompletion> Packages { get; set; } = [];

    public string? FilePath { get; set; }
}

/// <summary>
/// 上下文补全：内置命令、关键字、用户子程序、窗体控件/属性、
/// 当前参数和局部变量；同时提供参数签名提示数据。
/// </summary>
public sealed class CompletionService
{
    private readonly TextEditor _editor;
    private CompletionWindow? _window;
    private bool _attached;

    public CompletionService(TextEditor editor)
    {
        _editor = editor;
    }

    public CompletionEnvironment Environment { get; set; } = new();

    public bool Enabled { get; set; } = true;

    public CompletionWindow? Window => _window;

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _editor.TextArea.TextEntered += OnTextEntered;
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _editor.TextArea.TextEntered -= OnTextEntered;
        _window?.Close();
        _window = null;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_window is not null ||
            !Enabled ||
            _editor.IsReadOnly ||
            string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (char.IsLetter(character) || character == '_' || character == '.')
        {
            ShowCompletion();
        }
    }

    public void ShowCompletion(YyCodeFunction? currentFunction = null)
    {
        if (_window is not null || !Enabled || _editor.IsReadOnly)
        {
            return;
        }

        var items = BuildItems(_editor.Document.Text, _editor.CaretOffset, currentFunction);
        if (items.Count == 0)
        {
            return;
        }

        var window = new CompletionWindow(_editor.TextArea)
        {
            Width = 335,
            MaxHeight = 330
        };
        foreach (var item in items)
        {
            window.CompletionList.CompletionData.Add(item);
        }

        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        window.Show();
    }

    /// <summary>生成补全项；公开用于无界面回归测试和宿主自定义过滤。</summary>
    public IReadOnlyList<YyCompletionData> BuildItems(
        string text,
        int caretOffset,
        YyCodeFunction? currentFunction = null)
    {
        var items = new List<YyCompletionData>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var environment = Environment;
        var functions = GetDocumentFunctions(text);
        var function = currentFunction
            ?? environment.CurrentFunction
            ?? FindEnclosingFunction(text, caretOffset, functions);

        if (IsSourceImportContext(text, caretOffset))
        {
            foreach (var package in environment.Packages)
            {
                if (known.Add(package.Name))
                {
                    items.Add(new YyCompletionData(
                        package.Name,
                        package.Description,
                        CompletionKind.Package));
                }
            }

            if (items.Count > 0)
            {
                return items;
            }
        }

        if (IsPackageImportContext(text, caretOffset))
        {
            foreach (var package in YySdk.Packages.Where(item => item.IsImportable))
            {
                items.Add(new YyCompletionData(
                    package.Name,
                    $"系统 SDK 包 · {package.StatusText} · {package.Description}",
                    CompletionKind.Package));
            }

            return items;
        }

        var form = environment.Form;
        var memberOwner = TryGetMemberOwnerName(text, caretOffset);
        if (form is not null && memberOwner is not null)
        {
            AddMemberCompletions(items, known, form, memberOwner);
            return items;
        }

        if (function is not null)
        {
            foreach (var parameter in function.Parameters)
            {
                if (known.Add(parameter.Name))
                {
                    items.Add(new YyCompletionData(
                        parameter.Name,
                        $"参数 · {DescribeType(parameter.TypeName)}",
                        CompletionKind.Parameter));
                }
            }

            foreach (var local in function.Statements
                         .TakeWhile(statement =>
                             statement.Kind == YyCodeStatementKinds.Variable))
            {
                if (known.Add(local.Name))
                {
                    items.Add(new YyCompletionData(
                        local.Name,
                        $"局部变量 · {DescribeType(local.TypeName)}",
                        CompletionKind.Variable));
                }
            }
        }

        if (form is not null)
        {
            foreach (var alias in new[] { form.Name, "窗口" })
            {
                if (known.Add(alias))
                {
                    items.Add(new YyCompletionData(
                        alias,
                        $"窗口上下文 · {form.Title}",
                        CompletionKind.Component));
                }
            }

            foreach (var control in form.Controls)
            {
                if (known.Add(control.Name))
                {
                    items.Add(new YyCompletionData(
                        control.Name,
                        $"组件 · {control.Type}",
                        CompletionKind.Component));
                }
            }
        }

        foreach (var userFunction in functions)
        {
            if (known.Add(userFunction.Name))
            {
                items.Add(new YyCompletionData(
                    userFunction.Name,
                    $"子程序 · {FormatSignature(userFunction)}",
                    CompletionKind.Snippet));
            }
        }

        foreach (var (itemText, description) in CompletionCatalog.Completions)
        {
            if (!known.Add(itemText))
            {
                continue;
            }

            if (CompletionCatalog.Snippets.TryGetValue(itemText, out var snippet))
            {
                items.Add(new YyCompletionData(
                    itemText,
                    description + " · 插入代码块",
                    CompletionKind.Snippet,
                    snippet.InsertText,
                    snippet.CaretOffset,
                    snippet.SelectionLength));
            }
            else
            {
                items.Add(new YyCompletionData(
                    itemText,
                    description,
                    CompletionCatalog.IsBuiltin(itemText)
                        ? CompletionKind.Builtin
                        : CompletionKind.Keyword));
            }
        }

        return items;
    }

    /// <summary>返回内置命令或当前文件子程序的签名帮助；未找到时返回 null。</summary>
    public string? DescribeCallable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var callable = FindCallable(
            _editor.Document.Text,
            name,
            Environment.CurrentFunction);
        return callable is { } value
            ? value.Signature + "\n\n" + value.Description
            : null;
    }

    /// <summary>按当前光标位置计算参数提示；没有活动调用时返回 null。</summary>
    public YyParameterHint? GetParameterHint() =>
        GetParameterHint(_editor.Document.Text, _editor.CaretOffset);

    public YyParameterHint? GetParameterHint(
        string text,
        int caretOffset,
        YyCodeFunction? currentFunction = null)
    {
        var caret = Math.Clamp(caretOffset, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var prefix = text[lineStart..caret];
        var openIndex = FindActiveOpenParenthesis(prefix);
        if (openIndex < 0)
        {
            return null;
        }

        var nameEnd = openIndex - 1;
        while (nameEnd >= 0 && char.IsWhiteSpace(prefix[nameEnd]))
        {
            nameEnd--;
        }

        var nameStart = nameEnd;
        while (nameStart >= 0 &&
               (char.IsLetterOrDigit(prefix[nameStart]) || prefix[nameStart] == '_'))
        {
            nameStart--;
        }

        if (nameEnd <= nameStart)
        {
            return null;
        }

        var name = prefix[(nameStart + 1)..(nameEnd + 1)];
        var callable = FindCallable(text, name, currentFunction);
        if (callable is null)
        {
            return null;
        }

        var argumentIndex = CountTopLevelArguments(prefix[(openIndex + 1)..]) + 1;
        return new YyParameterHint(
            name,
            callable.Value.Signature,
            argumentIndex,
            callable.Value.Description);
    }

    /// <summary>扫描工程源码目录，生成“项目包名.子目录”候选，与 WPF Studio 规则一致。</summary>
    public static IReadOnlyList<YyPackageCompletion> DiscoverProjectPackages(
        string sourceRoot,
        string packageName)
    {
        var results = new List<YyPackageCompletion>();
        if (!Directory.Exists(sourceRoot))
        {
            return results;
        }

        try
        {
            foreach (var directory in Directory
                         .EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(sourceRoot, directory)
                    .Replace('\\', '/');
                if (relative.Split('/').Contains("Forms", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasSource = Directory
                    .EnumerateFiles(directory, "*.yyc", SearchOption.TopDirectoryOnly)
                    .Any(path =>
                        !Path.GetFileName(path).EndsWith(
                            ".Designer.yyc",
                            StringComparison.OrdinalIgnoreCase));
                if (!hasSource)
                {
                    continue;
                }

                var package = packageName +
                    "." +
                    string.Join(".", relative.Split('/'));
                results.Add(new YyPackageCompletion(
                    package,
                    $"源码目录包 · {YyProject.SourceDirectoryName}/{relative}"));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 工程目录不可读时保留已有候选，不影响输入。
        }

        return results;
    }

    private IReadOnlyList<YyCodeFunction> GetDocumentFunctions(string text)
    {
        if (Environment.DocumentFunctions is { Count: > 0 } provided)
        {
            return provided;
        }

        try
        {
            return YyCodeDocumentConverter
                .FromSource(text, Environment.FilePath)
                .Functions;
        }
        catch (Exception exception) when (
            exception is YyDiagnostic or YyCodeFormatException or ArgumentException)
        {
            return [];
        }
    }

    private (string Signature, string Description)? FindCallable(
        string text,
        string name,
        YyCodeFunction? currentFunction)
    {
        var builtin = CompletionCatalog.FindBuiltin(name);
        if (builtin is not null)
        {
            return (builtin.Signature, builtin.Description);
        }

        var function = GetDocumentFunctions(text)
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal))
            ?? (string.Equals(currentFunction?.Name, name, StringComparison.Ordinal)
                ? currentFunction
                : null);
        return function is null
            ? null
            : (FormatSignature(function), "当前文件定义的子程序。");
    }

    private static void AddMemberCompletions(
        ICollection<YyCompletionData> items,
        HashSet<string> known,
        YyFormModel form,
        string owner)
    {
        if (string.Equals(owner, form.Name, StringComparison.Ordinal) ||
            string.Equals(owner, "窗口", StringComparison.Ordinal))
        {
            var formProperties = new (string Name, string Description)[]
            {
                ("标题", "窗体标题"),
                ("宽", "窗体宽度"),
                ("高", "窗体高度"),
                ("背景", "窗体背景色"),
                ("可见", "是否显示窗体")
            };
            foreach (var (name, description) in formProperties)
            {
                if (known.Add(name))
                {
                    items.Add(new YyCompletionData(
                        name,
                        description + " · " + form.Name,
                        CompletionKind.Property));
                }
            }

            return;
        }

        var control = form.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, owner, StringComparison.Ordinal));
        var properties = new (string Name, string Description)[]
        {
            ("文本", "控件的文本、标题或内容"),
            ("名称", "控件名称"),
            ("可见", "是否显示控件"),
            ("启用", "是否允许用户操作"),
            ("选中", "复选框是否选中"),
            ("左", "控件的左侧坐标"),
            ("上", "控件的顶部坐标"),
            ("宽", "控件宽度"),
            ("高", "控件高度")
        };
        foreach (var (name, description) in properties)
        {
            if (known.Add(name))
            {
                items.Add(new YyCompletionData(
                    name,
                    control is null ? description : $"{description} · {control.Name}",
                    CompletionKind.Property));
            }
        }
    }

    private static string FormatSignature(YyCodeFunction function)
    {
        var parameters = string.Join(
            ", ",
            function.Parameters.Select(parameter =>
            {
                var text = parameter.Name;
                if (!string.IsNullOrWhiteSpace(parameter.TypeName))
                {
                    text += ": " + parameter.TypeName;
                }

                if (!string.IsNullOrWhiteSpace(parameter.DefaultExpression))
                {
                    text += " = " + parameter.DefaultExpression;
                }

                return text;
            }));
        var returnType = string.IsNullOrWhiteSpace(function.ReturnType)
            ? string.Empty
            : ": " + function.ReturnType;
        return $"{function.Name}({parameters}){returnType}";
    }

    private static string DescribeType(string? typeName) =>
        string.IsNullOrWhiteSpace(typeName) ? "任意" : typeName;

    private static YyCodeFunction? FindEnclosingFunction(
        string text,
        int caretOffset,
        IReadOnlyList<YyCodeFunction> functions)
    {
        if (functions.Count == 0)
        {
            return null;
        }

        var caret = Math.Clamp(caretOffset, 0, text.Length);
        var lines = text[..caret].Replace("\r\n", "\n").Split('\n');
        string? current = null;
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("结束子程序", StringComparison.Ordinal) ||
                trimmed.StartsWith("结束函数", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            if (trimmed.StartsWith("子程序 ", StringComparison.Ordinal) ||
                trimmed.StartsWith("函数 ", StringComparison.Ordinal))
            {
                var rest = trimmed[(trimmed.IndexOf(' ') + 1)..];
                var end = rest.IndexOf('(');
                current = (end >= 0 ? rest[..end] : rest).Trim();
            }
        }

        return current is null
            ? null
            : functions.FirstOrDefault(item =>
                string.Equals(item.Name, current, StringComparison.Ordinal));
    }

    private static bool IsPackageImportContext(string text, int caretOffset) =>
        IsQuotedDirectiveContext(text, caretOffset, "导入包");

    private static bool IsSourceImportContext(string text, int caretOffset) =>
        IsQuotedDirectiveContext(text, caretOffset, "引入");

    private static bool IsQuotedDirectiveContext(
        string text,
        int caretOffset,
        string directive)
    {
        var caret = Math.Clamp(caretOffset, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var prefix = text[lineStart..caret];
        var trimmed = prefix.TrimStart();
        if (trimmed.Length <= directive.Length ||
            !trimmed.StartsWith(directive, StringComparison.Ordinal) ||
            !char.IsWhiteSpace(trimmed[directive.Length]))
        {
            return false;
        }

        return prefix.Count(character => character == '"') == 1 &&
               text.AsSpan(caret, lineEnd - caret).IndexOf('"') < 0;
    }

    /// <summary>取得光标处“对象.成员”中的对象名；不在成员访问上下文时返回 null。</summary>
    public static string? TryGetMemberOwnerName(string text, int caretOffset)
    {
        var caret = Math.Clamp(caretOffset, 0, text.Length);
        if (caret <= 0)
        {
            return null;
        }

        var cursor = caret - 1;
        while (cursor >= 0 && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
        {
            cursor--;
        }

        while (cursor >= 0 && char.IsWhiteSpace(text[cursor]))
        {
            cursor--;
        }

        if (cursor < 0 || text[cursor] != '.')
        {
            return null;
        }

        var end = cursor - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
        {
            start--;
        }

        return end > start ? text[(start + 1)..(end + 1)] : null;
    }

    private static int FindActiveOpenParenthesis(string prefix)
    {
        var depth = 0;
        for (var index = prefix.Length - 1; index >= 0; index--)
        {
            if (prefix[index] == ')')
            {
                depth++;
            }
            else if (prefix[index] == '(')
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    private static int CountTopLevelArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var depth = 0;
        var commas = 0;
        foreach (var character in text)
        {
            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
    }
}
