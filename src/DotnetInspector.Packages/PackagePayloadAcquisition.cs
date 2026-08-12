using System.IO.Compression;
using System.Net.Http.Headers;
using DotnetInspector.Core;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>Where an acquired package payload came from.</summary>
public enum PackagePayloadOrigin
{
    /// <summary>A store entry answered without any network work.</summary>
    Cache,

    /// <summary>The payload was downloaded from a source and committed.</summary>
    Download,
}

/// <summary>
/// One acquired package payload together with the coordinate that selected it
/// and the producer identity recorded with its bytes.
/// </summary>
public sealed record AcquiredPackagePayload(
    ResolvedPackageCoordinate Coordinate,
    IPackageContent Content,
    string ProducerKey,
    PackagePayloadOrigin Origin);

/// <summary>The result of acquiring one exact package payload.</summary>
public abstract record PackagePayloadResult
{
    private protected PackagePayloadResult()
    {
    }

    /// <summary>The payload is available through <see cref="Payload"/>.</summary>
    public sealed record Acquired : PackagePayloadResult
    {
        internal Acquired(AcquiredPackagePayload payload) =>
            Payload = payload;

        public AcquiredPackagePayload Payload { get; }
    }

    /// <summary>
    /// No authorized source supplied the coordinate's bytes. The message names
    /// the requested coordinate and the sources that were tried, never content
    /// taken from a feed response or archive.
    /// </summary>
    public sealed record Unavailable : PackagePayloadResult
    {
        internal Unavailable(string message) => Message = message;

        public string Message { get; }
    }
}

/// <summary>
/// Acquires the payload for one already-resolved package coordinate through the
/// caller's <see cref="IPackageStore"/>, without requiring a filesystem.
/// </summary>
/// <remarks>
/// <para>
/// This is the host-neutral peer of <see cref="PackageExtractor"/>'s desktop
/// acquisition: the store decides where bytes live, so a browser host supplies
/// <see cref="InMemoryPackageStore"/> and a desktop host supplies
/// <see cref="FileSystemPackageStore"/>. Only the coordinate's own authorized
/// sources are consulted, in order, and the producer that served the bytes is
/// carried with them. Payload transport reuses the shared retry, credential
/// scoping, and feed-failure conventions (<see cref="HttpRetryHelper"/>,
/// <see cref="NuGetCredentialScope"/>) rather than issuing a bare request.
/// </para>
/// <para>
/// A payload is bounded and validated <em>before</em> it is published, never
/// after. The response body is read into one buffer under
/// <see cref="PackagePayloadLimits.MaxArchiveBytes"/>, counted as received so a
/// feed that advertises no length or under-reports one is bounded by the same
/// number, and then opened as an archive and checked against
/// <see cref="PackagePayloadLimits.MaxEntryCount"/> and
/// <see cref="PackagePayloadLimits.MaxExpandedBytes"/>. Only a payload that
/// passes reaches <see cref="IPackageStore.CommitAsync"/>, so an unreadable,
/// truncated, oversized, or bomb-shaped archive is a failure of that one source
/// — the next authorized source is tried, and nothing enters the store.
/// Validation happens here rather than behind the store interface because the
/// store's contract is persistence: a store that had to re-derive what a valid
/// payload is would be a second owner of that judgment, and the filesystem
/// store would learn it only by expanding the archive it was asked to trust.
/// </para>
/// <para>
/// The single buffer is the cost of that ordering. The archive is held once
/// while it is validated and committed; the transport is not asked to hand the
/// same bytes over twice, and the caller never sees a partially published
/// entry.
/// </para>
/// <para>
/// Gated by <c>PackagePayloadAcquisitionTests</c>:
/// <c>CachedContentOfAnUnauthorizedProducer_IsNotServed</c> for the rule that a
/// cache fulfills only an authorized producer,
/// <c>SourcesAreTriedInOrderUntilOneServesThePayload</c> for source order and
/// provenance, <c>CacheHit_AnswersWithoutNetworkWork</c> for the no-network
/// cache path, <c>UnboundedChunkedPayload_IsRejectedWithoutContentLength</c>,
/// <c>AdvertisedOversizePayload_IsATypedSourceFailure</c>,
/// <c>ArchiveDeclaringTooManyEntries_IsRejected</c>, and
/// <c>ArchiveDeclaringTooMuchExpandedContent_IsRejected</c> for the bounds —
/// each of which also asserts the rejected payload is absent from the store —
/// <c>InvalidArchiveFromOneSource_LetsTheNextSourceServe</c> for source
/// failover without cache poisoning, and
/// <c>Acquisition_ObservesCancellationDuringDownload</c> for cancellation.
/// </para>
/// </remarks>
public static class PackagePayloadAcquisition
{
    /// <summary>
    /// Returns the payload for <paramref name="coordinate"/>, preferring a
    /// cached entry committed by one of its authorized producers and otherwise
    /// downloading from those sources in order.
    /// </summary>
    public static async Task<PackagePayloadResult> AcquireAsync(
        HttpClient client,
        ResolvedPackageCoordinate coordinate,
        IPackageStore store,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(store);
        limits ??= PackagePayloadLimits.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxEntryCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (coordinate.Sources.Count == 0)
        {
            return new PackagePayloadResult.Unavailable(
                $"No source is authorized to provide package '{coordinate.PackageId}'.");
        }

        IReadOnlyList<string> producerKeys =
            NuGetSourceResolver.SourceKeys(coordinate.Sources);
        if (store.TryGetCached(
                coordinate.PackageId,
                coordinate.Version,
                producerKeys,
                log)
            is { } cached)
        {
            return Result(cached, PackagePayloadOrigin.Cache);
        }

        List<string> failedSources = [];
        foreach (PackageSource source in coordinate.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IPackageContent? content = await TryDownloadAsync(
                client,
                coordinate,
                source,
                store,
                log,
                limits,
                cancellationToken).ConfigureAwait(false);
            if (content is not null)
                return Result(content, PackagePayloadOrigin.Download);

            failedSources.Add(source.Name);
        }

        return new PackagePayloadResult.Unavailable(
            $"Package '{coordinate.PackageId}' version '{coordinate.Version}' "
            + $"was not supplied by any authorized source ({string.Join(", ", failedSources)}).");

        PackagePayloadResult Result(
            IPackageContent content,
            PackagePayloadOrigin origin)
        {
            // The store is asked for authorized producers only, and a download
            // commits under the source it came from. This confirms the store's
            // answer rather than treating availability as authorization: a
            // pre-existing entry can win a commit for another producer.
            if (!producerKeys.Contains(
                    content.ProducerKey,
                    StringComparer.Ordinal))
            {
                return new PackagePayloadResult.Unavailable(
                    $"Content for package '{coordinate.PackageId}' version "
                    + $"'{coordinate.Version}' belongs to an unauthorized producer.");
            }

            return new PackagePayloadResult.Acquired(
                new AcquiredPackagePayload(
                    coordinate,
                    content,
                    content.ProducerKey,
                    origin));
        }
    }

    static async Task<IPackageContent?> TryDownloadAsync(
        HttpClient client,
        ResolvedPackageCoordinate coordinate,
        PackageSource source,
        IPackageStore store,
        Action<string>? log,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        string? nupkgUrl = await PackageExtractor.GetPackageDownloadUrlAsync(
            client,
            source,
            coordinate.PackageId,
            coordinate.Version,
            log,
            cancellationToken).ConfigureAwait(false);
        if (nupkgUrl is null)
            return null;

        AuthenticationHeaderValue? auth =
            NuGetCredentialScope.AuthFor(source, nupkgUrl, log);
        log?.Invoke(
            $"Downloading: {coordinate.PackageId} {coordinate.Version} from {source.Name}");

        // The transport's own advertised-size cap is raised out of the way so
        // an oversized payload stays a typed source failure here instead of
        // becoming an exception that would abandon the remaining sources. The
        // bound is not lost: the bytes are counted as they arrive.
        using HttpResponseMessage? response =
            await HttpRetryHelper.GetStreamedWithRetryAsync(
                client,
                nupkgUrl,
                log: log,
                cancellationToken: cancellationToken,
                auth: auth,
                trafficKind: NetworkTrafficKind.PackageDownload,
                maxAdvertisedContentLength: long.MaxValue)
                .ConfigureAwait(false);
        if (response is null)
            return null;

        if (response.Content.Headers.ContentLength > limits.MaxArchiveBytes)
        {
            log?.Invoke(
                $"Source {source.Name} advertised a package payload above the configured archive limit.");
            return null;
        }

        byte[] archive;
        try
        {
            Stream payload = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (payload.ConfigureAwait(false))
            {
                if (await ReadBoundedAsync(
                        payload,
                        limits.MaxArchiveBytes,
                        cancellationToken).ConfigureAwait(false)
                    is not { } received)
                {
                    log?.Invoke(
                        $"Source {source.Name} sent a package payload above the configured archive limit.");
                    return null;
                }

                archive = received;
            }

            if (ValidateArchive(archive, limits, cancellationToken)
                is { } archiveProblem)
            {
                // The bytes are not a package this host will publish, so this
                // source failed to serve the coordinate and nothing is
                // committed. The next authorized source is tried.
                log?.Invoke(
                    $"Source {source.Name} did not deliver a usable package payload: {archiveProblem}");
                return null;
            }

            return await store.CommitAsync(
                coordinate.PackageId,
                coordinate.Version,
                NuGetCache.GetSourceKey(source.Url),
                new MemoryStream(archive, writable: false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
                or IOException
                or InvalidDataException
                || (ex is OperationCanceledException
                    && !cancellationToken.IsCancellationRequested))
        {
            // The payload stopped mid-body, timed out, or could not be
            // persisted. That is this source failing to serve the coordinate,
            // so the next authorized source is tried; the caller reports every
            // source that failed if none succeeds. A cancellation the caller
            // actually requested is not caught here.
            log?.Invoke(
                $"Source {source.Name} did not deliver a usable package payload: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from <paramref name="source"/>,
    /// or returns null when the stream carries more. The count is of bytes
    /// actually received, so a response with no advertised length, or one whose
    /// advertised length under-reports its body, is bounded identically.
    /// </summary>
    static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source
                .ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            if (buffer.Length + read > maxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Returns a description of why <paramref name="archive"/> may not be
    /// published, or null when it is a readable archive within
    /// <paramref name="limits"/>. The description names limits and counts only:
    /// no entry name from the archive appears in it.
    /// </summary>
    static string? ValidateArchive(
        byte[] archive,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        int entryCount = 0;
        long expandedBytes = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entryCount > limits.MaxEntryCount)
            {
                return
                    $"the archive declares more than {limits.MaxEntryCount} entries";
            }

            expandedBytes += entry.Length;
            if (expandedBytes > limits.MaxExpandedBytes)
            {
                return
                    $"the archive declares more than {limits.MaxExpandedBytes} bytes of expanded content";
            }
        }

        return null;
    }
}
