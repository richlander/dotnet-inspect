using System.Collections.ObjectModel;

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
/// workspace-wide reservation, single-flight admission, or dependent-group
/// quiescence.
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
/// </remarks>
public sealed class ArtifactSetSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ArtifactGenerationAuthority _authority = new();
    private readonly ArtifactAdmissionAuthorization _admission;
    private readonly ArtifactAdmissionLease _admissionLease;
    private readonly ArtifactSetSessionLimits _limits;
    private readonly List<AcquiredBatch> _acquired = [];
    private readonly List<ArtifactSetAdmissionFailure> _failures = [];
    private readonly HashSet<IArtifactAcquisitionLease> _leases =
        new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ArtifactDescriptor>? _catalog;
    private Dictionary<ArtifactIdentity, PublishedArtifact>? _artifacts;
    private IReadOnlyList<Exception> _cleanupFailures = [];
    private Task<IReadOnlyList<Exception>>? _terminationTask;
    private SessionState _state;
    private bool _acquisitionInProgress;

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
            if (cleanupFailures.Count > 0)
            {
                ex.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                    cleanupFailures;
            }

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
                disposed.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                    cleanupFailures;
            }
        }

        throw disposed;
    }

    public async ValueTask<ArtifactSetPublicationOutcome> SealAsync(
        CancellationToken cancellationToken = default)
    {
        List<AcquiredBatch> acquired;
        List<ArtifactSetAdmissionFailure> failures;
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
            failures = [.. _failures];
        }

        if (failures.Count > 0)
            return await RejectAsync(failures).ConfigureAwait(false);

        int artifactCount = acquired.Sum(
            static batch => batch.Artifacts.Count);
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

        try
        {
            var published =
                new Dictionary<ArtifactIdentity, PublishedArtifact>(
                    ReferenceEqualityComparer.Instance);
            var descriptors =
                new List<ArtifactDescriptor>(artifactCount);
            long retainedBytes = 0;
            foreach (AcquiredBatch batch in acquired)
            {
                foreach (ArtifactContribution contribution
                    in batch.Artifacts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] snapshot = await MaterializeAsync(
                            contribution,
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

                    RetainedArtifactContent retained =
                        _authority.CreateRetainedContent(
                            contribution.Registration,
                            () => OpenSnapshot(snapshot));
                    var artifact = new PublishedArtifact(
                        contribution.Descriptor,
                        contribution.Registration,
                        retained,
                        new HashSet<ArtifactWorkspaceRole>(
                            batch.Roles,
                            ReferenceEqualityComparer.Instance));
                    if (!published.TryAdd(
                            contribution.Descriptor.Identity,
                            artifact))
                    {
                        failures.Add(
                            Failure(
                                ArtifactSetAdmissionFailureKind.Failed,
                                "artifact.session.identity-collision",
                                "An artifact identity appeared more than once."));
                        return await RejectAsync(failures)
                            .ConfigureAwait(false);
                    }

                    descriptors.Add(contribution.Descriptor);
                }
            }

            IReadOnlyList<ArtifactDescriptor> catalog =
                new ReadOnlyCollection<ArtifactDescriptor>(
                    descriptors);

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

                _authority.CompleteAdmission(_admission);
                _artifacts = published;
                _catalog = catalog;
                _acquired.Clear();
                _failures.Clear();
                _state = SessionState.Published;
            }

            return new ArtifactSetPublicationOutcome.Published();
        }
        catch (OperationCanceledException ex)
        {
            IReadOnlyList<Exception> cleanupFailures =
                await AbortAsync().ConfigureAwait(false);
            if (cleanupFailures.Count > 0)
            {
                ex.Data[
                    "DotnetInspector.Artifacts.Workspaces.CleanupFailures"] =
                    cleanupFailures;
            }

            throw;
        }
        catch (ObjectDisposedException) when (IsDisposed())
        {
            throw;
        }
        catch (ArtifactMaterializationLimitException)
        {
            failures.Add(
                Failure(
                    ArtifactSetAdmissionFailureKind.Rejected,
                    "artifact.session.artifact-byte-limit",
                    "An artifact exceeds the per-artifact byte limit."));
            return await RejectAsync(failures).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or InvalidOperationException
                or OverflowException)
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

    /// <summary>
    /// Ends this generation and releases its owner-held content and acquisition
    /// leases.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await TerminateAsync().ConfigureAwait(false);
    }

    private async ValueTask<byte[]> MaterializeAsync(
        ArtifactContribution contribution,
        CancellationToken cancellationToken)
    {
        using Stream stream = contribution.OpenRead(_admissionLease);
        if (stream.CanSeek
            && checked(stream.Length - stream.Position)
                > _limits.MaxArtifactBytes)
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
                > _limits.MaxArtifactBytes)
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
        IReadOnlyList<Exception> cleanupFailures =
            await AbortAsync().ConfigureAwait(false);
        return new ArtifactSetPublicationOutcome.NotPublished(
            new ReadOnlyCollection<ArtifactSetAdmissionFailure>(
                [.. failures]),
            cleanupFailures);
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
                _state = SessionState.Disposed;
                _catalog = null;
                _artifacts = null;
                _acquired.Clear();
                _failures.Clear();
            }

            termination = _terminationTask;
        }

        if (starter is not null)
        {
            try
            {
                _authority.EndGeneration();
                _admissionLease.Dispose();
                IReadOnlyList<Exception> failures =
                    await DisposeLeasesAsync().ConfigureAwait(false);
                RecordCleanupFailures(failures);
                starter.SetResult(failures);
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

    private static MemoryStream OpenSnapshot(byte[] snapshot) =>
        new(
            snapshot,
            index: 0,
            count: snapshot.Length,
            writable: false,
            publiclyVisible: false);

    private enum SessionState
    {
        Constructing,
        Sealing,
        Published,
        Disposed,
    }

    private sealed record AcquiredBatch(
        IReadOnlyList<ArtifactContribution> Artifacts,
        IReadOnlyList<ArtifactWorkspaceRole> Roles);

    private sealed record PublishedArtifact(
        ArtifactDescriptor Descriptor,
        ArtifactAcquisitionRegistration Registration,
        RetainedArtifactContent Content,
        HashSet<ArtifactWorkspaceRole> Roles);

    private sealed record SessionDiagnostic(
        string Code,
        string Summary) :
        IArtifactAcquisitionDiagnostic;

    private sealed class ArtifactMaterializationLimitException :
        IOException;
}
