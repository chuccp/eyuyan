namespace Yy.Language.Syntax;

public sealed partial class Parser
{
    private Statement ParseStatement()
    {
        SkipNewLines();
        var token = Peek();

        // 老语法语句可带“.”前缀(.如果真、.计次循环首 …)。
        if (Check(TokenKind.Punctuation, ".") &&
            CheckAhead(1, TokenKind.Identifier) &&
            StatementKeywords.Contains(Peek(1).Text))
        {
            Advance();
            token = Peek();
        }

        if (IsAnyWord(["如果真结束", "如果结束", "结束如果", "否则", "否则如果",
                "判断结束", "结束判断", "结束循环", "循环结束", "结束遍历", "遍历结束",
                "计次循环尾", "变量循环尾", "判断循环尾", "循环判断尾", "遍历循环尾",
                "结束尝试", "尝试结束", "捕获", "结束子程序", "子程序结束",
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

        if (MatchWord("如果真"))
        {
            return ParseIfTrue(token);
        }

        if (MatchWord("判断开始"))
        {
            return ParseBranchChain(token);
        }

        if (MatchWord("如果"))
        {
            return ParseIf(token);
        }

        if (MatchWord("当"))
        {
            return ParseWhile(token);
        }

        if (MatchWord("计次循环首"))
        {
            return ParseCountLoopHead(token);
        }

        if (MatchWord("变量循环首"))
        {
            return ParseVariableLoopHead(token);
        }

        if (MatchWord("判断循环首"))
        {
            return ParseJudgeLoopHead(token);
        }

        if (MatchWord("循环判断首"))
        {
            return ParseLoopJudgeHead(token);
        }

        if (MatchWord("计次循环"))
        {
            return ParseCountLoop(token);
        }

        if (MatchWord("遍历循环首"))
        {
            return ParseForEachHead(token);
        }

        if (MatchWord("遍历"))
        {
            return ParseForEach(token);
        }

        if (MatchWord("返回"))
        {
            return ParseReturn(token);
        }

        if (MatchWord("跳出循环") || MatchWord("跳出"))
        {
            Match(TokenKind.Punctuation, "(");
            Match(TokenKind.Punctuation, ")");
            var statement = new BreakStatement(token.Line, token.Column);
            ConsumeLineEnd();
            return statement;
        }

        if (MatchWord("继续循环") || MatchWord("继续"))
        {
            Match(TokenKind.Punctuation, "(");
            Match(TokenKind.Punctuation, ")");
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
        // 强类型:变量/常量声明必须给出类型。
        if (!Match(TokenKind.Operator, ":"))
        {
            throw Error(Peek(), $"变量“{name.Text}”声明需要类型，例如：变量 {name.Text}: 数字 = 0");
        }

        var declaredType = ParseTypeName("变量类型");
        Expression? initializer = null;
        if (MatchOperator("="))
        {
            initializer = ParseExpression();
        }

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

    /// <summary>返回 ():无返回值;返回 (表达式):带值。老语法两者都用括号。</summary>
    private Statement ParseReturn(Token token)
    {
        Expression? value = null;
        if (!AtLineEnd)
        {
            if (Match(TokenKind.Punctuation, "("))
            {
                if (!Match(TokenKind.Punctuation, ")"))
                {
                    value = ParseExpression();
                    Consume(TokenKind.Punctuation, ")", "返回语句缺少“)”。");
                }
            }
            else
            {
                value = ParseExpression();
            }
        }

        var statement = new ReturnStatement(token.Line, token.Column, value);
        ConsumeLineEnd();
        return statement;
    }

    /// <summary>如果真 (条件):官方单分支条件,以“.如果真结束”收口或按缩进结束。</summary>
    private Statement ParseIfTrue(Token start)
    {
        Expression condition;
        if (Match(TokenKind.Punctuation, "("))
        {
            condition = ParseCondition();
            Consume(TokenKind.Punctuation, ")", "如果真语句缺少“)”。");
        }
        else
        {
            condition = ParseCondition();
        }

        MatchWord("则");
        ConsumeLineEnd();
        var then = ParseBlock(["如果真结束", "结束如果", "如果结束"], start.Column - 1);
        ConsumeEnd("如果真语句", "如果真结束", "结束如果", "如果结束");
        return new IfStatement(start.Line, start.Column, condition, then, null);
    }

    /// <summary>如果 条件 则 … 否则如果 … 否则 … 结束如果(文本风格),以收口行或缩进结束。</summary>
    private Statement ParseIf(Token start)
    {
        var condition = ParseExpression();
        MatchWord("则");
        ConsumeLineEnd();

        var then = ParseBlock(["否则如果", "否则", "结束如果", "如果结束"], start.Column - 1);
        BlockStatement? elseBlock = null;

        if (IsAnyWord(["否则如果", "否则"]))
        {
            var branch = Peek();
            if (MatchWord("否则如果"))
            {
                var nested = ParseIf(branch);
                elseBlock = new BlockStatement(branch.Line, branch.Column, [nested]);
            }
            else
            {
                Advance();
                ConsumeLineEnd();
                elseBlock = ParseBlock(["结束如果", "如果结束"], branch.Column - 1);
                ConsumeEnd("如果语句", "结束如果", "如果结束");
            }
        }
        else
        {
            ConsumeEnd("如果语句", "结束如果", "如果结束");
        }

        return new IfStatement(start.Line, start.Column, condition, then, elseBlock);
    }

    /// <summary>判断开始 (条件) / 判断 (条件) / 默认:官方多分支链。</summary>
    private Statement ParseBranchChain(Token start)
    {
        var condition = ParseBranchCondition("判断开始");
        MatchWord("则");
        ConsumeLineEnd();
        var then = ParseBlock(["判断", "默认", "结束判断", "判断结束"], start.Column - 1);
        BlockStatement? elseBlock = null;

        if (MatchWord("判断"))
        {
            var branch = Peek(-1);
            var nested = ParseBranchTail(branch);
            elseBlock = new BlockStatement(branch.Line, branch.Column, [nested]);
        }
        else if (MatchWord("默认"))
        {
            ConsumeLineEnd();
            elseBlock = ParseBlock(["结束判断", "判断结束"], start.Column - 1);
            ConsumeEnd("默认分支", "结束判断", "判断结束");
        }
        else
        {
            ConsumeEnd("判断语句", "结束判断", "判断结束");
        }

        return new IfStatement(start.Line, start.Column, condition, then, elseBlock);
    }

    private Statement ParseBranchTail(Token start)
    {
        if (MatchWord("默认"))
        {
            ConsumeLineEnd();
            var body = ParseBlock(["结束判断", "判断结束"], start.Column - 1);
            ConsumeEnd("默认分支", "结束判断", "判断结束");
            return new IfStatement(
                start.Line,
                start.Column,
                new LiteralExpression(start.Line, start.Column, true),
                body,
                null);
        }

        var condition = ParseBranchCondition("判断");
        MatchWord("则");
        ConsumeLineEnd();
        var then = ParseBlock(["判断", "默认", "结束判断", "判断结束"], start.Column - 1);
        BlockStatement? elseBlock = null;
        if (MatchWord("判断"))
        {
            var branch = Peek(-1);
            var nested = ParseBranchTail(branch);
            elseBlock = new BlockStatement(branch.Line, branch.Column, [nested]);
        }
        else if (MatchWord("默认"))
        {
            ConsumeLineEnd();
            elseBlock = ParseBlock(["结束判断", "判断结束"], start.Column - 1);
            ConsumeEnd("默认分支", "结束判断", "判断结束");
        }
        else
        {
            ConsumeEnd("判断语句", "结束判断", "判断结束");
        }

        return new IfStatement(start.Line, start.Column, condition, then, elseBlock);
    }

    private Expression ParseBranchCondition(string construct)
    {
        if (Match(TokenKind.Punctuation, "("))
        {
            var condition = ParseCondition();
            Consume(TokenKind.Punctuation, ")", $"{construct}缺少“)”。");
            return condition;
        }

        return ParseCondition();
    }

    /// <summary>当 条件 循环:先判断后执行。</summary>
    private Statement ParseWhile(Token start)
    {
        var condition = ParseCondition();
        MatchWord("循环");
        ConsumeLineEnd();
        var body = ParseBlock(["结束循环", "循环结束", "判断循环尾"], start.Column - 1);
        ConsumeEnd("当循环", "结束循环", "循环结束");
        return new WhileStatement(start.Line, start.Column, condition, body);
    }

    /// <summary>计次循环 变量 从 起 到 止 [步长 …]:新语汇计次循环。</summary>
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
        var body = ParseBlock(["结束循环", "循环结束", "计次循环尾"], start.Column - 1);
        ConsumeEnd("计次循环", "结束循环", "循环结束");
        return new CountLoopStatement(start.Line, start.Column, variable.Text, from, to, step, body);
    }

    /// <summary>遍历 变量 于 集合:新语汇遍历循环。</summary>
    private Statement ParseForEach(Token start)
    {
        var variable = ConsumeIdentifier("遍历变量");
        MatchWord("于");
        var collection = ParseExpression();
        MatchWord("循环");
        ConsumeLineEnd();
        var body = ParseBlock(["结束遍历", "遍历结束", "结束循环", "循环结束", "遍历循环尾"], start.Column - 1);

        if (IsAnyWord(["结束遍历", "遍历结束"]))
        {
            ConsumeEnd("遍历语句", "结束遍历", "遍历结束");
        }
        else
        {
            ConsumeEnd("遍历语句", "结束循环", "循环结束");
        }

        return new ForEachStatement(start.Line, start.Column, variable.Text, collection, body);
    }

    /// <summary>老语法:.计次循环首 (循环次数, 计次变量) … .计次循环尾 (),计次从 1 到次数。</summary>
    private Statement ParseCountLoopHead(Token start)
    {
        Consume(TokenKind.Punctuation, "(", "计次循环首需要“(循环次数, 计次变量)”参数");
        var count = ParseExpression();
        Consume(TokenKind.Punctuation, ",", "计次循环首需要两个参数");
        var variable = ConsumeIdentifier("计次变量");
        Consume(TokenKind.Punctuation, ")", "计次循环首缺少右括号");
        ConsumeLineEnd();
        var body = ParseBlock(["计次循环尾", "结束循环", "循环结束"], start.Column - 1);
        ConsumeEnd("计次循环", "计次循环尾", "结束循环", "循环结束");
        return new CountLoopStatement(
            start.Line,
            start.Column,
            variable.Text,
            new LiteralExpression(start.Line, start.Column, 1d),
            count,
            new LiteralExpression(start.Line, start.Column, 1d),
            body);
    }

    /// <summary>老语法:.变量循环首 (变量, 起始, 结束[, 步长]) … .变量循环尾 ()。</summary>
    private Statement ParseVariableLoopHead(Token start)
    {
        var variable = ConsumeLoopArgument("变量循环首");
        Consume(TokenKind.Punctuation, ",", "变量循环首需要“(变量, 起始, 结束[, 步长])”参数");
        var from = ParseExpression();
        Consume(TokenKind.Punctuation, ",", "变量循环首缺少结束值");
        var to = ParseExpression();
        Expression step = new LiteralExpression(start.Line, start.Column, 1d);
        if (Match(TokenKind.Punctuation, ","))
        {
            step = ParseExpression();
        }

        Consume(TokenKind.Punctuation, ")", "变量循环首缺少右括号");
        ConsumeLineEnd();
        var body = ParseBlock(["变量循环尾", "结束循环", "循环结束"], start.Column - 1);
        ConsumeEnd("变量循环", "变量循环尾", "结束循环", "循环结束");
        return new CountLoopStatement(start.Line, start.Column, variable.Text, from, to, step, body);
    }

    /// <summary>老语法:.判断循环首 (条件) … .判断循环尾 (),先判断后执行。</summary>
    private Statement ParseJudgeLoopHead(Token start)
    {
        Expression condition;
        if (Match(TokenKind.Punctuation, "("))
        {
            condition = ParseCondition();
            Consume(TokenKind.Punctuation, ")", "判断循环首缺少右括号");
        }
        else
        {
            condition = ParseCondition();
        }

        ConsumeLineEnd();
        var body = ParseBlock(["判断循环尾", "结束循环", "循环结束"], start.Column - 1);
        ConsumeEnd("判断循环", "判断循环尾", "结束循环", "循环结束");
        return new WhileStatement(start.Line, start.Column, condition, body);
    }

    /// <summary>
    /// 老语法:.循环判断首 () … .循环判断尾 (条件),先执行后判断。
    /// 展开为“当 真 循环”,循环尾判断条件,内部“继续”先测条件再决定去留。
    /// </summary>
    private Statement ParseLoopJudgeHead(Token start)
    {
        if (Match(TokenKind.Punctuation, "("))
        {
            Match(TokenKind.Punctuation, ")");
        }

        ConsumeLineEnd();
        var body = ParseBlock(["循环判断尾"], start.Column - 1);
        if (!MatchWord("循环判断尾"))
        {
            throw Error(Peek(), "循环判断首需要“循环判断尾 (条件)”收口。");
        }

        Expression condition;
        if (Match(TokenKind.Punctuation, "("))
        {
            condition = ParseCondition();
            Consume(TokenKind.Punctuation, ")", "循环判断尾缺少右括号");
        }
        else
        {
            condition = ParseCondition();
        }

        ConsumeLineEnd();
        var statements = RewriteLoopContinues(body.Statements, condition).ToList();
        statements.Add(new IfStatement(
            start.Line,
            start.Column,
            new UnaryExpression(start.Line, start.Column, "!", condition),
            new BlockStatement(start.Line, start.Column, [new BreakStatement(start.Line, start.Column)]),
            null));
        return new WhileStatement(
            start.Line,
            start.Column,
            new LiteralExpression(start.Line, start.Column, true),
            new BlockStatement(start.Line, start.Column, statements));
    }

    /// <summary>老语法:.遍历循环首 (变量, 集合) … .遍历循环尾 ()。</summary>
    private Statement ParseForEachHead(Token start)
    {
        var variable = ConsumeLoopArgument("遍历循环首");
        Consume(TokenKind.Punctuation, ",", "遍历循环首需要“(变量, 集合)”参数");
        var collection = ParseExpression();
        Consume(TokenKind.Punctuation, ")", "遍历循环首缺少右括号");
        ConsumeLineEnd();
        var body = ParseBlock(["遍历循环尾", "结束遍历", "遍历结束"], start.Column - 1);
        ConsumeEnd("遍历循环", "遍历循环尾", "结束遍历", "遍历结束");
        return new ForEachStatement(start.Line, start.Column, variable.Text, collection, body);
    }

    private Token ConsumeLoopArgument(string construct)
    {
        Consume(TokenKind.Punctuation, "(", $"{construct}需要左括号");
        return ConsumeIdentifier("循环变量");
    }

    /// <summary>
    /// 循环判断尾改写:内部“继续”改为先测条件(成立继续,否则退出),
    /// 保持“先执行后判断”循环的语义。
    /// </summary>
    private static IEnumerable<Statement> RewriteLoopContinues(
        IEnumerable<Statement> statements,
        Expression condition)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ContinueStatement continueStatement:
                    yield return new IfStatement(
                        continueStatement.Line,
                        continueStatement.Column,
                        condition,
                        new BlockStatement(
                            continueStatement.Line,
                            continueStatement.Column,
                            [new ContinueStatement(continueStatement.Line, continueStatement.Column)]),
                        new BlockStatement(
                            continueStatement.Line,
                            continueStatement.Column,
                            [new BreakStatement(continueStatement.Line, continueStatement.Column)]));
                    break;
                case WhileStatement or CountLoopStatement or ForEachStatement:
                    yield return statement;
                    break;
                case IfStatement conditional:
                    yield return conditional with
                    {
                        Then = new BlockStatement(
                            conditional.Then.Line,
                            conditional.Then.Column,
                            RewriteLoopContinues(conditional.Then.Statements, condition).ToList()),
                        Else = conditional.Else is null
                            ? null
                            : new BlockStatement(
                                conditional.Else.Line,
                                conditional.Else.Column,
                                RewriteLoopContinues(conditional.Else.Statements, condition).ToList())
                    };
                    break;
                case TryStatement guarded:
                    yield return guarded with
                    {
                        TryBlock = new BlockStatement(
                            guarded.TryBlock.Line,
                            guarded.TryBlock.Column,
                            RewriteLoopContinues(guarded.TryBlock.Statements, condition).ToList()),
                        CatchBlock = guarded.CatchBlock is null
                            ? null
                            : new BlockStatement(
                                guarded.CatchBlock.Line,
                                guarded.CatchBlock.Column,
                                RewriteLoopContinues(guarded.CatchBlock.Statements, condition).ToList())
                    };
                    break;
                default:
                    yield return statement;
                    break;
            }
        }
    }

    private Statement ParseTry(Token start)
    {
        ConsumeLineEnd();
        var tryBlock = ParseBlock(["捕获", "结束尝试", "尝试结束"], start.Column - 1);
        string? errorVariable = null;
        BlockStatement? catchBlock = null;

        if (MatchWord("捕获"))
        {
            if (Check(TokenKind.Identifier))
            {
                errorVariable = Advance().Text;
            }

            ConsumeLineEnd();
            catchBlock = ParseBlock(["结束尝试", "尝试结束"], start.Column - 1);
        }

        ConsumeEnd("尝试语句", "结束尝试", "尝试结束");
        return new TryStatement(start.Line, start.Column, tryBlock, errorVariable, catchBlock);
    }
}
