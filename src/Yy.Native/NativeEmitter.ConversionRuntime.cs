namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private void EmitConversionRuntime()
    {
        var digits = NewLabel("to_string_digits");
        var single = NewLabel("to_string_single");
        var sign = NewLabel("to_string_sign");
        var positive = NewLabel("to_string_positive");
        _code.MarkLabel("__yy_int_to_string");
        _code.BeginFrame(128);
        _code.StoreRaxToRbp(-8);
        _code.StoreByteRbp(-16, 0);
        _code.TestRaxRax();
        _code.JumpIfNotSigned(sign);
        _code.StoreByteRbp(-16, 1);
        _code.NegRax();
        _code.MarkLabel(sign);
        _code.MovR10RbpOffset(-96);
        _code.StoreByteR10(0);
        _code.MovR11R10();
        _code.MovImmediate(NativeRegister.Rcx, 10);
        _code.TestRaxRax();
        _code.JumpIfNotZero(digits);
        _code.DecR10();
        _code.StoreByteR10((byte)'0');
        _code.Jmp(single);
        _code.MarkLabel(digits);
        _code.XorEdxEdx();
        _code.DivRcx();
        _code.AddDl((byte)'0');
        _code.DecR10();
        _code.StoreByteR10Rdx();
        _code.TestRaxRax();
        _code.JumpIfNotZero(digits);
        _code.MarkLabel(single);
        _code.CmpByteRbp(-16, 0);
        _code.JumpIfZero(positive);
        _code.DecR10();
        _code.StoreByteR10((byte)'-');
        _code.MarkLabel(positive);
        _code.MovRcxR10();
        _code.MovRdxR11SubR10();
        _code.StoreRcxToRbp(-40);
        _code.StoreRdxToRbp(-48);
        _code.LoadRcxFromRbp(-48);
        _code.AddRcxByte(9);
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-56);
        _code.LoadRcxFromRbp(-48);
        _code.StoreRcxAtRax();
        _code.LoadRaxFromRbp(-56);
        _code.AddRaxByte(8);
        _code.MovRcxRax();
        _code.LoadRdxFromRbp(-40);
        _code.LoadR8FromRbp(-48);
        _code.CallCode("__yy_copy_bytes");
        _code.LoadRaxFromRbp(-56);
        _code.EndFrame();
    }
}
