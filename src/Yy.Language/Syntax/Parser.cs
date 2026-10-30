namespace Yy.Language.Syntax;

public sealed class Parser
{
    private readonly string _source;
    private readonly string? _sourceName;
    private readonly List<Token> _tokens;
    private readonly List<ImportDeclaration> _imports = [];
    private readonly List<PackageImportDeclaration> _packageImports = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);
    private int _current;

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
            if (Check(TokenKind.Punctuation, "."))
            {
                ParseMetadata();
                continue;
            }

            if (CheckWord("子程序") || CheckWord("函数"))
            {
                functions.Add(ParseFunction());
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
            PackageImports = _packageImports
        };
    }

    private FunctionDeclaration ParseFunction()
    {
        var keyword = Advance();
        var name = ConsumeIdentifier("子程序名");
        Consume(TokenKind.Punctuation, "(", "子程序名后需要左括号");

        var parameters = new List<ParameterDeclaration>();
        if (!Check(TokenKind.Punctuation, ")"))
        {
            do
            {
                var parameterName = ConsumeIdentifier("参数名");
                string? parameterType = null;
                if (Match(TokenKind.Operator, ":"))
                {
                    parameterType = ParseTypeName("参数类型");
                }

                Expression? defaultValue = null;
                if (MatchOperator("="))
                {
                    defaultValue = ParseExpression();
                }

                parameters.Add(new ParameterDeclaration(parameterName.Text, defaultValue, parameterType));
            }
            while (Match(TokenKind.Punctuation, ","));
        }

        Consume(TokenKind.Punctuation, ")", "参数列表缺少右括号");
        string? returnType = null;
        if (Match(TokenKind.Operator, ":"))
        {
            returnType = ParseTypeName("返回值类型");
        }

        ConsumeLineEnd();
        var body = ParseBlock(["结束子程序", "子程序结束", "结束函数", "函数结束"]);

        if (!IsAtEnd && IsAnyWord(["结束子程序", "子程序结束", "结束函数", "函数结束"]))
        {
            ConsumeLine();
        }
        else
        {
            throw Error(Peek(), $"子程序“{name.Text}”缺少结束标记，请添加“结束子程序”。");
        }

        return new FunctionDeclaration(
            keyword.Line,
            keyword.Column,
            name.Text,
            parameters,
            body,
            returnType,
            _sourceName);
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
                throw Error(
                    Peek(),
                    "“.版本”不属于用户源代码；格式版本由工程清单管理。");
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

        if (Yy.Sdk.YySdk.FindPackage(name.Text) is null)
        {
            throw new YyDiagnostic(
                $"找不到系统包“{name.Text}”。",
                name.Line,
                name.Column,
                "可用包：" + string.Join(
                    "、",
                    Yy.Sdk.YySdk.Packages.Select(package => package.Name)),
                _sourceName);
        }

        var import = new PackageImportDeclaration(keyword.Line, keyword.Column, name.Text);
        ConsumeLineEnd();
        return import;
    }

    private BlockStatement ParseBlock(string[] terminators)
    {
        var start = Peek();
        var statements = new List<Statement>();
        SkipNewLines();

        while (!IsAtEnd && !IsAnyWord(terminators))
        {
            statements.Add(ParseStatement());
        }

        return new BlockStatement(start.Line, start.Column, statements);
    }

    private Statement ParseStatement()
    {
        SkipNewLines();
        var token = Peek();

        if (IsAnyWord(["结束如果", "如果结束", "否则", "否则如果", "结束循环", "循环结束",
                "结束遍历", "遍历结束", "结束尝试", "尝试结束", "捕获", "结束子程序", "子程序结束",
                "结束函数", "函数结束"]))
        {
            throw Error(token, $"意外的结束标记“{token.Text}”。");
        }

        if (MatchWord("变量") || MatchWord("定义"))
        {
            return ParseVariable(token, false);
        }

        if (MatchWord("常量"))
        {
            return ParseVariable(token, true);
        }

        if (MatchWord("如果"))
        {
            return ParseIf(token);
        }

        if (MatchWord("当"))
        {
            return ParseWhile(token);
        }

        if (MatchWord("计次循环"))
        {
            return ParseCountLoop(token);
        }

        if (MatchWord("遍历"))
        {
            return ParseForEach(token);
        }

        if (MatchWord("返回"))
        {
            var value = AtLineEnd ? null : ParseExpression();
            var statement = new ReturnStatement(token.Line, token.Column, value);
            ConsumeLineEnd();
            return statement;
        }

        if (MatchWord("跳出"))
        {
            var statement = new BreakStatement(token.Line, token.Column);
            ConsumeLineEnd();
            return statement;
        }

        if (MatchWord("继续"))
        {
            var statement = new ContinueStatement(token.Line, token.Column);
            ConsumeLineEnd();
            return statement;
        }

        if (MatchWord("尝试"))
        {
            return ParseTry(token);
        }

        if ((CheckWord("输出") || CheckWord("打印") || CheckWord("提示")) &&
            !CheckAhead(1, TokenKind.Punctuation, "("))
        {
            var command = Advance();
            var arguments = new List<Expression>();
            if (!AtLineEnd)
            {
                do
                {
                    arguments.Add(ParseExpression());
                }
                while (Match(TokenKind.Punctuation, ","));
            }

            var call = new CallExpression(
                command.Line,
                command.Column,
                new IdentifierExpression(command.Line, command.Column, command.Text),
                arguments);
            var statement = new ExpressionStatement(command.Line, command.Column, call);
            ConsumeLineEnd();
            return statement;
        }

        var left = ParseExpression();
        if (Check(TokenKind.Operator, "=") ||
            Check(TokenKind.Operator, "+=") ||
            Check(TokenKind.Operator, "-=") ||
            Check(TokenKind.Operator, "*=") ||
            Check(TokenKind.Operator, "/=") ||
            Check(TokenKind.Operator, "%="))
        {
            var assignment = Advance();
            if (left is not IdentifierExpression and not MemberExpression and not IndexExpression)
            {
                throw Error(assignment, "赋值号左侧必须是变量、对象属性或数组元素。");
            }

            var value = ParseExpression();
            var statement = new AssignStatement(token.Line, token.Column, left, assignment.Text, value);
            ConsumeLineEnd();
            return statement;
        }

        var expressionStatement = new ExpressionStatement(token.Line, token.Column, left);
        ConsumeLineEnd();
        return expressionStatement;
    }

    private Statement ParseVariable(Token start, bool isConstant)
    {
        var name = ConsumeIdentifier("变量名");
        string? declaredType = null;
        if (Match(TokenKind.Operator, ":"))
        {
            declaredType = ParseTypeName("变量类型");
        }

        Consume(TokenKind.Operator, "=", "变量声明需要使用“=”给出初始值");
        var initializer = ParseExpression();
        var statement = new VariableStatement(
            start.Line,
            start.Column,
            name.Text,
            initializer,
            isConstant,
            declaredType);
        ConsumeLineEnd();
        return statement;
    }

    private string ParseTypeName(string description)
    {
        var token = ConsumeIdentifier(description);
        if (YyTypeSystem.IsKnown(token.Text))
        {
            return YyTypeSystem.CanonicalName(token.Text);
        }

        throw new YyDiagnostic(
            $"未知类型“{token.Text}”",
            token.Line,
            token.Column,
            $"可用类型：{string.Join("、", YyTypeSystem.KnownTypes)}。",
            _sourceName);
    }

    private IfStatement ParseIf(Token start)
    {
        return ParseIf(start, consumeEnd: true);
    }

    private IfStatement ParseIf(Token start, bool consumeEnd)
    {
        var condition = ParseExpression();
        MatchWord("则");
        ConsumeLineEnd();

        var then = ParseBlock(["否则如果", "否则", "结束如果", "如果结束"]);
        BlockStatement? elseBlock = null;

        if (IsAnyWord(["否则如果", "否则"]))
        {
            var branch = Peek();
            if (MatchWord("否则如果"))
            {
                var nested = ParseIf(branch, consumeEnd);
                elseBlock = new BlockStatement(branch.Line, branch.Column, [nested]);
            }
            else
            {
                Advance();
                ConsumeLineEnd();
                elseBlock = ParseBlock(["结束如果", "如果结束"]);
                if (consumeEnd)
                {
                    ConsumeEnd("结束如果", "如果结束", "如果语句");
                }
            }
        }
        else if (consumeEnd)
        {
            ConsumeEnd("结束如果", "如果结束", "如果语句");
        }

        return new IfStatement(start.Line, start.Column, condition, then, elseBlock);
    }

    private Statement ParseWhile(Token start)
    {
        var condition = ParseExpression();
        MatchWord("循环");
        ConsumeLineEnd();
        var body = ParseBlock(["结束循环", "循环结束"]);
        ConsumeEnd("结束循环", "循环结束", "当循环");
        return new WhileStatement(start.Line, start.Column, condition, body);
    }

    private Statement ParseCountLoop(Token start)
    {
        var variable = ConsumeIdentifier("循环变量");
        MatchWord("从");
        var from = ParseExpression();
        MatchWord("到");
        var to = ParseExpression();
        Expression step = new LiteralExpression(start.Line, start.Column, 1d);
        if (MatchWord("步长"))
        {
            step = ParseExpression();
        }

        ConsumeLineEnd();
        var body = ParseBlock(["结束循环", "循环结束"]);
        ConsumeEnd("结束循环", "循环结束", "计次循环");
        return new CountLoopStatement(start.Line, start.Column, variable.Text, from, to, step, body);
    }

    private Statement ParseForEach(Token start)
    {
        var variable = ConsumeIdentifier("遍历变量");
        MatchWord("于");
        var collection = ParseExpression();
        MatchWord("循环");
        ConsumeLineEnd();
        var body = ParseBlock(["结束遍历", "遍历结束", "结束循环", "循环结束"]);

        if (IsAnyWord(["结束遍历", "遍历结束"]))
        {
            ConsumeEnd("结束遍历", "遍历结束", "遍历语句");
        }
        else
        {
            ConsumeEnd("结束循环", "循环结束", "遍历语句");
        }

        return new ForEachStatement(start.Line, start.Column, variable.Text, collection, body);
    }

    private Statement ParseTry(Token start)
    {
        ConsumeLineEnd();
        var tryBlock = ParseBlock(["捕获", "结束尝试", "尝试结束"]);
        string? errorVariable = null;
        BlockStatement? catchBlock = null;

        if (MatchWord("捕获"))
        {
            if (Check(TokenKind.Identifier))
            {
                errorVariable = Advance().Text;
            }

            ConsumeLineEnd();
            catchBlock = ParseBlock(["结束尝试", "尝试结束"]);
        }

        ConsumeEnd("结束尝试", "尝试结束", "尝试语句");
        return new TryStatement(start.Line, start.Column, tryBlock, errorVariable, catchBlock);
    }

    private Expression ParseExpression(int minimumPrecedence = 0)
    {
        var left = ParseUnary();

        while (TryGetBinaryOperator(Peek(), out var precedence) && precedence >= minimumPrecedence)
        {
            var op = Advance();
            var right = ParseExpression(precedence + 1);
            left = new BinaryExpression(left.Line, left.Column, left, op.Text, right);
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (CheckWord("非") || Check(TokenKind.Operator, "!") || Check(TokenKind.Operator, "-") ||
            Check(TokenKind.Operator, "+"))
        {
            var op = Advance();
            var operand = ParseUnary();
            return new UnaryExpression(op.Line, op.Column, op.Text, operand);
        }

        return ParsePostfix(ParsePrimary());
    }

    private Expression ParsePostfix(Expression expression)
    {
        while (true)
        {
            if (Match(TokenKind.Punctuation, "("))
            {
                var arguments = new List<Expression>();
                if (!Check(TokenKind.Punctuation, ")"))
                {
                    do
                    {
                        SkipNewLines();
                        arguments.Add(ParseExpression());
                        SkipNewLines();
                    }
                    while (Match(TokenKind.Punctuation, ","));
                }

                Consume(TokenKind.Punctuation, ")", "函数调用缺少右括号");
                expression = new CallExpression(expression.Line, expression.Column, expression, arguments);
                continue;
            }

            if (Match(TokenKind.Punctuation, "["))
            {
                var index = ParseExpression();
                Consume(TokenKind.Punctuation, "]", "索引缺少右方括号");
                expression = new IndexExpression(expression.Line, expression.Column, expression, index);
                continue;
            }

            if (Match(TokenKind.Punctuation, "."))
            {
                var member = ConsumeIdentifier("属性名");
                expression = new MemberExpression(expression.Line, expression.Column, expression, member.Text);
                continue;
            }

            break;
        }

        return expression;
    }

    private Expression ParsePrimary()
    {
        var token = Advance();
        switch (token.Kind)
        {
            case TokenKind.Number:
            case TokenKind.String:
                return new LiteralExpression(token.Line, token.Column, token.Value);

            case TokenKind.Identifier:
                return token.Text switch
                {
                    "真" or "是" => new LiteralExpression(token.Line, token.Column, true),
                    "假" or "否" => new LiteralExpression(token.Line, token.Column, false),
                    "空" or "无" => new LiteralExpression(token.Line, token.Column, null),
                    _ => new IdentifierExpression(token.Line, token.Column, token.Text)
                };

            case TokenKind.Punctuation when token.Text == "(":
                var expression = ParseExpression();
                Consume(TokenKind.Punctuation, ")", "表达式缺少右括号");
                return expression;

            case TokenKind.Punctuation when token.Text == "[":
                var items = new List<Expression>();
                SkipNewLines();
                if (!Check(TokenKind.Punctuation, "]"))
                {
                    do
                    {
                        items.Add(ParseExpression());
                        SkipNewLines();
                    }
                    while (Match(TokenKind.Punctuation, ","));
                }

                Consume(TokenKind.Punctuation, "]", "数组缺少右方括号");
                return new ArrayExpression(token.Line, token.Column, items);

            case TokenKind.Punctuation when token.Text == "{":
                var properties = new List<(string Key, Expression Value)>();
                SkipNewLines();
                if (!Check(TokenKind.Punctuation, "}"))
                {
                    do
                    {
                        SkipNewLines();
                        var key = Advance();
                        if (key.Kind is not TokenKind.Identifier and not TokenKind.String)
                        {
                            throw Error(key, "对象属性名需要使用标识符或字符串。");
                        }

                        Consume(TokenKind.Operator, ":", $"对象属性“{key.Text}”后需要冒号");
                        var value = ParseExpression();
                        properties.Add((key.Text, value));
                        SkipNewLines();
                    }
                    while (Match(TokenKind.Punctuation, ","));
                }

                Consume(TokenKind.Punctuation, "}", "对象缺少右花括号");
                return new ObjectExpression(token.Line, token.Column, properties);

            default:
                throw Error(token, $"此处不能使用“{Display(token)}”。");
        }
    }

    private static bool TryGetBinaryOperator(Token token, out int precedence)
    {
        precedence = token.Text switch
        {
            "??" => 1,
            "或" or "||" => 2,
            "且" or "&&" => 3,
            "==" or "!=" or "等于" or "不等于" => 4,
            "<" or "<=" or ">" or ">=" => 5,
            "+" or "-" => 6,
            "*" or "/" or "%" => 7,
            _ => -1
        };

        return precedence >= 0 && token.Kind is TokenKind.Operator or TokenKind.Identifier;
    }

    private void ConsumeEnd(string first, string second, string construct)
    {
        if (!IsAnyWord([first, second]))
        {
            throw Error(Peek(), $"{construct}缺少结束标记，请添加“{first}”。");
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

    private bool MatchWord(string word)
    {
        if (!CheckWord(word))
        {
            return false;
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

    private bool CheckWord(string word) => Peek().IsWord(word);

    private bool IsAnyWord(IReadOnlyCollection<string> words)
    {
        return Peek().Kind == TokenKind.Identifier && words.Contains(Peek().Text, StringComparer.Ordinal);
    }

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
