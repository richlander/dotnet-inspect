using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InspectWeb.Acquisition;
using NuGetFetch;

namespace InspectWeb.Engine;

/// <summary>
/// Browser acquisition adapter: shared package owners resolve and admit payloads, while this host
/// owns the bounded session cache and registry of open workspaces.
/// </summary>
/// <remarks>
/// <para>
/// Acquisition mints typed <see cref="ResolvedAssemblyReference"/> participants; it never inspects
/// one. Inspection happens only inside a <see cref="BrowserInspectionScope"/>, and only through a
/// public product query that takes the scope's <see cref="AssemblyContextGroup"/>. Browser/Wasm is
/// single-threaded, so both caches are deliberately lock-free.
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
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_ExpiredDeadlineCannotPublishReservedContent</c>
/// gates the final monotonic check before cache publication, and
/// <c>BrowserEngineBoundaryTests.PackageOperation_LateFailureBecomesVisibleTimeout</c>
/// gates timeout classification after synchronous work overruns the deadline.
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_ExactPinUsesGalleryCdnWithoutServiceIndex</c>
/// and
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_FloatingRootUsesGallerySearchAndCdn</c>
/// gate the service-index-free Gallery routes, while
/// <c>BrowserEngineBoundaryTests.PackageAcquisition_RejectedReservationDisposesGalleryPayload</c>
/// gates response ownership when Browser capacity policy rejects a transfer.
/// <c>BrowserEngineBoundaryTests.BrowserGalleryDeadlineLeavesTimeForPartialRegistration</c>
/// and
/// <c>BrowserEngineBoundaryTests.VersionPickerRetainsFlatListWhenRegistrationTimesOut</c>
/// gate the timeout margin that lets optional registration degrade to a partial
/// version-picker result before the Browser operation ceiling.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageWorkspace
{
    const int MaxCachedPackages = 12;
    const long MaxCachedPackageBytes = 128L * 1024 * 1024;
    const int MaxOpenScopes = 4;
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
    static readonly UniformPackageSourceAuthorization SourceAuthorization =
        new([PackageSource.NuGetOrg]);
    internal static readonly IPackageSourceClient Gallery =
        PackageSourceClientFactory.CreateGallery(
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
    static readonly Dictionary<string, Task<AcquiredPackageSourcePayload>> PendingAcquisitions =
        new(StringComparer.Ordinal);
    static readonly HashSet<string> Downloaded = new(StringComparer.Ordinal);
    static long _clock;

    internal static HttpClient NetworkClient => Http;
    internal static void ConfigureMsdlProxy(string origin) =>
        MsdlProxyHandler.Configure(origin);
    internal static IPackageSourceAuthorization PackageSourceAuthorization =>
        SourceAuthorization;

    sealed record CacheEntry(byte[] Bytes, string ProducerKey, long LastAccess);

    sealed record ScopeEntry(
        BrowserInspectionScope Scope,
        ImmutableHashSet<string> PackageKeys,
        long LastAccess,
        int ActiveLeases);

    public static BrowserPackageCacheStats Stats() =>
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
                deadline,
                cancellationToken),
            PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPackage> AcquireAsync(
        string packageId,
        string? version,
        IPackageSourceClient source,
        TimeSpan operationTimeout) =>
        RunPackageOperationAsync(
            deadline => AcquireCoreAsync(
                packageId,
                version,
                source,
                deadline,
                CancellationToken.None),
            operationTimeout);

    static async Task<BrowserPackage> AcquireCoreAsync(
        string packageId,
        string? version,
        IPackageSourceClient source,
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
        string pendingKey =
            $"{key}@{NuGetCache.GetSourceKey(source.Identity.Value)}";
        if (!PendingAcquisitions.TryGetValue(
                pendingKey,
                out Task<AcquiredPackageSourcePayload>? pending))
        {
            pending = AcquirePayloadWithinOperationAsync(
                coordinate,
                source,
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
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The shared package acquisition completed without publishing its Browser cache entry.");
        }

        Cache[key] = cached with { LastAccess = ++_clock };
        return new BrowserPackage(
            packageId,
            coordinate.Version,
            cached.Bytes,
            payload.Origin == PackagePayloadOrigin.Cache,
            cached.ProducerKey);
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

    /// <summary>
    /// Resolves one exact package/version/framework identity into a selected, acquirable
    /// coordinate. The result carries typed participants but performs no inspection.
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
        PackageCompileAssetSelection selection = PackageCompileAssetSelector.Select(
            package.Content,
            packageId,
            targetFramework);
        return selection.Status switch
        {
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                throw new InvalidOperationException(
                    $"{package.PackageId} {package.Version} has no compile-time assemblies, so it "
                    + "has no inspection workspace."),
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                throw new InvalidOperationException(
                    $"{package.PackageId} {package.Version} declares an empty compile group for "
                    + $"{selection.TargetFramework}, so it ships no API surface for that "
                    + "framework. Available frameworks: "
                    + string.Join(", ", selection.AvailableTargetFrameworks)
                    + "."),
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework =>
                throw new InvalidOperationException(
                    $"Framework '{targetFramework}' is not present. Available frameworks: "
                    + string.Join(", ", selection.AvailableTargetFrameworks)
                    + "."),
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets =>
                throw new InvalidOperationException(
                    selection.Message
                    ?? "The package has an invalid implementation-asset layout."),
            PackageCompileAssetSelectionStatus.Selected when selection.IsSelected =>
                new BrowserPackageCoordinate(package, selection),
            _ => throw new InvalidOperationException(
                "Package compile-asset selection returned an unknown outcome."),
        };
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
    public static BrowserInspectionScope OpenScope(
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
            throw new ArgumentException("A workspace requires at least one package coordinate.");

        string key = string.Join(
            "|",
            coordinates.Select(coordinate => coordinate.Key).Order(StringComparer.Ordinal));
        if (Scopes.TryGetValue(key, out ScopeEntry? entry))
        {
            Scopes[key] = entry with { LastAccess = ++_clock };
            TouchPackages(entry.PackageKeys);
            return entry.Scope;
        }

        ImmutableHashSet<string> packageKeys = RetainCoordinatePackages(coordinates);
        var scope = new BrowserInspectionScope(coordinates);
        while (Scopes.Count >= MaxOpenScopes)
        {
            string? oldest = Scopes
                .Where(candidate => candidate.Value.ActiveLeases == 0)
                .OrderBy(candidate => candidate.Value.LastAccess)
                .Select(candidate => candidate.Key)
                .FirstOrDefault();
            if (oldest is null)
            {
                scope.Dispose();
                throw new InvalidOperationException(
                    "The browser workspace limit cannot evict an active inspection.");
            }
            Scopes[oldest].Scope.Dispose();
            Scopes.Remove(oldest);
        }

        Scopes[key] = new ScopeEntry(scope, packageKeys, ++_clock, ActiveLeases: 0);
        return scope;
    }

    /// <summary>
    /// Pins a registry-owned scope and its package archives for one asynchronous inspection.
    /// </summary>
    internal static BrowserInspectionScopeLease LeaseScope(
        BrowserInspectionScope scope)
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
        return new BrowserInspectionScopeLease(
            scope,
            () => ReleaseScopeLease(registered.Key, scope));
    }

    static void ReleaseScopeLease(
        string scopeKey,
        BrowserInspectionScope scope)
    {
        if (!Scopes.TryGetValue(scopeKey, out ScopeEntry? entry)
            || !ReferenceEquals(entry.Scope, scope)
            || entry.ActiveLeases <= 0)
        {
            throw new InvalidOperationException(
                "The browser inspection scope lease is not active.");
        }

        Scopes[scopeKey] = entry with
        {
            ActiveLeases = entry.ActiveLeases - 1,
        };
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

            BrowserInspectionScope scope = OpenScope(coordinates);
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
        CancellationToken cancellationToken,
        IPackagePayloadTransferPolicy transferPolicy)
    {
        PackageSourcePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
            source,
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

    internal static Task<T> WaitForSharedAcquisitionAsync<T>(
        Task<T> acquisition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        return acquisition.WaitAsync(cancellationToken);
    }

    static void ObserveAndRemovePendingAcquisition(
        string key,
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

    static Task<AcquiredPackageSourcePayload> AcquirePayloadWithinOperationAsync(
        PackageSourceCoordinate coordinate,
        IPackageSourceClient source,
        TimeSpan timeout) =>
        RunPackageOperationAsync(
            deadline => AcquirePayloadAsync(
                coordinate,
                source,
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
        return operation switch
        {
            PackageSourceOperationResult<PackageVersionResult>.Succeeded succeeded =>
                succeeded.Value,
            PackageSourceOperationResult<PackageVersionResult>.Failed failed =>
                throw new InvalidOperationException(failed.Failure.Message),
            _ => throw new InvalidOperationException(
                "Package version listing returned an unknown outcome."),
        };
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
            deadline.ThrowIfExpired();
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
        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer) =>
            ApplyDeadline(inner.Reserve(transfer));

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
        Dictionary<string, BrowserPackage> packages = coordinates
            .GroupBy(PackageKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Package,
                StringComparer.Ordinal);
        if (packages.Count > MaxCachedPackages)
        {
            throw new InvalidOperationException(
                "The requested workspace's package count exceeds the browser package-cache limit.");
        }

        ImmutableHashSet<string> packageKeys =
            packages.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        foreach ((string packageKey, BrowserPackage package) in packages)
        {
            if (!Cache.TryGetValue(packageKey, out CacheEntry? entry)
                || !ReferenceEquals(entry.Bytes, package.RetainedBytes))
            {
                throw new InvalidOperationException(
                    "A resolved browser package escaped aggregate cache accounting before its "
                    + "workspace opened.");
            }
        }

        TouchPackages(packageKeys);
        return packageKeys;
    }

    static void MakeCacheRoom(
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

            EvictPackage(oldest);
        }
    }

    static void EvictPackage(string packageKey)
    {
        string[] retainedScopes =
        [
            .. Scopes
                .Where(entry => entry.Value.PackageKeys.Contains(packageKey))
                .Select(entry => entry.Key),
        ];
        foreach (string scopeKey in retainedScopes)
        {
            Scopes[scopeKey].Scope.Dispose();
            Scopes.Remove(scopeKey);
        }

        Cache.Remove(packageKey);
    }

    internal static PackageDownloadReservation ReservePackageDownload(
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

        MakeCacheRoom(declaredLength, additionalEntries: 1);
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

    internal static void RegisterAcquiredPackage(BrowserPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string key = PackageKey(package.PackageId, package.Version);
        Cache.Remove(key);
        MakeCacheRoom(package.RetainedBytes.LongLength, additionalEntries: 1);
        Cache[key] = new CacheEntry(
            package.RetainedBytes,
            package.Content.ProducerKey,
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

    static string PackageKey(string packageId, string version) =>
        $"{packageId.ToLowerInvariant()}@{version.ToLowerInvariant()}";

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
            return new InMemoryPackageContent(
                entry.Bytes,
                fromCache: true,
                entry.ProducerKey);
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

            reservation.Stage(bytes, sourceKey);
            return new InMemoryPackageContent(
                bytes,
                fromCache: false,
                sourceKey);
        }

        public IPackagePayloadReservation Reserve(
            PackagePayloadTransfer transfer)
        {
            ArgumentNullException.ThrowIfNull(transfer);
            long declaredLength = transfer.AdvertisedLength
                ?? throw new InvalidOperationException(
                    $"Package '{transfer.Coordinate.PackageId}' "
                    + $"{transfer.Coordinate.Version} did not declare its byte length, "
                    + "so the Browser cannot reserve its package-cache budget before download.");
            return ReservePackageDownload(
                PackageKey(
                    transfer.Coordinate.PackageId,
                    transfer.Coordinate.Version),
                declaredLength);
        }
    }

    internal sealed class PackageDownloadReservation
        : IPackagePayloadReservation
    {
        readonly string _packageKey;
        byte[]? _stagedBytes;
        string? _producerKey;
        bool _completed;

        internal PackageDownloadReservation(
            string packageKey,
            long reservedBytes)
        {
            _packageKey = packageKey;
            ReservedBytes = reservedBytes;
        }

        internal long ReservedBytes { get; }

        internal void Stage(byte[] bytes, string producerKey)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
            if (_completed || _stagedBytes is not null)
                throw new InvalidOperationException("The package reservation is complete.");
            if (bytes.LongLength != ReservedBytes)
            {
                throw new InvalidDataException(
                    "The downloaded package length does not match its reservation.");
            }

            _stagedBytes = bytes;
            _producerKey = producerKey;
        }

        public void Complete()
        {
            if (_completed)
                throw new InvalidOperationException("The package reservation is complete.");
            if (_stagedBytes is null || _producerKey is null)
            {
                throw new InvalidOperationException(
                    "The package reservation has no validated content to publish.");
            }

            RemoveReservation();
            Cache[_packageKey] = new CacheEntry(
                _stagedBytes,
                _producerKey,
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
    const long MaxAssemblyEntryBytes = BrowserInspectionScope.MaxRetainedImageBytes;
    const long MaxTextEntryBytes = 16L * 1024 * 1024;

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
        Content = new InMemoryPackageContent(
            retainedBytes,
            fromCache,
            producerKey);
    }

    public string PackageId { get; }

    public string Version { get; }

    public InMemoryPackageContent Content { get; }

    internal byte[] RetainedBytes { get; }

    /// <summary>
    /// The package's browsable Markdown: a root <c>README.md</c>/<c>PACKAGE.md</c> and any
    /// <c>*.md</c> under a <c>skills</c> directory. Presence and size only; bodies are served by
    /// <see cref="ReadDocument"/>, which accepts only a path from this list, so no caller can
    /// coax an arbitrary entry — an assembly, a signature — out of the package.
    /// </summary>
    public IReadOnlyList<BrowserPackageDocument> Documents()
    {
        var documents = new List<BrowserPackageDocument>();
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

            documents.Add(new BrowserPackageDocument(
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

    public BrowserPackageDocumentContent ReadDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        BrowserPackageDocument document = Documents()
            .FirstOrDefault(candidate => candidate.Path.Equals(path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{path}' is not a browsable document in {PackageId} {Version}.");
        return new BrowserPackageDocumentContent(
            document.Kind,
            document.Name,
            document.Path,
            Encoding.UTF8.GetString(Read(document.Path, MaxTextEntryBytes)));
    }

    internal Stream OpenEntry(string path, long maxExpandedBytes)
        => Content.TryOpenEntry(path, maxExpandedBytes, out Stream? stream)
            ? stream
            : throw new InvalidOperationException($"'{path}' was not found in {PackageId} {Version}.");

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

    /// <summary>
    /// Mints one typed acquisition participant for a selected package entry. A healthy image uses
    /// its real metadata identity. A malformed, native, or module image uses its selected asset
    /// name only as a rejection carrier, so the workspace query reports that participant's typed
    /// acquisition failure instead of silently shortening the selected assembly set.
    /// </summary>
    internal ResolvedAssemblyReference CreateReference(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        AssemblyReferenceIdentity? identity =
            BrowserAssemblyIdentityDecoder.Decode(Read(path, MaxAssemblyEntryBytes));

        return ResolvedAssemblyReference.Create(
            identity ?? new AssemblyReferenceIdentity(
                Path.GetFileNameWithoutExtension(path),
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => OpenEntry(path, MaxAssemblyEntryBytes),
            provenance);
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
/// One resolved package/version/framework coordinate and the compile assets the product selector
/// chose for it. This is acquisition state: it names what a workspace would contain, and inspects
/// nothing.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageCoordinate(
    BrowserPackage package,
    PackageCompileAssetSelection selection)
{
    public BrowserPackage Package { get; } = package;

    public PackageCompileAssetSelection Selection { get; } = selection;

    public string PackageId => Package.PackageId;

    public string Version => Package.Version;

    public string Framework => Selection.TargetFramework!;

    /// <summary>
    /// The exact coordinate this workspace answers for. It is the registry key, so two requests
    /// for the same package, resolved version, and framework reuse one open workspace rather than
    /// reacquiring every image.
    /// </summary>
    public string Key =>
        $"{PackageId.ToLowerInvariant()}@{Version.ToLowerInvariant()}/{Framework.ToLowerInvariant()}";

    public PackageCompileAsset DefaultAsset => Selection.DefaultAsset!;

    /// <summary>Every assembly the package ships for the selected framework.</summary>
    public IReadOnlyList<PackageCompileAsset> FrameworkAssets => Selection.FrameworkAssets;

    /// <summary>Every assembly in the shared selector's effective implementation universe.</summary>
    public IReadOnlyList<PackageCompileAsset> ImplementationAssets =>
        Selection.ImplementationAssets;

    /// <summary>The selected compile asset for one assembly, by product-owned identity or name.</summary>
    public PackageCompileAsset CompileAsset(string assemblyIdOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyIdOrName);
        return Selection.FindAsset(assemblyIdOrName)
            ?? Selection.Assets.FirstOrDefault(asset => MatchesAssembly(asset, assemblyIdOrName))
            ?? throw new InvalidOperationException(
                $"'{assemblyIdOrName}' is not a selected compile assembly of "
                + $"{PackageId} {Version} for {Framework}.");
    }

    /// <summary>
    /// The implementation assembly for one assembly name. Reference assemblies carry no method
    /// bodies, so body-backed work resolves the matching asset from the shared effective
    /// implementation universe rather than reasoning about package paths.
    /// </summary>
    public PackageCompileAsset ImplementationAsset(string assemblyIdOrName)
    {
        PackageCompileAsset selected = CompileAsset(assemblyIdOrName);
        if (selected.Kind == PackageCompileAssetKind.Library)
            return selected;

        return Selection.FindImplementationAsset(selected)
            ?? throw new InvalidOperationException(
                $"{PackageId} {Version} ships {selected.AssemblyName} for {Framework} as a "
                + "reference assembly only, so it carries no method bodies.");
    }

    internal static bool MatchesAssembly(PackageCompileAsset asset, string name) =>
        asset.AssemblyName.Equals(name, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(asset.AssemblyName)
            .Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
}
