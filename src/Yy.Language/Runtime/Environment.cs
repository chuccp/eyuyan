namespace Yy.Language.Runtime;

internal sealed class Environment
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

    public Environment(Environment? parent = null)
    {
        Parent = parent;
    }

    public Environment? Parent { get; }

    public void Define(
        string name,
        object? value,
        bool isConstant = false,
        string? typeName = null)
    {
        if (_values.ContainsKey(name))
        {
            throw new YyRuntimeError($"当前作用域已经存在“{name}”", 1, 1);
        }

        YyTypeSystem.Ensure(value, typeName, 1, 1, $"变量“{name}”");
        _values[name] = value;
        if (typeName is not null)
        {
            _types[name] = typeName;
        }

        if (isConstant)
        {
            _constants.Add(name);
        }
    }

    public object? Get(string name, int line, int column)
    {
        if (_values.TryGetValue(name, out var value))
        {
            return value;
        }

        if (Parent is not null)
        {
            return Parent.Get(name, line, column);
        }

        throw new YyRuntimeError($"变量或函数“{name}”没有定义", line, column);
    }

    public bool TryGet(string name, out object? value)
    {
        if (_values.TryGetValue(name, out value))
        {
            return true;
        }

        if (Parent is not null)
        {
            return Parent.TryGet(name, out value);
        }

        value = null;
        return false;
    }

    public void Assign(string name, object? value, int line, int column)
    {
        if (_values.ContainsKey(name))
        {
            if (_constants.Contains(name))
            {
                throw new YyRuntimeError($"常量“{name}”不能重新赋值", line, column);
            }

            if (_types.TryGetValue(name, out var typeName))
            {
                YyTypeSystem.Ensure(value, typeName, line, column, $"变量“{name}”");
            }

            _values[name] = value;
            return;
        }

        if (Parent is not null)
        {
            Parent.Assign(name, value, line, column);
            return;
        }

        throw new YyRuntimeError($"不能给未定义的变量“{name}”赋值", line, column);
    }

    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyTo(snapshot);
        return snapshot;
    }

    private void CopyTo(Dictionary<string, object?> target)
    {
        Parent?.CopyTo(target);
        foreach (var pair in _values)
        {
            target[pair.Key] = pair.Value;
        }
    }
}

