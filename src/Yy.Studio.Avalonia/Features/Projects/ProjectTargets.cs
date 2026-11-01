using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yy.Compiler;

namespace Yy.Studio.Avalonia.Features.Projects;

/// <summary>目标平台候选项，显示名与 <see cref="YyBuildTargets"/> 的稳定 ID 分离。</summary>
public sealed record ProjectTargetChoice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class ProjectTargetCatalog
{
    public static IReadOnlyList<ProjectTargetChoice> All { get; } =
    [
        new(YyBuildTargets.Auto, "自动（当前平台）"),
        new(YyBuildTargets.WindowsX64, "Windows x64"),
        new(YyBuildTargets.MacOSArm64, "macOS ARM64"),
        new(YyBuildTargets.MacOSX64, "macOS x64")
    ];

    public static ProjectTargetChoice Resolve(string? id)
    {
        return All.FirstOrDefault(choice =>
                   string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }
}

/// <summary>
/// 项目清单中“目标平台”目前没有 YyProject 写入 API；读取和更新集中在此处，
/// 其余字段仍完全通过 <see cref="YyProject"/> 的创建、更新和加载 API 维护。
/// </summary>
internal static class ProjectManifestEditor
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ReadTargetPlatform(YyProject project)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(project.ProjectPath)) as JsonObject;
            var value = root?["目标平台"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? YyBuildTargets.Auto : value;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or InvalidOperationException or FormatException)
        {
            return project.TargetPlatform;
        }
    }

    public static YyProject ApplyTargetPlatform(YyProject project, string targetId)
    {
        if (!ProjectTargetCatalog.All.Any(choice =>
                string.Equals(choice.Id, targetId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"未知目标平台“{targetId}”。", nameof(targetId));
        }

        var current = ReadTargetPlatform(project);
        if (string.Equals(current, targetId, StringComparison.OrdinalIgnoreCase))
        {
            return project;
        }

        var root = JsonNode.Parse(File.ReadAllText(project.ProjectPath)) as JsonObject
            ?? throw new InvalidDataException("项目清单内容为空，无法保存目标平台。");
        root["目标平台"] = targetId.Trim();
        WriteAtomic(
            project.ProjectPath,
            root.ToJsonString(WriteOptions) + Environment.NewLine);
        return YyProject.LoadForDirectory(project.DirectoryPath)
            ?? throw new InvalidDataException("目标平台已保存，但项目清单无法重新读取。");
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // 临时文件清理失败不影响原始清单。
            }

            throw;
        }
    }
}
