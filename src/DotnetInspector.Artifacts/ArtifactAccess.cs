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
    public ArtifactContribution Register(
        IArtifactProvenance provenance,
        Func<Stream> openRead,
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
    private readonly Func<Stream> _openRead;

    internal ArtifactContribution(
        ArtifactGenerationAuthority authority,
        ArtifactAdmissionAuthorization authorization,
        ArtifactDescriptor descriptor,
        ArtifactAcquisitionRegistration registration,
        Func<Stream> openRead)
    {
        _authority = authority;
        _authorization = authorization;
        Descriptor = descriptor;
        Registration = registration;
        _openRead = openRead;
    }

    public ArtifactDescriptor Descriptor { get; }
    public ArtifactAcquisitionRegistration Registration { get; }

    /// <summary>Opens source content during the active admission.</summary>
    public Stream OpenRead(ArtifactAdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lease.EnsureAccess(_authority, _authorization);
        return OpenReadable(_openRead);
    }

    internal static Stream OpenReadable(Func<Stream> openRead)
    {
        Stream? stream = openRead();
        if (stream is not null && stream.CanRead)
            return stream;

        stream?.Dispose();
        throw new IOException(
            "The artifact opener did not return a readable stream.");
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
/// <c>RetainedContent_RejectsRevokedOrForeignAuthorizationWithoutRevokingOpenStream</c>.
/// </remarks>
public sealed class RetainedArtifactContent
{
    private readonly ArtifactGenerationAuthority _authority;
    private readonly Func<Stream> _openRead;

    internal RetainedArtifactContent(
        ArtifactGenerationAuthority authority,
        ArtifactAcquisitionRegistration registration,
        Func<Stream> openRead)
    {
        _authority = authority;
        Registration = registration;
        _openRead = openRead;
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

        access.EnsureAccess(_authority);
        return ArtifactContribution.OpenReadable(_openRead);
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
    private ArtifactAdmissionAuthorization? _admission;
    private long _nextOrdinal;
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

    public RetainedArtifactContent CreateRetainedContent(
        ArtifactAcquisitionRegistration registration,
        Func<Stream> openRead)
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
            return new RetainedArtifactContent(
                this,
                registration,
                openRead);
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

            _admissionCompleted = true;
            authorization.Revoke();
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
        }
    }

    /// <summary>Ends the generation and rejects every future open or mint.</summary>
    public void EndGeneration()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _ended) != 0)
                return;

            Volatile.Write(ref _ended, 1);
            foreach (ArtifactAuthorization authorization in _authorizations)
                authorization.Revoke();
        }
    }

    internal ArtifactContribution RegisterContribution(
        ArtifactContributionScope scope,
        ArtifactAdmissionAuthorization authorization,
        IArtifactProvenance provenance,
        Func<Stream> openRead,
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
            return new ArtifactContribution(
                this,
                authorization,
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
        ArtifactGenerationAuthority authority)
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
    }

    internal void EnsureAccess(
        ArtifactGenerationAuthority authority,
        ArtifactAuthorization authorization)
    {
        EnsureAccess(authority);
        if (!ReferenceEquals(Authorization, authorization))
        {
            throw new UnauthorizedAccessException(
                "The artifact access lease belongs to another authorization.");
        }
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _disposed, 1);
}
