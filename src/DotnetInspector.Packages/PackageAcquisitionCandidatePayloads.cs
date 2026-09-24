using System.Collections.ObjectModel;
using InertText;
using NuGetFetch;
using ZipFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One exact payload and its configured source identity, or attributed
/// failures.
/// </summary>
public sealed class ConfiguredPackagePayloadResult
{
    internal ConfiguredPackagePayloadResult(
        ConfiguredPackageAuthority? authority,
        PackageSourceResultIdentity? source,
        AcquiredPackageSourcePayload? payload,
        IReadOnlyList<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageAuthority>? notFoundAuthorities = null,
        IReadOnlyList<ConfiguredPackageAuthority>? reportingAuthorities = null,
        bool selectionUsesOriginalSources = false,
        PackageHouseSettlement? houseSettlement = null)
    {
        if ((authority is null) != (source is null)
            || (authority is null) != (payload is null))
        {
            throw new ArgumentException(
                "An acquired configured payload requires its authority and exact source identity.");
        }
        if (authority is not null
            && !ReferenceEquals(
                source!.Association,
                authority.Association))
        {
            throw new ArgumentException(
                "The configured payload source must belong to its authority.",
                nameof(source));
        }
        if (payload is not null
            && payload.LegacyProducerKey is null)
        {
            payload = payload.WithLegacyProducerKey(
                NuGetCache.GetSourceKey(authority!.Source.Url));
        }
        if (payload is not null
            && (!payload.ProducerKey.Equals(
                    source!.Producer.Key,
                    StringComparison.Ordinal)
                || payload.Producer != source.Producer))
        {
            throw new ArgumentException(
                "The configured payload and source identify different producers.",
                nameof(payload));
        }
        if (payload is not null
            && houseSettlement is not null
            && (houseSettlement is not PackageHouseSettlement.Acquired acquired
                || !ReferenceEquals(acquired.Payload, payload)))
        {
            throw new ArgumentException(
                "A configured payload House settlement must retain the exact acquired payload.",
                nameof(houseSettlement));
        }
        if (payload is null
            && houseSettlement is PackageHouseSettlement.Acquired)
        {
            throw new ArgumentException(
                "An acquired House settlement requires its payload.",
                nameof(houseSettlement));
        }

        Authority = authority;
        Source = source;
        Payload = payload;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>([.. failures]);
        NotFoundAuthorities = new ReadOnlyCollection<ConfiguredPackageAuthority>(
            [.. notFoundAuthorities ?? []]);
        ReportingAuthorities = reportingAuthorities is null
            ? null
            : new ReadOnlyCollection<ConfiguredPackageAuthority>(
                [.. reportingAuthorities]);
        SelectionUsesOriginalSources = selectionUsesOriginalSources;
        HouseSettlement = houseSettlement;
    }

    public ConfiguredPackageAuthority? Authority { get; }
    public PackageSourceResultIdentity? Source { get; }
    public AcquiredPackageSourcePayload? Payload { get; }
    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }
    public IReadOnlyList<ConfiguredPackageAuthority> NotFoundAuthorities { get; }
    internal IReadOnlyList<ConfiguredPackageAuthority>? ReportingAuthorities
        { get; }
    internal bool SelectionUsesOriginalSources { get; }
    internal PackageHouseSettlement? HouseSettlement { get; }
}

/// <summary>
/// Acquires admitted retained payloads only through candidates issued by one
/// package-owned candidate issuer.
/// </summary>
internal sealed class PackageAcquisitionCandidatePayloadAcquirer
{
    private readonly PackageAcquisitionCandidateIssuer _issuer;
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;

    public PackageAcquisitionCandidatePayloadAcquirer(
        PackageAcquisitionCandidateIssuer issuer,
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(getClient);
        _issuer = issuer;
        _getClient = getClient;
    }

    public Task<ConfiguredPackagePayloadResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        NuGetOperationContext? operationContext = null,
        PackageRangedRead? rangedRead = null) =>
        AcquireAsync(
            candidate,
            createStore,
            log,
            limits,
            cancellationToken,
            transferPolicy,
            operationContext,
            failures: [],
            selectionUsesOriginalSources: false,
            rangedRead);

    /// <param name="rangedRead">
    /// When supplied, an authority whose client exposes
    /// <see cref="IPackageArchiveRangeSource"/> is read by range on a cache
    /// miss: the archive directory first, then only the entries this selector
    /// chooses. A refusal falls back to the complete fetch on the same
    /// authority.
    /// </param>
    internal async Task<ConfiguredPackagePayloadResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        Action<string>? log,
        PackagePayloadLimits? limits,
        CancellationToken cancellationToken,
        IPackagePayloadTransferPolicy? transferPolicy,
        NuGetOperationContext? operationContext,
        List<PackageAuthorityFailure> failures,
        bool selectionUsesOriginalSources,
        PackageRangedRead? rangedRead = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(createStore);
        ArgumentNullException.ThrowIfNull(failures);
        if (!_issuer.OwnsCandidate(candidate))
        {
            throw new InvalidOperationException(
                "The package acquisition candidate belongs to another issuer.");
        }

        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new NuGetOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = ResolveInvocationToken(
            operation,
            cancellationToken);
        var notFoundAuthorities = new List<ConfiguredPackageAuthority>();
        try
        {
            operation.ThrowIfExpired();
            List<(
                ConfiguredPackageAuthority Authority,
                IPackageSourceClient Client,
                IPackageStore Store)> entries = [];
            foreach (PackageAcquisitionAuthorityEvidence evidence in
                     PackageAcquisitionCandidateManifestAcquirer
                         .OrderAuthorities(candidate.Authorities))
            {
                operation.ThrowIfExpired();
                ConfiguredPackageAuthority authority = evidence.Authority;
                IPackageSourceClient client = _getClient(authority)
                    ?? throw new InvalidOperationException(
                        "The package source client factory returned null.");
                RequireAuthority(client.Source, authority);
                IPackageStore store = createStore(
                    authority,
                    client.Source.Producer)
                    ?? throw new InvalidOperationException(
                        "The package store factory returned null.");
                entries.Add((authority, client, store));
            }
            ConfiguredPackageAuthority[]? selectedAuthorities =
                candidate.Kind == PackageAcquisitionCandidateKind.Discovered
                    ? [.. entries.Select(item => item.Authority)]
                    : null;

            // Every authorized cache is consulted before cold acquisition.
            foreach (var (authority, client, store) in entries)
            {
                operation.ThrowIfExpired();
                AcquiredPackageSourcePayload? cached =
                    await PackagePayloadAcquisition.TryGetCachedAsync(
                        candidate.Coordinate,
                        client.Source.Producer,
                        client.Source.Producer.Key,
                        store,
                        limits,
                        log,
                        operation.OperationToken).ConfigureAwait(false);
                operation.ThrowIfExpired();
                RequireAuthority(client.Source, authority);
                if (cached is not null)
                {
                    return new(
                        authority,
                        client.Source,
                        cached,
                        failures,
                        reportingAuthorities: selectedAuthorities,
                        selectionUsesOriginalSources:
                            selectionUsesOriginalSources);
                }
            }

            // Then every authorized entry cache, in the same order: a ranged
            // read whose directory and every selected entry are cached answers
            // with no request (docs/design/package-cache-policy.md).
            var entryStates = new Dictionary<ConfiguredPackageAuthority, EntryCacheState>(
                ReferenceEqualityComparer.Instance);
            if (rangedRead is not null)
            {
                foreach (var (authority, client, store) in entries)
                {
                    operation.ThrowIfExpired();
                    if (store is not IPackageEntryStore { KeepsEntries: true } entryStore
                        || client is not IPackageArchiveRangeSource)
                    {
                        continue;
                    }
                    EntryCacheState? state = ReadEntryCache(
                        entryStore,
                        candidate.Coordinate,
                        client.Source.Producer.Key,
                        rangedRead,
                        PackagePayloadAcquisition.ValidateLimits(limits),
                        log);
                    if (state is null)
                        continue;
                    if (state.Complete is { } complete)
                    {
                        RequireAuthority(client.Source, authority);
                        return new(
                            authority,
                            client.Source,
                            new AcquiredPackageSourcePayload(
                                candidate.Coordinate,
                                complete,
                                client.Source.Producer.Key,
                                client.Source.Producer,
                                PackagePayloadOrigin.Cache),
                            failures,
                            reportingAuthorities: selectedAuthorities,
                            selectionUsesOriginalSources:
                                selectionUsesOriginalSources);
                    }
                    entryStates[authority] = state;
                }
            }

            foreach (var (authority, client, store) in entries)
            {
                operation.ThrowIfExpired();
                // Size first: with ranged access on a source that can serve
                // ranges, the complete fetch is abandoned before its body when
                // the archive is above the cut, and the archive is read by
                // range instead (docs/design/package-cache-policy.md).
                IPackageArchiveRangeSource? rangedSource =
                    rangedRead is not null
                        ? client as IPackageArchiveRangeSource
                        : null;
                long? sizeGate = rangedSource is null ? null : rangedRead!.SizeCut;
                entryStates.TryGetValue(authority, out EntryCacheState? cachedState);
                if (cachedState is { Invalid: true })
                {
                    // An invalid cached item is preserved and bypassed: the
                    // complete fetch answers, and its store answers first from
                    // then on.
                    rangedSource = null;
                    sizeGate = null;
                }
                // A cached directory already records the archive's length, so
                // the missing entries are read by range with no size probe.
                bool rangedFirst = rangedSource is not null && cachedState is not null;
                bool nextAuthority = false;
                while (!nextAuthority)
                {
                    nextAuthority = true;
                    log?.Invoke(
                        $"Acquiring {candidate.Coordinate.PackageId} "
                        + $"{candidate.Coordinate.Version} from "
                        + $"{PackageSourceDisplay.ForDiagnostics(authority.Source)}.");
                    try
                    {
                        PackageSourcePayloadResult? result = null;
                        if (!rangedFirst)
                        {
                            result = await PackagePayloadAcquisition.AcquireAuthorizedAsync(
                                client,
                                candidate.Coordinate,
                                store,
                                operation,
                                log,
                                limits,
                                transferPolicy,
                                abandonAbove: sizeGate).ConfigureAwait(false);
                            operation.ThrowIfExpired();
                            RequireAuthority(client.Source, authority);
                        }
                        if (rangedFirst
                            || result is PackageSourcePayloadResult.Oversized)
                        {
                            if (result is PackageSourcePayloadResult.Oversized oversized)
                            {
                                log?.Invoke(
                                    $"{candidate.Coordinate.PackageId} {candidate.Coordinate.Version} "
                                    + $"is {oversized.AdvertisedLength} bytes, above the "
                                    + $"{rangedRead!.SizeCut}-byte cut; reading it by range.");
                            }
                            rangedFirst = false;
                            sizeGate = null;
                            RangedAttempt attempt = await TryAcquireRangedAsync(
                                rangedSource!,
                                client,
                                authority,
                                candidate.Coordinate,
                                rangedRead!.SelectEntries,
                                PackagePayloadAcquisition.ValidateLimits(limits),
                                operation,
                                log,
                                store as IPackageEntryStore,
                                cachedState).ConfigureAwait(false);
                            operation.ThrowIfExpired();
                            RequireAuthority(client.Source, authority);
                            switch (attempt.Outcome)
                            {
                                case RangedOutcome.Acquired:
                                    return new(
                                        authority,
                                        client.Source,
                                        attempt.Payload!,
                                        failures,
                                        notFoundAuthorities,
                                        selectedAuthorities,
                                        selectionUsesOriginalSources);
                                case RangedOutcome.NotFound:
                                    notFoundAuthorities.Add(authority);
                                    break;
                                case RangedOutcome.Failed:
                                    // An expired operation ceiling surfaces through
                                    // ThrowIfExpired above; a request-level failure
                                    // leaves the next authority its turn.
                                    failures.Add(attempt.Failure!);
                                    break;
                                case RangedOutcome.Fallback:
                                    // The complete fetch, without the size gate.
                                    nextAuthority = false;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException(
                                        nameof(attempt));
                            }
                            continue;
                        }
                        if (result is PackageSourcePayloadResult.Acquired acquired)
                        {
                            return new(
                                authority,
                                client.Source,
                                acquired.Payload,
                                failures,
                                notFoundAuthorities,
                                selectedAuthorities,
                                selectionUsesOriginalSources);
                        }
                        if (result is PackageSourcePayloadResult.Failed failed)
                        {
                            RequireAuthority(
                                failed.Failure.Source,
                                authority,
                                client.Source);
                            failures.Add(
                                DescribePayloadFailure(
                                    authority.Source,
                                    failed.Failure));
                        }
                        else if (result is PackageSourcePayloadResult.Unavailable
                                 unavailable)
                        {
                            if (unavailable.IsNotFound)
                            {
                                notFoundAuthorities.Add(authority);
                            }
                            else
                            {
                                failures.Add(new PackageAuthorityFailure(
                                    PackageSourceDisplay.ForDiagnostics(
                                        authority.Source),
                                    PackageAuthorityFailureKind.ResponseRejected,
                                    "The selected source did not supply a payload satisfying the package policy.")
                                {
                                    ResultSource = client.Source,
                                });
                            }
                        }
                    }
                    catch (PackageSourceStreamException exception)
                    {
                        RequireAuthority(
                            exception.ResultSource,
                            authority,
                            client.Source);
                        failures.Add(new PackageAuthorityFailure(
                            PackageSourceDisplay.ForDiagnostics(authority.Source),
                            ClassifySourceFailure(exception.Kind),
                            exception.Message)
                        {
                            ResultSource = exception.ResultSource,
                            Timeout = exception.Timeout,
                        });
                        if (exception.Timeout?.Kind
                            == PackageSourceTimeoutKind.Operation)
                        {
                            return new(
                                null,
                                null,
                                null,
                                failures,
                                notFoundAuthorities);
                        }
                    }
                }
            }
            operation.ThrowIfExpired();
            return new(null, null, null, failures, notFoundAuthorities);
        }
        catch (NuGetOperationTimeoutException)
        {
            return PayloadOperationTimedOut(
                operation,
                failures,
                notFoundAuthorities);
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                operation.CancellationToken);
        }
        catch (OperationCanceledException)
            when (operation.OperationToken.IsCancellationRequested)
        {
            return PayloadOperationTimedOut(
                operation,
                failures,
                notFoundAuthorities);
        }
    }

    private enum RangedOutcome
    {
        Acquired,
        NotFound,
        Failed,
        Fallback,
    }

    private readonly record struct RangedAttempt(
        RangedOutcome Outcome,
        AcquiredPackageSourcePayload? Payload = null,
        PackageAuthorityFailure? Failure = null);

    /// <summary>
    /// The entry-read slack a ranged read adds to each entry request. Real
    /// packages carry local extra fields at most a few dozen bytes longer
    /// than their central records (PCLStorage 1.0.2: 28), so one KiB keeps
    /// each entry to one request while costing each entry one KiB, not the
    /// reader's 64 KiB maximum.
    /// </summary>
    internal const int RangedEntryReadSlack = 1024;


    /// <summary>
    /// The unselected bytes a ranged read transfers to join two selected
    /// entries into one request. At 64 KiB the `Avalonia` 12.1.2 `net10.0`
    /// selection takes 7 requests instead of 22 for 163 KB more transfer.
    /// </summary>
    internal const int RangedEntryMergeGap = 64 * 1024;

    /// <summary>The ranged requests one acquisition keeps in flight at once.</summary>
    internal const int RangedConcurrentReads = 6;

    /// <summary>
    /// The archive library's bounds for a ranged read, mapped from the
    /// payload limits: the archive total, the entry count, the aggregate
    /// expanded bound, the library's default directory cap, and
    /// <see cref="RangedEntryReadSlack"/>.
    /// </summary>
    internal static ZipReadLimits RangedLimits(PackagePayloadLimits limits) =>
        new(
            maxArchiveBytes: limits.MaxArchiveBytes,
            maxEntryCount: limits.MaxEntryCount,
            maxDirectoryBytes: ZipReadLimits.Default.MaxDirectoryBytes,
            maxExpandedBytes: limits.MaxExpandedBytes,
            entryReadSlack: RangedEntryReadSlack,
            entryMergeGap: RangedEntryMergeGap,
            maxConcurrentReads: RangedConcurrentReads);

    private static async Task<RangedAttempt> TryAcquireRangedAsync(
        IPackageArchiveRangeSource rangedSource,
        IPackageSourceClient client,
        ConfiguredPackageAuthority authority,
        PackageSourceCoordinate coordinate,
        PackageEntrySelector selectEntries,
        PackagePayloadLimits limits,
        NuGetOperationContext operation,
        Action<string>? log,
        IPackageEntryStore? entryStore = null,
        EntryCacheState? cachedState = null)
    {
        InertString display = PackageSourceDisplay.ForDiagnostics(authority.Source);
        if (entryStore is { KeepsEntries: false })
            entryStore = null;
        log?.Invoke(
            $"Reading {coordinate.PackageId} {coordinate.Version} by range from {display}.");
        PackageArchiveReadResult<PackageArchiveReader> open =
            await rangedSource.OpenArchiveAsync(
                coordinate.PackageId,
                coordinate.Version,
                RangedLimits(limits),
                operation.CancellationToken,
                operation).ConfigureAwait(false);
        if (open.Value is not { } reader)
        {
            return ClassifyRanged(
                open.Refusal,
                open.Failure,
                authority,
                client,
                "opening the archive directory",
                log);
        }

        await using (reader.ConfigureAwait(false))
        {
            RequireAuthority(reader.Source, authority, client.Source);
            if (cachedState is not null
                && (reader.Directory.ArchiveLength != cachedState.Directory.ArchiveLength
                    || !reader.Directory.Region.Span.SequenceEqual(
                        cachedState.Directory.Region.Span)))
            {
                // The archive changed since its directory was cached: take the
                // complete fetch, as ArchiveChanged means. No cached item is
                // replaced.
                log?.Invoke(
                    $"The archive of {coordinate.PackageId} {coordinate.Version} at {display} "
                    + "differs from its cached directory; acquiring the complete archive instead.");
                return new(RangedOutcome.Fallback);
            }
            IReadOnlyList<PackageContentEntry> entries =
                DirectoryEntries(reader.Directory);
            string producerKey = client.Source.Producer.Key;
            RangedPackageContent directory =
                RangedPackageContent.CreateDirectory(entries, producerKey);
            IReadOnlyList<string> selected =
                selectEntries(directory)
                ?? throw new InvalidOperationException(
                    "The ranged entry selector returned null.");

            var targets = new List<ZipEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            long declared = 0;
            foreach (string path in selected)
            {
                if (!seen.Add(path))
                    continue;
                if (cachedState?.Cached.ContainsKey(path) == true)
                    continue;
                ZipEntry entry =
                    reader.Directory.Find(path)
                    ?? throw new InvalidOperationException(
                        $"The ranged entry selection named '{path}', which is not in the archive directory.");
                declared += entry.ExpandedLength;
                targets.Add(entry);
            }
            if (declared > limits.MaxExpandedBytes)
            {
                return new(
                    RangedOutcome.Failed,
                    Failure: new PackageAuthorityFailure(
                        display,
                        PackageAuthorityFailureKind.ResponseRejected,
                        $"The selected entries declare {declared} expanded bytes, above the payload limit of {limits.MaxExpandedBytes}.")
                    {
                        ResultSource = client.Source,
                    });
            }

            var materialized = new Dictionary<string, ReadOnlyMemory<byte>>(
                StringComparer.Ordinal);
            if (cachedState is not null)
            {
                foreach (KeyValuePair<string, ReadOnlyMemory<byte>> item in cachedState.Cached)
                    materialized[item.Key] = item.Value;
            }
            operation.ThrowIfExpired();
            PackageArchiveReadResult<IReadOnlyList<PackageArchiveEntryContent>> read =
                await reader.ReadEntriesAsync(
                    targets,
                    maxTotalExpandedBytes: limits.MaxExpandedBytes,
                    operation.CancellationToken).ConfigureAwait(false);
            if (read.Value is not { } contents)
            {
                return ClassifyRanged(
                    read.Refusal,
                    read.Failure,
                    authority,
                    client,
                    $"reading {targets.Count} selected entries",
                    log);
            }
            foreach (PackageArchiveEntryContent content in contents)
                materialized[content.Entry.Name] = content.Content;

            if (entryStore is not null)
            {
                if (cachedState is null)
                {
                    entryStore.PublishDirectory(
                        coordinate.PackageId,
                        coordinate.Version,
                        reader.Directory.Region,
                        reader.Directory.ArchiveLength);
                }
                foreach (PackageArchiveEntryContent content in contents)
                {
                    entryStore.PublishEntry(
                        coordinate.PackageId,
                        coordinate.Version,
                        content.Entry.Name,
                        content.Content);
                }
            }

            RangedPackageContent retained = directory.WithMaterialized(materialized);
            int fromCache = materialized.Count - contents.Count;
            log?.Invoke(
                $"Read {coordinate.PackageId} {coordinate.Version} by range from {display}: "
                + $"{materialized.Count} of {entries.Count} entries"
                + (fromCache > 0 ? $" ({fromCache} from the entry cache)" : "")
                + $", {retained.MaterializedBytes} of {entries.Sum(static entry => entry.Length)} expanded bytes; "
                + (entryStore is null
                    ? "nothing was cached."
                    : $"{contents.Count} entries kept in the entry cache."));
            return new(
                RangedOutcome.Acquired,
                new AcquiredPackageSourcePayload(
                    coordinate,
                    retained,
                    producerKey,
                    client.Source.Producer,
                    PackagePayloadOrigin.Ranged));
        }
    }

    /// <summary>
    /// One authority's entry cache for a coordinate: its cached directory and
    /// the selected entries it holds, a complete answer when it holds all of
    /// them, or invalid when a cached item fails its checks.
    /// </summary>
    private sealed record EntryCacheState(
        ZipDirectory Directory,
        Dictionary<string, ReadOnlyMemory<byte>> Cached,
        bool Invalid = false,
        RangedPackageContent? Complete = null);

    private static EntryCacheState? ReadEntryCache(
        IPackageEntryStore entryStore,
        PackageSourceCoordinate coordinate,
        string producerKey,
        PackageRangedRead rangedRead,
        PackagePayloadLimits limits,
        Action<string>? log)
    {
        if (!entryStore.TryReadDirectory(
                coordinate.PackageId,
                coordinate.Version,
                out ReadOnlyMemory<byte> region,
                out long archiveLength))
        {
            return null;
        }

        var cached = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        ZipDirectory directory;
        try
        {
            directory = ZipArchiveReader.ReadDirectoryFromRegion(
                region,
                archiveLength,
                RangedLimits(limits));
        }
        catch (ZipReadException exception)
        {
            log?.Invoke(
                $"The cached directory of {coordinate.PackageId} {coordinate.Version} "
                + $"cannot be read ({exception.Message}); it is kept and bypassed.");
            return new EntryCacheState(null!, cached, Invalid: true);
        }

        RangedPackageContent directoryView =
            RangedPackageContent.CreateDirectory(DirectoryEntries(directory), producerKey);
        IReadOnlyList<string> selected =
            rangedRead.SelectEntries(directoryView)
            ?? throw new InvalidOperationException(
                "The ranged entry selector returned null.");
        bool complete = true;
        foreach (string path in selected.Distinct(StringComparer.Ordinal))
        {
            if (directory.Find(path) is not { } entry
                || !entryStore.TryReadEntry(
                    coordinate.PackageId,
                    coordinate.Version,
                    path,
                    out byte[] content))
            {
                complete = false;
                continue;
            }
            if (!ZipArchiveReader.MatchesEntry(entry, content))
            {
                log?.Invoke(
                    $"The cached entry '{path}' of {coordinate.PackageId} {coordinate.Version} "
                    + "does not match its directory; it is kept and bypassed.");
                return new EntryCacheState(directory, cached, Invalid: true);
            }
            cached[path] = content;
        }

        if (!complete)
            return new EntryCacheState(directory, cached);
        log?.Invoke(
            $"Read {coordinate.PackageId} {coordinate.Version} from the entry cache: "
            + $"{cached.Count} entries, no request.");
        return new EntryCacheState(
            directory,
            cached,
            Complete: directoryView.WithMaterialized(cached));
    }

    private static RangedAttempt ClassifyRanged(
        PackageArchiveReadRefusal? refusal,
        PackageSourceFailure? failure,
        ConfiguredPackageAuthority authority,
        IPackageSourceClient client,
        string step,
        Action<string>? log)
    {
        if (refusal is { } reason)
        {
            log?.Invoke(
                $"Ranged read of {PackageSourceDisplay.ForDiagnostics(authority.Source)} "
                + $"while {step} was refused ({reason}); acquiring the complete archive instead.");
            return new(RangedOutcome.Fallback);
        }

        if (failure is null)
        {
            throw new InvalidOperationException(
                "The ranged read completed without a value, failure, or refusal.");
        }
        RequireAuthority(failure.Source, authority, client.Source);
        switch (failure.Kind)
        {
            case PackageSourceFailureKind.NotFound:
                return new(RangedOutcome.NotFound);

            // The complete fetch would answer the same way: the credential
            // is refused, or the archive exceeds the same payload limits.
            case PackageSourceFailureKind.AuthenticationRequired:
            case PackageSourceFailureKind.ResponseRejected:
                return new(
                    RangedOutcome.Failed,
                    Failure: DescribePayloadFailure(authority.Source, failure));

            // A ranged read is an optimization. A source that answers ranges
            // with an error status, a malformed partial response, or a
            // stalled request may still serve the whole archive, which the
            // complete path validates on its own. An expired operation
            // ceiling never reaches here: the caller checks it first.
            default:
                log?.Invoke(
                    $"Ranged read of {PackageSourceDisplay.ForDiagnostics(authority.Source)} "
                    + $"while {step} failed ({failure.Kind}); acquiring the complete archive instead.");
                return new(RangedOutcome.Fallback);
        }
    }

    private static IReadOnlyList<PackageContentEntry> DirectoryEntries(
        ZipDirectory directory)
    {
        var entries = new List<PackageContentEntry>(directory.Entries.Count);
        foreach (ZipEntry entry in directory.Entries)
        {
            if (entry.Name.Length == 0 || entry.Name.EndsWith('/'))
                continue;
            entries.Add(new PackageContentEntry(entry.Name, entry.ExpandedLength));
        }
        return entries.AsReadOnly();
    }

    private static ConfiguredPackagePayloadResult PayloadOperationTimedOut(
        NuGetOperationContext operation,
        List<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageAuthority>? notFoundAuthorities = null)
    {
        failures.Add(new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Timeout,
            "The package payload operation deadline expired before acquisition completed.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        });
        return new(null, null, null, failures, notFoundAuthorities);
    }

    private static PackageAuthorityFailure DescribePayloadFailure(
        PackageSource source,
        PackageSourceFailure failure) =>
        new(
            PackageSourceDisplay.ForDiagnostics(source),
            ClassifySourceFailure(failure.Kind),
            failure.Message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };

    private static PackageAuthorityFailureKind ClassifySourceFailure(
        PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.AuthenticationRequired =>
                PackageAuthorityFailureKind.AuthenticationRequired,
            PackageSourceFailureKind.Timeout =>
                PackageAuthorityFailureKind.Timeout,
            PackageSourceFailureKind.Unsupported =>
                PackageAuthorityFailureKind.Unsupported,
            PackageSourceFailureKind.InvalidResponse =>
                PackageAuthorityFailureKind.InvalidResponse,
            PackageSourceFailureKind.ResponseRejected =>
                PackageAuthorityFailureKind.ResponseRejected,
            PackageSourceFailureKind.Transport =>
                PackageAuthorityFailureKind.Transport,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void RequireAuthority(
        PackageSourceResultIdentity result,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity? expectedSource = null)
    {
        if (!ReferenceEquals(
                result.Association,
                authority.Association)
            || (expectedSource is not null
                && !ReferenceEquals(result, expectedSource)))
        {
            throw new InvalidOperationException(
                "The package source result belongs to another configured authority or client.");
        }
    }

    private static CancellationToken ResolveInvocationToken(
        NuGetOperationContext operationContext,
        CancellationToken invocationToken)
    {
        if (invocationToken != default
            && invocationToken != operationContext.CancellationToken)
        {
            throw new ArgumentException(
                "The invocation token must match the operation context's caller token.",
                nameof(invocationToken));
        }

        return operationContext.CancellationToken;
    }
}
