using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Yy.Language.Syntax;

namespace Yy.Language.Runtime;

internal sealed partial class Interpreter
{
    private const int MaximumCallDepth = 128;
    private readonly Action<string> _write;
    private readonly CancellationToken _cancellationToken;
    private readonly YyDebugProbe? _probe;
    private readonly Func<IReadOnlyDictionary<string, object?>> _snapshot;
    private readonly Environment _globals = new();
    private int _callDepth;
    private int _steps;

    public Interpreter(
        Action<string>? write,
        CancellationToken cancellationToken,
        YyDebugProbe? probe = null,
        Func<string, object?>? controlResolver = null,
        IReadOnlyDictionary<string, object?>? initialVariables = null)
    {
        _write = write ?? (_ => { });
        _cancellationToken = cancellationToken;
        _probe = probe;
        _snapshot = () => _globals.Snapshot();
        if (initialVariables is not null)
        {
            foreach (var (name, value) in initialVariables)
            {
                _globals.Define(name, value);
            }
        }

        Builtins.Install(_globals, Emit, cancellationToken, probe, controlResolver);
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
