using System.Globalization;
using System.Text;
using Yy.Language.Syntax;

namespace Yy.Language.Structured;

public sealed class YyCodeDocument
{
    public string Name { get; set; } = "程序集";
    public string Remark { get; set; } = string.Empty;
    public List<string> Packages { get; } = [];
    public List<string> Imports { get; } = [];
    public List<YyCodeStatement> MainStatements { get; } = [];
    public List<YyCodeFunction> Functions { get; } = [];
}

public sealed class YyCodeFunction
{
    public string Name { get; set; } = "子程序";
    public string ReturnType { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsExport { get; set; }
    public string Remark { get; set; } = string.Empty;
    public List<YyCodeParameter> Parameters { get; } = [];
    public List<YyCodeStatement> Statements { get; } = [];
}

public sealed class YyCodeParameter
{
    public string Name { get; set; } = "参数";
    public string TypeName { get; set; } = string.Empty;
    public string DefaultExpression { get; set; } = string.Empty;
    public bool ByRef { get; set; }
    public bool Nullable { get; set; }
    public bool IsArray { get; set; }
    public string Remark { get; set; } = string.Empty;
}

public sealed class YyCodeStatement
{
    public required string Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Operator { get; set; } = "=";
    public string Expression { get; set; } = string.Empty;
    public string Condition { get; set; } = "真";
    public string Variable { get; set; } = string.Empty;
    public string From { get; set; } = "1";
    public string To { get; set; } = "10";
    public string Step { get; set; } = "1";
    public string CatchName { get; set; } = "错误";
    public bool IsStatic { get; set; }
    public bool IsArray { get; set; }
    public string Remark { get; set; } = string.Empty;
    public List<YyCodeStatement> Children { get; } = [];
    public List<YyCodeStatement> ElseChildren { get; } = [];
}

public static class YyCodeStatementKinds
{
    public const string Variable = "变量";
    public const string Constant = "常量";
    public const string Assign = "赋值";
    public const string Call = "调用";
    public const string If = "如果";
    public const string While = "当";
    public const string CountLoop = "计数循环";
    public const string ForEach = "遍历";
    public const string Try = "尝试";
    public const string Return = "返回";
    public const string Break = "跳出";
    public const string Continue = "继续";

    public static IReadOnlyList<string> All { get; } =
    [
        Variable,
        Constant,
        Assign,
        Call,
        If,
        While,
        CountLoop,
        ForEach,
        Try,
        Return,
        Break,
        Continue
    ];

    public static bool IsContainer(string kind) =>
        kind is If or While or CountLoop or ForEach or Try;
}

public static class YyCodeDocumentConverter
{
    public static YyCodeDocument FromSource(string source, string? sourceName = null)
    {
        var program = new Parser(source, sourceName).Parse();
        var document = new YyCodeDocument
        {
            Name = program.Metadata.GetValueOrDefault("程序集", "程序集")
        };
        document.Imports.AddRange(program.Imports.Select(import => import.Path));
        document.Packages.AddRange(program.PackageImports.Select(package => package.Name));
        document.MainStatements.AddRange(program.Main.Statements.Select(ConvertStatement));
        foreach (var function in program.Functions)
        {
            var converted = new YyCodeFunction
            {
                Name = function.Name,
                ReturnType = function.ReturnType ?? string.Empty
            };
            converted.Parameters.AddRange(function.Parameters.Select(parameter => new YyCodeParameter
            {
                Name = parameter.Name,
                TypeName = parameter.TypeName ?? string.Empty,
                DefaultExpression = parameter.DefaultValue is null
                    ? string.Empty
                    : ExpressionToSource(parameter.DefaultValue)
            }));
            converted.Statements.AddRange(function.Body.Statements.Select(ConvertStatement));
            document.Functions.Add(converted);
        }

        return document;
    }

    public static string ToSource(YyCodeDocument document) => ToSource(document, null);

    /// <summary>生成源码;可选记录每条语句的起始行号(1 起),供调试定位表格行。</summary>
    public static string ToSource(YyCodeDocument document, IDictionary<YyCodeStatement, int>? lineMap)
    {
        var builder = new StringBuilder();
        builder.AppendLine(".版本 1");
        builder.Append(".程序集 ").AppendLine(document.Name);
        foreach (var package in document.Packages.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            builder.Append("导入包 ").AppendLine(Quote(package));
        }

        foreach (var import in document.Imports.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            builder.Append("引入 ").AppendLine(Quote(import));
        }

        if (document.Packages.Count > 0 || document.Imports.Count > 0)
        {
            builder.AppendLine();
        }

        foreach (var function in document.Functions)
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
            builder.Append("子程序 ").Append(function.Name)
                .Append('(').Append(parameters).Append(')')
                .Append(returnType).AppendLine();
            EmitStatements(builder, function.Statements, 1, lineMap);
            builder.AppendLine("结束子程序");
            builder.AppendLine();
        }

        EmitStatements(builder, document.MainStatements, 0, lineMap);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>把单条语句转成一行源码文本(表格编辑器行显示用;容器语句仅显示头行)。</summary>
    public static string StatementToSource(YyCodeStatement statement) => statement.Kind switch
    {
        YyCodeStatementKinds.Variable or YyCodeStatementKinds.Constant =>
            statement.Kind + " " + statement.Name +
            (string.IsNullOrWhiteSpace(statement.TypeName) ? string.Empty : ": " + statement.TypeName) +
            " = " + statement.Expression,
        YyCodeStatementKinds.Assign =>
            statement.Target + " " + statement.Operator + " " + statement.Expression,
        YyCodeStatementKinds.Call => statement.Expression,
        YyCodeStatementKinds.If => "如果(" + WrapCondition(statement.Condition) + ")则",
        YyCodeStatementKinds.While => "当(" + WrapCondition(statement.Condition) + ")循环",
        YyCodeStatementKinds.CountLoop =>
            "计次循环 " + statement.Variable + " 从 " + statement.From +
            " 到 " + statement.To + " 步长 " + statement.Step,
        YyCodeStatementKinds.ForEach => "遍历 " + statement.Variable + " 于 " + statement.Expression,
        YyCodeStatementKinds.Try => "尝试",
        YyCodeStatementKinds.Return => string.IsNullOrWhiteSpace(statement.Expression)
            ? "返回"
            : "返回 " + statement.Expression,
        YyCodeStatementKinds.Break => "跳出",
        YyCodeStatementKinds.Continue => "继续",
        _ => statement.Expression
    };

    /// <summary>解析一行语句文本为结构化语句(表格编辑器行编辑确认用)。兼容官方全角运算符显示输入。</summary>
    public static YyCodeStatement ParseStatementText(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("＜＞", "<>", StringComparison.Ordinal)
            .Replace("＜＝", "<=", StringComparison.Ordinal)
            .Replace("＞＝", ">=", StringComparison.Ordinal)
            .Replace("＝", "=", StringComparison.Ordinal)
            .Replace("＋", "+", StringComparison.Ordinal)
            .Replace("－", "-", StringComparison.Ordinal)
            .Replace("＊", "*", StringComparison.Ordinal)
            .Replace("／", "/", StringComparison.Ordinal)
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal)
            .Replace("＜", "<", StringComparison.Ordinal)
            .Replace("＞", ">", StringComparison.Ordinal);
        var trimmed = normalized.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new YyCodeFormatException("语句不能为空。", 1, 1);
        }

        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
        {
            throw new YyCodeFormatException("一次只能输入一行语句。", 1, 1);
        }

        var closer = trimmed switch
        {
            var t when t.StartsWith("如果", StringComparison.Ordinal) => "\n结束如果",
            var t when t.StartsWith("当", StringComparison.Ordinal) ||
                       t.StartsWith("计次循环", StringComparison.Ordinal) => "\n结束循环",
            var t when t.StartsWith("遍历", StringComparison.Ordinal) => "\n结束遍历",
            var t when t.StartsWith("尝试", StringComparison.Ordinal) => "\n结束尝试",
            _ => string.Empty
        };
        var wrapped = ".版本 1\n.程序集 _行\n子程序 _行()\n" + trimmed + closer + "\n结束子程序\n";
        var document = FromSource(wrapped, "_行");
        if (document.Functions.Count != 1 || document.Functions[0].Statements.Count != 1)
        {
            throw new YyCodeFormatException("一次只能输入一行语句。", 1, 1);
        }

        return document.Functions[0].Statements[0];
    }

    private static void EmitStatements(
        StringBuilder builder,
        IReadOnlyList<YyCodeStatement> statements,
        int depth,
        IDictionary<YyCodeStatement, int>? lineMap = null)
    {
        var indent = new string(' ', depth * 4);
        foreach (var statement in statements)
        {
            if (lineMap is not null)
            {
                lineMap[statement] = CountLines(builder) + 1;
            }

            switch (statement.Kind)
            {
                case YyCodeStatementKinds.Variable:
                case YyCodeStatementKinds.Constant:
                    builder.Append(indent).Append(statement.Kind).Append(' ')
                        .Append(statement.Name);
                    if (!string.IsNullOrWhiteSpace(statement.TypeName))
                    {
                        builder.Append(": ").Append(statement.TypeName);
                    }

                    builder.Append(" = ").AppendLine(statement.Expression);
                    break;
                case YyCodeStatementKinds.Assign:
                    builder.Append(indent).Append(statement.Target)
                        .Append(' ').Append(statement.Operator)
                        .Append(' ').AppendLine(statement.Expression);
                    break;
                case YyCodeStatementKinds.Call:
                    builder.Append(indent).AppendLine(statement.Expression);
                    break;
                case YyCodeStatementKinds.If:
                    builder.Append(indent).Append("如果(").Append(WrapCondition(statement.Condition))
                        .AppendLine(")则");
                    EmitStatements(builder, statement.Children, depth + 1, lineMap);
                    if (statement.ElseChildren.Count > 0)
                    {
                        builder.Append(indent).AppendLine("否则");
                        EmitStatements(builder, statement.ElseChildren, depth + 1, lineMap);
                    }

                    builder.Append(indent).AppendLine("结束如果");
                    break;
                case YyCodeStatementKinds.While:
                    builder.Append(indent).Append("当(").Append(WrapCondition(statement.Condition))
                        .AppendLine(")循环");
                    EmitStatements(builder, statement.Children, depth + 1, lineMap);
                    builder.Append(indent).AppendLine("结束循环");
                    break;
                case YyCodeStatementKinds.CountLoop:
                    builder.Append(indent).Append("计次循环 ").Append(statement.Variable)
                        .Append(" 从 ").Append(statement.From)
                        .Append(" 到 ").Append(statement.To)
                        .Append(" 步长 ").AppendLine(statement.Step);
                    EmitStatements(builder, statement.Children, depth + 1, lineMap);
                    builder.Append(indent).AppendLine("结束循环");
                    break;
                case YyCodeStatementKinds.ForEach:
                    builder.Append(indent).Append("遍历 ").Append(statement.Variable)
                        .Append(" 于 ").AppendLine(statement.Expression);
                    EmitStatements(builder, statement.Children, depth + 1, lineMap);
                    builder.Append(indent).AppendLine("结束遍历");
                    break;
                case YyCodeStatementKinds.Try:
                    builder.Append(indent).AppendLine("尝试");
                    EmitStatements(builder, statement.Children, depth + 1, lineMap);
                    builder.Append(indent).Append("捕获 ").AppendLine(statement.CatchName);
                    EmitStatements(builder, statement.ElseChildren, depth + 1, lineMap);
                    builder.Append(indent).AppendLine("结束尝试");
                    break;
                case YyCodeStatementKinds.Return:
                    builder.Append(indent).Append("返回");
                    if (!string.IsNullOrWhiteSpace(statement.Expression))
                    {
                        builder.Append(' ').Append(statement.Expression);
                    }

                    builder.AppendLine();
                    break;
                case YyCodeStatementKinds.Break:
                    builder.Append(indent).AppendLine("跳出");
                    break;
                case YyCodeStatementKinds.Continue:
                    builder.Append(indent).AppendLine("继续");
                    break;
            }
        }
    }

    private static int CountLines(StringBuilder builder)
    {
        var lines = 0;
        for (var i = 0; i < builder.Length; i++)
        {
            if (builder[i] == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static YyCodeStatement ConvertStatement(Statement statement)
    {
        return statement switch
        {
            VariableStatement variable => new YyCodeStatement
            {
                Kind = variable.IsConstant ? YyCodeStatementKinds.Constant : YyCodeStatementKinds.Variable,
                Name = variable.Name,
                TypeName = variable.DeclaredType ?? string.Empty,
                Expression = ExpressionToSource(variable.Initializer)
            },
            AssignStatement assignment => new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Assign,
                Target = ExpressionToSource(assignment.Target),
                Operator = assignment.Operator,
                Expression = ExpressionToSource(assignment.Value)
            },
            ExpressionStatement expression => new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Call,
                Expression = ExpressionToSource(expression.Expression)
            },
            IfStatement conditional => ConvertIf(conditional),
            WhileStatement loop => ConvertContainer(
                YyCodeStatementKinds.While,
                loop.Body,
                value => value.Condition = ExpressionToSource(loop.Condition)),
            CountLoopStatement loop => ConvertContainer(
                YyCodeStatementKinds.CountLoop,
                loop.Body,
                value =>
                {
                    value.Variable = loop.VariableName;
                    value.From = ExpressionToSource(loop.Start);
                    value.To = ExpressionToSource(loop.End);
                    value.Step = ExpressionToSource(loop.Step);
                }),
            ForEachStatement loop => ConvertContainer(
                YyCodeStatementKinds.ForEach,
                loop.Body,
                value =>
                {
                    value.Variable = loop.VariableName;
                    value.Expression = ExpressionToSource(loop.Collection);
                }),
            TryStatement attempt => ConvertTry(attempt),
            ReturnStatement @return => new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Return,
                Expression = @return.Value is null ? string.Empty : ExpressionToSource(@return.Value)
            },
            BreakStatement => new YyCodeStatement { Kind = YyCodeStatementKinds.Break },
            ContinueStatement => new YyCodeStatement { Kind = YyCodeStatementKinds.Continue },
            _ => new YyCodeStatement
            {
                Kind = YyCodeStatementKinds.Call,
                Expression = "' 暂不支持的语句"
            }
        };
    }

    private static YyCodeStatement ConvertIf(IfStatement conditional)
    {
        var statement = new YyCodeStatement
        {
            Kind = YyCodeStatementKinds.If,
            Condition = ExpressionToSource(conditional.Condition)
        };
        statement.Children.AddRange(conditional.Then.Statements.Select(ConvertStatement));
        if (conditional.Else is not null)
        {
            statement.ElseChildren.AddRange(conditional.Else.Statements.Select(ConvertStatement));
        }

        return statement;
    }

    private static YyCodeStatement ConvertContainer(
        string kind,
        BlockStatement body,
        Action<YyCodeStatement> configure)
    {
        var statement = new YyCodeStatement { Kind = kind };
        configure(statement);
        statement.Children.AddRange(body.Statements.Select(ConvertStatement));
        return statement;
    }

    private static YyCodeStatement ConvertTry(TryStatement attempt)
    {
        var statement = new YyCodeStatement
        {
            Kind = YyCodeStatementKinds.Try,
            CatchName = attempt.ErrorVariable ?? "错误"
        };
        statement.Children.AddRange(attempt.TryBlock.Statements.Select(ConvertStatement));
        if (attempt.CatchBlock is not null)
        {
            statement.ElseChildren.AddRange(attempt.CatchBlock.Statements.Select(ConvertStatement));
        }

        return statement;
    }

    private static string ExpressionToSource(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => LiteralToSource(literal.Value),
            IdentifierExpression identifier => identifier.Name,
            ArrayExpression array => "[" + string.Join(", ", array.Items.Select(ExpressionToSource)) + "]",
            ObjectExpression obj =>
                "{" + string.Join(", ", obj.Properties.Select(property =>
                    $"{property.Key}: {ExpressionToSource(property.Value)}")) + "}",
            UnaryExpression unary => $"{unary.Operator}{Parenthesize(unary.Operand)}",
            BinaryExpression binary => FormatBinary(binary),
            CallExpression call =>
                $"{ExpressionToSource(call.Callee)}({string.Join(", ", call.Arguments.Select(ExpressionToSource))})",
            MemberExpression member =>
                $"{ExpressionToSource(member.Target)}.{member.Member}",
            IndexExpression index =>
                $"{ExpressionToSource(index.Target)}[{ExpressionToSource(index.Index)}]",
            _ => "空"
        };
    }

    /// <summary>与 Parser.TryGetBinaryOperator 相同的优先级(值大绑定紧),按此省略冗余括号。</summary>
    private static int OperatorPrecedence(string op) => op switch
    {
        "??" => 1,
        "或" or "||" => 2,
        "且" or "&&" => 3,
        "==" or "!=" or "等于" or "不等于" => 4,
        "<" or "<=" or ">" or ">=" => 5,
        "+" or "-" => 6,
        "*" or "/" or "%" => 7,
        _ => 8
    };

    private static int ExpressionPrecedence(Expression expression) => expression switch
    {
        BinaryExpression binary => OperatorPrecedence(binary.Operator),
        UnaryExpression => 8,
        _ => 99
    };

    private static string FormatBinary(BinaryExpression binary)
    {
        var precedence = OperatorPrecedence(binary.Operator);
        var left = ExpressionToSource(binary.Left);
        var right = ExpressionToSource(binary.Right);
        if (ExpressionPrecedence(binary.Left) < precedence)
        {
            left = "(" + left + ")";
        }

        if (ExpressionPrecedence(binary.Right) <= precedence)
        {
            right = "(" + right + ")";
        }

        return left + " " + binary.Operator + " " + right;
    }

    /// <summary>条件文本补一层括号;旧数据自带整体括号的不重复包裹。</summary>
    private static string WrapCondition(string condition)
    {
        var trimmed = (condition ?? string.Empty).Trim();
        if (trimmed.StartsWith("(", StringComparison.Ordinal) &&
            trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var depth = 0;
            var wraps = true;
            for (var i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '(')
                {
                    depth++;
                }
                else if (trimmed[i] == ')')
                {
                    depth--;
                    if (depth == 0 && i < trimmed.Length - 1)
                    {
                        wraps = false;
                        break;
                    }
                }
            }

            if (wraps && depth == 0)
            {
                return trimmed;
            }
        }

        return "(" + trimmed + ")";
    }

    private static string Parenthesize(Expression expression) =>
        expression is BinaryExpression ? $"({ExpressionToSource(expression)})" : ExpressionToSource(expression);

    private static string LiteralToSource(object? value)
    {
        return value switch
        {
            null => "空",
            bool boolean => boolean ? "真" : "假",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            string text => Quote(text),
            _ => Quote(value.ToString() ?? string.Empty)
        };
    }

    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
