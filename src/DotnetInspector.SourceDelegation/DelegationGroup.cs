namespace DotnetInspector.SourceDelegation;

// One caller-defined execution group: the complete ordered member list of a
// delegation candidate, with each owner-issued member identity appearing
// exactly once. The map may be empty; a source that cannot serve that member
// shape declines it during planning. Member identity is the caller's own token;
// this contract mints no parallel identity system and reads no meaning from
// display text.
public sealed class DelegationGroup<TMember>
    where TMember : notnull
{
    private DelegationGroup(IReadOnlyList<TMember> members)
    {
        Members = members;
    }

    public IReadOnlyList<TMember> Members { get; }

    public int Count => Members.Count;

    public static DelegationGroup<TMember> Create(IEnumerable<TMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        TMember[] snapshot = [.. members];
        var seen = new HashSet<TMember>(EqualityComparer<TMember>.Default);
        for (int index = 0; index < snapshot.Length; index++)
        {
            TMember member = snapshot[index];
            ArgumentNullException.ThrowIfNull(member, nameof(members));
            if (!seen.Add(member))
            {
                throw new ArgumentException(
                    $"Member at position {index} appears more than once in the execution group.",
                    nameof(members));
            }
        }

        return new(DelegationSnapshot.Own(snapshot));
    }

    internal int IndexOf(TMember member)
    {
        var comparer = EqualityComparer<TMember>.Default;
        for (int index = 0; index < Members.Count; index++)
        {
            if (comparer.Equals(Members[index], member))
                return index;
        }

        return -1;
    }
}

internal static class DelegationSnapshot
{
    public static IReadOnlyList<T> Empty<T>() =>
        Array.AsReadOnly(Array.Empty<T>());

    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        T[] copy = [.. values];
        return copy.Length == 0 ? Empty<T>() : Own(copy);
    }

    public static IReadOnlyList<T> Own<T>(T[] values) =>
        values.Length == 0 ? Empty<T>() : Array.AsReadOnly(values);
}
