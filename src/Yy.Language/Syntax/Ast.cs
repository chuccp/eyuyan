namespace Yy.Language.Syntax;

public abstract record YyNode(int Line, int Column);

public sealed record YyProgram(
    int Line,
    int Column,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<FunctionDeclaration> Functions,
    IReadOnlyList<ImportDeclaration> Imports,
    BlockStatement Main) : YyNode(Line, Column)
{
    public IReadOnlyList<PackageImportDeclaration> PackageImports { get; init; } = [];
    public IReadOnlyList<StructDeclaration> Structs { get; init; } = [];
    public IReadOnlyList<InterfaceDeclaration> Interfaces { get; init; } = [];
}

public sealed record StructFieldDeclaration(string Name, string TypeName);

public sealed record StructDeclaration(
    int Line,
    int Column,
    string Name,
    IReadOnlyList<StructFieldDeclaration> Fields,
    IReadOnlyList<string> Interfaces) : YyNode(Line, Column);

public sealed record InterfaceMethodDeclaration(
    string Name,
    IReadOnlyList<ParameterDeclaration> Parameters,
    string? ReturnType);

public sealed record InterfaceDeclaration(
    int Line,
    int Column,
    string Name,
    IReadOnlyList<InterfaceMethodDeclaration> Methods) : YyNode(Line, Column);

public sealed record ImportDeclaration(
    int Line,
    int Column,
    string Path) : YyNode(Line, Column);

public sealed record PackageImportDeclaration(
    int Line,
    int Column,
    string Name) : YyNode(Line, Column);

public sealed record ParameterDeclaration(
    string Name,
    Expression? DefaultValue,
    string? TypeName = null);

public sealed record FunctionDeclaration(
    int Line,
    int Column,
    string Name,
    IReadOnlyList<ParameterDeclaration> Parameters,
    BlockStatement Body,
    string? ReturnType = null,
    string? SourceName = null) : YyNode(Line, Column);

public sealed record BlockStatement(
    int Line,
    int Column,
    IReadOnlyList<Statement> Statements) : YyNode(Line, Column);

public abstract record Statement(int Line, int Column) : YyNode(Line, Column);

public sealed record VariableStatement(
    int Line,
    int Column,
    string Name,
    Expression Initializer,
    bool IsConstant,
    string? DeclaredType = null) : Statement(Line, Column);

public sealed record ExpressionStatement(
    int Line,
    int Column,
    Expression Expression) : Statement(Line, Column);

public sealed record AssignStatement(
    int Line,
    int Column,
    Expression Target,
    string Operator,
    Expression Value) : Statement(Line, Column);

public sealed record IfStatement(
    int Line,
    int Column,
    Expression Condition,
    BlockStatement Then,
    BlockStatement? Else) : Statement(Line, Column);

public sealed record WhileStatement(
    int Line,
    int Column,
    Expression Condition,
    BlockStatement Body) : Statement(Line, Column);

public sealed record CountLoopStatement(
    int Line,
    int Column,
    string VariableName,
    Expression Start,
    Expression End,
    Expression Step,
    BlockStatement Body) : Statement(Line, Column);

public sealed record ForEachStatement(
    int Line,
    int Column,
    string VariableName,
    Expression Collection,
    BlockStatement Body) : Statement(Line, Column);

public sealed record ReturnStatement(
    int Line,
    int Column,
    Expression? Value) : Statement(Line, Column);

public sealed record BreakStatement(int Line, int Column) : Statement(Line, Column);

public sealed record ContinueStatement(int Line, int Column) : Statement(Line, Column);

public sealed record TryStatement(
    int Line,
    int Column,
    BlockStatement TryBlock,
    string? ErrorVariable,
    BlockStatement? CatchBlock) : Statement(Line, Column);

public abstract record Expression(int Line, int Column) : YyNode(Line, Column);

public sealed record LiteralExpression(
    int Line,
    int Column,
    object? Value) : Expression(Line, Column);

public sealed record IdentifierExpression(
    int Line,
    int Column,
    string Name) : Expression(Line, Column);

public sealed record ArrayExpression(
    int Line,
    int Column,
    IReadOnlyList<Expression> Items) : Expression(Line, Column);

public sealed record ObjectExpression(
    int Line,
    int Column,
    IReadOnlyList<(string Key, Expression Value)> Properties) : Expression(Line, Column);

public sealed record UnaryExpression(
    int Line,
    int Column,
    string Operator,
    Expression Operand) : Expression(Line, Column);

public sealed record BinaryExpression(
    int Line,
    int Column,
    Expression Left,
    string Operator,
    Expression Right) : Expression(Line, Column);

public sealed record CallExpression(
    int Line,
    int Column,
    Expression Callee,
    IReadOnlyList<Expression> Arguments) : Expression(Line, Column);

public sealed record MemberExpression(
    int Line,
    int Column,
    Expression Target,
    string Member) : Expression(Line, Column);

public sealed record IndexExpression(
    int Line,
    int Column,
    Expression Target,
    Expression Index) : Expression(Line, Column);

