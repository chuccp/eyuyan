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
            fields.Add(new StructFieldDeclaration(fieldName.Text, ParseTypeName("字段类型")));
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

        ConsumeEnd("结束接口", "接口结束", "接口");
        return new InterfaceDeclaration(keyword.Line, keyword.Column, name.Text, methods);
    }
}
