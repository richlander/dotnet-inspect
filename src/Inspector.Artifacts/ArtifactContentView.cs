using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Inspector.Artifacts;

/// <summary>Owner-attested retained bytes borrowed during admission.</summary>
public readonly ref struct ArtifactAdmissionContentView
{
    internal ArtifactAdmissionContentView(
        ArtifactIdentity artifact,
        ImmutableArray<byte> content)
    {
        Artifact = artifact;
        _content = content;
    }

    private readonly ImmutableArray<byte> _content;

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
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

/// <summary>Owner-attested retained bytes borrowed during a query.</summary>
public readonly ref struct ArtifactQueryContentView
{
    internal ArtifactQueryContentView(
        ArtifactIdentity artifact,
        ImmutableArray<byte> content)
    {
        Artifact = artifact;
        _content = content;
    }

    private readonly ImmutableArray<byte> _content;

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
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

public delegate TResult ArtifactAdmissionContentCallback<TResult>(
    scoped ArtifactAdmissionContentView view,
    CancellationToken cancellationToken);

public delegate TResult ArtifactQueryContentCallback<TResult>(
    scoped ArtifactQueryContentView view,
    CancellationToken cancellationToken);

/// <summary>
/// Separates owner authorization rejection from a consumer result or exception.
/// </summary>
public abstract class ArtifactContentAccessOutcome<TResult>
{
    private protected ArtifactContentAccessOutcome()
    {
    }

    public sealed class Accessed : ArtifactContentAccessOutcome<TResult>
    {
        internal Accessed(TResult value) => Value = value;

        public TResult Value { get; }
    }

    public sealed class Unauthorized : ArtifactContentAccessOutcome<TResult>
    {
        public Unauthorized()
        {
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
