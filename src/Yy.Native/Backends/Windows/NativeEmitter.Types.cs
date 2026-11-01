using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private NativeValueKind Infer(Expression expression, NativeContext context) =>
        _semantics.Infer(expression, context);

    private NativeValueKind InferArrayElement(
        Expression expression,
        NativeContext context) =>
        _semantics.InferArrayElement(expression, context);

    private NativeValueKind FunctionKind(FunctionDeclaration function) =>
        _semantics.FunctionKind(function);

    private NativeValueKind ParseType(string? typeName, YyNode node) =>
        _semantics.ParseType(typeName, node);
}
