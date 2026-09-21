namespace DotnetInspector.Sections;

public sealed class RowsCohortSequence<TIdentity, T>
    where TIdentity : notnull
{
    private RowsCohortSequence(
        TIdentity identity,
        IReadOnlyList<T> values,
        RowSequenceKey? key)
    {
        Identity = identity;
        Values = values;
        Key = key;
    }

    public TIdentity Identity { get; }

    public IReadOnlyList<T> Values { get; }

    internal RowSequenceKey? Key { get; }

    public static RowsCohortSequence<TIdentity, T> Create(
        TIdentity identity,
        IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(values);
        return new(
            identity,
            SectionContractSnapshot.Copy(values),
            null);
    }

    internal static RowsCohortSequence<TIdentity, T> CreateBound(
        TIdentity identity,
        IReadOnlyList<T> values,
        RowSequenceKey key)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(key);
        return new(
            identity,
            SectionContractSnapshot.Copy(values),
            key);
    }
}
