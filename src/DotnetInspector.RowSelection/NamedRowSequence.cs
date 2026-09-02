namespace DotnetInspector.RowSelection;

public sealed class RowSequenceKey :
    IEquatable<RowSequenceKey>
{
    private RowSequenceKey(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static RowSequenceKey Create(int value) =>
        new(value);

    public bool Equals(RowSequenceKey? other) =>
        other is not null && Value == other.Value;

    public override bool Equals(object? obj) =>
        obj is RowSequenceKey other && Equals(other);

    public override int GetHashCode() =>
        Value;
}

public sealed class NamedRowSequence<T>
{
    private NamedRowSequence(
        RowSequenceKey key,
        IReadOnlyList<T> values)
    {
        Key = key;
        Values = values;
    }

    public RowSequenceKey Key { get; }

    public IReadOnlyList<T> Values { get; }

    public static NamedRowSequence<T> Create(
        RowSequenceKey key,
        IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(values);
        return new(
            key,
            RowSelectionSnapshot.Copy(values));
    }
}
