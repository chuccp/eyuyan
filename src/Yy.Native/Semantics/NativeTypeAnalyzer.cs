using Yy.Language.Syntax;

namespace Yy.Native.Semantics;

/// <summary>
/// 平台无关的原生类型推导。所有后端共享这一份语义规则；
/// 平台特有的界面成员通过 <paramref name="platformInfer"/> 注入，
/// 不在通用规则中引入平台分支。
/// </summary>
internal sealed class NativeTypeAnalyzer(
    IReadOnlyDictionary<string, FunctionDeclaration> functions,
    IReadOnlySet<string> declaredObjectTypes,
    string sourceName,
    Func<Expression, NativeContext, NativeValueKind?>? platformInfer = null)
{
    private readonly IReadOnlyDictionary<string, FunctionDeclaration> _functions = functions;
    private readonly IReadOnlySet<string> _declaredObjectTypes = declaredObjectTypes;
    private readonly string _sourceName = sourceName;
    private readonly Func<Expression, NativeContext, NativeValueKind?>? _platformInfer = platformInfer;

    public NativeValueKind Infer(Expression expression, NativeContext context)
    {
        // 平台钩子只补充界面成员；对象字段和普通标识符仍按共享规则优先。
        if (_platformInfer is not null &&
            !(expression is MemberExpression memberExpression &&
              TryGetObjectField(memberExpression, context) is not null) &&
            _platformInfer(expression, context) is { } platformKind)
        {
            return platformKind;
        }

        return expression switch
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
            UnaryExpression { Operator: "非" or "!" } => NativeValueKind.Boolean,
            UnaryExpression => NativeValueKind.Integer,
            BinaryExpression binary when binary.Operator is
                "==" or "!=" or "<" or "<=" or ">" or ">=" or "且" or "&&" or "或" or "||" =>
                NativeValueKind.Boolean,
            BinaryExpression binary => InferBinaryKind(binary, context),
            CallExpression { Callee: IdentifierExpression identifier } when
                NativeBuiltinCatalog.TryGetResultKind(identifier.Name) is { } builtinKind =>
                builtinKind,
            CallExpression { Callee: IdentifierExpression identifier } when
                _functions.TryGetValue(identifier.Name, out var function) =>
                FunctionKind(function),
            CallExpression => NativeValueKind.Integer,
            _ => Fail(expression, "无法推断原生表达式类型")
        };
    }

    public NativeValueKind InferArrayElement(
        Expression expression,
        NativeContext context)
    {
        if (expression is ArrayExpression array)
        {
            if (array.Items.Count == 0)
            {
                return NativeValueKind.Integer;
            }

            var kind = Infer(array.Items[0], context);
            if (array.Items.Skip(1).Any(item => Infer(item, context) != kind))
            {
                Fail(array, "原生数组当前要求所有元素类型一致。");
            }

            return kind;
        }

        if (expression is IdentifierExpression identifier &&
            context.ArrayElementTypes.TryGetValue(identifier.Name, out var elementKind))
        {
            return elementKind;
        }

        if (expression is IndexExpression index &&
            index.Target is IdentifierExpression indexIdentifier &&
            context.ArrayElementTypes.TryGetValue(indexIdentifier.Name, out var indexedKind))
        {
            return indexedKind;
        }

        if (expression is MemberExpression member &&
            TryGetObjectField(member, context) is
            {
                Kind: NativeValueKind.Array
            } field)
        {
            return field.ArrayElementKind;
        }

        if (expression is CallExpression
            {
                Callee: IdentifierExpression { Name: "取目录文件" }
            })
        {
            return NativeValueKind.Text;
        }

        return NativeValueKind.Integer;
    }

    public NativeValueKind FunctionKind(FunctionDeclaration function) =>
        function.ReturnType switch
        {
            "文本" => NativeValueKind.Text,
            "逻辑" => NativeValueKind.Boolean,
            "数组" => NativeValueKind.Array,
            "对象" => NativeValueKind.Object,
            _ => NativeValueKind.Integer
        };

    public NativeValueKind ParseType(string? typeName, YyNode node) =>
        typeName switch
        {
            null or "" or "数字" => NativeValueKind.Integer,
            "文本" => NativeValueKind.Text,
            "逻辑" => NativeValueKind.Boolean,
            "数组" => NativeValueKind.Array,
            "对象" => NativeValueKind.Object,
            not null when _declaredObjectTypes.Contains(typeName) => NativeValueKind.Object,
            _ => Fail(node, $"原生后端暂不支持类型“{typeName}”")
        };

    public NativeObjectField? TryGetObjectField(
        MemberExpression member,
        NativeContext context) =>
        TryGetObjectShape(member.Target, context)?.Find(member.Member);

    public NativeObjectShape? TryGetObjectShape(
        Expression expression,
        NativeContext context)
    {
        if (expression is IdentifierExpression identifier &&
            context.ObjectShapes.TryGetValue(identifier.Name, out var shape))
        {
            return shape;
        }

        if (expression is ObjectExpression objectExpression)
        {
            var fields = new List<NativeObjectField>();
            foreach (var (name, value) in objectExpression.Properties)
            {
                var kind = Infer(value, context);
                fields.Add(new NativeObjectField(
                    name,
                    kind,
                    kind == NativeValueKind.Array
                        ? InferArrayElement(value, context)
                        : NativeValueKind.Integer));
            }

            return new NativeObjectShape(fields);
        }

        return null;
    }

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

    private NativeValueKind Fail(YyNode node, string message) =>
        throw new NativeCompileException(
            message,
            node.Line,
            node.Column,
            _sourceName);
}
