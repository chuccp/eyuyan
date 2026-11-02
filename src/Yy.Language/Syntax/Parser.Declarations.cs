namespace Yy.Language.Syntax;

public sealed partial class Parser
{
    private StructDeclaration ParseStruct()
    {
        var keyword = Advance();
        var name = ConsumeIdentifier("结构体名称");
        var interfaces = new List<string>();
        if (MatchWord("实现"))
        {
            do
            {
                interfaces.Add(ConsumeIdentifier("接口名称").Text);
            }
            while (Match(TokenKind.Punctuation, ","));
        }

        ConsumeLineEnd();
        var fields = new List<StructFieldDeclaration>();
        while (!IsAtEnd && !IsAnyWord(["结束结构体", "结构体结束", "结束记录", "记录结束"]))
        {
            SkipNewLines();
            if (IsAnyWord(["结束结构体", "结构体结束", "结束记录", "记录结束"]))
            {
                break;
            }

            var fieldName = ConsumeIdentifier("字段名称");
            Consume(TokenKind.Operator, ":", $"字段“{fieldName.Text}”后需要冒号");
            var fieldType = ParseTypeName("字段类型");
            Expression? defaultValue = null;
            if (MatchOperator("="))
            {
                defaultValue = ParseExpression();
            }

            fields.Add(new StructFieldDeclaration(
                fieldName.Text,
                fieldType,
                defaultValue));
            ConsumeLineEnd();
        }

        if (!IsAnyWord(["结束结构体", "结构体结束", "结束记录", "记录结束"]))
        {
            throw Error(Peek(), $"结构体“{name.Text}”缺少结束标记。");
        }

        ConsumeLine();
        return new StructDeclaration(keyword.Line, keyword.Column, name.Text, fields, interfaces);
    }

    private InterfaceDeclaration ParseInterface()
    {
        var keyword = Advance();
        var name = ConsumeIdentifier("接口名称");
        ConsumeLineEnd();
        var methods = new List<InterfaceMethodDeclaration>();
        while (!IsAtEnd && !IsAnyWord(["结束接口", "接口结束"]))
        {
            SkipNewLines();
            if (IsAnyWord(["结束接口", "接口结束"]))
            {
                break;
            }

            if (!MatchWord("子程序") && !MatchWord("函数"))
            {
                throw Error(Peek(), "接口成员必须是子程序声明。");
            }

            var methodName = ConsumeIdentifier("接口方法名");
            Consume(TokenKind.Punctuation, "(", "接口方法名后需要左括号");
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

                    parameters.Add(new ParameterDeclaration(parameterName.Text, null, parameterType));
                }
                while (Match(TokenKind.Punctuation, ","));
            }

            Consume(TokenKind.Punctuation, ")", "接口方法参数列表缺少右括号");
            string? returnType = null;
            if (Match(TokenKind.Operator, ":"))
            {
                returnType = ParseTypeName("返回值类型");
            }

            ConsumeLineEnd();
            methods.Add(new InterfaceMethodDeclaration(methodName.Text, parameters, returnType));
        }

        ConsumeEnd("接口", "结束接口", "接口结束");
        return new InterfaceDeclaration(keyword.Line, keyword.Column, name.Text, methods);
    }

    /// <summary>
    /// 子程序声明。rowForm 为老语法声明行:
    /// <c>.子程序 名[, 返回值类型[, 备注[, *]]]</c>,其后跟 .参数/.局部变量 行,
    /// 语句体到下一个声明行或文件尾结束;否则为新语汇
    /// <c>子程序 名(参数: 类型): 返回类型</c>,块按缩进或“结束子程序”结束。
    /// </summary>
    private FunctionDeclaration ParseFunction(bool rowForm)
    {
        if (Check(TokenKind.Punctuation, "."))
        {
            Advance();
        }

        var keyword = Advance();
        var name = ConsumeIdentifier("子程序名");
        var parameters = new List<ParameterDeclaration>();
        var locals = new List<Statement>();
        string? returnType = null;

        if (rowForm)
        {
            if (Match(TokenKind.Punctuation, ","))
            {
                if (Check(TokenKind.Identifier))
                {
                    returnType = ParseTypeName("返回值类型");
                }

                if (Match(TokenKind.Punctuation, ","))
                {
                    if (Check(TokenKind.String))
                    {
                        Advance();
                    }

                    if (Match(TokenKind.Punctuation, ","))
                    {
                        // 标志列(* 表示公开导出)。
                        if (Check(TokenKind.Operator, "*") || Check(TokenKind.Identifier))
                        {
                            Advance();
                        }
                    }
                }
            }

            ConsumeLineEnd();
            while (AtDataRow("参数") || AtDataRow("局部变量"))
            {
                Advance();
                var rowKeyword = Advance();
                var memberName = ConsumeIdentifier("名称");
                var memberType = "任意";
                var isArray = false;
                if (Match(TokenKind.Punctuation, ","))
                {
                    if (Check(TokenKind.Identifier))
                    {
                        memberType = ParseTypeName("类型");
                    }

                    if (Match(TokenKind.Punctuation, ","))
                    {
                        if (Check(TokenKind.String))
                        {
                            Advance();
                        }

                        if (Match(TokenKind.Punctuation, ","))
                        {
                            if (Check(TokenKind.String))
                            {
                                isArray = string.Equals(Peek().Text, "0", StringComparison.Ordinal);
                                Advance();
                            }
                        }
                    }
                }

                ConsumeLineEnd();
                if (rowKeyword.IsWord("参数"))
                {
                    parameters.Add(new ParameterDeclaration(
                        memberName.Text,
                        null,
                        isArray ? "数组" : memberType));
                }
                else
                {
                    locals.Add(new VariableStatement(
                        rowKeyword.Line,
                        rowKeyword.Column,
                        memberName.Text,
                        null,
                        false,
                        isArray ? "数组" : memberType));
                }
            }

            var statements = new List<Statement>(locals);
            while (!IsAtEnd && !IsDeclarationRowAhead())
            {
                statements.Add(ParseStatement());
            }

            return new FunctionDeclaration(
                keyword.Line,
                keyword.Column,
                name.Text,
                parameters,
                new BlockStatement(keyword.Line, keyword.Column, statements),
                returnType,
                _sourceName);
        }

        Consume(TokenKind.Punctuation, "(", "子程序名后需要左括号");
        if (!Check(TokenKind.Punctuation, ")"))
        {
            do
            {
                var parameterName = ConsumeIdentifier("参数名");
                // 强类型:参数声明必须给出类型。
                if (!Match(TokenKind.Operator, ":"))
                {
                    throw Error(
                        Peek(),
                        $"参数“{parameterName.Text}”声明需要类型，例如：{parameterName.Text}: 数字");
                }

                var parameterType = ParseTypeName("参数类型");
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
        if (Match(TokenKind.Operator, ":"))
        {
            returnType = ParseTypeName("返回值类型");
        }

        ConsumeLineEnd();
        var body = ParseBlock(
            ["结束子程序", "子程序结束", "结束函数", "函数结束"],
            keyword.Column - 1);

        // 缩进已能判定子程序结束;显式“结束子程序”仅在出现时消费(老语法)。
        if (!IsAtEnd && IsAnyWord(["结束子程序", "子程序结束", "结束函数", "函数结束"]))
        {
            ConsumeLine();
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

    /// <summary>老语法数据声明行:.程序集变量 名, 类型[, 备注[, "0"]] 与 .常量 名, 类型, 值。</summary>
    private Statement ParseDataRow()
    {
        Advance();
        var keyword = Advance();
        var isConstant = keyword.IsWord("常量");
        var name = ConsumeIdentifier("名称");
        var declaredType = "任意";
        Expression? value = null;
        var isArray = false;

        if (Match(TokenKind.Punctuation, ","))
        {
            if (Check(TokenKind.Identifier))
            {
                declaredType = ParseTypeName("类型");
            }

            if (Match(TokenKind.Punctuation, ","))
            {
                if (isConstant && !AtLineEnd && !Check(TokenKind.Punctuation, ","))
                {
                    value = ParseExpression();
                }
                else if (Check(TokenKind.String))
                {
                    Advance();
                }

                if (Match(TokenKind.Punctuation, ","))
                {
                    if (Check(TokenKind.String))
                    {
                        isArray = string.Equals(Peek().Text, "0", StringComparison.Ordinal);
                        Advance();
                    }
                }
            }
        }

        ConsumeLineEnd();
        var storedName = isConstant && !name.Text.StartsWith('#')
            ? "#" + name.Text
            : name.Text;
        return new VariableStatement(
            keyword.Line,
            keyword.Column,
            storedName,
            value,
            isConstant,
            isArray ? "数组" : declaredType);
    }

    private string ParseTypeName(string description)
    {
        var token = ConsumeIdentifier(description);
        if (YyTypeSystem.IsKnown(token.Text))
        {
            return YyTypeSystem.CanonicalName(token.Text);
        }

        if (_structs.Any(item => string.Equals(item.Name, token.Text, StringComparison.Ordinal)) ||
            _interfaces.Any(item => string.Equals(item.Name, token.Text, StringComparison.Ordinal)))
        {
            return token.Text;
        }

        // 老语法:支持库类型(如“字体”“页面接口”)按自定义类型名接受。
        return token.Text;
    }
}
