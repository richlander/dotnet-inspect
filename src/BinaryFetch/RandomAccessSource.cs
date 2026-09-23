namespace BinaryFetch;

/// <summary>
/// Byte access to one representation by offset: a tail read that discovers
/// the end, exact range reads, and a length the consumer confirms once it has
/// derived it from the bytes themselves. Remote and local sources share this
/// contract so a reader over it runs unchanged over either.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Length"/> may be unknown until the consumer confirms it: an HTTP
/// source whose <c>Content-Range</c> is not visible learns the total only from
/// the consumer's own structure reading. <see cref="ConfirmLength"/> is that
/// handoff; it checks the derived length against every fact the source did
/// observe and refuses a disagreement.
/// </para>
/// <para>
/// Reads that a source cannot describe as the bytes requested are refused with
/// a <see cref="RangeFetchException"/>; exceptions the underlying transport
/// raises propagate unwrapped.
/// </para>
/// </remarks>
public abstract class RandomAccessSource : IAsyncDisposable
{
    /// <summary>The representation's length once known; <see langword="null"/> until then.</summary>
    public long? Length { get; protected set; }

    /// <summary>
    /// Reads the last <paramref name="maxLength"/> bytes, or the whole
    /// representation when it is shorter. The returned buffer is owned by the
    /// caller.
    /// </summary>
    public abstract ValueTask<ReadOnlyMemory<byte>> ReadTailAsync(
        int maxLength,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads exactly <c>destination.Length</c> bytes starting at
    /// <paramref name="offset"/>. A short read is a failure, never a partial
    /// result.
    /// </summary>
    public abstract ValueTask ReadRangeAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the length the consumer derived from the representation's own
    /// structure. A source that already observed a length refuses a different
    /// one as <see cref="RangeFetchFailure.InvalidResponse"/>.
    /// </summary>
    public virtual void ConfirmLength(long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (Length is { } known && known != length)
        {
            throw new RangeFetchException(
                RangeFetchFailure.InvalidResponse,
                "The derived length does not match the length the source reported.");
        }

        Length = length;
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
