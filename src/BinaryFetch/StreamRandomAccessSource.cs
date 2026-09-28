namespace BinaryFetch;

/// <summary>
/// A random-access source over a seekable stream: a local file, or bytes
/// already in memory. The length is known from the start. Concurrent reads
/// are serialized, since they share the stream's position.
/// </summary>
public sealed class StreamRandomAccessSource : RandomAccessSource
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StreamRandomAccessSource(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException(
                "A random-access source requires a readable, seekable stream.",
                nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        Length = stream.Length;
    }

    public override async ValueTask<ReadOnlyMemory<byte>> ReadTailAsync(
        int maxLength,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        long length = Length!.Value;
        int tailLength = checked((int)Math.Min(length, maxLength));
        var tail = new byte[tailLength];
        await ReadRangeAsync(length - tailLength, tail, cancellationToken)
            .ConfigureAwait(false);
        return tail;
    }

    public override async ValueTask ReadRangeAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        long length = Length!.Value;
        if (offset + destination.Length > length)
        {
            throw new RangeFetchException(
                RangeFetchFailure.InvalidResponse,
                "The requested range lies past the end of the representation.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = offset;
            int filled = 0;
            while (filled < destination.Length)
            {
                int read = await _stream.ReadAsync(
                    destination[filled..],
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new RangeFetchException(
                        RangeFetchFailure.InvalidResponse,
                        "The representation ended before the requested range.");
                }

                filled += read;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public override ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
            _stream.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
