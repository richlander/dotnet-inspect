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
/// scoping, size cap, and feed-failure conventions
/// (<see cref="HttpRetryHelper"/>, <see cref="NuGetCredentialScope"/>) rather
/// than issuing a bare request, and it streams the response into the store so
/// nothing buffers the archive in memory on the caller's behalf.
/// </para>
/// <para>
/// A payload whose advertised size exceeds the shared transport cap fails as
/// an exception from that transport layer, exactly as on the desktop path; it
/// is not reshaped into an acquisition outcome here.
/// </para>
/// <para>
/// Gated by <c>PackagePayloadAcquisitionTests</c>:
/// <c>CachedContentOfAnUnauthorizedProducer_IsNotServed</c> for the rule that a
/// cache fulfills only an authorized producer,
/// <c>SourcesAreTriedInOrderUntilOneServesThePayload</c> for source order and
/// provenance, and <c>CacheHit_AnswersWithoutNetworkWork</c> for the
/// no-network cache path.
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(store);
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

        using HttpResponseMessage? response =
            await HttpRetryHelper.GetStreamedWithRetryAsync(
                client,
                nupkgUrl,
                log: log,
                cancellationToken: cancellationToken,
                auth: auth,
                trafficKind: NetworkTrafficKind.PackageDownload)
                .ConfigureAwait(false);
        if (response is null)
            return null;

        Stream payload = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (payload.ConfigureAwait(false))
        {
            try
            {
                return await store.CommitAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    NuGetCache.GetSourceKey(source.Url),
                    payload,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is HttpRequestException
                    or IOException
                    or InvalidDataException
                    || (ex is OperationCanceledException
                        && !cancellationToken.IsCancellationRequested))
            {
                // The payload stopped mid-body, timed out, or is not a readable
                // archive. That is this source failing to serve the coordinate,
                // so the next authorized source is tried; the caller reports
                // every source that failed if none succeeds. A cancellation the
                // caller actually requested is not caught here.
                log?.Invoke(
                    $"Source {source.Name} did not deliver a usable package payload: {ex.Message}");
                return null;
            }
        }
    }
}
