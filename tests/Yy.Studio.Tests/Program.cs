using System.IO;
using System.Windows.Automation;
using System.Windows.Input;
using Yy.Language;
using Yy.Studio;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;

var failures = 0;

Run("界面 XML 与生成关系分离", () =>
{
    var form = new YyFormModel
    {
        Name = "测试窗口",
        LoadEvent = "测试窗口_载入",
        CloseEvent = "测试窗口_关闭",
        Title = "客户登记",
        Width = 640,
        Height = 420,
        Background = "#F4F7FB",
        Icon = "@资源/图片/测试图标"
    };
    form.Controls.Add(YyControlTypes.Create(YyControlTypes.Button, 1));
    form.Controls.Add(YyControlTypes.Create(YyControlTypes.TextBox, 2));
    form.Controls[1].TextChangedEvent = "文本框_文本改变";

    var xml = YyFormSerializer.Serialize(form);
    var restored = YyFormSerializer.Deserialize(xml);
    Equal("测试窗口", restored.Name);
    Equal("客户登记", restored.Title);
    Equal("@资源/图片/测试图标", restored.Icon);
    Equal(2, restored.Controls.Count);
    Equal("按钮1", restored.Controls[0].Name);
    var previewCopy = restored.Clone();
    previewCopy.Icon = "@资源/图片/预览图标";
    previewCopy.Controls[0].Name = "预览按钮";
    Equal("@资源/图片/测试图标", restored.Icon);
    Equal("按钮1", restored.Controls[0].Name);
    True(
        xml.Contains("标识=\"@id/测试窗口/按钮1\"", StringComparison.Ordinal),
        "界面控件没有生成稳定标识。");
    True(!xml.Contains("载入=", StringComparison.Ordinal), "界面 XML 不应保存事件。");
    True(!xml.Contains("文本改变=", StringComparison.Ordinal), "界面 XML 不应保存控件事件。");

    var binding = YyFormBindingSerializer.Create(form, "测试窗口.yyx", "测试窗口.yyc");
    var bindingText = YyFormBindingSerializer.Serialize(binding);
    True(
        !bindingText.Contains("<yy:", StringComparison.Ordinal),
        "生成关系必须是纯代码，不应包含 XML 元素。");
    True(
        bindingText.Contains("导入包 \"系统.界面\"", StringComparison.Ordinal),
        "生成关系应声明界面 SDK 包。");
    True(
        bindingText.Contains(
            "绑定(\"@id/测试窗口/编辑框2\", \"文本改变\", \"文本框_文本改变\")",
            StringComparison.Ordinal),
        "生成关系没有通过控件 ID 绑定事件。");
    var restoredBinding = YyFormBindingSerializer.Deserialize(bindingText);
    YyFormBindingSerializer.ApplyTo(restored, restoredBinding);
    Equal("测试窗口_载入", restored.LoadEvent);
    Equal("测试窗口_关闭", restored.CloseEvent);
    Equal("文本框_文本改变", restored.Controls[1].TextChangedEvent);

    var userCode = YyFormCompanionFiles.CreateUserCodeDocument(form, restoredBinding);
    True(userCode.Packages.Contains(Yy.Sdk.YySdk.CorePackage), "窗体代码应默认导入语言基础包。");
    True(userCode.Packages.Contains(Yy.Sdk.YySdk.UiPackage), "窗体代码应默认导入界面包。");
    True(
        userCode.Functions.Any(function => function.Name == "文本框_文本改变"),
        "用户代码应只获得事件入口。");
    Equal(3, userCode.Functions.Count);
});

Run("示例窗体读取", () =>
{
    var path = Path.Combine(
        Directory.GetCurrentDirectory(),
        "examples",
        "Sources",
        "Forms",
        "主窗体.yyx");
    var form = YyFormSerializer.Deserialize(File.ReadAllText(path));
    var binding = YyFormBindingSerializer.Deserialize(
        File.ReadAllText(YyFormCompanionFiles.BindingPath(path)));
    YyFormBindingSerializer.ApplyTo(form, binding);
    Equal("主窗口", form.Name);
    Equal("客户登记", form.Title);
    Equal("_主窗口_创建完毕", form.LoadEvent);
    Equal("_主窗口_将被销毁", form.CloseEvent);
    Equal(9, form.Controls.Count);
    Equal("@资源/图片/应用图标", form.Icon);
    Equal(
        "@资源/图片/客户图标",
        form.Controls.Single(control => control.Name == "图标预览").Picture);
    Equal("_保存按钮_被单击", form.Controls.Single(control => control.Name == "保存按钮").ClickEvent);
    Equal(
        "_姓名输入框_内容被改变",
        form.Controls.Single(control => control.Name == "姓名输入框").TextChangedEvent);
    Equal(
        File.ReadAllText(YyFormCompanionFiles.BindingPath(path)).Replace("\r\n", "\n"),
        YyFormBindingSerializer.Generate(
                form,
                path,
                YyFormCompanionFiles.UserCodePath(path))
            .Replace("\r\n", "\n"));
});

Run("补齐事件入口不覆盖用户逻辑", () =>
{
    var document = new YyCodeDocument { Name = "用户代码" };
    var existing = new YyCodeFunction { Name = "_保存按钮_被单击" };
    existing.Statements.Add(new YyCodeStatement
    {
        Kind = YyCodeStatementKinds.Call,
        Expression = "输出(\"保留我\")"
    });
    document.Functions.Add(existing);
    var changed = YyFormCompanionFiles.EnsureEventHandlers(
        document,
        [
            new YyFormBindingEvent("保存按钮", "按钮", "单击", "_保存按钮_被单击"),
            new YyFormBindingEvent("取消按钮", "按钮", "单击", "_取消按钮_被单击")
        ]);
    True(changed, "缺失事件入口应触发代码更新。");
    Equal(2, document.Functions.Count);
    Equal("输出(\"保留我\")", document.Functions[0].Statements[0].Expression);
    Equal("_取消按钮_被单击", document.Functions[1].Name);
});

Run("源代码往返", () =>
{
    const string source = """
        .程序集 测试程序
        导入包 "系统.基础"
        子程序 问候(名称: 文本 = "世界"): 文本
            变量 前缀: 文本 = "你好，"
            返回 前缀 + 名称
        结束子程序
        输出(问候())
        """;
    var document = YyCodeDocumentConverter.FromSource(source);
    Equal(1, document.Functions.Count);
    Equal(1, document.MainStatements.Count);
    var serialized = YyCodeSerializer.Serialize(document);
    True(!serialized.Contains("<yy:", StringComparison.Ordinal), ".yyc 必须是源码而不是 XML。");
    var restored = YyCodeSerializer.Deserialize(serialized);
    Equal("测试程序", restored.Name);
    Equal("问候", restored.Functions[0].Name);
    var lines = new List<string>();
    YyRunner.RunSource(
        YyCodeDocumentConverter.ToSource(restored),
        null,
        lines.Add);
    Equal("你好，世界", string.Join("\n", lines));
});

Run("用户代码默认进入VB式代码窗口", () =>
{
    var source = YyCodeSerializer.Serialize(
        YyCodeDocumentConverter.FromSource(
            ".程序集 测试\n子程序 示例()\n    输出(\"你好\")\n结束子程序\n",
            "Sample.yyc"));
    var document = new EditorDocument("Sample.yyc", "Sample.yyc", source);
    True(document.UsePlainCodeEditor, "用户代码应默认进入连续代码窗口。");
    True(document.UseEasyLanguagePresentation, "用户代码应默认使用易语言代码窗口。");
    True(
        document.PlainCodeText.Contains(".程序集 测试", StringComparison.Ordinal),
        "代码窗口没有取得可编辑源码。");

    var indentation = new YyIndentationStrategy();
    var text = new ICSharpCode.AvalonEdit.Document.TextDocument(
        "子程序 示例()\n\n");
    indentation.IndentLine(text, text.GetLineByNumber(2));
    Equal("    ", text.GetText(text.GetLineByNumber(2)));

    var foldingText = new ICSharpCode.AvalonEdit.Document.TextDocument(
        """
        子程序 示例()
            如果 真 则
                输出("你好")
            结束如果
        结束子程序
        """);
    Equal(
        2,
        EasyLanguageFoldingStrategy.CreateFoldings(foldingText).Count());
});

Run("编辑器自动配对输入", () =>
{
    var insert = YyTextEditing.GetPairAction("", 0, 0, "(");
    Equal(YyPairActionKind.InsertPair, insert.Kind);
    Equal("(", insert.Opening);
    Equal(")", insert.Closing);

    var wrap = YyTextEditing.GetPairAction("选择", 0, 2, "“");
    Equal(YyPairActionKind.WrapSelection, wrap.Kind);
    Equal("“", wrap.Opening);
    Equal("”", wrap.Closing);

    var skip = YyTextEditing.GetPairAction("()", 1, 0, ")");
    Equal(YyPairActionKind.SkipClosing, skip.Kind);
});

Run("Studio 配置文件解析", () =>
{
    var configuration = StudioConfiguration.Parse(
        """
        {
          "格式版本": 1,
          "MCP": {
            "启用": true,
            "端口": 38123,
            "自动寻找可用端口": false,
            "端口尝试次数": 3
          },
          "快捷键": {
            "运行": ["Ctrl+R"],
            "编译": []
          }
        }
        """,
        "YyStudio.json");
    Equal(1, configuration.FormatVersion);
    Equal(38123, configuration.Mcp.Port);
    True(!configuration.Mcp.AutoFindAvailablePort, "MCP 自动端口配置没有读取。");
    Equal(3, configuration.Mcp.PortAttempts);
    Equal("YyStudio.json", configuration.ConfigurationPath);
    Equal(Key.R, configuration.GetGestures(StudioShortcutActions.Run).Single().Key);
    Equal(0, configuration.GetGestures(StudioShortcutActions.Compile).Count);
    Equal(
        Key.S,
        configuration.GetGestures(StudioShortcutActions.SaveFile).Single().Key);

    try
    {
        StudioConfiguration.Parse(
            """{"格式版本":1,"MCP":{"端口":80}}""");
        throw new Exception("无效 MCP 端口应被拒绝。");
    }
    catch (InvalidDataException exception)
    {
        True(exception.Message.Contains("1024", StringComparison.Ordinal), exception.Message);
    }

    try
    {
        StudioConfiguration.Parse(
            """
            {
              "格式版本": 1,
              "快捷键": {
                "运行": ["Ctrl+S"],
                "保存": ["Ctrl+S"]
              }
            }
            """);
        throw new Exception("重复快捷键应被拒绝。");
    }
    catch (InvalidDataException exception)
    {
        True(exception.Message.Contains("同时分配", StringComparison.Ordinal), exception.Message);
    }
});

Run("JetBrains 风格设置窗口", () =>
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var configuration = StudioConfiguration.Parse(
                """{"格式版本":1,"MCP":{"端口":38123}}""");
            var dialog = new SystemConfigDialog(configuration);
            Equal(940d, dialog.Width);
            dialog.Close();
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
});

Run("项目清单读取", () =>
{
    var directory = Path.Combine(Directory.GetCurrentDirectory(), "examples");
    var project = YyProject.LoadForDirectory(directory);
    True(project is not null, "应找到示例项目清单。");
    Equal("易语示例工程", project!.Name);
    True(project.EntryPath.EndsWith("入门.yyc", StringComparison.OrdinalIgnoreCase), "项目入口不正确。");
    Equal("主窗口", project.StartupForm);
    Equal(1, project.Forms.Count);
    Equal("主窗体.yyc", Path.GetFileName(project.Forms[0].CodePath));
    Equal(2, project.ResourceTable.Count);
    True(
        YyResourceIndex.ResolveResourcePath(
            directory,
            project.Forms[0].Model.Icon) is not null,
        "窗体图标没有关联到实际资源文件。");
    True(
        File.ReadAllText(Path.Combine(directory, "Resources", "ids.xml"))
            .Contains("@资源/图片/客户图标", StringComparison.Ordinal),
        "示例工程的资源没有写入资源标识。");
    True(
        File.ReadAllText(Path.Combine(directory, "Resources", "ids.xml"))
            .Contains("@资源/图片/应用图标", StringComparison.Ordinal),
        "窗体图标没有写入资源标识。");
});

Run("IDE 与命令行编译器程序集分离", () =>
{
    Equal("Yy.Compiler", typeof(YyProject).Assembly.GetName().Name);
    Equal("Yy.Compiler", typeof(ProjectCompiler).Assembly.GetName().Name);
    Equal("YyStudio", typeof(YyFormPreviewWindow).Assembly.GetName().Name);
});

Run("完整项目骨架", () =>
{
    var temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "yy-project-" + Guid.NewGuid().ToString("N"));
    var projectRoot = Path.Combine(temporaryRoot, "DemoProject");
    try
    {
        var project = YyProject.CreateProject(
            Path.Combine(projectRoot, "DemoProject.yyproj"),
            "DemoProject",
            "demo.project");
        Equal("demo.project", project.PackageName);
        True(
            project.SourceRootPath.EndsWith("Sources", StringComparison.OrdinalIgnoreCase),
            "项目源码根目录必须是 Sources。");
        True(File.Exists(project.EntryPath), "项目入口没有创建。");
        True(
            File.Exists(Path.Combine(projectRoot, "Sources", "Program.yyc")),
            "结构化入口源码没有创建。");
        True(
            File.Exists(Path.Combine(projectRoot, "Sources", "Forms", "MainForm.yyx")),
            "窗体没有创建。");
        True(
            File.Exists(Path.Combine(projectRoot, "Sources", "Forms", "MainForm.yyc")),
            "用户代码文件没有创建。");
        True(
            File.Exists(Path.Combine(
                projectRoot,
                "Sources",
                "Forms",
                "MainForm.Designer.yyc")),
            "自动生成的界面关系文件没有创建。");
        True(
            File.Exists(Path.Combine(projectRoot, "Resources", "ids.xml")),
            "自动生成的资源标识文件没有创建。");
        True(
            Directory.Exists(Path.Combine(projectRoot, "Resources", "images")) &&
            Directory.Exists(Path.Combine(projectRoot, "Resources", "sounds")) &&
            Directory.Exists(Path.Combine(projectRoot, "Resources", "files")),
            "资源分类目录没有创建。");
        var resourceIds = File.ReadAllText(
            Path.Combine(projectRoot, "Resources", "ids.xml"));
        True(
            resourceIds.Contains("@id/_启动窗口/欢迎按钮", StringComparison.Ordinal),
            "资源标识文件没有关联窗体控件。");
        var manifestText = File.ReadAllText(
            Path.Combine(projectRoot, "DemoProject.yyproj"));
        True(
            manifestText.Contains("\"资源标识\": \"Resources/ids.xml\"", StringComparison.Ordinal),
            "工程清单没有声明资源标识文件。");
        True(
            manifestText.Contains("\"包名\": \"demo.project\"", StringComparison.Ordinal),
            "工程清单没有保存包名。");
        True(
            manifestText.Contains("\"源码目录\": \"Sources\"", StringComparison.Ordinal) &&
            manifestText.Contains("\"资源目录\": \"Resources\"", StringComparison.Ordinal),
            "工程清单没有区分源码目录和资源目录。");
        var sourceImage = Path.Combine(temporaryRoot, "客户图标.png");
        File.WriteAllBytes(sourceImage, [1, 2, 3, 4]);
        var resource = project.AddResource(sourceImage, "图片", "客户图标");
        True(
            File.Exists(Path.Combine(projectRoot, resource.File)),
            "资源没有复制到工程目录。");
        True(
            resource.File.EndsWith(
                "Resources/images/客户图标.png",
                StringComparison.OrdinalIgnoreCase),
            "图片没有保存到 Resources/images。");
        resourceIds = File.ReadAllText(
            Path.Combine(projectRoot, "Resources", "ids.xml"));
        True(
            resourceIds.Contains("@资源/图片/客户图标", StringComparison.Ordinal),
            "资源标识文件没有关联图片资源。");
        var entryDocument = YyCodeSerializer.Deserialize(
            File.ReadAllText(Path.Combine(projectRoot, "Sources", "Program.yyc")));
        True(entryDocument.MainStatements.Count > 0, "结构化入口没有命令数据。");
        True(
            !File.ReadAllText(Path.Combine(projectRoot, "Sources", "Program.yyc"))
                .StartsWith("<?xml", StringComparison.OrdinalIgnoreCase),
            "新建项目入口必须以纯源码保存。");
        Equal(1, project.Forms.Count);
        Equal("_启动窗口", project.StartupForm);
        True(
            project.SourcePaths.Contains(project.Forms[0].CodePath!, StringComparer.OrdinalIgnoreCase),
            "源码列表应包含用户代码文件。");
        var sourcePath = project.CreateSourceFile();
        True(File.Exists(sourcePath), "项目内新建源码失败。");
        True(
            sourcePath.StartsWith(project.SourceRootPath, StringComparison.OrdinalIgnoreCase),
            "新建源码必须位于 Sources 目录。");
        project.UpdateSettings(
            "DemoProjectRenamed",
            "demo.project.renamed",
            "Sources/Program.yyc",
            "RenamedDemo",
            "_启动窗口");
        var updated = YyProject.LoadForDirectory(projectRoot)
            ?? throw new Exception("项目属性保存后无法重新加载。");
        Equal("DemoProjectRenamed", updated.Name);
        Equal("demo.project.renamed", updated.PackageName);
        Equal("RenamedDemo.exe", updated.OutputName);
        True(
            updated.SourcePaths.Contains(sourcePath, StringComparer.OrdinalIgnoreCase),
            "新建源码没有写入项目清单。");
        True(
            updated.Forms[0].BindingPath.EndsWith(
                "MainForm.Designer.yyc",
                StringComparison.OrdinalIgnoreCase),
            "项目窗体没有关联自动关系文件。");

        var consoleRoot = Path.Combine(temporaryRoot, "ConsoleProject");
        var console = YyProject.CreateProject(
            Path.Combine(consoleRoot, "ConsoleProject.yyproj"),
            "ConsoleProject",
            "demo.console",
            YyProjectKind.Console);
        Equal(0, console.Forms.Count);
        True(
            File.ReadAllText(Path.Combine(consoleRoot, "Sources", "Program.yyc"))
                .Contains("输出(", StringComparison.Ordinal),
            "控制台项目入口模板不正确。");

        var moduleRoot = Path.Combine(temporaryRoot, "ModuleProject");
        var module = YyProject.CreateProject(
            Path.Combine(moduleRoot, "ModuleProject.yyproj"),
            "ModuleProject",
            "demo.module",
            YyProjectKind.Module);
        Equal(0, module.Forms.Count);
        True(
            File.ReadAllText(Path.Combine(moduleRoot, "Sources", "Program.yyc"))
                .Contains("子程序 示例函数", StringComparison.Ordinal),
            "模块项目入口模板不正确。");
    }
    finally
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
});

Run("数据表清单往返", () =>
{
    var temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "yy-tables-" + Guid.NewGuid().ToString("N"));
    try
    {
        var project = YyProject.CreateProject(
            Path.Combine(temporaryRoot, "Tables.yyproj"),
            "Tables",
            "demo.tables");
        project.Globals.Add(new YyGlobalVarEntry { Name = "计数", TypeName = "整数型", Remark = "全局计数" });
        project.Constants.Add(new YyConstantEntry { Name = "最大值", Value = "100", Remark = "上限" });
        var resourceSource = Path.Combine(temporaryRoot, "测试图片.png");
        File.WriteAllBytes(resourceSource, [4, 3, 2, 1]);
        project.AddResource(resourceSource, "图片", "测试图片");
        project.DllCommands.Add(new YyDllCommandEntry { Name = "取系统目录", DllFile = "kernel32", CommandName = "GetSystemDirectory" });
        project.SaveManifest();

        var reloaded = YyProject.LoadForDirectory(temporaryRoot)
            ?? throw new Exception("数据表保存后无法重新加载。");
        Equal("计数", reloaded.Globals.Single().Name);
        Equal("整数型", reloaded.Globals.Single().TypeName);
        Equal("100", reloaded.Constants.Single().Value);
        Equal("测试图片", reloaded.ResourceTable.Single().Name);
        True(
            File.Exists(Path.Combine(temporaryRoot, reloaded.ResourceTable.Single().File)),
            "资源文件没有随工程保存。");
        Equal("kernel32", reloaded.DllCommands.Single().DllFile);
    }
    finally
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
});

Run("纯代码工程不需要窗体", () =>
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "yy-code-only-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        Directory.CreateDirectory(Path.Combine(directory, "Sources"));
        var source = YyCodeSerializer.Serialize(
            YyCodeDocumentConverter.FromSource(
                ".程序集 算法\n输出(\"纯代码\")\n",
                "Main.yyc"));
        File.WriteAllText(Path.Combine(directory, "Sources", "Main.yyc"), source);
        Directory.CreateDirectory(Path.Combine(directory, "Resources", "images"));
        Directory.CreateDirectory(Path.Combine(directory, "Resources", "sounds"));
        Directory.CreateDirectory(Path.Combine(directory, "Resources", "files"));
        File.WriteAllText(
            Path.Combine(directory, "Resources", "ids.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <yy:资源标识 xmlns:yy="urn:yy-studio:resource-ids:1" 格式版本="1" />
            """);
        File.WriteAllText(
            Path.Combine(directory, "CodeOnly.yyproj"),
            """
            {
              "格式版本": 1,
              "名称": "CodeOnly",
              "包名": "demo.codeonly",
              "源码目录": "Sources",
              "资源目录": "Resources",
              "入口": "Sources/Main.yyc",
              "输出": "CodeOnly.exe",
              "源码": ["Sources/Main.yyc"],
              "窗体": [],
              "资源": [],
              "资源标识": "Resources/ids.xml",
              "引用": []
            }
            """);
        var project = YyProject.LoadForDirectory(directory)
            ?? throw new Exception("纯代码项目无法加载。");
        Equal(0, project.Forms.Count);
        Equal(1, project.SourcePaths.Count);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});

Run("缺少自动关系文件时拒绝项目", () =>
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "yy-missing-binding-" + Guid.NewGuid().ToString("N"));
    try
    {
        YyProject.CreateProject(
            Path.Combine(root, "MissingBinding.yyproj"),
            "MissingBinding",
            "demo.missingbinding");
        File.Delete(Path.Combine(
            root,
            "Sources",
            "Forms",
            "MainForm.Designer.yyc"));
        try
        {
            YyProject.LoadForDirectory(root);
            throw new Exception("缺少关系文件的窗体不应加载。");
        }
        catch (InvalidDataException exception)
        {
            True(
                exception.Message.Contains("Designer.yyc", StringComparison.Ordinal),
                exception.Message);
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Run("缺少资源标识文件时拒绝项目", () =>
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "yy-missing-resource-ids-" + Guid.NewGuid().ToString("N"));
    try
    {
        YyProject.CreateProject(
            Path.Combine(root, "MissingResourceIds.yyproj"),
            "MissingResourceIds",
            "demo.missingresources");
        File.Delete(Path.Combine(root, "Resources", "ids.xml"));
        try
        {
            YyProject.LoadForDirectory(root);
            throw new Exception("缺少资源标识文件的项目不应加载。");
        }
        catch (InvalidDataException exception)
        {
            True(
                exception.Message.Contains("ids.xml", StringComparison.Ordinal),
                exception.Message);
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
});

Run("只接受三文件工程规则", () =>
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "yy-legacy-project-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var entryPath = Path.Combine(directory, "Program.ey");
    var projectPath = Path.Combine(directory, "Legacy.yyproj");
    File.WriteAllText(entryPath, "输出(\"旧版\")");
    File.WriteAllText(
        projectPath,
        """
        {
          "格式版本": 2,
          "名称": "Legacy",
          "入口": "Program.ey",
          "输出": "Legacy.exe",
          "源码": ["Program.ey"],
          "窗体": [],
          "资源": [],
          "引用": []
        }
        """);
    try
    {
        try
        {
            YyProject.LoadForDirectory(directory);
            throw new Exception("旧版 .ey 项目不应被加载。");
        }
        catch (InvalidDataException exception)
        {
            True(exception.Message.Contains("有效的易语工程", StringComparison.Ordinal), exception.Message);
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});

Run("重复控件名诊断", () =>
{
    const string xml = """
        <窗体 标识="@id/主窗口" 名称="主窗口" 标题="测试" 宽="640" 高="420">
          <按钮 标识="@id/主窗口/重复" 名称="重复" 文本="一" 左="10" 上="10" 宽="100" 高="40" />
          <按钮 标识="@id/主窗口/重复" 名称="重复" 文本="二" 左="20" 上="60" 宽="100" 高="40" />
        </窗体>
        """;
    try
    {
        YyFormSerializer.Deserialize(xml);
        throw new Exception("应报告重复控件名。");
    }
    catch (YyFormFormatException exception)
    {
        True(exception.Message.Contains("重复", StringComparison.Ordinal), exception.Message);
        True(exception.Line >= 2, "重复控件名需要包含行号。");
    }
});

Run("损坏 XML 诊断", () =>
{
    try
    {
        YyFormSerializer.Deserialize("<窗体 名称=\"测试\"><按钮></窗体>");
        throw new Exception("应报告 XML 格式错误。");
    }
    catch (YyFormFormatException exception)
    {
        True(exception.Line >= 1, "XML 错误需要包含行号。");
    }
});

Run("手写源文件保持模块化体积", () =>
{
    const int maximumLines = 900;
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null &&
           !File.Exists(Path.Combine(directory.FullName, "Eyuyan.slnx")))
    {
        directory = directory.Parent;
    }

    True(directory is not null, "找不到仓库根目录。");
    var oversized = Directory.EnumerateFiles(
            directory!.FullName,
            "*.cs",
            SearchOption.AllDirectories)
        .Where(path => path.Split(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .All(part => part is not ("bin" or "obj" or ".git")))
        .Select(path => new
        {
            Path = Path.GetRelativePath(directory.FullName, path),
            Lines = File.ReadLines(path).Count()
        })
        .Where(file => file.Lines > maximumLines)
        .ToArray();
    True(
        oversized.Length == 0,
        "以下文件超过模块体积门槛：" +
        string.Join("、", oversized.Select(file => $"{file.Path}({file.Lines})")));
});

if (string.Equals(
        Environment.GetEnvironmentVariable("YY_COMPILE_SMOKE"),
        "1",
        StringComparison.Ordinal))
{
    Run("编译并启动独立 EXE", TestCompiledExe);
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "全部桌面模型测试通过。" : $"{failures} 项测试失败。");
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"通过  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"失败  {name}: {exception.Message}");
    }
    finally
    {
        Console.ResetColor();
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"期望“{expected}”，实际“{actual}”。");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

static void TestCompiledExe()
{
    var projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), "examples");
    var project = YyProject.LoadForDirectory(projectDirectory)
        ?? throw new Exception("找不到示例项目。");
    var outputPath = Path.Combine(
        Path.GetTempPath(),
        "yy-compiled-" + Guid.NewGuid().ToString("N") + ".exe");
    try
    {
        var compiler = new ProjectCompiler();
        compiler.CompileAsync(project, outputPath).GetAwaiter().GetResult();
        True(File.Exists(outputPath), "编译后没有生成 EXE。");
        True(new FileInfo(outputPath).Length > 10_000_000, "编译后的 EXE 大小异常。");

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = outputPath,
            UseShellExecute = false
        }) ?? throw new Exception("无法启动编译后的 EXE。");
        try
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                Thread.Sleep(200);
                process.Refresh();
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    break;
                }
            }

            True(process.MainWindowTitle == "客户登记", $"编译程序窗口标题异常：{process.MainWindowTitle}");
            True(process.Responding, "编译程序没有响应。");
            var window = AutomationElement.FromHandle(process.MainWindowHandle);
            var input = window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Edit));
            True(input is not null, "编译程序中没有找到输入框。");
            ((ValuePattern)input!.GetCurrentPattern(ValuePattern.Pattern))
                .SetValue("编译客户");
            Thread.Sleep(500);
            var texts = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Text));
            True(
                texts.Cast<AutomationElement>().Any(element =>
                    element.Current.Name.Contains("你好，编译客户", StringComparison.Ordinal)),
                "编译后的文本改变事件没有更新标题控件。");
            var saveButton = window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "保存客户"));
            True(saveButton is not null, "编译程序中没有找到保存按钮。");
            ((InvokePattern)saveButton!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(1000);
            var value = ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern))
                .Current.Value;
            Equal("已保存：编译客户", value);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }
    finally
    {
        if (File.Exists(outputPath))
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    File.Delete(outputPath);
                    break;
                }
                catch (IOException) when (attempt < 9)
                {
                    Thread.Sleep(200);
                }
                catch (IOException)
                {
                    // Windows Defender can briefly hold freshly compiled executables.
                }
            }
        }
    }
}

