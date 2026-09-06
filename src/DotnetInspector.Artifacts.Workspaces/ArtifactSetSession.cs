using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DotnetInspector.Artifacts.Workspaces;

public sealed record ArtifactSetSessionLimits
{
    public const int DefaultMaxArtifacts = 1024;
    public const long DefaultMaxArtifactBytes = 512L * 1024 * 1024;
    public const long DefaultMaxRetainedBytes = 512L * 1024 * 1024;

    public int MaxArtifacts { get; init; } = DefaultMaxArtifacts;
    public long MaxArtifactBytes { get; init; } =
        DefaultMaxArtifactBytes;
    public long MaxRetainedBytes { get; init; } =
        DefaultMaxRetainedBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxArtifacts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxArtifactBytes,
            int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxRetainedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxRetainedBytes,
            int.MaxValue);
    }
}

/// <summary>
/// Owner-issued positive capacity for one supplemental acquisition call.
/// </summary>
public sealed class SupplementalAcquisitionCapacity
{
    internal SupplementalAcquisitionCapacity(
        int maxArtifacts,
        long maxArtifactBytes,
        long maxRetainedBytes)
    {
        MaxArtifacts = maxArtifacts;
        MaxArtifactBytes = maxArtifactBytes;
        MaxRetainedBytes = maxRetainedBytes;
    }

    public int MaxArtifacts { get; }
    public long MaxArtifactBytes { get; }
    public long MaxRetainedBytes { get; }
}

public enum ArtifactSetAdmissionFailureKind
{
    Unavailable,
    Rejected,
    Failed,
}

public sealed record ArtifactSetAdmissionFailure(
    ArtifactSetAdmissionFailureKind Kind,
    IArtifactAcquisitionDiagnostic Diagnostic);

public abstract class ArtifactSetPublicationOutcome
{
    private protected ArtifactSetPublicationOutcome()
    {
    }

    public sealed class Published : ArtifactSetPublicationOutcome
    {
        internal Published()
        {
        }
    }

    public sealed class NotPublished : ArtifactSetPublicationOutcome
    {
        internal NotPublished(
            IReadOnlyList<ArtifactSetAdmissionFailure> failures,
            IReadOnlyList<Exception> cleanupFailures)
        {
            Failures = failures;
            CleanupFailures = cleanupFailures;
        }

        public IReadOnlyList<ArtifactSetAdmissionFailure> Failures { get; }
        public IReadOnlyList<Exception> CleanupFailures { get; }
    }
}

/// <summary>
/// One source-neutral artifact generation with a construction phase and an
/// immutable published phase.
/// </summary>
/// <remarks>
/// The session invokes source adapters sequentially, materializes every
/// contribution into owner-private bounded memory, and retains successful
/// acquisition leases until disposal. This slice does not yet implement
/// workspace-wide reservation or single-flight admission.
/// Materialization, publication, mutation, open, and lease-lifetime behavior
/// are gated by <c>ArtifactSetSession_SealingRequiresMaterializedBoundedContent</c>,
/// <c>ArtifactSetSession_SealedGenerationCannotMutate</c>,
/// <c>ArtifactOpen_RejectsContentSubstitutionAfterAdmission</c>, and
/// <c>ArtifactSetSession_DisposesEveryContributingLease</c>. Awaited disposal
/// interleaving is gated by
/// <c>ArtifactSetSession_DisposalDuringAcquisitionDisposesLateLease</c>, while
/// seal exclusion is gated by
/// <c>ArtifactSetSession_SealRejectsAcquisitionInProgress</c> and
/// <c>ArtifactSetSession_DisposalDuringSealCannotPublish</c>. Owner-held
/// content release is gated by
/// <c>ArtifactSetSession_DisposalReleasesOwnerHeldState</c>. Concurrent
/// termination completion is gated by
/// <c>ArtifactSetSession_ConcurrentTerminationWaitsForCleanup</c> and
/// <c>ArtifactSetSession_ConcurrentAbortAndDisposalShareCleanup</c>.
/// Content-access quiescence is gated by
/// <c>ArtifactSetSession_ReleasesLeasesOnlyAfterOpenArtifactStreamsQuiesce</c>
/// and <c>ArtifactSetSession_DisposalCancelsInFlightMaterialization</c>.
/// </remarks>
public sealed class ArtifactSetSession : IAsyncDisposable
{
    private const string CleanupFailuresKey =
        "DotnetInspector.Artifacts.Workspaces.CleanupFailures";

    private readonly object _gate = new();
    private readonly ArtifactGenerationAuthority _authority = new();
    private readonly ArtifactAdmissionAuthorization _admission;
    private readonly ArtifactAdmissionLease _admissionLease;
    private readonly ArtifactSetSessionLimits _limits;
    private readonly List<AcquiredBatch> _acquired = [];
    private readonly List<PublishedArtifact> _prepared = [];
    private readonly HashSet<ArtifactIdentity> _preparedIdentities =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<ArtifactSetAdmissionFailure> _failures = [];
    private readonly HashSet<IArtifactAcquisitionLease> _leases =
        new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ArtifactDescriptor>? _catalog;
    private Dictionary<ArtifactIdentity, PublishedArtifact>? _artifacts;
    private IReadOnlyList<Exception> _cleanupFailures = [];
    private Task<IReadOnlyList<Exception>>? _terminationTask;
    private SupplementalOperationOrder? _supplementalOperation;
    private SessionState _state;
    private RequiredCheckpointState _requiredCheckpoint;
    private long _eventSequence;
    private long _terminationSequence;
    private int _preparedArtifactCount;
    private long _preparedRetainedBytes;
    private bool _acquisitionInProgress;
    private bool _requiredPhaseClosed;

    public ArtifactSetSession(ArtifactSetSessionLimits? limits = null)
    {
        _limits = limits ?? new ArtifactSetSessionLimits();
        _limits.Validate();
        _admission = _authority.CreateAdmissionAuthorization();
        _admissionLease = _authority.IssueLease(_admission);
    }

    public ArtifactGenerationIdentity Generation => _authority.Generation;

    /// <summary>
    /// Gets cleanup failures observed while ending this session.
    /// </summary>
    /// <remarks>
    /// Disposal does not throw cleanup failures because doing so can replace a
    /// primary exception from an <c>await using</c> body. The failures remain
    /// visible here. This behavior is gated by
    /// <c>ArtifactSetSession_PreservesPrimaryFailureWhenCleanupFails</c>.
    /// </remarks>
    public IReadOnlyList<Exception> CleanupFailures
    {
        get
        {
            lock (_gate)
                return _cleanupFailures;
        }
    }

    public async ValueTask AddRequiredAcquisitionAsync(
        Func<
            ArtifactContributionScope,
            CancellationToken,
            ValueTask<ArtifactAcquisitionOutcome>> acquire,
        IReadOnlyCollection<ArtifactWorkspaceRole>? roles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        ArtifactWorkspaceRole[] roleSnapshot = SnapshotRoles(roles);

        lock (_gate)
        {
            EnsureConstructing();
            if (_acquisitionInProgress)
            {
                throw new InvalidOperationException(
                    "Another artifact acquisition is already in progress.");
            }
            if (_requiredPhaseClosed)
            {
                throw new InvalidOperationException(
                    "Required artifact acquisition is closed after supplemental acquisition begins.");
            }

            _acquisitionInProgress = true;
        }

        ArtifactAcquisitionOutcome outcome;
        ArtifactContributionScope scope =
            _authority.BeginContribution(_admission);
        try
        {
            outcome = await acquire(
                    scope,
                    cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(outcome);
        }
        catch (Exception ex)
        {
            scope.Dispose();
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }
        finally
        {
            scope.Dispose();
        }

        IArtifactAcquisitionLease? lateLease = null;
        lock (_gate)
        {
            _acquisitionInProgress = false;
            if (_state != SessionState.Constructing)
            {
                if (outcome is ArtifactAcquisitionOutcome.Acquired late)
                    lateLease = late.Lease;
            }
            else if (outcome is ArtifactAcquisitionOutcome.Acquired acquired)
            {
                _leases.Add(acquired.Lease);
                if (acquired.Artifacts.Count == 0)
                {
                    _failures.Add(
                        Failure(
                            ArtifactSetAdmissionFailureKind.Rejected,
                            "artifact.acquisition.empty",
                            "A required acquisition returned no artifacts."));
                    return;
                }

                foreach (ArtifactContribution contribution
                    in acquired.Artifacts)
                {
                    if (!scope.Owns(contribution))
                    {
                        _failures.Add(
                            Failure(
                                ArtifactSetAdmissionFailureKind.Failed,
                                "artifact.acquisition.foreign",
                                "A required acquisition returned an artifact from another contribution scope."));
                        return;
                    }
                }

                _acquired.Add(
                    new AcquiredBatch(
                        acquired.Artifacts,
                        roleSnapshot));
                return;
            }
            else
            {
                _failures.Add(Failure(outcome));
                return;
            }
        }

        var disposed = new ObjectDisposedException(
            nameof(ArtifactSetSession),
            "The artifact session was disposed while acquisition was in progress.");
        if (lateLease is not null)
        {
            try
            {
                await lateLease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                IReadOnlyList<Exception> cleanupFailures =
                    new ReadOnlyCollection<Exception>([ex]);
                RecordCleanupFailures(cleanupFailures);
                AttachCleanupFailures(disposed, cleanupFailures);
            }
        }

        throw disposed;
    }

    /// <summary>
    /// Adds one bounded optional source whose invoked outcome participates in
    /// session admission.
    /// </summary>
    public async ValueTask AddSupplementalAcquisitionAsync(
        Func<
            ArtifactContributionScope,
            SupplementalAcquisitionCapacity,
            CancellationToken,
            ValueTask<ArtifactAcquisitionOutcome>> acquire,
        IReadOnlyCollection<ArtifactWorkspaceRole>? roles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        ArtifactWorkspaceRole[] roleSnapshot = SnapshotRoles(roles);
        var operationOrder =
            new SupplementalOperationOrder(cancellationToken);
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(
                () => RecordCancellation(operationOrder));
        List<AcquiredBatch>? required = null;
        SupplementalAcquisitionCapacity? capacity = null;
        HashSet<ArtifactIdentity>? existingIdentities = null;

        lock (_gate)
        {
            EnsureConstructing();
            if (_acquisitionInProgress)
            {
                throw new InvalidOperationException(
                    "Another artifact acquisition is already in progress.");
            }

            _requiredPhaseClosed = true;
            if (_requiredCheckpoint == RequiredCheckpointState.Failed
                || _failures.Count > 0)
            {
                return;
            }

            _acquisitionInProgress = true;
            _supplementalOperation = operationOrder;
            if (_requiredCheckpoint == RequiredCheckpointState.NotStarted)
            {
                int requiredCount = _acquired.Sum(
                    static batch => batch.Artifacts.Count);
                if (requiredCount > _limits.MaxArtifacts)
                {
                    _failures.Add(
                        Failure(
                            ArtifactSetAdmissionFailureKind.Rejected,
                            "artifact.session.count-limit",
                            "The artifact count exceeds the session limit."));
                    _requiredCheckpoint = RequiredCheckpointState.Failed;
                    _acquisitionInProgress = false;
                    _supplementalOperation = null;
                    return;
                }

                if (requiredCount == 0)
                {
                    _requiredCheckpoint =
                        RequiredCheckpointState.Succeeded;
                    capacity = ResolveSupplementalCapacity();
                    if (capacity is null)
                    {
                        _acquisitionInProgress = false;
                        _supplementalOperation = null;
                    }
                    else
                        existingIdentities =
                            new HashSet<ArtifactIdentity>(
                                _preparedIdentities,
                                ReferenceEqualityComparer.Instance);
                }
                else
                {
                    required = [.. _acquired];
                }
            }
            else
            {
                capacity = ResolveSupplementalCapacity();
                if (capacity is null)
                {
                    _acquisitionInProgress = false;
                    _supplementalOperation = null;
                }
                else
                    existingIdentities =
                        new HashSet<ArtifactIdentity>(
                            _preparedIdentities,
                            ReferenceEqualityComparer.Instance);
            }
        }

        if (required is not null)
        {
            RequiredCheckpointResult checkpoint;
            try
            {
                checkpoint = await MaterializeRequiredCheckpointAsync(
                        required,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                bool wasDisposed = IsDisposed();
                IReadOnlyList<Exception> cleanupFailures =
                    await AbortAsync().ConfigureAwait(false);
                if ((ex is OperationCanceledException
                        && wasDisposed
                        && TerminationPrecededCancellation(
                            operationOrder))
                    || (ex is not OperationCanceledException
                        && wasDisposed))
                {
                    ObjectDisposedException lateDisposed =
                        SupplementalDisposedException();
                    AttachCleanupFailures(
                        lateDisposed,
                        cleanupFailures);
                    throw lateDisposed;
                }

                AttachCleanupFailures(ex, cleanupFailures);
                throw;
            }

            bool disposed;
            lock (_gate)
            {
                disposed = _state == SessionState.Disposed;
                if (!disposed)
                {
                    if (checkpoint.Failure is not null)
                    {
                        _failures.Add(checkpoint.Failure);
                        _requiredCheckpoint =
                            RequiredCheckpointState.Failed;
                        _acquisitionInProgress = false;
                        _supplementalOperation = null;
                        return;
                    }

                    CommitPreparedBatch(checkpoint);
                    _acquired.Clear();
                    _requiredCheckpoint =
                        RequiredCheckpointState.Succeeded;
                    capacity = ResolveSupplementalCapacity();
                    if (capacity is null)
                    {
                        _acquisitionInProgress = false;
                        _supplementalOperation = null;
                    }
                    else
                        existingIdentities =
                            new HashSet<ArtifactIdentity>(
                                _preparedIdentities,
                                ReferenceEqualityComparer.Instance);
                }
                else
                {
                    _acquisitionInProgress = false;
                    _supplementalOperation = null;
                }
            }

            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ArtifactSetSession),
                    "The artifact session was disposed while required content was checkpointed.");
            }
        }

        if (capacity is null)
            return;

        ArtifactContributionScope scope;
        try
        {
            scope = _authority.BeginContribution(_admission);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _acquisitionInProgress = false;
                _supplementalOperation = null;
            }
            if (IsDisposed())
            {
                throw new ObjectDisposedException(
                    nameof(ArtifactSetSession),
                    "The artifact session was disposed before supplemental acquisition began.");
            }

            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }

        ArtifactAcquisitionOutcome outcome;
        try
        {
            outcome = await acquire(
                    scope,
                    capacity,
                    cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(outcome);
        }
        catch (Exception ex)
        {
            scope.Dispose();
            bool wasDisposed = IsDisposed();
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            if (ex is OperationCanceledException
                && wasDisposed
                && TerminationPrecededCancellation(operationOrder))
            {
                ObjectDisposedException disposed =
                    SupplementalDisposedException();
                AttachCleanupFailures(disposed, cleanupFailures);
                throw disposed;
            }

            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }
        finally
        {
            scope.Dispose();
        }

        if (outcome is not ArtifactAcquisitionOutcome.Acquired acquired)
        {
            ArtifactSetAdmissionFailure failure = Failure(outcome);
            ObjectDisposedException? disposed = null;
            lock (_gate)
            {
                _acquisitionInProgress = false;
                _supplementalOperation = null;
                if (_state == SessionState.Constructing)
                {
                    _failures.Add(failure);
                    return;
                }

                disposed = SupplementalDisposedException();
            }

            AttachAdmissionFailures(disposed, [failure]);
            throw disposed;
        }

        if (IsDisposed())
        {
            throw await CleanupLateAcquiredAsync(acquired.Lease)
                .ConfigureAwait(false);
        }

        if (acquired.Artifacts.Count == 0)
        {
            IReadOnlyList<Exception> cleanupFailures =
                await DisposeLeaseAsync(acquired.Lease)
                    .ConfigureAwait(false);
            RecordCleanupFailures(cleanupFailures);
            ObjectDisposedException? disposed = null;
            lock (_gate)
            {
                _acquisitionInProgress = false;
                _supplementalOperation = null;
                if (_state != SessionState.Constructing)
                    disposed = SupplementalDisposedException();
            }

            if (disposed is not null)
            {
                AttachCleanupFailures(disposed, cleanupFailures);
                throw disposed;
            }

            return;
        }

        ArtifactSetAdmissionFailure? validationFailure =
            ValidateSupplementalBatch(
                acquired.Artifacts,
                scope,
                capacity,
                existingIdentities!);
        if (validationFailure is not null)
        {
            await RejectSupplementalBatchAsync(
                    acquired.Lease,
                    validationFailure)
                .ConfigureAwait(false);
            return;
        }

        SupplementalMaterializationResult materialized;
        try
        {
            materialized = await MaterializeSupplementalBatchAsync(
                    acquired.Artifacts,
                    roleSnapshot,
                    capacity,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bool wasDisposed = IsDisposed();
            IReadOnlyList<Exception> lateCleanup =
                await DisposeLeaseAsync(acquired.Lease)
                    .ConfigureAwait(false);
            RecordCleanupFailures(lateCleanup);
            if ((ex is OperationCanceledException
                    && wasDisposed
                    && TerminationPrecededCancellation(
                        operationOrder))
                || (ex is not OperationCanceledException
                    && wasDisposed))
            {
                lock (_gate)
                {
                    _acquisitionInProgress = false;
                    _supplementalOperation = null;
                }
                ObjectDisposedException disposed =
                    SupplementalDisposedException();
                AttachCleanupFailures(disposed, lateCleanup);
                throw disposed;
            }

            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            IReadOnlyList<Exception> combined =
                [.. lateCleanup, .. cleanupFailures];
            AttachCleanupFailures(ex, combined);
            throw;
        }

        if (materialized.Failure is not null)
        {
            await RejectSupplementalBatchAsync(
                    acquired.Lease,
                    materialized.Failure)
                .ConfigureAwait(false);
            return;
        }

        bool late;
        lock (_gate)
        {
            late = _state != SessionState.Constructing;
            if (!late)
            {
                CommitPreparedBatch(materialized);
                _leases.Add(acquired.Lease);
                _acquisitionInProgress = false;
                _supplementalOperation = null;
                return;
            }
        }

        throw await CleanupLateAcquiredAsync(acquired.Lease)
            .ConfigureAwait(false);
    }

    public ValueTask<ArtifactSetPublicationOutcome> SealAsync(
        CancellationToken cancellationToken = default) =>
        SealCoreAsync(null, cancellationToken);

    /// <summary>
    /// Projects each retained artifact before publishing the catalog. A
    /// returned failure rejects the generation; exceptions propagate after
    /// cleanup. Projected facts are provisional until publication succeeds.
    /// </summary>
    public ValueTask<ArtifactSetPublicationOutcome> SealWithProjectionAsync(
        ArtifactAdmissionContentCallback<ArtifactSetAdmissionFailure?> project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return SealCoreAsync(project, cancellationToken);
    }

    private async ValueTask<ArtifactSetPublicationOutcome> SealCoreAsync(
        ArtifactAdmissionContentCallback<ArtifactSetAdmissionFailure?>? project,
        CancellationToken cancellationToken)
    {
        List<AcquiredBatch> acquired;
        List<PublishedArtifact> prepared;
        List<ArtifactSetAdmissionFailure> failures;
        bool usePrepared;
        lock (_gate)
        {
            EnsureConstructing();
            if (_acquisitionInProgress)
            {
                throw new InvalidOperationException(
                    "Artifact admission cannot seal while an acquisition is in progress.");
            }

            _state = SessionState.Sealing;
            acquired = [.. _acquired];
            prepared = [.. _prepared];
            failures = [.. _failures];
            usePrepared =
                _requiredCheckpoint == RequiredCheckpointState.Succeeded;
        }

        if (failures.Count > 0)
            return await RejectAsync(failures).ConfigureAwait(false);

        int artifactCount = usePrepared
            ? prepared.Count
            : acquired.Sum(static batch => batch.Artifacts.Count);
        if (artifactCount == 0)
        {
            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.session.empty",
                    "An artifact session requires at least one artifact."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }
        if (artifactCount > _limits.MaxArtifacts)
        {
            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.session.count-limit",
                    "The artifact count exceeds the session limit."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }

        Dictionary<ArtifactIdentity, PublishedArtifact> published;
        List<ArtifactDescriptor> descriptors;
        try
        {
            published =
                new Dictionary<ArtifactIdentity, PublishedArtifact>(
                    ReferenceEqualityComparer.Instance);
            descriptors =
                new List<ArtifactDescriptor>(artifactCount);
            var identities = new HashSet<ArtifactIdentity>(
                ReferenceEqualityComparer.Instance);
            long retainedBytes = 0;
            if (usePrepared)
            {
                foreach (PublishedArtifact artifact in prepared)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    retainedBytes = checked(
                        retainedBytes + artifact.RetainedBytes);
                    if (retainedBytes > _limits.MaxRetainedBytes)
                    {
                        failures.Add(
                            Failure(
                                ArtifactSetAdmissionFailureKind.Rejected,
                                "artifact.session.byte-limit",
                                "The retained artifact bytes exceed the session limit."));
                        return await RejectAsync(failures)
                            .ConfigureAwait(false);
                    }

                    if (!identities.Add(
                            artifact.Descriptor.Identity))
                    {
                        failures.Add(
                            Failure(
                                ArtifactSetAdmissionFailureKind.Failed,
                                "artifact.session.identity-collision",
                                "An artifact identity appeared more than once."));
                        return await RejectAsync(failures)
                            .ConfigureAwait(false);
                    }

                    published.Add(
                        artifact.Descriptor.Identity,
                        artifact);
                    descriptors.Add(artifact.Descriptor);
                }
            }
            else
            {
                foreach (AcquiredBatch batch in acquired)
                {
                    foreach (ArtifactContribution contribution
                        in batch.Artifacts)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        byte[] snapshot = await MaterializeAsync(
                                contribution,
                                _limits.MaxArtifactBytes,
                                cancellationToken)
                            .ConfigureAwait(false);
                        retainedBytes = checked(
                            retainedBytes + snapshot.LongLength);
                        if (retainedBytes > _limits.MaxRetainedBytes)
                        {
                            failures.Add(
                                Failure(
                                    ArtifactSetAdmissionFailureKind.Rejected,
                                    "artifact.session.byte-limit",
                                    "The retained artifact bytes exceed the session limit."));
                            return await RejectAsync(failures)
                                .ConfigureAwait(false);
                        }

                        if (!identities.Add(
                                contribution.Descriptor.Identity))
                        {
                            failures.Add(
                                Failure(
                                    ArtifactSetAdmissionFailureKind.Failed,
                                    "artifact.session.identity-collision",
                                    "An artifact identity appeared more than once."));
                            return await RejectAsync(failures)
                                .ConfigureAwait(false);
                        }

                        PublishedArtifact artifact =
                            CreatePreparedArtifact(
                                contribution,
                                batch.Roles,
                                snapshot);
                        published.Add(
                            contribution.Descriptor.Identity,
                            artifact);
                        descriptors.Add(contribution.Descriptor);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (IsDisposed())
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            var disposed = new ObjectDisposedException(
                nameof(ArtifactSetSession),
                "The artifact session was disposed while sealing was in progress.");
            AttachCleanupFailures(disposed, cleanupFailures);
            throw disposed;
        }
        catch (OperationCanceledException ex)
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }
        catch (ObjectDisposedException) when (IsDisposed())
        {
            throw;
        }
        catch (ArtifactMaterializationLimitException ex)
        {
            RecordMaterializationCleanupFailures(ex);
            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.session.artifact-byte-limit",
                    "An artifact exceeds the per-artifact byte limit."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsMaterializationFailure(ex))
        {
            if (IsDisposed())
            {
                throw new ObjectDisposedException(
                    nameof(ArtifactSetSession),
                    "The artifact session was disposed during publication.");
            }

            RecordMaterializationCleanupFailures(ex);
            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.session.materialization-failed",
                    "Artifact content could not be materialized for publication."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }

        // Consumer failures must not enter the materialization classifiers.
        try
        {
            if (project is not null)
            {
                foreach (ArtifactDescriptor descriptor in descriptors)
                {
                    ArtifactContentAccessOutcome<ArtifactSetAdmissionFailure?> outcome =
                        published[descriptor.Identity].Content.WithAdmissionContent(
                            _admissionLease,
                            project,
                            cancellationToken);
                    if (outcome is not
                        ArtifactContentAccessOutcome<ArtifactSetAdmissionFailure?>.Accessed accessed)
                    {
                        throw new ObjectDisposedException(
                            nameof(ArtifactSetSession),
                            "Admission ended before artifact projection.");
                    }
                    if (accessed.Value is ArtifactSetAdmissionFailure failure)
                    {
                        failures.Add(failure);
                        return await RejectAsync(failures).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }

        try
        {
            IReadOnlyList<ArtifactDescriptor> catalog =
                new ReadOnlyCollection<ArtifactDescriptor>(descriptors);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(
                    _state == SessionState.Disposed,
                    this);
                if (_state != SessionState.Sealing)
                {
                    throw new InvalidOperationException(
                        "Artifact publication requires an active sealing operation.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                _authority.CompleteAdmission(_admission);
                _artifacts = published;
                _catalog = catalog;
                _acquired.Clear();
                _prepared.Clear();
                _preparedIdentities.Clear();
                _preparedArtifactCount = 0;
                _preparedRetainedBytes = 0;
                _failures.Clear();
                _state = SessionState.Published;
            }

            return new ArtifactSetPublicationOutcome.Published();
        }
        catch (OperationCanceledException ex)
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }
        catch (ObjectDisposedException ex) when (IsDisposed())
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            AttachCleanupFailures(ex, cleanupFailures);
            throw;
        }
        catch (Exception ex) when (IsMaterializationFailure(ex))
        {
            if (IsDisposed())
            {
                throw new ObjectDisposedException(
                    nameof(ArtifactSetSession),
                    "The artifact session was disposed during publication.");
            }

            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.session.materialization-failed",
                    "Artifact content could not be materialized for publication."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }
    }

    public ArtifactQueryAuthorization CreateQueryAuthorization()
    {
        lock (_gate)
        {
            EnsurePublished();
            return _authority.CreateQueryAuthorization();
        }
    }

    public ArtifactQueryAuthorization ReplaceQueryAuthorization(
        ArtifactQueryAuthorization previous)
    {
        lock (_gate)
        {
            EnsurePublished();
            return _authority.ReplaceQueryAuthorization(previous);
        }
    }

    public void Revoke(ArtifactQueryAuthorization authorization)
    {
        lock (_gate)
        {
            EnsurePublished();
            _authority.Revoke(authorization);
        }
    }

    public ArtifactQueryLease IssueLease(
        ArtifactQueryAuthorization authorization)
    {
        lock (_gate)
        {
            EnsurePublished();
            return _authority.IssueLease(authorization);
        }
    }

    public IReadOnlyList<ArtifactDescriptor> GetCatalog(
        ArtifactQueryLease lease)
    {
        lock (_gate)
        {
            EnsurePublished();
            _authority.ValidateQueryLease(lease);
            return _catalog!;
        }
    }

    /// <summary>
    /// Projects one published artifact into an owner-bound content reference.
    /// </summary>
    public ArtifactContentReference GetContentReference(
        ArtifactIdentity identity,
        ArtifactQueryLease lease)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            EnsurePublished();
            _authority.ValidateQueryLease(lease);
            PublishedArtifact artifact = FindArtifact(identity);
            return new ArtifactContentReference(
                this,
                artifact.Descriptor,
                lease);
        }
    }

    internal ArtifactAcquisitionRegistration GetRegistration(
        ArtifactIdentity identity,
        ArtifactQueryLease lease)
    {
        lock (_gate)
        {
            EnsurePublished();
            _authority.ValidateQueryLease(lease);
            return FindArtifact(identity).Registration;
        }
    }

    public IArtifactProvenance GetProvenance(
        ArtifactIdentity identity,
        ArtifactQueryLease lease)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(lease);
        return GetRegistration(identity, lease).Provenance;
    }

    public bool HasRole(
        ArtifactIdentity identity,
        ArtifactWorkspaceRole role,
        ArtifactQueryLease lease)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(role);
        lock (_gate)
        {
            EnsurePublished();
            _authority.ValidateQueryLease(lease);
            return FindArtifact(identity).Roles.Contains(role);
        }
    }

    public Stream OpenRead(
        ArtifactIdentity identity,
        ArtifactQueryLease lease)
    {
        ArgumentNullException.ThrowIfNull(identity);
        RetainedArtifactContent retained;
        lock (_gate)
        {
            EnsurePublished();
            _authority.ValidateQueryLease(lease);
            retained = FindArtifact(identity).Content;
        }

        return retained.OpenRead(lease);
    }

    public ArtifactContentAccessOutcome<TResult> WithQueryContent<TResult>(
        ArtifactIdentity identity,
        ArtifactQueryLease? lease,
        ArtifactQueryContentCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        PublishedArtifact? artifact = FindQueryArtifact(identity, lease);
        if (artifact is null)
            return new ArtifactContentAccessOutcome<TResult>.Unauthorized();

        return artifact.Content.WithQueryContent(lease, callback, cancellationToken);
    }

    /// <summary>
    /// Gets a SHA-256 digest of retained content under current query authority.
    /// </summary>
    /// <param name="chargeWork">
    /// Charges the requesting operation for the snapshot's byte length before
    /// its first hash pass. Authorized cache hits do not invoke this callback.
    /// A callback exception prevents computation and propagates unchanged.
    /// </param>
    public ArtifactContentAccessOutcome<ArtifactContentDigest> GetContentDigest(
        ArtifactIdentity identity,
        ArtifactQueryLease? lease,
        Action<long> chargeWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(chargeWork);
        cancellationToken.ThrowIfCancellationRequested();
        PublishedArtifact? artifact = FindQueryArtifact(identity, lease);
        if (artifact is null)
            return new ArtifactContentAccessOutcome<ArtifactContentDigest>.Unauthorized();

        return artifact.Content.WithQueryContent(
            lease,
            (view, token) => artifact.GetDigest(view, chargeWork, token),
            cancellationToken);
    }

    private PublishedArtifact? FindQueryArtifact(
        ArtifactIdentity identity,
        ArtifactQueryLease? lease)
    {
        lock (_gate)
        {
            if (_state != SessionState.Published || lease is null)
                return null;
            try
            {
                _authority.ValidateQueryLease(lease);
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or ObjectDisposedException)
            {
                return null;
            }
            return FindArtifact(identity);
        }
    }

    /// <summary>
    /// Ends this generation and releases its owner-held content and acquisition
    /// leases.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await TerminateAsync().ConfigureAwait(false);
    }

    private SupplementalAcquisitionCapacity?
        ResolveSupplementalCapacity()
    {
        int maxArtifacts =
            _limits.MaxArtifacts - _preparedArtifactCount;
        long maxRetainedBytes =
            _limits.MaxRetainedBytes - _preparedRetainedBytes;
        if (maxArtifacts <= 0 || maxRetainedBytes <= 0)
        {
            _failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.supplemental.capacity-exhausted",
                    "No artifact count or retained-byte capacity remains for supplemental acquisition."));
            return null;
        }

        return new SupplementalAcquisitionCapacity(
            maxArtifacts,
            Math.Min(_limits.MaxArtifactBytes, maxRetainedBytes),
            maxRetainedBytes);
    }

    private async ValueTask<RequiredCheckpointResult>
        MaterializeRequiredCheckpointAsync(
            IReadOnlyList<AcquiredBatch> acquired,
            CancellationToken cancellationToken)
    {
        var prepared = new List<PublishedArtifact>();
        var identities = new HashSet<ArtifactIdentity>(
            ReferenceEqualityComparer.Instance);
        long retainedBytes = 0;
        try
        {
            foreach (AcquiredBatch batch in acquired)
            {
                foreach (ArtifactContribution contribution
                    in batch.Artifacts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] snapshot = await MaterializeAsync(
                            contribution,
                            _limits.MaxArtifactBytes,
                            cancellationToken)
                        .ConfigureAwait(false);
                    retainedBytes = checked(
                        retainedBytes + snapshot.LongLength);
                    if (retainedBytes > _limits.MaxRetainedBytes)
                    {
                        return new RequiredCheckpointResult(
                            [],
                            0,
                            Failure(
                                ArtifactSetAdmissionFailureKind.Rejected,
                                "artifact.session.byte-limit",
                                "The retained artifact bytes exceed the session limit."));
                    }

                    if (!identities.Add(
                            contribution.Descriptor.Identity))
                    {
                        return new RequiredCheckpointResult(
                            [],
                            0,
                            Failure(
                                ArtifactSetAdmissionFailureKind.Failed,
                                "artifact.session.identity-collision",
                                "An artifact identity appeared more than once."));
                    }

                    prepared.Add(
                        CreatePreparedArtifact(
                            contribution,
                            batch.Roles,
                            snapshot));
                }
            }
        }
        catch (ArtifactMaterializationLimitException ex)
        {
            RecordMaterializationCleanupFailures(ex);
            return new RequiredCheckpointResult(
                [],
                0,
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.session.artifact-byte-limit",
                    "An artifact exceeds the per-artifact byte limit."));
        }
        catch (Exception ex) when (
            !IsDisposed() && IsMaterializationFailure(ex))
        {
            RecordMaterializationCleanupFailures(ex);
            return new RequiredCheckpointResult(
                [],
                0,
                Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.session.materialization-failed",
                    "Artifact content could not be materialized for publication."));
        }

        return new RequiredCheckpointResult(
            prepared,
            retainedBytes,
            null);
    }

    private static ArtifactSetAdmissionFailure?
        ValidateSupplementalBatch(
            IReadOnlyList<ArtifactContribution> artifacts,
            ArtifactContributionScope scope,
            SupplementalAcquisitionCapacity capacity,
            HashSet<ArtifactIdentity> identities)
    {
        foreach (ArtifactContribution contribution in artifacts)
        {
            if (!scope.Owns(contribution))
            {
                return Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.supplemental.foreign",
                    "A supplemental acquisition returned an artifact from another contribution scope.");
            }
        }

        if (artifacts.Count > capacity.MaxArtifacts)
        {
            return Failure(
                ArtifactSetAdmissionFailureKind.Rejected,
                "artifact.supplemental.count-limit",
                "The supplemental artifact count exceeds the granted capacity.");
        }

        foreach (ArtifactContribution contribution in artifacts)
        {
            if (!identities.Add(
                    contribution.Descriptor.Identity))
            {
                return Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.supplemental.identity-collision",
                    "A supplemental artifact identity appeared more than once.");
            }
        }

        return null;
    }

    private async ValueTask<SupplementalMaterializationResult>
        MaterializeSupplementalBatchAsync(
            IReadOnlyList<ArtifactContribution> artifacts,
            IReadOnlyList<ArtifactWorkspaceRole> roles,
            SupplementalAcquisitionCapacity capacity,
            CancellationToken cancellationToken)
    {
        var prepared =
            new List<PublishedArtifact>(artifacts.Count);
        long retainedBytes = 0;
        try
        {
            foreach (ArtifactContribution contribution in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] snapshot = await MaterializeAsync(
                        contribution,
                        capacity.MaxArtifactBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                retainedBytes = checked(
                    retainedBytes + snapshot.LongLength);
                if (retainedBytes > capacity.MaxRetainedBytes)
                {
                    return new SupplementalMaterializationResult(
                        [],
                        0,
                        Failure(
                            ArtifactSetAdmissionFailureKind.Rejected,
                            "artifact.supplemental.byte-limit",
                            "The supplemental retained bytes exceed the granted capacity."));
                }

                prepared.Add(
                    CreatePreparedArtifact(
                        contribution,
                        roles,
                        snapshot));
            }
        }
        catch (ArtifactMaterializationLimitException ex)
        {
            RecordMaterializationCleanupFailures(ex);
            return new SupplementalMaterializationResult(
                [],
                0,
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.supplemental.artifact-byte-limit",
                    "A supplemental artifact exceeds the granted per-artifact byte limit."));
        }
        catch (Exception ex) when (
            !IsDisposed() && IsMaterializationFailure(ex))
        {
            RecordMaterializationCleanupFailures(ex);
            return new SupplementalMaterializationResult(
                [],
                0,
                Failure(
                    ArtifactSetAdmissionFailureKind.Failed,
                    "artifact.supplemental.materialization-failed",
                    "Supplemental artifact content could not be materialized."));
        }

        return new SupplementalMaterializationResult(
            prepared,
            retainedBytes,
            null);
    }

    private PublishedArtifact CreatePreparedArtifact(
        ArtifactContribution contribution,
        IReadOnlyList<ArtifactWorkspaceRole> roles,
        byte[] snapshot)
    {
        RetainedArtifactContent retained =
            _authority.CreateRetainedContent(
                contribution.Registration,
                ImmutableCollectionsMarshal.AsImmutableArray(snapshot));
        return new PublishedArtifact(
            contribution.Descriptor,
            contribution.Registration,
            retained,
            new HashSet<ArtifactWorkspaceRole>(
                roles,
                ReferenceEqualityComparer.Instance),
            snapshot.LongLength);
    }

    private void CommitPreparedBatch(
        RequiredCheckpointResult result) =>
        CommitPreparedBatch(
            result.Artifacts,
            result.RetainedBytes);

    private void CommitPreparedBatch(
        SupplementalMaterializationResult result) =>
        CommitPreparedBatch(
            result.Artifacts,
            result.RetainedBytes);

    private void CommitPreparedBatch(
        IReadOnlyList<PublishedArtifact> artifacts,
        long retainedBytes)
    {
        _prepared.AddRange(artifacts);
        foreach (PublishedArtifact artifact in artifacts)
            _preparedIdentities.Add(artifact.Descriptor.Identity);
        _preparedArtifactCount += artifacts.Count;
        _preparedRetainedBytes = checked(
            _preparedRetainedBytes + retainedBytes);
    }

    private async ValueTask RejectSupplementalBatchAsync(
        IArtifactAcquisitionLease lease,
        ArtifactSetAdmissionFailure failure)
    {
        IReadOnlyList<Exception> cleanupFailures =
            await DisposeLeaseAsync(lease).ConfigureAwait(false);
        RecordCleanupFailures(cleanupFailures);
        ObjectDisposedException? disposed = null;
        lock (_gate)
        {
            _acquisitionInProgress = false;
            _supplementalOperation = null;
            if (_state == SessionState.Constructing)
            {
                _failures.Add(failure);
                return;
            }

            disposed = SupplementalDisposedException();
        }

        AttachCleanupFailures(disposed, cleanupFailures);
        throw disposed;
    }

    private async ValueTask<ObjectDisposedException>
        CleanupLateAcquiredAsync(
            IArtifactAcquisitionLease lease)
    {
        IReadOnlyList<Exception> cleanupFailures =
            await DisposeLeaseAsync(lease).ConfigureAwait(false);
        RecordCleanupFailures(cleanupFailures);
        lock (_gate)
        {
            _acquisitionInProgress = false;
            _supplementalOperation = null;
        }
        ObjectDisposedException disposed =
            SupplementalDisposedException();
        AttachCleanupFailures(disposed, cleanupFailures);
        return disposed;
    }

    private static async ValueTask<IReadOnlyList<Exception>>
        DisposeLeaseAsync(IArtifactAcquisitionLease lease)
    {
        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            return [];
        }
        catch (Exception ex)
        {
            return new ReadOnlyCollection<Exception>([ex]);
        }
    }

    private static bool IsMaterializationFailure(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or InvalidOperationException
            or OverflowException;

    /// <summary>
    /// Attaches cleanup evidence to a primary exception, merging with any this
    /// owner already attached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cleanup evidence is always secondary and additive. Callers outside this
    /// assembly use this entry rather than writing the key directly, because a
    /// raw assignment would replace evidence an earlier stage attached to the
    /// same exception — for example a throwing stream disposal recorded during
    /// materialization, which must survive a later release sweep over the same
    /// failure.
    /// </para>
    /// <para>
    /// Read it back with <see cref="GetCleanupFailures"/>. Gated by
    /// <c>ArtifactSetSessionCleanupEvidenceTests.AttachedCleanupEvidenceMergesRatherThanReplaces</c>.
    /// </para>
    /// </remarks>
    public static void AttachCleanupFailures(
        Exception primary,
        IReadOnlyList<Exception> cleanupFailures)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(cleanupFailures);
        if (cleanupFailures.Count == 0)
            return;

        primary.Data[CleanupFailuresKey] =
            primary.Data[CleanupFailuresKey]
                is IReadOnlyList<Exception> existing
                ? new ReadOnlyCollection<Exception>(
                    [.. existing, .. cleanupFailures])
                : cleanupFailures;
    }

    /// <summary>
    /// Reads the cleanup failures this owner attached to a primary exception.
    /// </summary>
    /// <remarks>
    /// Cleanup evidence is always secondary: a cancelled or failed
    /// materialization keeps its own exception and token, and a throwing
    /// stream disposal is reported here instead of replacing it. Gated by
    /// <c>ArtifactSetSessionCleanupEvidenceTests.DisposalFailureDoesNotReplaceCancelledRead</c>.
    /// </remarks>
    public static IReadOnlyList<Exception> GetCleanupFailures(
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Data[CleanupFailuresKey]
            as IReadOnlyList<Exception>
            ?? [];
    }

    private void RecordMaterializationCleanupFailures(Exception failure) =>
        RecordCleanupFailures(GetCleanupFailures(failure));

    private static void AttachAdmissionFailures(
        Exception primary,
        IReadOnlyList<ArtifactSetAdmissionFailure> admissionFailures)
    {
        if (admissionFailures.Count > 0)
        {
            primary.Data[
                "DotnetInspector.Artifacts.Workspaces.AdmissionFailures"] =
                admissionFailures;
        }
    }

    private static ObjectDisposedException
        SupplementalDisposedException() =>
        new(
            nameof(ArtifactSetSession),
            "The artifact session was disposed while supplemental acquisition was in progress.");

    private async ValueTask<byte[]> MaterializeAsync(
        ArtifactContribution contribution,
        long maxArtifactBytes,
        CancellationToken cancellationToken)
    {
        Stream stream = contribution.OpenRead(_admissionLease);
        Exception? primary = null;
        try
        {
            return await CopyBoundedAsync(
                    stream,
                    maxArtifactBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            try
            {
                stream.Dispose();
            }
            // A throwing disposal must not replace the cancellation or read
            // failure that is already being reported; it stays secondary
            // evidence on that exact exception.
            catch (Exception disposalFailure) when (primary is not null)
            {
                AttachCleanupFailures(primary, [disposalFailure]);
            }
        }
    }

    private static async ValueTask<byte[]> CopyBoundedAsync(
        Stream stream,
        long maxArtifactBytes,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek
            && checked(stream.Length - stream.Position)
                > maxArtifactBytes)
        {
            throw new ArtifactMaterializationLimitException();
        }

        using var destination = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(
                    buffer,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            if (destination.Length + read
                > maxArtifactBytes)
            {
                throw new ArtifactMaterializationLimitException();
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private async ValueTask<ArtifactSetPublicationOutcome>
        RejectAsync(List<ArtifactSetAdmissionFailure> failures)
    {
        await AbortAsync().ConfigureAwait(false);
        return new ArtifactSetPublicationOutcome.NotPublished(
            new ReadOnlyCollection<ArtifactSetAdmissionFailure>(
                [.. failures]),
            CleanupFailures);
    }

    private async ValueTask<IReadOnlyList<Exception>> AbortAsync()
    {
        return await TerminateAsync().ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<Exception>> TerminateAsync()
    {
        TaskCompletionSource<IReadOnlyList<Exception>>? starter = null;
        Task<IReadOnlyList<Exception>> termination;
        lock (_gate)
        {
            if (_terminationTask is null)
            {
                starter =
                    new(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);
                _terminationTask = starter.Task;
                if (_supplementalOperation is
                    SupplementalOperationOrder operation
                    && operation.CancellationSequence == 0
                    && operation.CancellationToken
                        .IsCancellationRequested)
                {
                    operation.CancellationSequence =
                        checked(++_eventSequence);
                }
                _terminationSequence = checked(++_eventSequence);
                _supplementalOperation = null;
                _state = SessionState.Disposed;
                _catalog = null;
                _artifacts = null;
                _acquired.Clear();
                _prepared.Clear();
                _preparedIdentities.Clear();
                _preparedArtifactCount = 0;
                _preparedRetainedBytes = 0;
                _failures.Clear();
            }

            termination = _terminationTask;
        }

        if (starter is not null)
        {
            try
            {
                var failures = new List<Exception>();
                try
                {
                    await _authority.EndGenerationAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
                _admissionLease.Dispose();
                IReadOnlyList<Exception> leaseFailures =
                    await DisposeLeasesAsync().ConfigureAwait(false);
                failures.AddRange(leaseFailures);
                IReadOnlyList<Exception> cleanupFailures =
                    failures.Count == 0
                        ? []
                        : new ReadOnlyCollection<Exception>(
                            failures);
                RecordCleanupFailures(cleanupFailures);
                starter.SetResult(cleanupFailures);
            }
            catch (Exception ex)
            {
                starter.SetException(ex);
            }
        }

        return await termination.ConfigureAwait(false);
    }

    private void RecordCleanupFailures(
        IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0)
            return;

        lock (_gate)
        {
            _cleanupFailures =
                new ReadOnlyCollection<Exception>(
                    [.. _cleanupFailures, .. failures]);
        }
    }

    private async ValueTask<IReadOnlyList<Exception>>
        DisposeLeasesAsync()
    {
        IArtifactAcquisitionLease[] leases = [.. _leases];
        _leases.Clear();
        var failures = new List<Exception>();
        for (int index = leases.Length - 1; index >= 0; index--)
        {
            try
            {
                await leases[index].DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return new ReadOnlyCollection<Exception>(failures);
    }

    private PublishedArtifact FindArtifact(ArtifactIdentity identity)
    {
        if (!_artifacts!.TryGetValue(identity, out PublishedArtifact? artifact))
        {
            throw new KeyNotFoundException(
                "The artifact identity is not part of this session.");
        }

        return artifact;
    }

    private void EnsureConstructing()
    {
        if (_state != SessionState.Constructing)
        {
            throw new InvalidOperationException(
                "Artifact acquisitions can be added only while the session is under construction.");
        }
    }

    private void EnsurePublished()
    {
        ObjectDisposedException.ThrowIf(
            _state == SessionState.Disposed,
            this);
        if (_state != SessionState.Published)
        {
            throw new InvalidOperationException(
                "Artifact content is available only after the session is published.");
        }
    }

    private bool IsDisposed()
    {
        lock (_gate)
            return _state == SessionState.Disposed;
    }

    private void RecordCancellation(
        SupplementalOperationOrder operation)
    {
        lock (_gate)
        {
            if (operation.CancellationSequence == 0)
            {
                operation.CancellationSequence =
                    checked(++_eventSequence);
            }
        }
    }

    private bool TerminationPrecededCancellation(
        SupplementalOperationOrder operation)
    {
        lock (_gate)
        {
            return _terminationSequence != 0
                && (operation.CancellationSequence == 0
                    || _terminationSequence
                        < operation.CancellationSequence);
        }
    }

    private static ArtifactWorkspaceRole[] SnapshotRoles(
        IReadOnlyCollection<ArtifactWorkspaceRole>? roles)
    {
        if (roles is null)
            return [];

        ArtifactWorkspaceRole[] snapshot = [.. roles];
        if (snapshot.Any(static role => role is null))
        {
            throw new ArgumentException(
                "Artifact workspace roles cannot contain null.",
                nameof(roles));
        }

        var distinct = new HashSet<ArtifactWorkspaceRole>(
            ReferenceEqualityComparer.Instance);
        distinct.UnionWith(snapshot);
        return [.. distinct];
    }

    private static ArtifactSetAdmissionFailure Failure(
        ArtifactAcquisitionOutcome outcome) =>
        outcome switch
        {
            ArtifactAcquisitionOutcome.Unavailable unavailable =>
                new(
                    ArtifactSetAdmissionFailureKind.Unavailable,
                    unavailable.Diagnostic),
            ArtifactAcquisitionOutcome.Rejected rejected =>
                new(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    rejected.Diagnostic),
            ArtifactAcquisitionOutcome.Failed failed =>
                new(
                    ArtifactSetAdmissionFailureKind.Failed,
                    failed.Diagnostic),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "The acquisition outcome is not a failure."),
        };

    private static ArtifactSetAdmissionFailure Failure(
        ArtifactSetAdmissionFailureKind kind,
        string code,
        string summary) =>
        new(kind, new SessionDiagnostic(code, summary));

    private enum SessionState
    {
        Constructing,
        Sealing,
        Published,
        Disposed,
    }

    private enum RequiredCheckpointState
    {
        NotStarted,
        Succeeded,
        Failed,
    }

    private sealed record AcquiredBatch(
        IReadOnlyList<ArtifactContribution> Artifacts,
        IReadOnlyList<ArtifactWorkspaceRole> Roles);

    private sealed record RequiredCheckpointResult(
        IReadOnlyList<PublishedArtifact> Artifacts,
        long RetainedBytes,
        ArtifactSetAdmissionFailure? Failure);

    private sealed record SupplementalMaterializationResult(
        IReadOnlyList<PublishedArtifact> Artifacts,
        long RetainedBytes,
        ArtifactSetAdmissionFailure? Failure);

    private sealed class SupplementalOperationOrder(
        CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } =
            cancellationToken;
        public long CancellationSequence { get; set; }
    }

    private sealed record PublishedArtifact(
        ArtifactDescriptor Descriptor,
        ArtifactAcquisitionRegistration Registration,
        RetainedArtifactContent Content,
        HashSet<ArtifactWorkspaceRole> Roles,
        long RetainedBytes)
    {
        private readonly object _digestGate = new();
        private ArtifactContentDigest? _digest;

        public ArtifactContentDigest GetDigest(
            scoped ArtifactQueryContentView view,
            Action<long> chargeWork,
            CancellationToken cancellationToken)
        {
            lock (_digestGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_digest is not null)
                    return _digest;

                chargeWork(view.Content.Length);
                string hexValue = Convert.ToHexStringLower(
                    SHA256.HashData(view.Content));
                _digest = new ArtifactContentDigest(view.Artifact, hexValue);
                return _digest;
            }
        }
    }

    private sealed record SessionDiagnostic(
        string Code,
        string Summary) :
        IArtifactAcquisitionDiagnostic;

    private sealed class ArtifactMaterializationLimitException :
        IOException;
}
