using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Content-shaped inputs for comparing Analysis body signals across two
/// already-acquired assembly versions.
/// </summary>
public sealed record BodySignalComparisonInput(
    IReadOnlyList<LibraryBodyIndex> OldIndexes,
    IReadOnlyList<LibraryBodyIndex> NewIndexes,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

/// <summary>
/// Compares two sets of Analysis body indexes while retaining the
/// Research-owned evidence and Finding correspondence.
/// </summary>
public static class BodySignalComparisonQuery
{
    public static InspectionQuery<ResearchComparison> Definition { get; } =
        new("Body signal comparison", InspectionCost.Unbounded);

    public static ResearchComparison Execute(BodySignalComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.OldIndexes);
        ArgumentNullException.ThrowIfNull(input.NewIndexes);

        return ResearchDiff.Compare(
            new ResearchDiffInput([], BodyIndexes: input.OldIndexes),
            new ResearchDiffInput([], BodyIndexes: input.NewIndexes),
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                TypeFilters: input.TypeFilters,
                MemberTargetIdentities: input.MemberTargetIdentities));
    }
}
