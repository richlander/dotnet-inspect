using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>Finite Workspace realization limits for one package version cell.</summary>
public sealed class PackageVersionCellWorkspaceLimits
{
    public PackageVersionCellWorkspaceLimits(
        int maximumAssemblies,
        long maximumEntryBytes,
        long maximumRetainedImageBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumEntryBytes,
            Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumRetainedImageBytes);

        MaximumAssemblies = maximumAssemblies;
        MaximumEntryBytes = maximumEntryBytes;
        MaximumRetainedImageBytes = maximumRetainedImageBytes;
    }

    public int MaximumAssemblies { get; }

    public long MaximumEntryBytes { get; }

    public long MaximumRetainedImageBytes { get; }
}

/// <summary>
/// One exact version-population cell prepared for bounded Metadata inspection.
/// </summary>
public sealed class PackageVersionCellMetadataInspectionRequest
{
    public PackageVersionCellMetadataInspectionRequest(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext,
        PackageVersionCellWorkspaceLimits limits,
        DateTimeOffset workspaceDeadline)
        : this(cell, operation, targetContext, limits, workspaceDeadline, null)
    {
    }

    public PackageVersionCellMetadataInspectionRequest(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext,
        PackageVersionCellWorkspaceLimits limits,
        DateTimeOffset workspaceDeadline,
        PackageVersionCellApiInspectionRequest? apiInspection)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(limits);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Version-cell Metadata inspection requires a Realize operation.",
                nameof(operation));
        }
        if (workspaceDeadline == DateTimeOffset.MinValue
            || workspaceDeadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workspaceDeadline),
                "Version-cell Metadata inspection requires a finite Workspace deadline.");
        }

        Cell = cell;
        Limits = limits;
        WorkspaceDeadline = workspaceDeadline;
        ApiInspection = apiInspection;
        // Every Metadata finding (the image overview and the API surfaces
        // behind api.type, api.member, and api.attribute) reads the compile
        // surface group only, so the cell is realized surface-only: a ranged
        // read fetches the surface folders, and the package Root prepares no
        // implementation role (docs/design/package-read-demand.md).
        HouseExecution = cell.PrepareExecution(
            operation,
            targetContext,
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.PackageOnly,
            PackageAssetDemand.Surface);
    }

    public PackageHouseVersionPopulationCell Cell { get; }

    public PackageVersionCellWorkspaceLimits Limits { get; }

    public DateTimeOffset WorkspaceDeadline { get; }

    public PackageVersionCellApiInspectionRequest? ApiInspection { get; }

    public PackageHouseVersionPopulationCellExecution HouseExecution
    {
        get;
    }
}

/// <summary>
/// Host-supplied capability for executing one PackageHouse-issued cell request.
/// </summary>
public interface IPackageHouseVersionPopulationCellExecutor
{
    Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseVersionPopulationCellExecution execution,
        CancellationToken cancellationToken = default);
}

/// <summary>Detached execution evidence retained by a cell operation.</summary>
public sealed class PackageVersionCellExecutionEvidence
{
    internal PackageVersionCellExecutionEvidence(
        PackageHouseVersionPopulationCellExecution execution,
        PackageHouseResult houseResult,
        PackageHouseRealizationReceipt.Compile? compileRealization,
        RealizedMemberCoordinate.Package? rootCoordinate)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(houseResult);
        if (!ReferenceEquals(houseResult.Request, execution.Request))
        {
            throw new ArgumentException(
                "Inspection evidence requires the exact prepared House request.",
                nameof(houseResult));
        }
        if (compileRealization is not null
            && !ReferenceEquals(
                houseResult.Evidence.Realization,
                compileRealization))
        {
            throw new ArgumentException(
                "Inspection evidence requires the House result's exact compile realization.",
                nameof(compileRealization));
        }
        if (rootCoordinate is not null && compileRealization is null)
        {
            throw new ArgumentException(
                "A realized Root coordinate requires exact compile realization evidence.",
                nameof(rootCoordinate));
        }

        PackageId =
            execution.Cell.Population.Request.Range.PackageId;
        Address = execution.Cell.Address;
        HouseResult = houseResult;
        CompileRealization = compileRealization;
        RootCoordinate = rootCoordinate;
    }

    public string PackageId { get; }

    public PackageVersionAddress Address { get; }

    public PackageHouseResult HouseResult { get; }

    public PackageHouseRealizationReceipt.Compile? CompileRealization
    {
        get;
    }

    public RealizedMemberCoordinate.Package? RootCoordinate { get; }

    internal static PackageVersionCellExecutionEvidence Create(
        PackageHouseVersionPopulationCellExecution execution,
        PackageHouseResult result,
        PackageHouseRootContribution? contribution) =>
        new(
            execution,
            result,
            contribution?.Realization
                ?? result.Evidence.Realization
                    as PackageHouseRealizationReceipt.Compile,
            contribution?.Binding.Coordinate);
}

public enum PackageVersionCellMetadataWorkspaceStage
{
    ScopeRead,
    ScopeAdmission,
    RootQuery,
}

/// <summary>One owner-issued Workspace failure at the operation boundary.</summary>
public sealed class PackageVersionCellMetadataWorkspaceFailure
{
    private PackageVersionCellMetadataWorkspaceFailure(
        PackageVersionCellMetadataWorkspaceStage stage,
        WorkspaceScopeRejection? rejection,
        ArtifactRootFailure? artifactFailure)
    {
        if ((rejection is null) == (artifactFailure is null))
        {
            throw new ArgumentException(
                "A Workspace failure requires exactly one owner-issued reason.");
        }

        Stage = stage;
        Rejection = rejection;
        ArtifactFailure = artifactFailure;
    }

    public PackageVersionCellMetadataWorkspaceStage Stage { get; }

    public WorkspaceScopeRejection? Rejection { get; }

    public ArtifactRootFailure? ArtifactFailure { get; }

    internal static PackageVersionCellMetadataWorkspaceFailure Rejected(
        PackageVersionCellMetadataWorkspaceStage stage,
        WorkspaceScopeRejection rejection) =>
        new(stage, rejection, artifactFailure: null);

    internal static PackageVersionCellMetadataWorkspaceFailure Failed(
        PackageVersionCellMetadataWorkspaceStage stage,
        ArtifactRootFailure failure) =>
        new(stage, rejection: null, failure);
}

public enum PackageVersionCellWorkspaceCleanupStage
{
    GroupRelease,
    ArtifactRelease,
    CloseReportContract,
    CloseOrchestration,
}

public readonly record struct PackageVersionCellWorkspaceCleanupFailure(
    PackageVersionCellWorkspaceCleanupStage Stage,
    int Count);

/// <summary>Bounded resource-free evidence from awaited Workspace close.</summary>
public sealed record PackageVersionCellWorkspaceCleanupEvidence
{
    internal PackageVersionCellWorkspaceCleanupEvidence(
        ImmutableArray<PackageVersionCellWorkspaceCleanupFailure> failures)
    {
        Failures = failures;
    }

    public ImmutableArray<PackageVersionCellWorkspaceCleanupFailure> Failures
    {
        get;
    }

    public bool IsEmpty => Failures.IsEmpty;
}

/// <summary>Cleanup evidence attached to a propagated cell-operation exception.</summary>
public static class PackageVersionCellWorkspaceExceptionEvidence
{
    static readonly object CleanupKey = new();

    public static bool TryGetCleanup(
        Exception primary,
        [NotNullWhen(true)]
        out PackageVersionCellWorkspaceCleanupEvidence? cleanup)
    {
        ArgumentNullException.ThrowIfNull(primary);
        cleanup =
            primary.Data[CleanupKey]
                as PackageVersionCellWorkspaceCleanupEvidence;
        return cleanup is not null;
    }

    internal static void Attach(
        Exception primary,
        PackageVersionCellWorkspaceCleanupEvidence cleanup)
    {
        if (!cleanup.IsEmpty)
            primary.Data[CleanupKey] = cleanup;
    }
}

/// <summary>
/// Closed result of one PackageHouse cell realization and Metadata inspection.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(PackageVersionCellMetadataInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(PackageVersionCellMetadataInspectionOutcome.NoContribution),
    "noContribution")]
[JsonDerivedType(
    typeof(PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure),
    "workspaceFailure")]
[JsonDerivedType(
    typeof(PackageVersionCellMetadataInspectionOutcome.CleanupFailure),
    "cleanupFailure")]
public abstract record PackageVersionCellMetadataInspectionOutcome
{
    private protected PackageVersionCellMetadataInspectionOutcome(
        PackageVersionCellExecutionEvidence evidence,
        PackageVersionCellWorkspaceCleanupEvidence? cleanup)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
        Cleanup = cleanup;
    }

    public PackageVersionCellExecutionEvidence Evidence { get; }

    public PackageVersionCellWorkspaceCleanupEvidence? Cleanup { get; }

    public sealed record Available :
        PackageVersionCellMetadataInspectionOutcome
    {
        internal Available(
            PackageVersionCellExecutionEvidence evidence,
            AssemblyContextResult<MetadataImageOverview> metadata,
            PackageVersionCellApiInspectionResult? apiInspection = null)
            : base(evidence, cleanup: null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            Metadata = metadata;
            ApiInspection = apiInspection;
        }

        public AssemblyContextResult<MetadataImageOverview> Metadata { get; }

        public PackageVersionCellApiInspectionResult? ApiInspection { get; }
    }

    public sealed record NoContribution :
        PackageVersionCellMetadataInspectionOutcome
    {
        internal NoContribution(
            PackageVersionCellExecutionEvidence evidence,
            PackageHouseRootNoContributionReason reason)
            : base(evidence, cleanup: null)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }

        public PackageHouseRootNoContributionReason Reason { get; }
    }

    public sealed record WorkspaceFailure :
        PackageVersionCellMetadataInspectionOutcome
    {
        internal WorkspaceFailure(
            PackageVersionCellExecutionEvidence evidence,
            PackageVersionCellMetadataWorkspaceFailure failure,
            PackageVersionCellWorkspaceCleanupEvidence? cleanup = null)
            : base(evidence, cleanup)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public PackageVersionCellMetadataWorkspaceFailure Failure { get; }
    }

    public sealed record CleanupFailure :
        PackageVersionCellMetadataInspectionOutcome
    {
        internal CleanupFailure(
            PackageVersionCellExecutionEvidence evidence,
            PackageVersionCellWorkspaceCleanupEvidence cleanup)
            : base(evidence, cleanup)
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
