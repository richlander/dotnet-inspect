namespace ILInspector.Metadata;

/// <summary>
/// The Members an API-equality extraction projects, by exact metadata name
/// bytes. A method, field, property, or event is selected when its own name,
/// or the name of one of its accessors, equals a selected name byte for byte,
/// so every overload of a selected name and the property or event that owns a
/// selected accessor are projected together.
/// </summary>
internal sealed class ApiMemberNameSelection
{
    readonly List<byte[]> _names = [];

    public int Count => _names.Count;

    /// <summary>Adds one name; a name already selected is not added twice.</summary>
    public void Add(ReadOnlySpan<byte> name)
    {
        if (!Contains(name))
            _names.Add(name.ToArray());
    }

    public bool Contains(ReadOnlySpan<byte> name)
    {
        foreach (byte[] selected in _names)
        {
            if (name.SequenceEqual(selected))
                return true;
        }
        return false;
    }
}
