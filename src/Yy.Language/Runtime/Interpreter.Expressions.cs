using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Yy.Language.Syntax;

namespace Yy.Language.Runtime;

/// <summary>
/// 表达式求值、成员/索引访问和二元运算。
/// </summary>
internal sealed partial class Interpreter
{
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

}
