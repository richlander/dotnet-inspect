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
        PackageEntrySelector? rangedSelection = null) =>
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
            rangedSelection);

    /// <param name="rangedSelection">
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
        PackageEntrySelector? rangedSelection = null)
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

            foreach (var (authority, client, store) in entries)
            {
                operation.ThrowIfExpired();
                if (rangedSelection is not null
                    && client is IPackageArchiveRangeSource rangedSource)
                {
                    RangedAttempt attempt = await TryAcquireRangedAsync(
                        rangedSource,
                        client,
                        authority,
                        candidate.Coordinate,
                        rangedSelection,
                        PackagePayloadAcquisition.ValidateLimits(limits),
                        operation,
                        log).ConfigureAwait(false);
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
                            continue;
                        case RangedOutcome.Failed:
                            // An expired operation ceiling surfaces through
                            // ThrowIfExpired above; a request-level failure
                            // leaves the next authority its turn.
                            failures.Add(attempt.Failure!);
                            continue;
                        case RangedOutcome.Fallback:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(attempt));
                    }
                }

                log?.Invoke(
                    $"Acquiring {candidate.Coordinate.PackageId} "
                    + $"{candidate.Coordinate.Version} from "
                    + $"{PackageSourceDisplay.ForDiagnostics(authority.Source)}.");
                try
                {
                    PackageSourcePayloadResult result =
                        await PackagePayloadAcquisition.AcquireAuthorizedAsync(
                            client,
                            candidate.Coordinate,
                            store,
                            operation,
                            log,
                            limits,
                            transferPolicy).ConfigureAwait(false);
                    operation.ThrowIfExpired();
                    RequireAuthority(client.Source, authority);
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
    /// The archive library's bounds for a ranged read, mapped from the
    /// payload limits: the archive total, the entry count, the aggregate
    /// expanded bound, the library's default directory cap, and the largest
    /// entry-read slack so an entry whose local header is longer than its
    /// central record still costs one request.
    /// </summary>
    internal static ZipReadLimits RangedLimits(PackagePayloadLimits limits) =>
        new(
            maxArchiveBytes: limits.MaxArchiveBytes,
            maxEntryCount: limits.MaxEntryCount,
            maxDirectoryBytes: ZipReadLimits.Default.MaxDirectoryBytes,
            maxExpandedBytes: limits.MaxExpandedBytes,
            entryReadSlack: ZipReadLimits.MaxEntryReadSlack);

    private static async Task<RangedAttempt> TryAcquireRangedAsync(
        IPackageArchiveRangeSource rangedSource,
        IPackageSourceClient client,
        ConfiguredPackageAuthority authority,
        PackageSourceCoordinate coordinate,
        PackageEntrySelector selectEntries,
        PackagePayloadLimits limits,
        NuGetOperationContext operation,
        Action<string>? log)
    {
        InertString display = PackageSourceDisplay.ForDiagnostics(authority.Source);
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
            long remaining = limits.MaxExpandedBytes;
            foreach (ZipEntry entry in targets)
            {
                operation.ThrowIfExpired();
                PackageArchiveReadResult<PackageArchiveEntryContent> read =
                    await reader.ReadEntryAsync(
                        entry,
                        maxExpandedBytes: remaining,
                        operation.CancellationToken).ConfigureAwait(false);
                if (read.Value is not { } content)
                {
                    return ClassifyRanged(
                        read.Refusal,
                        read.Failure,
                        authority,
                        client,
                        $"reading entry '{entry.Name}'",
                        log);
                }
                materialized[entry.Name] = content.Content;
                remaining -= content.Content.Length;
            }

            RangedPackageContent retained = directory.WithMaterialized(materialized);
            log?.Invoke(
                $"Read {coordinate.PackageId} {coordinate.Version} by range from {display}: "
                + $"{materialized.Count} of {entries.Count} entries, "
                + $"{retained.MaterializedBytes} of {entries.Sum(static entry => entry.Length)} expanded bytes; "
                + "nothing was cached.");
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
        if (failure.Kind == PackageSourceFailureKind.NotFound)
            return new(RangedOutcome.NotFound);
        return new(
            RangedOutcome.Failed,
            Failure: DescribePayloadFailure(authority.Source, failure));
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
