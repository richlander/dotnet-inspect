using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

internal interface IPackageHousePayloadSource
{
    bool TryOpenPayloadRead(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream);
}

/// <summary>
/// A cold, single-use read of one exact entry from a live
/// <see cref="PackageHouseSettlement.Acquired"/> payload.
/// </summary>
/// <remarks>
/// Creating this stream binds the entry request to the House acquisition but
/// does not open or read the entry. The first non-empty read opens the retained
/// content, and each subsequent read produces only the bytes requested by the
/// caller. Consumers must read through end of stream to complete size and
/// checksum validation. Reads must not overlap. Disposing earlier abandons the
/// read.
/// </remarks>
public sealed class PackageHousePayloadRead : Stream
{
    private readonly IPackageContent _content;
    private readonly string _relativePath;
    private readonly long _maxExpandedBytes;
    private Stream? _source;
    private bool _started;
    private bool _disposed;

    internal PackageHousePayloadRead(
        PackageHouseSettlement.Acquired settlement,
        string relativePath,
        long maxExpandedBytes)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        Acquisition = settlement.Result.Evidence.Acquisition
            ?? throw new InvalidOperationException(
                "Exact payload reads require PackageHouse acquisition evidence.");
        if (!ReferenceEquals(
                Acquisition.Generation,
                settlement.Payload.Content.GenerationIdentity))
        {
            throw new InvalidOperationException(
                "The live payload no longer matches its PackageHouse acquisition.");
        }

        _content = settlement.Payload.Content;
        _relativePath = relativePath;
        _maxExpandedBytes = maxExpandedBytes;
    }

    /// <summary>
    /// The House receipt binding this read to the retained package generation.
    /// </summary>
    public PackageHouseAcquisitionReceipt Acquisition { get; }

    internal bool HasStarted => _started;

    public override bool CanRead => !_disposed;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        return buffer.IsEmpty ? 0 : Source.Read(buffer);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.IsEmpty
            ? 0
            : await Source.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
    }

    private Stream Source
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_source is not null)
                return _source;

            _started = true;
            if (_content is not IPackageHousePayloadSource source)
            {
                throw new NotSupportedException(
                    "This PackageHouse payload does not support pull-based reads.");
            }

            if (!source.TryOpenPayloadRead(
                    _relativePath,
                    _maxExpandedBytes,
                    out Stream? payload))
            {
                throw new FileNotFoundException(
                    "The selected package payload entry is unavailable.");
            }

            _source = payload;
            return payload;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
            _source?.Dispose();
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed && _source is not null)
            await _source.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count) =>
        throw new NotSupportedException();
}
