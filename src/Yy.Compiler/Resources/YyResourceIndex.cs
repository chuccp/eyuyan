using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Yy.Compiler;

/// <summary>
/// 工程资源和控件使用的稳定字符串标识，以及 Resources/ids.xml 的生成规则。
/// 标识文件只负责关联，不承载界面属性或程序逻辑。
/// </summary>
public static class YyResourceIndex
{
    public const string DirectoryName = "Resources";
    public const string FileName = "ids.xml";
    public const string RelativePath = DirectoryName + "/" + FileName;

    private static readonly XNamespace Namespace = "urn:yy-studio:resource-ids:1";

    public static string FormId(string formName) => $"@id/{formName}";

    public static string ControlId(string formName, string controlName) =>
        $"@id/{formName}/{controlName}";

    public static string ResourceId(YyResourceEntry entry) =>
        $"@资源/{entry.TypeName.Trim()}/{entry.Name.Trim()}";

    public static string ResourceDirectory(string projectDirectory, string typeName) =>
        Path.Combine(projectDirectory, DirectoryName, typeName switch
        {
            "图片" => "images",
            "声音" => "sounds",
            _ => "files"
        });

    public static void EnsureLayout(string projectDirectory)
    {
        var root = Path.Combine(projectDirectory, DirectoryName);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "images"));
        Directory.CreateDirectory(Path.Combine(root, "sounds"));
        Directory.CreateDirectory(Path.Combine(root, "files"));
    }

    public static string Generate(
        string projectDirectory,
        IEnumerable<YyProjectForm> forms,
        IEnumerable<YyResourceEntry> resources)
    {
        var root = new XElement(
            Namespace + "资源标识",
            new XAttribute(XNamespace.Xmlns + "yy", Namespace),
            new XAttribute("格式版本", 1));

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var projectForm in forms.OrderBy(
                     item => item.FormPath,
                     StringComparer.OrdinalIgnoreCase))
        {
            var form = projectForm.Model;
            var formId = FormId(form.Name);
            AddIdentifier(identifiers, formId, $"窗体“{form.Name}”");
            var element = new XElement(
                Namespace + "界面",
                new XAttribute("名称", form.Name),
                new XAttribute("标识", formId),
                new XAttribute("文件", Relative(projectDirectory, projectForm.FormPath)));
            foreach (var control in form.Controls)
            {
                var controlId = ControlId(form.Name, control.Name);
                AddIdentifier(identifiers, controlId, $"控件“{form.Name}.{control.Name}”");
                element.Add(new XElement(
                    Namespace + "控件",
                    new XAttribute("名称", control.Name),
                    new XAttribute("类型", control.Type),
                    new XAttribute("标识", controlId)));
            }

            root.Add(element);
        }

        foreach (var resource in resources
                     .Where(IsIndexable)
                     .OrderBy(ResourceId, StringComparer.Ordinal))
        {
            var id = ResourceId(resource);
            AddIdentifier(identifiers, id, $"资源“{resource.Name}”");
            var element = new XElement(
                Namespace + "资源",
                new XAttribute("名称", resource.Name.Trim()),
                new XAttribute("类型", resource.TypeName.Trim()),
                new XAttribute("标识", id),
                new XAttribute("文件", Relative(projectDirectory, resource.File.Trim())));
            if (!string.IsNullOrWhiteSpace(resource.Remark))
            {
                element.SetAttributeValue("备注", resource.Remark);
            }

            root.Add(element);
        }

        var document = new XDocument(
            new XComment(" 此文件由易语 Studio 自动生成。请勿手工修改，保存工程时会完整覆盖。 "),
            root);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
               document.ToString(SaveOptions.None) + Environment.NewLine;
    }

    public static void Write(
        string projectDirectory,
        IEnumerable<YyProjectForm> forms,
        IEnumerable<YyResourceEntry> resources)
    {
        EnsureLayout(projectDirectory);
        var path = Path.Combine(projectDirectory, DirectoryName, FileName);
        WriteAtomic(path, Generate(projectDirectory, forms, resources));
    }

    public static bool IsIndexable(YyResourceEntry resource) =>
        !string.IsNullOrWhiteSpace(resource.Name) &&
        !string.IsNullOrWhiteSpace(resource.TypeName) &&
        !string.IsNullOrWhiteSpace(resource.File) &&
        Yy.Compiler.Forms.YyFormSerializer.IsIdentifier(resource.Name.Trim());

    public static string Relative(string projectDirectory, string path)
    {
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(projectDirectory, path));
        return Path.GetRelativePath(projectDirectory, fullPath).Replace('\\', '/');
    }

    public static bool IsIdentifier(string value) =>
        Yy.Compiler.Forms.YyFormSerializer.IsIdentifier(value);

    public static bool IsIndexPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return string.Equals(
                   Path.GetFileName(fullPath),
                   FileName,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   Path.GetFileName(Path.GetDirectoryName(fullPath)),
                   DirectoryName,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string? FindProjectDirectory(string? formPath)
    {
        if (string.IsNullOrWhiteSpace(formPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(formPath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, DirectoryName, FileName)))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    public static string? ResolveResourcePath(string? projectDirectory, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Path.IsPathRooted(reference))
        {
            return File.Exists(reference) ? Path.GetFullPath(reference) : null;
        }

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return File.Exists(reference) ? Path.GetFullPath(reference) : null;
        }

        var fullProjectDirectory = Path.GetFullPath(projectDirectory);
        if (!reference.StartsWith("@资源/", StringComparison.Ordinal))
        {
            var directPath = Path.GetFullPath(Path.Combine(fullProjectDirectory, reference));
            return File.Exists(directPath) ? directPath : null;
        }

        var index = Path.Combine(fullProjectDirectory, DirectoryName, FileName);
        if (!File.Exists(index))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(index, LoadOptions.None);
            var relativePath = document.Root?
                .Elements()
                .Where(element => element.Name.LocalName == "资源")
                .FirstOrDefault(element => string.Equals(
                    element.Attribute("标识")?.Value,
                    reference,
                    StringComparison.Ordinal))?
                .Attribute("文件")?.Value;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var path = Path.GetFullPath(Path.Combine(fullProjectDirectory, relativePath));
            var resourceRoot = Path.Combine(fullProjectDirectory, DirectoryName);
            return IsWithin(path, resourceRoot) && File.Exists(path) ? path : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    private static void AddIdentifier(
        HashSet<string> identifiers,
        string identifier,
        string description)
    {
        if (!identifiers.Add(identifier))
        {
            throw new InvalidDataException(
                $"{description}的标识“{identifier}”与其他窗体或资源冲突。");
        }
    }

    private static bool IsWithin(string path, string directory)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
