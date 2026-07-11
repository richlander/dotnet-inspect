using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Analysis;

/// <summary>Analysis observations and comparisons over the Finding substrate.</summary>
public static class AnalysisFindings
{
    public static readonly FindingDescriptor AllocationDescriptor =
        new("analysis.allocation", "Allocation occurrence");

    /// <summary>
    /// Projects one method's allocation occurrences into IL order. An empty occurrence sequence is
    /// a complete empty census; acquisition failures belong to the caller that builds the body index.
    /// </summary>
    public static FindingInspection<AllocationOccurrence> InspectAllocations(
        IEnumerable<AllocationOccurrence> occurrences,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(subject);

        var ordered = occurrences
            .OrderBy(static occurrence => occurrence.ILOffset)
            .ThenBy(GetAllocationIdentityKey, StringComparer.Ordinal)
            .ToImmutableArray();
        var findings = ImmutableArray.CreateBuilder<Finding<AllocationOccurrence>>(ordered.Length);
        for (int i = 0; i < ordered.Length; i++)
        {
            AllocationOccurrence occurrence = ordered[i];
            findings.Add(new Finding<AllocationOccurrence>(
                subject,
                AllocationDescriptor,
                new FindingKey(GetAllocationIdentityKey(occurrence)),
                occurrence,
                Ordinal: i,
                Detail: occurrence.Detail));
        }

        return new FindingInspection<AllocationOccurrence>.Complete(findings.MoveToImmutable());
    }

    /// <summary>
    /// Compares two allocation censuses conservatively. Hard correspondence requires the same
    /// allocation source, kind, and allocated type; version-local IL coordinates never establish
    /// cross-version identity.
    /// </summary>
    public static FindingComparison<AllocationOccurrence> CompareAllocations(
        IEnumerable<AllocationOccurrence> oldOccurrences,
        IEnumerable<AllocationOccurrence> newOccurrences,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldOccurrences);
        ArgumentNullException.ThrowIfNull(newOccurrences);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
                InspectAllocations(oldOccurrences, subject),
                InspectAllocations(newOccurrences, subject),
                acceptanceThreshold: acceptanceThreshold)
            .TransformPairs(ClassifyFacetChanges);
    }

    public static string GetAllocationIdentityKey(AllocationOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        string allocatedType = occurrence.AllocatedType?.ToQualifiedDisplayString()
            ?? occurrence.RuntimeAllocationType
            ?? occurrence.Detail
            ?? "?";
        return $"{(int)occurrence.Source}:{(int)occurrence.Kind}:{allocatedType}";
    }

    static ImmutableArray<PairFinding<AllocationOccurrence>> ClassifyFacetChanges(
        ImmutableArray<PairFinding<AllocationOccurrence>> pairs)
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<AllocationOccurrence>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<AllocationOccurrence>.Present present
                && !SameSemanticFacets(present.Old.Payload, present.New.Payload))
            {
                builder.Add(new PairFinding<AllocationOccurrence>.Changed(
                    present.Old,
                    present.New,
                    present.Difference,
                    DescribeFacetChanges(present.Old.Payload, present.New.Payload)));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static bool SameSemanticFacets(AllocationOccurrence oldOccurrence, AllocationOccurrence newOccurrence)
        => oldOccurrence with
        {
            Method = newOccurrence.Method,
            ILOffset = newOccurrence.ILOffset,
            OperandToken = newOccurrence.OperandToken,
        } == newOccurrence;

    static string DescribeFacetChanges(
        AllocationOccurrence oldOccurrence,
        AllocationOccurrence newOccurrence)
    {
        var changes = new List<string>();
        AddChange(changes, "detail", oldOccurrence.Detail, newOccurrence.Detail);
        AddChange(changes, "heap allocation", oldOccurrence.CountsAsHeapAllocation, newOccurrence.CountsAsHeapAllocation);
        AddChange(changes, "frequency", oldOccurrence.Frequency, newOccurrence.Frequency);
        AddChange(changes, "in loop", oldOccurrence.InLoop, newOccurrence.InLoop);
        AddChange(changes, "escape", oldOccurrence.Escape, newOccurrence.Escape);
        AddChange(changes, "runtime type", oldOccurrence.RuntimeAllocationType, newOccurrence.RuntimeAllocationType);
        AddChange(changes, "path", oldOccurrence.PathContext, newOccurrence.PathContext);
        AddChange(changes, "path confidence", oldOccurrence.PathConfidence, newOccurrence.PathConfidence);
        AddChange(changes, "estimated size", oldOccurrence.EstimatedSizeBytes, newOccurrence.EstimatedSizeBytes);
        AddChange(changes, "size tier", oldOccurrence.SizeTier, newOccurrence.SizeTier);
        AddChange(changes, "post-dominance", oldOccurrence.PostDominance, newOccurrence.PostDominance);
        AddChange(changes, "escape kind", oldOccurrence.EscapeKind, newOccurrence.EscapeKind);
        AddChange(changes, "multiplicity", oldOccurrence.Multiplicity, newOccurrence.Multiplicity);
        AddChange(changes, "churned type", oldOccurrence.ChurnedType, newOccurrence.ChurnedType);
        return string.Join("; ", changes);
    }

    static void AddChange<T>(List<string> changes, string name, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        changes.Add($"{name}: {Format(oldValue)} -> {Format(newValue)}");
    }

    static string Format<T>(T value) => value?.ToString() ?? "none";
}
