using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Content-shaped inputs for comparing Analysis body signals across two
/// already-acquired assembly versions.
/// </summary>
public sealed record BodySignalComparisonInput(
    IReadOnlyList<BodySignalAnalysisInput> OldAnalyses,
    IReadOnlyList<BodySignalAnalysisInput> NewAnalyses,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

/// <summary>
/// Compares two sets of focused Analysis results while retaining the
/// Research-owned evidence and Finding correspondence.
/// </summary>
public static class BodySignalComparisonQuery
{
    public static InspectionQuery<ResearchComparison> Definition { get; } =
        new("Body signal comparison", InspectionCost.Unbounded);

    public static ResearchComparison Execute(BodySignalComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.OldAnalyses);
        ArgumentNullException.ThrowIfNull(input.NewAnalyses);

        return ResearchDiff.Compare(
            new ResearchDiffInput([])
            {
                BodySignalAnalyses = input.OldAnalyses,
            },
            new ResearchDiffInput([])
            {
                BodySignalAnalyses = input.NewAnalyses,
            },
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                TypeFilters: input.TypeFilters,
                MemberTargetIdentities: input.MemberTargetIdentities));
    }
}
