namespace DotnetInspector.Sections;

public sealed class RowsCohortSequence<TIdentity, T>
    where TIdentity : notnull
{
    private RowsCohortSequence(
        TIdentity identity,
        IReadOnlyList<T> values)
    {
        Identity = identity;
        Values = values;
    }

    public TIdentity Identity { get; }

    public IReadOnlyList<T> Values { get; }

    public static RowsCohortSequence<TIdentity, T> Create(
        TIdentity identity,
        IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(values);
        return new(
            identity,
            SectionContractSnapshot.Copy(values));
    }
}
