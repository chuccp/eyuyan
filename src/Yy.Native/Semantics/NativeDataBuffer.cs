using System.Text;

namespace Yy.Native.Semantics;

internal sealed class NativeDataBuffer
{
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<NativeDataFixup> _fixups = [];

    public IReadOnlyList<byte> Bytes => _bytes;
    public IReadOnlyList<NativeDataFixup> Fixups => _fixups;

    public void Align(int alignment)
    {
        while (_bytes.Count % alignment != 0)
        {
            _bytes.Add(0);
        }
    }

    public void MarkLabel(string name) => _labels[name] = _bytes.Count;

    public int ResolveLabel(string name) =>
        _labels.TryGetValue(name, out var offset)
            ? offset
            : throw new InvalidOperationException($"未定义的数据标签：{name}");

    public string AddUtf8String(string value)
    {
        var name = "string_" + _labels.Count;
        Align(8);
        MarkLabel(name);
        var bytes = Encoding.UTF8.GetBytes(value);
        _bytes.AddRange(BitConverter.GetBytes((long)bytes.Length));
        _bytes.AddRange(bytes);
        _bytes.Add(0);
        return name;
    }

    public string AddUtf16String(string value)
    {
        var name = "utf16_" + _labels.Count;
        Align(2);
        MarkLabel(name);
        _bytes.AddRange(Encoding.Unicode.GetBytes(value));
        _bytes.Add(0);
        _bytes.Add(0);
        return name;
    }

    public void AddUInt32(uint value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void AddUInt64(ulong value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void AddZeroes(int count) => _bytes.AddRange(new byte[count]);

    public void AddFixup(FixupTargetKind kind, string target)
    {
        _fixups.Add(new NativeDataFixup(_bytes.Count, kind, target));
        AddZeroes(8);
    }

    public void AddBytes(string label, params byte[] bytes)
    {
        Align(8);
        MarkLabel(label);
        _bytes.AddRange(bytes);
    }
}
