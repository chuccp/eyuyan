using System.Globalization;
using System.Text;
using Yy.Language.Syntax;

namespace Yy.Language.Structured;

public sealed class YyCodeDocument
{
    public string Name { get; set; } = "程序集";
    public List<string> Imports { get; } = [];
    public List<YyCodeStatement> MainStatements { get; } = [];
    public List<YyCodeFunction> Functions { get; } = [];
}

public sealed class YyCodeFunction
{
    public string Name { get; set; } = "子程序";
    public string ReturnType { get; set; } = string.Empty;
    public List<YyCodeParameter> Parameters { get; } = [];
    public List<YyCodeStatement> Statements { get; } = [];
}

public sealed class YyCodeParameter
{
    public string Name { get; set; } = "参数";
    public string TypeName { get; set; } = string.Empty;
    public string DefaultExpression { get; set; } = string.Empty;
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

    public static string ToSource(YyCodeDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(".版本 3");
        builder.Append(".程序集 ").AppendLine(document.Name);
        foreach (var import in document.Imports.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            builder.Append("引入 ").AppendLine(Quote(import));
        }

        if (document.Imports.Count > 0)
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
            EmitStatements(builder, function.Statements, 1);
            builder.AppendLine("结束子程序");
            builder.AppendLine();
        }

        EmitStatements(builder, document.MainStatements, 0);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void EmitStatements(
        StringBuilder builder,
        IReadOnlyList<YyCodeStatement> statements,
        int depth)
    {
        var indent = new string(' ', depth * 4);
        foreach (var statement in statements)
        {
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
                    builder.Append(indent).Append("如果 ").Append(statement.Condition)
                        .AppendLine(" 则");
                    EmitStatements(builder, statement.Children, depth + 1);
                    if (statement.ElseChildren.Count > 0)
                    {
                        builder.Append(indent).AppendLine("否则");
                        EmitStatements(builder, statement.ElseChildren, depth + 1);
                    }

                    builder.Append(indent).AppendLine("结束如果");
                    break;
                case YyCodeStatementKinds.While:
                    builder.Append(indent).Append("当 ").Append(statement.Condition)
                        .AppendLine(" 循环");
                    EmitStatements(builder, statement.Children, depth + 1);
                    builder.Append(indent).AppendLine("结束循环");
                    break;
                case YyCodeStatementKinds.CountLoop:
                    builder.Append(indent).Append("计次循环 ").Append(statement.Variable)
                        .Append(" 从 ").Append(statement.From)
                        .Append(" 到 ").Append(statement.To)
                        .Append(" 步长 ").AppendLine(statement.Step);
                    EmitStatements(builder, statement.Children, depth + 1);
                    builder.Append(indent).AppendLine("结束循环");
                    break;
                case YyCodeStatementKinds.ForEach:
                    builder.Append(indent).Append("遍历 ").Append(statement.Variable)
                        .Append(" 于 ").AppendLine(statement.Expression);
                    EmitStatements(builder, statement.Children, depth + 1);
                    builder.Append(indent).AppendLine("结束遍历");
                    break;
                case YyCodeStatementKinds.Try:
                    builder.Append(indent).AppendLine("尝试");
                    EmitStatements(builder, statement.Children, depth + 1);
                    builder.Append(indent).Append("捕获 ").AppendLine(statement.CatchName);
                    EmitStatements(builder, statement.ElseChildren, depth + 1);
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
            BinaryExpression binary =>
                $"({ExpressionToSource(binary.Left)} {binary.Operator} {ExpressionToSource(binary.Right)})",
            CallExpression call =>
                $"{ExpressionToSource(call.Callee)}({string.Join(", ", call.Arguments.Select(ExpressionToSource))})",
            MemberExpression member =>
                $"{ExpressionToSource(member.Target)}.{member.Member}",
            IndexExpression index =>
                $"{ExpressionToSource(index.Target)}[{ExpressionToSource(index.Index)}]",
            _ => "空"
        };
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
