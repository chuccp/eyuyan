namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private const int StmSetImage = 0x0172;
    private string _gdiPlusTokenLabel = string.Empty;
    private string _gdiPlusInputLabel = string.Empty;

    private void EmitWriteControlPicture()
    {
        var initialized = NewLabel("gdiplus_initialized");
        _code.MarkLabel("__yy_set_control_picture");
        _code.BeginFrame(96);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LoadRcxRip("data:" + _gdiPlusTokenLabel);
        _code.TestRcxRcx();
        _code.JumpIfNotZero(initialized);
        _code.LeaRcxRip("data:" + _gdiPlusTokenLabel);
        _code.LeaRdxRip("data:" + _gdiPlusInputLabel);
        _code.MovImmediate(NativeRegister.R8, 0);
        _code.CallImport("import:GdiplusStartup");

        _code.MarkLabel(initialized);
        _code.LoadRcxFromRbp(-16);
        _code.CallCode("__yy_utf8_to_utf16");
        _code.MovRcxRax();
        _code.LeaRdxRbp(-24);
        _code.CallImport("import:GdipCreateBitmapFromFile");
        _code.LoadRcxFromRbp(-24);
        _code.LeaRdxRbp(-32);
        _code.MovImmediate(NativeRegister.R8, -1);
        _code.CallImport("import:GdipCreateHBITMAPFromBitmap");
        _code.LoadRcxFromRbp(-24);
        _code.CallImport("import:GdipDisposeImage");

        _code.LoadRcxFromRbp(-8);
        _code.MovImmediate(NativeRegister.Rdx, StmSetImage);
        _code.MovImmediate(NativeRegister.R8, 0);
        _code.LoadR9FromRbp(-32);
        _code.CallImport("import:SendMessageW");
        _code.MovImmediate(NativeRegister.Rax, 1);
        _code.EndFrame();
    }
}
