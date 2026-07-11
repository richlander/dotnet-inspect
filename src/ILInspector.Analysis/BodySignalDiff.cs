using System.Collections.Immutable;

using ILInspector.Findings;

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

        var rows = ImmutableArray.CreateBuilder<BodySignalDiffRow>();
        var oldMethods = MethodsByKey(oldIndex);
        var newMethods = MethodsByKey(newIndex);
        var keys = new SortedSet<string>(oldMethods.Keys.Concat(newMethods.Keys), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            oldMethods.TryGetValue(key, out var oldMethod);
            newMethods.TryGetValue(key, out var newMethod);
            var method = newMethod ?? oldMethod
                ?? throw new InvalidOperationException($"Unsafe method identity '{key}' has no payload.");
            var subject = new FindingSubject(
                key,
                $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}");

            var oldOccurrences = Occurrences(oldIndex, oldMethod);
            var newOccurrences = Occurrences(newIndex, newMethod);
            AddComparisonRows(
                rows,
                key,
                AnalysisFindings.CompareUnsafety(
                    oldOccurrences,
                    newOccurrences,
                    subject),
                SemanticFactProjection.ToSafetyFact);

            var oldEvidence = UncoveredEvidence(
                Evidence(oldIndex, oldMethod),
                oldOccurrences);
            var newEvidence = UncoveredEvidence(
                Evidence(newIndex, newMethod),
                newOccurrences);
            AddComparisonRows(
                rows,
                key,
                AnalysisFindings.CompareUnsafeEvidence(
                    oldEvidence,
                    newEvidence,
                    subject),
                SemanticFactProjection.ToSafetyFact);
        }

        return new BodySignalDiffResult(
        [
            .. rows
                .OrderBy(static row => row.Member, StringComparer.Ordinal)
                .ThenBy(static row => row.Signal, StringComparer.Ordinal)
                .ThenBy(static row => row.Operation, StringComparer.Ordinal)
                .ThenBy(static row => row.Evidence, StringComparer.Ordinal)
                .ThenBy(static row => row.Kind)
                .ThenBy(static row => row.ILOffset),
        ]);
    }

    static void AddComparisonRows<T>(
        ImmutableArray<BodySignalDiffRow>.Builder rows,
        string memberKey,
        FindingComparison<T> comparison,
        Func<T, SafetyFact> project)
        where T : notnull
    {
        var complete = comparison switch
        {
            FindingComparison<T>.Complete value => value,
            // FindingComparison fails only when an input inspection is Failed. Both
            // producer inputs above are total Complete censuses; acceptanceThreshold
            // affects fringe folding, not the comparison outcome.
            FindingComparison<T>.Failed failed => throw new InvalidOperationException(
                $"A comparison of total Analysis censuses cannot fail: {failed.Failure}"),
        };
        var oldGroups = GroupFacts(complete.OldAtoms, memberKey, project);
        var newGroups = GroupFacts(complete.NewAtoms, memberKey, project);
        var addedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var removedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var pair in complete.Pairs)
        {
            switch (pair)
            {
                case PairFinding<T>.Added added:
                    Increment(addedCounts, added.New.Key.IdentityKey);
                    break;
                case PairFinding<T>.Removed removed:
                    Increment(removedCounts, removed.Old.Key.IdentityKey);
                    break;
            }
        }

        var keys = new SortedSet<string>(
            addedCounts.Keys.Concat(removedCounts.Keys),
            StringComparer.Ordinal);
        foreach (string key in keys)
        {
            oldGroups.TryGetValue(key, out var oldGroup);
            newGroups.TryGetValue(key, out var newGroup);
            oldGroup ??= [];
            newGroup ??= [];

            int delta = addedCounts.GetValueOrDefault(key) - removedCounts.GetValueOrDefault(key);
            if (delta > 0)
            {
                AddDeltaRows(
                    rows,
                    BodySignalDiffKind.Added,
                    UnmatchedByOffset(newGroup, oldGroup),
                    UnmatchedByOffset(oldGroup, newGroup),
                    delta);
            }
            else if (delta < 0)
            {
                AddDeltaRows(
                    rows,
                    BodySignalDiffKind.Removed,
                    UnmatchedByOffset(oldGroup, newGroup),
                    UnmatchedByOffset(newGroup, oldGroup),
                    -delta);
            }
        }
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

    static Dictionary<string, UnsafeFact[]> GroupFacts<T>(
        ImmutableArray<Finding<T>> findings,
        string memberKey,
        Func<T, SafetyFact> project)
        where T : notnull
        => findings
            .GroupBy(static finding => finding.Key.IdentityKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(finding =>
                    {
                        SafetyFact fact = project(finding.Payload);
                        return new UnsafeFact(
                            memberKey,
                            fact.SafetyKind,
                            fact.Operation,
                            fact.ILOffset,
                            fact.Evidence);
                    })
                    .OrderBy(static fact => fact.ILOffset ?? -1)
                    .ToArray(),
                StringComparer.Ordinal);

    static Dictionary<string, MethodIdentity> MethodsByKey(LibraryBodyIndex index)
        => index.Methods
            .Concat(index.GetUnsafetyOccurrences().Values
                .SelectMany(static group => group)
                .Select(static occurrence => occurrence.Method))
            .Concat(index.GetUnsafeEvidenceByMember().Values
                .SelectMany(static group => group)
                .Select(static evidence => evidence.Member))
            .GroupBy(MethodKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.Ordinal);

    static ImmutableArray<UnsafetyOccurrence> Occurrences(
        LibraryBodyIndex index,
        MethodIdentity? method)
        => method is not null
            && index.GetUnsafetyOccurrences().TryGetValue(method.MetadataToken, out var occurrences)
                ? occurrences
                : [];

    static ImmutableArray<UnsafeEvidence> Evidence(
        LibraryBodyIndex index,
        MethodIdentity? method)
        => method is not null
            && index.GetUnsafeEvidenceByMember().TryGetValue(method.MetadataToken, out var evidence)
                ? evidence
                : [];

    static ImmutableArray<UnsafeEvidence> UncoveredEvidence(
        ImmutableArray<UnsafeEvidence> evidence,
        ImmutableArray<UnsafetyOccurrence> occurrences)
    {
        var coveredOffsets = occurrences
            .Select(static occurrence => occurrence.ILOffset)
            .ToHashSet();
        return
        [
            .. evidence.Where(item =>
                item.ILOffset is null
                || !coveredOffsets.Contains(item.ILOffset.Value)),
        ];
    }

    static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    static BodySignalDiffRow ToRow(BodySignalDiffKind kind, UnsafeFact fact)
        => new(
            kind,
            fact.Signal,
            fact.MemberKey,
            fact.Operation,
            fact.ILOffset,
            fact.Evidence);

    static string MethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{method.GenericArity}|{method.IsExtension}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    sealed record UnsafeFact(string MemberKey, string Signal, string Operation, int? ILOffset, string Evidence);
}
