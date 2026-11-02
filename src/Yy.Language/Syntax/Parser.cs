namespace Yy.Language.Syntax;

/// <summary>
/// 易语解析器:老语法(官方语汇,点前缀声明行/语句与收口行)是基础,
/// 新语汇(缩进块、如果真/判断开始、类型化声明)是其超集。
/// </summary>
public sealed partial class Parser
{
    private readonly string _source;
    private readonly string? _sourceName;
    private readonly List<Token> _tokens;
    private readonly List<ImportDeclaration> _imports = [];
    private readonly List<PackageImportDeclaration> _packageImports = [];
    private readonly List<StructDeclaration> _structs = [];
    private readonly List<InterfaceDeclaration> _interfaces = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private int _current;

    /// <summary>语句关键字(老语法可带“.”前缀)。</summary>
    private static readonly HashSet<string> StatementKeywords = new(StringComparer.Ordinal)
    {
        "变量", "定义", "常量", "如果真", "如果", "判断开始", "判断", "默认", "否则", "否则如果",
        "当", "计次循环", "计次循环首", "计次循环尾", "变量循环首", "变量循环尾",
        "判断循环首", "判断循环尾", "循环判断首", "循环判断尾",
        "遍历", "遍历循环首", "遍历循环尾",
        "返回", "跳出", "继续", "跳出循环", "继续循环", "尝试", "捕获",
        "如果真结束", "如果结束", "结束如果", "判断结束", "结束判断",
        "结束循环", "循环结束", "结束遍历", "遍历结束", "结束尝试", "尝试结束",
        "结束子程序", "子程序结束", "结束函数", "函数结束"
    };

    /// <summary>顶层声明行(老语法以“.”开头)。</summary>
    private static readonly HashSet<string> DeclarationRows = new(StringComparer.Ordinal)
    {
        "子程序", "函数", "程序集", "程序集变量", "支持库", "版本", "常量", "引入", "导入包"
    };

    public Parser(string source, string? sourceName = null)
    {
        _source = source ?? string.Empty;
        _sourceName = sourceName;
        try
        {
            _tokens = new Lexer(_source).Tokenize().ToList();
        }
        catch (YyDiagnostic diagnostic) when (diagnostic.SourceName is null && sourceName is not null)
        {
            throw new YyDiagnostic(
                diagnostic.Message,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Hint,
                sourceName);
        }
    }

    public YyProgram Parse()
    {
        var functions = new List<FunctionDeclaration>();
        var mainStatements = new List<Statement>();
        SkipNewLines();

        while (!IsAtEnd)
        {
            if (Check(TokenKind.Punctuation, ".") && CheckAhead(1, TokenKind.Identifier))
            {
                var key = Peek(1).Text;
                if (key is "子程序" or "函数")
                {
                    functions.Add(ParseFunction(rowForm: true));
                    continue;
                }

                if (key is "程序集变量" or "常量")
                {
                    mainStatements.Add(ParseDataRow());
                    continue;
                }

                if (StatementKeywords.Contains(key))
                {
                    mainStatements.Add(ParseStatement());
                    continue;
                }

                ParseMetadata();
                continue;
            }

            if (CheckWord("子程序") || CheckWord("函数"))
            {
                functions.Add(ParseFunction(rowForm: false));
                continue;
            }

            if (CheckWord("引入"))
            {
                _imports.Add(ParseImport());
                continue;
            }

            if (CheckWord("导入包"))
            {
                _packageImports.Add(ParsePackageImport());
                continue;
            }

            if (CheckWord("结构体") || CheckWord("记录"))
            {
                _structs.Add(ParseStruct());
                continue;
            }

            if (CheckWord("接口"))
            {
                _interfaces.Add(ParseInterface());
                continue;
            }

            mainStatements.Add(ParseStatement());
        }

        return new YyProgram(
            1,
            1,
            _metadata,
            functions,
            _imports,
            new BlockStatement(1, 1, mainStatements))
        {
            PackageImports = _packageImports,
            Structs = _structs,
            Interfaces = _interfaces
        };
    }

    private void ParseMetadata()
    {
        if (Check(TokenKind.Punctuation, "."))
        {
            Advance();
            if (!Check(TokenKind.Identifier))
            {
                throw Error(Peek(), "点号后需要指令名称。");
            }

            var key = Advance().Text;
            if (string.Equals(key, "版本", StringComparison.Ordinal))
            {
                // 老语法文件头“.版本 2”:登记版本号,不再视为错误。
                var versionValues = new List<string>();
                while (!AtLineEnd)
                {
                    versionValues.Add(Advance().Text);
                }

                _metadata["版本"] = string.Join(" ", versionValues);
                ConsumeLineEnd();
                return;
            }

            var values = new List<string>();
            while (!AtLineEnd)
            {
                values.Add(Advance().Text);
            }

            _metadata[key] = string.Join(" ", values);
            ConsumeLineEnd();
            return;
        }

        throw Error(Peek(), "无法识别此指令。");
    }

    private ImportDeclaration ParseImport()
    {
        var keyword = Advance();
        if (!Check(TokenKind.String))
        {
            throw Error(Peek(), "“引入”后需要模块文件路径字符串。");
        }

        var path = Advance();
        if (!AtLineEnd)
        {
            throw Error(Peek(), "“引入”语句末尾存在多余内容。");
        }

        var import = new ImportDeclaration(keyword.Line, keyword.Column, path.Text);
        ConsumeLineEnd();
        return import;
    }

    private PackageImportDeclaration ParsePackageImport()
    {
        var keyword = Advance();
        if (!Check(TokenKind.String))
        {
            throw Error(Peek(), "“导入包”后需要包名字符串。");
        }

        var name = Advance();
        if (!AtLineEnd)
        {
            throw Error(Peek(), "“导入包”语句末尾存在多余内容。");
        }

        var package = Yy.Sdk.YySdk.FindPackage(name.Text);
        if (package is null)
        {
            throw new YyDiagnostic(
                $"找不到系统包“{name.Text}”。",
                name.Line,
                name.Column,
                "可用包：" + string.Join(
                    "、",
                    Yy.Sdk.YySdk.Packages
                        .Where(item => item.IsImportable)
                        .Select(item => item.Name)),
                _sourceName);
        }

        if (!package.IsImportable)
        {
            throw new YyDiagnostic(
                $"系统包“{package.Name}”已登记，但尚未实现。",
                name.Line,
                name.Column,
                "该包已纳入 SDK 路线图，实现完成前不能导入。",
                _sourceName);
        }

        var import = new PackageImportDeclaration(keyword.Line, keyword.Column, name.Text);
        ConsumeLineEnd();
        return import;
    }

    /// <summary>
    /// 语句块:块体必须比块关键字缩进一级,缩进回退即块结束;
    /// 老语法的收口行(如“.如果真结束”)作为终止符在出现时消费。
    /// </summary>
    private BlockStatement ParseBlock(string[] terminators, int openerIndent = -1)
    {
        var start = Peek();
        var statements = new List<Statement>();
        SkipNewLines();
        var baseIndent = -1;

        while (!IsAtEnd && !IsAnyWord(terminators))
        {
            var indent = Peek().Column - 1;
            if (baseIndent < 0)
            {
                if (openerIndent >= 0 && indent <= openerIndent)
                {
                    throw Error(Peek(), "块体需要缩进。");
                }

                baseIndent = indent;
            }
            else if (indent < baseIndent)
            {
                break;
            }

            statements.Add(ParseStatement());
        }

        return new BlockStatement(start.Line, start.Column, statements);
    }

    // ---- 记号工具 ----

    private void ConsumeEnd(string construct, params string[] words)
    {
        // 缩进已能判定块结束;收口行仅在出现时消费(老语法)。
        if (!IsAnyWord(words))
        {
            return;
        }

        if (Check(TokenKind.Punctuation, "."))
        {
            Advance();
        }

        ConsumeLine();
    }

    private void ConsumeLine()
    {
        while (!AtLineEnd && !IsAtEnd)
        {
            Advance();
        }

        ConsumeLineEnd();
    }

    private void ConsumeLineEnd()
    {
        if (IsAtEnd)
        {
            return;
        }

        if (!Check(TokenKind.NewLine))
        {
            throw Error(Peek(), $"语句结尾存在多余内容“{Display(Peek())}”。");
        }

        SkipNewLines();
    }

    private void SkipNewLines()
    {
        while (Check(TokenKind.NewLine))
        {
            _current++;
        }
    }

    private Token ConsumeIdentifier(string description)
    {
        if (Check(TokenKind.Identifier))
        {
            return Advance();
        }

        throw Error(Peek(), $"此处需要{description}。");
    }

    private Token Consume(TokenKind kind, string text, string message)
    {
        if (Check(kind, text))
        {
            return Advance();
        }

        throw Error(Peek(), message);
    }

    private bool Match(TokenKind kind, string text)
    {
        if (!Check(kind, text))
        {
            return false;
        }

        Advance();
        return true;
    }

    private bool MatchOperator(string text)
    {
        if (!Check(TokenKind.Operator, text))
        {
            return false;
        }

        Advance();
        return true;
    }

    /// <summary>匹配关键字;老语法的“.”前缀一并消费。</summary>
    private bool MatchWord(string word)
    {
        if (!AtWord(word))
        {
            return false;
        }

        if (Check(TokenKind.Punctuation, "."))
        {
            Advance();
        }

        Advance();
        return true;
    }

    private bool Check(TokenKind kind, string text)
    {
        return Peek().Kind == kind && Peek().Text == text;
    }

    private bool Check(TokenKind kind) => Peek().Kind == kind;

    private bool CheckAhead(int distance, TokenKind kind, string text) =>
        Peek(distance).Kind == kind && Peek(distance).Text == text;

    private bool CheckAhead(int distance, TokenKind kind) =>
        Peek(distance).Kind == kind;

    private bool CheckWord(string word) => Peek().IsWord(word);

    /// <summary>当前位置是关键字(允许“.”前缀)。</summary>
    private bool AtWord(string word) =>
        CheckWord(word) ||
        (Check(TokenKind.Punctuation, ".") &&
         CheckAhead(1, TokenKind.Identifier) &&
         Peek(1).IsWord(word));

    /// <summary>当前位置是任一关键字(允许“.”前缀)。</summary>
    private bool IsAnyWord(IReadOnlyCollection<string> words)
    {
        if (Peek().Kind == TokenKind.Identifier && words.Contains(Peek().Text, StringComparer.Ordinal))
        {
            return true;
        }

        return Check(TokenKind.Punctuation, ".") &&
               CheckAhead(1, TokenKind.Identifier) &&
               words.Contains(Peek(1).Text, StringComparer.Ordinal);
    }

    /// <summary>老语法声明行(.子程序/.程序集/…)或顶层声明关键字是否开始。</summary>
    private bool IsDeclarationRowAhead() =>
        (Check(TokenKind.Punctuation, ".") &&
         CheckAhead(1, TokenKind.Identifier) &&
         DeclarationRows.Contains(Peek(1).Text)) ||
        CheckWord("引入") || CheckWord("导入包") ||
        CheckWord("子程序") || CheckWord("函数") ||
        CheckWord("结构体") || CheckWord("记录") || CheckWord("接口");

    /// <summary>声明行的某一行(.参数/.局部变量)是否开始。</summary>
    private bool AtDataRow(string word) =>
        Check(TokenKind.Punctuation, ".") &&
        CheckAhead(1, TokenKind.Identifier) &&
        Peek(1).IsWord(word);

    private Token Advance()
    {
        if (!IsAtEnd)
        {
            _current++;
        }

        return Previous;
    }

    private Token Previous => _tokens[Math.Max(0, _current - 1)];
    private Token Peek() => _tokens[_current];
    private Token Peek(int distance) => _tokens[Math.Min(_tokens.Count - 1, _current + distance)];
    private bool AtLineEnd => Check(TokenKind.NewLine) || Check(TokenKind.EndOfFile);
    private bool IsAtEnd => Peek().Kind == TokenKind.EndOfFile;

    private YyDiagnostic Error(Token token, string message)
    {
        var hint = token.Kind == TokenKind.EndOfFile
            ? "检查上方是否缺少结束标记或右括号。"
            : null;
        return new YyDiagnostic(message, token.Line, token.Column, hint, _sourceName);
    }

    private static string Display(Token token) =>
        token.Kind is TokenKind.NewLine or TokenKind.EndOfFile ? "行尾" : token.Text;
}
