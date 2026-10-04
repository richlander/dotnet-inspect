namespace QuerySpace.Rows;

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
            QuerySpaceSnapshot.Copy(values));
    }
}
