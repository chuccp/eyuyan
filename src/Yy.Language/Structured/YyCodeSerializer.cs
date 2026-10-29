using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Yy.Language.Structured;

public static class YyCodeSerializer
{
    private static readonly XNamespace Namespace = "urn:yy-studio:code:1";

    public static string Serialize(YyCodeDocument document)
    {
        var root = new XElement(
            Namespace + "代码",
            new XAttribute(XNamespace.Xmlns + "yy", Namespace),
            new XAttribute("格式版本", 1),
            new XAttribute("名称", document.Name));
        foreach (var import in document.Imports)
        {
            root.Add(new XElement(Namespace + "引入", new XAttribute("路径", import)));
        }

        foreach (var function in document.Functions)
        {
            var functionElement = new XElement(
                Namespace + "子程序",
                new XAttribute("名称", function.Name));
            if (!string.IsNullOrWhiteSpace(function.ReturnType))
            {
                functionElement.SetAttributeValue("返回类型", function.ReturnType);
            }

            foreach (var parameter in function.Parameters)
            {
                var parameterElement = new XElement(
                    Namespace + "参数",
                    new XAttribute("名称", parameter.Name));
                if (!string.IsNullOrWhiteSpace(parameter.TypeName))
                {
                    parameterElement.SetAttributeValue("类型", parameter.TypeName);
                }

                if (!string.IsNullOrWhiteSpace(parameter.DefaultExpression))
                {
                    parameterElement.SetAttributeValue("默认值", parameter.DefaultExpression);
                }

                functionElement.Add(parameterElement);
            }

            foreach (var statement in function.Statements)
            {
                functionElement.Add(SerializeStatement(statement));
            }

            root.Add(functionElement);
        }

        if (document.MainStatements.Count > 0)
        {
            var entry = new XElement(Namespace + "入口");
            foreach (var statement in document.MainStatements)
            {
                entry.Add(SerializeStatement(statement));
            }

            root.Add(entry);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root)
            .ToString(SaveOptions.None);
    }

    public static YyCodeDocument Deserialize(string text)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new YyCodeFormatException(exception.Message, exception.LineNumber, exception.LinePosition);
        }

        var root = xml.Root;
        if (root is null || root.Name.LocalName != "代码")
        {
            throw new YyCodeFormatException("代码文件的根元素必须是“代码”。", 1, 1);
        }

        var version = Attribute(root, "格式版本", "1");
        if (!int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var versionNumber) ||
            versionNumber != 1)
        {
            throw Error(root, $"不支持的代码格式版本“{version}”。");
        }

        var document = new YyCodeDocument
        {
            Name = RequiredAttribute(root, "名称")
        };
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "引入":
                    document.Imports.Add(RequiredAttribute(element, "路径"));
                    break;
                case "子程序":
                    document.Functions.Add(ParseFunction(element));
                    break;
                case "入口":
                    ParseStatements(element, document.MainStatements);
                    break;
                default:
                    throw Error(element, $"不支持的代码元素“{element.Name.LocalName}”。");
            }
        }

        return document;
    }

    private static XElement SerializeStatement(YyCodeStatement statement)
    {
        var element = new XElement(Namespace + statement.Kind);
        switch (statement.Kind)
        {
            case YyCodeStatementKinds.Variable:
            case YyCodeStatementKinds.Constant:
                element.SetAttributeValue("名称", statement.Name);
                if (!string.IsNullOrWhiteSpace(statement.TypeName))
                {
                    element.SetAttributeValue("类型", statement.TypeName);
                }

                element.SetAttributeValue("值", statement.Expression);
                break;
            case YyCodeStatementKinds.Assign:
                element.SetAttributeValue("目标", statement.Target);
                element.SetAttributeValue("运算符", statement.Operator);
                element.SetAttributeValue("值", statement.Expression);
                break;
            case YyCodeStatementKinds.Call:
            case YyCodeStatementKinds.Return:
                element.SetAttributeValue("值", statement.Expression);
                break;
            case YyCodeStatementKinds.If:
            case YyCodeStatementKinds.While:
                element.SetAttributeValue("条件", statement.Condition);
                break;
            case YyCodeStatementKinds.CountLoop:
                element.SetAttributeValue("变量", statement.Variable);
                element.SetAttributeValue("从", statement.From);
                element.SetAttributeValue("到", statement.To);
                element.SetAttributeValue("步长", statement.Step);
                break;
            case YyCodeStatementKinds.ForEach:
                element.SetAttributeValue("变量", statement.Variable);
                element.SetAttributeValue("值", statement.Expression);
                break;
            case YyCodeStatementKinds.Try:
                element.SetAttributeValue("捕获", statement.CatchName);
                break;
        }

        foreach (var child in statement.Children)
        {
            element.Add(SerializeStatement(child));
        }

        if (statement.ElseChildren.Count > 0)
        {
            var elseElement = new XElement(Namespace + "否则");
            foreach (var child in statement.ElseChildren)
            {
                elseElement.Add(SerializeStatement(child));
            }

            element.Add(elseElement);
        }

        return element;
    }

    private static YyCodeFunction ParseFunction(XElement element)
    {
        var function = new YyCodeFunction
        {
            Name = RequiredAttribute(element, "名称"),
            ReturnType = Attribute(element, "返回类型", string.Empty)
        };
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "参数")
            {
                function.Parameters.Add(new YyCodeParameter
                {
                    Name = RequiredAttribute(child, "名称"),
                    TypeName = Attribute(child, "类型", string.Empty),
                    DefaultExpression = Attribute(child, "默认值", string.Empty)
                });
            }
            else
            {
                function.Statements.Add(ParseStatement(child));
            }
        }

        return function;
    }

    private static void ParseStatements(XElement parent, List<YyCodeStatement> target)
    {
        foreach (var element in parent.Elements())
        {
            if (element.Name.LocalName == "否则")
            {
                throw Error(element, "“否则”只能出现在条件步骤内部。");
            }

            target.Add(ParseStatement(element));
        }
    }

    private static YyCodeStatement ParseStatement(XElement element)
    {
        var kind = element.Name.LocalName;
        if (!YyCodeStatementKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            throw Error(element, $"不支持的代码步骤“{kind}”。");
        }

        var statement = new YyCodeStatement
        {
            Kind = kind,
            Name = Attribute(element, "名称", string.Empty),
            TypeName = Attribute(element, "类型", string.Empty),
            Target = Attribute(element, "目标", string.Empty),
            Operator = Attribute(element, "运算符", "="),
            Expression = Attribute(element, "值", string.Empty),
            Condition = Attribute(element, "条件", "真"),
            Variable = Attribute(element, "变量", string.Empty),
            From = Attribute(element, "从", "1"),
            To = Attribute(element, "到", "10"),
            Step = Attribute(element, "步长", "1"),
            CatchName = Attribute(element, "捕获", "错误")
        };

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "否则" &&
                kind is YyCodeStatementKinds.If or YyCodeStatementKinds.Try)
            {
                ParseStatements(child, statement.ElseChildren);
            }
            else if (YyCodeStatementKinds.IsContainer(kind))
            {
                statement.Children.Add(ParseStatement(child));
            }
            else
            {
                throw Error(child, $"步骤“{kind}”不能包含子步骤。");
            }
        }

        return statement;
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = Attribute(element, name, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(element, $"元素“{element.Name.LocalName}”缺少“{name}”属性。");
        }

        return value;
    }

    private static string Attribute(XElement element, string name, string defaultValue) =>
        element.Attribute(name)?.Value ?? defaultValue;

    private static YyCodeFormatException Error(XElement element, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return new YyCodeFormatException(
            message,
            lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
    }
}

public sealed class YyCodeFormatException(string message, int line, int column) : Exception(message)
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
}
