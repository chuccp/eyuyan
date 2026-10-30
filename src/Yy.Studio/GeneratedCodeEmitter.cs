using System.Globalization;
using System.Text;
using Yy.Language.Syntax;
using Yy.Studio.Forms;

namespace Yy.Studio;

public sealed class GeneratedCodeEmitter
{
    private static readonly HashSet<string> BuiltinNames =
    [
        "输出", "打印", "提示", "断言", "长度", "类型", "文本", "数字", "整数", "小数",
        "绝对值", "平方根", "幂", "最大", "最小", "随机数", "范围", "包含", "分割", "连接",
        "替换", "修剪", "转大写", "转小写", "取子文本", "追加", "插入", "移除", "排序",
        "倒序", "键集", "值集", "当前时间", "格式化时间", "JSON解析", "JSON生成",
        "UTF8编码", "UTF8解码"
    ];

    private readonly StringBuilder _builder = new();
    private readonly Dictionary<string, string> _functionNames = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<FunctionDeclaration> _functions;
    private readonly YyProgram _program;
    private FunctionDeclaration? _currentFunction;
    private string _currentEnvironment = "__env";
    private int _indent;
    private int _temporary;

    private GeneratedCodeEmitter(YyProgram program)
    {
        _program = program;
        _functions = program.Functions;
        for (var index = 0; index < _functions.Count; index++)
        {
            _functionNames[_functions[index].Name] = "YyFunction_" + index;
        }
    }

    public static string Generate(YyProject project, YyProgram program)
    {
        var emitter = new GeneratedCodeEmitter(program);
        emitter.Emit(project);
        return emitter._builder.ToString();
    }

    private void Emit(YyProject project)
    {
        Line("using System;");
        Line("using System.Collections.Generic;");
        Line("using System.IO;");
        Line("using System.Linq;");
        Line("using System.Windows;");
        Line("using System.Windows.Controls;");
        Line("using System.Windows.Media;");
        Line("using Yy.Compiler.Runtime;");
        Line();
        Line("namespace YyCompiledApplication;");
        Line();
        Line("internal static class Program");
        Line("{");
        _indent++;
        Line("[STAThread]");
        Line("private static void Main()");
        Line("{");
        _indent++;
        Line("var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };");
        Line("try");
        Line("{");
        _indent++;
        Line("CompiledProgram.RunEntry();");
        var startupIndex = project.Forms
            .Select((form, index) => (form, index))
            .FirstOrDefault(pair => string.Equals(
                pair.form.Model.Name,
                project.StartupForm,
                StringComparison.Ordinal));
        if (startupIndex.form is not null)
        {
            Line($"var window = new YyForm_{startupIndex.index}();");
        }
        else
        {
            Line($"var window = ScriptRuntime.CreateOutputWindow({Quote(project.Name)});");
        }

        Line("application.MainWindow = window;");
        Line("application.Run(window);");
        _indent--;
        Line("}");
        Line("catch (Exception exception)");
        Line("{");
        _indent++;
        Line("try { File.WriteAllText(Path.Combine(Path.GetTempPath(), \"YyCompiledError.log\"), exception.ToString()); } catch { }");
        Line("MessageBox.Show(exception.Message, \"易语应用\", MessageBoxButton.OK, MessageBoxImage.Error);");
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        Line();
        Line("internal static class CompiledProgram");
        Line("{");
        _indent++;
        Line("private static readonly ScriptEnvironment Globals = new();");
        Line();
        EmitEntry();
        EmitEventDispatcher();
        foreach (var function in _functions)
        {
            EmitFunction(function);
        }

        _indent--;
        Line("}");
        Line();
        for (var index = 0; index < project.Forms.Count; index++)
        {
            EmitForm(project.Forms[index].Model, index);
        }
    }

    private void EmitEntry()
    {
        Line("public static void RunEntry()");
        Line("{");
        _indent++;
        Line("var __env = Globals;");
        EmitStatements(_program.Main.Statements, "__env");
        _indent--;
        Line("}");
        Line();
    }

    private void EmitEventDispatcher()
    {
        Line("public static void InvokeEvent(string name, IYyFormHost form)");
        Line("{");
        _indent++;
        Line("var previousForm = ScriptRuntime.CurrentForm;");
        Line("ScriptRuntime.CurrentForm = form;");
        Line("try");
        Line("{");
        _indent++;
        Line("switch (name)");
        Line("{");
        _indent++;
        foreach (var function in _functions)
        {
            Line($"case {Quote(function.Name)}:");
            _indent++;
            Line($"{_functionNames[function.Name]}(Array.Empty<object?>());");
            Line("break;");
            _indent--;
        }

        Line("default:");
        _indent++;
        Line("throw new ScriptRuntimeException($\"未知事件函数：{name}\");");
        _indent--;
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        Line("finally");
        Line("{");
        _indent++;
        Line("ScriptRuntime.CurrentForm = previousForm;");
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        Line();
    }

    private void EmitFunction(FunctionDeclaration function)
    {
        _currentFunction = function;
        _currentEnvironment = "__env";
        Line($"private static object? {_functionNames[function.Name]}(object?[] __args)");
        Line("{");
        _indent++;
        Line($"if (__args.Length > {function.Parameters.Count}) throw new ScriptRuntimeException({Quote($"子程序“{function.Name}”参数过多")});");
        Line("var __env = new ScriptEnvironment(Globals);");
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            var value = Next("__value");
            if (parameter.DefaultValue is null)
            {
                Line($"if (__args.Length <= {index}) throw new ScriptRuntimeException({Quote($"子程序“{function.Name}”缺少参数“{parameter.Name}”")});");
                Line($"object? {value} = __args[{index}];");
            }
            else
            {
                Line($"object? {value} = __args.Length > {index} ? __args[{index}] : {Expression(parameter.DefaultValue)};");
            }

            Line($"__env.Define({Quote(parameter.Name)}, {value}, false, {QuoteOrNull(parameter.TypeName)});");
        }

        EmitStatements(function.Body.Statements, "__env");
        Line($"return ScriptRuntime.ReturnValue(null, {QuoteOrNull(function.ReturnType)}, {Quote($"子程序“{function.Name}”的返回值")});");
        _indent--;
        Line("}");
        Line();
        _currentFunction = null;
    }

    private string Expression(Expression expression)
    {
        return expression switch
        {
            LiteralExpression literal => Literal(literal.Value),
            IdentifierExpression identifier =>
                $"ScriptRuntime.GetIdentifier({_currentEnvironment}, {Quote(identifier.Name)})",
            ArrayExpression array =>
                $"new List<object?> {{ {string.Join(", ", array.Items.Select(Expression))} }}",
            ObjectExpression obj =>
                $"new Dictionary<string, object?>(StringComparer.Ordinal) {{ {string.Join(", ", obj.Properties.Select(property => $"[{Quote(property.Key)}] = {Expression(property.Value)}"))} }}",
            UnaryExpression unary => unary.Operator switch
            {
                "非" or "!" => $"ScriptRuntime.Not({Expression(unary.Operand)})",
                "-" => $"ScriptRuntime.Negate({Expression(unary.Operand)})",
                "+" => $"ScriptRuntime.Positive({Expression(unary.Operand)})",
                _ => throw new InvalidOperationException($"不支持一元运算符“{unary.Operator}”")
            },
            BinaryExpression binary => Binary(binary),
            CallExpression call => Call(call),
            MemberExpression member =>
                $"ScriptRuntime.GetMember({Expression(member.Target)}, {Quote(member.Member)})",
            IndexExpression index =>
                $"ScriptRuntime.GetIndex({Expression(index.Target)}, {Expression(index.Index)})",
            _ => throw new InvalidOperationException("未知表达式。")
        };
    }

    private string Binary(BinaryExpression binary)
    {
        var left = Expression(binary.Left);
        var right = Expression(binary.Right);
        return binary.Operator switch
        {
            "且" or "&&" =>
                $"ScriptRuntime.And(() => {left}, () => {right})",
            "或" or "||" =>
                $"ScriptRuntime.Or(() => {left}, () => {right})",
            "??" =>
                $"ScriptRuntime.Coalesce(() => {left}, () => {right})",
            _ => $"ScriptRuntime.ApplyBinary({Quote(binary.Operator)}, {left}, {right})"
        };
    }

    private string Call(CallExpression call)
    {
        var arguments = $"new object?[] {{ {string.Join(", ", call.Arguments.Select(Expression))} }}";
        if (call.Callee is IdentifierExpression identifier)
        {
            if (_functionNames.TryGetValue(identifier.Name, out var method))
            {
                return $"{method}({arguments})";
            }

            if (BuiltinNames.Contains(identifier.Name))
            {
                return $"ScriptRuntime.CallBuiltin({Quote(identifier.Name)}, {arguments})";
            }
        }

        return $"throw new ScriptRuntimeException({Quote("不支持的函数调用")})";
    }

    private void EmitForm(YyFormModel form, int index)
    {
        Line($"internal sealed class YyForm_{index} : Window, IYyFormHost");
        Line("{");
        _indent++;
        Line("private readonly Dictionary<string, FrameworkElement> _yyControls = new(StringComparer.Ordinal);");
        Line("private bool _yyEventRunning;");
        Line("public IReadOnlyDictionary<string, FrameworkElement> YyControls => _yyControls;");
        Line();
        Line($"public YyForm_{index}()");
        Line("{");
        _indent++;
        Line($"Title = {Quote(form.Title)};");
        Line($"Width = {Number(form.Width)};");
        Line($"Height = {Number(form.Height)};");
        Line($"MinWidth = {Number(Math.Min(form.Width, 320))};");
        Line($"MinHeight = {Number(Math.Min(form.Height, 240))};");
        Line($"Background = ScriptRuntime.Brush({Quote(form.Background)});");
        Line("FontFamily = new FontFamily(\"Microsoft YaHei UI\");");
        Line("var canvas = new Canvas");
        Line("{");
        _indent++;
        Line($"Width = {Number(form.Width)},");
        Line($"Height = {Number(form.Height)},");
        Line($"Background = ScriptRuntime.Brush({Quote(form.Background)}),");
        Line("ClipToBounds = true");
        _indent--;
        Line("};");

        foreach (var control in form.Controls)
        {
            EmitControl(control);
        }

        Line("Content = new ScrollViewer");
        Line("{");
        _indent++;
        Line("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,");
        Line("VerticalScrollBarVisibility = ScrollBarVisibility.Auto,");
        Line("Content = canvas");
        _indent--;
        Line("};");
        if (!string.IsNullOrWhiteSpace(form.LoadEvent))
        {
            Line($"Loaded += (_, _) => InvokeEvent({Quote(form.LoadEvent)});");
        }

        if (!string.IsNullOrWhiteSpace(form.CloseEvent))
        {
            Line($"Closing += (_, _) => InvokeEvent({Quote(form.CloseEvent)});");
        }

        _indent--;
        Line("}");
        Line();
        Line("private void InvokeEvent(string name)");
        Line("{");
        _indent++;
        Line("if (_yyEventRunning) return;");
        Line("_yyEventRunning = true;");
        Line("try");
        Line("{");
        _indent++;
        Line("CompiledProgram.InvokeEvent(name, this);");
        _indent--;
        Line("}");
        Line("catch (Exception exception)");
        Line("{");
        _indent++;
        Line("MessageBox.Show(exception.Message, \"易语事件\", MessageBoxButton.OK, MessageBoxImage.Error);");
        _indent--;
        Line("}");
        Line("finally");
        Line("{");
        _indent++;
        Line("_yyEventRunning = false;");
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        _indent--;
        Line("}");
        Line();
    }

    private void EmitControl(YyControlModel control)
    {
        var variable = Next("__control");
        var type = control.Type switch
        {
            YyControlTypes.Button => "Button",
            YyControlTypes.Label => "TextBlock",
            YyControlTypes.TextBox => "TextBox",
            YyControlTypes.CheckBox => "CheckBox",
            YyControlTypes.RadioButton => "RadioButton",
            YyControlTypes.ComboBox => "ComboBox",
            YyControlTypes.ListBox => "ListBox",
            YyControlTypes.GroupBox => "GroupBox",
            _ => throw new InvalidOperationException($"不支持的控件类型：{control.Type}")
        };
        Line($"var {variable} = new {type}();");
        Line($"{variable}.Name = {Quote(control.Name)};");
        Line($"{variable}.Width = {Number(control.Width)};");
        Line($"{variable}.Height = {Number(control.Height)};");
        Line($"{variable}.IsEnabled = {(control.IsEnabled ? "true" : "false")};");
        Line($"{variable}.Visibility = {(control.IsVisible ? "Visibility.Visible" : "Visibility.Hidden")};");
        if (control.Type is YyControlTypes.CheckBox or YyControlTypes.RadioButton)
        {
            Line($"{variable}.IsChecked = {(control.IsChecked ? "true" : "false")};");
        }
        if (control.Type is YyControlTypes.Button or YyControlTypes.Label or YyControlTypes.TextBox
            or YyControlTypes.CheckBox or YyControlTypes.RadioButton or YyControlTypes.GroupBox)
        {
            var property = control.Type switch
            {
                YyControlTypes.Label => "Text",
                YyControlTypes.GroupBox => "Header",
                _ => "Content"
            };
            if (control.Type == YyControlTypes.TextBox)
            {
                property = "Text";
            }

            Line($"{variable}.{property} = {Quote(control.Text)};");
        }

        if (control.Type is YyControlTypes.ComboBox or YyControlTypes.ListBox)
        {
            var items = control.Text.Split(
                [',', '，'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var item in items)
            {
                Line($"{variable}.Items.Add({Quote(item)});");
            }
        }

        if (control.Type is not YyControlTypes.Label)
        {
            Line($"{variable}.FontSize = {Number(control.FontSize)};");
            Line($"{variable}.Foreground = ScriptRuntime.Brush({Quote(control.Foreground)});");
            Line($"{variable}.Background = ScriptRuntime.Brush({Quote(control.Background)});");
            Line($"{variable}.BorderBrush = ScriptRuntime.Brush(\"#B5C0CD\");");
            Line($"{variable}.Padding = new Thickness(8, 4, 8, 4);");
        }
        else
        {
            Line($"{variable}.FontSize = {Number(control.FontSize)};");
            Line($"{variable}.Foreground = ScriptRuntime.Brush({Quote(control.Foreground)});");
            Line($"{variable}.TextWrapping = TextWrapping.Wrap;");
        }

        Line($"Canvas.SetLeft({variable}, {Number(control.X)});");
        Line($"Canvas.SetTop({variable}, {Number(control.Y)});");
        Line($"canvas.Children.Add({variable});");
        Line($"_yyControls.Add({Quote(control.Name)}, {variable});");
        if (control.Type == YyControlTypes.Button && !string.IsNullOrWhiteSpace(control.ClickEvent))
        {
            Line($"{variable}.Click += (_, _) => InvokeEvent({Quote(control.ClickEvent)});");
        }

        if (control.Type == YyControlTypes.TextBox && !string.IsNullOrWhiteSpace(control.TextChangedEvent))
        {
            Line($"{variable}.TextChanged += (_, _) => InvokeEvent({Quote(control.TextChangedEvent)});");
        }

        if (control.Type is YyControlTypes.CheckBox or YyControlTypes.RadioButton &&
            !string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
        {
            Line($"{variable}.Checked += (_, _) => InvokeEvent({Quote(control.CheckedChangedEvent)});");
            Line($"{variable}.Unchecked += (_, _) => InvokeEvent({Quote(control.CheckedChangedEvent)});");
        }

        if (control.Type is YyControlTypes.ComboBox or YyControlTypes.ListBox &&
            !string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
        {
            Line($"{variable}.SelectionChanged += (_, _) => InvokeEvent({Quote(control.SelectionChangedEvent)});");
        }
    }

    private void EmitStatements(IReadOnlyList<Statement> statements, string environment)
    {
        var previousEnvironment = _currentEnvironment;
        _currentEnvironment = environment;
        try
        {
            foreach (var statement in statements)
            {
                EmitStatement(statement, environment);
            }
        }
        finally
        {
            _currentEnvironment = previousEnvironment;
        }
    }

    private void EmitScopedStatements(
        IReadOnlyList<Statement> statements,
        string parentEnvironment)
    {
        var environment = Next("__scope");
        Line($"var {environment} = new ScriptEnvironment({parentEnvironment});");
        EmitStatements(statements, environment);
    }

    private void EmitStatement(Statement statement, string environment)
    {
        switch (statement)
        {
            case VariableStatement variable:
                Line($"{environment}.Define({Quote(variable.Name)}, {Expression(variable.Initializer)}, {(variable.IsConstant ? "true" : "false")}, {QuoteOrNull(variable.DeclaredType)});");
                break;
            case ExpressionStatement expression:
                Line($"_ = {Expression(expression.Expression)};");
                break;
            case AssignStatement assignment:
                EmitAssignment(assignment, environment);
                break;
            case IfStatement conditional:
                Line($"if (ScriptRuntime.Truthy({Expression(conditional.Condition)}))");
                Line("{");
                _indent++;
                EmitScopedStatements(conditional.Then.Statements, environment);
                _indent--;
                Line("}");
                if (conditional.Else is not null)
                {
                    Line("else");
                    Line("{");
                    _indent++;
                    EmitScopedStatements(conditional.Else.Statements, environment);
                    _indent--;
                    Line("}");
                }

                break;
            case WhileStatement loop:
                Line($"while (ScriptRuntime.Truthy({Expression(loop.Condition)}))");
                Line("{");
                _indent++;
                EmitScopedLoop(loop.Body.Statements, environment);
                _indent--;
                Line("}");
                break;
            case CountLoopStatement loop:
                EmitCountLoop(loop, environment);
                break;
            case ForEachStatement loop:
                EmitForEach(loop, environment);
                break;
            case ReturnStatement @return:
                var returnValue = @return.Value is null ? "null" : Expression(@return.Value);
                Line($"return ScriptRuntime.ReturnValue({returnValue}, {QuoteOrNull(_currentFunction?.ReturnType)}, {Quote("子程序返回值")});");
                break;
            case BreakStatement:
                Line("throw new ScriptBreakSignal();");
                break;
            case ContinueStatement:
                Line("throw new ScriptContinueSignal();");
                break;
            case TryStatement attempt:
                EmitTry(attempt, environment);
                break;
        }
    }

    private void EmitAssignment(AssignStatement assignment, string environment)
    {
        var current = GetTarget(assignment.Target, environment);
        var value = Expression(assignment.Value);
        if (assignment.Operator != "=")
        {
            value = $"ScriptRuntime.ApplyBinary({Quote(assignment.Operator[..1])}, {current}, {value})";
        }

        switch (assignment.Target)
        {
            case IdentifierExpression identifier:
                Line($"{environment}.Assign({Quote(identifier.Name)}, {value});");
                break;
            case MemberExpression member:
                Line($"ScriptRuntime.SetMember({Expression(member.Target)}, {Quote(member.Member)}, {value});");
                break;
            case IndexExpression index:
                Line($"ScriptRuntime.SetIndex({Expression(index.Target)}, {Expression(index.Index)}, {value});");
                break;
        }
    }

    private string GetTarget(Expression target, string environment)
    {
        return target switch
        {
            IdentifierExpression identifier =>
                $"ScriptRuntime.GetIdentifier({environment}, {Quote(identifier.Name)})",
            MemberExpression member =>
                $"ScriptRuntime.GetMember({Expression(member.Target)}, {Quote(member.Member)})",
            IndexExpression index =>
                $"ScriptRuntime.GetIndex({Expression(index.Target)}, {Expression(index.Index)})",
            _ => throw new InvalidOperationException("无效的赋值目标。")
        };
    }

    private void EmitScopedLoop(IReadOnlyList<Statement> statements, string parentEnvironment)
    {
        Line("var __loopEnv = new ScriptEnvironment(" + parentEnvironment + ");");
        Line("try");
        Line("{");
        _indent++;
        EmitStatements(statements, "__loopEnv");
        _indent--;
        Line("}");
        Line("catch (ScriptContinueSignal) { continue; }");
        Line("catch (ScriptBreakSignal) { break; }");
    }

    private void EmitCountLoop(CountLoopStatement loop, string environment)
    {
        var start = Next("__start");
        var end = Next("__end");
        var step = Next("__step");
        var value = Next("__index");
        Line($"var {start} = ScriptRuntime.Number({Expression(loop.Start)});");
        Line($"var {end} = ScriptRuntime.Number({Expression(loop.End)});");
        Line($"var {step} = ScriptRuntime.Number({Expression(loop.Step)});");
        Line($"if ({step} == 0) throw new ScriptRuntimeException(\"循环步长不能为 0\");");
        Line($"for (var {value} = {start}; {step} > 0 ? {value} <= {end} : {value} >= {end}; {value} += {step})");
        Line("{");
        _indent++;
        Line($"var __loopEnv = new ScriptEnvironment({environment});");
        Line($"__loopEnv.Define({Quote(loop.VariableName)}, {value});");
        Line("try");
        Line("{");
        _indent++;
        EmitStatements(loop.Body.Statements, "__loopEnv");
        _indent--;
        Line("}");
        Line("catch (ScriptContinueSignal) { continue; }");
        Line("catch (ScriptBreakSignal) { break; }");
        _indent--;
        Line("}");
    }

    private void EmitForEach(ForEachStatement loop, string environment)
    {
        var item = Next("__item");
        Line($"foreach (var {item} in ScriptRuntime.Enumerate({Expression(loop.Collection)}))");
        Line("{");
        _indent++;
        Line($"var __loopEnv = new ScriptEnvironment({environment});");
        Line($"__loopEnv.Define({Quote(loop.VariableName)}, {item});");
        Line("try");
        Line("{");
        _indent++;
        EmitStatements(loop.Body.Statements, "__loopEnv");
        _indent--;
        Line("}");
        Line("catch (ScriptContinueSignal) { continue; }");
        Line("catch (ScriptBreakSignal) { break; }");
        _indent--;
        Line("}");
    }

    private void EmitTry(TryStatement attempt, string environment)
    {
        if (attempt.CatchBlock is null)
        {
            EmitScopedStatements(attempt.TryBlock.Statements, environment);
            return;
        }

        Line("try");
        Line("{");
        _indent++;
        EmitScopedStatements(attempt.TryBlock.Statements, environment);
        _indent--;
        Line("}");
        if (attempt.CatchBlock is not null)
        {
            Line("catch (ScriptRuntimeException __error)");
            Line("{");
            _indent++;
            Line("var __catchEnv = new ScriptEnvironment(" + environment + ");");
            if (!string.IsNullOrWhiteSpace(attempt.ErrorVariable))
            {
                Line($"__catchEnv.Define({Quote(attempt.ErrorVariable)}, __error.Message);");
            }

            EmitStatements(attempt.CatchBlock.Statements, "__catchEnv");
            _indent--;
            Line("}");
        }
    }

    private static string Literal(object? value)
    {
        return value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            double number => Number(number),
            string text => Quote(text),
            _ => "null"
        };
    }

    private static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture) + "D";

    private static string QuoteOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "null" : Quote(value);

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(character) =>
                    $"\\u{(int)character:X4}",
                _ => character.ToString()
            });
        }

        builder.Append('"');
        return builder.ToString();
    }

    private string Next(string prefix) => prefix + "_" + _temporary++;

    private void Line(string value = "")
    {
        if (value.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _builder.Append(value);
        }

        _builder.AppendLine();
    }
}
