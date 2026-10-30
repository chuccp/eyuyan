using System.IO;
using System.Text;
using Yy.Language.Structured;
using Yy.Studio.Forms;

namespace Yy.Studio;

/// <summary>
/// 项目与窗体三件套的创建、清单写入和默认模板。
/// </summary>
public sealed partial class YyProject
{
    public static YyProject CreateProject(string projectPath, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName) ||
            !YyFormSerializer.IsIdentifier(projectName))
        {
            throw new ArgumentException("项目名称必须是中文、英文、数字或下划线组成的标识符。", nameof(projectName));
        }

        var fullProjectPath = Path.GetFullPath(projectPath);
        if (File.Exists(fullProjectPath))
        {
            throw new IOException($"项目文件已经存在：{fullProjectPath}");
        }

        var root = Path.GetDirectoryName(fullProjectPath)
            ?? throw new IOException("无法确定项目目录。");
        if (Directory.Exists(root) &&
            Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException("新建项目请选择空目录，或选择一个新的项目文件夹。");
        }

        Directory.CreateDirectory(root);
        var formsDirectory = Path.Combine(root, "Forms");
        var resourcesDirectory = Path.Combine(root, "Resources");
        Directory.CreateDirectory(formsDirectory);
        Directory.CreateDirectory(resourcesDirectory);

        var entryPath = Path.Combine(root, "Program.yyc");
        var formPath = Path.Combine(formsDirectory, "MainForm.yyx");
        var codePath = Path.Combine(formsDirectory, "MainForm.yyc");

        WriteStructuredSource(entryPath, CreateEntryTemplate(projectName));
        var form = new YyFormModel
        {
            Name = "_启动窗口",
            CodeFile = "MainForm.yyc",
            LoadEvent = "__启动窗口_创建完毕",
            CloseEvent = "__启动窗口_将被销毁",
            Title = projectName,
            Width = 760,
            Height = 500
        };
        form.Controls.Add(CreateWelcomeLabel());
        form.Controls.Add(CreateWelcomeButton());
        WriteFormSet(formPath, codePath, form);

        var manifest = new YyProjectManifest
        {
            FormatVersion = 3,
            Name = projectName,
            Entry = "Program.yyc",
            Output = projectName + ".exe",
            StartupForm = "_启动窗口",
            Sources = ["Program.yyc", "Forms/MainForm.yyc"],
            Forms = ["Forms/MainForm.yyx"],
            Resources = [],
            References = []
        };
        WriteManifest(fullProjectPath, manifest);
        return LoadForDirectory(root)
            ?? throw new InvalidDataException("新项目已创建，但项目清单无法重新读取。");
    }

    public YyProjectForm CreateForm(string title)
    {
        var sequence = 1;
        string className;
        do
        {
            className = "窗口" + sequence;
            sequence++;
        }
        while (Forms.Any(form => string.Equals(form.Model.Name, className, StringComparison.Ordinal)) ||
               File.Exists(Path.Combine(DirectoryPath, "Forms", className + ".yyx")));

        var formsDirectory = Path.Combine(DirectoryPath, "Forms");
        Directory.CreateDirectory(formsDirectory);
        var formPath = Path.Combine(formsDirectory, className + ".yyx");
        var codePath = Path.Combine(formsDirectory, className + ".yyc");
        var model = new YyFormModel
        {
            Name = className,
            CodeFile = className + ".yyc",
            Title = title,
            Width = 720,
            Height = 480
        };
        var button = YyControlTypes.Create(YyControlTypes.Button, 1);
        button.Name = "确定按钮";
        button.Text = "确定";
        button.ClickEvent = "_确定按钮_被单击";
        model.Controls.Add(button);

        File.WriteAllText(formPath, YyFormSerializer.Serialize(model), new UTF8Encoding(false));
        var binding = YyFormBindingSerializer.Create(model, formPath, codePath);
        var userCode = YyFormCompanionFiles.CreateUserCodeDocument(model, binding);
        File.WriteAllText(
            codePath,
            YyCodeSerializer.Serialize(userCode),
            new UTF8Encoding(false));
        YyFormCompanionFiles.WriteGeneratedBinding(formPath, model, codePath);

        AddRelativePath(_formPaths, formPath, DirectoryPath);
        AddRelativePath(_sourcePaths, codePath, DirectoryPath);
        WriteCurrentManifest();
        return new YyProjectForm(
            formPath,
            YyFormCompanionFiles.BindingPath(formPath),
            codePath,
            model);
    }

    public string CreateSourceFile()
    {
        var sequence = 1;
        string path;
        do
        {
            path = Path.Combine(DirectoryPath, $"Source{sequence++}.yyc");
        }
        while (File.Exists(path));

        WriteStructuredSource(
            path,
            $"""
            .版本 3
            .程序集 {Name}

            输出("新建源码文件")
            """);
        AddRelativePath(_sourcePaths, path, DirectoryPath);
        WriteCurrentManifest();
        return path;
    }


    private static string CreateEntryTemplate(string projectName) =>
        $"""
        .版本 3
        .程序集 {projectName}

        ' 项目入口。窗体代码位于 Forms 目录。
        输出("{projectName} 已启动。")
        """;

    private static void WriteFormSet(
        string formPath,
        string userCodePath,
        YyFormModel form)
    {
        File.WriteAllText(
            formPath,
            YyFormSerializer.Serialize(form),
            new UTF8Encoding(false));
        var binding = YyFormBindingSerializer.Create(form, formPath, userCodePath);
        var userCode = YyFormCompanionFiles.CreateUserCodeDocument(form, binding);
        File.WriteAllText(
            userCodePath,
            YyCodeSerializer.Serialize(userCode),
            new UTF8Encoding(false));
        YyFormCompanionFiles.WriteGeneratedBinding(formPath, form, userCodePath);
    }

    private static YyControlModel CreateWelcomeLabel() =>
        new()
        {
            Type = YyControlTypes.Label,
            Name = "欢迎标签",
            Text = "这是一个真正的易语项目",
            X = 40,
            Y = 38,
            Width = 380,
            Height = 46,
            FontSize = 24,
            Background = "Transparent"
        };

    private static YyControlModel CreateWelcomeButton() =>
        new()
        {
            Type = YyControlTypes.Button,
            Name = "欢迎按钮",
            Text = "运行事件代码",
            ClickEvent = "_欢迎按钮_被单击",
            X = 40,
            Y = 108,
            Width = 150,
            Height = 42,
            Background = "#ECE9D8",
            Foreground = "#000000"
        };

}
