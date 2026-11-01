using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private void CollectVariables(BlockStatement block, NativeContext context)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case VariableStatement variable:
                    context.AddSlot(variable.Name);
                    break;
                case IfStatement conditional:
                    CollectVariables(conditional.Then, context);
                    if (conditional.Else is not null)
                    {
                        CollectVariables(conditional.Else, context);
                    }

                    break;
                case WhileStatement loop:
                    CollectVariables(loop.Body, context);
                    break;
                case CountLoopStatement loop:
                    context.AddSlot(loop.VariableName);
                    CollectVariables(loop.Body, context);
                    break;
                case ForEachStatement loop:
                    context.AddSlot(loop.VariableName);
                    context.AddSlot(ForeachSlotName(loop, "collection"));
                    context.AddSlot(ForeachSlotName(loop, "index"));
                    CollectVariables(loop.Body, context);
                    break;
                case TryStatement attempt:
                    CollectVariables(attempt.TryBlock, context);
                    if (attempt.CatchBlock is not null)
                    {
                        CollectVariables(attempt.CatchBlock, context);
                    }

                    break;
            }
        }
    }

    private void EmitBlock(BlockStatement block, NativeContext context)
    {
        foreach (var statement in block.Statements)
        {
            EmitStatement(statement, context);
        }
    }

    private void EmitStatement(Statement statement, NativeContext context)
    {
        switch (statement)
        {
            case VariableStatement variable:
                EmitExpression(variable.Initializer, context);
                _code.Str(0, 29, context.Slots[variable.Name]);
                RecordKind(variable.Name, variable.Initializer, context);
                if (variable.DeclaredType is not null)
                {
                    context.Types[variable.Name] =
                        ParseType(variable.DeclaredType, variable);
                }

                break;
            case AssignStatement assignment:
                EmitAssignment(assignment, context);
                break;
            case ExpressionStatement expression:
                EmitExpression(expression.Expression, context);
                break;
            case IfStatement conditional:
                EmitIf(conditional, context);
                break;
            case WhileStatement loop:
                EmitWhile(loop, context);
                break;
            case CountLoopStatement loop:
                EmitCountLoop(loop, context);
                break;
            case ForEachStatement loop:
                EmitForEach(loop, context);
                break;
            case ReturnStatement @return:
                if (@return.Value is null)
                {
                    _code.LoadImmediate(0, 0);
                }
                else
                {
                    EmitExpression(@return.Value, context);
                }

                if (context.EndLabel is null)
                {
                    Fail(@return, "返回位置尚未初始化");
                }

                _code.B(@context.EndLabel!);
                break;
            case BreakStatement:
                if (context.Loops.Count == 0)
                {
                    Fail(statement, "跳出不在循环内");
                }

                _code.B(context.Loops.Peek().Break);
                break;
            case ContinueStatement:
                if (context.Loops.Count == 0)
                {
                    Fail(statement, "继续不在循环内");
                }

                _code.B(context.Loops.Peek().Continue);
                break;
            case TryStatement attempt:
                Fail(attempt, "macOS 原生后端暂不支持尝试/捕获");
                break;
            default:
                Fail(statement, "当前 macOS 原生后端暂不支持此语句");
                break;
        }
    }

    private void EmitAssignment(AssignStatement assignment, NativeContext context)
    {
        if (assignment.Target is IdentifierExpression identifier)
        {
            var slot = context.Slot(identifier.Name, assignment);
            if (assignment.Operator == "=")
            {
                EmitExpression(assignment.Value, context);
                _code.Str(0, 29, slot);
                RecordKind(identifier.Name, assignment.Value, context);
                return;
            }

            EmitExpression(identifier, context);
            _code.Push(0);
            EmitExpression(assignment.Value, context);
            _code.Pop(1);
            EmitCompound(assignment.Operator, Infer(identifier, context), assignment);
            _code.Str(0, 29, slot);
            context.Types[identifier.Name] =
                assignment.Operator == "+=" && Infer(assignment.Value, context) == NativeValueKind.Text
                    ? NativeValueKind.Text
                    : NativeValueKind.Integer;
            return;
        }

        if (assignment.Target is MemberExpression member)
        {
            if (TryEmitWindowStatePropertyAssignment(member, assignment, context) ||
                TryEmitControlPropertyAssignment(member, assignment, context) ||
                TryEmitMemberAssignment(member, assignment, context))
            {
                return;
            }
        }

        if (assignment.Target is IndexExpression index &&
            TryEmitIndexAssignment(index, assignment, context))
        {
            return;
        }

        Fail(assignment, "当前 macOS 原生后端暂不支持此赋值目标");
    }

    private bool TryEmitMemberAssignment(
        MemberExpression member,
        AssignStatement assignment,
        NativeContext context)
    {
        var field = TryGetObjectField(member, context);
        if (field is null)
        {
            return false;
        }

        var shape = TryGetObjectShape(member.Target, context)
            ?? throw new InvalidOperationException("对象形状丢失。");
        var offset = NativeRuntimeLayout.PayloadOffset + shape.IndexOf(field.Name) * 8;
        EmitExpression(member.Target, context);
        _code.Push(0);
        if (assignment.Operator == "=")
        {
            EmitExpression(assignment.Value, context);
        }
        else
        {
            _code.Ldr(0, 0, offset);
            _code.Push(0);
            EmitExpression(assignment.Value, context);
            _code.Pop(1);
            EmitCompound(assignment.Operator, field.Kind, assignment);
            _code.Pop(1);
        }

        if (assignment.Operator == "=")
        {
            _code.Pop(1);
        }

        _code.Str(0, 1, offset);
        context.LastKind = field.Kind;
        return true;
    }

    private bool TryEmitIndexAssignment(
        IndexExpression index,
        AssignStatement assignment,
        NativeContext context)
    {
        EmitExpression(index.Target, context);
        _code.Push(0);
        EmitExpression(index.Index, context);
        _code.Push(0);
        _code.Pop(1);
        _code.Pop(2);
        _code.LslImm(1, 1, 3);
        _code.AddReg(1, 2, 1);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.Push(1);
        if (assignment.Operator == "=")
        {
            EmitExpression(assignment.Value, context);
        }
        else
        {
            _code.Ldr(0, 1, 0);
            _code.Push(0);
            EmitExpression(assignment.Value, context);
            _code.Pop(1);
            EmitCompound(assignment.Operator, InferArrayElement(index, context), assignment);
            _code.Pop(1);
        }

        if (assignment.Operator == "=")
        {
            _code.Pop(1);
        }

        _code.Str(0, 1, 0);
        return true;
    }

    private void EmitCompound(
        string @operator,
        NativeValueKind kind,
        YyNode node)
    {
        switch (@operator)
        {
            case "+=" when kind == NativeValueKind.Text:
                _code.MovReg(9, 0);
                _code.MovReg(0, 1);
                _code.MovReg(1, 9);
                _code.Bl(ConcatLabel);
                break;
            case "+=" when kind == NativeValueKind.Integer:
                _code.AddReg(0, 1, 0);
                break;
            case "-=" when kind == NativeValueKind.Integer:
                _code.SubReg(0, 1, 0);
                break;
            case "*=" when kind == NativeValueKind.Integer:
                _code.Mul(0, 1, 0);
                break;
            case "/=" when kind == NativeValueKind.Integer:
                _code.Sdiv(0, 1, 0);
                break;
            case "%=" when kind == NativeValueKind.Integer:
                EmitRemainder();
                break;
            default:
                Fail(node, $"macOS 原生后端暂不支持复合运算符“{@operator}”");
                break;
        }
    }

    private void EmitIf(IfStatement conditional, NativeContext context)
    {
        var elseLabel = NewLabel("if_else");
        var endLabel = NewLabel("if_end");
        EmitExpression(conditional.Condition, context);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, elseLabel);
        EmitBlock(conditional.Then, context);
        _code.B(endLabel);
        _code.MarkLabel(elseLabel);
        if (conditional.Else is not null)
        {
            EmitBlock(conditional.Else, context);
        }

        _code.MarkLabel(endLabel);
    }

    private void EmitWhile(WhileStatement loop, NativeContext context)
    {
        var start = NewLabel("while_start");
        var end = NewLabel("while_end");
        context.Loops.Push(new NativeLoop(start, end));
        _code.MarkLabel(start);
        EmitExpression(loop.Condition, context);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Eq, end);
        EmitBlock(loop.Body, context);
        _code.B(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
    }

    private void EmitCountLoop(CountLoopStatement loop, NativeContext context)
    {
        var slot = context.Slot(loop.VariableName, loop);
        var start = NewLabel("count_start");
        var update = NewLabel("count_update");
        var end = NewLabel("count_end");
        EmitExpression(loop.Start, context);
        _code.Str(0, 29, slot);
        context.Types[loop.VariableName] = NativeValueKind.Integer;
        context.Loops.Push(new NativeLoop(update, end));
        _code.MarkLabel(start);
        EmitExpression(loop.End, context);
        _code.Push(0);
        _code.Ldr(0, 29, slot);
        _code.Pop(1);
        _code.Cmp(0, 1);
        _code.Bcond(Arm64Condition.Gt, end);
        EmitBlock(loop.Body, context);
        _code.MarkLabel(update);
        EmitExpression(loop.Step, context);
        _code.Push(0);
        _code.Ldr(0, 29, slot);
        _code.Pop(1);
        _code.AddReg(0, 1, 0);
        _code.Str(0, 29, slot);
        _code.B(start);
        _code.MarkLabel(end);
        context.Loops.Pop();
    }

}
