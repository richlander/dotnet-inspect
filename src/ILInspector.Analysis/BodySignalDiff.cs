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

        var oldFacts = UnsafeFactKeys(oldIndex);
        var newFacts = UnsafeFactKeys(newIndex);
        var rows = ImmutableArray.CreateBuilder<BodySignalDiffRow>();

        foreach (var added in newFacts.Keys.Except(oldFacts.Keys).Order(StringComparer.Ordinal))
            rows.Add(ToRow(BodySignalDiffKind.Added, newFacts[added]));
        foreach (var removed in oldFacts.Keys.Except(newFacts.Keys).Order(StringComparer.Ordinal))
            rows.Add(ToRow(BodySignalDiffKind.Removed, oldFacts[removed]));

        return new BodySignalDiffResult(rows.ToImmutable());
    }

    static Dictionary<string, UnsafeFact> UnsafeFactKeys(LibraryBodyIndex index)
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
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
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
        => $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}({string.Join(", ", method.ParameterTypes.Select(type => type.ToQualifiedDisplayString()))}):{method.ReturnType.ToQualifiedDisplayString()}";

    sealed record UnsafeFact(string MemberKey, string Signal, string Operation, int? ILOffset, string Evidence);
}
