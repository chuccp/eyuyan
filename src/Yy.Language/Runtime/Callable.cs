using Yy.Language.Syntax;

namespace Yy.Language.Runtime;

internal sealed record NativeFunction(
    string Name,
    int MinimumArguments,
    int MaximumArguments,
    Func<object?[], object?> Invoke);

internal sealed record UserFunction(
    FunctionDeclaration Declaration,
    Environment Closure);

