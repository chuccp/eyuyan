namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private void EmitAllocate()
    {
        _code.MarkLabel("__yy_alloc");
        _code.BeginFrame(32);
        _code.StoreRcxToRbp(-8);
        _code.CallImport("import:GetProcessHeap");
        _code.MovRcxRax();
        _code.MovImmediate(NativeRegister.Rdx, 8);
        _code.MovR8Rbp(-8);
        _code.CallImport("import:HeapAlloc");
        _code.EndFrame();
    }

    private void EmitConcatString()
    {
        _code.MarkLabel("__yy_concat_string");
        _code.BeginFrame(80);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LoadRaxFromRbp(-8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-24);
        _code.LoadRaxFromRbp(-16);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-32);
        _code.MovRcxRbp(-24);
        _code.AddRcxRax();
        _code.StoreRcxToRbp(-40);
        _code.AddRcxByte(9);
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-48);
        _code.MovRcxRbp(-40);
        _code.StoreRcxAtRax();

        _code.LoadRaxFromRbp(-48);
        _code.AddRaxByte(8);
        _code.MovRcxRax();
        _code.LoadRaxFromRbp(-8);
        _code.AddRaxByte(8);
        _code.MovRdxRax();
        _code.LoadR8FromRbp(-24);
        _code.CallCode("__yy_copy_bytes");
        _code.LoadRaxFromRbp(-48);
        _code.AddRaxByte(8);
        _code.LoadRcxFromRbp(-24);
        _code.AddRaxRcx();
        _code.MovRcxRax();
        _code.LoadRaxFromRbp(-16);
        _code.AddRaxByte(8);
        _code.MovRdxRax();
        _code.LoadR8FromRbp(-32);
        _code.CallCode("__yy_copy_bytes");
        _code.LoadRaxFromRbp(-48);
        _code.EndFrame();
    }

    private void EmitCopyBytes()
    {
        var loop = NewLabel("copy_loop");
        var end = NewLabel("copy_end");
        _code.MarkLabel("__yy_copy_bytes");
        _code.TestR8R8();
        _code.JumpIfZero(end);
        _code.MarkLabel(loop);
        _code.MovAlPtrRdx();
        _code.MovPtrRcxAl();
        _code.IncRcx();
        _code.IncRdx();
        _code.DecR8();
        _code.JumpIfNotZero(loop);
        _code.MarkLabel(end);
        _code.Ret();
    }

    private void EmitCompareString()
    {
        _code.MarkLabel("__yy_compare_string");
        _code.BeginFrame(32);
        _code.CallImport("import:lstrcmpA");
        _code.TestRaxRax();
        _code.SetConditional("==");
        _code.EndFrame();
    }
}
