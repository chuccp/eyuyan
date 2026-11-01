using Yy.Language.Syntax;

namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private bool TryEmitCoreBuiltin(
        CallExpression call,
        IdentifierExpression identifier,
        NativeContext context)
    {
        switch (identifier.Name)
        {
            case "数字" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                if (Infer(call.Arguments[0], context) == NativeValueKind.Text)
                {
                    _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
                    _code.LoadImmediate(1, 0);
                    _code.LoadImmediate(2, 10);
                    CallImport("_strtoll");
                }

                context.LastKind = NativeValueKind.Integer;
                return true;
            case "文本" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                if (Infer(call.Arguments[0], context) != NativeValueKind.Text)
                {
                    _code.Bl(IntToStringLabel);
                }

                context.LastKind = NativeValueKind.Text;
                return true;
            case "长度" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                if (Infer(call.Arguments[0], context) is
                    NativeValueKind.Text or NativeValueKind.Array)
                {
                    _code.Ldr(0, 0, NativeRuntimeLayout.CountOffset);
                }

                context.LastKind = NativeValueKind.Integer;
                return true;
            case "追加":
                EmitArrayAppend(call, context);
                return true;
            default:
                return false;
        }
    }
}
