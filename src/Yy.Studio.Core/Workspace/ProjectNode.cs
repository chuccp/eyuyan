using System.Collections.ObjectModel;
using System.IO;

namespace Yy.Studio.Core.Workspace;

public sealed class ProjectNode
{
    public ProjectNode(string name, string? fullPath, bool isDirectory, bool isGenerated = false)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsGenerated = isGenerated;
    }

    public string Name { get; }
    public string? FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsGenerated { get; }
    public bool IsExpanded { get; set; } = true;
    public string Icon => IsDirectory
        ? "▸"
        : IsGenerated
            ? "⚙"
            : Path.GetExtension(FullPath ?? Name).ToLowerInvariant() switch
            {
                ".yyc" => "‹›",
                ".yyx" => "▣",
                ".yyproj" => "◆",
                _ => "·"
            };
    public ObservableCollection<ProjectNode> Children { get; } = [];
}
