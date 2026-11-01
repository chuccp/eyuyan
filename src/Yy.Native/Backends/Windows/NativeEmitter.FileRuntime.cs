using Yy.Language.Syntax;

namespace Yy.Native;

internal sealed partial class NativeEmitter
{
    private string _emptyTextLabel = string.Empty;
    private string _pathSeparatorLabel = string.Empty;
    private string _anyFilePatternLabel = string.Empty;
    private string _fileWideBufferLabel = string.Empty;
    private string _fileUtf8BufferLabel = string.Empty;

    private void PrepareFileRuntime()
    {
        foreach (var name in new[]
                 {
                     "CreateFileW", "ReadFile", "WriteFile", "GetFileSizeEx",
                     "CloseHandle", "GetFileAttributesW", "DeleteFileW",
                     "FindFirstFileW", "FindNextFileW", "FindClose",
                     "MultiByteToWideChar", "WideCharToMultiByte"
                 })
        {
            AddImport("kernel32.dll", name);
        }

        _emptyTextLabel = _data.AddUtf8String(string.Empty);
        _pathSeparatorLabel = _data.AddUtf8String("\\");
        _anyFilePatternLabel = _data.AddUtf8String("*");
        _fileWideBufferLabel = "file_wide_buffer";
        _data.Align(8);
        _data.MarkLabel(_fileWideBufferLabel);
        _data.AddZeroes(8192);
        _fileUtf8BufferLabel = "file_utf8_buffer";
        _data.Align(8);
        _data.MarkLabel(_fileUtf8BufferLabel);
        _data.AddZeroes(8192);
    }

    private bool TryEmitFileBuiltin(CallExpression call, NativeContext context)
    {
        if (call.Callee is not IdentifierExpression identifier)
        {
            return false;
        }

        switch (identifier.Name)
        {
            case "文件存在" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.MovRcxRax();
                _code.CallCode("__yy_file_exists");
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "删除文件" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.MovRcxRax();
                _code.CallCode("__yy_file_delete");
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "读文件" when call.Arguments.Count == 1:
                EmitExpression(call.Arguments[0], context);
                _code.MovRcxRax();
                _code.CallCode("__yy_file_read");
                context.LastKind = NativeValueKind.Text;
                return true;
            case "写文件" when call.Arguments.Count == 2:
                EmitExpression(call.Arguments[0], context);
                _code.PushRax();
                EmitExpression(call.Arguments[1], context);
                _code.MovRdxRax();
                _code.Pop(NativeRegister.Rcx);
                _code.CallCode("__yy_file_write");
                context.LastKind = NativeValueKind.Boolean;
                return true;
            case "取目录文件" when call.Arguments.Count is 1 or 2:
                EmitExpression(call.Arguments[0], context);
                _code.PushRax();
                if (call.Arguments.Count == 2)
                {
                    EmitExpression(call.Arguments[1], context);
                }
                else
                {
                    _code.LeaRaxRip("data:" + _anyFilePatternLabel);
                }

                _code.MovRdxRax();
                _code.Pop(NativeRegister.Rcx);
                _code.CallCode("__yy_list_files");
                context.LastKind = NativeValueKind.Array;
                return true;
            default:
                return false;
        }
    }

    private void EmitFileRuntime()
    {
        EmitUtf8ToUtf16();
        EmitUtf16ToUtf8();
        EmitFilePredicates();
        EmitFileReadWrite();
        EmitListFiles();
    }

    private void EmitUtf8ToUtf16()
    {
        _code.MarkLabel("__yy_utf8_to_utf16");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.LoadRaxFromRbp(-8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-16);
        _code.MovImmediate(NativeRegister.Rcx, 65001);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.LoadRaxFromRbp(-8);
        _code.AddRaxByte(8);
        _code.MovR8Rax();
        _code.LoadR9FromRbp(-16);
        _code.LeaRaxRip("data:" + _fileWideBufferLabel);
        _code.StoreRaxRsp(32);
        _code.StoreRspImmediate(40, 4096);
        _code.StoreRspImmediate(48, 0);
        _code.StoreRspImmediate(56, 0);
        _code.CallImport("import:MultiByteToWideChar");
        _code.MovEaxEax();
        _code.AddRaxRax();
        _code.LeaRcxRip("data:" + _fileWideBufferLabel);
        _code.AddRcxRax();
        _code.StoreZeroWordRcx();
        _code.LeaRaxRip("data:" + _fileWideBufferLabel);
        _code.EndFrame();
    }

    private void EmitUtf16ToUtf8()
    {
        _code.MarkLabel("__yy_utf16_to_utf8");
        _code.BeginFrame(64);
        _code.StoreRcxToRbp(-8);
        _code.MovImmediate(NativeRegister.Rcx, 65001);
        _code.MovImmediate(NativeRegister.Rdx, 0);
        _code.LoadR8FromRbp(-8);
        _code.MovImmediate(NativeRegister.R9, -1);
        _code.LeaRaxRip("data:" + _fileUtf8BufferLabel);
        _code.AddRaxByte(8);
        _code.StoreRaxRsp(32);
        _code.StoreRspImmediate(40, 4095);
        _code.StoreRspImmediate(48, 0);
        _code.StoreRspImmediate(56, 0);
        _code.CallImport("import:WideCharToMultiByte");
        _code.MovEaxEax();
        _code.StoreRaxToRbp(-16);
        _code.LeaRaxRip("data:" + _fileUtf8BufferLabel);
        _code.LoadRcxFromRbp(-16);
        _code.DecRcx();
        _code.StoreRcxAtRax();
        _code.LeaRcxRip("data:" + _fileUtf8BufferLabel);
        _code.CallCode("__yy_clone_string");
        _code.EndFrame();
    }

    private void EmitFilePredicates()
    {
        _code.MarkLabel("__yy_file_exists");
        _code.BeginFrame(32);
        _code.CallCode("__yy_utf8_to_utf16");
        _code.MovRcxRax();
        _code.CallImport("import:GetFileAttributesW");
        _code.MovEaxEax();
        _code.MovImmediate(NativeRegister.Rcx, 0xFFFFFFFF);
        _code.CmpRaxRcx();
        _code.SetConditional("!=");
        _code.EndFrame();

        _code.MarkLabel("__yy_file_delete");
        _code.BeginFrame(32);
        _code.CallCode("__yy_utf8_to_utf16");
        _code.MovRcxRax();
        _code.CallImport("import:DeleteFileW");
        _code.MovEaxEax();
        _code.TestRaxRax();
        _code.SetConditional("!=");
        _code.EndFrame();
    }

    private void EmitFileReadWrite()
    {
        var readFailed = NewLabel("file_read_failed");
        var readEnd = NewLabel("file_read_end");
        _code.MarkLabel("__yy_file_read");
        _code.BeginFrame(96);
        _code.CallCode("__yy_utf8_to_utf16");
        _code.MovRcxRax();
        _code.MovImmediate(NativeRegister.Rdx, 0x80000000);
        _code.MovImmediate(NativeRegister.R8, 1);
        _code.MovImmediate(NativeRegister.R9, 0);
        _code.StoreRspImmediate(32, 3);
        _code.StoreRspImmediate(40, 0x80);
        _code.StoreRspImmediate(48, 0);
        _code.CallImport("import:CreateFileW");
        _code.StoreRaxToRbp(-8);
        _code.MovImmediate(NativeRegister.Rcx, -1);
        _code.CmpRaxRcx();
        _code.JumpIfZero(readFailed);
        _code.LoadRcxFromRbp(-8);
        _code.LeaRdxRbp(-16);
        _code.CallImport("import:GetFileSizeEx");
        _code.LoadRaxFromRbp(-16);
        _code.StoreRaxToRbp(-24);
        _code.AddRaxByte(9);
        _code.MovRcxRax();
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-32);
        _code.LoadRcxFromRbp(-24);
        _code.StoreRcxAtRax();
        _code.LoadRcxFromRbp(-8);
        _code.LoadRdxFromRbp(-32);
        _code.AddRdxByte(8);
        _code.LoadR8FromRbp(-24);
        _code.LeaR9Rbp(-40);
        _code.StoreRspImmediate(32, 0);
        _code.CallImport("import:ReadFile");
        _code.LoadRcxFromRbp(-8);
        _code.CallImport("import:CloseHandle");
        _code.LoadRaxFromRbp(-32);
        _code.Jmp(readEnd);
        _code.MarkLabel(readFailed);
        _code.LeaRaxRip("data:" + _emptyTextLabel);
        _code.MarkLabel(readEnd);
        _code.EndFrame();

        _code.MarkLabel("__yy_file_write");
        var writeFailed = NewLabel("file_write_failed");
        var writeEnd = NewLabel("file_write_end");
        _code.BeginFrame(96);
        _code.StoreRdxToRbp(-8);
        _code.CallCode("__yy_utf8_to_utf16");
        _code.MovRcxRax();
        _code.MovImmediate(NativeRegister.Rdx, 0x40000000);
        _code.MovImmediate(NativeRegister.R8, 0);
        _code.MovImmediate(NativeRegister.R9, 0);
        _code.StoreRspImmediate(32, 2);
        _code.StoreRspImmediate(40, 0x80);
        _code.StoreRspImmediate(48, 0);
        _code.CallImport("import:CreateFileW");
        _code.StoreRaxToRbp(-16);
        _code.MovImmediate(NativeRegister.Rcx, -1);
        _code.CmpRaxRcx();
        _code.JumpIfZero(writeFailed);
        _code.LoadRaxFromRbp(-8);
        _code.MovRcxRax();
        _code.LoadRaxFromRcx();
        _code.StoreRaxToRbp(-24);
        _code.LoadRcxFromRbp(-16);
        _code.LoadRdxFromRbp(-8);
        _code.AddRdxByte(8);
        _code.LoadR8FromRbp(-24);
        _code.LeaR9Rbp(-32);
        _code.StoreRspImmediate(32, 0);
        _code.CallImport("import:WriteFile");
        _code.LoadRcxFromRbp(-16);
        _code.CallImport("import:CloseHandle");
        _code.TestRaxRax();
        _code.SetConditional("!=");
        _code.Jmp(writeEnd);
        _code.MarkLabel(writeFailed);
        _code.MovImmediate(NativeRegister.Rax, 0);
        _code.MarkLabel(writeEnd);
        _code.EndFrame();
    }

    private void EmitListFiles()
    {
        var empty = NewLabel("list_empty");
        var loop = NewLabel("list_loop");
        var next = NewLabel("list_next");
        var end = NewLabel("list_end");
        _code.MarkLabel("__yy_list_files");
        _code.BeginFrame(160);
        _code.StoreRcxToRbp(-8);
        _code.StoreRdxToRbp(-16);
        _code.LeaRdxRip("data:" + _pathSeparatorLabel);
        _code.CallCode("__yy_concat_string");
        _code.StoreRaxToRbp(-24);
        _code.MovRcxRax();
        _code.LoadRdxFromRbp(-16);
        _code.CallCode("__yy_concat_string");
        _code.MovRcxRax();
        _code.CallCode("__yy_utf8_to_utf16");
        _code.StoreRaxToRbp(-32);
        _code.MovImmediate(NativeRegister.Rcx, 592);
        _code.CallCode("__yy_alloc");
        _code.StoreRaxToRbp(-40);
        _code.LoadRcxFromRbp(-32);
        _code.LoadRdxFromRbp(-40);
        _code.CallImport("import:FindFirstFileW");
        _code.StoreRaxToRbp(-48);
        _code.MovImmediate(NativeRegister.Rcx, -1);
        _code.CmpRaxRcx();
        _code.JumpIfZero(empty);
        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallCode("__yy_array_new");
        _code.StoreRaxToRbp(-56);
        _code.MarkLabel(loop);
        _code.LoadRaxFromRbp(-40);
        _code.MovRcxRax();
        _code.LoadEaxFromRcx();
        _code.AndEax(0x10);
        _code.TestRaxRax();
        _code.JumpIfNotZero(next);
        _code.LoadRaxFromRbp(-40);
        _code.AddRaxByte(44);
        _code.MovRcxRax();
        _code.CallCode("__yy_utf16_to_utf8");
        _code.StoreRaxToRbp(-64);
        _code.LoadRcxFromRbp(-8);
        _code.LeaRdxRip("data:" + _pathSeparatorLabel);
        _code.CallCode("__yy_concat_string");
        _code.MovRcxRax();
        _code.LoadRdxFromRbp(-64);
        _code.CallCode("__yy_concat_string");
        _code.MovRdxRax();
        _code.LoadRcxFromRbp(-56);
        _code.CallCode("__yy_array_append");
        _code.StoreRaxToRbp(-56);
        _code.MarkLabel(next);
        _code.LoadRcxFromRbp(-48);
        _code.LoadRdxFromRbp(-40);
        _code.CallImport("import:FindNextFileW");
        _code.MovEaxEax();
        _code.TestRaxRax();
        _code.JumpIfNotZero(loop);
        _code.LoadRcxFromRbp(-48);
        _code.CallImport("import:FindClose");
        _code.LoadRaxFromRbp(-56);
        _code.Jmp(end);
        _code.MarkLabel(empty);
        _code.MovImmediate(NativeRegister.Rcx, 0);
        _code.CallCode("__yy_array_new");
        _code.MarkLabel(end);
        _code.EndFrame();
    }
}
