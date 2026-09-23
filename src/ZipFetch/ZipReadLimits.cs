namespace ZipFetch;

/// <summary>
/// The bounds one ZIP read respects, supplied by the caller before any
/// transfer: the archive total, the central-directory caps, the per-entry
/// expanded bound, and the slack an entry request adds for a local header
/// whose extra field is longer than the directory declares.
/// </summary>
public sealed class ZipReadLimits
{
    /// <summary>The largest entry-read slack the reader accepts, in bytes.</summary>
    public const int MaxEntryReadSlack = 64 * 1024;

    public ZipReadLimits(
        long maxArchiveBytes = 500_000_000,
        int maxEntryCount = 50_000,
        long maxDirectoryBytes = 16L * 1024 * 1024,
        long maxExpandedBytes = 1L << 30,
        int entryReadSlack = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDirectoryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(entryReadSlack);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entryReadSlack, MaxEntryReadSlack);
        MaxArchiveBytes = maxArchiveBytes;
        MaxEntryCount = maxEntryCount;
        MaxDirectoryBytes = maxDirectoryBytes;
        MaxExpandedBytes = maxExpandedBytes;
        EntryReadSlack = entryReadSlack;
    }

    /// <summary>The bounds used when a caller states none.</summary>
    public static ZipReadLimits Default { get; } = new();

    /// <summary>The largest archive, by its derived total length, the reader will read.</summary>
    public long MaxArchiveBytes { get; }

    /// <summary>The largest number of entries the central directory may declare.</summary>
    public int MaxEntryCount { get; }

    /// <summary>The largest central directory, in bytes, the reader will fetch and parse.</summary>
    public long MaxDirectoryBytes { get; }

    /// <summary>The largest expansion of one entry the reader will produce.</summary>
    public long MaxExpandedBytes { get; }

    /// <summary>
    /// Bytes added to the first request of an entry read beyond what the
    /// directory declares, so a longer local extra field does not force a
    /// follow-up request. Every entry request is still clamped to the
    /// central-directory offset.
    /// </summary>
    public int EntryReadSlack { get; }
}
