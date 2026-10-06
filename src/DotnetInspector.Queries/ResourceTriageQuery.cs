using System.Collections.Immutable;
using ILInspector.Analysis;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>Typed result of assessing whole-assembly resource lifecycle evidence.</summary>
public abstract record ResourceTriageResult
{
    private ResourceTriageResult()
    {
    }

    /// <summary>The complete lifecycle census and its typed triage assessments.</summary>
    public sealed record Available(
        FindingInspection<ResourceLifecycleOccurrence>.Complete Inspection,
        ImmutableArray<ResourceTriageAssessment> Assessments)
        : ResourceTriageResult;

    /// <summary>Sound candidates are available, but the whole-assembly census is incomplete.</summary>
    public sealed record Incomplete : ResourceTriageResult
    {
        public Incomplete(
            FindingInspection<ResourceLifecycleOccurrence>.Complete inspection,
            ImmutableArray<ResourceTriageAssessment> assessments,
            ImmutableArray<ResourceLifecycleLimitation> limitations)
        {
            ArgumentNullException.ThrowIfNull(inspection);
            if (assessments.IsDefault)
                throw new ArgumentException(
                    "Assessments must be initialized.",
                    nameof(assessments));
            if (limitations.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "Incomplete Resource Triage requires a limitation.",
                    nameof(limitations));
            }

            Inspection = inspection;
            Assessments = assessments;
            Limitations = limitations;
        }

        public FindingInspection<ResourceLifecycleOccurrence>.Complete
            Inspection { get; }
        public ImmutableArray<ResourceTriageAssessment> Assessments { get; }
        public ImmutableArray<ResourceLifecycleLimitation> Limitations { get; }
    }

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : ResourceTriageResult;

    /// <summary>The query failed or could not complete its whole-assembly census.</summary>
    public sealed record Failed(InspectionError Error) : ResourceTriageResult;
}

/// <summary>Assesses resource lifecycle evidence from an already-acquired body index.</summary>
public static class ResourceTriageQuery
{
    public static InspectionQuery<ResourceTriageResult> Definition { get; } =
        new("Resource triage", InspectionCost.Unbounded);

    public static ResourceTriageResult Execute(
        LibraryResourceLifecycleAnalysisResult lifecycle,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(subject);

        ResourceLifecycleFindingInspection inspection =
            ResourceLifecycleAnalysis.Inspect(lifecycle, subject);
        return inspection switch
        {
            ResourceLifecycleFindingInspection.Complete complete =>
                new ResourceTriageResult.Available(
                    complete.Inspection,
                    ResourceTriageAnalysis.Assess(complete.Inspection)),
            ResourceLifecycleFindingInspection.Incomplete incomplete =>
                new ResourceTriageResult.Incomplete(
                    incomplete.Inspection,
                    ResourceTriageAnalysis.Assess(incomplete.Inspection),
                    incomplete.Limitations),
            ResourceLifecycleFindingInspection.Failed failed =>
                new ResourceTriageResult.Failed(failed.Error),
            _ => throw new InvalidOperationException(
                $"Unknown resource lifecycle inspection '{inspection.GetType().Name}'."),
        };
    }
}
