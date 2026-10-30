using Yy.Language.Runtime;
using Yy.Language.Syntax;

namespace Yy.Language;

internal sealed class YySemanticAnalyzer
{
    private const string Any = "任意";
    private const string Number = "数字";
    private const string Text = "文本";
    private const string Boolean = "逻辑";
    private const string Array = "数组";
    private const string Object = "对象";
    private const string Date = "日期";

    private readonly YyProgram _program;
    private readonly string? _rootSourceName;
    private readonly List<YyDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, FunctionDeclaration> _functions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructDeclaration> _structs =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _interfaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _predeclaredGlobals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _importedPackages;
    private readonly HashSet<string> _visibleBuiltins;
    private Scope _scope = new();
    private FunctionDeclaration? _currentFunction;
    private string? _currentSource;

    private YySemanticAnalyzer(YyProgram program, string? rootSourceName)
    {
        _program = program;
        _rootSourceName = rootSourceName;
        _importedPackages = new HashSet<string>(
            Yy.Sdk.YySdk.ResolveEnabledPackages(
                program.PackageImports.Select(package => package.Name)),
            StringComparer.Ordinal);
        _visibleBuiltins = Builtins.FunctionNames
            .Where(name => Yy.Sdk.YySdk.IsBuiltinAvailable(name, _importedPackages))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            _functions[function.Name] = function;
        }

        foreach (var structure in program.Structs)
        {
            _structs[structure.Name] = structure;
        }

        foreach (var contract in program.Interfaces)
        {
            _interfaces.Add(contract.Name);
        }
    }

    public static IReadOnlyList<YyDiagnostic> Analyze(YyProgram program, string? rootSourceName)
    {
        var analyzer = new YySemanticAnalyzer(program, rootSourceName);
        analyzer.Run();
        return analyzer._diagnostics;
    }

    private void Run()
    {
        _currentSource = _rootSourceName;
        foreach (var builtin in _visibleBuiltins)
        {
            _scope.TryDeclare(builtin, "函数", isConstant: true, new TokenLocation(1, 1));
        }

        foreach (var statement in _program.Main.Statements)
        {
            if (statement is VariableStatement variable)
            {
                DeclareGlobal(variable);
            }
        }

        foreach (var statement in _program.Main.Statements)
        {
            AnalyzeStatement(statement, loopDepth: 0, useExistingGlobal: true);
        }

        foreach (var function in _program.Functions)
        {
            AnalyzeFunction(function);
        }
    }

    private void DeclareGlobal(VariableStatement variable)
    {
        if (_functions.ContainsKey(variable.Name) || Builtins.FunctionNames.Contains(variable.Name))
        {
            Report(
                variable,
                $"名称“{variable.Name}”已被函数占用",
                "请为变量换一个名称。");
            return;
        }

        if (!_scope.TryDeclare(
                variable.Name,
                variable.DeclaredType ?? Any,
                variable.IsConstant,
                new TokenLocation(variable.Line, variable.Column)))
        {
            Report(variable, $"变量“{variable.Name}”在当前作用域重复定义");
            return;
        }

        _predeclaredGlobals.Add(variable.Name);
    }

    private void AnalyzeFunction(FunctionDeclaration function)
    {
        _currentFunction = function;
        _currentSource = function.SourceName ?? _rootSourceName;
        var functionScope = _scope.Child();
        var previousScope = _scope;
        _scope = functionScope;

        foreach (var parameter in function.Parameters)
        {
            var type = parameter.TypeName ?? Any;
            if (!_scope.TryDeclare(
                    parameter.Name,
                    type,
                    isConstant: false,
                    new TokenLocation(function.Line, function.Column)))
            {
                Report(function, $"参数“{parameter.Name}”重复定义");
            }

            if (parameter.DefaultValue is not null)
            {
                CheckAssignable(
                    parameter.TypeName,
                    Infer(parameter.DefaultValue),
                    parameter.DefaultValue,
                    $"参数“{parameter.Name}”");
            }
        }

        AnalyzeBlock(function.Body, createScope: false, loopDepth: 0);
        _scope = previousScope;
        _currentFunction = null;
        _currentSource = _rootSourceName;
    }

    private void AnalyzeBlock(BlockStatement block, bool createScope, int loopDepth)
    {
        var previousScope = _scope;
        if (createScope)
        {
            _scope = _scope.Child();
        }

        try
        {
            for (var index = 0; index < block.Statements.Count; index++)
            {
                AnalyzeStatement(block.Statements[index], loopDepth, useExistingGlobal: false);
            }
        }
        finally
        {
            _scope = previousScope;
        }
    }

    private void AnalyzeStatement(Statement statement, int loopDepth, bool useExistingGlobal)
    {
        switch (statement)
        {
            case VariableStatement variable:
                var inferred = Infer(variable.Initializer);
                CheckAssignable(variable.DeclaredType, inferred, variable, $"变量“{variable.Name}”");
                if (!(useExistingGlobal && _predeclaredGlobals.Contains(variable.Name)))
                {
                    if (!_scope.TryDeclare(
                            variable.Name,
                            variable.DeclaredType ?? Any,
                            variable.IsConstant,
                            new TokenLocation(variable.Line, variable.Column)))
                    {
                        Report(variable, $"变量“{variable.Name}”在当前作用域重复定义");
                    }
                }

                break;

            case ExpressionStatement expression:
                Infer(expression.Expression);
                break;

            case AssignStatement assignment:
                var valueType = Infer(assignment.Value);
                AnalyzeAssignmentTarget(assignment.Target, valueType, assignment);
                break;

            case IfStatement conditional:
                Infer(conditional.Condition);
                AnalyzeBlock(conditional.Then, createScope: true, loopDepth);
                if (conditional.Else is not null)
                {
                    AnalyzeBlock(conditional.Else, createScope: true, loopDepth);
                }

                break;

            case WhileStatement loop:
                Infer(loop.Condition);
                AnalyzeBlock(loop.Body, createScope: true, loopDepth + 1);
                break;

            case CountLoopStatement loop:
                Infer(loop.Start);
                Infer(loop.End);
                Infer(loop.Step);
                var loopScope = _scope.Child();
                loopScope.TryDeclare(
                    loop.VariableName,
                    Number,
                    isConstant: false,
                    new TokenLocation(loop.Line, loop.Column));
                var previous = _scope;
                _scope = loopScope;
                AnalyzeBlock(loop.Body, createScope: false, loopDepth + 1);
                _scope = previous;
                break;

            case ForEachStatement loop:
                Infer(loop.Collection);
                var eachScope = _scope.Child();
                eachScope.TryDeclare(
                    loop.VariableName,
                    Any,
                    isConstant: false,
                    new TokenLocation(loop.Line, loop.Column));
                var eachPrevious = _scope;
                _scope = eachScope;
                AnalyzeBlock(loop.Body, createScope: false, loopDepth + 1);
                _scope = eachPrevious;
                break;

            case ReturnStatement @return:
                if (_currentFunction is null)
                {
                    Report(@return, "“返回”只能用在子程序内部");
                }
                else if (@return.Value is null && _currentFunction.ReturnType is not null)
                {
                    Report(
                        @return,
                        $"子程序“{_currentFunction.Name}”必须返回“{_currentFunction.ReturnType}”");
                }
                else if (@return.Value is not null)
                {
                    CheckAssignable(
                        _currentFunction.ReturnType,
                        Infer(@return.Value),
                        @return.Value,
                        $"子程序“{_currentFunction.Name}”的返回值");
                }

                break;

            case BreakStatement @break:
                if (loopDepth == 0)
                {
                    Report(@break, "“跳出”只能用在循环内部");
                }

                break;

            case ContinueStatement @continue:
                if (loopDepth == 0)
                {
                    Report(@continue, "“继续”只能用在循环内部");
                }

                break;

            case TryStatement attempt:
                AnalyzeBlock(attempt.TryBlock, createScope: true, loopDepth);
                if (attempt.CatchBlock is not null)
                {
                    var catchScope = _scope.Child();
                    if (!string.IsNullOrWhiteSpace(attempt.ErrorVariable))
                    {
                        catchScope.TryDeclare(
                            attempt.ErrorVariable,
                            Text,
                            isConstant: false,
                            new TokenLocation(attempt.Line, attempt.Column));
                    }

                    var catchPrevious = _scope;
                    _scope = catchScope;
                    AnalyzeBlock(attempt.CatchBlock, createScope: false, loopDepth);
                    _scope = catchPrevious;
                }

                break;
        }
    }

    private void AnalyzeAssignmentTarget(Expression target, string valueType, AssignStatement assignment)
    {
        switch (target)
        {
            case IdentifierExpression identifier:
                if (!_scope.TryLookup(identifier.Name, out var symbol))
                {
                    Report(identifier, $"变量“{identifier.Name}”没有定义");
                    return;
                }

                if (symbol.IsConstant)
                {
                    Report(identifier, $"常量“{identifier.Name}”不能重新赋值");
                }

                var comparedType = assignment.Operator == "=" || symbol.Type == Any
                    ? valueType
                    : InferBinaryType(assignment.Operator[..1], symbol.Type, valueType);
                CheckAssignable(symbol.Type, comparedType, assignment, $"变量“{identifier.Name}”");
                break;

            case MemberExpression member:
                Infer(member.Target);
                break;

            case IndexExpression index:
                Infer(index.Target);
                Infer(index.Index);
                break;
        }
    }

    private string Infer(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => literal.Value switch
            {
                double => Number,
                string => Text,
                bool => Boolean,
                null => "空",
                DateTime => Date,
                _ => Any
            },
            IdentifierExpression identifier => InferIdentifier(identifier),
            ArrayExpression array => InferArray(array),
            ObjectExpression obj => InferObject(obj),
            UnaryExpression unary => InferUnary(unary),
            BinaryExpression binary => InferBinary(binary),
            CallExpression call => InferCall(call),
            MemberExpression member => InferMember(member),
            IndexExpression index => InferIndex(index),
            _ => Any
        };
    }

    private string InferIdentifier(IdentifierExpression identifier)
    {
        if (_scope.TryLookup(identifier.Name, out var symbol))
        {
            return symbol.Type;
        }

        if (_functions.ContainsKey(identifier.Name))
        {
            return "函数";
        }

        if (Builtins.FunctionNames.Contains(identifier.Name))
        {
            ReportMissingPackage(identifier.Name, identifier);
            return Any;
        }

        Report(identifier, $"变量或函数“{identifier.Name}”没有定义");
        return Any;
    }

    private string InferArray(ArrayExpression array)
    {
        foreach (var item in array.Items)
        {
            Infer(item);
        }

        return Array;
    }

    private string InferObject(ObjectExpression obj)
    {
        foreach (var property in obj.Properties)
        {
            Infer(property.Value);
        }

        return Object;
    }

    private string InferUnary(UnaryExpression unary)
    {
        var operand = Infer(unary.Operand);
        if (unary.Operator is "非" or "!")
        {
            return Boolean;
        }

        CheckAssignable(Number, operand, unary.Operand, "一元数值运算");
        return Number;
    }

    private string InferBinary(BinaryExpression binary)
    {
        var left = Infer(binary.Left);
        var right = Infer(binary.Right);
        return InferBinaryType(binary.Operator, left, right);
    }

    private string InferBinaryType(string op, string left, string right)
    {
        if (op is "且" or "&&" or "或" or "||" or "==" or "!=" or "等于" or "不等于"
            or "<" or "<=" or ">" or ">=")
        {
            return Boolean;
        }

        if (op == "??")
        {
            return left == "空" ? right : left;
        }

        if (op == "+" && (left == Text || right == Text))
        {
            return Text;
        }

        return Number;
    }

    private string InferCall(CallExpression call)
    {
        var argumentTypes = call.Arguments.Select(Infer).ToArray();
        if (call.Callee is IdentifierExpression identifier)
        {
            if (_scope.TryLookup(identifier.Name, out var symbol) &&
                symbol.Type is not "函数")
            {
                Report(identifier, $"“{identifier.Name}”是 {symbol.Type} 类型的值，不是函数");
                return Any;
            }

            if (_functions.TryGetValue(identifier.Name, out var function))
            {
                var required = function.Parameters.Count(parameter => parameter.DefaultValue is null);
                if (call.Arguments.Count < required || call.Arguments.Count > function.Parameters.Count)
                {
                    Report(
                        call,
                        $"子程序“{function.Name}”需要 {required} 到 {function.Parameters.Count} 个参数，实际传入 {call.Arguments.Count} 个");
                }

                var count = Math.Min(call.Arguments.Count, function.Parameters.Count);
                for (var index = 0; index < count; index++)
                {
                    CheckAssignable(
                        function.Parameters[index].TypeName,
                        argumentTypes[index],
                        call.Arguments[index],
                        $"参数“{function.Parameters[index].Name}”");
                }

                return function.ReturnType ?? Any;
            }

            if (Builtins.FunctionNames.Contains(identifier.Name))
            {
                if (!_visibleBuiltins.Contains(identifier.Name))
                {
                    ReportMissingPackage(identifier.Name, identifier);
                    return Any;
                }

                return InferBuiltinType(identifier.Name);
            }

            Report(identifier, $"函数“{identifier.Name}”没有定义");
            return Any;
        }

        Infer(call.Callee);
        return Any;
    }

    private string InferMember(MemberExpression member)
    {
        Infer(member.Target);
        return Any;
    }

    private string InferIndex(IndexExpression index)
    {
        Infer(index.Target);
        Infer(index.Index);
        return Any;
    }

    private static string InferBuiltinType(string name)
    {
        return name switch
        {
            "长度" or "数字" or "整数" or "小数" or "绝对值" or "平方根" or "幂" or "最大" or "最小"
                or "随机数" => Number,
            "类型" or "文本" or "格式化时间" or "JSON生成" or "UTF8编码" or "UTF8解码"
                or "连接" or "替换" or "修剪" or "转大写" or "转小写" or "取子文本" => Text,
            "包含" or "断言" => Boolean,
            "范围" or "分割" or "键集" or "值集" or "追加" or "插入" or "排序" or "倒序" => Array,
            "当前时间" => Date,
            "移除" => Any,
            _ => Any
        };
    }

    private void CheckAssignable(
        string? expected,
        string actual,
        YyNode node,
        string target)
    {
        if (expected is null || expected == Any || actual == Any ||
            string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return;
        }

        if (_structs.TryGetValue(expected, out var structure))
        {
            if (actual != Object && actual != Any)
            {
                Report(node, $"{target}需要结构体“{expected}”");
                return;
            }

            if (node is ObjectExpression obj)
            {
                foreach (var field in structure.Fields)
                {
                    if (!obj.Properties.Any(property =>
                            string.Equals(property.Key, field.Name, StringComparison.Ordinal)))
                    {
                        Report(node, $"{target}缺少结构体字段“{field.Name}”");
                    }
                }

                foreach (var property in obj.Properties)
                {
                    var field = structure.Fields.FirstOrDefault(item =>
                        string.Equals(item.Name, property.Key, StringComparison.Ordinal));
                    if (field is null)
                    {
                        Report(property.Value, $"{target}不包含字段“{property.Key}”");
                        continue;
                    }

                    CheckAssignable(field.TypeName, Infer(property.Value), property.Value,
                        $"字段“{field.Name}”");
                }
            }

            return;
        }

        if (_interfaces.Contains(expected))
        {
            if (actual != Object && actual != Any)
            {
                Report(node, $"{target}需要接口“{expected}”");
            }

            return;
        }

        if (expected is not null && !YyTypeSystem.IsKnown(expected))
        {
            return;
        }

        Report(node, $"{target}需要“{expected}”，表达式类型为“{actual}”");
    }

    private void Report(YyNode node, string message, string? hint = null)
    {
        var sourceName = _currentFunction?.SourceName ?? _currentSource;
        if (node.Line <= 1 && node.Column <= 1)
        {
            sourceName = _rootSourceName;
        }

        _diagnostics.Add(new YyDiagnostic(
            message,
            node.Line,
            node.Column,
            hint,
            sourceName));
    }

    private void ReportMissingPackage(string functionName, YyNode node)
    {
        var packages = Yy.Sdk.YySdk.PackagesProvidingBuiltin(functionName);
        Report(
            node,
            $"函数“{functionName}”没有导入。",
            packages.Count == 0
                ? "该函数不属于任何已安装包。"
                : "请先导入：" + string.Join(
                    " 或 ",
                    packages.Select(package => $"导入包 \"{package}\"")));
    }

    private sealed class Scope
    {
        private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);

        public Scope(Scope? parent = null)
        {
            Parent = parent;
        }

        private Scope? Parent { get; }

        public bool TryDeclare(string name, string type, bool isConstant, TokenLocation location)
        {
            return _symbols.TryAdd(name, new Symbol(type, isConstant, location));
        }

        public bool TryLookup(string name, out Symbol symbol)
        {
            if (_symbols.TryGetValue(name, out symbol!))
            {
                return true;
            }

            if (Parent is not null)
            {
                return Parent.TryLookup(name, out symbol);
            }

            symbol = default!;
            return false;
        }

        public Scope Child() => new(this);
    }

    private readonly record struct Symbol(string Type, bool IsConstant, TokenLocation Location);
    private readonly record struct TokenLocation(int Line, int Column);
}
