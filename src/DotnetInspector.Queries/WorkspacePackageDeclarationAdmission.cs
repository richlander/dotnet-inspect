using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries;

public enum WorkspacePackageDeclarationAdmissionFailureKind
{
    Malformed,
    ForeignWorkspace,
    OccurrenceNotCurrent,
    RootPending,
    RootFailed,
    ArtifactGenerationMismatch,
    WorkspaceClosing,
    WorkspaceClosed,
}

public sealed record WorkspacePackageDeclarationAdmissionFailure(
    WorkspacePackageDeclarationAdmissionFailureKind Kind,
    ArtifactRootFailure? ArtifactFailure = null);

public sealed class WorkspacePackageDeclarationAdmission
{
    internal WorkspacePackageDeclarationAdmission(
        WorkspacePackageOccurrenceDescriptor occurrence,
        ArtifactRootGenerationReference generation,
        WorkspaceDeclarationContext context)
    {
        Occurrence = occurrence;
        Generation = generation;
        Context = context;
    }

    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }
    public ArtifactRootGenerationReference Generation { get; }
    public WorkspaceDeclarationContext Context { get; }
}

public abstract record WorkspacePackageDeclarationAdmissionResult
{
    private protected WorkspacePackageDeclarationAdmissionResult() { }

    public sealed record Admitted(
        WorkspacePackageDeclarationAdmission Admission)
        : WorkspacePackageDeclarationAdmissionResult;

    public sealed record Rejected(
        WorkspacePackageDeclarationAdmissionFailure Failure)
        : WorkspacePackageDeclarationAdmissionResult;
}

public sealed partial class InspectionWorkspace
{
    readonly Dictionary<
        WorkspacePackageOccurrenceIdentity,
        PackageDeclarationAdmissionState> _packageDeclarationAdmissions =
            new(ReferenceEqualityComparer.Instance);
    readonly HashSet<WorkspaceDeclarationContext>
        _packageDeclarationContexts =
            new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Explicitly admits one exact current Package Scope occurrence to this
    /// Workspace's declaration population.
    /// </summary>
    public async ValueTask<WorkspacePackageDeclarationAdmissionResult>
        AdmitPackageScopeDeclarationAsync(
            WorkspacePackageOccurrenceDescriptor occurrence,
            CancellationToken cancellationToken = default)
    {
        if (occurrence is null)
        {
            return Rejected(
                WorkspacePackageDeclarationAdmissionFailureKind.Malformed);
        }

        WorkspaceDeclarationLocator? observer = null;
        ArtifactRootQueryLease? retainedLease = null;
        await _rootCompositionGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_state != InspectionWorkspaceState.Open)
                {
                    return Rejected(
                        _state == InspectionWorkspaceState.Closing
                            ? WorkspacePackageDeclarationAdmissionFailureKind
                                .WorkspaceClosing
                            : WorkspacePackageDeclarationAdmissionFailureKind
                                .WorkspaceClosed);
                }

                WorkspacePackageOccurrence submitted =
                    occurrence.Occurrence;
                if (!ReferenceEquals(
                        submitted.Identity.WorkspaceIdentity,
                        _identity))
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .ForeignWorkspace);
                }
                if (submitted.Correspondence
                        is not PackageArtifactRootCorrespondence
                    || !Equals(
                        submitted.Correspondence,
                        occurrence.Realization.Correspondence))
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .Malformed);
                }

                WorkspacePackageOccurrenceDescriptor? current =
                    _scopeSnapshot?.Packages.FirstOrDefault(candidate =>
                        ReferenceEquals(
                            candidate.Occurrence.Identity,
                            submitted.Identity));
                if (current is null
                    || !ReferenceEquals(current.Occurrence, submitted)
                    || !Equals(
                        current.Occurrence.Correspondence,
                        submitted.Correspondence))
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .OccurrenceNotCurrent);
                }

                if (!_currentRoots.TryGetValue(
                        submitted.Correspondence,
                        out RootCurrent? currentRoot))
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .OccurrenceNotCurrent);
                }
                if (currentRoot.Projection.Status
                    is ArtifactRootRealizationStatus.Pending)
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .RootPending);
                }
                if (currentRoot.Projection.Status
                    is ArtifactRootRealizationStatus.Failed failed)
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .RootFailed,
                        failed.Failure);
                }

                if (occurrence.Realization.Status
                        is not ArtifactRootRealizationStatus.Ready submittedReady
                    || currentRoot.Projection.Status
                        is not ArtifactRootRealizationStatus.Ready currentReady
                    || !ReferenceEquals(
                        submittedReady.Generation,
                        currentReady.Generation))
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .ArtifactGenerationMismatch);
                }

                if (_packageDeclarationAdmissions.TryGetValue(
                        submitted.Identity,
                        out PackageDeclarationAdmissionState? existing))
                {
                    return ReferenceEquals(
                            existing.Admission.Generation,
                            submittedReady.Generation)
                        ? new WorkspacePackageDeclarationAdmissionResult
                            .Admitted(existing.Admission)
                        : Rejected(
                            WorkspacePackageDeclarationAdmissionFailureKind
                                .ArtifactGenerationMismatch);
                }

                if (currentRoot.Lifetime is not { } root)
                {
                    return Rejected(
                        WorkspacePackageDeclarationAdmissionFailureKind
                            .ArtifactGenerationMismatch);
                }

                retainedLease = root.Enter();
                PackageAssemblyContextRealization realization =
                    retainedLease.Realization;
                int order = checked(_nextDeclarationContextOrder++);
                WorkspaceDeclarationContext context = CreatePackageContext(
                    order,
                    occurrence,
                    realization);
                var admission = new WorkspacePackageDeclarationAdmission(
                    occurrence,
                    submittedReady.Generation,
                    context);
                var state = new PackageDeclarationAdmissionState(
                    admission,
                    retainedLease);

                _packageDeclarationAdmissions.Add(
                    submitted.Identity,
                    state);
                _packageDeclarationContexts.Add(context);
                _declarationContexts.Add(context);
                _declarationPopulation = null;
                observer = _declarationObserver;
                retainedLease = null;
                return new WorkspacePackageDeclarationAdmissionResult.Admitted(
                    admission);
            }
        }
        finally
        {
            retainedLease?.Dispose();
            _rootCompositionGate.Release();
            observer?.PopulationChanged();
        }
    }

    WorkspaceDeclarationContext CreatePackageContext(
        int order,
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageAssemblyContextRealization realization)
    {
        var request =
            new WorkspaceDeclarationRequest.PackageScope(occurrence);
        if (!realization.HasAssemblyContexts)
        {
            return new WorkspaceDeclarationContext(
                new(
                    _identity,
                    order,
                    request,
                    isRealized: false,
                    [],
                    [
                        new WorkspaceDeclarationFailure
                            .PackageScopeSelection(
                                occurrence,
                                occurrence.Occurrence.Package
                                    .SelectionStatus),
                    ]),
                group: null);
        }

        var members =
            ImmutableArray.CreateBuilder<WorkspaceDeclarationMember>(
                realization.SurfaceParticipants.Length);
        RealizedMemberCoordinate.Package package =
            occurrence.Occurrence.Package.Coordinate;
        PackageSourceCoordinate sourceCoordinate =
            PackageSourceCoordinate.Create(
                package.PackageId,
                package.Version);
        foreach (PackageAssemblyRoleParticipant entry
            in realization.SurfaceParticipants)
        {
            ResolvedAssemblyReference assembly =
                entry.Participant.Assembly;
            var identity =
                new ManagedMetadataIdentity.Assembly(assembly.Identity);
            AssemblyResolutionProvenance selection =
                assembly.Provenance
                    is AssemblyResolutionProvenance.PackageAsset selected
                ? AssemblyResolutionProvenance.Package(
                    selected.PackageId,
                    selected.PackageVersion,
                    selected.Tfm,
                    selected.Rid,
                    entry.Asset.Path)
                : assembly.Provenance;
            members.Add(new(
                new(_identity, order, members.Count),
                new ExactLibrarySourceCoordinate.Package(
                    sourceCoordinate,
                    identity),
                assembly.Identity,
                new WorkspaceDeclarationOrigin.PackageScope(
                    occurrence,
                    entry.Asset),
                selection));
        }

        return new WorkspaceDeclarationContext(
            new(
                _identity,
                order,
                request,
                isRealized: true,
                members.MoveToImmutable(),
                []),
            realization.SurfaceGroup);
    }

    static WorkspacePackageDeclarationAdmissionResult.Rejected Rejected(
        WorkspacePackageDeclarationAdmissionFailureKind kind,
        ArtifactRootFailure? artifactFailure = null) =>
        new(new(kind, artifactFailure));

    internal bool IsPackageDeclarationContext(
        WorkspaceDeclarationContext context) =>
        _packageDeclarationContexts.Contains(context);

    internal ImmutableArray<ArtifactRootQueryLease>
        DetachPackageDeclarationLeases()
    {
        ImmutableArray<ArtifactRootQueryLease> leases =
            [.. _packageDeclarationAdmissions.Values.Select(
                static admission => admission.Lease)];
        _packageDeclarationAdmissions.Clear();
        _packageDeclarationContexts.Clear();
        return leases;
    }

    sealed record PackageDeclarationAdmissionState(
        WorkspacePackageDeclarationAdmission Admission,
        ArtifactRootQueryLease Lease);
}
