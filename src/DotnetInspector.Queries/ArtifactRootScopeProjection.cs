using System.Collections.Immutable;

using DotnetInspector.Packages;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque process-local correspondence for one logical Artifact Root request.
/// </summary>
/// <remarks>
/// Correspondence is resource-free and Workspace-scoped. Equality uses only
/// owner-issued logical request facts; it never uses a physical generation,
/// display text, or retained artifact resource.
/// </remarks>
public abstract record ArtifactRootCorrespondence
{
    private protected ArtifactRootCorrespondence(
        InspectionWorkspaceIdentity workspaceIdentity)
    {
        ArgumentNullException.ThrowIfNull(workspaceIdentity);
        WorkspaceIdentity = workspaceIdentity;
    }

    internal InspectionWorkspaceIdentity WorkspaceIdentity { get; }
}

/// <summary>
/// Resource-free correspondence for one exact package Root request.
/// </summary>
public sealed record PackageArtifactRootCorrespondence :
    ArtifactRootCorrespondence
{
    readonly PackageArtifactRootCorrespondenceKey _key;

    internal PackageArtifactRootCorrespondence(
        InspectionWorkspaceIdentity workspaceIdentity,
        PackageArtifactRootCorrespondenceKey key)
        : base(workspaceIdentity)
    {
        _key = key;
    }

    internal bool Matches(PackageRootBinding binding) =>
        _key == PackageArtifactRootCorrespondenceKey.From(binding);
}

/// <summary>
/// Opaque process-local freshness reference for one exact Artifact Root
/// physical-generation issuance.
/// </summary>
/// <remarks>
/// Equality is reference identity. The reference carries no binding, context,
/// lease, content, delegate, or access authority.
/// </remarks>
public sealed class ArtifactRootGenerationReference
{
    internal ArtifactRootGenerationReference(
        InspectionWorkspaceIdentity workspaceIdentity,
        ArtifactRootCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(workspaceIdentity);
        ArgumentNullException.ThrowIfNull(correspondence);
        WorkspaceIdentity = workspaceIdentity;
        Correspondence = correspondence;
    }

    internal InspectionWorkspaceIdentity WorkspaceIdentity { get; }

    internal ArtifactRootCorrespondence Correspondence { get; }
}

/// <summary>
/// Stable resource-free diagnostic attached to a non-ready Root projection.
/// </summary>
public sealed record ArtifactRootRealizationDiagnostic
{
    internal ArtifactRootRealizationDiagnostic(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Point-in-time physical status for one Artifact Root.</summary>
public abstract record ArtifactRootRealizationStatus
{
    private protected ArtifactRootRealizationStatus()
    {
    }

    public sealed record Ready : ArtifactRootRealizationStatus
    {
        internal Ready(ArtifactRootGenerationReference generation)
        {
            ArgumentNullException.ThrowIfNull(generation);
            Generation = generation;
        }

        public ArtifactRootGenerationReference Generation { get; }
    }

    public sealed record Pending : ArtifactRootRealizationStatus
    {
        internal Pending(ArtifactRootRealizationDiagnostic evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public ArtifactRootRealizationDiagnostic Evidence { get; }
    }

    public sealed record Failed : ArtifactRootRealizationStatus
    {
        internal Failed(ArtifactRootRealizationDiagnostic evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public ArtifactRootRealizationDiagnostic Evidence { get; }
    }
}

/// <summary>
/// Immutable resource-free status projection for one Artifact Root.
/// </summary>
public sealed record ArtifactRootScopeProjection
{
    internal ArtifactRootScopeProjection(
        ArtifactRootCorrespondence correspondence,
        ArtifactRootRealizationStatus status)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ArgumentNullException.ThrowIfNull(status);
        Correspondence = correspondence;
        Status = status;
    }

    public ArtifactRootCorrespondence Correspondence { get; }

    public ArtifactRootRealizationStatus Status { get; }
}

/// <summary>Reason a current Artifact Root projection was not returned.</summary>
public enum ArtifactRootScopeProjectionUnavailableReason
{
    ForeignWorkspace,
    Absent,
    WorkspaceClosing,
    WorkspaceClosed,
}

/// <summary>Typed result of refreshing one retained Root correspondence.</summary>
public abstract record ArtifactRootScopeProjectionResult
{
    private protected ArtifactRootScopeProjectionResult()
    {
    }

    public sealed record Current : ArtifactRootScopeProjectionResult
    {
        internal Current(ArtifactRootScopeProjection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            Projection = projection;
        }

        public ArtifactRootScopeProjection Projection { get; }
    }

    public sealed record Unavailable : ArtifactRootScopeProjectionResult
    {
        internal Unavailable(
            ArtifactRootScopeProjectionUnavailableReason reason)
        {
            Reason = reason;
        }

        public ArtifactRootScopeProjectionUnavailableReason Reason { get; }
    }
}

internal readonly record struct PackageArtifactRootCorrespondenceKey(
    RealizedMemberCoordinate.Package Coordinate,
    string? SelectionTargetFramework,
    string? SelectionRuntimeIdentifier)
{
    internal static PackageArtifactRootCorrespondenceKey From(
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(
            binding.Coordinate,
            NormalizeSelectionTarget(
                binding.Root.RequestedTargetFramework),
            binding.Root.RequestedRuntimeIdentifier);
    }

    static string? NormalizeSelectionTarget(string? target) =>
        PackageCoordinateResolver.IsAcquisitionTargetText(target)
            ? target!.ToLowerInvariant()
            : target;
}

public sealed partial class InspectionWorkspace
{
    readonly Dictionary<
        ArtifactRootCorrespondence,
        ArtifactRootScopeProjection> _artifactRootScope = [];

    internal PackageArtifactRootCorrespondence
        CreatePackageArtifactRootCorrespondence(
            PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            return new(
                _identity,
                PackageArtifactRootCorrespondenceKey.From(binding));
        }
    }

    /// <summary>
    /// Refreshes the current resource-free status for one retained
    /// correspondence.
    /// </summary>
    public ArtifactRootScopeProjectionResult
        GetCurrentRootScopeProjection(
            ArtifactRootCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        lock (_gate)
        {
            if (!ReferenceEquals(
                    correspondence.WorkspaceIdentity,
                    _identity))
            {
                return new ArtifactRootScopeProjectionResult.Unavailable(
                    ArtifactRootScopeProjectionUnavailableReason
                        .ForeignWorkspace);
            }

            if (_state == InspectionWorkspaceState.Closing)
            {
                return new ArtifactRootScopeProjectionResult.Unavailable(
                    ArtifactRootScopeProjectionUnavailableReason
                        .WorkspaceClosing);
            }
            if (_state == InspectionWorkspaceState.Closed)
            {
                return new ArtifactRootScopeProjectionResult.Unavailable(
                    ArtifactRootScopeProjectionUnavailableReason
                        .WorkspaceClosed);
            }

            return _artifactRootScope.TryGetValue(
                    correspondence,
                    out ArtifactRootScopeProjection? projection)
                ? new ArtifactRootScopeProjectionResult.Current(
                    projection)
                : new ArtifactRootScopeProjectionResult.Unavailable(
                    ArtifactRootScopeProjectionUnavailableReason.Absent);
        }
    }

    internal bool TryAdmitArtifactRootAccess<TResult>(
        ImmutableArray<ArtifactRootScopeProjection> expected,
        ImmutableArray<ArtifactRootGenerationReference> supplied,
        Func<TResult> create,
        out TResult? result)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(create);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _state != InspectionWorkspaceState.Open,
                this);
            if (expected.Length != supplied.Length)
            {
                result = null;
                return false;
            }

            for (int index = 0; index < expected.Length; index++)
            {
                ArtifactRootScopeProjection projection =
                    expected[index];
                ArtifactRootGenerationReference generation =
                    supplied[index];
                if (generation is null
                    || projection.Status
                        is not ArtifactRootRealizationStatus.Ready expectedReady
                    || !ReferenceEquals(
                        expectedReady.Generation,
                        generation)
                    || !ReferenceEquals(
                        generation.WorkspaceIdentity,
                        _identity)
                    || !Equals(
                        generation.Correspondence,
                        projection.Correspondence)
                    || !_artifactRootScope.TryGetValue(
                        projection.Correspondence,
                        out ArtifactRootScopeProjection? current)
                    || current.Status
                        is not ArtifactRootRealizationStatus.Ready ready
                    || !ReferenceEquals(
                        ready.Generation,
                        generation))
                {
                    result = null;
                    return false;
                }
            }

            result = create();
            return true;
        }
    }

    internal void RetireArtifactRootScopeProjections(
        ImmutableArray<ArtifactRootScopeProjection> projections)
    {
        lock (_gate)
        {
            foreach (ArtifactRootScopeProjection projection in projections)
            {
                if (projection.Status
                        is not ArtifactRootRealizationStatus.Ready retired
                    || !_artifactRootScope.TryGetValue(
                        projection.Correspondence,
                        out ArtifactRootScopeProjection? current)
                    || current.Status
                        is not ArtifactRootRealizationStatus.Ready ready
                    || !ReferenceEquals(
                        ready.Generation,
                        retired.Generation))
                {
                    continue;
                }

                _artifactRootScope.Remove(projection.Correspondence);
            }
        }
    }
}
