using System.Collections.Immutable;

namespace ILInspector.Analysis;

public enum BodySignalDiffKind
{
    Added,
    Removed,
}

public sealed record BodySignalDiffRow(
    BodySignalDiffKind Kind,
    string Signal,
    string Member,
    string Operation,
    int? ILOffset,
    string Evidence);

public sealed record BodySignalDiffResult(ImmutableArray<BodySignalDiffRow> Rows)
{
    public bool IsEmpty => Rows.IsEmpty;
}

/// <summary>
/// Product-level body signal diff over Analysis facts.
/// </summary>
public static class BodySignalDiff
{
    public static BodySignalDiffResult CompareUnsafe(LibraryBodyIndex oldIndex, LibraryBodyIndex newIndex)
    {
        ArgumentNullException.ThrowIfNull(oldIndex);
        ArgumentNullException.ThrowIfNull(newIndex);

        var oldFacts = UnsafeFactGroups(oldIndex);
        var newFacts = UnsafeFactGroups(newIndex);
        var rows = ImmutableArray.CreateBuilder<BodySignalDiffRow>();
        var keys = new SortedSet<string>(oldFacts.Keys.Concat(newFacts.Keys), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            oldFacts.TryGetValue(key, out var oldGroup);
            newFacts.TryGetValue(key, out var newGroup);
            oldGroup ??= [];
            newGroup ??= [];

            AddDifferenceRows(rows, oldGroup, newGroup);
        }

        return new BodySignalDiffResult(rows.ToImmutable());
    }

    static void AddDifferenceRows(
        ImmutableArray<BodySignalDiffRow>.Builder rows,
        UnsafeFact[] oldGroup,
        UnsafeFact[] newGroup)
    {
        int addedCount = newGroup.Length - oldGroup.Length;
        int removedCount = oldGroup.Length - newGroup.Length;
        if (addedCount > 0)
            AddDeltaRows(rows, BodySignalDiffKind.Added, UnmatchedByOffset(newGroup, oldGroup), UnmatchedByOffset(oldGroup, newGroup), addedCount);
        if (removedCount > 0)
            AddDeltaRows(rows, BodySignalDiffKind.Removed, UnmatchedByOffset(oldGroup, newGroup), UnmatchedByOffset(newGroup, oldGroup), removedCount);
    }

    static void AddDeltaRows(
        ImmutableArray<BodySignalDiffRow>.Builder rows,
        BodySignalDiffKind kind,
        UnsafeFact[] candidate,
        UnsafeFact[] opposite,
        int count)
    {
        if (candidate.Length == count && opposite.Length == 0)
        {
            foreach (var fact in candidate)
                rows.Add(ToRow(kind, fact));
            return;
        }

        for (int i = 0; i < count; i++)
            rows.Add(ToRow(kind, candidate[Math.Min(i, candidate.Length - 1)] with { ILOffset = null }));
    }

    static UnsafeFact[] UnmatchedByOffset(UnsafeFact[] candidate, UnsafeFact[] baseline)
    {
        var baselineOffsets = baseline
            .GroupBy(fact => fact.ILOffset)
            .ToDictionary(group => OffsetKey(group.Key), group => group.Count(), StringComparer.Ordinal);

        var result = ImmutableArray.CreateBuilder<UnsafeFact>();
        foreach (var fact in candidate)
        {
            string offsetKey = OffsetKey(fact.ILOffset);
            if (baselineOffsets.TryGetValue(offsetKey, out int count) && count > 0)
            {
                baselineOffsets[offsetKey] = count - 1;
                continue;
            }

            result.Add(fact);
        }

        return result.ToArray();
    }

    static string OffsetKey(int? offset)
        => offset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";

    static Dictionary<string, UnsafeFact[]> UnsafeFactGroups(LibraryBodyIndex index)
    {
        var facts = SemanticFactProjection.SafetyFacts(
            index.GetUnsafeEvidenceByMember().Values.SelectMany(group => group).ToImmutableArray(),
            index.GetUnsafetyOccurrences());
        return facts
            .Select(fact => new UnsafeFact(
                MethodKey(fact.Method),
                fact.SafetyKind,
                fact.Operation,
                fact.ILOffset,
                fact.Evidence))
            .GroupBy(fact => $"{fact.MemberKey}|{fact.Signal}|{fact.Operation}|{fact.Evidence}", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(fact => fact.ILOffset ?? -1).ToArray(),
                StringComparer.Ordinal);
    }

    static BodySignalDiffRow ToRow(BodySignalDiffKind kind, UnsafeFact fact)
        => new(
            kind,
            fact.Signal,
            fact.MemberKey,
            fact.Operation,
            fact.ILOffset,
            fact.Evidence);

    static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    sealed record UnsafeFact(string MemberKey, string Signal, string Operation, int? ILOffset, string Evidence);
}
