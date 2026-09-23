using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// The completed outcome of one pairwise Library Direct-Use Cluster inspection.
/// </summary>
public abstract record AssemblyPairDirectUseClusterInspectionOutcome
{
    private AssemblyPairDirectUseClusterInspectionOutcome()
    {
    }

    public sealed record Available(
        AssemblyPairDirectUseClusterProjection Projection)
        : AssemblyPairDirectUseClusterInspectionOutcome;

    public sealed record Rejected(
        AssemblyPairCallUseInspectionOutcome.Rejected Pair)
        : AssemblyPairDirectUseClusterInspectionOutcome;
}

/// <summary>
/// Derives deterministic Direct-Use Clusters from one completed pairwise
/// Library call-use inspection.
/// </summary>
public static class AssemblyPairDirectUseClusterInspection
{
    public static InspectionEnvelope<
        AssemblyPairDirectUseClusterInspectionOutcome> Execute(
            InspectionEnvelope<AssemblyPairCallUseInspectionOutcome>
                pairInspection)
    {
        ArgumentNullException.ThrowIfNull(pairInspection);

        AssemblyPairDirectUseClusterInspectionOutcome content =
            pairInspection.Content switch
            {
                AssemblyPairCallUseInspectionOutcome.Available available =>
                    new AssemblyPairDirectUseClusterInspectionOutcome
                        .Available(
                            AssemblyPairDirectUseClusterProjection.Create(
                                available.Projection.Pair)),
                AssemblyPairCallUseInspectionOutcome.Rejected rejected =>
                    new AssemblyPairDirectUseClusterInspectionOutcome
                        .Rejected(rejected),
                _ => throw new InvalidOperationException(
                    "Unknown pairwise Library call-use inspection outcome."),
            };
        return new(
            content,
            new InspectionShare.NonProjectable(
                "assembly-pair/direct-use-clusters",
                "Pairwise Library Direct-Use Clusters do not yet have "
                    + "a canonical Workspace Share projection."),
            pairInspection.Diagnostics);
    }
}
