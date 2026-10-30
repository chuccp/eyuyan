using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Yy.Language.Syntax;

namespace Yy.Language.Runtime;

internal sealed class Interpreter
{
    private const int MaximumCallDepth = 128;
    private readonly Action<string> _write;
    private readonly CancellationToken _cancellationToken;
    private readonly YyDebugProbe? _probe;
    private readonly Func<IReadOnlyDictionary<string, object?>> _snapshot;
    private readonly Environment _globals = new();
    private int _callDepth;
    private int _steps;

    public Interpreter(Action<string>? write, CancellationToken cancellationToken, YyDebugProbe? probe = null)
    {
        _write = write ?? (_ => { });
        _cancellationToken = cancellationToken;
        _probe = probe;
        _snapshot = () => _globals.Snapshot();
        Builtins.Install(_globals, Emit, cancellationToken, probe);
    }

    public IReadOnlyDictionary<string, object?> Globals => _globals.Snapshot();

    public object? Execute(YyProgram program)
    {
        foreach (var function in program.Functions)
        {
            if (_globals.TryGet(function.Name, out _))
            {
                throw new YyRuntimeError($"函数“{function.Name}”重复定义", function.Line, function.Column);
            }

            _globals.Define(function.Name, new UserFunction(function, _globals));
        }

        try
        {
            return ExecuteBlock(program.Main, _globals);
        }
        catch (BreakSignal)
        {
            throw new YyRuntimeError("“跳出”只能用在循环内部", 1, 1);
        }
        catch (ContinueSignal)
        {
            throw new YyRuntimeError("“继续”只能用在循环内部", 1, 1);
        }
        catch (ReturnSignal)
        {
            throw new YyRuntimeError("“返回”只能用在子程序内部", 1, 1);
        }
    }

    private object? ExecuteBlock(BlockStatement block, Environment environment)
    {
        object? last = null;
        foreach (var statement in block.Statements)
        {
            Tick(statement.Line, statement.Column);
            last = ExecuteStatement(statement, environment);
        }

        return last;
    }

    private object? ExecuteStatement(Statement statement, Environment environment)
    {
        cancellationTokenCheck(statement.Line, statement.Column);
        try
        {
            return statement switch
            {
                VariableStatement variable => ExecuteVariable(variable, environment),
                ExpressionStatement expression => Evaluate(expression.Expression, environment),
                AssignStatement assignment => ExecuteAssignment(assignment, environment),
                IfStatement conditional => ExecuteIf(conditional, environment),
                WhileStatement loop => ExecuteWhile(loop, environment),
                CountLoopStatement loop => ExecuteCountLoop(loop, environment),
                ForEachStatement loop => ExecuteForEach(loop, environment),
                ReturnStatement @return => throw new ReturnSignal(
                    @return.Value is null ? null : Evaluate(@return.Value, environment),
                    @return.Line,
                    @return.Column),
                BreakStatement => throw new BreakSignal(),
                ContinueStatement => throw new ContinueSignal(),
                TryStatement tryStatement => ExecuteTry(tryStatement, environment),
                _ => throw Error(statement, $"暂不支持语句类型“{statement.GetType().Name}”")
            };
        }
        catch (YyRuntimeError error)
        {
            if (error.Line == 1 && error.Column == 1 && statement.Line != 1)
            {
                throw new YyRuntimeError(error.Message, statement.Line, statement.Column, error.Detail);
            }

            throw;
        }
    }

    private object? ExecuteVariable(VariableStatement statement, Environment environment)
    {
        var value = Evaluate(statement.Initializer, environment);
        YyTypeSystem.Ensure(
            value,
            statement.DeclaredType,
            statement.Line,
            statement.Column,
            $"变量“{statement.Name}”");
        environment.Define(
            statement.Name,
            value,
            statement.IsConstant,
            statement.DeclaredType);
        return value;
    }

    private object? ExecuteAssignment(AssignStatement statement, Environment environment)
    {
        var value = Evaluate(statement.Value, environment);
        if (statement.Operator != "=")
        {
            var existing = ReadTarget(statement.Target, environment);
            var binaryOperator = statement.Operator[..1];
            value = ApplyBinary(
                binaryOperator,
                existing,
                value,
                statement.Line,
                statement.Column);
        }

        WriteTarget(statement.Target, value, environment);
        return value;
    }

    private object? ExecuteIf(IfStatement statement, Environment environment)
    {
        if (IsTruthy(Evaluate(statement.Condition, environment)))
        {
            return ExecuteBlock(statement.Then, new Environment(environment));
        }

        if (statement.Else is not null)
        {
            return ExecuteBlock(statement.Else, new Environment(environment));
        }

        return null;
    }

    private object? ExecuteWhile(WhileStatement statement, Environment environment)
    {
        object? last = null;
        while (IsTruthy(Evaluate(statement.Condition, environment)))
        {
            Tick(statement.Line, statement.Column);
            try
            {
                last = ExecuteBlock(statement.Body, new Environment(environment));
            }
            catch (ContinueSignal)
            {
                continue;
            }
            catch (BreakSignal)
            {
                break;
            }
        }

        return last;
    }

    private object? ExecuteCountLoop(CountLoopStatement statement, Environment environment)
    {
        var start = RequireNumber(Evaluate(statement.Start, environment), statement.Line, statement.Column);
        var end = RequireNumber(Evaluate(statement.End, environment), statement.Line, statement.Column);
        var step = RequireNumber(Evaluate(statement.Step, environment), statement.Line, statement.Column);
        if (step == 0)
        {
            throw Error(statement, "计次循环的步长不能为 0");
        }

        object? last = null;
        for (var current = start; step > 0 ? current <= end : current >= end; current += step)
        {
            Tick(statement.Line, statement.Column);
            var iterationEnvironment = new Environment(environment);
            iterationEnvironment.Define(statement.VariableName, current);
            try
            {
                last = ExecuteBlock(statement.Body, iterationEnvironment);
            }
            catch (ContinueSignal)
            {
                continue;
            }
            catch (BreakSignal)
            {
                break;
            }
        }

        return last;
    }

    private object? ExecuteForEach(ForEachStatement statement, Environment environment)
    {
        var collection = Evaluate(statement.Collection, environment);
        var items = Enumerate(collection, statement.Line, statement.Column);
        object? last = null;

        foreach (var item in items)
        {
            Tick(statement.Line, statement.Column);
            var iterationEnvironment = new Environment(environment);
            iterationEnvironment.Define(statement.VariableName, item);
            try
            {
                last = ExecuteBlock(statement.Body, iterationEnvironment);
            }
            catch (ContinueSignal)
            {
                continue;
            }
            catch (BreakSignal)
            {
                break;
            }
        }

        return last;
    }

    private object? ExecuteTry(TryStatement statement, Environment environment)
    {
        try
        {
            return ExecuteBlock(statement.TryBlock, new Environment(environment));
        }
        catch (YyRuntimeError error)
        {
            if (statement.CatchBlock is null)
            {
                throw;
            }

            var catchEnvironment = new Environment(environment);
            if (!string.IsNullOrWhiteSpace(statement.ErrorVariable))
            {
                catchEnvironment.Define(statement.ErrorVariable, error.Message);
            }

            return ExecuteBlock(statement.CatchBlock, catchEnvironment);
        }
    }

    private object? Evaluate(Expression expression, Environment environment)
    {
        Tick(expression.Line, expression.Column, isStatement: false);
        return expression switch
        {
            LiteralExpression literal => literal.Value,
            IdentifierExpression identifier => environment.Get(identifier.Name, identifier.Line, identifier.Column),
            ArrayExpression array => array.Items.Select(item => Evaluate(item, environment)).ToList(),
            ObjectExpression obj => EvaluateObject(obj, environment),
            UnaryExpression unary => EvaluateUnary(unary, environment),
            BinaryExpression binary => EvaluateBinary(binary, environment),
            CallExpression call => EvaluateCall(call, environment),
            MemberExpression member => ReadMember(member, environment),
            IndexExpression index => ReadIndex(index, environment),
            _ => throw Error(expression, $"暂不支持表达式类型“{expression.GetType().Name}”")
        };
    }

    private Dictionary<string, object?> EvaluateObject(ObjectExpression expression, Environment environment)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in expression.Properties)
        {
            if (!result.TryAdd(property.Key, Evaluate(property.Value, environment)))
            {
                throw Error(expression, $"对象属性“{property.Key}”重复定义");
            }
        }

        return result;
    }

    private object? EvaluateUnary(UnaryExpression expression, Environment environment)
    {
        var value = Evaluate(expression.Operand, environment);
        return expression.Operator switch
        {
            "非" or "!" => !IsTruthy(value),
            "+" => RequireNumber(value, expression.Line, expression.Column),
            "-" => -RequireNumber(value, expression.Line, expression.Column),
            _ => throw Error(expression, $"不支持一元运算符“{expression.Operator}”")
        };
    }

    private object? EvaluateBinary(BinaryExpression expression, Environment environment)
    {
        var left = Evaluate(expression.Left, environment);
        if (expression.Operator is "且" or "&&")
        {
            return IsTruthy(left) && IsTruthy(Evaluate(expression.Right, environment));
        }

        if (expression.Operator is "或" or "||")
        {
            return IsTruthy(left) || IsTruthy(Evaluate(expression.Right, environment));
        }

        if (expression.Operator == "??")
        {
            return left ?? Evaluate(expression.Right, environment);
        }

        var right = Evaluate(expression.Right, environment);
        return ApplyBinary(expression.Operator, left, right, expression.Line, expression.Column);
    }

    private object? ApplyBinary(string op, object? left, object? right, int line, int column)
    {
        return op switch
        {
            "==" or "等于" => ValuesEqual(left, right),
            "!=" or "不等于" => !ValuesEqual(left, right),
            "+" => Add(left, right, line, column),
            "-" => RequireNumber(left, line, column) - RequireNumber(right, line, column),
            "*" => RequireNumber(left, line, column) * RequireNumber(right, line, column),
            "/" => Divide(left, right, line, column),
            "%" => Modulo(left, right, line, column),
            "<" => Compare(left, right, line, column) < 0,
            "<=" => Compare(left, right, line, column) <= 0,
            ">" => Compare(left, right, line, column) > 0,
            ">=" => Compare(left, right, line, column) >= 0,
            _ => throw new YyRuntimeError($"不支持二元运算符“{op}”", line, column)
        };
    }

    private object Add(object? left, object? right, int line, int column)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber + rightNumber;
        }

        if (left is string || right is string)
        {
            return Format(left) + Format(right);
        }

        throw new YyRuntimeError(
            $"类型“{TypeName(left)}”与“{TypeName(right)}”不能相加",
            line,
            column,
            "文本拼接时至少需要一侧是文本，可使用“文本(值)”转换。");
    }

    private double Divide(object? left, object? right, int line, int column)
    {
        var denominator = RequireNumber(right, line, column);
        if (denominator == 0)
        {
            throw new YyRuntimeError("不能除以零", line, column);
        }

        return RequireNumber(left, line, column) / denominator;
    }

    private double Modulo(object? left, object? right, int line, int column)
    {
        var denominator = RequireNumber(right, line, column);
        if (denominator == 0)
        {
            throw new YyRuntimeError("不能对零取余", line, column);
        }

        return RequireNumber(left, line, column) % denominator;
    }

    private int Compare(object? left, object? right, int line, int column)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Compare(leftText, rightText, StringComparison.Ordinal);
        }

        throw new YyRuntimeError(
            $"类型“{TypeName(left)}”与“{TypeName(right)}”不能比较大小",
            line,
            column);
    }

    private object? EvaluateCall(CallExpression call, Environment environment)
    {
        var callee = Evaluate(call.Callee, environment);
        var arguments = call.Arguments.Select(argument => Evaluate(argument, environment)).ToArray();
        return callee switch
        {
            NativeFunction native => InvokeNative(native, arguments, call),
            UserFunction user => InvokeUser(user, arguments, call),
            _ => throw new YyRuntimeError(
                $"“{Describe(call.Callee)}”不是可调用的函数",
                call.Line,
                call.Column)
        };
    }

    private object? InvokeNative(NativeFunction function, object?[] arguments, CallExpression call)
    {
        if (arguments.Length < function.MinimumArguments || arguments.Length > function.MaximumArguments)
        {
            var expected = function.MinimumArguments == function.MaximumArguments
                ? function.MinimumArguments.ToString(CultureInfo.InvariantCulture)
                : $"{function.MinimumArguments} 到 {function.MaximumArguments}";
            throw new YyRuntimeError(
                $"函数“{function.Name}”需要 {expected} 个参数，实际传入 {arguments.Length} 个",
                call.Line,
                call.Column);
        }

        try
        {
            return function.Invoke(arguments);
        }
        catch (YyRuntimeError)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new YyRuntimeError(
                $"函数“{function.Name}”执行失败：{exception.Message}",
                call.Line,
                call.Column);
        }
    }

    private object? InvokeUser(UserFunction function, object?[] arguments, CallExpression call)
    {
        if (_callDepth >= MaximumCallDepth)
        {
            throw new YyRuntimeError(
                "函数调用层级过深，可能存在无限递归",
                call.Line,
                call.Column,
                $"当前最大调用层级为 {MaximumCallDepth}。");
        }

        var declaration = function.Declaration;
        if (arguments.Length > declaration.Parameters.Count)
        {
            throw new YyRuntimeError(
                $"子程序“{declaration.Name}”最多接受 {declaration.Parameters.Count} 个参数",
                call.Line,
                call.Column);
        }

        var local = new Environment(function.Closure);
        for (var index = 0; index < declaration.Parameters.Count; index++)
        {
            var parameter = declaration.Parameters[index];
            object? value;
            if (index < arguments.Length)
            {
                value = arguments[index];
            }
            else if (parameter.DefaultValue is not null)
            {
                value = Evaluate(parameter.DefaultValue, local);
            }
            else
            {
                throw new YyRuntimeError(
                    $"子程序“{declaration.Name}”缺少参数“{parameter.Name}”",
                    call.Line,
                    call.Column);
            }

            YyTypeSystem.Ensure(
                value,
                parameter.TypeName,
                call.Line,
                call.Column,
                $"参数“{parameter.Name}”");
            local.Define(parameter.Name, value, typeName: parameter.TypeName);
        }

        _callDepth++;
        try
        {
            object? result;
            try
            {
                result = ExecuteBlock(declaration.Body, local);
            }
            catch (ReturnSignal signal)
            {
                result = signal.Value;
                YyTypeSystem.Ensure(
                    result,
                    declaration.ReturnType,
                    signal.Line,
                    signal.Column,
                    $"子程序“{declaration.Name}”的返回值");
                return result;
            }

            YyTypeSystem.Ensure(
                result,
                declaration.ReturnType,
                call.Line,
                call.Column,
                $"子程序“{declaration.Name}”的返回值");
            return result;
        }
        catch (YyRuntimeError error) when (error.SourceName is null && declaration.SourceName is not null)
        {
            throw new YyRuntimeError(
                error.Message,
                error.Line,
                error.Column,
                error.Detail,
                declaration.SourceName);
        }
        finally
        {
            _callDepth--;
        }
    }

    private object? ReadMember(MemberExpression expression, Environment environment)
    {
        var target = Evaluate(expression.Target, environment);
        if (target is Dictionary<string, object?> dictionary)
        {
            if (dictionary.TryGetValue(expression.Member, out var value))
            {
                return value;
            }

            throw Error(expression, $"对象没有属性“{expression.Member}”");
        }

        throw Error(expression, $"类型“{TypeName(target)}”不支持属性“{expression.Member}”");
    }

    private object? ReadIndex(IndexExpression expression, Environment environment)
    {
        var target = Evaluate(expression.Target, environment);
        var index = Evaluate(expression.Index, environment);

        if (target is List<object?> list)
        {
            var integer = RequireIndex(index, expression.Line, expression.Column);
            if (integer < 0 || integer >= list.Count)
            {
                throw Error(expression, $"数组索引 {integer} 越界，有效范围是 0 到 {list.Count - 1}");
            }

            return list[integer];
        }

        if (target is string text)
        {
            var integer = RequireIndex(index, expression.Line, expression.Column);
            if (integer < 0 || integer >= text.Length)
            {
                throw Error(expression, $"文本索引 {integer} 越界，有效范围是 0 到 {text.Length - 1}");
            }

            return text[integer].ToString();
        }

        if (target is Dictionary<string, object?> dictionary)
        {
            var key = Format(index);
            if (dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            throw Error(expression, $"对象没有键“{key}”");
        }

        throw Error(expression, $"类型“{TypeName(target)}”不支持索引访问");
    }

    private object? ReadTarget(Expression target, Environment environment)
    {
        return target switch
        {
            IdentifierExpression identifier => environment.Get(
                identifier.Name,
                identifier.Line,
                identifier.Column),
            MemberExpression member => ReadMember(member, environment),
            IndexExpression index => ReadIndex(index, environment),
            _ => throw Error(target, "无效的赋值目标")
        };
    }

    private void WriteTarget(Expression target, object? value, Environment environment)
    {
        switch (target)
        {
            case IdentifierExpression identifier:
                environment.Assign(identifier.Name, value, identifier.Line, identifier.Column);
                return;

            case MemberExpression member:
                var targetObject = Evaluate(member.Target, environment);
                if (targetObject is Dictionary<string, object?> dictionary)
                {
                    dictionary[member.Member] = value;
                    return;
                }

                throw Error(member, $"类型“{TypeName(targetObject)}”不支持设置属性");

            case IndexExpression index:
                var targetValue = Evaluate(index.Target, environment);
                var indexValue = Evaluate(index.Index, environment);
                if (targetValue is List<object?> list)
                {
                    var integer = RequireIndex(indexValue, index.Line, index.Column);
                    if (integer < 0 || integer >= list.Count)
                    {
                        throw Error(index, $"数组索引 {integer} 越界，有效范围是 0 到 {list.Count - 1}");
                    }

                    list[integer] = value;
                    return;
                }

                if (targetValue is Dictionary<string, object?> map)
                {
                    map[Format(indexValue)] = value;
                    return;
                }

                throw Error(index, $"类型“{TypeName(targetValue)}”不支持通过索引赋值");

            default:
                throw Error(target, "无效的赋值目标");
        }
    }

    private void Emit(object? value) => _write(Format(value));

    private void Tick(int line, int column, bool isStatement = true)
    {
        _steps++;
        if ((_steps & 127) == 0)
        {
            cancellationTokenCheck(line, column);
        }

        // 调试探针只挂语句级停点;表达式求值同属一行,若也停点会让单步卡在同一行。
        if (isStatement)
        {
            _probe?.OnStatement(line, column, _callDepth, _snapshot);
        }
    }

    private void cancellationTokenCheck(int line, int column)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"程序已停止（第 {line} 行）",
                _cancellationToken);
        }
    }

    internal static double RequireNumber(object? value, int line, int column)
    {
        if (value is double number)
        {
            return number;
        }

        if (value is int integer)
        {
            return integer;
        }

        throw new YyRuntimeError(
            $"此处需要数字，实际得到“{TypeName(value)}”",
            line,
            column,
            "可使用“数字(值)”显式转换。");
    }

    internal static int RequireIndex(object? value, int line, int column)
    {
        var number = RequireNumber(value, line, column);
        if (number != Math.Truncate(number))
        {
            throw new YyRuntimeError("索引必须是整数", line, column);
        }

        return checked((int)number);
    }

    internal static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            double number => number != 0,
            string text => text.Length > 0,
            ICollection collection => collection.Count > 0,
            _ => true
        };
    }

    internal static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.Equals(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        return left.Equals(right);
    }

    internal static string Format(object? value)
    {
        return value switch
        {
            null => "空",
            bool boolean => boolean ? "真" : "假",
            double number when number == Math.Truncate(number) &&
                               number >= long.MinValue &&
                               number <= long.MaxValue =>
                ((long)number).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("G15", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            string text => text,
            List<object?> list => "[" + string.Join(", ", list.Select(Format)) + "]",
            Dictionary<string, object?> dictionary =>
                "{" + string.Join(", ", dictionary.Select(pair => $"{pair.Key}: {Format(pair.Value)}")) + "}",
            IEnumerable enumerable => string.Join(", ", enumerable.Cast<object?>().Select(Format)),
            _ => value.ToString() ?? string.Empty
        };
    }

    internal static string TypeName(object? value)
    {
        return value switch
        {
            null => "空",
            bool => "逻辑",
            double => "数字",
            string => "文本",
            List<object?> => "数组",
            Dictionary<string, object?> => "对象",
            DateTime => "日期时间",
            NativeFunction or UserFunction => "函数",
            _ => value.GetType().Name
        };
    }

    private static IEnumerable<object?> Enumerate(object? value, int line, int column)
    {
        return value switch
        {
            List<object?> list => list.ToList(),
            Dictionary<string, object?> dictionary => dictionary.Keys.Cast<object?>().ToList(),
            string text => text.Select(character => (object?)character.ToString()).ToList(),
            IEnumerable enumerable => enumerable.Cast<object?>().ToList(),
            _ => throw new YyRuntimeError(
                $"类型“{TypeName(value)}”不能遍历",
                line,
                column,
                "遍历目标需要是数组、对象或文本。")
        };
    }

    private static string Describe(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => identifier.Name,
            MemberExpression member => $"{Describe(member.Target)}.{member.Member}",
            _ => "表达式"
        };
    }

    private static YyRuntimeError Error(YyNode node, string message, string? detail = null) =>
        new(message, node.Line, node.Column, detail);

    private sealed class ReturnSignal(object? value, int line, int column) : Exception
    {
        public object? Value { get; } = value;
        public int Line { get; } = line;
        public int Column { get; } = column;
    }

    private sealed class BreakSignal : Exception;

    private sealed class ContinueSignal : Exception;
}

