using System.Collections.Immutable;
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

    internal ArtifactContentLease(
        ArtifactSetSession owner,
        ArtifactContentReference reference,
        ImmutableArray<byte> snapshot)
    {
        _owner = owner;
        Reference = reference;
        _snapshot = snapshot;
    }

    public ArtifactContentReference Reference { get; }
    public ArtifactIdentity Artifact => Reference.Descriptor.Identity;
    public ArtifactGenerationIdentity Generation => Artifact.Generation;

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
    /// Computes or reuses the digest for this lease's exact retained content.
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

    internal void MarkReleased()
    {
        _snapshot = default;
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
    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact => Reference.Descriptor.Identity;
    public ReadOnlySpan<byte> Content { get; }
}

public delegate TResult ArtifactContentCallback<TResult>(
    scoped ArtifactContentView view,
    CancellationToken cancellationToken);
