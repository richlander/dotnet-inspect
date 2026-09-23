using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Cache;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>Tunables for <see cref="PackageVersionService"/>.</summary>
public sealed class PackageVersionServiceOptions
{
    /// <summary>The base freshness window before jitter.</summary>
    public TimeSpan BaseWindow { get; init; } = TimeSpan.FromHours(1);

    /// <summary>The deterministic per-key jitter, as a fraction of the base window.</summary>
    public double JitterFraction { get; init; } = 0.2;

    /// <summary>The time bound for one budgeted refresh of a past-window entry.</summary>
    public TimeSpan RefreshBound { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (BaseWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(BaseWindow));
        if (JitterFraction is < 0 or >= 1 || double.IsNaN(JitterFraction))
            throw new ArgumentOutOfRangeException(nameof(JitterFraction));
        if (RefreshBound <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshBound));
    }
}

/// <summary>
/// The per-invocation cap on synchronous refreshes of past-window entries.
/// One instance is shared by every request an invocation settles.
/// </summary>
public sealed class PackageVersionRefreshBudget
{
    private readonly object _gate = new();

    public PackageVersionRefreshBudget(int cap = 8)
    {
        if (cap < 0)
            throw new ArgumentOutOfRangeException(nameof(cap));
        Cap = cap;
    }

    public int Cap { get; }

    public int Used { get; private set; }

    internal bool TryTake()
    {
        lock (_gate)
        {
            if (Used >= Cap)
                return false;
            Used++;
            return true;
        }
    }
}

/// <summary>
/// The source-side handles one settlement uses. The House supplies them from
/// its operation lease; the contract suite supplies fakes.
/// </summary>
public sealed class PackageVersionServiceOperation
{
    /// <summary>
    /// Discovers versions through the authorized sources within the given
    /// scope. A terminal discovery (no prior to fall back on) is unbounded;
    /// a refresh carries the per-refresh bound, and a source that cannot
    /// observe the token honors <see cref="PackageVersionDiscoveryScope.Bound"/>
    /// directly.
    /// </summary>
    public required Func<PackageVersionDiscoveryScope, Task<PackageVersionDiscoveryResult>> Discover { get; init; }

    /// <summary>Issues the current generation's pinned candidate for one coordinate.</summary>
    public required Func<PackageSourceCoordinate, PackageAcquisitionCandidateResult> Pin { get; init; }

    /// <summary>The invocation-scoped refresh budget.</summary>
    public required PackageVersionRefreshBudget Budget { get; init; }

    /// <summary>Consumer offline policy: serve any prior regardless of age.</summary>
    public bool Offline { get; init; }
}

/// <summary>
/// The time scope of one discovery the service requests: the caller's token
/// and, for a refresh, the per-refresh bound after which the prior is served
/// instead.
/// </summary>
public readonly record struct PackageVersionDiscoveryScope(
    TimeSpan? Bound,
    CancellationToken CancellationToken);

/// <summary>
/// The per-invocation service, budget, and offline policy a House consults
/// for its selecting demands. A composition creates one per invocation so
/// the refresh cap is shared by every House it issues.
/// </summary>
public sealed class PackageVersionServicePlan
{
    public PackageVersionServicePlan(
        PackageVersionService service,
        PackageVersionRefreshBudget budget,
        bool offline = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(budget);
        Service = service;
        Budget = budget;
        Offline = offline;
    }

    public PackageVersionService Service { get; }

    public PackageVersionRefreshBudget Budget { get; }

    public bool Offline { get; }
}

/// <summary>Which path produced a settlement.</summary>
public enum PackageVersionServicePath
{
    Discovered,
    PriorWithinWindow,
    PriorAfterRefreshFailure,
    PriorBudgetExhausted,
    PriorOffline,
}

/// <summary>One settlement and the path that produced it.</summary>
public sealed class PackageVersionServiceSettlement
{
    internal PackageVersionServiceSettlement(
        PackageVersionResolutionReceipt receipt,
        PackageVersionServicePath path,
        bool entryWritten,
        bool entryEvicted,
        IReadOnlyList<PackageAuthorityFailure>? pinFailures = null)
    {
        Receipt = receipt;
        Path = path;
        EntryWritten = entryWritten;
        EntryEvicted = entryEvicted;
        PinFailures = pinFailures ?? [];
    }

    public PackageVersionResolutionReceipt Receipt { get; }

    public PackageVersionServicePath Path { get; }

    public bool EntryWritten { get; }

    public bool EntryEvicted { get; }

    /// <summary>
    /// The failures of a pin the current generation refused for a retained
    /// prior. The prior was not served; the settlement came from discovery,
    /// and these failures belong on it beside the discovery's own.
    /// </summary>
    public IReadOnlyList<PackageAuthorityFailure> PinFailures { get; }
}

/// <summary>
/// The join of the persistent cache and package-source discovery under the
/// consistency principles of Package Version Selection. Given one request,
/// its authorization, and its discovery contract, the service settles from
/// discovery or from a retained prior settlement and says which. It is the
/// only place that decides between the two, and therefore the only place
/// that makes <c>Name</c> and <c>Name@latest</c> behave differently.
/// See <c>docs/design/package-version-service.md</c>.
/// </summary>
public sealed class PackageVersionService
{
    public const string StoreCategoryPrefix = "package-version-priors-v";

    public const string StoreCategory = "package-version-priors-v1";

    private const string KeyNamespace = "prior-v1";

    private readonly TimeProvider _time;

    private readonly PackageVersionServiceOptions _options;

    public PackageVersionService(
        TimeProvider? timeProvider = null,
        PackageVersionServiceOptions? options = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _options = options ?? new PackageVersionServiceOptions();
        _options.Validate();
    }

    /// <summary>
    /// Settles one request per the decision table: an always-check request or
    /// a request with no prior discovers; a prior inside its window is served;
    /// a prior past its window is served after a budgeted refresh fails.
    /// </summary>
    public async Task<PackageVersionServiceSettlement> SettleAsync(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        PackageVersionServiceOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        bool requiresRefresh =
            request.Discovery.Freshness
                == PackageVersionDiscoveryFreshness.RefreshedForRequest;
        string? key = StoreKey(request, authorization, contract);
        PriorEntry? entry = key is null || requiresRefresh
            ? null
            : ReadEntry(key);

        if (entry is null)
        {
            return await DiscoverAndRecordAsync(
                request,
                authorization,
                contract,
                operation,
                key,
                cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _time.GetUtcNow();
        TimeSpan age = now - entry.WrittenAt;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (operation.Offline)
        {
            return await ServePriorOrDiscoverAsync(
                request,
                authorization,
                contract,
                operation,
                key!,
                entry,
                PackageVersionServicePath.PriorOffline,
                PackageVersionDiscoveryFreshness.ServedPrior,
                age,
                cancellationToken);
        }

        if (age <= WindowFor(key!))
        {
            return await ServePriorOrDiscoverAsync(
                request,
                authorization,
                contract,
                operation,
                key!,
                entry,
                PackageVersionServicePath.PriorWithinWindow,
                PackageVersionDiscoveryFreshness.Current,
                age: null,
                cancellationToken);
        }

        if (!operation.Budget.TryTake())
        {
            return await ServePriorOrDiscoverAsync(
                request,
                authorization,
                contract,
                operation,
                key!,
                entry,
                PackageVersionServicePath.PriorBudgetExhausted,
                PackageVersionDiscoveryFreshness.ServedPrior,
                age,
                cancellationToken);
        }

        PackageVersionDiscoveryResult? discovery = null;
        using (var bound = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken))
        {
            bound.CancelAfter(_options.RefreshBound);
            try
            {
                discovery = await operation.Discover(
                    new PackageVersionDiscoveryScope(_options.RefreshBound, bound.Token))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (bound.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                discovery = null;
            }
            catch (TimeoutException)
                when (!cancellationToken.IsCancellationRequested)
            {
                // A source that bounds itself by deadline rather than by the
                // token reports the expired refresh this way; the prior
                // answers, and the caller's own deadline is checked by the
                // caller after settlement.
                discovery = null;
            }
        }

        if (discovery is not null)
        {
            PackageVersionResolutionReceipt refreshed =
                PackageVersionSelectionResolver.Resolve(
                    request,
                    discovery,
                    PackageVersionDiscoveryFreshness.RefreshedForRequest);
            switch (refreshed)
            {
                case PackageVersionResolutionReceipt.Resolved resolved:
                    WriteEntry(key!, request, authorization, contract, resolved, now);
                    return new(
                        resolved,
                        PackageVersionServicePath.Discovered,
                        entryWritten: true,
                        entryEvicted: false);
                case PackageVersionResolutionReceipt.NotFound
                    or PackageVersionResolutionReceipt.NoMatch:
                    // Authoritative absence is an answer, not a failure: the
                    // prior no longer describes the sources, so it goes.
                    Remove(key!);
                    return new(
                        refreshed,
                        PackageVersionServicePath.Discovered,
                        entryWritten: false,
                        entryEvicted: true);
            }
        }

        return await ServePriorOrDiscoverAsync(
            request,
            authorization,
            contract,
            operation,
            key!,
            entry,
            PackageVersionServicePath.PriorAfterRefreshFailure,
            PackageVersionDiscoveryFreshness.ServedPrior,
            age,
            cancellationToken);
    }

    /// <summary>
    /// Removes the prior settlement for one request under one authorization.
    /// PackageHouse calls this when acquisition after a prior decision reports
    /// the coordinate absent from every authorized source.
    /// </summary>
    public bool Evict(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        string? key = StoreKey(request, authorization, contract);
        return key is not null && Remove(key);
    }

    /// <summary>
    /// The freshness window for one key: the base window adjusted by a
    /// deterministic jitter derived from the key, so entries written together
    /// do not expire together and the same key has the same window everywhere.
    /// </summary>
    public TimeSpan WindowFor(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        string? key = StoreKey(request, authorization, contract);
        return key is null ? _options.BaseWindow : WindowFor(key);
    }

    /// <summary>
    /// The store key, or <see langword="null"/> when any authorized source has
    /// no persistent identity (process-local authority), in which case no
    /// prior is read or written and every request discovers.
    /// </summary>
    internal static string? StoreKey(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        var text = new StringBuilder();
        text.Append(KeyNamespace).Append('\n');
        foreach (ConfiguredPackageAuthority authority in authorization.Authorities)
        {
            if (SourceIdentity(authority) is not { } sourceKey)
                return null;
            text.Append(sourceKey).Append('\n');
        }
        // NuGet package IDs are case-insensitive; the key must not split on
        // the spelling one invocation happened to use.
        text.Append("package:").Append(request.PackageId.ToLowerInvariant()).Append('\n');
        text.Append("kind:").Append(KindDescriptor(request)).Append('\n');
        text.Append("prerelease:").Append(contract.IncludePrerelease ? '1' : '0').Append('\n');
        text.Append("unlisted:").Append(contract.IncludeUnlisted ? '1' : '0');
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return $"{KeyNamespace}-{Convert.ToHexStringLower(digest.AsSpan(0, 16))}";
    }

    /// <summary>
    /// A stable identity for one authorized source: the local authority's
    /// persistent key, or the HTTP endpoint. An authority with neither is
    /// process-local and cannot anchor a prior across invocations.
    /// </summary>
    private static string? SourceIdentity(ConfiguredPackageAuthority authority)
    {
        if (authority.PersistentCacheKey is { } local)
            return local;
        if (authority.HttpEndpoint is { } endpoint)
            return "http:" + endpoint.AbsoluteUri.ToLowerInvariant();
        return null;
    }

    private static string KindDescriptor(PackageVersionSelectionRequest request) =>
        request switch
        {
            PackageVersionSelectionRequest.LatestStable => "latest-stable",
            PackageVersionSelectionRequest.LatestPrerelease => "latest-prerelease",
            // An always-check request never reads a prior; its successful
            // discovery rewrites the matching latest entry.
            PackageVersionSelectionRequest.AlwaysLatest always =>
                always.Discovery.IncludePrerelease ? "latest-prerelease" : "latest-stable",
            PackageVersionSelectionRequest.Wildcard wildcard =>
                "wildcard:" + wildcard.VersionPrefix,
            PackageVersionSelectionRequest.Range range =>
                "range:" + range.VersionRange.Start.ToNormalizedString()
                + ".." + range.VersionRange.End.ToNormalizedString()
                + ":" + RangeSelectionDescriptor(range.Selection),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static string RangeSelectionDescriptor(PackageVersionRangeSelection selection) =>
        selection switch
        {
            PackageVersionRangeSelection.First => "first",
            PackageVersionRangeSelection.Last => "last",
            PackageVersionRangeSelection.Ordinal ordinal =>
                "ordinal:" + ordinal.Value.ToString(CultureInfo.InvariantCulture),
            PackageVersionRangeSelection.Exact exact => "exact:" + exact.Version,
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };

    private TimeSpan WindowFor(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        uint sample = BitConverter.ToUInt32(digest, 0);
        double unit = sample / (double)uint.MaxValue;
        double fraction = (unit * 2 - 1) * _options.JitterFraction;
        return TimeSpan.FromTicks((long)(_options.BaseWindow.Ticks * (1 + fraction)));
    }

    private async Task<PackageVersionServiceSettlement> DiscoverAndRecordAsync(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        PackageVersionServiceOperation operation,
        string? key,
        CancellationToken cancellationToken,
        IReadOnlyList<PackageAuthorityFailure>? pinFailures = null)
    {
        PackageVersionDiscoveryResult discovery =
            await operation.Discover(
                new PackageVersionDiscoveryScope(Bound: null, cancellationToken))
                .ConfigureAwait(false);
        PackageVersionResolutionReceipt receipt =
            PackageVersionSelectionResolver.Resolve(
                request,
                discovery,
                PackageVersionDiscoveryFreshness.RefreshedForRequest);
        bool written = false;
        if (key is not null
            && receipt is PackageVersionResolutionReceipt.Resolved resolved)
        {
            WriteEntry(key, request, authorization, contract, resolved, _time.GetUtcNow());
            written = true;
        }
        return new(
            receipt,
            PackageVersionServicePath.Discovered,
            written,
            entryEvicted: false,
            pinFailures);
    }

    private async Task<PackageVersionServiceSettlement> ServePriorOrDiscoverAsync(
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        PackageVersionServiceOperation operation,
        string key,
        PriorEntry entry,
        PackageVersionServicePath path,
        PackageVersionDiscoveryFreshness freshness,
        TimeSpan? age,
        CancellationToken cancellationToken)
    {
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
            request.PackageId,
            entry.Version);
        PackageAcquisitionCandidateResult pinned = operation.Pin(coordinate);
        if (pinned.State == PackageAcquisitionCandidateResultState.Resolved
            && pinned.Candidate is { } candidate)
        {
            var prior = new PackageVersionResolutionReceipt.Prior(
                request,
                candidate,
                freshness,
                age);
            return new(prior, path, entryWritten: false, entryEvicted: false);
        }

        // The current generation would not pin the prior coordinate (the
        // authorization was denied or incomplete). The prior cannot be served,
        // so the request discovers as if no prior existed, under the ordinary
        // operation deadline, and the pin's own failures travel with the
        // settlement so the refusal stays visible. The entry stays for a
        // later invocation whose authorization admits it.
        return await DiscoverAndRecordAsync(
            request,
            authorization,
            contract,
            operation,
            key,
            cancellationToken,
            pinned.Failures).ConfigureAwait(false);
    }

    private static void WriteEntry(
        string key,
        PackageVersionSelectionRequest request,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        PackageVersionResolutionReceipt.Resolved resolved,
        DateTimeOffset writtenAt)
    {
        string version = resolved.Coordinate.Version;
        // The reporting source is recorded under the same identity the key
        // uses, so a local and an HTTP authority are both attributable.
        string sourceKey = resolved.Discovery.Candidates
            .Where(candidate => candidate.Observation.Coordinate.Version.Equals(
                version,
                StringComparison.OrdinalIgnoreCase))
            .Select(candidate => SourceIdentity(candidate.Authority))
            .FirstOrDefault(sourceKey => sourceKey is not null)
            ?? string.Empty;
        string content = string.Join(
            '\n',
            version,
            sourceKey,
            writtenAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        PersistentCache.Set(StoreCategory, key, content, extension: "txt");

        // An always-check success also refreshes the matching latest entry
        // so a later bare request benefits from the explicit refresh.
        if (request is PackageVersionSelectionRequest.AlwaysLatest always)
        {
            PackageVersionSelectionRequest latest = always.Discovery.IncludePrerelease
                ? new PackageVersionSelectionRequest.LatestPrerelease(request.PackageId)
                : new PackageVersionSelectionRequest.LatestStable(request.PackageId);
            if (StoreKey(latest, authorization, contract) is { } latestKey
                && !latestKey.Equals(key, StringComparison.Ordinal))
            {
                PersistentCache.Set(StoreCategory, latestKey, content, extension: "txt");
            }
        }
    }

    private static PriorEntry? ReadEntry(string key)
    {
        string? content = PersistentCache.TryGet(StoreCategory, key, extension: "txt");
        if (content is null)
            return null;
        string[] lines = content.Split('\n');
        if (lines.Length != 3
            || string.IsNullOrWhiteSpace(lines[0])
            || !long.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || !NuGet.Versioning.NuGetVersion.TryParse(lines[0], out _))
        {
            // A malformed entry is treated as absent, not as evidence.
            Remove(key);
            return null;
        }
        return new PriorEntry(
            lines[0],
            lines[1],
            new DateTimeOffset(ticks, TimeSpan.Zero));
    }

    private static bool Remove(string key) =>
        PersistentCache.Remove(StoreCategory, key, extension: "txt");

    private sealed record PriorEntry(string Version, string SourceKey, DateTimeOffset WrittenAt);
}
