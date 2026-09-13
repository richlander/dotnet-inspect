using System.Collections.Immutable;
using System.Security.Cryptography;
using Inspector.Resources;

namespace Inspector.Artifacts.Workspaces;

/// <summary>
/// Artifact-issued ownership of continued access to one exact retained content
/// item.
/// </summary>
[ResourceOwnership]
public sealed class ArtifactContentLease : IDisposable
{
    private ArtifactSetSession? _owner;
    private ImmutableArray<byte> _snapshot;
    private ArtifactContentDigestCache? _digestCache;

    internal ArtifactContentLease(
        ArtifactSetSession owner,
        ArtifactContentReference reference,
        ImmutableArray<byte> snapshot,
        ArtifactContentDigestCache digestCache)
    {
        _owner = owner;
        Reference = reference;
        _snapshot = snapshot;
        _digestCache = digestCache;
    }

    public ArtifactContentReference Reference { get; }
    public ArtifactIdentity Artifact => Reference.Artifact;
    public ArtifactGenerationIdentity Generation => Reference.Generation;

    /// <summary>
    /// Borrows the exact retained bytes synchronously.
    /// </summary>
    public ArtifactContentAccessOutcome<TResult> WithContent<TResult>(
        ArtifactContentCallback<TResult> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArtifactSetSession owner =
            Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(
                nameof(ArtifactContentLease));
        return owner.WithContent(
            this,
            callback,
            cancellationToken);
    }

    /// <summary>
    /// Gets the Artifact-owned SHA-256 digest under this content authority.
    /// </summary>
    public ArtifactContentAccessOutcome<ArtifactContentDigest> GetContentDigest(
        Action<long> chargeWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chargeWork);
        ArtifactSetSession owner =
            Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(
                nameof(ArtifactContentLease));
        return owner.GetContentDigest(
            this,
            chargeWork,
            cancellationToken);
    }

    public void Dispose()
    {
        ArtifactSetSession? owner = Volatile.Read(ref _owner);
        owner?.ReleaseContentLease(this);
    }

    internal bool IsOwnedBy(ArtifactSetSession owner) =>
        ReferenceEquals(Volatile.Read(ref _owner), owner);

    internal ImmutableArray<byte> Snapshot => _snapshot;
    internal int ActiveBorrows { get; set; }

    internal ArtifactContentDigest GetContentDigest(
        scoped ArtifactContentView view,
        Action<long> chargeWork,
        CancellationToken cancellationToken) =>
        _digestCache!.GetDigest(
            view.Content,
            chargeWork,
            cancellationToken);

    internal void MarkReleased()
    {
        _snapshot = default;
        _digestCache = null;
        Volatile.Write(ref _owner, null);
    }
}

/// <summary>
/// Owner-attested retained bytes borrowed through one exact content lease.
/// </summary>
public readonly ref struct ArtifactContentView
{
    internal ArtifactContentView(
        ArtifactContentReference reference,
        ReadOnlySpan<byte> content)
    {
        Reference = reference;
        Content = content;
    }

    public ArtifactContentReference Reference { get; }
    public ArtifactGenerationIdentity Generation => Reference.Generation;
    public ArtifactIdentity Artifact => Reference.Artifact;
    public ReadOnlySpan<byte> Content { get; }
}

public delegate TResult ArtifactContentCallback<TResult>(
    scoped ArtifactContentView view,
    CancellationToken cancellationToken);

internal sealed class ArtifactContentDigestCache(
    ArtifactIdentity artifact)
{
    private readonly object _gate = new();
    private ArtifactContentDigest? _digest;

    internal ArtifactContentDigest GetDigest(
        scoped ReadOnlySpan<byte> content,
        Action<long> chargeWork,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_digest is not null)
                return _digest;

            chargeWork(content.Length);
            string hexValue = Convert.ToHexStringLower(
                SHA256.HashData(content));
            _digest = new ArtifactContentDigest(
                artifact,
                hexValue);
            return _digest;
        }
    }
}
