using System.Text.Json;

namespace Yy.Language;

internal sealed record ProjectPackageResolution(
    bool IsPackageImport,
    string? Directory,
    string? Error);

/// <summary>
/// 把“项目包名.源码子目录”解析为工程源码目录中的目录包。
/// 语言层只读取清单中的包名和源码目录，不依赖 Studio 或编译器项目。
/// </summary>
internal static class ProjectPackageResolver
{
    private const string SourceDirectoryName = "Sources";

    public static ProjectPackageResolution Resolve(string importPath, string sourcePath)
    {
        var context = FindContext(sourcePath);
        if (context is null)
        {
            return new(false, null, null);
        }

        var prefix = context.PackageName + ".";
        if (!importPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return new(false, null, null);
        }

        var packageSuffix = importPath[prefix.Length..];
        var segments = packageSuffix.Split('.', StringSplitOptions.None);
        if (segments.Length == 0 ||
            segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return new(
                true,
                null,
                $"命名模块“{importPath}”不是有效的“包名.目录”路径。");
        }

        var directory = segments.Aggregate(
            Path.Combine(context.ProjectRoot, context.SourceDirectory),
            Path.Combine);
        if (!System.IO.Directory.Exists(directory))
        {
            return new(
                true,
                null,
                $"找不到包“{importPath}”。预期目录：{directory}");
        }

        return new(
            true,
            Path.GetFullPath(directory),
            null);
    }

    private static ProjectContext? FindContext(string sourcePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var manifests = System.IO.Directory
                .EnumerateFiles(directory, "*.yyproj", SearchOption.TopDirectoryOnly)
                .ToArray();
            if (manifests.Length == 1)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifests[0]));
                    var root = document.RootElement;
                    var packageName = ReadString(root, "包名");
                    var sourceDirectory = ReadString(root, "源码目录");
                    if (Yy.Sdk.YySdk.IsPackageName(packageName) &&
                        string.Equals(
                            sourceDirectory,
                            SourceDirectoryName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return new ProjectContext(
                            directory,
                            packageName,
                            sourceDirectory);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    return null;
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static string ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private sealed record ProjectContext(
        string ProjectRoot,
        string PackageName,
        string SourceDirectory);
}
