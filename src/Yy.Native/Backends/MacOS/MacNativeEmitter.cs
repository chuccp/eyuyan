using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed record MacNativeGeneration(
    Arm64CodeBuffer Code,
    NativeDataBuffer Data,
    IReadOnlyList<MacImport> Imports,
    IReadOnlyList<string> Libraries);

/// <summary>
/// AArch64 代码生成。语言语义由 <see cref="NativeTypeAnalyzer"/> 统一提供，
/// 本类只负责寄存器分配、栈帧和 Mach-O 调用约定。
/// </summary>
internal sealed partial class MacNativeEmitter
{
    private readonly YyProgram _program;
    private readonly string _sourceName;
    private readonly YyBuildTarget _target;
    private readonly IReadOnlyList<YyFormModel> _forms;
    private readonly YyFormModel? _startupForm;
    private readonly string? _projectDirectory;
    private readonly Arm64CodeBuffer _code = new();
    private readonly NativeDataBuffer _data = new();
    private readonly HashSet<MacImport> _imports = [];
    private readonly HashSet<string> _libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _functions;
    private readonly NativeTypeAnalyzer _semantics;
    private readonly Dictionary<string, string> _functionLabels =
        new(StringComparer.Ordinal);
    private int _labelSequence;
    private string _newlineLabel = string.Empty;
    private string _trueLabel = string.Empty;
    private string _falseLabel = string.Empty;
    private string _emptyLabel = string.Empty;
    private string _slashLabel = string.Empty;
    private string _wildcardLabel = string.Empty;
    private string _quoteLabel = string.Empty;
    private string _quoteSpaceLabel = string.Empty;

    public MacNativeEmitter(
        YyProgram program,
        string sourceName,
        YyBuildTarget target,
        IReadOnlyList<YyFormModel>? forms = null,
        YyFormModel? startupForm = null,
        string? projectDirectory = null)
    {
        _program = program;
        _sourceName = sourceName;
        _target = target;
        _forms = forms ?? [];
        _startupForm = startupForm;
        _projectDirectory = projectDirectory;
        _functions = program.Functions.ToDictionary(
            function => function.Name,
            StringComparer.Ordinal);
        _semantics = new NativeTypeAnalyzer(
            _functions,
            program.Structs.Select(structure => structure.Name)
                .Concat(program.Interfaces.Select(contract => contract.Name))
                .ToHashSet(StringComparer.Ordinal),
            sourceName,
            HasForms ? TryInferPlatform : null);
        foreach (var function in program.Functions)
        {
            _functionLabels[function.Name] = "__fn_" + function.Name;
        }

        _libraries.Add(MacLibraries.System);
    }

    private bool HasForms => _forms.Count > 0;

    public MacNativeGeneration Generate()
    {
        PrepareRuntimeData();
        if (HasForms)
        {
            PrepareAppKitData();
            _libraries.Add(MacLibraries.ObjectiveC);
            _libraries.Add(MacLibraries.Foundation);
            _libraries.Add(MacLibraries.AppKit);
        }

        EmitEntry();
        EmitMain();
        foreach (var function in _program.Functions)
        {
            EmitFunction(function);
        }

        EmitRuntimeHelpers();
        EmitFileRuntime();
        if (HasForms)
        {
            EmitAppKitRuntime();
        }

        var orderedLibraries = MacLibraries.All
            .Where(_libraries.Contains)
            .Concat(_libraries
                .Where(library => !MacLibraries.All.Contains(library))
                .Order(StringComparer.Ordinal))
            .ToArray();
        var imports = _imports
            .OrderBy(item => item.Library, StringComparer.Ordinal)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
        return new MacNativeGeneration(
            _code,
            _data,
            imports,
            orderedLibraries);
    }

    private void PrepareRuntimeData()
    {
        _newlineLabel = _data.AddUtf8String(NativeRuntimeLayout.Newline(_target));
        _trueLabel = _data.AddUtf8String("真");
        _falseLabel = _data.AddUtf8String("假");
        _emptyLabel = _data.AddUtf8String(string.Empty);
        _slashLabel = _data.AddUtf8String("/");
        _wildcardLabel = _data.AddUtf8String("*");
        _quoteLabel = _data.AddUtf8String("\"");
        _quoteSpaceLabel = _data.AddUtf8String("\" ");
    }

    private void EmitEntry()
    {
        if (HasForms)
        {
            EmitAppKitEntry();
            return;
        }

        _code.MarkLabel("__entry");
        _code.BeginFrame(16);
        _code.Bl("__yy_main");
        CallImport("_exit");
        _code.EndFrame();
    }

    private void EmitMain()
    {
        var context = CreateContext(_program.Main, null);
        _code.MarkLabel("__yy_main");
        EmitPrologue(context);
        context.EndLabel = NewLabel("main_end");
        EmitBlock(_program.Main, context);
        _code.LoadImmediate(0, 0);
        _code.MarkLabel(context.EndLabel);
        _code.EndFrame();
    }

    private void EmitFunction(FunctionDeclaration function)
    {
        var context = CreateContext(function.Body, function);
        _code.MarkLabel(_functionLabels[function.Name]);
        EmitPrologue(context);
        if (function.Parameters.Count > 8)
        {
            Fail(function, "macOS 原生后端当前最多支持 8 个参数");
        }

        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            _code.Str(index, 29, context.Slots[parameter.Name]);
            context.Types[parameter.Name] = ParseType(parameter.TypeName, function);
        }

        context.EndLabel = NewLabel("fn_end");
        EmitBlock(function.Body, context);
        _code.LoadImmediate(0, 0);
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

    private void EmitExpression(Expression expression, NativeContext context)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                EmitLiteral(literal);
                break;
            case IdentifierExpression identifier:
                _code.Ldr(0, 29, context.Slot(identifier.Name, identifier));
                context.LastKind = Infer(identifier, context);
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
                EmitUnary(unary, context);
                break;
            case BinaryExpression binary:
                EmitBinary(binary, context);
                break;
            case CallExpression call:
                EmitCall(call, context);
                break;
            case MemberExpression member when TryEmitObjectPropertyRead(member, context):
                break;
            case MemberExpression member when TryEmitWindowStatePropertyRead(member, context):
                break;
            case MemberExpression member when TryEmitControlPropertyRead(member, context):
                break;
            default:
                Fail(expression, "当前 macOS 原生后端暂不支持此表达式");
                break;
        }
    }

    private void EmitLiteral(LiteralExpression literal)
    {
        switch (literal.Value)
        {
            case double number when Math.Truncate(number) == number:
                _code.LoadImmediate(0, (long)number);
                break;
            case string text:
                _code.LoadAddress(0, _data.AddUtf8String(text));
                break;
            case bool boolean:
                _code.LoadImmediate(0, boolean ? 1 : 0);
                break;
            default:
                Fail(literal, "原生数值暂时只支持整数");
                break;
        }
    }

    private void EmitUnary(UnaryExpression unary, NativeContext context)
    {
        EmitExpression(unary.Operand, context);
        switch (unary.Operator)
        {
            case "!" or "非":
                _code.CmpImm(0, 0);
                _code.Cset(0, Arm64Condition.Eq);
                context.LastKind = NativeValueKind.Boolean;
                break;
            case "-":
                _code.Neg(0, 0);
                context.LastKind = NativeValueKind.Integer;
                break;
            default:
                Fail(unary, "不支持的一元运算");
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
            _code.Push(0);
            EmitExpression(binary.Right, context);
            _code.Pop(1);
            _code.MovReg(9, 0);
            _code.MovReg(0, 1);
            _code.MovReg(1, 9);
            _code.Bl(ConcatLabel);
            context.LastKind = NativeValueKind.Text;
            return;
        }

        if (binary.Operator is "==" or "!=" &&
            leftKind == NativeValueKind.Text &&
            rightKind == NativeValueKind.Text)
        {
            EmitExpression(binary.Left, context);
            _code.Push(0);
            EmitExpression(binary.Right, context);
            _code.Pop(1);
            _code.MovReg(9, 0);
            _code.MovReg(0, 1);
            _code.MovReg(1, 9);
            _code.Bl(CompareLabel);
            if (binary.Operator == "!=")
            {
                _code.CmpImm(0, 0);
                _code.Cset(0, Arm64Condition.Eq);
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
        _code.Push(0);
        EmitExpression(binary.Right, context);
        _code.Pop(1);
        switch (binary.Operator)
        {
            case "+":
                _code.AddReg(0, 1, 0);
                break;
            case "-":
                _code.SubReg(0, 1, 0);
                break;
            case "*":
                _code.Mul(0, 1, 0);
                break;
            case "/":
                _code.Sdiv(0, 1, 0);
                break;
            case "%":
                EmitRemainder();
                break;
            case "==":
            case "!=":
            case "<":
            case "<=":
            case ">":
            case ">=":
                _code.Cmp(1, 0);
                _code.Cset(0, ConditionOf(binary.Operator));
                context.LastKind = NativeValueKind.Boolean;
                return;
            default:
                Fail(binary, $"macOS 原生后端暂不支持运算符“{binary.Operator}”");
                break;
        }

        context.LastKind = NativeValueKind.Integer;
    }

    private void EmitRemainder()
    {
        _code.MovReg(9, 1);
        _code.Sdiv(10, 9, 0);
        _code.Mul(9, 10, 0);
        _code.SubReg(0, 1, 9);
    }

    private static Arm64Condition ConditionOf(string @operator) => @operator switch
    {
        "==" => Arm64Condition.Eq,
        "!=" => Arm64Condition.Ne,
        "<" => Arm64Condition.Lt,
        "<=" => Arm64Condition.Le,
        ">" => Arm64Condition.Gt,
        ">=" => Arm64Condition.Ge,
        _ => throw new ArgumentOutOfRangeException(nameof(@operator))
    };

    private void EmitLogical(BinaryExpression binary, NativeContext context)
    {
        var end = NewLabel("logic_end");
        var shortcut = NewLabel("logic_shortcut");
        EmitExpression(binary.Left, context);
        _code.CmpImm(0, 0);
        if (binary.Operator is "且" or "&&")
        {
            _code.Bcond(Arm64Condition.Eq, end);
            EmitExpression(binary.Right, context);
            _code.CmpImm(0, 0);
            _code.Cset(0, Arm64Condition.Ne);
        }
        else
        {
            _code.Bcond(Arm64Condition.Ne, shortcut);
            EmitExpression(binary.Right, context);
            _code.CmpImm(0, 0);
            _code.Cset(0, Arm64Condition.Ne);
            _code.B(end);
            _code.MarkLabel(shortcut);
            _code.LoadImmediate(0, 1);
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

        if (NativeBuiltinCatalog.IsOutput(identifier.Name))
        {
            EmitOutput(call, context);
            return;
        }

        if (HasForms)
        {
            if (NativeBuiltinCatalog.IsControlLookup(identifier.Name) &&
                TryEmitControlLookup(call, context))
            {
                return;
            }

            if (NativeBuiltinCatalog.IsWindowCommand(identifier.Name) &&
                TryEmitWindowCommand(call, context))
            {
                return;
            }
        }

        if (NativeBuiltinCatalog.IsWindowCommand(identifier.Name) ||
            NativeBuiltinCatalog.IsControlLookup(identifier.Name))
        {
            Fail(identifier, "界面命令需要窗体工程，且当前 macOS AppKit 后端未支持该用法");
        }

        if (TryEmitCoreBuiltin(call, identifier, context) ||
            TryEmitFileBuiltin(call, identifier, context) ||
            TryEmitProcessBuiltin(call, identifier, context))
        {
            return;
        }

        if (!_functions.TryGetValue(identifier.Name, out var function))
        {
            Fail(identifier, $"原生后端暂不支持函数“{identifier.Name}”");
            return;
        }

        if (call.Arguments.Count > 8)
        {
            Fail(call, "macOS 原生后端暂时只支持最多 8 个参数");
        }

        foreach (var argument in call.Arguments)
        {
            EmitExpression(argument, context);
            _code.Push(0);
        }

        for (var index = call.Arguments.Count - 1; index >= 0; index--)
        {
            _code.Pop(index);
        }

        _code.Bl(_functionLabels[function.Name]);
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
                _code.Bl(PrintIntegerLabel);
                break;
            case NativeValueKind.Text:
                _code.Bl(PrintStringLabel);
                break;
            case NativeValueKind.Boolean:
                EmitBooleanPrint();
                break;
        }

        context.LastKind = NativeValueKind.Integer;
    }

    private void EmitBooleanPrint()
    {
        var falseLabel = NewLabel("bool_false");
        var end = NewLabel("bool_end");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, end);
        _code.LoadAddress(0, _falseLabel);
        _code.B(falseLabel);
        _code.MarkLabel(end);
        _code.LoadAddress(0, _trueLabel);
        _code.MarkLabel(falseLabel);
        _code.Bl(PrintStringLabel);
    }

    private void RecordKind(string name, Expression value, NativeContext context)
    {
        var kind = Infer(value, context);
        context.Types[name] = kind;
        if (kind == NativeValueKind.Array)
        {
            context.ArrayElementTypes[name] = InferArrayElement(value, context);
        }
        else if (kind == NativeValueKind.Object &&
                 TryGetObjectShape(value, context) is { } shape)
        {
            context.ObjectShapes[name] = shape;
        }
    }

    private void CallImport(string symbol, string? library = null)
    {
        var resolved = library ?? MacLibraries.System;
        _libraries.Add(resolved);
        _imports.Add(new MacImport(resolved, symbol));
        _code.CallImport(symbol);
    }

    private NativeValueKind Infer(Expression expression, NativeContext context) =>
        _semantics.Infer(expression, context);

    private NativeValueKind InferArrayElement(Expression expression, NativeContext context) =>
        _semantics.InferArrayElement(expression, context);

    private NativeValueKind ParseType(string? typeName, YyNode node) =>
        _semantics.ParseType(typeName, node);

    private NativeValueKind FunctionKind(FunctionDeclaration function) =>
        _semantics.FunctionKind(function);

    private NativeObjectField? TryGetObjectField(
        MemberExpression member,
        NativeContext context) =>
        _semantics.TryGetObjectField(member, context);

    private NativeObjectShape? TryGetObjectShape(
        Expression expression,
        NativeContext context) =>
        _semantics.TryGetObjectShape(expression, context);

    private void Fail(YyNode node, string message) =>
        throw new NativeCompileException(message, node.Line, node.Column, _sourceName);

    private string NewLabel(string prefix) => $"__mac_{prefix}_{_labelSequence++}";

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
