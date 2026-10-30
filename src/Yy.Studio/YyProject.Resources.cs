using System.IO;

namespace Yy.Studio;

/// <summary>项目资源文件管理、资源表校验和 Resources/ids.xml 同步。</summary>
public sealed partial class YyProject
{
    private static readonly HashSet<string> ResourceTypes =
        ["图片", "声音", "文件"];

    public YyResourceEntry AddResource(
        string sourcePath,
        string typeName,
        string? desiredName = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("资源文件不存在。", sourcePath);
        }

        if (!ResourceTypes.Contains(typeName))
        {
            throw new ArgumentException($"不支持的资源类型“{typeName}”。", nameof(typeName));
        }

        YyResourceIndex.EnsureLayout(DirectoryPath);
        var baseName = MakeIdentifier(
            string.IsNullOrWhiteSpace(desiredName)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : desiredName);
        var existingNames = _resourceTable
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.Ordinal);
        var uniqueName = baseName;
        while (existingNames.Contains(uniqueName))
        {
            uniqueName += "_";
        }

        var directory = YyResourceIndex.ResourceDirectory(DirectoryPath, typeName);
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(sourcePath);
        var targetPath = Path.Combine(directory, uniqueName + extension);
        var suffix = 1;
        while (File.Exists(targetPath) &&
               !string.Equals(
                   Path.GetFullPath(sourcePath),
                   Path.GetFullPath(targetPath),
                   StringComparison.OrdinalIgnoreCase))
        {
            targetPath = Path.Combine(directory, $"{uniqueName}_{suffix++}{extension}");
        }

        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, targetPath);
        }

        var entry = new YyResourceEntry
        {
            Name = uniqueName,
            TypeName = typeName,
            File = ToRelativePath(targetPath, DirectoryPath)
        };
        _resourceTable.Add(entry);
        AddRelativePath(_resources, targetPath, DirectoryPath);
        SaveManifest();
        return entry;
    }

    public void RemoveResource(YyResourceEntry entry)
    {
        _resourceTable.Remove(entry);
        if (!string.IsNullOrWhiteSpace(entry.File))
        {
            var fullPath = ResolveResourceFile(DirectoryPath, entry.File);
            _resources.RemoveAll(path =>
                string.Equals(
                    Path.GetFullPath(path),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
        }

        SaveManifest();
    }

    private static void ValidateResourceEntries(
        string projectDirectory,
        IEnumerable<YyResourceEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var resourceRoot = Path.Combine(projectDirectory, YyResourceIndex.DirectoryName);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.File))
            {
                continue;
            }

            if (!YyResourceIndex.IsIdentifier(entry.Name.Trim()))
            {
                throw new InvalidDataException($"资源名称“{entry.Name}”不是有效标识符。");
            }

            if (!ResourceTypes.Contains(entry.TypeName))
            {
                throw new InvalidDataException(
                    $"资源“{entry.Name}”的类型必须是图片、声音或文件。");
            }

            if (!names.Add(entry.Name.Trim()))
            {
                throw new InvalidDataException($"资源名称“{entry.Name}”重复。");
            }

            var path = ResolveProjectFile(
                projectDirectory,
                entry.File,
                $"资源“{entry.Name}”");
            if (!IsWithin(path, resourceRoot))
            {
                throw new InvalidDataException(
                    $"资源“{entry.Name}”必须位于 {YyResourceIndex.DirectoryName} 目录。");
            }
        }
    }

    private static string MakeIdentifier(string value)
    {
        var trimmed = value.Trim();
        var characters = trimmed
            .Select(character =>
                char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_')
            .ToArray();
        var identifier = new string(characters);
        if (identifier.Length == 0 ||
            !(char.IsLetter(identifier[0]) || identifier[0] == '_'))
        {
            identifier = "_" + identifier;
        }

        return identifier;
    }

    private static void ValidateFormResourceReferences(
        string projectDirectory,
        IEnumerable<YyProjectForm> forms)
    {
        foreach (var form in forms)
        {
            ValidateReference(
                projectDirectory,
                form.Model.Icon,
                $"窗体“{form.Model.Name}”的图标");
            foreach (var control in form.Model.Controls)
            {
                ValidateReference(
                    projectDirectory,
                    control.Picture,
                    $"控件“{form.Model.Name}.{control.Name}”的图片");
            }
        }
    }

    private static void ValidateReference(
        string projectDirectory,
        string reference,
        string description)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        if (YyResourceIndex.ResolveResourcePath(projectDirectory, reference) is null)
        {
            throw new InvalidDataException($"{description}引用了不存在的资源“{reference}”。");
        }
    }

    private static string ResolveResourceFile(string projectDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectDirectory, path));
}
