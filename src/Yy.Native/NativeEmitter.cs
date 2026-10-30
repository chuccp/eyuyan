using Yy.Language.Syntax;
using Yy.Compiler.Forms;

namespace Yy.Native;

internal enum NativeValueKind
{
    Integer,
    Text,
    Boolean,
    Array,
    Object
}

internal sealed record NativeGeneration(
    NativeCodeBuffer Code,
    NativeDataBuffer Data,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Imports,
    bool GuiSubsystem);

internal sealed partial class NativeEmitter
{
    private readonly YyProgram _program;
    private readonly string _sourceName;
    private readonly YyFormModel? _form;
    private readonly NativeCodeBuffer _code = new();
    private readonly NativeDataBuffer _data = new();
    private readonly Dictionary<string, HashSet<string>> _imports =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["kernel32.dll"] =
        [
            "GetStdHandle", "WriteFile", "ExitProcess", "GetProcessHeap", "HeapAlloc",
            "lstrcmpA"
        ]
    };
    private readonly Dictionary<string, FunctionDeclaration> _functions;
    private readonly Dictionary<string, string> _functionLabels =
        new(StringComparer.Ordinal);
    private int _labelSequence;
    private string _newlineLabel = string.Empty;
    private string _trueLabel = string.Empty;
    private string _falseLabel = string.Empty;

    public NativeEmitter(
        YyProgram program,
        string sourceName,
        YyFormModel? form = null)
    {
        _program = program;
        _sourceName = sourceName;
        _form = form;
        _functions = program.Functions.ToDictionary(
            function => function.Name,
            StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            _functionLabels[function.Name] = "__fn_" + function.Name;
        }
    }

    public NativeGeneration Generate()
    {
        _newlineLabel = _data.AddUtf8String("\r\n");
        _trueLabel = _data.AddUtf8String("真");
        _falseLabel = _data.AddUtf8String("假");
        PrepareFileRuntime();
        if (_form is not null)
        {
            PrepareWin32Data();
        }

        EmitEntry();
        EmitMain();
        foreach (var function in _program.Functions)
        {
            EmitFunction(function);
        }

        EmitWriteBuffer();
        EmitPrintString();
        EmitPrintInteger();
        EmitAllocate();
        EmitCopyBytes();
        EmitConcatString();
        EmitCompareString();
        EmitArrayRuntime();
        EmitCloneString();
        EmitFileRuntime();
        if (_form is not null)
        {
            EmitWindowProcedure();
            EmitReadControlText();
            EmitWriteControlText();
        }
        return new NativeGeneration(
            _code,
            _data,
            _imports.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Order().ToArray(),
                StringComparer.OrdinalIgnoreCase),
            _form is not null);
    }

    private void EmitEntry()
    {
        if (_form is not null)
        {
            EmitWin32Entry();
            return;
        }

        _code.MarkLabel("__entry");
        _code.BeginFrame(32);
        _code.CallCode("__yy_main");
        _code.MovRcxRax();
        _code.CallImport("import:ExitProcess");
        _code.EndFrame();
    }

    private void EmitMain()
    {
        var context = CreateContext(_program.Main, null);
        _code.MarkLabel("__yy_main");
        EmitPrologue(context);
        context.EndLabel = NewLabel("main_end");
        EmitBlock(_program.Main, context);
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.MarkLabel(context.EndLabel);
        _code.EndFrame();
    }

    private void EmitFunction(FunctionDeclaration function)
    {
        var context = CreateContext(function.Body, function);
        _code.MarkLabel(_functionLabels[function.Name]);
        EmitPrologue(context);
        var registers = new[]
        {
            NativeRegister.Rcx,
            NativeRegister.Rdx,
            NativeRegister.R8,
            NativeRegister.R9
        };
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            if (index >= registers.Length)
            {
                Fail(function, "原生后端暂时只支持最多 4 个参数");
            }

            var parameter = function.Parameters[index];
            if (registers[index] != NativeRegister.Rcx)
            {
                _code.StoreRegisterToRbp(registers[index], context.Slots[parameter.Name]);
            }
            else
            {
                _code.StoreRcxToRbp(context.Slots[parameter.Name]);
            }

            context.Types[parameter.Name] = ParseType(parameter.TypeName, function);
        }

        context.EndLabel = NewLabel("fn_end");
        EmitBlock(function.Body, context);
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.MarkLabel(context.EndLabel);
        _code.EndFrame();
    }

    private NativeContext CreateContext(BlockStatement body, FunctionDeclaration? function)
    {
        var context = new NativeContext(function);
        if (function is not null)
        {
            foreach (var parameter in function.Parameters)
            {
                context.AddSlot(parameter.Name);
            }
        }

        CollectVariables(body, context);
        return context;
    }

    private void EmitPrologue(NativeContext context)
    {
        var frameSize = Align(context.Slots.Count * 8 + 32, 16);
        _code.BeginFrame(frameSize);
    }

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
                _code.StoreRaxToRbp(context.Slots[variable.Name]);
                var kind = variable.DeclaredType is null
                    ? Infer(variable.Initializer, context)
                    : ParseType(variable.DeclaredType, statement);
                context.Types[variable.Name] = kind;
                if (kind == NativeValueKind.Array)
                {
                    context.ArrayElementTypes[variable.Name] =
                        InferArrayElement(variable.Initializer, context);
                }
                else if (kind == NativeValueKind.Object &&
                         TryGetObjectShape(variable.Initializer, context) is { } objectShape)
                {
                    context.ObjectShapes[variable.Name] = objectShape;
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
                    _code.MovImmediate(NativeRegister.Rax, 0);
                }
                else
                {
                    EmitExpression(@return.Value, context);
                }

                if (context.EndLabel is null)
                {
                    Fail(@return, "返回位置尚未初始化");
                }

                _code.Jmp(context.EndLabel!);
                break;
            case BreakStatement:
                if (context.Loops.Count == 0)
                {
                    Fail(statement, "跳出不在循环内");
                }

                _code.Jmp(context.Loops.Peek().Break);
                break;
            case ContinueStatement:
                if (context.Loops.Count == 0)
                {
                    Fail(statement, "继续不在循环内");
                }

                _code.Jmp(context.Loops.Peek().Continue);
                break;
            default:
                Fail(statement, "当前原生后端暂不支持此语句");
                break;
        }
    }

    private void EmitAssignment(AssignStatement assignment, NativeContext context)
    {
        if (assignment.Target is IdentifierExpression identifier)
        {
            EmitExpression(assignment.Value, context);
            _code.StoreRaxToRbp(context.Slot(identifier.Name, assignment));
            var kind = Infer(assignment.Value, context);
            context.Types[identifier.Name] = kind;
            if (kind == NativeValueKind.Array)
            {
                context.ArrayElementTypes[identifier.Name] =
                    InferArrayElement(assignment.Value, context);
            }
            else if (kind == NativeValueKind.Object &&
                     TryGetObjectShape(assignment.Value, context) is { } objectShape)
            {
                context.ObjectShapes[identifier.Name] = objectShape;
            }

            return;
        }

        if (assignment.Target is MemberExpression member)
        {
            if (TryEmitObjectPropertyAssignment(member, assignment.Value, context) ||
                TryEmitControlPropertyAssignment(member, assignment.Value, context))
            {
                return;
            }
        }

        Fail(assignment, "原生后端暂不支持此赋值目标");
    }

    private void EmitIf(IfStatement conditional, NativeContext context)
    {
        var elseLabel = NewLabel("if_else");
        var endLabel = NewLabel("if_end");
        EmitExpression(conditional.Condition, context);
        _code.TestRaxRax();
        _code.JumpIfZero(elseLabel);
        EmitBlock(conditional.Then, context);
        _code.Jmp(endLabel);
        _code.MarkLabel(elseLabel);
        if (conditional.Else is not null)
        {
            EmitBlock(conditional.Else, context);
        }

        _code.MarkLabel(endLabel);
    }

    private void EmitExpression(Expression expression, NativeContext context)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                EmitLiteral(literal, context);
                break;
            case IdentifierExpression identifier:
                _code.LoadRaxFromRbp(context.Slot(identifier.Name, identifier));
                break;
            case ArrayExpression array:
                EmitArrayLiteral(array, context);
                break;
            case ObjectExpression obj:
                EmitObjectLiteral(obj, context);
                break;
            case IndexExpression index:
                EmitArrayIndex(index, context);
                break;
            case UnaryExpression unary:
                EmitExpression(unary.Operand, context);
                if (unary.Operator is "!" or "非")
                {
                    _code.TestRaxRax();
                    _code.SetConditional("==");
                }
                else if (unary.Operator == "-")
                {
                    _code.NegRax();
                }
                else
                {
                    Fail(unary, "不支持的一元运算");
                }

                break;
            case BinaryExpression binary:
                EmitBinary(binary, context);
                break;
            case CallExpression call:
                EmitCall(call, context);
                break;
            case MemberExpression member when TryEmitObjectPropertyRead(member, context):
                break;
            case MemberExpression member when TryEmitControlPropertyRead(member, context):
                break;
            default:
                Fail(expression, "当前原生后端暂不支持此表达式");
                break;
        }
    }

    private void EmitLiteral(LiteralExpression literal, NativeContext context)
    {
        switch (literal.Value)
        {
            case double number when Math.Truncate(number) == number:
                _code.MovImmediate(NativeRegister.Rax, (long)number);
                context.LastKind = NativeValueKind.Integer;
                break;
            case string text:
                var label = _data.AddUtf8String(text);
                _code.LeaRaxRip("data:" + label);
                context.LastKind = NativeValueKind.Text;
                break;
            case bool boolean:
                _code.MovImmediate(NativeRegister.Rax, boolean ? 1 : 0);
                context.LastKind = NativeValueKind.Boolean;
                break;
            default:
                Fail(literal, "原生数值暂时只支持整数");
                break;
        }
    }

    private void EmitBinary(BinaryExpression binary, NativeContext context)
    {
        if (binary.Operator is "且" or "&&" or "或" or "||")
        {
            EmitLogical(binary, context);
            return;
        }

        var leftKind = Infer(binary.Left, context);
        var rightKind = Infer(binary.Right, context);
        if (binary.Operator == "+" &&
            leftKind == NativeValueKind.Text &&
            rightKind == NativeValueKind.Text)
        {
            EmitExpression(binary.Left, context);
            _code.PushRax();
            EmitExpression(binary.Right, context);
            _code.MovRdxRax();
            _code.Pop(NativeRegister.Rcx);
            _code.CallCode("__yy_concat_string");
            context.LastKind = NativeValueKind.Text;
            return;
        }

        if (binary.Operator is "==" or "!=" &&
            leftKind == NativeValueKind.Text &&
            rightKind == NativeValueKind.Text)
        {
            EmitExpression(binary.Left, context);
            _code.PushRax();
            EmitExpression(binary.Right, context);
            _code.MovRdxRax();
            _code.Pop(NativeRegister.Rcx);
            _code.CallCode("__yy_compare_string");
            if (binary.Operator == "!=")
            {
                _code.TestRaxRax();
                _code.SetConditional("==");
            }

            context.LastKind = NativeValueKind.Boolean;
            return;
        }

        if (leftKind != NativeValueKind.Integer ||
            rightKind != NativeValueKind.Integer)
        {
            Fail(binary, $"原生运算“{binary.Operator}”当前只支持整数");
        }

        EmitExpression(binary.Left, context);
        _code.PushRax();
        EmitExpression(binary.Right, context);
        _code.Pop(NativeRegister.Rcx);
        switch (binary.Operator)
        {
            case "+":
                _code.AddRaxRcx();
                break;
            case "-":
                _code.SubRaxRcx();
                break;
            case "*":
                _code.ImulRaxRcx();
                break;
            case "/":
                _code.Cqo();
                _code.IdivRcx();
                break;
            case "==":
            case "!=":
            case "<":
            case "<=":
            case ">":
            case ">=":
                _code.CmpRcxRax();
                _code.SetConditional(binary.Operator);
                break;
            default:
                Fail(binary, $"原生后端暂不支持运算符“{binary.Operator}”");
                break;
        }
    }

    private void EmitLogical(BinaryExpression binary, NativeContext context)
    {
        var end = NewLabel("logic_end");
        EmitExpression(binary.Left, context);
        _code.TestRaxRax();
        if (binary.Operator is "且" or "&&")
        {
            _code.JumpIfZero(end);
            EmitExpression(binary.Right, context);
            _code.TestRaxRax();
            _code.SetConditional("!=");
        }
        else
        {
            _code.JumpIfNotZero(end);
            EmitExpression(binary.Right, context);
            _code.TestRaxRax();
            _code.SetConditional("!=");
        }

        _code.MarkLabel(end);
        context.LastKind = NativeValueKind.Boolean;
    }

    private void EmitCall(CallExpression call, NativeContext context)
    {
        if (call.Callee is not IdentifierExpression identifier)
        {
            Fail(call, "原生后端暂不支持间接调用");
            return;
        }

        if (identifier.Name is "输出" or "打印" or "提示")
        {
            EmitOutput(call, context);
            return;
        }

        if (identifier.Name == "取控件")
        {
            TryEmitControlLookup(call, context);
            return;
        }

        if (TryEmitFileBuiltin(call, context))
        {
            return;
        }

        if (identifier.Name == "长度" && call.Arguments.Count == 1)
        {
            EmitExpression(call.Arguments[0], context);
            var argumentKind = Infer(call.Arguments[0], context);
            if (argumentKind is NativeValueKind.Text or NativeValueKind.Array)
            {
                _code.MovRcxRax();
                _code.LoadRaxFromRcx();
            }

            context.LastKind = NativeValueKind.Integer;
            return;
        }

        if (identifier.Name == "追加")
        {
            EmitArrayAppend(call, context);
            return;
        }

        if (!_functions.TryGetValue(identifier.Name, out var function))
        {
            Fail(identifier, $"原生后端暂不支持函数“{identifier.Name}”");
            return;
        }

        if (function.Parameters.Count > 4)
        {
            Fail(call, "原生后端暂时只支持最多 4 个参数");
        }

        foreach (var argument in call.Arguments)
        {
            EmitExpression(argument, context);
            _code.PushRax();
        }

        var parameterRegisters = new[]
        {
            NativeRegister.Rcx,
            NativeRegister.Rdx,
            NativeRegister.R8,
            NativeRegister.R9
        };
        for (var index = call.Arguments.Count - 1; index >= 0; index--)
        {
            _code.Pop(parameterRegisters[index]);
        }

        _code.CallCode(_functionLabels[function.Name]);
        context.LastKind = FunctionKind(function);
    }

    private void EmitOutput(CallExpression call, NativeContext context)
    {
        if (call.Arguments.Count != 1)
        {
            Fail(call, "原生后端当前只支持一个输出参数");
        }

        EmitExpression(call.Arguments[0], context);
        switch (Infer(call.Arguments[0], context))
        {
            case NativeValueKind.Integer:
                _code.CallCode("__yy_print_int");
                break;
            case NativeValueKind.Text:
                _code.MovRcxRax();
                _code.CallCode("__yy_print_string");
                break;
            case NativeValueKind.Boolean:
                EmitBooleanPrint(context);
                break;
        }

        context.LastKind = NativeValueKind.Integer;
    }

    private void EmitBooleanPrint(NativeContext context)
    {
        var falseLabel = NewLabel("bool_false");
        var end = NewLabel("bool_end");
        _code.TestRaxRax();
        _code.JumpIfZero(falseLabel);
        _code.LeaRcxRip("data:" + _trueLabel);
        _code.Jmp(end);
        _code.MarkLabel(falseLabel);
        _code.LeaRcxRip("data:" + _falseLabel);
        _code.MarkLabel(end);
        _code.CallCode("__yy_print_string");
    }

    private void EmitWriteBuffer()
    {
        _code.MarkLabel("__yy_write_buffer");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.MovImmediate(NativeRegister.Rcx, -11);
        _code.CallImport("import:GetStdHandle");
        _code.StoreRaxToRbp(-24);
        _code.LoadRcxFromRbp(-24);
        _code.LoadRdxFromRbp(-8);
        _code.LoadR8FromRbp(-16);
        _code.LeaR9Rbp(-40);
        _code.StoreZeroRsp32();
        _code.CallImport("import:WriteFile");
        _code.EndFrame();
    }

    private void EmitPrintString()
    {
        _code.MarkLabel("__yy_print_string");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.LoadRcxFromRbp(-8);
        _code.LoadRdxFromRcx();
        _code.AddRcxByte(8);
        _code.CallCode("__yy_write_buffer");
        _code.LeaRcxRip("data:" + _newlineLabel);
        _code.LoadRdxFromRcx();
        _code.AddRcxByte(8);
        _code.CallCode("__yy_write_buffer");
        _code.EndFrame();
    }

    private void EmitPrintInteger()
    {
        var digits = NewLabel("int_digits");
        var singleDigit = NewLabel("int_single");
        var sign = NewLabel("int_sign");
        _code.MarkLabel("__yy_print_int");
        _code.BeginFrame(128);
        _code.StoreRaxToRbp(-8);
        _code.StoreByteRbp(-16, 0);
        _code.TestRaxRax();
        _code.JumpIfNotSigned(sign);
        _code.StoreByteRbp(-16, 1);
        _code.NegRax();
        _code.MarkLabel(sign);
        _code.MovR10RbpOffset(-24);
        _code.StoreByteR10(13);
        _code.IncR10();
        _code.StoreByteR10(10);
        _code.MovR11R10();
        _code.IncR11();
        _code.DecR10();
        _code.MovImmediate(NativeRegister.Rcx, 10);
        _code.TestRaxRax();
        _code.JumpIfNotZero(digits);
        _code.DecR10();
        _code.StoreByteR10((byte)'0');
        _code.Jmp(singleDigit);
        _code.MarkLabel(digits);
        _code.XorEdxEdx();
        _code.DivRcx();
        _code.AddDl((byte)'0');
        _code.DecR10();
        _code.StoreByteR10Rdx();
        _code.TestRaxRax();
        _code.JumpIfNotZero(digits);
        _code.MarkLabel(singleDigit);
        _code.CmpByteRbp(-16, 0);
        _code.JumpIfZero(sign + "_positive");
        _code.DecR10();
        _code.StoreByteR10((byte)'-');
        _code.MarkLabel(sign + "_positive");
        _code.MovRcxR10();
        _code.MovRdxR11SubR10();
        _code.CallCode("__yy_write_buffer");
        _code.EndFrame();
    }

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
            _ => FailKind(node, $"原生后端暂不支持类型“{typeName}”")
        };

    private NativeValueKind FailKind(YyNode node, string? message = null) =>
        throw new NativeCompileException(
            message ?? "无法推断原生表达式类型",
            node.Line,
            node.Column,
            _sourceName);

    private void Fail(YyNode node, string message) =>
        throw new NativeCompileException(message, node.Line, node.Column, _sourceName);

    private string NewLabel(string prefix) => $"__{prefix}_{_labelSequence++}";

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}

internal sealed class NativeContext(FunctionDeclaration? function)
{
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    public FunctionDeclaration? Function { get; } = function;
    public Dictionary<string, NativeValueKind> Types { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, NativeValueKind> ArrayElementTypes { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, NativeObjectShape> ObjectShapes { get; } =
        new(StringComparer.Ordinal);
    public Stack<NativeLoop> Loops { get; } = new();
    public string? EndLabel { get; set; }
    public NativeValueKind LastKind { get; set; }
    public IReadOnlyDictionary<string, int> Slots => _slots;

    public void AddSlot(string name)
    {
        if (!_slots.ContainsKey(name))
        {
            _slots[name] = (_slots.Count + 1) * -8;
        }
    }

    public int Slot(string name, YyNode node) =>
        _slots.TryGetValue(name, out var slot)
            ? slot
            : throw new NativeCompileException(
                $"变量“{name}”没有原生栈位置",
                node.Line,
                node.Column,
                null);
}

internal sealed record NativeLoop(string Continue, string Break);

public sealed class NativeCompileException(
    string message,
    int line,
    int column,
    string? sourceName) : Exception(message)
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
    public string? SourceName { get; } = sourceName;
}
