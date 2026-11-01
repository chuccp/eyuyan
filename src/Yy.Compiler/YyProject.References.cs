using System.IO;

namespace Yy.Compiler;

public sealed partial class YyProject
{
    /// <summary>向项目清单追加一个模块/文件引用并立即保存清单。</summary>
    public void AddReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("引用路径不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("引用的文件不存在。", fullPath);
        }

        if (_references.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _references.Add(fullPath);
        SaveManifest();
    }

    /// <summary>移除项目清单中的引用；返回是否找到并移除。</summary>
    public bool RemoveReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var removed = _references.RemoveAll(reference =>
            string.Equals(reference, fullPath, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SaveManifest();
        }

        return removed > 0;
    }
}
