using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Analysis;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

[Flags]
public enum MemberMetricKind
{
    None = 0,
    BodySize = 1 << 0,
    SiblingRelationships = 1 << 1,
    All = BodySize | SiblingRelationships,
}

public sealed record MemberMetricsInspectionLimits
{
    public MemberMetricsInspectionLimits(
        long maximumAssemblyBytes,
        ImplementationMetricWorkLimits analysis)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        MaximumAssemblyBytes = maximumAssemblyBytes;
        Analysis = analysis
            ?? throw new ArgumentNullException(nameof(analysis));
    }

    public long MaximumAssemblyBytes { get; }

    public ImplementationMetricWorkLimits Analysis { get; }
}

public sealed record MemberMetricsInspectionRequest
{
    public MemberMetricsInspectionRequest(
        LibraryReference library,
        MemberGroupDocument document,
        MemberMetricKind authorizedMetrics,
        MemberMetricKind projectedMetrics,
        QuerySpaceRequest query,
        MemberMetricsInspectionLimits limits)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        Query = query
            ?? throw new ArgumentNullException(nameof(query));
        Limits = limits
            ?? throw new ArgumentNullException(nameof(limits));
        MemberMetricsQuery.ValidateMetrics(
            authorizedMetrics,
            nameof(authorizedMetrics));
        MemberMetricsQuery.ValidateMetrics(
            projectedMetrics,
            nameof(projectedMetrics));

        AuthorizedMetrics = authorizedMetrics;
        ProjectedMetrics = projectedMetrics;
    }

    public LibraryReference Library { get; }

    public MemberGroupDocument Document { get; }

    public MemberMetricKind AuthorizedMetrics { get; }

    public MemberMetricKind ProjectedMetrics { get; }

    public QuerySpaceRequest Query { get; }

    public MemberMetricsInspectionLimits Limits { get; }
}

public enum MemberMetricCellState
{
    Available,
    Bodyless,
    Incomplete,
    Unavailable,
    Failed,
}

public sealed record MemberPhysicalBodySize(
    MethodIdentity EvidenceMethod,
    int EncodedIlBytes);

public sealed record MemberBodySizeMetric(
    MemberMetricCellState State,
    int? LargestPhysicalIlBytes,
    ImmutableArray<MemberPhysicalBodySize> PhysicalBodies,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    public bool IsComplete =>
        State is MemberMetricCellState.Available
            or MemberMetricCellState.Bodyless;
}

public sealed record MemberSiblingRelationshipMetric(
    ImmutableArray<OverloadCallRelationship> Incoming,
    ImmutableArray<OverloadCallRelationship> Outgoing,
    bool IsComplete)
{
    public int IncomingSiblingCallers =>
        Incoming
            .Select(static relationship =>
                relationship.Caller.MetadataToken)
            .Distinct()
            .Count();

    public int OutgoingSiblingTargets =>
        Outgoing
            .Select(static relationship =>
                relationship.Callee.MetadataToken)
            .Distinct()
            .Count();
}

public sealed record MemberMetricsRow(
    MemberOverloadShape Member,
    MemberBodySizeMetric? BodySize,
    MemberSiblingRelationshipMetric? SiblingRelationships)
{
    public int? LargestPhysicalIlBytes =>
        BodySize?.LargestPhysicalIlBytes;

    public int? IncomingSiblingCallers =>
        SiblingRelationships?.IncomingSiblingCallers;

    public int? OutgoingSiblingTargets =>
        SiblingRelationships?.OutgoingSiblingTargets;
}

public sealed record MemberMetricsCoverageReceipt(
    int ExactMemberCount,
    int PhysicalBodyCount,
    int AvailableBodySizeCount,
    int BodylessCount,
    int IncompleteCount,
    int UnavailableCount,
    int FailedCount,
    bool RelationshipsComplete);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemberMetricsPopulationOutcome.Rows), "rows")]
[JsonDerivedType(typeof(MemberMetricsPopulationOutcome.Count), "count")]
[JsonDerivedType(
    typeof(MemberMetricsPopulationOutcome.Incomplete),
    "incomplete")]
public abstract record MemberMetricsPopulationOutcome
{
    private protected MemberMetricsPopulationOutcome()
    {
    }

    public sealed record Rows(ImmutableArray<MemberMetricsRow> Items)
        : MemberMetricsPopulationOutcome;

    public sealed record Count(int Value)
        : MemberMetricsPopulationOutcome;

    public sealed record Incomplete(RowWindowFailure Failure)
        : MemberMetricsPopulationOutcome;
}

public sealed record MemberMetricsInspectionContent(
    MemberGroupSubject Subject,
    MemberOverloadPopulationBinding PopulationBinding,
    MemberMetricKind AuthorizedMetrics,
    MemberMetricKind ProjectedMetrics,
    MemberMetricKind RequestedMetrics,
    MemberMetricsPopulationOutcome Population,
    MemberMetricsCoverageReceipt Coverage,
    ImplementationMetricParticipationReceipt Participation,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public enum MemberMetricsInspectionRejection
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
    QueryIntentRejected,
    UnauthorizedMetric,
    NoEffectiveMetric,
    LeaseReferenceMismatch,
    MissingImplementationAssembly,
    SeparateApiAndImplementationAssemblies,
    PopulationRowsRequired,
    PopulationRowsIncomplete,
    PopulationMismatch,
    DuplicateMemberIdentity,
    AssemblyIdentityMismatch,
    StalePopulationBinding,
}

public enum MemberMetricsInspectionBound
{
    AssemblyBytes,
}

public enum MemberMetricsInspectionFailure
{
    ContentAccess,
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    Analysis,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(MemberMetricsInspectionOutcome.Failed),
    "failed")]
public abstract record MemberMetricsInspectionOutcome
{
    private protected MemberMetricsInspectionOutcome()
    {
    }

    public sealed record Available(MemberMetricsInspectionContent Content)
        : MemberMetricsInspectionOutcome;

    public sealed record Rejected(
        MemberMetricsInspectionRejection Reason,
        RowQueryFailure? RowQueryFailure = null)
        : MemberMetricsInspectionOutcome;

    public sealed record Incomplete(
        MemberMetricsInspectionBound Bound,
        long Limit,
        long Measured)
        : MemberMetricsInspectionOutcome;

    public sealed record Failed(
        MemberMetricsInspectionFailure Reason,
        string Message)
        : MemberMetricsInspectionOutcome;
}
