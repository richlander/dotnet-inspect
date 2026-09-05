using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace DotnetInspector.Artifacts;

/// <summary>
/// Owner-issued capability for guarded artifact content access.
/// </summary>
public interface IArtifactAccessLease : IDisposable
{
}

/// <summary>Authorization for admission-time artifact access.</summary>
public sealed class ArtifactAdmissionAuthorization : ArtifactAuthorization
{
    internal ArtifactAdmissionAuthorization(
        ArtifactGenerationAuthority authority)
        : base(authority)
    {
    }
}

/// <summary>Authorization for query-time artifact access.</summary>
public sealed class ArtifactQueryAuthorization : ArtifactAuthorization
{
    internal ArtifactQueryAuthorization(
        ArtifactGenerationAuthority authority)
        : base(authority)
    {
    }
}

/// <summary>An admission-scoped artifact access capability.</summary>
public sealed class ArtifactAdmissionLease : ArtifactAccessLease
{
    internal ArtifactAdmissionLease(
        ArtifactAdmissionAuthorization authorization)
        : base(authorization)
    {
    }
}

/// <summary>A query-scoped artifact access capability.</summary>
public sealed class ArtifactQueryLease : ArtifactAccessLease
{
    internal ArtifactQueryLease(
        ArtifactQueryAuthorization authorization)
        : base(authorization)
    {
    }
}

/// <summary>
/// A narrow capability supplied to one source adapter while it contributes
/// artifacts to an admission generation.
/// </summary>
public sealed class ArtifactContributionScope : IDisposable
{
    private readonly ArtifactGenerationAuthority _authority;
    private readonly ArtifactAdmissionAuthorization _authorization;
    private int _disposed;

    internal ArtifactContributionScope(
        ArtifactGenerationAuthority authority,
        ArtifactAdmissionAuthorization authorization)
    {
        _authority = authority;
        _authorization = authorization;
    }

    /// <summary>Registers one source contribution in the owning generation.</summary>
    /// <remarks>
    /// The opener runs only after its access is registered. A potentially
    /// blocking opener must promptly observe the supplied generation-end
    /// cancellation token without depending on a worker thread. The token is
    /// scoped to the callback and is detached before a returned stream escapes;
    /// it does not represent the returned stream's lifetime.
    /// </remarks>
    public ArtifactContribution Register(
        IArtifactProvenance provenance,
        Func<CancellationToken, Stream> openRead,
        string? mediaType = null,
        string? kind = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(openRead);

        return _authority.RegisterContribution(
            this,
            _authorization,
            provenance,
            openRead,
            mediaType,
            kind);
    }

    /// <summary>
    /// Reports whether this scope minted the supplied contribution.
    /// </summary>
    public bool Owns(ArtifactContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return ReferenceEquals(contribution.Scope, this);
    }

    internal void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _authority.CloseContributionScope(this);
    }
}

/// <summary>
/// One adapter contribution. Only the admission authorization that created its
/// scope can open the source content.
/// </summary>
public sealed class ArtifactContribution
{
    private readonly ArtifactGenerationAuthority _authority;
    private readonly ArtifactAdmissionAuthorization _authorization;
    private readonly Func<CancellationToken, Stream> _openRead;

    internal ArtifactContribution(
        ArtifactGenerationAuthority authority,
        ArtifactAdmissionAuthorization authorization,
        ArtifactContributionScope scope,
        ArtifactDescriptor descriptor,
        ArtifactAcquisitionRegistration registration,
        Func<CancellationToken, Stream> openRead)
    {
        _authority = authority;
        _authorization = authorization;
        Scope = scope;
        Descriptor = descriptor;
        Registration = registration;
        _openRead = openRead;
    }

    internal ArtifactContributionScope Scope { get; }
    public ArtifactDescriptor Descriptor { get; }
    public ArtifactAcquisitionRegistration Registration { get; }

    /// <summary>Opens source content during the active admission.</summary>
    public Stream OpenRead(ArtifactAdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return _authority.OpenContribution(
            lease,
            _authorization,
            _openRead);
    }
}

/// <summary>
/// Guarded access to content already materialized into an owner-retained
/// immutable snapshot.
/// </summary>
/// <remarks>
/// Snapshot materialization is unverified until
/// <c>ArtifactSetSession_SealingRequiresMaterializedBoundedContent</c>.
/// Lease disposal or authorization revocation rejects new opens; a stream
/// already returned remains valid until its consumer disposes it. This access
/// behavior is gated by
/// <c>RetainedContent_RejectsRevokedOrForeignAuthorizationWithoutRevokingOpenStream</c>
/// and <c>ArtifactAccess_ReturnedStreamKeepsGenerationAliveUntilDisposed</c>.
/// </remarks>
public sealed class RetainedArtifactContent
{
    private readonly ArtifactGenerationAuthority _authority;
    private readonly Func<CancellationToken, Stream> _openRead;
    private readonly ImmutableArray<byte> _snapshot;

    internal RetainedArtifactContent(
        ArtifactGenerationAuthority authority,
        ArtifactAcquisitionRegistration registration,
        Func<CancellationToken, Stream> openRead,
        ImmutableArray<byte> snapshot)
    {
        _authority = authority;
        Registration = registration;
        _openRead = openRead;
        _snapshot = snapshot;
    }

    public ArtifactAcquisitionRegistration Registration { get; }

    public Stream OpenRead(IArtifactAccessLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease is not ArtifactAccessLease access)
        {
            throw new UnauthorizedAccessException(
                "The artifact access lease was not issued by this owner.");
        }

        return _authority.OpenRetained(
            access,
            _openRead);
    }

    public ArtifactContentAccessOutcome<TResult> WithAdmissionContent<TResult>(
        ArtifactAdmissionLease? lease,
        ArtifactAdmissionContentCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        using ArtifactGenerationAuthority.ArtifactContentAccess? access =
            _authority.TryBeginScopedAccess(lease);
        if (access is null)
            return new ArtifactContentAccessOutcome<TResult>.Unauthorized();

        EnsureSnapshot();
        TResult result = callback(
            new ArtifactAdmissionContentView(
                Registration.Artifact,
                _snapshot.AsSpan()),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new ArtifactContentAccessOutcome<TResult>.Accessed(result);
    }

    public ArtifactContentAccessOutcome<TResult> WithQueryContent<TResult>(
        ArtifactQueryLease? lease,
        ArtifactQueryContentCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        using ArtifactGenerationAuthority.ArtifactContentAccess? access =
            _authority.TryBeginScopedAccess(lease);
        if (access is null)
            return new ArtifactContentAccessOutcome<TResult>.Unauthorized();

        EnsureSnapshot();
        TResult result = callback(
            new ArtifactQueryContentView(
                Registration.Artifact,
                _snapshot.AsSpan()),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new ArtifactContentAccessOutcome<TResult>.Accessed(result);
    }

    private void EnsureSnapshot()
    {
        if (_snapshot.IsDefault)
        {
            throw new InvalidOperationException(
                "Scoped byte access requires owner-retained immutable content, not a compatibility stream opener.");
        }
    }
}

/// <summary>
/// Owner-held authority for one artifact generation.
/// </summary>
/// <remarks>
/// The authority is thread-safe. Adapters receive only an
/// <see cref="ArtifactContributionScope"/>, never this owner capability.
/// Thread-safe issuance is gated by
/// <c>GenerationAuthority_ConcurrentScopesMintUniqueOrderedIdentities</c>.
/// </remarks>
public sealed class ArtifactGenerationAuthority
{
    private readonly object _gate = new();
    private readonly HashSet<ArtifactAuthorization> _authorizations =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ArtifactAcquisitionRegistration, bool>
        _registrations =
            new(ReferenceEqualityComparer.Instance);
    private readonly CancellationTokenSource _endCancellation = new();
    private readonly TaskCompletionSource<Exception?>
        _endCancellationCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _accessQuiescence;
    private ArtifactAdmissionAuthorization? _admission;
    private long _nextOrdinal;
    private int _activeAccesses;
    private int _activeContributionScopes;
    private bool _admissionCompleted;
    private int _ended;

    public ArtifactGenerationAuthority()
    {
        Generation = new ArtifactGenerationIdentity();
    }

    public ArtifactGenerationIdentity Generation { get; }

    public ArtifactAdmissionAuthorization CreateAdmissionAuthorization()
    {
        lock (_gate)
        {
            ThrowIfEnded();
            if (_admission is not null)
            {
                throw new InvalidOperationException(
                    "This artifact generation already has an admission authorization.");
            }

            _admission = new ArtifactAdmissionAuthorization(this);
            _authorizations.Add(_admission);
            return _admission;
        }
    }

    public ArtifactContributionScope BeginContribution(
        ArtifactAdmissionAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_gate)
        {
            EnsureAdmissionActive(authorization);
            _activeContributionScopes++;
            return new ArtifactContributionScope(this, authorization);
        }
    }

    public ArtifactAdmissionLease IssueLease(
        ArtifactAdmissionAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_gate)
        {
            EnsureAdmissionActive(authorization);
            return new ArtifactAdmissionLease(authorization);
        }
    }

    /// <summary>
    /// Registers owner-retained content for one admitted artifact.
    /// </summary>
    /// <remarks>
    /// The opener runs only after its access is registered. A potentially
    /// blocking opener must promptly observe the supplied generation-end
    /// cancellation token without depending on a worker thread. The token is
    /// scoped to the callback and is detached before a returned stream escapes;
    /// it does not represent the returned stream's lifetime.
    /// </remarks>
    public RetainedArtifactContent CreateRetainedContent(
        ArtifactAcquisitionRegistration registration,
        Func<CancellationToken, Stream> openRead) =>
        CreateRetainedContentCore(registration, openRead, default);

    /// <summary>
    /// Retains one immutable snapshot for both scoped byte and stream access.
    /// </summary>
    /// <remarks>
    /// The owner must relinquish any mutable alias before supplying the image.
    /// The immutable array is retained without making another full-image copy.
    /// </remarks>
    public RetainedArtifactContent CreateRetainedContent(
        ArtifactAcquisitionRegistration registration,
        ImmutableArray<byte> snapshot)
    {
        if (snapshot.IsDefault)
            throw new ArgumentException("A snapshot is required.", nameof(snapshot));

        return CreateRetainedContentCore(
            registration,
            _ => new MemoryStream(
                ImmutableCollectionsMarshal.AsArray(snapshot)!,
                index: 0,
                count: snapshot.Length,
                writable: false,
                publiclyVisible: false),
            snapshot);
    }

    private RetainedArtifactContent CreateRetainedContentCore(
        ArtifactAcquisitionRegistration registration,
        Func<CancellationToken, Stream> openRead,
        ImmutableArray<byte> snapshot)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(openRead);
        lock (_gate)
        {
            ThrowIfEnded();
            if (_admission is null || _admissionCompleted)
            {
                throw new InvalidOperationException(
                    "Retained content can be created only during admission.");
            }

            EnsureOwned(registration);
            if (!_registrations.TryGetValue(
                    registration,
                    out bool retained))
            {
                throw new ArgumentException(
                    "The artifact registration was not minted by this authority.",
                    nameof(registration));
            }
            if (retained)
            {
                throw new InvalidOperationException(
                    "Retained content already exists for this artifact registration.");
            }

            var content = new RetainedArtifactContent(
                this,
                registration,
                openRead,
                snapshot);
            _registrations[registration] = true;
            return content;
        }
    }

    /// <summary>
    /// Atomically closes contribution and admission access after successful
    /// publication.
    /// </summary>
    public void CompleteAdmission(
        ArtifactAdmissionAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_gate)
        {
            EnsureAdmissionActive(authorization);
            if (_activeContributionScopes != 0)
            {
                throw new InvalidOperationException(
                    "Admission cannot complete while a contribution scope is active.");
            }
            if (_registrations.Values.Any(static retained => !retained))
            {
                throw new InvalidOperationException(
                    "Admission cannot complete until every registered artifact has retained content.");
            }

            _admissionCompleted = true;
            authorization.Revoke();
            _authorizations.Remove(authorization);
        }
    }

    public ArtifactQueryAuthorization CreateQueryAuthorization()
    {
        lock (_gate)
        {
            EnsureQueryPhase();
            var authorization = new ArtifactQueryAuthorization(this);
            _authorizations.Add(authorization);
            return authorization;
        }
    }

    /// <summary>
    /// Atomically revokes one query authorization and replaces it with a new
    /// policy snapshot.
    /// </summary>
    public ArtifactQueryAuthorization ReplaceQueryAuthorization(
        ArtifactQueryAuthorization previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        lock (_gate)
        {
            EnsureQueryPhase();
            EnsureOwned(previous);
            previous.ThrowIfRevoked();
            previous.Revoke();
            _authorizations.Remove(previous);

            var replacement = new ArtifactQueryAuthorization(this);
            _authorizations.Add(replacement);
            return replacement;
        }
    }

    public ArtifactQueryLease IssueLease(
        ArtifactQueryAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_gate)
        {
            EnsureQueryPhase();
            EnsureOwned(authorization);
            authorization.ThrowIfRevoked();
            return new ArtifactQueryLease(authorization);
        }
    }

    public void Revoke(ArtifactQueryAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        lock (_gate)
        {
            EnsureOwned(authorization);
            authorization.Revoke();
            _authorizations.Remove(authorization);
        }
    }

    /// <summary>Ends the generation and rejects every future open or mint.</summary>
    public void EndGeneration()
    {
        BeginEndGeneration(out bool started);
        if (!started)
            return;
        Exception? cancellationFailure =
            _endCancellationCompletion.Task
                .GetAwaiter()
                .GetResult();
        if (cancellationFailure is not null)
            throw cancellationFailure;
    }

    /// <summary>
    /// Ends the generation and waits until every admitted content access
    /// completes.
    /// </summary>
    /// <remarks>
    /// Already-returned query streams remain valid and keep this operation
    /// incomplete until their consumers dispose them.
    /// </remarks>
    public async ValueTask EndGenerationAsync()
    {
        Task quiescence = BeginEndGeneration(out _);
        Exception? cancellationFailure =
            await _endCancellationCompletion.Task
                .ConfigureAwait(false);
        await quiescence.ConfigureAwait(false);
        if (cancellationFailure is not null)
            throw cancellationFailure;
    }

    private Task BeginEndGeneration(out bool started)
    {
        bool cancel = false;
        Task quiescence;
        lock (_gate)
        {
            if (Volatile.Read(ref _ended) == 0)
            {
                Volatile.Write(ref _ended, 1);
                foreach (ArtifactAuthorization authorization
                    in _authorizations)
                {
                    authorization.Revoke();
                }
                _authorizations.Clear();
                _registrations.Clear();
                cancel = true;
            }

            quiescence =
                _accessQuiescence?.Task
                ?? Task.CompletedTask;
        }

        if (cancel)
        {
            Exception? cancellationFailure = null;
            try
            {
                _endCancellation.Cancel();
            }
            catch (Exception ex)
            {
                cancellationFailure = ex;
            }
            _endCancellationCompletion.TrySetResult(
                cancellationFailure);
        }
        started = cancel;
        return quiescence;
    }

    /// <summary>
    /// Validates that a query lease is current for this generation.
    /// </summary>
    public void ValidateQueryLease(ArtifactQueryLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            EnsureQueryPhase();
            lease.EnsureAccess(
                this,
                expectedAuthorization: null);
        }
    }

    internal ArtifactContribution RegisterContribution(
        ArtifactContributionScope scope,
        ArtifactAdmissionAuthorization authorization,
        IArtifactProvenance provenance,
        Func<CancellationToken, Stream> openRead,
        string? mediaType,
        string? kind)
    {
        lock (_gate)
        {
            EnsureAdmissionActive(authorization);
            scope.ThrowIfDisposed();

            long ordinal = _nextOrdinal;
            _nextOrdinal = checked(_nextOrdinal + 1);
            var identity = new ArtifactIdentity(this, ordinal);
            var registration =
                new ArtifactAcquisitionRegistration(identity, provenance);
            var descriptor =
                new ArtifactDescriptor(identity, mediaType, kind);
            _registrations.Add(registration, false);
            return new ArtifactContribution(
                this,
                authorization,
                scope,
                descriptor,
                registration,
                openRead);
        }
    }

    internal void CloseContributionScope(
        ArtifactContributionScope scope)
    {
        lock (_gate)
        {
            if (_activeContributionScopes > 0)
                _activeContributionScopes--;
        }
    }

    internal Stream OpenContribution(
        ArtifactAdmissionLease lease,
        ArtifactAdmissionAuthorization authorization,
        Func<CancellationToken, Stream> openRead)
    {
        ArtifactContentAccess access =
            BeginAccess(
                lease,
                authorization,
                cancelReads: true);
        return OpenReadable(openRead, access);
    }

    internal Stream OpenRetained(
        ArtifactAccessLease lease,
        Func<CancellationToken, Stream> openRead)
    {
        ArtifactContentAccess access =
            BeginAccess(
                lease,
                expectedAuthorization: null,
                cancelReads: false);
        return OpenReadable(openRead, access);
    }

    internal ArtifactContentAccess? TryBeginScopedAccess(
        ArtifactAccessLease? lease)
    {
        if (lease is null)
            return null;

        // Only admission failure is translated. Consumer code runs after this
        // catch and keeps its own exception type and identity.
        try
        {
            return BeginAccess(
                lease,
                expectedAuthorization: null,
                cancelReads: false);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or ObjectDisposedException)
        {
            return null;
        }
    }

    private ArtifactContentAccess BeginAccess(
        ArtifactAccessLease lease,
        ArtifactAuthorization? expectedAuthorization,
        bool cancelReads)
    {
        lock (_gate)
        {
            lease.EnsureAccess(this, expectedAuthorization);
            if (_activeAccesses == 0)
            {
                _accessQuiescence =
                    new(
                        TaskCreationOptions
                            .RunContinuationsAsynchronously);
            }
            _activeAccesses =
                checked(_activeAccesses + 1);
            return new ArtifactContentAccess(
                this,
                _endCancellation.Token,
                cancelReads);
        }
    }

    private Stream OpenReadable(
        Func<CancellationToken, Stream> openRead,
        ArtifactContentAccess access)
    {
        CancellationTokenSource openerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                access.CancellationToken);
        bool openingFinished = false;
        Stream? stream = null;
        try
        {
            stream = openRead(openerCancellation.Token);
            if (stream is null || !stream.CanRead)
            {
                throw new IOException(
                    "The artifact opener did not return a readable stream.");
            }

            if (!TryCompleteOpening(openerCancellation))
            {
                openingFinished = true;
                throw new OperationCanceledException(
                    access.CancellationToken);
            }
            openingFinished = true;
            return new ArtifactAccessStream(stream, access);
        }
        catch
        {
            if (!openingFinished)
                AbandonOpening(openerCancellation);
            try
            {
                stream?.Dispose();
            }
            finally
            {
                access.Dispose();
            }
            throw;
        }
    }

    private bool TryCompleteOpening(
        CancellationTokenSource openerCancellation)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _ended) != 0)
                return false;

            openerCancellation.Dispose();
            return true;
        }
    }

    private void AbandonOpening(
        CancellationTokenSource openerCancellation)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _ended) == 0
                || _endCancellationCompletion.Task.IsCompleted)
            {
                openerCancellation.Dispose();
            }
        }
    }

    internal void CompleteAccess()
    {
        TaskCompletionSource? completion = null;
        lock (_gate)
        {
            if (_activeAccesses <= 0)
            {
                throw new InvalidOperationException(
                    "Artifact content access completion was unbalanced.");
            }

            _activeAccesses--;
            if (_activeAccesses == 0)
                completion = _accessQuiescence;
        }

        completion?.TrySetResult();
    }

    internal void DisposeLease(ArtifactAccessLease lease)
    {
        lock (_gate)
            lease.MarkDisposed();
    }

    private void EnsureAdmissionActive(
        ArtifactAdmissionAuthorization authorization)
    {
        ThrowIfEnded();
        EnsureOwned(authorization);
        if (!ReferenceEquals(_admission, authorization)
            || _admissionCompleted)
        {
            throw new InvalidOperationException(
                "The admission authorization is not current.");
        }

        authorization.ThrowIfRevoked();
    }

    private void EnsureQueryPhase()
    {
        ThrowIfEnded();
        if (!_admissionCompleted)
        {
            throw new InvalidOperationException(
                "Query authorization begins only after admission completes.");
        }
    }

    private void EnsureOwned(
        ArtifactAcquisitionRegistration registration)
    {
        if (!ReferenceEquals(registration.Authority, this))
        {
            throw new ArgumentException(
                "The artifact registration belongs to another generation.",
                nameof(registration));
        }
    }

    private void EnsureOwned(ArtifactAuthorization authorization)
    {
        if (!ReferenceEquals(authorization.Authority, this))
        {
            throw new ArgumentException(
                "The authorization belongs to another generation.",
                nameof(authorization));
        }
    }

    private void ThrowIfEnded() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _ended) != 0,
            this);

    internal void ThrowIfAccessEnded()
    {
        if (Volatile.Read(ref _ended) != 0)
        {
            throw new UnauthorizedAccessException(
                "The artifact generation has ended.");
        }
    }

    internal sealed class ArtifactContentAccess : IDisposable
    {
        private readonly ArtifactGenerationAuthority _authority;
        private int _disposed;

        internal ArtifactContentAccess(
            ArtifactGenerationAuthority authority,
            CancellationToken cancellationToken,
            bool cancelReads)
        {
            _authority = authority;
            CancellationToken = cancellationToken;
            CancelReads = cancelReads;
        }

        internal CancellationToken CancellationToken { get; }
        internal bool CancelReads { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _authority.CompleteAccess();
        }
    }

    internal sealed class ArtifactAccessStream(
        Stream inner,
        ArtifactContentAccess access) : Stream
    {
        private int _disposeStarted;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanTimeout => inner.CanTimeout;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }
        public override int ReadTimeout
        {
            get => inner.ReadTimeout;
            set => inner.ReadTimeout = value;
        }
        public override int WriteTimeout
        {
            get => inner.WriteTimeout;
            set => inner.WriteTimeout = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(
            CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer);

        public override int ReadByte() => inner.ReadByte();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(
                buffer.AsMemory(offset, count),
                cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!access.CancelReads)
                return inner.ReadAsync(buffer, cancellationToken);
            return ReadWithOwnerCancellationAsync(
                buffer,
                cancellationToken);
        }

        private async ValueTask<int> ReadWithOwnerCancellationAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            CancellationToken ownerToken = access.CancellationToken;
            if (!cancellationToken.CanBeCanceled)
            {
                int read = await inner.ReadAsync(buffer, ownerToken)
                    .ConfigureAwait(false);
                ownerToken.ThrowIfCancellationRequested();
                return read;
            }

            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    ownerToken);
            try
            {
                int read = await inner.ReadAsync(buffer, linked.Token)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ownerToken.ThrowIfCancellationRequested();
                return read;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (OperationCanceledException)
                when (ownerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ownerToken);
            }
        }

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Write(buffer, offset, count);

        public override void Write(
            ReadOnlySpan<byte> buffer) =>
            inner.Write(buffer);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing
                && Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            {
                try
                {
                    inner.Dispose();
                }
                finally
                {
                    access.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            {
                try
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    access.Dispose();
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}

public abstract class ArtifactAuthorization
{
    private int _revoked;

    private protected ArtifactAuthorization(
        ArtifactGenerationAuthority authority)
    {
        Authority = authority;
        Generation = authority.Generation;
    }

    internal ArtifactGenerationAuthority Authority { get; }
    public ArtifactGenerationIdentity Generation { get; }

    internal void Revoke() =>
        Interlocked.Exchange(ref _revoked, 1);

    internal void ThrowIfRevoked()
    {
        if (Volatile.Read(ref _revoked) != 0)
        {
            throw new UnauthorizedAccessException(
                "The artifact authorization is no longer current.");
        }
    }
}

public abstract class ArtifactAccessLease : IArtifactAccessLease
{
    private int _disposed;

    private protected ArtifactAccessLease(
        ArtifactAuthorization authorization)
    {
        Authorization = authorization;
    }

    private ArtifactAuthorization Authorization { get; }

    internal void EnsureAccess(
        ArtifactGenerationAuthority authority,
        ArtifactAuthorization? expectedAuthorization)
    {
        if (!ReferenceEquals(Authorization.Authority, authority))
        {
            throw new UnauthorizedAccessException(
                "The artifact access lease belongs to another generation.");
        }

        authority.ThrowIfAccessEnded();
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        Authorization.ThrowIfRevoked();
        if (expectedAuthorization is not null
            && !ReferenceEquals(
                Authorization,
                expectedAuthorization))
        {
            throw new UnauthorizedAccessException(
                "The artifact access lease belongs to another authorization.");
        }
    }

    internal void MarkDisposed() =>
        Interlocked.Exchange(ref _disposed, 1);

    public void Dispose() =>
        Authorization.Authority.DisposeLease(this);
}
