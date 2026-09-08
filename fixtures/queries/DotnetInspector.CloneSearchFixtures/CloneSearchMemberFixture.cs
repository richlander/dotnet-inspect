namespace Cases;

/// <summary>
/// An ordinary compiled property and event whose accessors carry real method
/// bodies, so a logical Member seed has exact bodies to expand to.
/// </summary>
public sealed class Widget
{
    int _value;
    EventHandler? _changed;

    /// <summary>
    /// A field: a supported Member subject that occupies no method body.
    /// </summary>
    public int Tag;

    /// <summary>A property whose getter and setter both occupy a body.</summary>
    public int Value
    {
        get { return _value; }
        set { _value = value; }
    }

    /// <summary>An event whose adder and remover both occupy a body.</summary>
    public event EventHandler? Changed
    {
        add { _changed += value; }
        remove { _changed -= value; }
    }

    public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A structural peer of <see cref="Widget"/> under a similar type name, so a
/// Member seed expanded to accessor bodies has qualifying candidates.
/// </summary>
public sealed class Widgets
{
    int _value;
    EventHandler? _changed;

    public int Value
    {
        get { return _value; }
        set { _value = value; }
    }

    public event EventHandler? Changed
    {
        add { _changed += value; }
        remove { _changed -= value; }
    }

    public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Two indexer overloads that differ only by their index parameter type, so
/// they collide on <c>P:Cases.Lookup.Item</c> unless index parameters are part
/// of property identity. Their accessor counts differ, which makes the
/// selected overload's own bodies observable.
/// </summary>
public sealed class Lookup
{
    readonly Dictionary<int, string> _byIndex = [];

    /// <summary>The int overload: a getter and a setter.</summary>
    public string this[int index]
    {
        get { return _byIndex[index]; }
        set { _byIndex[index] = value; }
    }

    /// <summary>The string overload: a getter only.</summary>
    public string this[string key]
    {
        get { return _byIndex[key.Length]; }
    }
}
