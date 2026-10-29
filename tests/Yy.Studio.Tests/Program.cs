using System.IO;
using System.Windows.Automation;
using Yy.Language;
using Yy.Studio;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;

var failures = 0;

Run("窗体 XML 往返", () =>
{
    var form = new YyFormModel
    {
        Name = "测试窗口",
        LoadEvent = "测试窗口_载入",
        CloseEvent = "测试窗口_关闭",
        Title = "客户登记",
        Width = 640,
        Height = 420,
        Background = "#F4F7FB"
    };
    form.Controls.Add(YyControlTypes.Create(YyControlTypes.Button, 1));
    form.Controls.Add(YyControlTypes.Create(YyControlTypes.TextBox, 2));
    form.Controls[1].TextChangedEvent = "文本框_文本改变";

    var xml = YyFormSerializer.Serialize(form);
    var restored = YyFormSerializer.Deserialize(xml);
    Equal("测试窗口", restored.Name);
    Equal("测试窗口_载入", restored.LoadEvent);
    Equal("测试窗口_关闭", restored.CloseEvent);
    Equal("客户登记", restored.Title);
    Equal(2, restored.Controls.Count);
    Equal("按钮1", restored.Controls[0].Name);
    Equal("文本框_文本改变", restored.Controls[1].TextChangedEvent);
});

Run("示例窗体读取", () =>
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "examples", "主窗体.yyx");
    var form = YyFormSerializer.Deserialize(File.ReadAllText(path));
    Equal("主窗口", form.Name);
    Equal("客户登记", form.Title);
    Equal("主窗口_载入", form.LoadEvent);
    Equal("主窗口_关闭", form.CloseEvent);
    Equal(8, form.Controls.Count);
    Equal("保存按钮_被单击", form.Controls.Single(control => control.Name == "保存按钮").ClickEvent);
    Equal(
        "姓名输入框_文本改变",
        form.Controls.Single(control => control.Name == "姓名输入框").TextChangedEvent);
});

Run("结构化代码往返", () =>
{
    const string source = """
        .版本 3
        .程序集 测试程序
        子程序 问候(名称: 文本 = "世界"): 文本
            变量 前缀: 文本 = "你好，"
            返回 前缀 + 名称
        结束子程序
        输出(问候())
        """;
    var document = YyCodeDocumentConverter.FromSource(source);
    Equal(1, document.Functions.Count);
    Equal(1, document.MainStatements.Count);
    var xml = YyCodeSerializer.Serialize(document);
    var restored = YyCodeSerializer.Deserialize(xml);
    Equal("测试程序", restored.Name);
    Equal("问候", restored.Functions[0].Name);
    var lines = new List<string>();
    YyRunner.RunSource(
        YyCodeDocumentConverter.ToSource(restored),
        null,
        lines.Add);
    Equal("你好，世界", string.Join("\n", lines));
});

Run("旧 .ey 二次解析与界面事件关联", () =>
{
    const string legacySource = """
        .版本 3
        .程序集 主窗体代码
        子程序 保存按钮_被单击()
            姓名输入框.文本 = "已保存"
            标题标签.文本 = "完成"
        结束子程序
        """;
    var document = YyCodeDocumentConverter.FromSource(legacySource, "主窗体.ey");
    var structured = YyCodeSerializer.Deserialize(YyCodeSerializer.Serialize(document));
    Equal(1, structured.Functions.Count);
    Equal(2, structured.Functions[0].Statements.Count);

    var form = new YyFormModel { Name = "主窗口", LoadEvent = "主窗口_载入" };
    var button = YyControlTypes.Create(YyControlTypes.Button, 1);
    button.Name = "保存按钮";
    button.ClickEvent = "保存按钮_被单击";
    form.Controls.Add(button);

    var association = YyCodeAssociation.Build(form, structured);
    Equal(2, association.EventCount);
    True(
        association.Controls.Single(item => item.ControlName == "保存按钮")
            .Events.Single().IsImplemented,
        "按钮单击事件没有关联到代码子程序。");
    Equal(1, association.MissingEventCount);
    True(
        association.FormEvents.Single(item => item.EventName == "主窗口_载入").IsImplemented == false,
        "缺失的窗体载入事件没有标记。");
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
});

Run("旧版 .ey 完整项目读取", () =>
{
    var directory = Path.Combine(Directory.GetCurrentDirectory(), "examples", "旧版兼容示例");
    var project = YyProject.LoadForDirectory(directory);
    True(project is not null, "应找到旧版兼容项目清单。");
    True(project!.EntryPath.EndsWith("Program.ey", StringComparison.OrdinalIgnoreCase), "旧版入口未保留。");
    Equal(1, project.Forms.Count);
    True(
        project.Forms[0].CodePath?.EndsWith("MainForm.ey", StringComparison.OrdinalIgnoreCase) == true,
        "旧版代码后置路径未保留。");
    var code = YyCodeDocumentConverter.FromSource(
        File.ReadAllText(project.Forms[0].CodePath!),
        project.Forms[0].CodePath);
    var association = YyCodeAssociation.Build(project.Forms[0].Model, code);
    Equal(3, association.EventCount);
    Equal(0, association.MissingEventCount);
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
            "DemoProject");
        True(File.Exists(project.EntryPath), "项目入口没有创建。");
        True(File.Exists(Path.Combine(projectRoot, "Program.yyc")), "结构化入口源码没有创建。");
        True(File.Exists(Path.Combine(projectRoot, "Forms", "MainForm.yyx")), "窗体没有创建。");
        True(File.Exists(Path.Combine(projectRoot, "Forms", "MainForm.yyc")), "结构化代码后置没有创建。");
        var entryDocument = YyCodeSerializer.Deserialize(
            File.ReadAllText(Path.Combine(projectRoot, "Program.yyc")));
        True(entryDocument.MainStatements.Count > 0, "结构化入口没有命令数据。");
        Equal(1, project.Forms.Count);
        Equal("MainForm", project.StartupForm);
        True(
            project.SourcePaths.Contains(project.Forms[0].CodePath!, StringComparer.OrdinalIgnoreCase),
            "源码列表应包含代码后置文件。");
        var sourcePath = project.CreateSourceFile();
        True(File.Exists(sourcePath), "项目内新建源码失败。");
        project.UpdateSettings("DemoProjectRenamed", "Program.yyc", "RenamedDemo", "MainForm");
        var updated = YyProject.LoadForDirectory(projectRoot)
            ?? throw new Exception("项目属性保存后无法重新加载。");
        Equal("DemoProjectRenamed", updated.Name);
        Equal("RenamedDemo.exe", updated.OutputName);
        True(
            updated.SourcePaths.Contains(sourcePath, StringComparer.OrdinalIgnoreCase),
            "新建源码没有写入项目清单。");
    }
    finally
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
});

Run("重复控件名诊断", () =>
{
    const string xml = """
        <窗体 名称="主窗口" 标题="测试" 宽="640" 高="420">
          <按钮 名称="重复" 文本="一" 左="10" 上="10" 宽="100" 高="40" />
          <按钮 名称="重复" 文本="二" 左="20" 上="60" 宽="100" 高="40" />
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

