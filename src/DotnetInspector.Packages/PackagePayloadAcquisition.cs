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
/// One payload acquired through a typed source client.
/// </summary>
public sealed record AcquiredPackageSourcePayload(
    PackageSourceCoordinate Coordinate,
    IPackageContent Content,
    string ProducerKey,
    PackagePayloadOrigin Origin);

/// <summary>The result of acquiring one exact typed-source package payload.</summary>
public abstract record PackageSourcePayloadResult
{
    private protected PackageSourcePayloadResult()
    {
    }

    public sealed record Acquired(AcquiredPackageSourcePayload Payload)
        : PackageSourcePayloadResult;

    public sealed record Unavailable(string Message)
        : PackageSourcePayloadResult
    {
        internal PackageSourcePayloadUnavailableKind Kind { get; init; }
    }

    public sealed record Failed(PackageSourceFailure Failure)
        : PackageSourcePayloadResult;
}

internal enum PackageSourcePayloadUnavailableKind
{
    NotFound,
    MismatchedPayload,
    PolicyRejected,
    UnauthorizedProducer,
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
/// carried with them. Payload transport crosses <see cref="IPackageSourceClient"/>;
/// the source client owns retries and credential scoping, while the desktop
/// adapter preserves host transport policy and feed-failure reporting.
/// </para>
/// <para>
/// A payload is bounded and validated <em>before</em> it is published, never
/// after. The response body is read into one buffer under
/// <see cref="PackagePayloadLimits.MaxArchiveBytes"/>, counted as received so a
/// feed that advertises no length or under-reports one is bounded by the same
/// number — a response that <em>does</em> advertise a length is read into
/// exactly one buffer of that length, so the host's reservation and the
/// allocation agree and a body contradicting its own header fails the source —
/// and then opened as an archive and checked against
/// <see cref="PackagePayloadLimits.MaxEntryCount"/> and
/// <see cref="PackagePayloadLimits.MaxExpandedBytes"/>. Only a payload that
/// passes reaches <see cref="IPackageStore.CommitAsync"/>, so an unreadable,
/// truncated, oversized, or bomb-shaped archive is a failure of that one source
/// — the next authorized source is tried, and nothing enters the store.
/// Source clients own request policy, retries, credentials, and typed transport
/// failures. Validation happens here rather than behind the store interface
/// because the store's contract is persistence: a store that had to re-derive
/// what a valid payload is would be a second owner of that judgment, and the
/// filesystem store would learn it only by expanding the archive it was asked
/// to trust.
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
/// <c>CacheHit_IsRevalidatedAgainstCurrentPayloadLimits</c>,
/// <c>CacheHitWithoutRetainedNupkg_IsAdmittedFromExtractedTree</c>,
/// <c>InadmissibleCacheEntry_DoesNotMaskAnotherProducer</c>, and
/// <c>CommitThatLosesToInadmissibleCachedContent_IsNotServed</c> for
/// admission at both cache-return seams,
/// <c>SourcesAreTriedInOrderUntilOneServesThePayload</c> for source order and
/// provenance, <c>CacheHit_AnswersWithoutNetworkWork</c> for the no-network
/// cache path, <c>UnboundedChunkedPayload_IsRejectedWithoutContentLength</c>,
/// <c>AdvertisedOversizePayload_IsATypedSourceFailure</c>,
/// <c>ArchiveDeclaringTooManyEntries_IsRejected</c>, and
/// <c>ArchiveDeclaringTooMuchExpandedContent_IsRejected</c> for the bounds —
/// each of which also asserts the rejected payload is absent from the store —
/// <c>TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit</c>,
/// <c>TransferPolicy_RejectedPayloadDisposesWithoutCompleting</c>, and
/// <c>TransferPolicy_CanRequireContentLengthBeforeBodyRead</c> for the optional
/// host-capacity seam,
/// <c>BodyTransferDeadline_DoesNotBoundCacheCommit</c> for separation between
/// transport consumption and post-transfer admission work,
/// <c>InvalidArchiveFromOneSource_LetsTheNextSourceServe</c> for source
/// failover without cache poisoning, and
/// <c>Acquisition_ObservesCancellationDuringDownload</c> for cancellation.
/// </para>
/// </remarks>
public static class PackagePayloadAcquisition
{
    /// <summary>
    /// Returns an exact payload through a typed source client while preserving
    /// the same cache authorization, archive admission, and publication policy
    /// as the legacy source path.
    /// </summary>
    public static async Task<PackageSourcePayloadResult> AcquireAsync(
        IPackageSourceClient source,
        PackageSourceCoordinate coordinate,
        IPackageStore store,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(store);
        limits = ValidateLimits(limits);
        cancellationToken.ThrowIfCancellationRequested();

        string producerKey = NuGetCache.GetSourceKey(source.Identity.Value);
        foreach (IPackageContent cached in store.EnumerateCached(
                     coordinate.PackageId,
                     coordinate.Version,
                     [producerKey],
                     log))
        {
            PackageContentAdmission.Outcome admission =
                await PackageContentAdmission.EvaluateAsync(
                    cached,
                    limits,
                    cancellationToken).ConfigureAwait(false);
            if (admission != PackageContentAdmission.Outcome.Admissible)
            {
                log?.Invoke(
                    $"Cached content for package '{coordinate.PackageId}' version "
                    + $"'{coordinate.Version}' from the selected producer does "
                    + "not satisfy the current payload limits.");
                continue;
            }

            return Result(cached, PackagePayloadOrigin.Cache);
        }

        return await AcquireFromSourceAsync(
            source,
            coordinate,
            store,
            log,
            limits,
            transferPolicy,
            cancellationToken).ConfigureAwait(false);

        PackageSourcePayloadResult Result(
            IPackageContent content,
            PackagePayloadOrigin origin) =>
            content.ProducerKey.Equals(producerKey, StringComparison.Ordinal)
                ? new PackageSourcePayloadResult.Acquired(
                    new AcquiredPackageSourcePayload(
                        coordinate,
                        content,
                        producerKey,
                        origin))
                : new PackageSourcePayloadResult.Unavailable(
                    $"Content for package '{coordinate.PackageId}' version "
                    + $"'{coordinate.Version}' belongs to an unauthorized producer.")
                {
                    Kind = PackageSourcePayloadUnavailableKind
                        .UnauthorizedProducer,
                };
    }

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
        CancellationToken cancellationToken = default,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        Func<PackageSource, IPackageSourceClient>?
            borrowedSourceClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(store);
        limits = ValidateLimits(limits);
        cancellationToken.ThrowIfCancellationRequested();

        if (coordinate.Sources.Count == 0)
        {
            return new PackagePayloadResult.Unavailable(
                $"No source is authorized to provide package '{coordinate.PackageId}'.");
        }

        // One EnumerateCached over the full authorized producer list so the
        // store can yield every app-cache slot (configured order) before any
        // global-packages tier. Per-producer loops would inspect producer A's
        // global entry before producer B's app entry.
        IReadOnlyList<string> producerKeys =
            NuGetSourceResolver.SourceKeys(coordinate.Sources);
        foreach (IPackageContent cached in store.EnumerateCached(
                     coordinate.PackageId,
                     coordinate.Version,
                     producerKeys,
                     log))
        {
            PackageContentAdmission.Outcome admission =
                await PackageContentAdmission.EvaluateAsync(
                    cached,
                    limits,
                    cancellationToken).ConfigureAwait(false);
            if (admission != PackageContentAdmission.Outcome.Admissible)
            {
                log?.Invoke(
                    admission == PackageContentAdmission.Outcome.MissingArchive
                        ? $"Cached content for package '{coordinate.PackageId}' version "
                            + $"'{coordinate.Version}' from one authorized producer has "
                            + "no retained archive and no usable extracted tree."
                        : $"Cached content for package '{coordinate.PackageId}' version "
                            + $"'{coordinate.Version}' from one authorized producer does "
                            + "not satisfy the current payload limits.");
                continue;
            }

            return Result(cached, PackagePayloadOrigin.Cache);
        }

        List<string> failedSources = [];
        foreach (PackageSource source in coordinate.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Uri.TryCreate(
                    source.Url,
                    UriKind.Absolute,
                    out Uri? endpoint)
                && endpoint.Scheme is "http" or "https")
            {
                IPackageSourceClient sourceClient =
                    borrowedSourceClientFactory?.Invoke(source)
                    ?? PackageSourceClientProvider.Create(source, client);
                using var trafficScope =
                    NetworkTelemetry.Scope(
                        NetworkTrafficKind.PackageDownload);
                PackageSourcePayloadResult sourceResult;
                try
                {
                    sourceResult = await AcquireFromSourceAsync(
                        sourceClient,
                        PackageSourceCoordinate.Create(
                            coordinate.PackageId,
                            coordinate.Version),
                        store,
                        log,
                        limits,
                        transferPolicy,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OfflineException)
                {
                    log?.Invoke(
                        "Network access is disabled (--offline mode).");
                    failedSources.Add(
                        PackageSourceDisplay.ForDiagnostics(source).ToString());
                    continue;
                }
                finally
                {
                    if (borrowedSourceClientFactory is null)
                        sourceClient.Dispose();
                }
                if (sourceResult
                    is PackageSourcePayloadResult.Acquired acquired)
                {
                    return Result(
                        acquired.Payload.Content,
                        acquired.Payload.Origin);
                }

                if (sourceResult
                    is PackageSourcePayloadResult.Failed failed)
                {
                    PackageSourceClientProvider.RecordFailure(
                        source,
                        failed.Failure,
                        NetworkTrafficKind.PackageDownload);
                    log?.Invoke(
                        $"Source {PackageSourceDisplay.ForDiagnostics(source)} did not deliver a package payload: "
                        + failed.Failure.Message);
                }
            }

            failedSources.Add(PackageSourceDisplay.ForDiagnostics(source).ToString());
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

    internal static async Task<PackageSourcePayloadResult> AcquireFromSourceAsync(
        IPackageSourceClient source,
        PackageSourceCoordinate coordinate,
        IPackageStore store,
        Action<string>? log,
        PackagePayloadLimits limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        CancellationToken cancellationToken)
    {
        PackageSourceOperationResult<PackageSourcePayload> operation =
            await source.GetPackageAsync(
            coordinate.PackageId,
            coordinate.Version,
            cancellationToken).ConfigureAwait(false);
        if (operation
            is PackageSourceOperationResult<PackageSourcePayload>.Failed failed)
        {
            if (failed.Failure.Kind != PackageSourceFailureKind.NotFound)
                return new PackageSourcePayloadResult.Failed(failed.Failure);

            return new PackageSourcePayloadResult.Unavailable(
                $"Package '{coordinate.PackageId}' version "
                + $"'{coordinate.Version}' was not supplied by the selected source.")
            {
                Kind = PackageSourcePayloadUnavailableKind.NotFound,
            };
        }

        PackageSourcePayload payload =
            ((PackageSourceOperationResult<PackageSourcePayload>.Succeeded)
            operation).Value;
        if (payload.Kind != PackageSourcePayloadKind.Package
            || payload.Coordinate != coordinate
            || payload.Producer != source.Identity)
        {
            await payload.Content.DisposeAsync().ConfigureAwait(false);
            return new PackageSourcePayloadResult.Unavailable(
                "The package source returned a payload that did not match the requested coordinate.")
            {
                Kind = PackageSourcePayloadUnavailableKind
                    .MismatchedPayload,
            };
        }

        string producerKey =
            NuGetCache.GetSourceKey(source.Identity.Value);
        IPackageContent? content;
        try
        {
            content = await TryAdmitAsync(
                payload.Content,
                payload.AdvertisedLength,
                coordinate,
                producerKey,
                $"source '{source.Kind}'",
                store,
                log,
                limits,
                transferPolicy,
                cancellationToken,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            NuGetRequestTimeoutException
            or NuGetOperationTimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PackageSourcePayloadResult.Failed(
                new PackageSourceFailure(
                    source.Identity,
                    source.Kind,
                    PackageSourceCapabilities.PackagePayload,
                    coordinate,
                    PackageSourceFailureKind.Timeout,
                    "The package source operation exceeded its configured deadline."));
        }
        if (content is null)
        {
            return new PackageSourcePayloadResult.Unavailable(
                $"Package '{coordinate.PackageId}' version "
                + $"'{coordinate.Version}' did not satisfy the payload policy.")
            {
                Kind = PackageSourcePayloadUnavailableKind.PolicyRejected,
            };
        }

        if (!content.ProducerKey.Equals(
                producerKey,
                StringComparison.Ordinal))
        {
            return new PackageSourcePayloadResult.Unavailable(
                $"Content for package '{coordinate.PackageId}' version "
                + $"'{coordinate.Version}' belongs to an unauthorized producer.")
            {
                Kind = PackageSourcePayloadUnavailableKind
                    .UnauthorizedProducer,
            };
        }

        return new PackageSourcePayloadResult.Acquired(
            new AcquiredPackageSourcePayload(
                coordinate,
                content,
                producerKey,
                PackagePayloadOrigin.Download));
    }

    static PackagePayloadLimits ValidateLimits(PackagePayloadLimits? limits)
    {
        limits ??= PackagePayloadLimits.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxEntryCount);
        return limits;
    }

    static async Task<IPackageContent?> TryAdmitAsync(
        Stream payload,
        long? advertisedLength,
        PackageSourceCoordinate coordinate,
        string producerKey,
        string sourceDescription,
        IPackageStore store,
        Action<string>? log,
        PackagePayloadLimits limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        CancellationToken bodyCancellationToken,
        CancellationToken operationCancellationToken)
    {
        await using (payload.ConfigureAwait(false))
        {
            if (advertisedLength > limits.MaxArchiveBytes)
            {
                log?.Invoke(
                    $"{sourceDescription} advertised a package payload above the configured archive limit.");
                return null;
            }

            using IPackagePayloadReservation? reservation =
                transferPolicy?.Reserve(
                    new PackagePayloadTransfer(
                        coordinate,
                        producerKey,
                        advertisedLength));
            try
            {
                byte[]? archive = advertisedLength is { } declared
                    && declared >= 0
                    && declared <= int.MaxValue
                    ? await PackageContentAdmission.ReadExactAsync(
                            payload,
                            (int)declared,
                            bodyCancellationToken)
                        .ConfigureAwait(false)
                    : await PackageContentAdmission.ReadBoundedAsync(
                            payload,
                            limits.MaxArchiveBytes,
                            bodyCancellationToken)
                        .ConfigureAwait(false);

                if (archive is null)
                {
                    log?.Invoke(
                        advertisedLength is null
                            ? $"{sourceDescription} sent a package payload above the configured archive limit."
                            : $"{sourceDescription} did not send the package payload length it advertised.");
                    return null;
                }

                if (PackageArchiveValidator.Validate(
                        archive,
                        limits,
                        operationCancellationToken)
                    is PackageArchiveValidation.Rejected rejection)
                {
                    log?.Invoke(
                        $"{sourceDescription} did not deliver a usable package payload: {rejection.Reason}");
                    return null;
                }

                IPackageContent committed = await store.CommitAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    producerKey,
                    new MemoryStream(
                        archive,
                        index: 0,
                        count: archive.Length,
                        writable: false,
                        publiclyVisible: true),
                    operationCancellationToken).ConfigureAwait(false);
                if (!await PackageContentAdmission.IsAdmissibleAsync(
                        committed,
                        limits,
                        operationCancellationToken).ConfigureAwait(false))
                {
                    log?.Invoke(
                        $"{sourceDescription} did not publish content satisfying the current payload limits.");
                    return null;
                }

                reservation?.Complete();
                return committed;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or NotSupportedException
                    || (ex is OperationCanceledException
                        && !operationCancellationToken.IsCancellationRequested))
            {
                log?.Invoke(
                    $"{sourceDescription} did not deliver a usable package payload.");
                return null;
            }
        }
    }

}
