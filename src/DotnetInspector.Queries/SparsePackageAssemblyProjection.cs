using System.Collections.Immutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Explicit entry and aggregate retained-image bounds for one sparse selected
/// package assembly projection.
/// </summary>
/// <remarks>
/// Both bounds are required and must be positive; the projection never infers
/// a default budget. The aggregate bound covers the artifact-owned snapshot
/// and the independent Metadata group snapshot, so half is reserved for each.
/// </remarks>
public sealed record SparsePackageAssemblyProjectionOptions
{
    /// <summary>The largest expanded package entry the projection admits.</summary>
    public required long MaxSelectedEntryBytes { get; init; }

    /// <summary>
    /// The retained-byte budget covering the artifact snapshot and the
    /// one-participant group snapshot together.
    /// </summary>
    public required long MaxAggregateRetainedImageBytes { get; init; }

    internal void Validate()
    {
        if (MaxSelectedEntryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSelectedEntryBytes),
                MaxSelectedEntryBytes,
                "The sparse package assembly projection requires a positive per-entry byte limit.");
        }
        if (MaxAggregateRetainedImageBytes < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAggregateRetainedImageBytes),
                MaxAggregateRetainedImageBytes,
                "The sparse package assembly projection requires an aggregate retained-image bound of at least two bytes.");
        }
    }
}

/// <summary>One owner-local cleanup stage attempted before ownership transfer.</summary>
public enum SparsePackageProjectionCleanupStage
{
    /// <summary>Disposal of the not-yet-transferred participant group.</summary>
    Participant,

    /// <summary>Disposal of the not-yet-transferred artifact query lease.</summary>
    QueryLease,

    /// <summary>Disposal of the not-yet-transferred artifact session.</summary>
    ArtifactSession,
}

/// <summary>One incomplete cleanup stage and how many failures it observed.</summary>
public readonly record struct SparsePackageProjectionCleanupEvidence(
    SparsePackageProjectionCleanupStage Stage,
    int FailureCount);

/// <summary>
/// Bounded, resource-free evidence that owner-local cleanup before ownership
/// transfer did not complete.
/// </summary>
/// <remarks>
/// The receipt is always secondary to the primary projection outcome,
/// cancellation, or unexpected exception, and is absent when cleanup
/// succeeded or when the projection transferred ownership. It carries closed
/// stage and count data only: no exception, message, path, package-authored
/// string, stream, artifact registration, session, lease, participant,
/// workspace, callback, or opener. Gated by
/// <c>SparsePackageAssemblyProjectionTests.Projection_CancellationKeepsCancellationAndAttachesReceipt</c>
/// and <c>Projection_IncompleteCleanupProducesBoundedResourceFreeReceipt</c>.
/// </remarks>
public sealed class SparsePackageProjectionCleanupReceipt
{
    static readonly object ReceiptKey = new();

    internal SparsePackageProjectionCleanupReceipt(
        ImmutableArray<SparsePackageProjectionCleanupEvidence> incompleteStages)
    {
        IncompleteStages = incompleteStages;
        int total = 0;
        foreach (SparsePackageProjectionCleanupEvidence evidence
            in incompleteStages)
        {
            total = checked(total + evidence.FailureCount);
        }

        FailureCount = total;
    }

    /// <summary>The owner-local stages whose cleanup did not complete.</summary>
    public ImmutableArray<SparsePackageProjectionCleanupEvidence>
        IncompleteStages
    { get; }

    /// <summary>The total number of observed cleanup failures.</summary>
    public int FailureCount { get; }

    /// <summary>
    /// Reads the receipt this owner attached to a primary exception, or
    /// <see langword="null"/> when owner-local cleanup completed.
    /// </summary>
    public static SparsePackageProjectionCleanupReceipt? FromException(
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Data[ReceiptKey]
            as SparsePackageProjectionCleanupReceipt;
    }

    internal void AttachTo(Exception primary) =>
        primary.Data[ReceiptKey] = this;
}

/// <summary>
/// The closed package-owned outcome of one sparse selected-assembly
/// projection.
/// </summary>
/// <remarks>
/// Null inputs, an invalid bound, and a workspace that is not asynchronous
/// remain caller contract violations outside this algebra. Cancellation
/// propagates with the caller's token, and unexpected implementation
/// exceptions remain exceptional.
/// </remarks>
public abstract class SparsePackageAssemblyProjectionOutcome
{
    private protected SparsePackageAssemblyProjectionOutcome()
    {
    }

    /// <summary>One operation-scoped, resource-bearing sparse realization.</summary>
    public sealed class Available : SparsePackageAssemblyProjectionOutcome
    {
        internal Available(SparsePackageAssemblyRealization realization) =>
            Realization = realization;

        public SparsePackageAssemblyRealization Realization { get; }
    }

    /// <summary>
    /// The Root no longer corresponds to the binding's content-generation
    /// identity.
    /// </summary>
    public sealed class InvalidBinding : SparsePackageAssemblyProjectionOutcome
    {
        internal InvalidBinding()
        {
        }
    }

    /// <summary>
    /// The supplied asset is not an exact canonical member of the binding's
    /// frozen selected sequences.
    /// </summary>
    public sealed class InvalidSelectedAsset :
        SparsePackageAssemblyProjectionOutcome
    {
        internal InvalidSelectedAsset()
        {
        }
    }

    /// <summary>
    /// The selected entry is missing, or the package content returned
    /// <see langword="false"/> from bounded open.
    /// </summary>
    public sealed class SelectedEntryUnavailable :
        SparsePackageAssemblyProjectionOutcome
    {
        internal SelectedEntryUnavailable(
            SparsePackageProjectionCleanupReceipt? cleanup) =>
            Cleanup = cleanup;

        public SparsePackageProjectionCleanupReceipt? Cleanup { get; }
    }

    /// <summary>
    /// A declared-length preflight or observed artifact copy crossed the
    /// admitted entry or artifact-share byte limit.
    /// </summary>
    public sealed class EntryByteLimitExceeded :
        SparsePackageAssemblyProjectionOutcome
    {
        internal EntryByteLimitExceeded(
            SparsePackageProjectionCleanupReceipt? cleanup) =>
            Cleanup = cleanup;

        public SparsePackageProjectionCleanupReceipt? Cleanup { get; }
    }

    /// <summary>The artifact owner's typed publication failures, preserved.</summary>
    public sealed class ArtifactPublicationFailed :
        SparsePackageAssemblyProjectionOutcome
    {
        internal ArtifactPublicationFailed(
            ImmutableArray<ArtifactSetAdmissionFailure> failures,
            SparsePackageProjectionCleanupReceipt? cleanup)
        {
            Failures = failures;
            Cleanup = cleanup;
        }

        public ImmutableArray<ArtifactSetAdmissionFailure> Failures { get; }

        public SparsePackageProjectionCleanupReceipt? Cleanup { get; }
    }
}

/// <summary>
/// One operation-scoped, resource-bearing projection of a single canonical
/// package assembly.
/// </summary>
/// <remarks>
/// <para>
/// This value is execution authority, not durable query evidence. It is
/// deliberately live: the group, participant, and query entry point exist so a
/// consumer can enter real owner-authorized query validation while the
/// candidate workspace is open. A consumer may copy the package coordinate,
/// content-generation identity, selection identity, selected asset, and typed
/// admission facts into a resource-free receipt, but must not retain this
/// realization, its group, or its participant.
/// </para>
/// <para>
/// Disposing the realization releases its group but not the transferred
/// artifact session; the candidate workspace remains the release owner until
/// <see cref="InspectionWorkspace.CloseAsync"/> completes. Gated by
/// <c>SparsePackageAssemblyProjectionTests.Projection_CloseWaitsForActiveQueryThenDeniesAccess</c>
/// and
/// <c>ReacquisitionRequest_SurvivesCandidateWorkspaceDisposal</c>.
/// </para>
/// </remarks>
public sealed class SparsePackageAssemblyRealization : IDisposable
{
    readonly PackageAssemblyContextRoles _roles;
    readonly ArtifactSetSession _session;
    readonly ArtifactQueryLease _queryLease;
    readonly ArtifactIdentity _artifact;

    internal SparsePackageAssemblyRealization(
        PackageAssemblyContextRoles roles,
        ArtifactSetSession session,
        ArtifactQueryLease queryLease,
        ArtifactIdentity artifact,
        PackageCompileAsset asset,
        ArtifactAssemblyProjectionOutcome admission,
        bool identityDecoded)
    {
        _roles = roles;
        _session = session;
        _queryLease = queryLease;
        _artifact = artifact;
        Asset = asset;
        Admission = admission;
        IdentityDecoded = identityDecoded;
        Participant = roles.SurfaceParticipants.Single();
    }

    /// <summary>The exact canonical selected asset this projection admitted.</summary>
    public PackageCompileAsset Asset { get; }

    /// <summary>The exact one-participant group that owns query authority.</summary>
    public AssemblyContextGroup Group => _roles.SurfaceGroup;

    /// <summary>The exact published participant.</summary>
    public AssemblyContextParticipant Participant { get; }

    /// <summary>
    /// Whether the compatibility descriptor decoded an assembly identity
    /// rather than using the deterministic rejection carrier.
    /// </summary>
    /// <remarks>
    /// This is compatibility evidence only. It is never supported-format or
    /// query authority; <see cref="Admission"/> owns both. Gated by
    /// <c>SparsePackageAssemblyProjectionTests.Projection_RejectedCarrierRefusesQueryWithOwnerFailure</c>.
    /// </remarks>
    public bool IdentityDecoded { get; }

    /// <summary>
    /// The exact exhaustive Metadata-owned admission outcome for the retained
    /// artifact.
    /// </summary>
    public ArtifactAssemblyProjectionOutcome Admission { get; }

    /// <summary>
    /// Runs one bounded producer against the retained artifact through the
    /// assembly-inspection owner's query revalidation.
    /// </summary>
    /// <remarks>
    /// The group owns active-operation quiescence for the call, and the
    /// artifact owner supplies the content view; no raw bytes, opener, or
    /// query lease escapes. An admission that is not
    /// <see cref="ArtifactAssemblyProjectionOutcome.Projected"/> refuses with
    /// the same owner-typed reason and runs no producer. Gated by
    /// <c>SparsePackageAssemblyProjectionTests.Projection_RunsProducerThroughOwnerAuthorizedQueryView</c>
    /// and
    /// <c>Projection_RejectedCarrierRefusesQueryWithOwnerFailure</c>.
    /// </remarks>
    public ArtifactAssemblyQueryOutcome<TResult> ExecuteAssemblyQuery<TResult>(
        Func<AssemblyInspectionSession, CancellationToken, TResult> producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (Admission is not ArtifactAssemblyProjectionOutcome.Projected
            projected)
        {
            return Refuse<TResult>(Admission);
        }

        return Group.UseContext(() =>
            ArtifactAssemblyQueryOutcome<TResult>.FromAccess(
                _session.WithQueryContent(
                    _artifact,
                    _queryLease,
                    (view, token) => ArtifactAssemblyInspection.Execute(
                        view,
                        projected.Value,
                        producer,
                        token),
                    cancellationToken)));
    }

    public void Dispose() => _roles.Dispose();

    static ArtifactAssemblyQueryOutcome<TResult> Refuse<TResult>(
        ArtifactAssemblyProjectionOutcome admission) =>
        admission switch
        {
            ArtifactAssemblyProjectionOutcome.NotAssembly notAssembly =>
                new ArtifactAssemblyQueryOutcome<TResult>.NotAssembly(
                    notAssembly.Kind),
            ArtifactAssemblyProjectionOutcome.Rejected rejected =>
                new ArtifactAssemblyQueryOutcome<TResult>.Rejected(
                    new ArtifactAssemblyQueryFailure(
                        QueryFailure(rejected.Failure.Kind))),
            _ => throw new InvalidOperationException(
                "Unknown artifact assembly admission outcome."),
        };

    static ArtifactAssemblyQueryFailureKind QueryFailure(
        ArtifactAssemblyProjectionFailureKind kind) =>
        kind switch
        {
            ArtifactAssemblyProjectionFailureKind.AdmissionUnauthorized =>
                ArtifactAssemblyQueryFailureKind.QueryUnauthorized,
            ArtifactAssemblyProjectionFailureKind.UnsupportedWindowsMetadata =>
                ArtifactAssemblyQueryFailureKind.UnsupportedWindowsMetadata,
            ArtifactAssemblyProjectionFailureKind.MalformedMetadata =>
                ArtifactAssemblyQueryFailureKind.MalformedMetadata,
            ArtifactAssemblyProjectionFailureKind.EmptyModuleVersionId =>
                ArtifactAssemblyQueryFailureKind.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown artifact assembly admission failure."),
        };
}

public sealed partial class InspectionWorkspace
{
    const string SparseRejectionCarrierName = "SelectedPackageAsset";

    /// <summary>
    /// Projects one exact canonical selected package asset into one
    /// artifact-backed, one-participant candidate group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller owns why it selected the asset. This adapter does not choose
    /// a primary assembly, interpret a query role, count siblings, or map
    /// package selection states into query outcomes. Only the exact canonical
    /// <see cref="PackageCompileAsset"/> retained in the binding's frozen
    /// <c>Assets</c> or <c>ImplementationAssets</c> sequence carries opening
    /// authority; a reconstructed equal value, a foreign binding's asset, or a
    /// non-selected candidate is rejected before content access.
    /// </para>
    /// <para>
    /// Exactly one package entry is opened and materialized under both the
    /// declared and observed byte limits. The aggregate bound is partitioned
    /// between the artifact snapshot and the one Metadata group snapshot, so an
    /// image of <c>N</c> bytes is admitted at <c>2N</c> and rejected at
    /// <c>2N - 1</c>. Gated by
    /// <c>SparsePackageAssemblyProjectionTests.Projection_RejectsReconstructedOrForeignSelectedAsset</c>,
    /// <c>Projection_PublishesOneExactParticipantForCanonicalAsset</c>,
    /// <c>Projection_AggregatePartitionAdmitsAtTwiceTheImage</c>,
    /// <c>Projection_DeclaredEntryLimitRejectsBeforeOpen</c>,
    /// and
    /// <c>Projection_ReportsArtifactPublicationFailureWithOwnerCodes</c>.
    /// </para>
    /// </remarks>
    public async ValueTask<SparsePackageAssemblyProjectionOutcome>
        ProjectSelectedPackageAssemblyAsync(
            PackageRootBinding package,
            PackageCompileAsset selectedAsset,
            SparsePackageAssemblyProjectionOptions options,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(selectedAsset);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "Sparse package assembly projection requires a workspace created by CreateAsynchronous.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        IPackageContent content = package.Root.Content;
        if (!ReferenceEquals(
                content.GenerationIdentity,
                package.ContentGenerationIdentity))
        {
            return new SparsePackageAssemblyProjectionOutcome
                .InvalidBinding();
        }
        if (!IsCanonicalSelectedAsset(
                package.Root.AssetSelection,
                selectedAsset))
        {
            return new SparsePackageAssemblyProjectionOutcome
                .InvalidSelectedAsset();
        }

        long artifactShare = options.MaxAggregateRetainedImageBytes / 2;
        long groupShare =
            options.MaxAggregateRetainedImageBytes - artifactShare;
        long entryLimit = Math.Min(
            options.MaxSelectedEntryBytes,
            artifactShare);
        if (content is IPackageContentEntryManifest manifest)
        {
            if (!manifest.TryGetEntryLength(
                    selectedAsset.Path,
                    out long declaredLength))
            {
                return new SparsePackageAssemblyProjectionOutcome
                    .SelectedEntryUnavailable(cleanup: null);
            }
            if (declaredLength < 0 || declaredLength > entryLimit)
            {
                return new SparsePackageAssemblyProjectionOutcome
                    .EntryByteLimitExceeded(cleanup: null);
            }
        }

        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = 1,
                MaxArtifactBytes = entryLimit,
                MaxRetainedBytes = artifactShare,
            });
        var entryState = new SelectedEntryOpenState();
        ArtifactQueryLease? queryLease = null;
        PackageAssemblyContextRoles? roles = null;
        bool transferred = false;
        try
        {
            ArtifactContribution contribution =
                await RegisterSelectedEntryAsync(
                        session,
                        package,
                        selectedAsset,
                        entryLimit,
                        entryState,
                        cancellationToken)
                    .ConfigureAwait(false);
            ArtifactAssemblyProjectionOutcome? admission = null;
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            admission = ArtifactAssemblyInspection.Project(
                                view,
                                token);
                            // Non-projectable images still publish as
                            // compatibility rejection carriers.
                            return null;
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished rejected)
            {
                SparsePackageProjectionCleanupReceipt? receipt =
                    await CleanupSparseProjectionAsync(
                            roles: null,
                            queryLease: null,
                            session,
                            primary: null)
                        .ConfigureAwait(false);
                return NotPublished(rejected, entryState, receipt);
            }

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            ArtifactContentReference reference =
                session.GetContentReference(
                    contribution.Descriptor.Identity,
                    queryLease);
            ResolvedAssemblyReference assembly = SparseAssembly(
                reference,
                package,
                selectedAsset,
                admission!,
                out bool identityDecoded);
            roles = CreatePackageAssemblyContextRoles(
                [assembly],
                implementationAssemblies: null,
                correspondences: [],
                surfaceOptions: new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = groupShare,
                });
            RegisterArtifactSession(
                session,
                queryLease,
                [roles.SurfaceGroup]);
            transferred = true;
            return new SparsePackageAssemblyProjectionOutcome.Available(
                new SparsePackageAssemblyRealization(
                    roles,
                    session,
                    queryLease,
                    contribution.Descriptor.Identity,
                    selectedAsset,
                    admission!,
                    identityDecoded));
        }
        catch (Exception failure)
        {
            if (!transferred)
            {
                await CleanupSparseProjectionAsync(
                        roles,
                        queryLease,
                        session,
                        failure)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    static bool IsCanonicalSelectedAsset(
        PackageCompileAssetSelection selection,
        PackageCompileAsset asset) =>
        Contains(selection.Assets, asset)
        || Contains(selection.ImplementationAssets, asset);

    static bool Contains(
        IReadOnlyList<PackageCompileAsset> assets,
        PackageCompileAsset asset)
    {
        for (int index = 0; index < assets.Count; index++)
        {
            if (ReferenceEquals(assets[index], asset))
                return true;
        }

        return false;
    }

    static async ValueTask<ArtifactContribution> RegisterSelectedEntryAsync(
        ArtifactSetSession session,
        PackageRootBinding package,
        PackageCompileAsset selectedAsset,
        long entryLimit,
        SelectedEntryOpenState entryState,
        CancellationToken cancellationToken)
    {
        ArtifactContribution? registered = null;
        await session.AddRequiredAcquisitionAsync(
                (scope, generationEnd) =>
                {
                    generationEnd.ThrowIfCancellationRequested();
                    registered = scope.Register(
                        new PackageAssemblyArtifactProvenance(
                            package.Coordinate,
                            package.ContentGenerationIdentity,
                            package.SelectionIdentity,
                            selectedAsset),
                        token => OpenSelectedEntry(
                            package.Root.Content,
                            selectedAsset.Path,
                            entryLimit,
                            entryState,
                            token),
                        kind: "package-assembly");
                    return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [registered],
                            ArtifactAcquisitionLeases.None));
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return registered!;
    }

    static Stream OpenSelectedEntry(
        IPackageContent content,
        string path,
        long maxExpandedBytes,
        SelectedEntryOpenState entryState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.TryOpenEntry(
                path,
                maxExpandedBytes,
                out Stream? stream))
        {
            // The current IPackageContent boundary cannot distinguish a
            // missing entry from an implementation that refuses the bound.
            entryState.EntryUnavailable = true;
            throw new SelectedPackageEntryUnavailableException();
        }

        return stream;
    }

    ResolvedAssemblyReference SparseAssembly(
        ArtifactContentReference reference,
        PackageRootBinding package,
        PackageCompileAsset selectedAsset,
        ArtifactAssemblyProjectionOutcome admission,
        out bool identityDecoded)
    {
        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Package(
                package.Root.PackageId,
                package.Root.PackageVersion,
                selectedAsset.TargetFramework,
                rid: null);
        if (admission is ArtifactAssemblyProjectionOutcome.Projected projected
            && !string.IsNullOrWhiteSpace(projected.Value.Identity.Name))
        {
            identityDecoded = true;
            return ResolvedAssemblyReference.CreateFromArtifactProjection(
                reference.Registration,
                projected.Value,
                reference.OpenRead,
                provenance);
        }

        // A rejection carrier keeps the selected image visible as one
        // participant; its deterministic name is never artifact-derived
        // identity.
        ResolvedAssemblyReference carrier = ResolvedAssemblyReference
            .CreateFromArtifactWithFallbackIdentity(
                reference.Registration,
                reference.OpenRead,
                new AssemblyReferenceIdentity(
                    SparseRejectionCarrierName,
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null),
                provenance,
                out bool usedFallbackIdentity);
        identityDecoded = !usedFallbackIdentity;
        return carrier;
    }

    static SparsePackageAssemblyProjectionOutcome NotPublished(
        ArtifactSetPublicationOutcome.NotPublished rejected,
        SelectedEntryOpenState entryState,
        SparsePackageProjectionCleanupReceipt? cleanup)
    {
        if (entryState.EntryUnavailable)
        {
            return new SparsePackageAssemblyProjectionOutcome
                .SelectedEntryUnavailable(cleanup);
        }
        foreach (ArtifactSetAdmissionFailure failure in rejected.Failures)
        {
            if (failure.Diagnostic.Code
                is "artifact.session.artifact-byte-limit")
            {
                return new SparsePackageAssemblyProjectionOutcome
                    .EntryByteLimitExceeded(cleanup);
            }
        }

        return new SparsePackageAssemblyProjectionOutcome
            .ArtifactPublicationFailed(
                [.. rejected.Failures],
                cleanup);
    }

    static async ValueTask<SparsePackageProjectionCleanupReceipt?>
        CleanupSparseProjectionAsync(
            PackageAssemblyContextRoles? roles,
            ArtifactQueryLease? queryLease,
            ArtifactSetSession session,
            Exception? primary)
    {
        var stages =
            ImmutableArray.CreateBuilder<
                SparsePackageProjectionCleanupEvidence>();
        AddStage(
            stages,
            SparsePackageProjectionCleanupStage.Participant,
            TryDispose(roles));
        AddStage(
            stages,
            SparsePackageProjectionCleanupStage.QueryLease,
            TryDispose(queryLease));

        int sessionFailures = 0;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            sessionFailures++;
        }
        sessionFailures += session.CleanupFailures.Count;
        if (primary is not null)
        {
            sessionFailures +=
                ArtifactSetSession.GetCleanupFailures(primary).Count;
        }
        AddStage(
            stages,
            SparsePackageProjectionCleanupStage.ArtifactSession,
            sessionFailures);
        if (stages.Count == 0)
            return null;

        var receipt = new SparsePackageProjectionCleanupReceipt(
            stages.ToImmutable());
        if (primary is not null)
            receipt.AttachTo(primary);
        return receipt;
    }

    static void AddStage(
        ImmutableArray<SparsePackageProjectionCleanupEvidence>.Builder stages,
        SparsePackageProjectionCleanupStage stage,
        int failureCount)
    {
        if (failureCount > 0)
            stages.Add(new(stage, failureCount));
    }

    static int TryDispose(IDisposable? resource)
    {
        if (resource is null)
            return 0;

        try
        {
            resource.Dispose();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    sealed class SelectedEntryOpenState
    {
        internal bool EntryUnavailable { get; set; }
    }

    sealed class SelectedPackageEntryUnavailableException : IOException
    {
        internal SelectedPackageEntryUnavailableException()
            : base("The selected package entry is unavailable.")
        {
        }
    }
}
