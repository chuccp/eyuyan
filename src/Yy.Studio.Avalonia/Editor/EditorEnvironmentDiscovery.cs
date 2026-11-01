using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 从同名 .yyx / .Designer.yyc 自动恢复窗体上下文，
/// 使打开窗体用户代码时无需主窗口额外配置即可补全控件和属性。
/// </summary>
public static class EditorEnvironmentDiscovery
{
    public static CompletionEnvironment Create(string? filePath)
    {
        var environment = new CompletionEnvironment { FilePath = filePath };
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return environment;
        }

        try
        {
            var formPath = YyFormCompanionFiles.GetAssociatedFormPath(filePath);
            if (!File.Exists(formPath))
            {
                return environment;
            }

            var form = YyFormSerializer.Deserialize(File.ReadAllText(formPath));
            var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
            if (File.Exists(bindingPath))
            {
                var binding = YyFormBindingSerializer.Deserialize(
                    File.ReadAllText(bindingPath));
                YyFormBindingSerializer.ApplyTo(form, binding);
                form.CodeFile = binding.CodeFile;
            }

            environment.Form = form;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyFormFormatException)
        {
            // 窗体尚未保存或关系文件暂不完整时保留纯代码补全。
        }

        return environment;
    }
}
