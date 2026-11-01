namespace Yy.Native.MacOS;

internal sealed partial class MacNativeEmitter
{
    private const string WriteStreamLabel = "__yy_write_stream";
    private const string WriteAllLabel = "__yy_write_all";
    private const string PrintStringLabel = "__yy_print_string";
    private const string PrintIntegerLabel = "__yy_print_int";
    private const string IntToStringLabel = "__yy_int_to_string";
    private const string ConcatLabel = "__yy_concat_string";
    private const string CompareLabel = "__yy_compare_string";
    private const string ArrayNewLabel = "__yy_array_new";
    private const string ArrayAppendLabel = "__yy_array_append";
    private const string TextNewLabel = "__yy_text_new";
    private const string FileExistsLabel = "__yy_file_exists";
    private const string FileDeleteLabel = "__yy_file_delete";
    private const string FileReadLabel = "__yy_file_read";
    private const string FileWriteLabel = "__yy_file_write";
    private const string ListFilesLabel = "__yy_list_files";
    private const string RunProcessLabel = "__yy_run_process";

    private void EmitRuntimeHelpers()
    {
        EmitWriteStream();
        EmitWriteAll();
        EmitPrintString();
        EmitPrintInteger();
        EmitIntToString();
        EmitConcatString();
        EmitCompareString();
        EmitArrayNew();
        EmitArrayAppendRuntime();
        EmitTextNew();
    }

    private void EmitWriteStream()
    {
        var loop = NewLabel("write_loop");
        var success = NewLabel("write_success");
        var failed = NewLabel("write_failed");
        var done = NewLabel("write_done");
        _code.MarkLabel(WriteStreamLabel);
        _code.BeginFrame(64);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.Str(2, 29, -24);
        _code.MarkLabel(loop);
        _code.Ldr(3, 29, -24);
        _code.CmpImm(3, 0);
        _code.Bcond(Arm64Condition.Le, success);
        _code.Ldr(0, 29, -8);
        _code.Ldr(1, 29, -16);
        _code.Ldr(2, 29, -24);
        CallImport("_write");
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Le, failed);
        _code.Ldr(1, 29, -16);
        _code.AddReg(1, 1, 0);
        _code.Str(1, 29, -16);
        _code.Ldr(1, 29, -24);
        _code.SubReg(1, 1, 0);
        _code.Str(1, 29, -24);
        _code.B(loop);
        _code.MarkLabel(failed);
        _code.LoadImmediate(0, 0);
        _code.B(done);
        _code.MarkLabel(success);
        _code.LoadImmediate(0, 1);
        _code.MarkLabel(done);
        _code.EndFrame();
    }

    private void EmitWriteAll()
    {
        _code.MarkLabel(WriteAllLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.MovReg(2, 1);
        _code.MovReg(1, 0);
        _code.LoadImmediate(0, 1);
        _code.Bl(WriteStreamLabel);
        _code.EndFrame();
    }

    private void EmitPrintString()
    {
        _code.MarkLabel(PrintStringLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.Ldr(1, 0, NativeRuntimeLayout.CountOffset);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Bl(WriteAllLabel);
        _code.LoadAddress(0, _newlineLabel);
        _code.Ldr(1, 0, NativeRuntimeLayout.CountOffset);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Bl(WriteAllLabel);
        _code.EndFrame();
    }

    private void EmitPrintInteger()
    {
        var positive = NewLabel("print_int_positive");
        var digits = NewLabel("print_int_digits");
        var converted = NewLabel("print_int_converted");
        var noSign = NewLabel("print_int_no_sign");
        _code.MarkLabel(PrintIntegerLabel);
        _code.BeginFrame(128);
        _code.Str(0, 29, -8);
        _code.LoadImmediate(9, 0);
        _code.Str(9, 29, -16);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ge, positive);
        _code.LoadImmediate(9, 1);
        _code.Str(9, 29, -16);
        _code.Ldr(0, 29, -8);
        _code.Neg(0, 0);
        _code.Str(0, 29, -8);
        _code.MarkLabel(positive);
        _code.SubImm(9, 29, 40);
        _code.Str(9, 29, -24);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, digits);
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.LoadImmediate(10, '0');
        _code.Strb(10, 9, 0);
        _code.Str(9, 29, -24);
        _code.B(converted);
        _code.MarkLabel(digits);
        _code.Ldr(0, 29, -8);
        _code.LoadImmediate(10, 10);
        _code.Udiv(11, 0, 10);
        _code.Mul(12, 11, 10);
        _code.SubReg(12, 0, 12);
        _code.AddImm(12, 12, '0');
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.Strb(12, 9, 0);
        _code.Str(9, 29, -24);
        _code.Str(11, 29, -8);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, digits);
        _code.MarkLabel(converted);
        _code.Ldr(9, 29, -16);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, noSign);
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.LoadImmediate(10, '-');
        _code.Strb(10, 9, 0);
        _code.Str(9, 29, -24);
        _code.MarkLabel(noSign);
        _code.Ldr(0, 29, -24);
        _code.SubImm(1, 29, 40);
        _code.SubReg(1, 1, 0);
        _code.Bl(WriteAllLabel);
        _code.LoadAddress(0, _newlineLabel);
        _code.Ldr(1, 0, NativeRuntimeLayout.CountOffset);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Bl(WriteAllLabel);
        _code.EndFrame();
    }

    private void EmitIntToString()
    {
        var positive = NewLabel("int_text_positive");
        var digits = NewLabel("int_text_digits");
        var converted = NewLabel("int_text_converted");
        var noSign = NewLabel("int_text_no_sign");
        _code.MarkLabel(IntToStringLabel);
        _code.BeginFrame(192);
        _code.Str(0, 29, -8);
        _code.LoadImmediate(9, 0);
        _code.Str(9, 29, -16);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ge, positive);
        _code.LoadImmediate(9, 1);
        _code.Str(9, 29, -16);
        _code.Ldr(0, 29, -8);
        _code.Neg(0, 0);
        _code.Str(0, 29, -8);
        _code.MarkLabel(positive);
        _code.SubImm(9, 29, 80);
        _code.Str(9, 29, -24);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, digits);
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.LoadImmediate(10, '0');
        _code.Strb(10, 9, 0);
        _code.Str(9, 29, -24);
        _code.B(converted);
        _code.MarkLabel(digits);
        _code.Ldr(0, 29, -8);
        _code.LoadImmediate(10, 10);
        _code.Udiv(11, 0, 10);
        _code.Mul(12, 11, 10);
        _code.SubReg(12, 0, 12);
        _code.AddImm(12, 12, '0');
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.Strb(12, 9, 0);
        _code.Str(9, 29, -24);
        _code.Str(11, 29, -8);
        _code.Ldr(0, 29, -8);
        _code.CmpImm(0, 0);
        _code.Bcond(Arm64Condition.Ne, digits);
        _code.MarkLabel(converted);
        _code.Ldr(9, 29, -16);
        _code.CmpImm(9, 0);
        _code.Bcond(Arm64Condition.Eq, noSign);
        _code.Ldr(9, 29, -24);
        _code.SubImm(9, 9, 1);
        _code.LoadImmediate(10, '-');
        _code.Strb(10, 9, 0);
        _code.Str(9, 29, -24);
        _code.MarkLabel(noSign);
        _code.SubImm(1, 29, 80);
        _code.Ldr(0, 29, -24);
        _code.SubReg(1, 1, 0);
        _code.Str(1, 29, -32);
        _code.AddImm(0, 1, 9);
        CallImport("_malloc");
        _code.Str(0, 29, -40);
        _code.Ldr(1, 29, -32);
        _code.Str(1, 0, 0);
        _code.Ldr(0, 29, -40);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(1, 29, -24);
        _code.Ldr(2, 29, -32);
        CallImport("_memcpy");
        _code.Ldr(0, 29, -40);
        _code.Ldr(1, 29, -32);
        _code.AddReg(0, 0, 1);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(2, 0);
        _code.Strb(2, 0, 0);
        _code.Ldr(0, 29, -40);
        _code.EndFrame();
    }

    private void EmitConcatString()
    {
        _code.MarkLabel(ConcatLabel);
        _code.BeginFrame(64);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.Ldr(9, 0, NativeRuntimeLayout.CountOffset);
        _code.Str(9, 29, -24);
        _code.Ldr(9, 1, NativeRuntimeLayout.CountOffset);
        _code.Str(9, 29, -32);
        _code.Ldr(9, 29, -24);
        _code.Ldr(10, 29, -32);
        _code.AddReg(9, 9, 10);
        _code.Str(9, 29, -40);
        _code.AddImm(0, 9, 9);
        CallImport("_malloc");
        _code.Str(0, 29, -48);
        _code.Ldr(9, 29, -40);
        _code.Str(9, 0, NativeRuntimeLayout.CountOffset);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(1, 29, -8);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(2, 29, -24);
        CallImport("_memcpy");
        _code.Ldr(0, 29, -48);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(9, 29, -24);
        _code.AddReg(0, 0, 9);
        _code.Ldr(1, 29, -16);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(2, 29, -32);
        CallImport("_memcpy");
        _code.Ldr(0, 29, -48);
        _code.Ldr(9, 29, -40);
        _code.AddReg(0, 0, 9);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(10, 0);
        _code.Strb(10, 0, 0);
        _code.Ldr(0, 29, -48);
        _code.EndFrame();
    }

    private void EmitCompareString()
    {
        _code.MarkLabel(CompareLabel);
        _code.BeginFrame(16);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.AddImm(1, 1, NativeRuntimeLayout.PayloadOffset);
        CallImport("_strcmp");
        _code.CmpImm(0, 0);
        _code.Cset(0, Arm64Condition.Eq);
        _code.EndFrame();
    }

    private void EmitArrayNew()
    {
        _code.MarkLabel(ArrayNewLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.LslImm(0, 0, 3);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        CallImport("_malloc");
        _code.Str(0, 29, -16);
        _code.Ldr(1, 29, -8);
        _code.Str(1, 0, NativeRuntimeLayout.CountOffset);
        _code.Ldr(0, 29, -16);
        _code.EndFrame();
    }

    private void EmitArrayAppendRuntime()
    {
        _code.MarkLabel(ArrayAppendLabel);
        _code.BeginFrame(64);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.Ldr(9, 0, NativeRuntimeLayout.CountOffset);
        _code.Str(9, 29, -24);
        _code.AddImm(9, 9, 1);
        _code.Str(9, 29, -32);
        _code.LslImm(0, 9, 3);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        CallImport("_malloc");
        _code.Str(0, 29, -40);
        _code.Ldr(9, 29, -24);
        _code.AddImm(9, 9, 1);
        _code.LslImm(2, 9, 3);
        _code.Ldr(0, 29, -40);
        _code.Ldr(1, 29, -8);
        CallImport("_memcpy");
        _code.Ldr(0, 29, -40);
        _code.Ldr(9, 29, -32);
        _code.Str(9, 0, NativeRuntimeLayout.CountOffset);
        _code.Ldr(0, 29, -40);
        _code.Ldr(9, 29, -24);
        _code.LslImm(9, 9, 3);
        _code.AddReg(0, 0, 9);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(1, 29, -16);
        _code.Str(1, 0, 0);
        _code.Ldr(0, 29, -40);
        _code.EndFrame();
    }

    private void EmitTextNew()
    {
        _code.MarkLabel(TextNewLabel);
        _code.BeginFrame(32);
        _code.Str(0, 29, -8);
        _code.Str(1, 29, -16);
        _code.AddImm(0, 1, NativeRuntimeLayout.PayloadOffset + 1);
        CallImport("_malloc");
        _code.Str(0, 29, -24);
        _code.Ldr(1, 29, -16);
        _code.Str(1, 0, NativeRuntimeLayout.CountOffset);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.Ldr(1, 29, -8);
        _code.Ldr(2, 29, -16);
        CallImport("_memcpy");
        _code.Ldr(0, 29, -24);
        _code.Ldr(9, 29, -16);
        _code.AddReg(0, 0, 9);
        _code.AddImm(0, 0, NativeRuntimeLayout.PayloadOffset);
        _code.LoadImmediate(10, 0);
        _code.Strb(10, 0, 0);
        _code.Ldr(0, 29, -24);
        _code.EndFrame();
    }
}
