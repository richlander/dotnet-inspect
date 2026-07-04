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

            for (int i = oldGroup.Length; i < newGroup.Length; i++)
                rows.Add(ToRow(BodySignalDiffKind.Added, newGroup[i]));
            for (int i = newGroup.Length; i < oldGroup.Length; i++)
                rows.Add(ToRow(BodySignalDiffKind.Removed, oldGroup[i]));
        }

        return new BodySignalDiffResult(rows.ToImmutable());
    }

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
