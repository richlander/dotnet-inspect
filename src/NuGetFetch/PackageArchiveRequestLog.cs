namespace NuGetFetch;

/// <summary>
/// Records the ranged requests one archive read makes, each attempt as its
/// own entry, for the caller that owns the read. It retains no URL, header,
/// credential, or response content. Pass a fresh log to
/// <see cref="IPackageArchiveRangeSource.OpenArchiveAsync"/>; its entries are
/// settled once the returned reader is disposed or the open fails.
/// </summary>
public sealed class PackageArchiveRequestLog
{
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];

    internal Entry Begin(PackageArchiveRequestPurpose purpose, long? start, long length)
    {
        var entry = new Entry(purpose, start, length);
        lock (_gate)
            _entries.Add(entry);
        return entry;
    }

    /// <summary>The recorded requests in issue order.</summary>
    internal IReadOnlyList<PackageArchiveRequestObservation> Snapshot()
    {
        lock (_gate)
            return [.. _entries.Select(static entry => entry.Observe())];
    }

    internal sealed class Entry(PackageArchiveRequestPurpose purpose, long? start, long length)
    {
        private readonly object _gate = new();
        private PackageArchiveRequestOutcome? _outcome;
        private long? _advertisedLength;
        private long _bytesReceived;

        public void AddBytes(long count)
        {
            lock (_gate)
                _bytesReceived += count;
        }

        /// <summary>Settles the entry once; later settlements are ignored.</summary>
        public void Settle(PackageArchiveRequestOutcome outcome, long? advertisedLength)
        {
            lock (_gate)
            {
                if (_outcome is not null)
                    return;
                _outcome = outcome;
                _advertisedLength = advertisedLength;
            }
        }

        public PackageArchiveRequestObservation Observe()
        {
            lock (_gate)
            {
                return new(
                    purpose,
                    start,
                    length,
                    // An entry still open when observed was abandoned by
                    // its reader.
                    _outcome ?? PackageArchiveRequestOutcome.Abandoned,
                    _advertisedLength,
                    _bytesReceived);
            }
        }
    }
}

internal enum PackageArchiveRequestPurpose
{
    DirectoryTail,
    DirectoryHead,
    EntrySpan,
}

internal enum PackageArchiveRequestOutcome
{
    Completed,
    Abandoned,
    RangeIgnored,
    Refused,
    NotFound,
    Failed,
}

/// <summary>
/// One ranged request attempt: <see cref="Start"/> is absent for a suffix
/// (tail) range of <see cref="Length"/> bytes.
/// </summary>
internal sealed record PackageArchiveRequestObservation(
    PackageArchiveRequestPurpose Purpose,
    long? Start,
    long Length,
    PackageArchiveRequestOutcome Outcome,
    long? AdvertisedLength,
    long BytesReceived);

/// <summary>
/// Counts the body bytes a ranged response delivers to its reader and settles
/// the request when the reader disposes the body: completed when the reader
/// reached the end, abandoned otherwise.
/// </summary>
internal sealed class PackageArchiveCountingStream(
    Stream inner,
    PackageArchiveRequestLog.Entry entry,
    long? advertisedLength) : Stream
{
    private bool _reachedEnd;
    private int _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Observe(inner.Read(buffer, offset, count), count);

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Observe(
            await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false),
            count);

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        Observe(
            await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false),
            buffer.Length);

    private int Observe(int read, int requested)
    {
        if (read > 0)
            entry.AddBytes(read);
        else if (requested > 0)
            _reachedEnd = true;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Settle();
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Settle();
            await inner.DisposeAsync().ConfigureAwait(false);
        }
        GC.SuppressFinalize(this);
    }

    private void Settle() =>
        entry.Settle(
            _reachedEnd
                ? PackageArchiveRequestOutcome.Completed
                : PackageArchiveRequestOutcome.Abandoned,
            advertisedLength);
}
