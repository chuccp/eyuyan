namespace Yy.Language.Syntax;

public sealed partial class Parser
{
    /// <summary>条件表达式:官方用“=”/“＝”表示相等比较(赋值仅在语句层出现)。</summary>
    private Expression ParseCondition()
    {
        var left = ParseExpression();
        while (Check(TokenKind.Operator, "=") ||
               Check(TokenKind.Operator, "＝") ||
               Check(TokenKind.Operator, "≠"))
        {
            var op = Advance();
            var right = ParseExpression();
            var operatorText = op.Text == "≠" ? "!=" : "==";
            left = new BinaryExpression(left.Line, left.Column, left, operatorText, right);
        }

        return left;
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

        // 老语法:“&子程序名”取子程序地址(如启动线程的参数)。
        if (Check(TokenKind.Operator, "&"))
        {
            var op = Advance();
            var operand = ParseUnary();
            return new UnaryExpression(op.Line, op.Column, "&", operand);
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
                    while (true)
                    {
                        SkipNewLines();
                        // 老语法允许省略参数:f(a, , b)、f(a, )。
                        if (Check(TokenKind.Punctuation, ",") || Check(TokenKind.Punctuation, ")"))
                        {
                            arguments.Add(new LiteralExpression(
                                Peek().Line,
                                Peek().Column,
                                null));
                        }
                        else
                        {
                            arguments.Add(ParseExpression());
                        }

                        SkipNewLines();
                        if (!Match(TokenKind.Punctuation, ","))
                        {
                            break;
                        }
                    }
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
}
