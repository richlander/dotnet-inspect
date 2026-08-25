using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Findings;

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
        LibraryBodyIndex index,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(subject);

        FindingInspection<ResourceLifecycleOccurrence> inspection =
            ResourceLifecycleAnalysis.InspectAssembly(
                () => index,
                subject);
        return inspection.Value switch
        {
            FindingInspection<ResourceLifecycleOccurrence>.Complete complete =>
                new ResourceTriageResult.Available(
                    complete,
                    ResourceTriageAnalysis.Assess(complete)),
            FindingInspection<ResourceLifecycleOccurrence>.Failed failed =>
                new ResourceTriageResult.Failed(failed.Error),
            _ => throw new InvalidOperationException(
                $"Unknown resource lifecycle inspection '{inspection.Value.GetType().Name}'."),
        };
    }
}
