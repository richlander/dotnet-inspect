using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace InspectWeb.Engine;

internal sealed record BrowserPackageCacheSnapshot(
    int Packages,
    int Resident,
    int Workspaces,
    long ResidentBytes);

internal sealed record BrowserPackageDocumentEntry(
    string Kind,
    string Name,
    string Path,
    int Size);

internal sealed record BrowserPackageDocumentPayload(
    string Kind,
    string Name,
    string Path,
    string Text);

internal sealed record BrowserPackageIconPayload(
    string MediaType,
    string Base64);

/// <summary>
/// Browser acquisition adapter: shared package owners resolve and admit payloads, while this host
/// owns the bounded session cache and registry of open workspaces.
/// </summary>
/// <remarks>
/// <para>
/// Product package realization mints typed <see cref="ResolvedAssemblyReference"/> participants
/// from Browser-acquired content. Inspection happens only inside a
/// <see cref="BrowserInspectionScope"/>, and only through a public product query that takes the
/// scope's <see cref="AssemblyContextGroup"/>. Browser/Wasm is single-threaded, so both caches are
/// deliberately lock-free.
/// </para>
/// <para>
/// A workspace is keyed by its <em>complete</em> exact coordinate set, so the package surface, a
/// type projection, an annotated member, Integrations, and a composite call-graph workspace over
/// several packages each reuse one open group instead of reacquiring every image. The registry is
/// bounded and disposes the least recently used scope on eviction, which is what returns its
/// retained image bytes. Because a scope is reused, nothing here releases a participant
/// terminally: <c>AssemblyContextIntegrationsQuery.ExecuteParticipantAsync</c>'s release ends that
/// participant's availability for the whole group, so it is only correct for a group that is
/// discarded immediately afterwards.
/// </para>
/// <para>
/// <c>BrowserEngineBoundaryTests.WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures</c>
/// gates the aggregate package-cache and scope-retention budget.
/// </para>
/// <para>
/// <c>BrowserEngineBoundaryTests.PackageResolution_StallBecomesVisibleOperationTimeout</c>
/// and
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_StallBecomesVisibleOperationTimeout</c>
/// gate the Browser operation deadline through shared coordinate resolution
/// and payload acquisition.
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller</c>
/// gates per-caller deadlines over a shared transfer, and
/// <c>BrowserEngineBoundaryTests.PendingAcquisitionAssociation_UsesCoordinateAndExactClientReference</c>
/// and
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_DistinctSameProducerClientsDoNotSharePendingTransfer</c>
/// gate Browser-owned pending-acquisition association without reparsing producer identity,
/// and
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent</c>
/// gates the final monotonic check before cache publication, and
/// <c>BrowserEngineBoundaryTests.PackageOperation_LateFailureBecomesVisibleTimeout</c>
/// gates timeout classification after synchronous work overruns the deadline.
/// <c>BrowserEngineBoundaryTests.PackageOperation_LateSuccessDisposesOwnedResult</c>
/// gates ownership when that final deadline check rejects a completed result.
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_ExactPinUsesGalleryCdnWithoutServiceIndex</c>
/// and
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_FloatingRootUsesGallerySearchAndCdn</c>
/// gate the service-index-free Gallery routes, while
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_RejectedReservationDisposesGalleryPayload</c>
/// gates response ownership when Browser capacity policy rejects a transfer.
/// <c>BrowserEngineBoundaryTests.BrowserGalleryDeadlineLeavesTimeForSourceTimeout</c>
/// and
/// <c>BrowserEngineBoundaryTests.VersionPickerPreservesGalleryRegistrationTimeout</c>
/// gate the timeout margin that lets the source-owned registration timeout remain
/// visible before the Browser operation ceiling.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageWorkspace
{
    const int MaxCachedPackages = 12;
    const long MaxCachedPackageBytes = 128L * 1024 * 1024;
    internal const int MaxOpenScopes = 4;
    internal static TimeSpan PackageOperationTimeout { get; } =
        TimeSpan.FromSeconds(30);
    internal static TimeSpan GalleryOperationTimeout { get; } =
        PackageOperationTimeout - TimeSpan.FromSeconds(5);

    static readonly BrowserMsdlProxyHandler MsdlProxyHandler =
        new(new HttpClientHandler());
    static readonly HttpClient Http = new(MsdlProxyHandler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    static readonly PackageSourceAssociation GalleryAssociation =
        PackageSourceAssociation.Create();
    static readonly PackageSourceIdentity GalleryConfiguredIdentity =
        PackageSourceIdentity.NuGetOrg;
    static readonly IReadOnlyDictionary<
        PackageSourceAssociation,
        PackageSourceIdentity> ConfiguredSourceIdentities =
        new Dictionary<PackageSourceAssociation, PackageSourceIdentity>(
            ReferenceEqualityComparer.Instance)
        {
            [GalleryAssociation] = GalleryConfiguredIdentity,
        };
    static readonly UniformPackageSourceAuthorization SourceAuthorization =
        new([PackageSource.NuGetOrg]);
    internal static readonly IPackageSourceClient Gallery =
        PackageSourceClientFactory.CreateGallery(
            GalleryAssociation,
            new NuGetFetchOptions
            {
                RequestTimeout = GalleryOperationTimeout,
                OperationTimeout = GalleryOperationTimeout,
            });
    static readonly BrowserSessionPackageStore Store = new();
    static readonly PackagePayloadLimits PayloadLimits = new()
    {
        MaxArchiveBytes = MaxCachedPackageBytes,
        MaxExpandedBytes = 512L * 1024 * 1024,
        MaxEntryCount = 4_096,
        MaxUniqueDirectories = 16_384,
    };
    static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    static readonly Dictionary<string, ScopeEntry> Scopes = new(StringComparer.Ordinal);
    static readonly Dictionary<string, PackageDownloadReservation> Reservations =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, int> Leases = new(StringComparer.Ordinal);
    static readonly Dictionary<PendingAcquisitionKey, Task<AcquiredPackageSourcePayload>>
        PendingAcquisitions = [];
    static readonly Dictionary<string, Task<BrowserInspectionScope>> PendingScopeOpens =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, Task> PendingPackageEvictions =
        new(StringComparer.Ordinal);
    static readonly HashSet<string> Downloaded = new(StringComparer.Ordinal);
    static long _clock;

    internal static HttpClient NetworkClient => Http;
    internal static void ConfigureMsdlProxy(string origin) =>
        MsdlProxyHandler.Configure(origin);
    internal static IPackageSourceAuthorization PackageSourceAuthorization =>
        SourceAuthorization;
    internal static IPackageStore SessionPackageStore => Store;
    internal static IPackagePayloadTransferPolicy PackageTransferPolicy =>
        Store;
    internal static PackagePayloadLimits PackageLimits => PayloadLimits;

    sealed record CacheEntry
    {
        public CacheEntry(
            byte[] bytes,
            InMemoryPackageContent content,
            long lastAccess)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(content);
            if (!content.ReferencesArchive(bytes))
            {
                throw new ArgumentException(
                    "The Browser cache content does not retain the supplied package archive.",
                    nameof(content));
            }

            Bytes = bytes;
            Content = content;
            LastAccess = lastAccess;
        }

        public byte[] Bytes { get; private init; }

        public InMemoryPackageContent Content { get; private init; }

        public long LastAccess { get; init; }

        public string ProducerKey => Content.ProducerKey;
    }

    sealed record ScopeEntry(
        IAsyncDisposable Scope,
        ImmutableHashSet<string> PackageKeys,
        long LastAccess,
        int ActiveLeases,
        bool RemovalRequested,
        Action<IAsyncDisposable>? OnDisposed);

    public static BrowserPackageCacheSnapshot Stats() =>
        new(
            Downloaded.Count,
            Cache.Count,
            Scopes.Count,
            Cache.Values.Sum(entry => entry.Bytes.LongLength)
                + Reservations.Values.Sum(reservation => reservation.ReservedBytes));

    /// <summary>
    /// Resolves and acquires one package through the shared product owners
    /// within the Browser operation deadline.
    /// </summary>
    public static Task<BrowserPackage> AcquireAsync(
        string packageId,
        string? version,
        CancellationToken cancellationToken = default) =>
        RunPackageOperationAsync(
            deadline => AcquireCoreAsync(
                packageId,
                version,
                Gallery,
                ConfiguredSourceIdentityFor(Gallery),
                deadline,
                cancellationToken),
            PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPackage> AcquireAsync(
        string packageId,
        string? version,
        IPackageSourceClient source,
        PackageSourceIdentity configuredSourceIdentity,
        TimeSpan operationTimeout) =>
        RunPackageOperationAsync(
            deadline => AcquireCoreAsync(
                packageId,
                version,
                source,
                configuredSourceIdentity,
                deadline,
                CancellationToken.None),
            operationTimeout);

    static async Task<BrowserPackage> AcquireCoreAsync(
        string packageId,
        string? version,
        IPackageSourceClient source,
        PackageSourceIdentity configuredSourceIdentity,
        BrowserPackageOperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        cancellationToken.ThrowIfCancellationRequested();

        string? requestedVersion =
            string.IsNullOrWhiteSpace(version)
            || version.Equals("latest", StringComparison.OrdinalIgnoreCase)
                ? null
                : version;
        using var resolutionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                deadline.Token,
                cancellationToken);
        PackageSourceCoordinate coordinate = await ResolveCoordinateAsync(
            new PackageCoordinate(packageId, requestedVersion),
            source,
            resolutionCancellation.Token).ConfigureAwait(false);

        string key = PackageKey(coordinate.PackageId, coordinate.Version);
        var pendingKey = new PendingAcquisitionKey(key, source);
        if (!PendingAcquisitions.TryGetValue(
                pendingKey,
                out Task<AcquiredPackageSourcePayload>? pending))
        {
            pending = AcquirePayloadWithinOperationAsync(
                coordinate,
                source,
                configuredSourceIdentity,
                deadline.Remaining);
            PendingAcquisitions.Add(pendingKey, pending);
            ObserveAndRemovePendingAcquisition(pendingKey, pending);
        }

        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                deadline.Token,
                cancellationToken);
        AcquiredPackageSourcePayload payload = await WaitForSharedAcquisitionAsync(
            pending,
            waitCancellation.Token).ConfigureAwait(false);

        if (!Cache.TryGetValue(key, out CacheEntry? cached)
            || !cached.ProducerKey.Equals(
                payload.ProducerKey,
                StringComparison.Ordinal)
            || !ReferenceEquals(
                cached.Content.GenerationIdentity,
                payload.Content.GenerationIdentity))
        {
            throw new InvalidOperationException(
                "The shared package acquisition completed without publishing its Browser cache entry.");
        }

        Cache[key] = cached with { LastAccess = ++_clock };
        return new BrowserPackage(
            packageId,
            payload,
            cached.Bytes);
    }

    static async Task<PackageSourceCoordinate> ResolveCoordinateAsync(
        PackageCoordinate request,
        IPackageSourceClient source,
        CancellationToken cancellationToken)
    {
        PackageSourceCoordinateResolution resolution =
            await PackageSourceCoordinateResolver.ResolveAsync(
                source,
                request,
                cancellationToken).ConfigureAwait(false);
        return resolution switch
        {
            PackageSourceCoordinateResolution.Resolved resolved =>
                resolved.Coordinate,
            PackageSourceCoordinateResolution.Invalid rejected =>
                throw new InvalidOperationException(rejected.Message),
            PackageSourceCoordinateResolution.Unavailable unavailable =>
                throw new InvalidOperationException(unavailable.Message),
            PackageSourceCoordinateResolution.Failed failed =>
                throw new InvalidOperationException(failed.Failure.Message),
            _ => throw new InvalidOperationException(
                "Package coordinate resolution returned an unknown outcome."),
        };
    }

    static PackageSourceIdentity ConfiguredSourceIdentityFor(
        IPackageSourceClient source)
    {
        if (ConfiguredSourceIdentities.TryGetValue(
                source.Source.Association,
                out PackageSourceIdentity? identity))
        {
            return identity;
        }

        throw new InvalidOperationException(
            "The package source association is not registered with a configured Browser source identity.");
    }

    /// <summary>
    /// Resolves one exact package/version/framework identity into an acquirable package Root.
    /// The result preserves the product's compile-library outcome and carries assembly
    /// participants only when compile assets were selected.
    /// </summary>
    public static async Task<BrowserPackageCoordinate> ResolveAsync(
        string packageId,
        string? version,
        string? targetFramework,
        CancellationToken cancellationToken = default)
    {
        BrowserPackage package = await AcquireAsync(
            packageId,
            version,
            cancellationToken);
        return new BrowserPackageCoordinate(
            package,
            package.CreateRootBinding(targetFramework));
    }

    internal static async Task<BrowserPackageCoordinate> ResolveAsync(
        string packageId,
        string? version,
        string? targetFramework,
        IPackageSourceClient source,
        PackageSourceIdentity configuredSourceIdentity,
        TimeSpan operationTimeout)
    {
        BrowserPackage package = await AcquireAsync(
            packageId,
            version,
            source,
            configuredSourceIdentity,
            operationTimeout);
        return new BrowserPackageCoordinate(
            package,
            package.CreateRootBinding(targetFramework));
    }

    /// <summary>
    /// Opens — or reuses — the one workspace for an exact set of package coordinates. Several
    /// coordinates produce binding-consistent compile and implementation groups. A workspace-wide
    /// interaction such as the member call graph uses the implementation group: callers in a
    /// sibling package are only visible when that package is a participant of that same group.
    /// </summary>
    /// <remarks>
    /// The returned scope is owned by this registry, not by the caller: it is reused by every
    /// later query over the same coordinate set and disposed when the registry evicts it. Callers
    /// must not dispose it, and must not run a query that releases a participant terminally.
    /// </remarks>
    public static async Task<BrowserInspectionScope> OpenScopeAsync(
        IReadOnlyList<BrowserPackageCoordinate> coordinates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
            throw new ArgumentException("A workspace requires at least one package coordinate.");

        cancellationToken.ThrowIfCancellationRequested();
        string key = PackageScopeKey(coordinates);
        if (TryReuseScope(key, coordinates) is { } reused)
            return reused;

        if (!PendingScopeOpens.TryGetValue(
                key,
                out Task<BrowserInspectionScope>? pending))
        {
            pending = OpenScopeCoreAsync(key, [.. coordinates]);
            PendingScopeOpens.Add(key, pending);
            ObserveAndRemovePendingScopeOpen(key, pending);
        }

        BrowserInspectionScope opened =
            await WaitForSharedAcquisitionAsync(pending, cancellationToken)
                .ConfigureAwait(false);
        if (!opened.ContainsExactCoordinates(coordinates))
        {
            throw new InvalidOperationException(
                "The retained browser workspace does not match the exact requested "
                + "package content.");
        }

        TouchScope(opened);
        return opened;
    }

    /// <summary>
    /// Builds one workspace for an exact coordinate set. The coordinates' archives stay leased
    /// for the whole construction, so a concurrent acquisition cannot evict the content this
    /// scope is being realized from, and registration re-validates that content before the
    /// completed scope is published.
    /// </summary>
    static async Task<BrowserInspectionScope> OpenScopeCoreAsync(
        string key,
        ImmutableArray<BrowserPackageCoordinate> coordinates)
    {
        using var construction = new PackageLeaseSet();
        ImmutableHashSet<string> requested =
            RetainCoordinatePackages(coordinates);
        foreach (string packageKey in requested)
            construction.Lease(packageKey);

        BrowserInspectionScope scope =
            await BrowserInspectionScope.CreateAsync(coordinates)
                .ConfigureAwait(false);
        try
        {
            ImmutableHashSet<string> packageKeys =
                RetainCoordinatePackages(coordinates);
            return await RegisterScopeAsync(key, scope, packageKeys)
                .ConfigureAwait(false);
        }
        catch (Exception registrationFailure)
        {
            try
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    registrationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    static BrowserInspectionScope? TryReuseScope(
        string key,
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        if (!Scopes.TryGetValue(key, out ScopeEntry? entry))
            return null;
        if (entry.Scope is not BrowserInspectionScope retained)
        {
            throw new InvalidOperationException(
                "The browser scope registry key names a different scope kind.");
        }
        if (!retained.ContainsExactCoordinates(coordinates))
        {
            throw new InvalidOperationException(
                "The retained browser workspace does not match the exact requested "
                + "package content.");
        }

        Scopes[key] = entry with { LastAccess = ++_clock };
        TouchPackages(entry.PackageKeys);
        return retained;
    }

    static void ObserveAndRemovePendingScopeOpen(
        string key,
        Task<BrowserInspectionScope> open)
    {
        _ = open.ContinueWith(
            completed =>
            {
                if (PendingScopeOpens.TryGetValue(
                        key,
                        out Task<BrowserInspectionScope>? current)
                    && ReferenceEquals(current, completed))
                {
                    PendingScopeOpens.Remove(key);
                }

                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Admits one built scope into the bounded registry, awaiting the disposal of any scope it
    /// evicts so the evicted workspace's retained bytes are released before this one is counted.
    /// </summary>
    internal static async ValueTask<T> RegisterScopeAsync<T>(
        string key,
        T scope,
        ImmutableHashSet<string> packageKeys,
        Action<T>? onDisposed = null)
        where T : class, IAsyncDisposable
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(packageKeys);
        try
        {
            RetainPackageKeys(packageKeys);
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (Scopes.TryGetValue(key, out ScopeEntry? retained))
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            if (retained.Scope is not T typed)
            {
                throw new InvalidOperationException(
                    "The browser scope registry key names a different scope kind.");
            }

            Scopes[key] = retained with { LastAccess = ++_clock };
            if (retained.RemovalRequested)
            {
                Scopes[key] = Scopes[key] with
                {
                    RemovalRequested = false,
                };
            }
            TouchPackages(retained.PackageKeys);
            return typed;
        }

        while (Scopes.Count >= MaxOpenScopes)
        {
            string? oldest = Scopes
                .Where(candidate => candidate.Value.ActiveLeases == 0)
                .OrderBy(candidate => candidate.Value.LastAccess)
                .Select(candidate => candidate.Key)
                .FirstOrDefault();
            if (oldest is null)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The browser workspace limit cannot evict an active inspection.");
            }
            await DisposeRegisteredScopeAsync(oldest, Scopes[oldest])
                .ConfigureAwait(false);
        }

        if (Scopes.ContainsKey(key))
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "The browser scope registry admitted another workspace for this coordinate set "
                + "while capacity was being released.");
        }

        Scopes[key] = new ScopeEntry(
            scope,
            packageKeys,
            ++_clock,
            ActiveLeases: 0,
            RemovalRequested: false,
            onDisposed is null
                ? null
                : disposed => onDisposed((T)disposed));
        return scope;
    }

    internal static bool IsScopeRetained(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Scopes.Values.Any(entry => ReferenceEquals(entry.Scope, scope));
    }

    internal static void TouchScope(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        KeyValuePair<string, ScopeEntry> registered = Scopes
            .SingleOrDefault(candidate => ReferenceEquals(candidate.Value.Scope, scope));
        if (registered.Value is null)
        {
            throw new InvalidOperationException(
                "The browser inspection scope is no longer retained.");
        }

        Scopes[registered.Key] = registered.Value with
        {
            LastAccess = ++_clock,
        };
        TouchPackages(registered.Value.PackageKeys);
    }

    internal static ValueTask RemoveScopeAsync(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        KeyValuePair<string, ScopeEntry> registered = Scopes
            .SingleOrDefault(candidate => ReferenceEquals(candidate.Value.Scope, scope));
        if (registered.Value is null)
            return ValueTask.CompletedTask;
        if (registered.Value.ActiveLeases != 0)
        {
            Scopes[registered.Key] = registered.Value with
            {
                RemovalRequested = true,
            };
            return ValueTask.CompletedTask;
        }

        return DisposeRegisteredScopeAsync(registered.Key, registered.Value);
    }

    /// <summary>
    /// Pins a registry-owned scope and its package archives for one asynchronous inspection.
    /// </summary>
    internal static BrowserScopeLease<TScope> LeaseScope<TScope>(
        TScope scope)
        where TScope : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(scope);
        KeyValuePair<string, ScopeEntry> registered = Scopes
            .SingleOrDefault(candidate => ReferenceEquals(candidate.Value.Scope, scope));
        if (registered.Value is null)
        {
            throw new InvalidOperationException(
                "The browser inspection scope is no longer retained.");
        }

        foreach (string packageKey in registered.Value.PackageKeys)
            LeasePackage(packageKey);
        Scopes[registered.Key] = registered.Value with
        {
            LastAccess = ++_clock,
            ActiveLeases = registered.Value.ActiveLeases + 1,
        };
        return new BrowserScopeLease<TScope>(
            scope,
            () => ReleaseScopeLeaseAsync(registered.Key, scope));
    }

    static async ValueTask ReleaseScopeLeaseAsync(
        string scopeKey,
        IAsyncDisposable scope)
    {
        if (!Scopes.TryGetValue(scopeKey, out ScopeEntry? entry)
            || !ReferenceEquals(entry.Scope, scope)
            || entry.ActiveLeases <= 0)
        {
            throw new InvalidOperationException(
                "The browser inspection scope lease is not active.");
        }

        int activeLeases = entry.ActiveLeases - 1;
        if (activeLeases == 0 && entry.RemovalRequested)
        {
            Scopes[scopeKey] = entry with { ActiveLeases = activeLeases };
            await DisposeRegisteredScopeAsync(scopeKey, entry)
                .ConfigureAwait(false);
        }
        else
        {
            Scopes[scopeKey] = entry with
            {
                ActiveLeases = activeLeases,
            };
        }
        foreach (string packageKey in entry.PackageKeys)
            ReleasePackageLease(packageKey);
    }

    /// <summary>Opens — or reuses — the workspace for one exact package coordinate.</summary>
    public static async Task<BrowserInspectionScope> OpenScopeAsync(
        string packageId,
        string? version,
        string? targetFramework,
        CancellationToken cancellationToken = default)
        => (await ResolveAndOpenScopeAsync(
            [new BrowserPackageRequest(packageId, version, targetFramework)],
            cancellationToken)).Scope;

    /// <summary>
    /// Resolves and temporarily leases every requested coordinate until the aggregate scope owns
    /// them. A later package acquisition cannot evict an earlier coordinate while a composite
    /// workspace is still being assembled.
    /// </summary>
    public static async Task<BrowserScopeResolution> ResolveAndOpenScopeAsync(
        IReadOnlyList<BrowserPackageRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("A workspace requires at least one package request.");

        var coordinates = new List<BrowserPackageCoordinate>();
        var coordinateKeys = new HashSet<string>(StringComparer.Ordinal);
        var leasedPackages = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (BrowserPackageRequest request in requests)
            {
                BrowserPackageCoordinate coordinate = await ResolveAsync(
                    request.PackageId,
                    request.Version,
                    request.TargetFramework,
                    cancellationToken);
                string packageKey = PackageKey(coordinate);
                if (leasedPackages.Add(packageKey))
                    LeasePackage(packageKey);
                if (coordinateKeys.Add(coordinate.Key))
                    coordinates.Add(coordinate);
            }

            BrowserInspectionScope scope =
                await OpenScopeAsync(coordinates, cancellationToken);
            return new BrowserScopeResolution(scope, [.. coordinates]);
        }
        finally
        {
            foreach (string packageKey in leasedPackages)
                ReleasePackageLease(packageKey);
        }
    }

    static async Task<AcquiredPackageSourcePayload> AcquirePayloadAsync(
        PackageSourceCoordinate coordinate,
        IPackageSourceClient source,
        PackageSourceIdentity configuredSourceIdentity,
        CancellationToken cancellationToken,
        IPackagePayloadTransferPolicy transferPolicy)
    {
        PackageSourcePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
            source,
            configuredSourceIdentity,
            coordinate,
            Store,
            limits: PayloadLimits,
            cancellationToken: cancellationToken,
            transferPolicy: transferPolicy).ConfigureAwait(false);
        return result switch
        {
            PackageSourcePayloadResult.Acquired acquired => acquired.Payload,
            PackageSourcePayloadResult.Unavailable unavailable =>
                throw new InvalidOperationException(unavailable.Message),
            PackageSourcePayloadResult.Failed failed =>
                throw new InvalidOperationException(failed.Failure.Message),
            _ => throw new InvalidOperationException(
                "Package payload acquisition returned an unknown outcome."),
        };
    }

    internal static ValueTask<PackageQueryContentResult>
        AcquirePackageQueryContentAsync(
            PackageProfileMatch package,
            IPackageSourceClient source,
            BrowserPackageOperationDeadline deadline) =>
        AcquirePackageQueryContentAsync(
            package,
            source,
            ConfiguredSourceIdentityFor(source),
            deadline);

    internal static async ValueTask<PackageQueryContentResult>
        AcquirePackageQueryContentAsync(
            PackageProfileMatch package,
            IPackageSourceClient source,
            PackageSourceIdentity configuredSourceIdentity,
            BrowserPackageOperationDeadline deadline)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configuredSourceIdentity);
        ArgumentNullException.ThrowIfNull(deadline);
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
            package.PackageId,
            package.Version);

        PackageSourcePayloadResult result;
        try
        {
            result = await PackagePayloadAcquisition.AcquireAsync(
                    source,
                    configuredSourceIdentity,
                    coordinate,
                    Store,
                    limits: PayloadLimits,
                    cancellationToken: deadline.Token,
                    transferPolicy: new BrowserPackageQueryTransferPolicy(
                        new BrowserPackageOperationTransferPolicy(
                            Store,
                            deadline)))
                .ConfigureAwait(false);
        }
        catch (BrowserPackagePayloadPolicyException exception)
        {
            return new PackageQueryContentResult.Unavailable(
                exception.Message);
        }
        return result switch
        {
            PackageSourcePayloadResult.Acquired acquired =>
                new PackageQueryContentResult.Available(
                    acquired.Payload.Content),
            PackageSourcePayloadResult.Unavailable unavailable =>
                new PackageQueryContentResult.Unavailable(
                    unavailable.Message),
            PackageSourcePayloadResult.Failed failed =>
                new PackageQueryContentResult.Unavailable(
                    failed.Failure.Message),
            _ => throw new InvalidOperationException(
                "Package payload acquisition returned an unknown outcome."),
        };
    }

    internal static Task<T> WaitForSharedAcquisitionAsync<T>(
        Task<T> acquisition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        return acquisition.WaitAsync(cancellationToken);
    }

    static void ObserveAndRemovePendingAcquisition(
        PendingAcquisitionKey key,
        Task<AcquiredPackageSourcePayload> acquisition)
    {
        _ = acquisition.ContinueWith(
            completed =>
            {
                if (PendingAcquisitions.TryGetValue(
                        key,
                        out Task<AcquiredPackageSourcePayload>? current)
                    && ReferenceEquals(current, completed))
                {
                    PendingAcquisitions.Remove(key);
                }

                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal sealed class PendingAcquisitionKey
        : IEquatable<PendingAcquisitionKey>
    {
        readonly string _coordinateKey;
        readonly IPackageSourceClient _source;

        internal PendingAcquisitionKey(
            string coordinateKey,
            IPackageSourceClient source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(coordinateKey);
            ArgumentNullException.ThrowIfNull(source);
            _coordinateKey = coordinateKey;
            _source = source;
        }

        public bool Equals(PendingAcquisitionKey? other) =>
            other is not null
            && _coordinateKey.Equals(
                other._coordinateKey,
                StringComparison.Ordinal)
            && ReferenceEquals(_source, other._source);

        public override bool Equals(object? obj) =>
            obj is PendingAcquisitionKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(_coordinateKey),
                RuntimeHelpers.GetHashCode(_source));
    }

    static Task<AcquiredPackageSourcePayload> AcquirePayloadWithinOperationAsync(
        PackageSourceCoordinate coordinate,
        IPackageSourceClient source,
        PackageSourceIdentity configuredSourceIdentity,
        TimeSpan timeout) =>
        RunPackageOperationAsync(
            deadline => AcquirePayloadAsync(
                coordinate,
                source,
                configuredSourceIdentity,
                deadline.Token,
                new BrowserPackageOperationTransferPolicy(
                    Store,
                    deadline)),
            timeout);

    internal static Task<string[]> GetVersionsAsync(string packageId) =>
        GetVersionsAsync(
            packageId,
            Gallery,
            PackageOperationTimeout);

    internal static Task<string[]> GetVersionsAsync(
        string packageId,
        IPackageSourceClient source,
        TimeSpan timeout) =>
        RunPackageOperationAsync(
            deadline => GetVersionsCoreAsync(
                packageId,
                source,
                deadline.Token),
            timeout);

    static async Task<string[]> GetVersionsCoreAsync(
        string packageId,
        IPackageSourceClient source,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (PackageCoordinateResolver.Validate(new PackageCoordinate(packageId))
            is { } invalid)
        {
            throw new InvalidOperationException(invalid.Message);
        }

        PackageVersionResult result = await GetVersionResultAsync(
            source,
            packageId,
            cancellationToken).ConfigureAwait(false);
        return
        [
            .. result.Candidates.Select(candidate =>
                candidate.Coordinate.Version),
        ];
    }

    static async Task<PackageVersionResult> GetVersionResultAsync(
        IPackageSourceClient source,
        string packageId,
        CancellationToken cancellationToken)
    {
        PackageSourceOperationResult<PackageVersionResult> operation =
            await source.GetVersionsAsync(
                packageId,
                cancellationToken).ConfigureAwait(false);
        if (operation.Failure is { } failure)
            throw new InvalidOperationException(failure.Message);
        return operation.Value
            ?? throw new InvalidOperationException(
                "Package version listing returned no value or failure.");
    }

    internal static Task<string> ResolveDependencyVersionAsync(
        string packageId,
        string? declaredRange) =>
        ResolveDependencyVersionAsync(
            packageId,
            declaredRange,
            Gallery,
            PackageOperationTimeout);

    internal static Task<string> ResolveDependencyVersionAsync(
        string packageId,
        string? declaredRange,
        IPackageSourceClient source,
        TimeSpan timeout) =>
        RunPackageOperationAsync(
            deadline => ResolveDependencyVersionCoreAsync(
                packageId,
                declaredRange,
                source,
                deadline.Token),
            timeout);

    static async Task<string> ResolveDependencyVersionCoreAsync(
        string packageId,
        string? declaredRange,
        IPackageSourceClient source,
        CancellationToken cancellationToken)
    {
        if (PackageDependencyVersionRange.GetExactVersion(declaredRange)
            is { } exactVersion)
        {
            PackageSourceCoordinate coordinate =
                await ResolveCoordinateAsync(
                    new PackageCoordinate(packageId, exactVersion),
                    source,
                    cancellationToken).ConfigureAwait(false);
            return coordinate.Version;
        }

        PackageVersionResult result = await GetVersionResultAsync(
            source,
            packageId,
            cancellationToken).ConfigureAwait(false);
        if (!result.HasAuthoritativeListingState)
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' cannot safely resolve the declared range "
                + $"'{declaredRange}' because authoritative Gallery listing state is unavailable.");
        }

        return SelectDependencyVersion(
                [
                    .. result.Candidates
                        .Where(candidate =>
                            candidate.ListingState == PackageListingState.Listed)
                        .Select(candidate => candidate.Coordinate.Version),
                ],
                declaredRange)
            ?? throw new InvalidOperationException(
                $"Package '{packageId}' has no published version satisfying "
                + $"the declared range '{declaredRange}'.");
    }

    internal static async Task<T> RunPackageOperationAsync<T>(
        Func<BrowserPackageOperationDeadline, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken callerCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The Browser package-operation timeout must be positive.");
        }

        using var deadline =
            new BrowserPackageOperationDeadline(
                timeout,
                callerCancellation);
        try
        {
            T result = await operation(deadline).ConfigureAwait(false);
            try
            {
                deadline.ThrowIfExpired();
            }
            catch (Exception exception)
                when (exception is OperationCanceledException
                    or TimeoutException)
            {
                await DisposeLateResultAsync(result).ConfigureAwait(false);
                throw;
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            deadline.ThrowIfExpired();
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception exception)
            when (deadline.HasExpired)
        {
            throw deadline.Timeout(exception);
        }
    }

    /// <summary>
    /// Releases a result the deadline rejected after the operation already produced it, awaiting
    /// asynchronous cleanup instead of dropping it.
    /// </summary>
    static async ValueTask DisposeLateResultAsync<T>(T result)
    {
        switch (result)
        {
            case IAsyncDisposable asyncOwned:
                await asyncOwned.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable owned:
                owned.Dispose();
                break;
        }
    }

    internal sealed class BrowserPackageOperationDeadline : IDisposable
    {
        readonly CancellationToken _callerCancellation;
        readonly CancellationTokenSource _deadlineCancellation;
        readonly CancellationTokenSource _operationCancellation;
        readonly long _started = Stopwatch.GetTimestamp();
        readonly TimeSpan _timeout;

        internal BrowserPackageOperationDeadline(
            TimeSpan timeout,
            CancellationToken callerCancellation = default)
        {
            _timeout = timeout;
            _callerCancellation = callerCancellation;
            _deadlineCancellation = new CancellationTokenSource(timeout);
            _operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    callerCancellation,
                    _deadlineCancellation.Token);
        }

        internal CancellationToken Token => _operationCancellation.Token;

        internal bool HasExpired =>
            _deadlineCancellation.IsCancellationRequested
            || Stopwatch.GetElapsedTime(_started) >= _timeout;

        internal TimeSpan Remaining
        {
            get
            {
                TimeSpan remaining =
                    _timeout - Stopwatch.GetElapsedTime(_started);
                if (remaining <= TimeSpan.Zero)
                    ThrowIfExpired();
                return remaining;
            }
        }

        internal void ThrowIfExpired()
        {
            if (_callerCancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Browser package operation was canceled by the caller.",
                    _callerCancellation);
            }

            if (HasExpired)
            {
                throw Timeout(
                    new OperationCanceledException(
                        "Browser package operation deadline expired.",
                        Token));
            }
        }

        internal TimeoutException Timeout(Exception exception) =>
            new(
                $"The Browser package operation exceeded its {_timeout.TotalSeconds:g}-second deadline.",
                exception);

        public void Dispose()
        {
            _operationCancellation.Dispose();
            _deadlineCancellation.Dispose();
        }
    }

    internal sealed class BrowserPackageOperationTransferPolicy(
        IPackagePayloadTransferPolicy inner,
        BrowserPackageOperationDeadline deadline)
        : IPackagePayloadTransferPolicy
    {
        public async ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default) =>
            ApplyDeadline(
                await inner.ReserveAsync(transfer, cancellationToken)
                    .ConfigureAwait(false));

        internal IPackagePayloadReservation ApplyDeadline(
            IPackagePayloadReservation reservation) =>
            new DeadlineReservation(
                reservation,
                deadline);

        sealed class DeadlineReservation(
            IPackagePayloadReservation inner,
            BrowserPackageOperationDeadline deadline)
            : IPackagePayloadReservation
        {
            public void Complete()
            {
                deadline.ThrowIfExpired();
                inner.Complete();
            }

            public void Dispose() => inner.Dispose();
        }
    }

    internal sealed class BrowserPackageQueryTransferPolicy(
        IPackagePayloadTransferPolicy inner)
        : IPackagePayloadTransferPolicy
    {
        public async ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.ReserveAsync(transfer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new BrowserPackagePayloadPolicyException(
                    exception.Message,
                    exception);
            }
        }
    }

    internal sealed class BrowserPackagePayloadPolicyException(
        string message,
        Exception innerException)
        : InvalidOperationException(message, innerException);

    internal static string? SelectDependencyVersion(
        string[] versions,
        string? declaredRange) =>
        PackageDependencyVersionRange.SelectBestSatisfying(
            versions,
            string.IsNullOrWhiteSpace(declaredRange)
                ? "*"
                : declaredRange);
    static ImmutableHashSet<string> RetainCoordinatePackages(
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        ImmutableHashSet<string> packageKeys = coordinates
            .Select(PackageKey)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (packageKeys.Count > MaxCachedPackages)
        {
            throw new InvalidOperationException(
                "The requested workspace's package count exceeds the browser package-cache limit.");
        }

        foreach (BrowserPackageCoordinate coordinate in coordinates)
        {
            string packageKey = PackageKey(coordinate);
            if (!Cache.TryGetValue(packageKey, out CacheEntry? entry)
                || !ReferenceEquals(
                    entry.Bytes,
                    coordinate.Package.RetainedBytes)
                || !entry.ProducerKey.Equals(
                    coordinate.Root.ProducerKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A resolved browser package escaped aggregate cache accounting before its "
                    + "workspace opened.");
            }
        }

        RetainPackageKeys(packageKeys);
        return packageKeys;
    }

    static void RetainPackageKeys(ImmutableHashSet<string> packageKeys)
    {
        foreach (string packageKey in packageKeys)
        {
            if (!Cache.ContainsKey(packageKey))
            {
                throw new InvalidOperationException(
                    "A resolved browser package escaped aggregate cache accounting before its "
                    + "workspace opened.");
            }
        }

        TouchPackages(packageKeys);
    }

    /// <summary>
    /// Frees bounded cache capacity by evicting least-recently-used unleased packages, awaiting
    /// each eviction so the retained bytes are actually released before the caller's reservation
    /// is admitted.
    /// </summary>
    static async ValueTask MakeCacheRoomAsync(
        long additionalBytes,
        int additionalEntries)
    {
        while (Cache.Count + Reservations.Count + additionalEntries > MaxCachedPackages
            || Cache.Values.Sum(entry => entry.Bytes.LongLength)
                + Reservations.Values.Sum(reservation => reservation.ReservedBytes)
                + additionalBytes
                > MaxCachedPackageBytes)
        {
            string? oldest = Cache
                .Where(entry => !Leases.ContainsKey(entry.Key))
                .OrderBy(entry => entry.Value.LastAccess)
                .Select(entry => entry.Key)
                .FirstOrDefault();
            if (oldest is null)
            {
                throw new InvalidOperationException(
                    "The browser package-cache limit cannot accommodate the requested workspace.");
            }

            await EvictPackageAsync(oldest).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes every scope that retains one package and then drops its archive. A concurrent
    /// evictor of the same package joins the in-flight eviction instead of racing it, and a
    /// dependent scope that acquired an active lease during the awaited disposal keeps its
    /// archive: that package is leased again, so the next candidate is chosen instead.
    /// </summary>
    static Task EvictPackageAsync(string packageKey)
    {
        if (PendingPackageEvictions.TryGetValue(packageKey, out Task? pending))
            return pending;

        Task eviction = EvictPackageCoreAsync(packageKey);
        if (!eviction.IsCompleted)
        {
            PendingPackageEvictions[packageKey] = eviction;
            _ = eviction.ContinueWith(
                completed =>
                {
                    if (PendingPackageEvictions.TryGetValue(
                            packageKey,
                            out Task? current)
                        && ReferenceEquals(current, completed))
                    {
                        PendingPackageEvictions.Remove(packageKey);
                    }

                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return eviction;
    }

    static async Task EvictPackageCoreAsync(string packageKey)
    {
        while (true)
        {
            KeyValuePair<string, ScopeEntry> dependent = Scopes
                .FirstOrDefault(entry =>
                    entry.Value.PackageKeys.Contains(packageKey));
            if (dependent.Value is null)
                break;
            if (dependent.Value.ActiveLeases != 0)
                return;

            await DisposeRegisteredScopeAsync(dependent.Key, dependent.Value)
                .ConfigureAwait(false);
        }

        Cache.Remove(packageKey);
    }

    static async ValueTask DisposeRegisteredScopeAsync(
        string scopeKey,
        ScopeEntry entry)
    {
        Scopes.Remove(scopeKey);
        try
        {
            entry.OnDisposed?.Invoke(entry.Scope);
        }
        finally
        {
            await entry.Scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static async ValueTask<PackageDownloadReservation>
        ReservePackageDownloadAsync(
            string packageKey,
            long declaredLength)
    {
        if (declaredLength < 0 || declaredLength > MaxCachedPackageBytes)
        {
            throw new InvalidOperationException(
                "The package exceeds the browser package-cache byte limit.");
        }
        if (Reservations.ContainsKey(packageKey))
            throw new InvalidOperationException("The package download is already reserved.");

        await MakeCacheRoomAsync(declaredLength, additionalEntries: 1)
            .ConfigureAwait(false);
        if (Reservations.ContainsKey(packageKey))
            throw new InvalidOperationException("The package download is already reserved.");

        var reservation = new PackageDownloadReservation(
            packageKey,
            declaredLength);
        Reservations.Add(packageKey, reservation);
        return reservation;
    }

    static void LeasePackage(string packageKey)
    {
        if (!Cache.ContainsKey(packageKey))
            throw new InvalidOperationException("A package must be cached before it can be leased.");
        Leases[packageKey] = Leases.TryGetValue(packageKey, out int count) ? count + 1 : 1;
    }

    static void ReleasePackageLease(string packageKey)
    {
        if (!Leases.TryGetValue(packageKey, out int count))
            throw new InvalidOperationException("The package lease is not active.");
        if (count == 1)
            Leases.Remove(packageKey);
        else
            Leases[packageKey] = count - 1;
    }

    internal sealed class PackageLeaseSet : IDisposable
    {
        HashSet<string>? _packageKeys = new(StringComparer.Ordinal);

        internal void Lease(BrowserPackageCoordinate coordinate)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            Lease(PackageKey(coordinate));
        }

        internal void Lease(string packageKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageKey);
            ObjectDisposedException.ThrowIf(_packageKeys is null, this);
            if (!_packageKeys.Add(packageKey))
                return;

            try
            {
                LeasePackage(packageKey);
            }
            catch
            {
                _packageKeys.Remove(packageKey);
                throw;
            }
        }

        public void Dispose()
        {
            if (_packageKeys is not { } packageKeys)
                return;

            _packageKeys = null;
            foreach (string packageKey in packageKeys)
                ReleasePackageLease(packageKey);
        }
    }

    internal static async ValueTask RegisterAcquiredPackageAsync(
        BrowserPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string key = PackageKey(package.PackageId, package.Version);
        Cache.Remove(key);
        await MakeCacheRoomAsync(
                package.RetainedBytes.LongLength,
                additionalEntries: 1)
            .ConfigureAwait(false);
        Cache[key] = new CacheEntry(
            package.RetainedBytes,
            package.Content,
            ++_clock);
    }

    static void TouchPackages(IEnumerable<string> packageKeys)
    {
        foreach (string packageKey in packageKeys)
        {
            if (!Cache.TryGetValue(packageKey, out CacheEntry? entry))
            {
                throw new InvalidOperationException(
                    "An open browser workspace lost its retained package-cache entry.");
            }

            Cache[packageKey] = entry with { LastAccess = ++_clock };
        }
    }

    static string PackageKey(BrowserPackageCoordinate coordinate) =>
        PackageKey(coordinate.PackageId, coordinate.Version);

    internal static string PackageKey(string packageId, string version) =>
        $"{packageId.ToLowerInvariant()}@{version.ToLowerInvariant()}";

    static string PackageScopeKey(
        IReadOnlyList<BrowserPackageCoordinate> coordinates) =>
        CompositeKey(
            [
                "packages",
                .. coordinates
                .Select(coordinate => coordinate.Key)
                .Order(StringComparer.Ordinal),
            ]);

    internal static string CompositeKey(params string[] components)
    {
        var key = new StringBuilder();
        foreach (string component in components)
        {
            key.Append(component.Length);
            key.Append(':');
            key.Append(component);
        }

        return key.ToString();
    }

    internal static void ValidateArchive(byte[] archive)
    {
        if (PackageArchiveValidator.Validate(archive, PayloadLimits)
            is PackageArchiveValidation.Rejected rejection)
        {
            throw new InvalidOperationException(
                $"The package is outside the Browser/Wasm payload policy: {rejection.Reason}.");
        }
    }

    sealed class BrowserSessionPackageStore
        : IPackageStore, IPackagePayloadTransferPolicy
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null)
        {
            string key = PackageKey(packageName, version);
            if (!Cache.TryGetValue(key, out CacheEntry? entry)
                || allowedSourceKeys is null
                || !allowedSourceKeys.Contains(
                    entry.ProducerKey,
                    StringComparer.Ordinal))
            {
                return null;
            }

            Cache[key] = entry with { LastAccess = ++_clock };
            log?.Invoke($"Using cached package: {packageName} {version}");
            return entry.Content.AsCacheHit();
        }

        public async ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
            ArgumentNullException.ThrowIfNull(nupkg);

            string key = PackageKey(packageName, version);
            if (!Reservations.TryGetValue(
                    key,
                    out PackageDownloadReservation? reservation))
            {
                throw new InvalidOperationException(
                    "The Browser package store requires a pre-download reservation.");
            }

            byte[] bytes;
            if (nupkg is MemoryStream memory
                && memory.Position == 0
                && memory.TryGetBuffer(out ArraySegment<byte> segment)
                && segment.Offset == 0
                && segment.Count == segment.Array!.Length)
            {
                bytes = segment.Array;
            }
            else
            {
                bytes = await BoundedContentReader.ReadAllBytesAsync(
                    nupkg,
                    reservation.ReservedBytes,
                    reservation.ReservedBytes,
                    cancellationToken).ConfigureAwait(false);
            }

            var content = InMemoryPackageContent.CreateOwned(
                bytes,
                fromCache: false,
                sourceKey);
            reservation.Stage(bytes, content);
            return content;
        }

        public async ValueTask<IPackagePayloadReservation> ReserveAsync(
            PackagePayloadTransfer transfer,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transfer);
            cancellationToken.ThrowIfCancellationRequested();
            long declaredLength = transfer.AdvertisedLength
                ?? throw new InvalidOperationException(
                    $"Package '{transfer.Coordinate.PackageId}' "
                    + $"{transfer.Coordinate.Version} did not declare its byte length, "
                    + "so the Browser cannot reserve its package-cache budget before download.");
            return await ReservePackageDownloadAsync(
                    PackageKey(
                        transfer.Coordinate.PackageId,
                        transfer.Coordinate.Version),
                    declaredLength)
                .ConfigureAwait(false);
        }
    }

    internal sealed class PackageDownloadReservation
        : IPackagePayloadReservation
    {
        readonly string _packageKey;
        byte[]? _stagedBytes;
        InMemoryPackageContent? _stagedContent;
        bool _completed;

        internal PackageDownloadReservation(
            string packageKey,
            long reservedBytes)
        {
            _packageKey = packageKey;
            ReservedBytes = reservedBytes;
        }

        internal long ReservedBytes { get; }

        internal void Stage(
            byte[] bytes,
            InMemoryPackageContent content)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(content);
            if (_completed || _stagedBytes is not null)
                throw new InvalidOperationException("The package reservation is complete.");
            if (bytes.LongLength != ReservedBytes)
            {
                throw new InvalidDataException(
                    "The downloaded package length does not match its reservation.");
            }

            _stagedBytes = bytes;
            _stagedContent = content;
        }

        public void Complete()
        {
            if (_completed)
                throw new InvalidOperationException("The package reservation is complete.");
            if (_stagedBytes is null || _stagedContent is null)
            {
                throw new InvalidOperationException(
                    "The package reservation has no validated content to publish.");
            }

            RemoveReservation();
            Cache[_packageKey] = new CacheEntry(
                _stagedBytes,
                _stagedContent,
                ++_clock);
            Downloaded.Add(_packageKey);
            _completed = true;
        }

        public void Dispose()
        {
            if (_completed)
                return;
            RemoveReservation();
            _completed = true;
        }

        void RemoveReservation()
        {
            if (Reservations.TryGetValue(
                    _packageKey,
                    out PackageDownloadReservation? active)
                && ReferenceEquals(active, this))
            {
                Reservations.Remove(_packageKey);
            }
        }
    }
}

/// <summary>One exact package coordinate request used to assemble a browser workspace.</summary>
internal sealed record BrowserPackageRequest(
    string PackageId,
    string? Version,
    string? TargetFramework);

/// <summary>
/// A registry-owned scope together with the coordinates resolved for this request, in request
/// order. The scope may have been opened earlier with the same coordinate set in another order.
/// </summary>
internal sealed record BrowserScopeResolution(
    BrowserInspectionScope Scope,
    ImmutableArray<BrowserPackageCoordinate> RequestedCoordinates);

/// <summary>One acquired package: its exact identity and its content.</summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPackage
{
    const long MaxTextEntryBytes = 16L * 1024 * 1024;
    readonly AcquiredPackageSourcePayload? _acquiredPayload;
    readonly Lazy<BrowserPackageIconPayload?> _icon;

    public BrowserPackage(
        string packageId,
        string version,
        byte[] retainedBytes,
        bool fromCache,
        string? producerKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(retainedBytes);
        producerKey ??= NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        BrowserPackageWorkspace.ValidateArchive(retainedBytes);
        PackageId = packageId;
        Version = version;
        RetainedBytes = retainedBytes;
        Content = InMemoryPackageContent.CreateOwned(
            retainedBytes,
            fromCache,
            producerKey);
        _icon = new(ProjectIcon);
    }

    internal BrowserPackage(
        string requestedPackageId,
        AcquiredPackageSourcePayload acquiredPayload,
        byte[] retainedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPackageId);
        ArgumentNullException.ThrowIfNull(acquiredPayload);
        ArgumentNullException.ThrowIfNull(retainedBytes);
        if (!requestedPackageId.Equals(
                acquiredPayload.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The acquired package payload does not match the requested package.",
                nameof(acquiredPayload));
        }
        if (acquiredPayload.Content is not InMemoryPackageContent content)
        {
            throw new ArgumentException(
                "The Browser package store returned non-memory package content.",
                nameof(acquiredPayload));
        }

        BrowserPackageWorkspace.ValidateArchive(retainedBytes);
        PackageId = requestedPackageId;
        Version = acquiredPayload.Coordinate.Version;
        RetainedBytes = retainedBytes;
        Content = content;
        _acquiredPayload = acquiredPayload;
        _icon = new(ProjectIcon);
    }

    public string PackageId { get; }

    public string Version { get; }

    public InMemoryPackageContent Content { get; }

    internal byte[] RetainedBytes { get; }

    public BrowserPackageIconPayload? Icon => _icon.Value;

    internal PackageRootBinding CreateRootBinding(string? targetFramework) =>
        PackageRootBinding.CreateFromSource(
            _acquiredPayload
            ?? throw new InvalidOperationException(
                "Only an acquisition-issued Browser package can create a bound package Root."),
            targetFramework,
            displayPackageId: PackageId);

    /// <summary>
    /// The package's browsable Markdown: a root <c>README.md</c>/<c>PACKAGE.md</c> and any
    /// <c>*.md</c> under a <c>skills</c> directory. Presence and size only; bodies are served by
    /// <see cref="ReadDocument"/>, which accepts only a path from this list, so no caller can
    /// coax an arbitrary entry — an assembly, a signature — out of the package.
    /// </summary>
    public IReadOnlyList<BrowserPackageDocumentEntry> Documents()
    {
        var documents = new List<BrowserPackageDocumentEntry>();
        foreach (PackageContentEntry entry in Content.EnumerateEntriesWithLengths())
        {
            string[] segments = entry.Path.Split('/');
            string fileName = segments[^1];
            bool isRoot = segments.Length == 1;
            string? kind =
                isRoot && fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? "readme"
                : isRoot && fileName.Equals("PACKAGE.md", StringComparison.OrdinalIgnoreCase) ? "package"
                : fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && IsUnderSkillsDirectory(segments) ? "skill"
                : null;
            if (kind is null)
                continue;
            if (entry.Length > MaxTextEntryBytes || entry.Length > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"A browsable document in {PackageId} {Version} exceeds the browser byte "
                    + "limit.");
            }

            documents.Add(new BrowserPackageDocumentEntry(
                kind,
                kind == "skill" ? SkillDisplayName(segments) : fileName,
                entry.Path,
                (int)entry.Length));
        }

        return
        [
            .. documents
                .OrderBy(document => document.Kind switch { "readme" => 0, "package" => 1, _ => 2 })
                .ThenBy(document => document.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public BrowserPackageDocumentPayload ReadDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        BrowserPackageDocumentEntry document = Documents()
            .FirstOrDefault(candidate => candidate.Path.Equals(path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{path}' is not a browsable document in {PackageId} {Version}.");
        return new BrowserPackageDocumentPayload(
            document.Kind,
            document.Name,
            document.Path,
            Encoding.UTF8.GetString(Read(document.Path, MaxTextEntryBytes)));
    }

    internal Stream OpenEntry(string path, long maxExpandedBytes)
        => Content.TryOpenEntry(path, maxExpandedBytes, out Stream? stream)
            ? stream
            : throw new InvalidOperationException(
                $"The requested package entry was not found in {PackageId} {Version}.");

    internal byte[] Read(string path, long maxExpandedBytes)
    {
        using Stream stream = OpenEntry(path, maxExpandedBytes);
        if (stream is MemoryStream memory
            && memory.TryGetBuffer(out ArraySegment<byte> segment)
            && segment.Offset == 0
            && segment.Count == segment.Array!.Length)
        {
            return segment.Array;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal bool TryRead(string path, long maxExpandedBytes, out byte[] bytes)
    {
        if (!Content.TryOpenEntry(path, maxExpandedBytes, out Stream? stream))
        {
            bytes = [];
            return false;
        }

        using (stream)
        {
            if (stream is MemoryStream memory
                && memory.TryGetBuffer(out ArraySegment<byte> segment)
                && segment.Offset == 0
                && segment.Count == segment.Array!.Length)
            {
                bytes = segment.Array;
                return true;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            return true;
        }
    }

    internal bool TryReadText(string path, out byte[] bytes) =>
        TryRead(path, MaxTextEntryBytes, out bytes);

    BrowserPackageIconPayload? ProjectIcon()
    {
        PackageIconResult result =
            PackageIconQuery.Execute(Content, PackageId, Version);
        if (result is not PackageIconResult.Available available)
            return null;

        return new BrowserPackageIconPayload(
            available.Value.MediaType,
            Convert.ToBase64String(available.Value.Bytes.AsSpan()));
    }

    static bool IsUnderSkillsDirectory(string[] segments)
    {
        for (int index = 0; index < segments.Length - 1; index++)
            if (segments[index].Equals("skills", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string SkillDisplayName(string[] segments)
    {
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (!segments[index].Equals("skills", StringComparison.OrdinalIgnoreCase))
                continue;
            return index + 2 < segments.Length ? segments[index + 1] : segments[^1];
        }
        return segments[^1];
    }
}

/// <summary>
/// One resolved package/version/framework Root and the compile outcome the product selector chose
/// for it. This is acquisition state: it names what a workspace would contain, and inspects
/// nothing.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageCoordinate
{
    public BrowserPackageCoordinate(
        BrowserPackage package,
        PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(binding);
        if (!package.PackageId.Equals(
                binding.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !package.Version.Equals(
                binding.Coordinate.Version,
                StringComparison.OrdinalIgnoreCase)
            || !binding.Root.ReferencesContent(package.Content))
        {
            throw new ArgumentException(
                "The product package Root binding does not describe the acquired Browser package.",
                nameof(binding));
        }

        Package = package;
        Binding = binding;
        Root = binding.Root;
    }

    public BrowserPackageCoordinate(
        BrowserPackage package,
        PackageRootRealization root)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(root);
        if (!package.PackageId.Equals(
                root.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !package.Version.Equals(
                root.PackageVersion,
                StringComparison.OrdinalIgnoreCase)
            || !root.ReferencesContent(package.Content))
        {
            throw new ArgumentException(
                "The product package Root must describe the acquired Browser package and its "
                + "exact content.",
                nameof(root));
        }

        Package = package;
        Root = root;
    }

    public BrowserPackage Package { get; }

    public PackageRootBinding? Binding { get; }

    public PackageRootRealization Root { get; }

    public RealizedMemberCoordinate.Package RealizedCoordinate =>
        Binding?.Coordinate
        ?? throw new InvalidOperationException(
            "The legacy Browser package coordinate has no acquisition-issued binding.");

    public PackageCompileAssetSelection Selection =>
        Root.AssetSelection;

    public string PackageId => Package.PackageId;

    public string Version => Package.Version;

    public string Framework =>
        Selection.TargetFramework
        ?? Root.RequestedTargetFramework
        ?? "";

    /// <summary>
    /// The exact coordinate this workspace answers for. Each component is length-prefixed so
    /// caller-controlled framework text cannot alter coordinate or workspace key boundaries.
    /// </summary>
    public string Key =>
        BrowserPackageWorkspace.CompositeKey(
            PackageId.ToLowerInvariant(),
            Version.ToLowerInvariant(),
            Framework.ToLowerInvariant());

    public bool HasExactContentAs(BrowserPackageCoordinate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Binding is not null || other.Binding is not null)
        {
            return Binding is not null
                && other.Binding is not null
                && Key.Equals(other.Key, StringComparison.Ordinal)
                && ReferenceEquals(
                    Binding.ContentGenerationIdentity,
                    other.Binding.ContentGenerationIdentity);
        }

        return Key.Equals(other.Key, StringComparison.Ordinal)
            && ReferenceEquals(Package.RetainedBytes, other.Package.RetainedBytes)
            && Root.ProducerKey.Equals(
                other.Root.ProducerKey,
                StringComparison.Ordinal);
    }

    public PackageCompileAsset? DefaultAsset => Selection.DefaultAsset;

    /// <summary>Every assembly the package ships for the selected framework.</summary>
    public IReadOnlyList<PackageCompileAsset> FrameworkAssets => Selection.FrameworkAssets;

    /// <summary>Every assembly in the shared selector's effective implementation universe.</summary>
    public IReadOnlyList<PackageCompileAsset> ImplementationAssets =>
        Selection.ImplementationAssets;

    /// <summary>The selected compile asset for one assembly, by product-owned identity or name.</summary>
    public PackageCompileAsset CompileAsset(string assemblyIdOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyIdOrName);
        if (!Selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{PackageId} {Version} has no selected compile library "
                + $"({Selection.Status}).");
        }
        return Selection.FindAsset(assemblyIdOrName)
            ?? Selection.Assets.FirstOrDefault(asset => MatchesAssembly(asset, assemblyIdOrName))
            ?? throw new InvalidOperationException(
                $"'{assemblyIdOrName}' is not a selected compile assembly of "
                + $"{PackageId} {Version}.");
    }

    /// <summary>
    /// The implementation assembly for one assembly name. Body-backed work resolves the matching
    /// asset from the shared effective implementation universe rather than reasoning about
    /// package paths.
    /// </summary>
    public PackageCompileAsset ImplementationAsset(string assemblyIdOrName)
    {
        PackageCompileAsset selected = CompileAsset(assemblyIdOrName);
        return Selection.FindImplementationAsset(selected)
            ?? throw new InvalidOperationException(
                $"The requested compile assembly in {PackageId} {Version} is a reference "
                + "assembly only, so it carries no method bodies.");
    }

    internal static bool MatchesAssembly(PackageCompileAsset asset, string name) =>
        asset.AssemblyName.Equals(name, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(asset.AssemblyName)
            .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
}
