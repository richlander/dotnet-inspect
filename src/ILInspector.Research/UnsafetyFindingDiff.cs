using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Findings;

namespace ILInspector.Research;

internal enum UnsafetyFindingChangeKind
{
    Added,
    Removed,
}

internal sealed record UnsafetyFindingChange(
    UnsafetyFindingChangeKind Kind,
    string Signal,
    string MemberKey,
    string Operation,
    int? ILOffset,
    string Evidence);

internal static class UnsafetyFindingDiff
{
    public static ImmutableArray<UnsafetyFindingChange> Compare(
        LibraryBodyIndex oldIndex,
        LibraryBodyIndex newIndex,
        Func<MethodIdentity, string> methodKey)
    {
        ArgumentNullException.ThrowIfNull(oldIndex);
        ArgumentNullException.ThrowIfNull(newIndex);
        ArgumentNullException.ThrowIfNull(methodKey);

        var changes = ImmutableArray.CreateBuilder<UnsafetyFindingChange>();
        var oldMethods = MethodsByKey(oldIndex, methodKey);
        var newMethods = MethodsByKey(newIndex, methodKey);
        var keys = new SortedSet<string>(
            oldMethods.Keys.Concat(newMethods.Keys),
            StringComparer.Ordinal);

        foreach (var key in keys)
        {
            oldMethods.TryGetValue(key, out var oldMethod);
            newMethods.TryGetValue(key, out var newMethod);
            var method = newMethod ?? oldMethod
                ?? throw new InvalidOperationException(
                    $"Unsafe method identity '{key}' has no payload.");
            var subject = new FindingSubject(
                key,
                $"{method.DeclaringType.ToQualifiedDisplayString()}.{method.Name}");

            var oldOccurrences = Occurrences(oldIndex, oldMethod);
            var newOccurrences = Occurrences(newIndex, newMethod);
            AddComparisonChanges(
                changes,
                key,
                AnalysisFindings.CompareUnsafety(
                    oldOccurrences,
                    newOccurrences,
                    subject),
                Project);

            AddComparisonChanges(
                changes,
                key,
                AnalysisFindings.CompareUnsafeEvidence(
                    UncoveredEvidence(Evidence(oldIndex, oldMethod), oldOccurrences),
                    UncoveredEvidence(Evidence(newIndex, newMethod), newOccurrences),
                    subject),
                Project);
        }

        return
        [
            .. changes
                .OrderBy(static change => change.MemberKey, StringComparer.Ordinal)
                .ThenBy(static change => change.Signal, StringComparer.Ordinal)
                .ThenBy(static change => change.Operation, StringComparer.Ordinal)
                .ThenBy(static change => change.Evidence, StringComparer.Ordinal)
                .ThenBy(static change => change.Kind)
                .ThenBy(static change => change.ILOffset),
        ];
    }

    static void AddComparisonChanges<T>(
        ImmutableArray<UnsafetyFindingChange>.Builder changes,
        string memberKey,
        FindingComparison<T> comparison,
        Func<T, SafetyProjection> project)
        where T : notnull
    {
        var complete = comparison switch
        {
            FindingComparison<T>.Complete value => value,
            // Both producer inputs are total Complete censuses. A failed
            // comparison here would violate the Analysis producer contract.
            FindingComparison<T>.Failed failed => throw new InvalidOperationException(
                $"A comparison of total Analysis censuses cannot fail: {failed.Failure}"),
        };
        var oldGroups = GroupProjections(complete.OldAtoms, memberKey, project);
        var newGroups = GroupProjections(complete.NewAtoms, memberKey, project);
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

            int delta = addedCounts.GetValueOrDefault(key)
                - removedCounts.GetValueOrDefault(key);
            if (delta > 0)
            {
                AddDeltaChanges(
                    changes,
                    UnsafetyFindingChangeKind.Added,
                    UnmatchedByOffset(newGroup, oldGroup),
                    UnmatchedByOffset(oldGroup, newGroup),
                    delta);
            }
            else if (delta < 0)
            {
                AddDeltaChanges(
                    changes,
                    UnsafetyFindingChangeKind.Removed,
                    UnmatchedByOffset(oldGroup, newGroup),
                    UnmatchedByOffset(newGroup, oldGroup),
                    -delta);
            }
        }
    }

    static void AddDeltaChanges(
        ImmutableArray<UnsafetyFindingChange>.Builder changes,
        UnsafetyFindingChangeKind kind,
        UnsafeProjection[] candidate,
        UnsafeProjection[] opposite,
        int count)
    {
        if (candidate.Length == count && opposite.Length == 0)
        {
            foreach (var projection in candidate)
                changes.Add(ToChange(kind, projection));
            return;
        }

        for (int i = 0; i < count; i++)
        {
            changes.Add(ToChange(
                kind,
                candidate[Math.Min(i, candidate.Length - 1)] with { ILOffset = null }));
        }
    }

    static UnsafeProjection[] UnmatchedByOffset(
        UnsafeProjection[] candidate,
        UnsafeProjection[] baseline)
    {
        var baselineOffsets = baseline
            .GroupBy(projection => projection.ILOffset)
            .ToDictionary(
                group => OffsetKey(group.Key),
                group => group.Count(),
                StringComparer.Ordinal);

        var result = ImmutableArray.CreateBuilder<UnsafeProjection>();
        foreach (var projection in candidate)
        {
            string offsetKey = OffsetKey(projection.ILOffset);
            if (baselineOffsets.TryGetValue(offsetKey, out int count) && count > 0)
            {
                baselineOffsets[offsetKey] = count - 1;
                continue;
            }

            result.Add(projection);
        }

        return result.ToArray();
    }

    static Dictionary<string, UnsafeProjection[]> GroupProjections<T>(
        ImmutableArray<Finding<T>> findings,
        string memberKey,
        Func<T, SafetyProjection> project)
        where T : notnull
        => findings
            .GroupBy(static finding => finding.Key.IdentityKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .Select(finding =>
                    {
                        SafetyProjection projection = project(finding.Payload);
                        return new UnsafeProjection(
                            memberKey,
                            projection.Signal,
                            projection.Operation,
                            projection.ILOffset,
                            projection.Evidence);
                    })
                    .OrderBy(static projection => projection.ILOffset ?? -1)
                    .ToArray(),
                StringComparer.Ordinal);

    static Dictionary<string, MethodIdentity> MethodsByKey(
        LibraryBodyIndex index,
        Func<MethodIdentity, string> methodKey)
        => index.Methods
            .Concat(index.GetUnsafetyOccurrences().Values
                .SelectMany(static group => group)
                .Select(static occurrence => occurrence.Method))
            .Concat(index.GetUnsafeEvidenceByMember().Values
                .SelectMany(static group => group)
                .Select(static evidence => evidence.Member))
            .GroupBy(methodKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.Ordinal);

    static ImmutableArray<UnsafetyOccurrence> Occurrences(
        LibraryBodyIndex index,
        MethodIdentity? method)
        => method is not null
            && index.GetUnsafetyOccurrences().TryGetValue(
                method.MetadataToken,
                out var occurrences)
                ? occurrences
                : [];

    static ImmutableArray<UnsafeEvidence> Evidence(
        LibraryBodyIndex index,
        MethodIdentity? method)
        => method is not null
            && index.GetUnsafeEvidenceByMember().TryGetValue(
                method.MetadataToken,
                out var evidence)
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

    static SafetyProjection Project(UnsafetyOccurrence occurrence)
    {
        string signal = occurrence.Kind switch
        {
            UnsafetyKind.CallIndirect => "calli",
            UnsafetyKind.StackAlloc => "stackalloc",
            _ => "dereference",
        };
        return new(
            signal,
            occurrence.Detail ?? signal,
            occurrence.ILOffset,
            signal);
    }

    static SafetyProjection Project(UnsafeEvidence evidence)
        => new(
            evidence.Reason,
            evidence.Detail,
            evidence.ILOffset,
            evidence.Kind);

    static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.GetValueOrDefault(key) + 1;

    static string OffsetKey(int? offset)
        => offset?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? "<null>";

    static UnsafetyFindingChange ToChange(
        UnsafetyFindingChangeKind kind,
        UnsafeProjection projection)
        => new(
            kind,
            projection.Signal,
            projection.MemberKey,
            projection.Operation,
            projection.ILOffset,
            projection.Evidence);

    sealed record SafetyProjection(
        string Signal,
        string Operation,
        int? ILOffset,
        string Evidence);

    sealed record UnsafeProjection(
        string MemberKey,
        string Signal,
        string Operation,
        int? ILOffset,
        string Evidence);
}
