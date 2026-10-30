using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yy.Language;
using Yy.Compiler.Forms;
using Yy.Language.Structured;

namespace Yy.Compiler;

public sealed partial class YyProject
{
    public const int CurrentFormatVersion = 1;

    private readonly List<string> _formPaths;
    private readonly List<string> _sourcePaths;
    private readonly List<string> _resources;
    private readonly List<string> _references;
    private readonly List<YyGlobalVarEntry> _globals;
    private readonly List<YyConstantEntry> _constants;
    private readonly List<YyResourceEntry> _resourceTable;
    private readonly List<YyDllCommandEntry> _dllCommands;
    private readonly List<YyDbTable> _databaseTables;

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
        List<YyGlobalVarEntry> globals,
        List<YyConstantEntry> constants,
        List<YyResourceEntry> resourceTable,
        List<YyDllCommandEntry> dllCommands,
        List<YyDbTable> databaseTables,
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
        _globals = globals;
        _constants = constants;
        _resourceTable = resourceTable;
        _dllCommands = dllCommands;
        _databaseTables = databaseTables;
        Forms = forms;
    }

    public string ProjectPath { get; }
    public int FormatVersion { get; }
    public string DirectoryPath => Path.GetDirectoryName(ProjectPath) ?? string.Empty;
    public string Name { get; private set; }
    public string EntryPath { get; private set; }
    public string OutputName { get; private set; }
    public string StartupForm { get; private set; }
    public IReadOnlyList<string> SourcePaths => _sourcePaths;
    public IReadOnlyList<string> FormPaths => _formPaths;
    public IReadOnlyList<string> Resources => _resources;
    public IReadOnlyList<string> References => _references;
    public List<YyGlobalVarEntry> Globals => _globals;
    public List<YyConstantEntry> Constants => _constants;
    public List<YyResourceEntry> ResourceTable => _resourceTable;
    public List<YyDllCommandEntry> DllCommands => _dllCommands;
    public List<YyDbTable> DatabaseTables => _databaseTables;
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

        if (manifest.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                "不是有效的易语工程清单格式，请新建工程或使用当前格式。");
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
        if (!string.Equals(
                manifest.ResourceIdFile,
                YyResourceIndex.RelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"项目清单的“资源标识”必须指向 {YyResourceIndex.RelativePath}。");
        }

        var resourceIdsPath = Path.Combine(
            baseDirectory,
            YyResourceIndex.DirectoryName,
            YyResourceIndex.FileName);
        if (!File.Exists(resourceIdsPath))
        {
            throw new InvalidDataException(
                $"项目缺少资源标识文件：{YyResourceIndex.RelativePath}");
        }

        var entryPath = ResolveProjectFile(baseDirectory, manifest.Entry, "项目入口");
        EnsureExtension(entryPath, ".yyc", "项目入口");
        var sourcePaths = ResolveOptionalFiles(
            baseDirectory,
            manifest.Sources,
            ".yyc",
            "源码");
        var formPaths = ResolveOptionalFiles(
            baseDirectory,
            manifest.Forms,
            ".yyx",
            "窗体");
        var resources = (manifest.Resources ?? [])
            .Select(path => ResolveProjectFile(baseDirectory, path, "资源"))
            .ToList();
        var resourceRoot = Path.Combine(baseDirectory, YyResourceIndex.DirectoryName);
        foreach (var resource in resources)
        {
            if (!IsWithin(resource, resourceRoot))
            {
                throw new InvalidDataException(
                    $"资源文件必须位于 {YyResourceIndex.DirectoryName} 目录：{resource}");
            }
        }

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

            var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
            if (!File.Exists(bindingPath))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Name}”缺少自动生成的关系文件：{Path.GetFileName(bindingPath)}");
            }

            var binding = YyFormBindingSerializer.Deserialize(File.ReadAllText(bindingPath));
            YyFormBindingSerializer.ApplyTo(form, binding);
            var expectedUiFile = Path.GetFileName(formPath);
            if (!string.Equals(binding.UiFile, expectedUiFile, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"关系文件“{Path.GetFileName(bindingPath)}”绑定的是“{binding.UiFile}”，" +
                    $"不是“{expectedUiFile}”。");
            }

            var codePath = YyFormCompanionFiles.UserCodePath(formPath);
            if (!string.Equals(
                    binding.CodeFile,
                    Path.GetFileName(codePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Name}”的关系文件必须指向同名用户代码“{Path.GetFileName(codePath)}”。");
            }

            if (!File.Exists(codePath))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Name}”缺少用户代码文件：{Path.GetFileName(codePath)}");
            }

            form.CodeFile = Path.GetFileName(codePath);
            if (!sourcePaths.Contains(codePath, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"窗体“{form.Name}”的用户代码“{Path.GetFileName(codePath)}”没有列入项目源码。");
            }

            forms.Add(new YyProjectForm(formPath, bindingPath, codePath, form));
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
        var resourceTable = manifest.ResourceTable ?? [];
        ValidateResourceEntries(baseDirectory, resourceTable);
        YyResourceIndex.Write(baseDirectory, forms, resourceTable);
        ValidateFormResourceReferences(baseDirectory, forms);
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
            manifest.Globals ?? [],
            manifest.Constants ?? [],
            resourceTable,
            manifest.DllCommands ?? [],
            manifest.DatabaseTables ?? [],
            forms);
    }

    public void UpdateSettings(
        string name,
        string entryRelativePath,
        string outputName,
        string startupForm)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("项目名称不能为空。", nameof(name));
        }

        var entryPath = ResolveProjectFile(DirectoryPath, entryRelativePath, "项目入口");
        EnsureExtension(entryPath, ".yyc", "项目入口");
        if (!string.IsNullOrWhiteSpace(startupForm) &&
            Forms.All(form => !string.Equals(form.Model.Name, startupForm, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"启动窗体“{startupForm}”不在项目窗体列表中。");
        }

        var normalizedOutput = Path.GetFileName(outputName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedOutput))
        {
            normalizedOutput = name + ".exe";
        }

        if (!string.Equals(Path.GetExtension(normalizedOutput), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedOutput += ".exe";
        }

        Name = name.Trim();
        EntryPath = entryPath;
        OutputName = normalizedOutput;
        StartupForm = startupForm.Trim();
        AddRelativePath(_sourcePaths, entryPath, DirectoryPath);
        WriteCurrentManifest();
    }

    /// <summary>将数据表(全局变量/常量表/资源表/Dll命令)等清单内容写回 .yyproj。</summary>
    public void SaveManifest() => WriteCurrentManifest();

    /// <summary>窗体保存后重新读取磁盘界面，刷新 Resources/ids.xml。</summary>
    public void RefreshResourceIndex()
    {
        var forms = _formPaths
            .Where(File.Exists)
            .Select(path => new YyProjectForm(
                path,
                YyFormCompanionFiles.BindingPath(path),
                YyFormCompanionFiles.UserCodePath(path),
                YyFormSerializer.Deserialize(File.ReadAllText(path))))
            .ToList();
        YyResourceIndex.Write(DirectoryPath, forms, _resourceTable);
    }

    private void WriteCurrentManifest()
    {
        var indexedResources = _resources
            .Concat(_resourceTable
                .Where(YyResourceIndex.IsIndexable)
                .Select(item => item.File))
            .Select(path => ResolveResourceFile(DirectoryPath, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifest = new YyProjectManifest
        {
            FormatVersion = CurrentFormatVersion,
            Name = Name,
            Entry = ToRelativePath(EntryPath, DirectoryPath),
            Output = OutputName,
            StartupForm = StartupForm,
            Sources = _sourcePaths.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            Forms = _formPaths.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            Resources = indexedResources
                .Select(path => ToRelativePath(path, DirectoryPath))
                .ToList(),
            ResourceIdFile = YyResourceIndex.RelativePath,
            References = _references.Select(path => ToRelativePath(path, DirectoryPath)).ToList(),
            Globals = _globals.Count > 0 ? _globals : null,
            Constants = _constants.Count > 0 ? _constants : null,
            ResourceTable = _resourceTable.Count > 0 ? _resourceTable : null,
            DllCommands = _dllCommands.Count > 0 ? _dllCommands : null,
            DatabaseTables = _databaseTables.Count > 0 ? _databaseTables : null
        };
        WriteManifest(ProjectPath, manifest);
        RefreshResourceIndex();
    }

    private static List<string> ResolveOptionalFiles(
        string baseDirectory,
        List<string>? explicitPaths,
        string extension,
        string description)
    {
        if (explicitPaths is { Count: > 0 })
        {
            var resolved = explicitPaths
                .Select(path => ResolveProjectFile(baseDirectory, path, description))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var path in resolved)
            {
                EnsureExtension(path, extension, description);
                if (extension == ".yyc" && YyFormCompanionFiles.IsGeneratedBindingPath(path))
                {
                    throw new InvalidDataException(
                        $"{description}不能包含自动关系文件“{Path.GetFileName(path)}”。");
                }
            }

            return resolved;
        }

        return Directory.EnumerateFiles(baseDirectory, "*" + extension, SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or ".git" or ".vs"))
            .Where(path => extension != ".yyc" || !YyFormCompanionFiles.IsGeneratedBindingPath(path))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void EnsureExtension(string path, string extension, string description)
    {
        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{description}必须使用 {extension} 扩展名：{path}");
        }
    }

    private static void WriteStructuredSource(string path, string source)
    {
        var model = YyCodeDocumentConverter.FromSource(source, path);
        File.WriteAllText(path, YyCodeSerializer.Serialize(model), new UTF8Encoding(false));
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

    private sealed class YyProjectManifest
    {
        [JsonPropertyName("格式版本")]
        public int FormatVersion { get; init; } = CurrentFormatVersion;

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

        [JsonPropertyName("资源标识")]
        public string? ResourceIdFile { get; init; }

        [JsonPropertyName("引用")]
        public List<string>? References { get; init; }

        [JsonPropertyName("全局变量")]
        public List<YyGlobalVarEntry>? Globals { get; init; }

        [JsonPropertyName("常量表")]
        public List<YyConstantEntry>? Constants { get; init; }

        [JsonPropertyName("资源表")]
        public List<YyResourceEntry>? ResourceTable { get; init; }

        [JsonPropertyName("Dll命令")]
        public List<YyDllCommandEntry>? DllCommands { get; init; }

        [JsonPropertyName("数据库")]
        public List<YyDbTable>? DatabaseTables { get; init; }
    }
}
