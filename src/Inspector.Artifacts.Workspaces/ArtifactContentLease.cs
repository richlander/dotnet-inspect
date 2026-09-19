using System.Collections.Immutable;
using System.Runtime.InteropServices;
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
    /// Borrows the exact retained bytes synchronously with caller-supplied
    /// scoped state.
    /// </summary>
    public ArtifactContentAccessOutcome<TResult>
        WithContent<TState, TResult>(
        scoped TState state,
        ArtifactContentCallback<TState, TResult> callback,
        CancellationToken cancellationToken = default)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArtifactSetSession owner =
            Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(
                nameof(ArtifactContentLease));
        return owner.WithContent(
            this,
            state,
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
        ImmutableArray<byte> content)
    {
        Reference = reference;
        _content = content;
    }

    private readonly ImmutableArray<byte> _content;

    public ArtifactContentReference Reference { get; }
    public ArtifactGenerationIdentity Generation => Reference.Generation;
    public ArtifactIdentity Artifact => Reference.Artifact;
    public ReadOnlySpan<byte> Content => _content.AsSpan();

    /// <summary>
    /// Uses a zero-copy seekable stream only for the synchronous callback.
    /// </summary>
    /// <remarks>
    /// The stream is disposed and drops the retained image before this method
    /// returns. The callback result must be detached or independently owned.
    /// </remarks>
    public TResult UseReadStream<TResult>(
        Func<Stream, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        using var stream = new ScopedContentReadStream(_content);
        return callback(stream);
    }
}

public delegate TResult ArtifactContentCallback<TResult>(
    scoped ArtifactContentView view,
    CancellationToken cancellationToken);

public delegate TResult ArtifactContentCallback<TState, TResult>(
    scoped ArtifactContentView view,
    scoped TState state,
    CancellationToken cancellationToken)
    where TState : allows ref struct;

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

internal sealed class ScopedContentReadStream(
    ImmutableArray<byte> content) : Stream
{
    private MemoryStream? _inner = new(
        ImmutableCollectionsMarshal.AsArray(content)!,
        index: 0,
        count: content.Length,
        writable: false,
        publiclyVisible: false);

    private MemoryStream Inner =>
        Volatile.Read(ref _inner)
        ?? throw new ObjectDisposedException(
            nameof(ScopedContentReadStream));

    public override bool CanRead => Volatile.Read(ref _inner)?.CanRead == true;
    public override bool CanSeek => Volatile.Read(ref _inner)?.CanSeek == true;
    public override bool CanWrite => false;
    public override long Length => Inner.Length;

    public override long Position
    {
        get => Inner.Position;
        set => Inner.Position = value;
    }

    public override void Flush() => Inner.Flush();

    public override int Read(
        byte[] buffer,
        int offset,
        int count) =>
        Inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) =>
        Inner.Read(buffer);

    public override int ReadByte() => Inner.ReadByte();

    public override long Seek(
        long offset,
        SeekOrigin origin) =>
        Inner.Seek(offset, origin);

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Interlocked.Exchange(ref _inner, null)?.Dispose();

        base.Dispose(disposing);
    }
}
