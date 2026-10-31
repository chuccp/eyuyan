using ICSharpCode.AvalonEdit;
using Yy.Studio.Structured;

internal static class TextEditingRegression
{
    public static void VerifyNoSelectionPairing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editor = new TextEditor { Text = "输出" };
                editor.CaretOffset = editor.Text.Length;
                if (!YyTextEditing.TryHandleTextInput(editor, "("))
                {
                    throw new Exception("无选区输入没有触发自动配对。");
                }

                if (editor.Text != "输出()")
                {
                    throw new Exception("无选区自动配对结果不正确。");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
