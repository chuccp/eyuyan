namespace Yy.Native.Semantics;

internal sealed record NativeObjectField(
    string Name,
    NativeValueKind Kind,
    NativeValueKind ArrayElementKind);

internal sealed record NativeObjectShape(IReadOnlyList<NativeObjectField> Fields)
{
    public NativeObjectField? Find(string name) =>
        Fields.FirstOrDefault(field =>
            string.Equals(field.Name, name, StringComparison.Ordinal));

    public int IndexOf(string name)
    {
        for (var index = 0; index < Fields.Count; index++)
        {
            if (string.Equals(Fields[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
