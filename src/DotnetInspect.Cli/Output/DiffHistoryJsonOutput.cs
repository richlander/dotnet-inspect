using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspect.Cli.Output;

public static class DiffHistoryJsonOutput
{
    public static void Write(
        Utf8JsonWriter writer,
        DiffHistoryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(outcome);
        JsonSerializer.Serialize(
            writer,
            Project(outcome),
            DiffHistoryJsonContext.Default.DiffHistoryJsonOutcome);
    }

    internal static DiffHistoryJsonOutcome Project(
        DiffHistoryOutcome outcome) =>
        outcome switch
        {
            DiffHistorySectionAvailable available =>
                new DiffHistoryJsonAvailable(
                    Project(available.Document),
                    Project(available.Count)),
            DiffHistoryOutcome.Available available =>
                new DiffHistoryJsonAvailable(
                    Project(available.Document),
                    Count: null),
            DiffHistoryOutcome.ExactApiMemberUnavailable unavailable =>
                new DiffHistoryJsonExactApiMemberUnavailable(
                    Project(unavailable.Selection)),
            _ => throw new JsonException("Unknown Diff History outcome."),
        };

    static DiffHistoryJsonDocument Project(DiffHistoryDocument document) =>
        document switch
        {
            DiffHistoryDocument.ApiMembers apiMembers =>
                new DiffHistoryJsonApiMembers(
                    Project(apiMembers.Content)),
            DiffHistoryDocument.ApiTypes apiTypes =>
                new DiffHistoryJsonApiTypes(
                    Project(apiTypes.Content)),
            DiffHistoryDocument.ApiAttributes apiAttributes =>
                new DiffHistoryJsonApiAttributes(
                    Project(apiAttributes.Content)),
            DiffHistoryDocument.ExactApiMember exact =>
                new DiffHistoryJsonExactApiMember(
                    new(
                        Project(exact.Content.Selection),
                        Project(exact.Content.History),
                        Project(exact.Content.Identity))),
            DiffHistoryDocument.Allocations allocations =>
                new DiffHistoryJsonAllocations(
                    Project(allocations.Content)),
            DiffHistoryDocument.CallSites callSites =>
                new DiffHistoryJsonCallSites(
                    Project(callSites.Content)),
            DiffHistoryDocument.Unsafety unsafety =>
                new DiffHistoryJsonUnsafety(
                    Project(unsafety.Content)),
            _ => throw new JsonException("Unknown Diff History document."),
        };

    static DiffHistoryJsonApiDocument<ApiMemberHandle> Project(
        DiffHistoryApiMemberDocument document) =>
        new(
            document.Population,
            document.TypeFullName,
            document.Scope,
            document.TargetContext,
            document.EvaluationLimits,
            document.EvaluationPlan,
            document.AuthorizedProbeCount,
            document.UsedProbeCount,
            document.WorkspaceLimits,
            document.ProjectionLimits,
            document.EvaluationSelection,
            [.. document.Evaluations.Select(Project)],
            [.. document.Probes.Select(probe => new DiffHistoryJsonApiProbe<ApiMemberHandle>(
                probe.Step,
                probe.Purpose,
                probe.SelectedInterval,
                Project(probe.Evaluation),
                probe.Learning))],
            Project(document.Correlation),
            [.. document.Transitions.Select(Project)],
            [.. document.ChangedVersionAssessments.Select(Project)],
            document.TerminalOutcome,
            document.NextActions,
            document.ComparisonOptions,
            document.MatchAcceptanceThreshold,
            document.ReplayContext,
            document.UnevaluatedAddresses,
            [.. document.ChangedVersions.Select(Project)],
            document.ChangedVersionAddresses);

    static DiffHistoryJsonApiDocument<T> Project<T>(
        DiffHistoryApiFindingDocument<T> document)
        where T : notnull =>
        new(
            document.Population,
            document.TypeFullName,
            document.Scope,
            document.TargetContext,
            document.EvaluationLimits,
            document.EvaluationPlan,
            document.AuthorizedProbeCount,
            document.UsedProbeCount,
            document.WorkspaceLimits,
            document.ProjectionLimits,
            document.EvaluationSelection,
            [.. document.Evaluations.Select(Project)],
            [.. document.Probes.Select(probe => new DiffHistoryJsonApiProbe<T>(
                probe.Step,
                probe.Purpose,
                probe.SelectedInterval,
                Project(probe.Evaluation),
                probe.Learning))],
            Project(document.Correlation),
            [.. document.Transitions.Select(Project)],
            [.. document.ChangedVersionAssessments.Select(Project)],
            document.TerminalOutcome,
            document.NextActions,
            document.ComparisonOptions,
            document.MatchAcceptanceThreshold,
            document.ReplayContext,
            document.UnevaluatedAddresses,
            [.. document.ChangedVersions.Select(Project)],
            document.ChangedVersionAddresses);

    static DiffHistoryJsonApiEvaluation<ApiMemberHandle> Project(
        DiffHistoryApiMemberEvaluation evaluation) =>
        new(
            evaluation.Address,
            evaluation.Version,
            Project(evaluation.Inspection),
            evaluation.Execution,
            evaluation.Cleanup,
            evaluation.NoContributionReason,
            evaluation.WorkspaceFailure,
            evaluation.SubjectResolution,
            evaluation.ProjectionTruncation,
            evaluation.Participants);

    static DiffHistoryJsonApiEvaluation<T> Project<T>(
        DiffHistoryApiFindingEvaluation<T> evaluation)
        where T : notnull =>
        new(
            evaluation.Address,
            evaluation.Version,
            Project(evaluation.Inspection),
            evaluation.Execution,
            evaluation.Cleanup,
            evaluation.NoContributionReason,
            evaluation.WorkspaceFailure,
            evaluation.SubjectResolution,
            evaluation.ProjectionTruncation,
            evaluation.Participants);

    static DiffHistoryJsonAnalysisDocument<T> Project<T>(
        DiffHistoryAnalysisDocument<T> document)
        where T : notnull =>
        new(
            document.Finding,
            document.Population,
            document.Selector,
            document.FindingSubject,
            document.TargetContext,
            document.EvaluationLimits,
            document.EvaluationPlan,
            document.AuthorizedProbeCount,
            document.UsedProbeCount,
            document.WorkspaceLimits,
            document.EvaluationSelection,
            [.. document.Evaluations.Select(Project)],
            [.. document.Probes.Select(probe => new DiffHistoryJsonAnalysisProbe<T>(
                probe.Step,
                probe.Purpose,
                probe.SelectedInterval,
                Project(probe.Evaluation),
                probe.Learning))],
            document.SourceReceipt,
            Project(document.Correlation),
            [.. document.Transitions.Select(Project)],
            [.. document.ChangedVersionAssessments.Select(Project)],
            document.TerminalOutcome,
            document.NextActions,
            document.MatchAcceptanceThreshold,
            document.ReplayContext,
            document.UnevaluatedAddresses,
            [.. document.ChangedVersions.Select(Project)],
            document.ChangedVersionAddresses);

    static DiffHistoryJsonAnalysisEvaluation<T> Project<T>(
        DiffHistoryAnalysisEvaluation<T> evaluation)
        where T : notnull =>
        new(
            evaluation.Address,
            evaluation.Version,
            evaluation.State,
            Project(evaluation.Inspection),
            evaluation.Executions,
            evaluation.Cleanup,
            evaluation.NoContributions,
            evaluation.WorkspaceFailure,
            evaluation.SourceReceipt,
            evaluation.SourceSelection,
            evaluation.SourceValidation,
            evaluation.SourceBinding,
            evaluation.Relationship,
            evaluation.BodyResolution,
            evaluation.AnalysisRootFailure);

    static DiffHistoryJsonInspection<T> Project<T>(
        FindingInspection<T> inspection)
        where T : notnull =>
        inspection.Value switch
        {
            FindingInspection<T>.Complete complete =>
                new("complete", complete.Findings, null, null, null),
            FindingInspection<T>.Absent absent =>
                new(
                    "absent",
                    ImmutableArray<Finding<T>>.Empty,
                    absent.Kind,
                    absent.Detail,
                    null),
            FindingInspection<T>.Failed failed =>
                new(
                    "failed",
                    ImmutableArray<Finding<T>>.Empty,
                    null,
                    null,
                    failed.Error),
            _ => throw new JsonException("Unknown Finding inspection."),
        };

    static DiffHistoryJsonComparison<T>? Project<T>(
        FindingComparison<T>? comparison)
        where T : notnull =>
        comparison?.Value switch
        {
            null => null,
            FindingComparison<T>.Complete complete =>
                new(
                    "complete",
                    [.. complete.Pairs.Select(Project)],
                    complete.Match,
                    Project(complete.OldInspection),
                    Project(complete.NewInspection),
                    complete.Transition,
                    complete.IsExact,
                    null),
            FindingComparison<T>.Failed failed =>
                new(
                    "failed",
                    ImmutableArray<DiffHistoryJsonPair<T>>.Empty,
                    null,
                    Project(failed.OldInspection),
                    Project(failed.NewInspection),
                    null,
                    false,
                    failed.Failure),
            _ => throw new JsonException("Unknown Finding comparison."),
        };

    static DiffHistoryJsonPair<T> Project<T>(PairFinding<T> pair)
        where T : notnull
    {
        IPairFinding value = pair;
        return new(
            pair.Kind,
            pair.Difference,
            pair.Subject,
            pair.Descriptor,
            pair.Detail,
            value.Old as Finding<T>,
            value.New as Finding<T>,
            pair.Value is IMatchedPairFinding matched
                ? matched.Match
                : null);
    }

    static DiffHistoryJsonCorrelation<T> Project<T>(
        FindingCensusCorrelation<T> correlation)
        where T : notnull =>
        new(
        [
            .. correlation.Inspections.Select(inspection =>
                new DiffHistoryJsonVersionedInspection<T>(
                    inspection.Version,
                    Project(inspection.Inspection))),
        ]);

    static DiffHistoryJsonExactCorrelation<T> Project<T>(
        FindingCorrelation<T> correlation)
        where T : notnull =>
        new(
            correlation.Key,
            [.. correlation.Inspections.Select(inspection =>
                new DiffHistoryJsonVersionedInspection<T>(
                    inspection.Version,
                    Project(inspection.Inspection)))],
            [.. correlation.Timeline.Select(Project)],
            correlation.Finding);

    static DiffHistoryJsonCorrelationPoint<T> Project<T>(
        FindingCorrelationPoint<T> point)
        where T : notnull =>
        point.Value switch
        {
            FindingCorrelationPoint<T>.Present present =>
                new(
                    "present",
                    present.Version,
                    present.Finding,
                    null,
                    null),
            FindingCorrelationPoint<T>.Missing missing =>
                new("missing", missing.Version, null, null, null),
            FindingCorrelationPoint<T>.SubjectAbsent absent =>
                new(
                    "subjectAbsent",
                    absent.Version,
                    null,
                    absent.Detail,
                    null),
            FindingCorrelationPoint<T>.NoApplicableInput inapplicable =>
                new(
                    "noApplicableInput",
                    inapplicable.Version,
                    null,
                    inapplicable.Detail,
                    null),
            FindingCorrelationPoint<T>.Failed failed =>
                new(
                    "failed",
                    failed.Version,
                    null,
                    null,
                    failed.Error),
            _ => throw new JsonException("Unknown Finding correlation point."),
        };

    static DiffHistoryJsonTransition<T> Project<T>(
        DiffHistoryTransition<T> transition)
        where T : notnull =>
        new(
            transition.Source,
            transition.Destination,
            transition.UnevaluatedBetween,
            transition.IsPopulationAdjacent,
            Project(transition.Comparison)!);

    static DiffHistoryJsonChangedVersionAssessment<T> Project<T>(
        DiffHistoryChangedVersionAssessment<T> assessment)
        where T : notnull =>
        new(
            assessment.Predecessor,
            assessment.Destination,
            assessment.State,
            Project(assessment.Comparison),
            assessment.PredecessorEvaluated,
            assessment.DestinationEvaluated);

    static DiffHistoryJsonExactSelection Project(
        DiffHistoryExactApiMemberSelection selection) =>
        new(
            selection.Selector,
            selection.State,
            Project(selection.SourceEvaluation),
            selection.Member,
            selection.CorrelationKey,
            selection.Diagnostic);

    static DiffHistoryJsonCountOutcome? Project(
        SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>? count) =>
        count switch
        {
            null => null,
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed completed =>
                new("completed", completed.Counts, null, null, null, null, null),
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount source =>
                new("sourceForCount", null, source.Sources, null, null, null, null),
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Semantic semantic =>
                new(
                    "semantic",
                    null,
                    null,
                    semantic.Identity,
                    semantic.StageNumber,
                    semantic.RequiredPosition,
                    semantic.AvailableCount),
            _ => throw new JsonException("Unknown Diff History Count outcome."),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(typeof(DiffHistoryJsonAvailable), "available")]
[JsonDerivedType(
    typeof(DiffHistoryJsonExactApiMemberUnavailable),
    "exactApiMemberUnavailable")]
public abstract record DiffHistoryJsonOutcome;

public sealed record DiffHistoryJsonAvailable(
    DiffHistoryJsonDocument Document,
    DiffHistoryJsonCountOutcome? Count) : DiffHistoryJsonOutcome;

public sealed record DiffHistoryJsonExactApiMemberUnavailable(
    DiffHistoryJsonExactSelection Selection) : DiffHistoryJsonOutcome;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "document")]
[JsonDerivedType(typeof(DiffHistoryJsonApiMembers), "apiMembers")]
[JsonDerivedType(typeof(DiffHistoryJsonApiTypes), "apiTypes")]
[JsonDerivedType(typeof(DiffHistoryJsonApiAttributes), "apiAttributes")]
[JsonDerivedType(typeof(DiffHistoryJsonExactApiMember), "exactApiMember")]
[JsonDerivedType(typeof(DiffHistoryJsonAllocations), "allocations")]
[JsonDerivedType(typeof(DiffHistoryJsonCallSites), "callSites")]
[JsonDerivedType(typeof(DiffHistoryJsonUnsafety), "unsafety")]
public abstract record DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonApiMembers(
    DiffHistoryJsonApiDocument<ApiMemberHandle> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonApiTypes(
    DiffHistoryJsonApiDocument<ApiTypeHandle> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonApiAttributes(
    DiffHistoryJsonApiDocument<ApiAttributeHandle> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonExactApiMember(
    DiffHistoryJsonExactApiMemberDocument Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonAllocations(
    DiffHistoryJsonAnalysisDocument<AllocationOccurrence> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonCallSites(
    DiffHistoryJsonAnalysisDocument<DirectCall> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonUnsafety(
    DiffHistoryJsonAnalysisDocument<UnsafetyOccurrence> Content) :
    DiffHistoryJsonDocument;

public sealed record DiffHistoryJsonApiDocument<T>(
    PackageVersionVector Population,
    string TypeFullName,
    ApiSurfaceScope Scope,
    PackageHouseTargetContext TargetContext,
    DiffHistoryEvaluationLimits EvaluationLimits,
    DiffHistoryEvaluationPlan EvaluationPlan,
    int? AuthorizedProbeCount,
    int UsedProbeCount,
    PackageVersionCellWorkspaceLimits WorkspaceLimits,
    ApiSurfaceProjectionLimits ProjectionLimits,
    ImmutableArray<PackageVersionAddress> EvaluationSelection,
    ImmutableArray<DiffHistoryJsonApiEvaluation<T>> Evaluations,
    ImmutableArray<DiffHistoryJsonApiProbe<T>> Probes,
    DiffHistoryJsonCorrelation<T> Correlation,
    ImmutableArray<DiffHistoryJsonTransition<T>> Transitions,
    ImmutableArray<DiffHistoryJsonChangedVersionAssessment<T>>
        ChangedVersionAssessments,
    DiffHistoryTerminalOutcome TerminalOutcome,
    ImmutableArray<DiffHistoryNextAction> NextActions,
    ApiDiffOptions ComparisonOptions,
    int MatchAcceptanceThreshold,
    DiffHistoryPackageReplayContext? ReplayContext,
    ImmutableArray<PackageVersionAddress> UnevaluatedAddresses,
    ImmutableArray<DiffHistoryJsonChangedVersionAssessment<T>>
        ChangedVersions,
    ImmutableArray<PackageVersionAddress> ChangedVersionAddresses)
    where T : notnull;

public sealed record DiffHistoryJsonApiEvaluation<T>(
    PackageVersionAddress Address,
    FindingVersion Version,
    DiffHistoryJsonInspection<T> Inspection,
    PackageVersionCellExecutionEvidence Execution,
    PackageVersionCellWorkspaceCleanupEvidence? Cleanup,
    PackageHouseRootNoContributionReason? NoContributionReason,
    PackageVersionCellMetadataWorkspaceFailure? WorkspaceFailure,
    DiffHistoryApiMemberSubjectResolution SubjectResolution,
    ApiSurfaceProjectionTruncation? ProjectionTruncation,
    ImmutableArray<DiffHistoryApiParticipantEvidence> Participants)
    where T : notnull;

public sealed record DiffHistoryJsonApiProbe<T>(
    int Step,
    DiffHistoryProbePurpose Purpose,
    DiffHistoryInterval? SelectedInterval,
    DiffHistoryJsonApiEvaluation<T> Evaluation,
    DiffHistoryProbeLearning Learning)
    where T : notnull;

public sealed record DiffHistoryJsonAnalysisDocument<T>(
    PackageVersionCellAnalysisProducerKind Finding,
    PackageVersionVector Population,
    PackageVersionCellMemberSelector Selector,
    FindingSubject FindingSubject,
    PackageHouseTargetContext TargetContext,
    DiffHistoryEvaluationLimits EvaluationLimits,
    DiffHistoryEvaluationPlan EvaluationPlan,
    int? AuthorizedProbeCount,
    int UsedProbeCount,
    PackageVersionCellWorkspaceLimits WorkspaceLimits,
    ImmutableArray<PackageVersionAddress> EvaluationSelection,
    ImmutableArray<DiffHistoryJsonAnalysisEvaluation<T>> Evaluations,
    ImmutableArray<DiffHistoryJsonAnalysisProbe<T>> Probes,
    DiffHistoryMemberSourceReceipt? SourceReceipt,
    DiffHistoryJsonCorrelation<T> Correlation,
    ImmutableArray<DiffHistoryJsonTransition<T>> Transitions,
    ImmutableArray<DiffHistoryJsonChangedVersionAssessment<T>>
        ChangedVersionAssessments,
    DiffHistoryTerminalOutcome TerminalOutcome,
    ImmutableArray<DiffHistoryNextAction> NextActions,
    int MatchAcceptanceThreshold,
    DiffHistoryPackageReplayContext? ReplayContext,
    ImmutableArray<PackageVersionAddress> UnevaluatedAddresses,
    ImmutableArray<DiffHistoryJsonChangedVersionAssessment<T>>
        ChangedVersions,
    ImmutableArray<PackageVersionAddress> ChangedVersionAddresses)
    where T : notnull;

public sealed record DiffHistoryJsonAnalysisEvaluation<T>(
    PackageVersionAddress Address,
    FindingVersion Version,
    DiffHistoryAnalysisEvaluationState State,
    DiffHistoryJsonInspection<T> Inspection,
    ImmutableArray<PackageVersionCellExecutionEvidence> Executions,
    PackageVersionCellWorkspaceCleanupEvidence? Cleanup,
    ImmutableArray<PackageVersionCellNoContribution> NoContributions,
    PackageVersionCellAnalysisWorkspaceFailure? WorkspaceFailure,
    DiffHistoryMemberSourceReceipt? SourceReceipt,
    ApiCoordinateSourceSelectionEvidence? SourceSelection,
    ApiCoordinateCorrespondenceEvidence? SourceValidation,
    PackageVersionCellSourceBindingEvidence? SourceBinding,
    ApiCoordinateCorrespondenceEvidence? Relationship,
    MatchedApiMemberBodyResolutionEvidence? BodyResolution,
    ArtifactRootFailure? AnalysisRootFailure)
    where T : notnull;

public sealed record DiffHistoryJsonAnalysisProbe<T>(
    int Step,
    DiffHistoryProbePurpose Purpose,
    DiffHistoryInterval? SelectedInterval,
    DiffHistoryJsonAnalysisEvaluation<T> Evaluation,
    DiffHistoryProbeLearning Learning)
    where T : notnull;

public sealed record DiffHistoryJsonInspection<T>(
    string Outcome,
    ImmutableArray<Finding<T>> Findings,
    FindingInspectionAbsenceKind? Kind,
    string? Detail,
    InspectionError? Error)
    where T : notnull;

public sealed record DiffHistoryJsonComparison<T>(
    string Outcome,
    ImmutableArray<DiffHistoryJsonPair<T>> Pairs,
    FindingMatch? Match,
    DiffHistoryJsonInspection<T> OldInspection,
    DiffHistoryJsonInspection<T> NewInspection,
    FindingInspectionTransition? Transition,
    bool IsExact,
    string? Failure)
    where T : notnull;

public sealed record DiffHistoryJsonPair<T>(
    PairKind Kind,
    FindingDifferenceKind Difference,
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    string? Detail,
    Finding<T>? Old,
    Finding<T>? New,
    FindingMatchProvenance? Match)
    where T : notnull;

public sealed record DiffHistoryJsonCorrelation<T>(
    ImmutableArray<DiffHistoryJsonVersionedInspection<T>> Inspections)
    where T : notnull;

public sealed record DiffHistoryJsonVersionedInspection<T>(
    FindingVersion Version,
    DiffHistoryJsonInspection<T> Inspection)
    where T : notnull;

public sealed record DiffHistoryJsonTransition<T>(
    PackageVersionAddress Source,
    PackageVersionAddress Destination,
    ImmutableArray<PackageVersionAddress> UnevaluatedBetween,
    bool IsPopulationAdjacent,
    DiffHistoryJsonComparison<T> Comparison)
    where T : notnull;

public sealed record DiffHistoryJsonChangedVersionAssessment<T>(
    PackageVersionAddress Predecessor,
    PackageVersionAddress Destination,
    DiffHistoryChangedVersionState State,
    DiffHistoryJsonComparison<T>? Comparison,
    bool PredecessorEvaluated,
    bool DestinationEvaluated)
    where T : notnull;

public sealed record DiffHistoryJsonExactSelection(
    MemberTargetSelector Selector,
    DiffHistoryExactApiMemberSelectionState State,
    DiffHistoryJsonApiEvaluation<ApiMemberHandle> SourceEvaluation,
    ApiMemberHandle? Member,
    FindingCorrelationKey? CorrelationKey,
    MemberTargetDiagnostic? Diagnostic);

public sealed record DiffHistoryJsonExactApiMemberDocument(
    DiffHistoryJsonExactSelection Selection,
    DiffHistoryJsonApiDocument<ApiMemberHandle> History,
    DiffHistoryJsonExactCorrelation<ApiMemberHandle> Identity);

public sealed record DiffHistoryJsonExactCorrelation<T>(
    FindingCorrelationKey Key,
    ImmutableArray<DiffHistoryJsonVersionedInspection<T>> Inspections,
    ImmutableArray<DiffHistoryJsonCorrelationPoint<T>> Timeline,
    CorrelatedFinding<T> Finding)
    where T : notnull;

public sealed record DiffHistoryJsonCorrelationPoint<T>(
    string Outcome,
    FindingVersion Version,
    Finding<T>? Finding,
    string? Detail,
    InspectionError? Error)
    where T : notnull;

public sealed record DiffHistoryJsonCountOutcome(
    string Outcome,
    IReadOnlyList<SectionCountEntry<DiffHistoryCountCohort>>? Counts,
    IReadOnlyList<
        SectionCountSourceEvidence<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>>? Sources,
    DiffHistoryCountCohort? Identity,
    int? StageNumber,
    int? RequiredPosition,
    int? AvailableCount);
