using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yy.Language;
using Yy.Studio.Forms;

namespace Yy.Studio;

public sealed record YyProjectForm(
    string FormPath,
    string? CodePath,
    YyFormModel Model);

public sealed class YyProject
{
    private readonly List<string> _formPaths;
    private readonly List<string> _sourcePaths;
    private readonly List<string> _resources;
    private readonly List<string> _references;

    private YyProject(
        string projectPath,
        int formatVersion,
        string name,
        string entryPath,
        string outputName,
        string startupForm,
        List<string> sourcePaths,
        List<string> formPaths,
        List<string> resources,
        List<string> references,
        IReadOnlyList<YyProjectForm> forms)
    {
        ProjectPath = projectPath;
        FormatVersion = formatVersion;
        Name = name;
        EntryPath = entryPath;
        OutputName = outputName;
        StartupForm = startupForm;
        _sourcePaths = sourcePaths;
        _formPaths = formPaths;
        _resources = resources;
        _references = references;
        Forms = forms;
    }

    public string ProjectPath { get; }
    public int FormatVersion { get; }
    public string DirectoryPath => Path.GetDirectoryName(ProjectPath) ?? string.Empty;
    public string Name { get; }
    public string EntryPath { get; }
    public string OutputName { get; }
    public string StartupForm { get; }
    public IReadOnlyList<string> SourcePaths => _sourcePaths;
    public IReadOnlyList<string> FormPaths => _formPaths;
    public IReadOnlyList<string> Resources => _resources;
    public IReadOnlyList<string> References => _references;
    public IReadOnlyList<YyProjectForm> Forms { get; }

    public static YyProject? LoadForDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"项目目录不存在：{directory}");
        }

        var projectFile = Directory.EnumerateFiles(directory, "*.yyproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (projectFile is null)
        {
            return null;
        }

        YyProjectManifest manifest;
        try
        {
            var json = File.ReadAllText(projectFile);
            manifest = JsonSerializer.Deserialize<YyProjectManifest>(json)
                ?? throw new InvalidDataException("项目清单内容为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"无法读取项目清单 {Path.GetFileName(projectFile)}：{exception.Message}",
                exception);
        }

        if (manifest.FormatVersion is not 1 and not 2)
        {
            throw new InvalidDataException(
                $"项目格式版本 {manifest.FormatVersion} 不受支持，当前支持版本为 1 和 2。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidDataException("项目清单缺少“名称”。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry))
        {
            throw new InvalidDataException("项目清单缺少“入口”。");
        }

        var fullProjectPath = Path.GetFullPath(projectFile);
        var baseDirectory = Path.GetDirectoryName(fullProjectPath) ?? directory;
        var entryPath = ResolveProjectFile(baseDirectory, manifest.Entry, "项目入口");
        var sourcePaths = ResolveOptionalFiles(
            baseDirectory,
            manifest.Sources,
            ".ey",
            "源码");
        var formPaths = ResolveOptionalFiles(
            baseDirectory,
            manifest.Forms,
            ".yyx",
            "窗体");
        var resources = (manifest.Resources ?? [])
            .Select(path => ResolveProjectFile(baseDirectory, path, "资源"))
            .ToList();
        var references = (manifest.References ?? [])
            .Select(path => ResolveProjectFile(baseDirectory, path, "引用"))
            .ToList();

        if (!sourcePaths.Contains(entryPath, StringComparer.OrdinalIgnoreCase))
        {
            sourcePaths.Insert(0, entryPath);
        }

        var forms = new List<YyProjectForm>();
        var classNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var formPath in formPaths)
        {
            var form = YyFormSerializer.Deserialize(File.ReadAllText(formPath));
            if (!classNames.Add(form.Name))
            {
                throw new InvalidDataException(
                    $"项目窗体类“{form.Name}”重复定义：{Path.GetFileName(formPath)}");
            }

            string? codePath = null;
            if (!string.IsNullOrWhiteSpace(form.CodeFile))
            {
                var formDirectory = Path.GetDirectoryName(formPath)
                    ?? throw new InvalidDataException($"无法确定窗体目录：{formPath}");
                codePath = ResolveProjectFile(formDirectory, form.CodeFile, "窗体代码后置文件");
                if (!sourcePaths.Contains(codePath, StringComparer.OrdinalIgnoreCase))
                {
                    sourcePaths.Add(codePath);
                }

                ValidateEventBindings(form, codePath);
            }

            forms.Add(new YyProjectForm(formPath, codePath, form));
        }

        var startupForm = string.IsNullOrWhiteSpace(manifest.StartupForm)
            ? forms.FirstOrDefault()?.Model.Name
            : manifest.StartupForm.Trim();
        if (forms.Count > 0 && string.IsNullOrWhiteSpace(startupForm))
        {
            throw new InvalidDataException("项目没有可用的启动窗体。");
        }

        if (startupForm is not null &&
            forms.All(form => !string.Equals(form.Model.Name, startupForm, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"项目启动窗体“{startupForm}”不在窗体列表中。");
        }

        var outputName = string.IsNullOrWhiteSpace(manifest.Output)
            ? manifest.Name + ".exe"
            : Path.GetFileName(manifest.Output);
        return new YyProject(
            fullProjectPath,
            manifest.FormatVersion,
            manifest.Name.Trim(),
            entryPath,
            outputName,
            startupForm ?? string.Empty,
            sourcePaths,
            formPaths,
            resources,
            references,
            forms);
    }

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

        var entryPath = Path.Combine(root, "Program.ey");
        var formPath = Path.Combine(formsDirectory, "MainForm.yyx");
        var codePath = Path.Combine(formsDirectory, "MainForm.ey");

        File.WriteAllText(entryPath, CreateEntryTemplate(projectName), new UTF8Encoding(false));
        File.WriteAllText(codePath, CreateCodeBehindTemplate(), new UTF8Encoding(false));
        var form = new YyFormModel
        {
            Name = "MainForm",
            CodeFile = "MainForm.ey",
            LoadEvent = "主窗口_载入",
            CloseEvent = "主窗口_关闭",
            Title = projectName,
            Width = 760,
            Height = 500
        };
        form.Controls.Add(CreateWelcomeLabel());
        form.Controls.Add(CreateWelcomeButton());
        File.WriteAllText(formPath, YyFormSerializer.Serialize(form), new UTF8Encoding(false));

        var manifest = new YyProjectManifest
        {
            FormatVersion = 2,
            Name = projectName,
            Entry = "Program.ey",
            Output = projectName + ".exe",
            StartupForm = "MainForm",
            Sources = ["Program.ey", "Forms/MainForm.ey"],
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
            className = "Window" + sequence;
            sequence++;
        }
        while (Forms.Any(form => string.Equals(form.Model.Name, className, StringComparison.Ordinal)) ||
               File.Exists(Path.Combine(DirectoryPath, "Forms", className + ".yyx")));

        var formsDirectory = Path.Combine(DirectoryPath, "Forms");
        Directory.CreateDirectory(formsDirectory);
        var formPath = Path.Combine(formsDirectory, className + ".yyx");
        var codePath = Path.Combine(formsDirectory, className + ".ey");
        var model = new YyFormModel
        {
            Name = className,
            CodeFile = className + ".ey",
            Title = title,
            Width = 720,
            Height = 480
        };
        var button = YyControlTypes.Create(YyControlTypes.Button, 1);
        button.Name = "确定按钮";
        button.Text = "确定";
        button.ClickEvent = "确定按钮_被单击";
        model.Controls.Add(button);

        File.WriteAllText(formPath, YyFormSerializer.Serialize(model), new UTF8Encoding(false));
        File.WriteAllText(
            codePath,
            $"""
            .版本 3
            .程序集 {className}代码

            子程序 确定按钮_被单击()
                提示("窗体事件已经执行。")
            结束子程序
            """,
            new UTF8Encoding(false));

        AddRelativePath(_formPaths, formPath, DirectoryPath);
        AddRelativePath(_sourcePaths, codePath, DirectoryPath);
        WriteCurrentManifest();
        return new YyProjectForm(formPath, codePath, model);
    }

    private void WriteCurrentManifest()
    {
        var manifest = new YyProjectManifest
        {
            FormatVersion = 2,
            Name = Name,
            Entry = ToRelativePath(EntryPath, DirectoryPath),
            Output = OutputName,
            StartupForm = StartupForm,
            Sources = _sourcePaths.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            Forms = _formPaths.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            Resources = _resources.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            References = _references.Select(path => ToRelativePath(path, DirectoryPath)).ToList()
        };
        WriteManifest(ProjectPath, manifest);
    }

    private static void ValidateEventBindings(YyFormModel form, string codePath)
    {
        var source = File.ReadAllText(codePath, new UTF8Encoding(false, true));
        var program = YyRunner.CompileSource(source, codePath);
        var handlers = program.Functions
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (eventName, eventKind) in new[]
                 {
                     (form.LoadEvent, "载入"),
                     (form.CloseEvent, "关闭")
                 })
        {
            if (!string.IsNullOrWhiteSpace(eventName) && !handlers.Contains(eventName))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Title}”绑定了不存在的事件函数“{eventName}”（{eventKind}，{Path.GetFileName(codePath)}）。");
            }
        }

        foreach (var control in form.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.ClickEvent) &&
                !handlers.Contains(control.ClickEvent))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Title}”的控件“{control.Name}”绑定了不存在的事件函数“{control.ClickEvent}”（{Path.GetFileName(codePath)}）。");
            }
        }
    }

    private static List<string> ResolveOptionalFiles(
        string baseDirectory,
        List<string>? explicitPaths,
        string extension,
        string description)
    {
        if (explicitPaths is { Count: > 0 })
        {
            return explicitPaths
                .Select(path => ResolveProjectFile(baseDirectory, path, description))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return Directory.EnumerateFiles(baseDirectory, "*" + extension, SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or ".git" or ".vs"))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveProjectFile(string baseDirectory, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"{description}路径必须是非空相对路径：{relativePath}");
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        if (!IsWithin(fullPath, baseDirectory))
        {
            throw new InvalidDataException($"{description}不能位于项目目录之外：{relativePath}");
        }

        if (!File.Exists(fullPath))
        {
            throw new InvalidDataException($"{description}不存在：{relativePath}");
        }

        return fullPath;
    }

    private static bool IsWithin(string path, string directory)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddRelativePath(List<string> paths, string path, string baseDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(fullPath);
        }
    }

    private static string ToRelativePath(string path, string baseDirectory)
    {
        return Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
    }

    private static void WriteManifest(string path, YyProjectManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string CreateEntryTemplate(string projectName) =>
        $"""
        .版本 3
        .程序集 {projectName}

        ' 项目入口。窗体代码位于 Forms 目录。
        输出("{projectName} 已启动。")
        """;

    private static string CreateCodeBehindTemplate() =>
        """
        .版本 3
        .程序集 MainForm代码

        ' 控件在窗体运行时作为对象变量注入。
        子程序 主窗口_载入()
            输出("窗体已经载入。")
        结束子程序

        子程序 欢迎按钮_被单击()
            提示("界面事件已经连接到代码后置文件。")
        结束子程序

        子程序 主窗口_关闭()
            输出("窗体已经关闭。")
        结束子程序
        """;

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
            ClickEvent = "欢迎按钮_被单击",
            X = 40,
            Y = 108,
            Width = 150,
            Height = 42,
            Background = "#2F7F6E",
            Foreground = "#FFFFFF"
        };

    private sealed class YyProjectManifest
    {
        [JsonPropertyName("格式版本")]
        public int FormatVersion { get; init; } = 2;

        [JsonPropertyName("名称")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("入口")]
        public string Entry { get; init; } = string.Empty;

        [JsonPropertyName("输出")]
        public string? Output { get; init; }

        [JsonPropertyName("启动窗体")]
        public string? StartupForm { get; init; }

        [JsonPropertyName("源码")]
        public List<string>? Sources { get; init; }

        [JsonPropertyName("窗体")]
        public List<string>? Forms { get; init; }

        [JsonPropertyName("资源")]
        public List<string>? Resources { get; init; }

        [JsonPropertyName("引用")]
        public List<string>? References { get; init; }
    }
}
