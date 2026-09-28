using System.Collections.ObjectModel;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The path one package payload acquisition took.</summary>
public enum PackageTransferPath
{
    /// <summary>A complete payload from a cache, with no request.</summary>
    Cache,

    /// <summary>One complete transfer.</summary>
    Download,

    /// <summary>A ranged read of the directory and selected entries.</summary>
    Ranged,

    /// <summary>A ranged read that fell back to a complete transfer.</summary>
    RangedThenDownload,

    /// <summary>
    /// A ranged read the entry cache answered in full: the cached directory and
    /// every selected entry, with no request.
    /// </summary>
    EntryCache,
}

/// <summary>Why a ranged read fell back to a complete transfer.</summary>
public enum PackageTransferFallbackReason
{
    /// <summary>The source answered a ranged request with the whole archive.</summary>
    RangeIgnored,

    /// <summary>The archive changed between ranged requests.</summary>
    ArchiveChanged,

    /// <summary>The archive cannot be read by range.</summary>
    ArchiveUnsupported,

    /// <summary>A ranged request failed in a way the complete fetch may not.</summary>
    RangedReadFailed,
}

/// <summary>Why a package request was made.</summary>
public enum PackageTransferRequestPurpose
{
    /// <summary>The complete archive.</summary>
    Complete,

    /// <summary>The archive's tail, which carries the directory's end record.</summary>
    DirectoryTail,

    /// <summary>The part of the directory the tail did not cover.</summary>
    DirectoryHead,

    /// <summary>One span of selected entries.</summary>
    EntrySpan,

    /// <summary>
    /// A complete request made to learn the archive's size, abandoned before
    /// its body because the advertised length was above the size cut.
    /// </summary>
    SizeProbe,
}

/// <summary>How one package request ended.</summary>
public enum PackageTransferRequestOutcome
{
    /// <summary>The response body was read to its end.</summary>
    Completed,

    /// <summary>The response was abandoned before its body ended.</summary>
    Abandoned,

    /// <summary>The source answered a ranged request with the whole archive.</summary>
    RangeIgnored,

    /// <summary>The source refused the credential.</summary>
    Refused,

    /// <summary>The source does not have the package.</summary>
    NotFound,

    /// <summary>The request failed.</summary>
    Failed,
}

/// <summary>
/// One requested byte range: <see cref="Start"/> is absent for the final
/// <see cref="Length"/> bytes of the archive.
/// </summary>
public readonly record struct PackageTransferRange(long? Start, long Length);

/// <summary>One package request attempt. It carries no URL, header, credential, or content.</summary>
public sealed record PackageTransferRequest(
    PackageTransferRequestPurpose Purpose,
    PackageTransferRange? Range,
    PackageTransferRequestOutcome Outcome,
    long? AdvertisedLength,
    long BytesReceived);

/// <summary>
/// The owner-issued record of what one package payload acquisition
/// transferred: its path and every package request it made, in issue order.
/// Owned by <c>docs/design/package-transfer-receipt.md</c>.
/// </summary>
public sealed class PackageTransferReceipt
{
    /// <summary>The largest number of requests a receipt lists.</summary>
    public const int MaxRequests = 256;

    /// <summary>The receipt of a payload served from a cache.</summary>
    public static PackageTransferReceipt Cache { get; } =
        new(PackageTransferPath.Cache, null, [], 0, 0, false);

    /// <summary>The receipt of a ranged read the entry cache answered in full.</summary>
    public static PackageTransferReceipt EntryCache { get; } =
        new(PackageTransferPath.EntryCache, null, [], 0, 0, false);

    private PackageTransferReceipt(
        PackageTransferPath path,
        PackageTransferFallbackReason? fallbackReason,
        IReadOnlyList<PackageTransferRequest> requests,
        int requestCount,
        long bytesReceived,
        bool isTruncated)
    {
        Path = path;
        FallbackReason = fallbackReason;
        Requests = requests;
        RequestCount = requestCount;
        BytesReceived = bytesReceived;
        IsTruncated = isTruncated;
    }

    public PackageTransferPath Path { get; }

    /// <summary>Present exactly when <see cref="Path"/> is <see cref="PackageTransferPath.RangedThenDownload"/>.</summary>
    public PackageTransferFallbackReason? FallbackReason { get; }

    /// <summary>The first <see cref="MaxRequests"/> requests, in issue order.</summary>
    public IReadOnlyList<PackageTransferRequest> Requests { get; }

    /// <summary>Every request the acquisition made, including any past the bound.</summary>
    public int RequestCount { get; }

    /// <summary>The response body bytes of every request the acquisition made.</summary>
    public long BytesReceived { get; }

    /// <summary>Whether <see cref="Requests"/> omits requests past the bound.</summary>
    public bool IsTruncated { get; }

    /// <summary>Whether a House payload origin agrees with this receipt's path.</summary>
    public bool AgreesWith(PackagePayloadOrigin origin) =>
        origin switch
        {
            PackagePayloadOrigin.Cache =>
                Path is PackageTransferPath.Cache or PackageTransferPath.EntryCache,
            PackagePayloadOrigin.Download =>
                Path is PackageTransferPath.Download
                    or PackageTransferPath.RangedThenDownload,
            PackagePayloadOrigin.Ranged => Path == PackageTransferPath.Ranged,
            _ => false,
        };

    internal static PackageTransferReceipt Create(
        PackageTransferPath path,
        PackageTransferFallbackReason? fallbackReason,
        IEnumerable<PackageTransferRequest> requests)
    {
        if (!Enum.IsDefined(path))
            throw new ArgumentOutOfRangeException(nameof(path));
        if ((path == PackageTransferPath.RangedThenDownload)
            != fallbackReason.HasValue)
        {
            throw new ArgumentException(
                "A fallback reason is present exactly on the RangedThenDownload path.",
                nameof(fallbackReason));
        }

        var kept = new List<PackageTransferRequest>();
        int count = 0;
        long bytes = 0;
        foreach (PackageTransferRequest request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            count++;
            bytes += request.BytesReceived;
            if (kept.Count < MaxRequests)
                kept.Add(request);
        }
        if (path is PackageTransferPath.Cache or PackageTransferPath.EntryCache
            && count != 0)
        {
            throw new ArgumentException(
                "A cached payload carries no request.",
                nameof(requests));
        }
        if (path == PackageTransferPath.Cache)
            return Cache;
        if (path == PackageTransferPath.EntryCache)
            return EntryCache;

        return new(
            path,
            fallbackReason,
            new ReadOnlyCollection<PackageTransferRequest>(kept),
            count,
            bytes,
            count > kept.Count);
    }
}

/// <summary>
/// The acquisition step's running record of its package requests, in issue
/// order, from which it issues one <see cref="PackageTransferReceipt"/>.
/// </summary>
internal sealed class PackageTransferRecorder
{
    private readonly List<PackageTransferRequest> _requests = [];

    public void Add(PackageTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);
    }

    /// <summary>Appends a settled ranged read's requests, each attempt as its own request.</summary>
    public void AddRanged(PackageArchiveRequestLog log)
    {
        foreach (PackageArchiveRequestObservation observed in log.Snapshot())
        {
            _requests.Add(new PackageTransferRequest(
                observed.Purpose switch
                {
                    PackageArchiveRequestPurpose.DirectoryTail =>
                        PackageTransferRequestPurpose.DirectoryTail,
                    PackageArchiveRequestPurpose.DirectoryHead =>
                        PackageTransferRequestPurpose.DirectoryHead,
                    _ => PackageTransferRequestPurpose.EntrySpan,
                },
                new PackageTransferRange(observed.Start, observed.Length),
                Map(observed.Outcome),
                observed.AdvertisedLength,
                observed.BytesReceived));
        }
    }

    public PackageTransferReceipt Issue(
        PackageTransferPath path,
        PackageTransferFallbackReason? fallbackReason = null) =>
        PackageTransferReceipt.Create(path, fallbackReason, _requests);

    private static PackageTransferRequestOutcome Map(
        PackageArchiveRequestOutcome outcome) =>
        outcome switch
        {
            PackageArchiveRequestOutcome.Completed => PackageTransferRequestOutcome.Completed,
            PackageArchiveRequestOutcome.Abandoned => PackageTransferRequestOutcome.Abandoned,
            PackageArchiveRequestOutcome.RangeIgnored => PackageTransferRequestOutcome.RangeIgnored,
            PackageArchiveRequestOutcome.Refused => PackageTransferRequestOutcome.Refused,
            PackageArchiveRequestOutcome.NotFound => PackageTransferRequestOutcome.NotFound,
            _ => PackageTransferRequestOutcome.Failed,
        };

    internal static PackageTransferRequestOutcome Classify(
        PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.NotFound => PackageTransferRequestOutcome.NotFound,
            PackageSourceFailureKind.AuthenticationRequired => PackageTransferRequestOutcome.Refused,
            _ => PackageTransferRequestOutcome.Failed,
        };
}

/// <summary>Counts the response body bytes a complete fetch's admission consumes.</summary>
internal sealed class PackageTransferCountingStream(Stream inner) : Stream
{
    private long _bytesReceived;

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    private int Count(int read)
    {
        if (read > 0)
            Interlocked.Add(ref _bytesReceived, read);
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
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
