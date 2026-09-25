using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

public enum PackageVersionCellAnalysisProducerKind
{
    Allocation,
    CallSite,
    Unsafety,
}

public enum PackageVersionCellAnalysisEndpointRole
{
    Source,
    Destination,
}

/// <summary>One exact version-population cell prepared for Analysis.</summary>
public sealed class PackageVersionCellAnalysisEndpoint
{
    public PackageVersionCellAnalysisEndpoint(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(targetContext);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Version-cell Analysis requires a Realize operation.",
                nameof(operation));
        }

        Cell = cell;
        // IL-body findings read the implementation, so a ranged read fetches
        // the surface and implementation folders
        // (docs/design/package-read-demand.md#per-command-demand).
        HouseExecution = cell.PrepareExecution(
            operation,
            targetContext,
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.PackageOnly,
            PackageAssetDemand.SurfaceAndImplementation);
    }

    public PackageHouseVersionPopulationCell Cell { get; }

    public PackageHouseVersionPopulationCellExecution HouseExecution { get; }
}

/// <summary>One exact Member selector resolved only in the baseline cell.</summary>
public sealed class PackageVersionCellMemberSelector
{
    public PackageVersionCellMemberSelector(
        string type,
        string member,
        string? library = null,
        bool includeAll = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        if (library is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(library);

        Type = type;
        Member = member;
        Library = library;
        IncludeAll = includeAll;
    }

    public string Type { get; }
    public string Member { get; }
    public string? Library { get; }
    public bool IncludeAll { get; }
}

/// <summary>
/// Detached Diff History source identity and exact source-cell association.
/// </summary>
public sealed class DiffHistoryMemberSourceReceipt
{
    readonly PackageHouseRequestAssociation _cellAssociation;

    internal DiffHistoryMemberSourceReceipt(
        PackageHouseVersionPopulationCell cell,
        ApiCoordinateDeclarationEvidence declaration,
        FindingSubject findingSubject)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(findingSubject);
        if (declaration.Member is null
            || declaration.Kind == ApiDeclarationKind.Type)
        {
            throw new ArgumentException(
                "A Diff History Analysis receipt requires one exact Member declaration.",
                nameof(declaration));
        }
        if (declaration.Library.Asset is not { } asset)
        {
            throw new ArgumentException(
                "A Diff History Analysis receipt requires one exact package compile asset.",
                nameof(declaration));
        }
        if (!declaration.Library.Package.PackageId.Equals(
                cell.Population.Request.Range.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !declaration.Library.Package.PackageVersion.Equals(
                cell.NormalizedVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A Diff History Analysis receipt must describe its exact source population cell.",
                nameof(declaration));
        }

        _cellAssociation = cell.Association;
        Package = declaration.Library.Package.Coordinate;
        Asset = asset;
        Assembly = declaration.Library.Assembly.Assembly;
        DeclaringType = declaration.DeclaringType;
        Member = declaration.Member;
        DeclarationKind = declaration.Kind;
        FindingSubject = findingSubject;
    }

    public RealizedMemberCoordinate.Package Package { get; }
    public PackageCompileAsset Asset { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MemberAnchor Member { get; }
    public ApiDeclarationKind DeclarationKind { get; }
    public FindingSubject FindingSubject { get; }

    internal bool IsFor(PackageHouseVersionPopulationCell cell) =>
        ReferenceEquals(_cellAssociation, cell.Association);
}

public sealed class PackageVersionCellBaselineAnalysisRequest
{
    public PackageVersionCellBaselineAnalysisRequest(
        PackageVersionCellAnalysisEndpoint source,
        PackageVersionCellMemberSelector selector,
        FindingSubject findingSubject,
        PackageVersionCellWorkspaceLimits limits,
        DateTimeOffset workspaceDeadline)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(findingSubject);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateDeadline(workspaceDeadline);

        Source = source;
        Selector = selector;
        FindingSubject = findingSubject;
        Limits = limits;
        WorkspaceDeadline = workspaceDeadline;
    }

    public PackageVersionCellAnalysisEndpoint Source { get; }
    public PackageVersionCellMemberSelector Selector { get; }
    public FindingSubject FindingSubject { get; }
    public PackageVersionCellWorkspaceLimits Limits { get; }
    public DateTimeOffset WorkspaceDeadline { get; }

    internal static void ValidateDeadline(DateTimeOffset workspaceDeadline)
    {
        if (workspaceDeadline == DateTimeOffset.MinValue
            || workspaceDeadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workspaceDeadline),
                "Version-cell Analysis requires a finite Workspace deadline.");
        }
    }
}

public sealed class PackageVersionCellCheckpointAnalysisRequest
{
    public PackageVersionCellCheckpointAnalysisRequest(
        PackageVersionCellAnalysisEndpoint source,
        PackageVersionCellAnalysisEndpoint destination,
        DiffHistoryMemberSourceReceipt receipt,
        PackageVersionCellWorkspaceLimits limits,
        DateTimeOffset workspaceDeadline)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(limits);
        PackageVersionCellBaselineAnalysisRequest.ValidateDeadline(
            workspaceDeadline);
        if (!receipt.IsFor(source.Cell))
        {
            throw new ArgumentException(
                "The source endpoint does not match the receipt's exact population cell.",
                nameof(source));
        }
        if (!ReferenceEquals(
                source.Cell.Population,
                destination.Cell.Population))
        {
            throw new ArgumentException(
                "Checkpoint endpoints must belong to the exact same settled population.",
                nameof(destination));
        }
        if (ReferenceEquals(
                source.Cell.Association,
                destination.Cell.Association))
        {
            throw new ArgumentException(
                "A checkpoint destination must differ from the source cell.",
                nameof(destination));
        }

        Source = source;
        Destination = destination;
        Receipt = receipt;
        Limits = limits;
        WorkspaceDeadline = workspaceDeadline;
    }

    public PackageVersionCellAnalysisEndpoint Source { get; }
    public PackageVersionCellAnalysisEndpoint Destination { get; }
    public DiffHistoryMemberSourceReceipt Receipt { get; }
    public PackageVersionCellWorkspaceLimits Limits { get; }
    public DateTimeOffset WorkspaceDeadline { get; }
}

public enum PackageVersionCellSourceBindingStatus
{
    Exact,
    LibraryAbsent,
    LibraryAmbiguous,
    DeclarationAbsent,
    DeclarationAmbiguous,
    DeclarationRefused,
    DeclarationFailed,
}

/// <summary>Detached evidence for binding a receipt in a re-executed source cell.</summary>
public sealed record PackageVersionCellSourceBindingEvidence(
    PackageVersionCellSourceBindingStatus Status,
    ImmutableArray<CoordinateApiLibraryEvidence> Candidates,
    string? Detail);

/// <summary>One Analysis Finding result and its optional body-resolution evidence.</summary>
public sealed class PackageVersionCellAnalysisFinding<T>
    where T : notnull
{
    public PackageVersionCellAnalysisFinding(
        FindingInspection<T> inspection,
        MatchedApiMemberBodyResolutionEvidence? resolution = null,
        ArtifactRootFailure? analysisRootFailure = null)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (resolution is not null && analysisRootFailure is not null)
        {
            throw new ArgumentException(
                "An Analysis result cannot retain both body resolution and Root failure.");
        }

        Inspection = inspection;
        Resolution = resolution;
        AnalysisRootFailure = analysisRootFailure;
    }

    public FindingInspection<T> Inspection { get; }
    public MatchedApiMemberBodyResolutionEvidence? Resolution { get; }
    public ArtifactRootFailure? AnalysisRootFailure { get; }
}

public abstract record PackageVersionCellBaselineAnalysisResult<T>
    where T : notnull
{
    private protected PackageVersionCellBaselineAnalysisResult(
        PackageVersionCellAnalysisProducerKind producer,
        ApiCoordinateSourceSelectionEvidence selection)
    {
        if (!Enum.IsDefined(producer))
            throw new ArgumentOutOfRangeException(nameof(producer));
        ArgumentNullException.ThrowIfNull(selection);
        Producer = producer;
        Selection = selection;
    }

    public PackageVersionCellAnalysisProducerKind Producer { get; }
    public ApiCoordinateSourceSelectionEvidence Selection { get; }

    public sealed record Unselected :
        PackageVersionCellBaselineAnalysisResult<T>
    {
        internal Unselected(
            PackageVersionCellAnalysisProducerKind producer,
            ApiCoordinateSourceSelectionEvidence selection)
            : base(producer, selection)
        {
            if (selection.Status
                == ApiCoordinateSourceSelectionStatus.Selected)
            {
                throw new ArgumentException(
                    "An unselected baseline requires source-selection non-success.",
                    nameof(selection));
            }
        }
    }

    public sealed record Evaluated :
        PackageVersionCellBaselineAnalysisResult<T>
    {
        internal Evaluated(
            PackageVersionCellAnalysisProducerKind producer,
            ApiCoordinateSourceSelectionEvidence selection,
            DiffHistoryMemberSourceReceipt receipt,
            ApiCoordinateCorrespondenceEvidence validation,
            PackageVersionCellAnalysisFinding<T> finding)
            : base(producer, selection)
        {
            if (selection.Status
                != ApiCoordinateSourceSelectionStatus.Selected)
            {
                throw new ArgumentException(
                    "An evaluated baseline requires exact source selection.",
                    nameof(selection));
            }
            ArgumentNullException.ThrowIfNull(receipt);
            ArgumentNullException.ThrowIfNull(validation);
            ArgumentNullException.ThrowIfNull(finding);
            Receipt = receipt;
            Validation = validation;
            Finding = finding;
        }

        public DiffHistoryMemberSourceReceipt Receipt { get; }
        public ApiCoordinateCorrespondenceEvidence Validation { get; }
        public PackageVersionCellAnalysisFinding<T> Finding { get; }
    }
}

public sealed class PackageVersionCellCheckpointAnalysisResult<T>
    where T : notnull
{
    internal PackageVersionCellCheckpointAnalysisResult(
        PackageVersionCellAnalysisProducerKind producer,
        DiffHistoryMemberSourceReceipt receipt,
        PackageVersionCellSourceBindingEvidence sourceBinding,
        ApiCoordinateCorrespondenceEvidence? relationship,
        PackageVersionCellAnalysisFinding<T> finding)
    {
        if (!Enum.IsDefined(producer))
            throw new ArgumentOutOfRangeException(nameof(producer));
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(sourceBinding);
        ArgumentNullException.ThrowIfNull(finding);
        if ((sourceBinding.Status is
                PackageVersionCellSourceBindingStatus.LibraryAbsent
                or PackageVersionCellSourceBindingStatus.LibraryAmbiguous)
            != (relationship is null))
        {
            throw new ArgumentException(
                "Only a matched source Library can publish declaration relationship evidence.",
                nameof(relationship));
        }

        Producer = producer;
        Receipt = receipt;
        SourceBinding = sourceBinding;
        Relationship = relationship;
        Finding = finding;
    }

    public PackageVersionCellAnalysisProducerKind Producer { get; }
    public DiffHistoryMemberSourceReceipt Receipt { get; }
    public PackageVersionCellSourceBindingEvidence SourceBinding { get; }
    public ApiCoordinateCorrespondenceEvidence? Relationship { get; }
    public PackageVersionCellAnalysisFinding<T> Finding { get; }
}

public sealed record PackageVersionCellNoContribution(
    PackageVersionCellAnalysisEndpointRole Endpoint,
    PackageHouseRootNoContributionReason Reason);

public enum PackageVersionCellAnalysisWorkspaceStage
{
    ScopeRead,
    ScopeAdmission,
    Observation,
}

/// <summary>One owner-issued Workspace failure at the pair operation boundary.</summary>
public sealed class PackageVersionCellAnalysisWorkspaceFailure
{
    private PackageVersionCellAnalysisWorkspaceFailure(
        PackageVersionCellAnalysisWorkspaceStage stage,
        PackageVersionCellAnalysisEndpointRole? endpoint,
        WorkspaceScopeRejection? rejection,
        ArtifactRootFailure? artifactFailure,
        CoordinateLibraryPairingFailure? observationFailure)
    {
        int reasonCount = (rejection is null ? 0 : 1)
            + (artifactFailure is null ? 0 : 1)
            + (observationFailure is null ? 0 : 1);
        if (reasonCount != 1)
        {
            throw new ArgumentException(
                "An Analysis Workspace failure requires exactly one owner-issued reason.");
        }
        if ((stage == PackageVersionCellAnalysisWorkspaceStage.Observation)
            != (endpoint is not null && observationFailure is not null))
        {
            throw new ArgumentException(
                "Only an observation failure identifies an endpoint.");
        }

        Stage = stage;
        Endpoint = endpoint;
        Rejection = rejection;
        ArtifactFailure = artifactFailure;
        ObservationFailure = observationFailure;
    }

    public PackageVersionCellAnalysisWorkspaceStage Stage { get; }
    public PackageVersionCellAnalysisEndpointRole? Endpoint { get; }
    public WorkspaceScopeRejection? Rejection { get; }
    public ArtifactRootFailure? ArtifactFailure { get; }
    public CoordinateLibraryPairingFailure? ObservationFailure { get; }

    internal static PackageVersionCellAnalysisWorkspaceFailure Rejected(
        WorkspaceScopeRejection rejection) =>
        new(
            PackageVersionCellAnalysisWorkspaceStage.ScopeAdmission,
            endpoint: null,
            rejection,
            artifactFailure: null,
            observationFailure: null);

    internal static PackageVersionCellAnalysisWorkspaceFailure Failed(
        PackageVersionCellAnalysisWorkspaceStage stage,
        ArtifactRootFailure failure) =>
        new(
            stage,
            endpoint: null,
            rejection: null,
            failure,
            observationFailure: null);

    internal static PackageVersionCellAnalysisWorkspaceFailure Observation(
        PackageVersionCellAnalysisEndpointRole endpoint,
        CoordinateLibraryPairingFailure failure) =>
        new(
            PackageVersionCellAnalysisWorkspaceStage.Observation,
            endpoint,
            rejection: null,
            artifactFailure: null,
            failure);
}

/// <summary>Closed result of one bounded baseline or checkpoint operation.</summary>
public abstract record PackageVersionCellAnalysisOutcome<TResult>
    where TResult : notnull
{
    private protected PackageVersionCellAnalysisOutcome(
        ImmutableArray<PackageVersionCellExecutionEvidence> executions,
        PackageVersionCellWorkspaceCleanupEvidence? cleanup)
    {
        if (executions.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A cell Analysis outcome requires execution evidence.",
                nameof(executions));
        }
        Executions = executions;
        Cleanup = cleanup;
    }

    public ImmutableArray<PackageVersionCellExecutionEvidence> Executions
    {
        get;
    }

    public PackageVersionCellWorkspaceCleanupEvidence? Cleanup { get; }

    public sealed record Available : PackageVersionCellAnalysisOutcome<TResult>
    {
        internal Available(
            ImmutableArray<PackageVersionCellExecutionEvidence> executions,
            TResult result)
            : base(executions, cleanup: null)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        public TResult Result { get; }
    }

    public sealed record NoContribution :
        PackageVersionCellAnalysisOutcome<TResult>
    {
        internal NoContribution(
            ImmutableArray<PackageVersionCellExecutionEvidence> executions,
            ImmutableArray<PackageVersionCellNoContribution> failures)
            : base(executions, cleanup: null)
        {
            if (failures.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A no-contribution outcome requires at least one endpoint failure.",
                    nameof(failures));
            }
            Failures = failures;
        }

        public ImmutableArray<PackageVersionCellNoContribution> Failures
        {
            get;
        }
    }

    public sealed record WorkspaceFailure :
        PackageVersionCellAnalysisOutcome<TResult>
    {
        internal WorkspaceFailure(
            ImmutableArray<PackageVersionCellExecutionEvidence> executions,
            PackageVersionCellAnalysisWorkspaceFailure failure,
            PackageVersionCellWorkspaceCleanupEvidence? cleanup = null)
            : base(executions, cleanup)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public PackageVersionCellAnalysisWorkspaceFailure Failure { get; }
    }

    public sealed record CleanupFailure :
        PackageVersionCellAnalysisOutcome<TResult>
    {
        internal CleanupFailure(
            ImmutableArray<PackageVersionCellExecutionEvidence> executions,
            PackageVersionCellWorkspaceCleanupEvidence cleanup)
            : base(executions, cleanup)
        {
            if (cleanup.IsEmpty)
            {
                throw new ArgumentException(
                    "A cleanup-failure outcome requires cleanup evidence.",
                    nameof(cleanup));
            }
        }
    }
}
