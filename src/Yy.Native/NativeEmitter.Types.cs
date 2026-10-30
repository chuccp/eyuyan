using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private NativeValueKind Infer(Expression expression, NativeContext context) =>
        expression switch
        {
            LiteralExpression { Value: string } => NativeValueKind.Text,
            LiteralExpression { Value: bool } => NativeValueKind.Boolean,
            LiteralExpression { Value: double } => NativeValueKind.Integer,
            ArrayExpression => NativeValueKind.Array,
            ObjectExpression => NativeValueKind.Object,
            IndexExpression index => InferArrayElement(index, context),
            MemberExpression member when TryGetObjectField(member, context) is { } field =>
                field.Kind,
            IdentifierExpression identifier when context.Types.TryGetValue(identifier.Name, out var kind) =>
                kind,
            MemberExpression
            {
                Member: "文本" or "标题",
                Target: CallExpression
                {
                    Callee: IdentifierExpression { Name: "取控件" }
                }
            } => NativeValueKind.Text,
            UnaryExpression { Operator: "非" or "!" } => NativeValueKind.Boolean,
            UnaryExpression => NativeValueKind.Integer,
            BinaryExpression binary when binary.Operator is
                "==" or "!=" or "<" or "<=" or ">" or ">=" or "且" or "&&" or "或" or "||" =>
                NativeValueKind.Boolean,
            BinaryExpression binary => InferBinaryKind(binary, context),
            CallExpression { Callee: IdentifierExpression { Name: "长度" } } =>
                NativeValueKind.Integer,
            CallExpression { Callee: IdentifierExpression { Name: "追加" } } =>
                NativeValueKind.Array,
            CallExpression { Callee: IdentifierExpression { Name: "文件存在" or "删除文件" or "写文件" } } =>
                NativeValueKind.Boolean,
            CallExpression { Callee: IdentifierExpression { Name: "读文件" } } =>
                NativeValueKind.Text,
            CallExpression { Callee: IdentifierExpression { Name: "取目录文件" } } =>
                NativeValueKind.Array,
            CallExpression { Callee: IdentifierExpression { Name: "数字" } } =>
                NativeValueKind.Integer,
            CallExpression { Callee: IdentifierExpression { Name: "文本" } } =>
                NativeValueKind.Text,
            CallExpression { Callee: IdentifierExpression { Name: "运行程序" } } =>
                NativeValueKind.Integer,
            CallExpression { Callee: IdentifierExpression identifier } when
                _functions.TryGetValue(identifier.Name, out var function) =>
                FunctionKind(function),
            CallExpression => NativeValueKind.Integer,
            _ => FailKind(expression)
        };

    private NativeValueKind InferBinaryKind(
        BinaryExpression binary,
        NativeContext context)
    {
        if (binary.Operator == "+" &&
            Infer(binary.Left, context) == NativeValueKind.Text &&
            Infer(binary.Right, context) == NativeValueKind.Text)
        {
            return NativeValueKind.Text;
        }

        return NativeValueKind.Integer;
    }

    private NativeValueKind FunctionKind(FunctionDeclaration function) =>
        function.ReturnType switch
        {
            "文本" => NativeValueKind.Text,
            "逻辑" => NativeValueKind.Boolean,
            "数组" => NativeValueKind.Array,
            "对象" => NativeValueKind.Object,
            _ => NativeValueKind.Integer
        };

    private NativeValueKind ParseType(string? typeName, YyNode node) =>
        typeName switch
        {
            null or "" or "数字" => NativeValueKind.Integer,
            "文本" => NativeValueKind.Text,
            "逻辑" => NativeValueKind.Boolean,
            "数组" => NativeValueKind.Array,
            "对象" => NativeValueKind.Object,
            not null when _declaredObjectTypes.Contains(typeName) => NativeValueKind.Object,
            _ => FailKind(node, $"原生后端暂不支持类型“{typeName}”")
        };

    private NativeValueKind FailKind(YyNode node, string? message = null) =>
        throw new NativeCompileException(
            message ?? "无法推断原生表达式类型",
            node.Line,
            node.Column,
            _sourceName);
}
