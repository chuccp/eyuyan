using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Yy.Studio.Avalonia.Highlighting;

/// <summary>集中加载易语 XSHD 语法定义，供代码编辑器和经典视图共用。</summary>
public static class YyHighlighting
{
    public const string ResourceName = "Yy.Studio.Avalonia.Highlighting.Yy.xshd";

    private static readonly Lazy<IHighlightingDefinition?> Definition = new(Load);

    /// <summary>易语语法着色定义；资源缺失时返回 null，不阻断编辑。</summary>
    public static IHighlightingDefinition? Yy => Definition.Value;

    /// <summary>按文件扩展名选择易语或 XML 着色。</summary>
    public static IHighlightingDefinition? ForPath(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty);
        if (string.Equals(extension, ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            return Yy;
        }

        return string.Equals(extension, ".yyx", StringComparison.OrdinalIgnoreCase)
            ? HighlightingManager.Instance.GetDefinition("XML")
            : null;
    }

    private static IHighlightingDefinition? Load()
    {
        using var stream = typeof(YyHighlighting).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
