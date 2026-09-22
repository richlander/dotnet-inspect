namespace DotnetInspector.Sections;

internal sealed class RowsCohortCardinality<TIdentity>
    where TIdentity : notnull
{
    public RowsCohortCardinality(
        TIdentity identity,
        int count,
        RowSequenceKey key)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(key);
        Identity = identity;
        Count = count;
        Key = key;
    }

    public TIdentity Identity { get; }

    public int Count { get; }

    public RowSequenceKey Key { get; }
}

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

    internal static RowsCohortSequence<TIdentity, T>
        CreateBoundFromDeclaration<TProjection>(
        SectionRowSetDeclaration<TIdentity, TProjection, T> declaration,
        RowSequenceKey key)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(key);
        return new(
            declaration.Identity,
            declaration.Rows,
            key);
    }
}
