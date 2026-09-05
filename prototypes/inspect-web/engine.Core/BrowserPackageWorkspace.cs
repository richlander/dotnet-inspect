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

    /// <summary>
    /// Every counted workspace entry — pending construction, ready, retiring, and terminally
    /// failed — in one bounded table. An entry is its own identity: a replacement for the same
    /// coordinates is a different entry, so a stale construction can never publish into it.
    /// </summary>
    static readonly List<ScopeEntry> Scopes = [];
    static readonly Dictionary<string, PackageDownloadReservation> Reservations =
        new(StringComparer.Ordinal);
    static readonly Dictionary<string, int> Leases = new(StringComparer.Ordinal);
    static readonly Dictionary<PendingAcquisitionKey, Task<AcquiredPackageSourcePayload>>
        PendingAcquisitions = [];
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
    /// Opens — or joins — the one workspace for an exact set of package coordinates and returns
    /// protected use of it. Several coordinates produce binding-consistent compile and
    /// implementation groups. A workspace-wide interaction such as the member call graph uses the
    /// implementation group: callers in a sibling package are only visible when that package is a
    /// participant of that same group.
    /// </summary>
    /// <remarks>
    /// The scope is owned by this registry, not by the caller. The returned lease is the caller's
    /// protection: it is taken before the caller suspends and holds the workspace — and the
    /// archives it reads — until the caller's query has returned, including an asynchronous
    /// return. Callers dispose the lease and never the scope.
    /// </remarks>
    public static Task<BrowserScopeLease<BrowserInspectionScope>> OpenScopeAsync(
        IReadOnlyList<BrowserPackageCoordinate> coordinates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count == 0)
            throw new ArgumentException("A workspace requires at least one package coordinate.");

        ImmutableArray<BrowserPackageCoordinate> exact = [.. coordinates];
        ScopeDemand demand = exact is [{ Binding: { } binding }]
            ? new BoundScopeDemand(binding)
            : new CompositeScopeDemand(
                [.. exact.Select(coordinate => coordinate.Key).Order(StringComparer.Ordinal)]);
        return OpenPackageScopeAsync(demand, exact, cancellationToken);
    }

    /// <summary>
    /// Joins a retained workspace for this demand, or reserves a counted entry and builds one.
    /// The entry's full image allowance is reserved before construction starts, and the caller's
    /// protected use is taken before it suspends.
    /// </summary>
    static async Task<BrowserScopeLease<BrowserInspectionScope>> OpenPackageScopeAsync(
        ScopeDemand demand,
        ImmutableArray<BrowserPackageCoordinate> coordinates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var construction = new PackageLeaseSet();
        ImmutableHashSet<string> packageKeys = RetainCoordinatePackages(coordinates);
        foreach (string packageKey in packageKeys)
            construction.Lease(packageKey);

        ScopeAdmission admission = await ReserveScopeEntryAsync(
                PackageScopeKey(coordinates),
                demand,
                packageKeys,
                cancellationToken)
            .ConfigureAwait(false);
        return await UseAdmittedPackageScopeAsync(admission, coordinates, cancellationToken)
            .ConfigureAwait(false);
    }

    static Task<BrowserScopeLease<BrowserInspectionScope>> UseAdmittedPackageScopeAsync(
        ScopeAdmission admission,
        ImmutableArray<BrowserPackageCoordinate> coordinates,
        CancellationToken cancellationToken)
    {
        if (!admission.IsNew)
            return UseJoinedScopeAsync(admission.Use, coordinates, cancellationToken);

        ScopeEntry entry = admission.Use.Entry;
        entry.Key = PackageScopeKey(coordinates);
        entry.Coordinates = coordinates;
        entry.Binding = coordinates is [{ Binding: { } binding }] ? binding : null;
        StartConstruction(
            entry,
            async token => await BrowserInspectionScope.CreateAsync(coordinates, token)
                .ConfigureAwait(false));
        return UseScopeAsync<BrowserInspectionScope>(admission.Use, cancellationToken);
    }

    static async Task<BrowserScopeLease<BrowserInspectionScope>> UseJoinedScopeAsync(
        ScopeUse joined,
        ImmutableArray<BrowserPackageCoordinate> coordinates,
        CancellationToken cancellationToken)
    {
        BrowserScopeLease<BrowserInspectionScope> lease =
            await UseScopeAsync<BrowserInspectionScope>(joined, cancellationToken)
                .ConfigureAwait(false);
        if (lease.Scope.ContainsExactCoordinates(coordinates))
            return lease;

        await lease.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            "The retained browser workspace does not match the exact requested package content.");
    }

    /// <summary>
    /// Takes protected use of the one joinable entry for this demand, if there is one. The use is
    /// taken before the caller suspends, so a workspace a caller is waiting for can never be
    /// evicted out from under that caller's resumption.
    /// </summary>
    static ScopeUse? TryJoinScope(ScopeDemand demand)
    {
        ScopeEntry? entry = Scopes.FirstOrDefault(demand.Joins);
        return entry is null ? null : TakeUse(entry);
    }

    /// <summary>
    /// Reserves one counted entry — and with it the full workspace image allowance — before any
    /// construction starts, or joins the one retained workspace that answers this demand. The join
    /// is re-evaluated after every capacity wait, so a caller that queued behind a full registry
    /// joins the workspace another caller built while it was waiting instead of demanding a second
    /// slot for the same content. Capacity is reclaimed asynchronously: an idle workspace is
    /// retired and awaited, and an entry that is already settling is awaited rather than counted as
    /// free. An entry whose retirement failed terminally stays charged for the process lifetime and
    /// is named in the rejection a later admission sees.
    /// </summary>
    static async Task<ScopeAdmission> ReserveScopeEntryAsync(
        string key,
        ScopeDemand demand,
        ImmutableHashSet<string> packageKeys,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryJoinScope(demand) is { } joined)
                return new ScopeAdmission(joined, IsNew: false);
            if (Scopes.Count < MaxOpenScopes)
                break;

            ScopeEntry? evictable = Scopes
                .Where(candidate =>
                    candidate.State is BrowserScopeState.Ready
                    && candidate.Uses == 0
                    && !candidate.RemovalRequested)
                .OrderBy(candidate => candidate.LastAccess)
                .FirstOrDefault();
            if (evictable is not null)
            {
                await ObserveAsync(RetireEntryAsync(evictable))
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            Task[] settling =
            [
                .. Scopes
                    .Where(candidate => candidate.State is not BrowserScopeState.Failed)
                    .Select(SettlementOf)
                    .Where(settlement => settlement is not null)
                    .Select(settlement => settlement!),
            ];
            if (settling.Length > 0)
            {
                await Task.WhenAny(settling)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException(ScopeCapacityRejection());
        }

        var entry = new ScopeEntry(key, demand, ++_clock)
        {
            Uses = 1,
            PackageKeys = packageKeys,
        };
        Scopes.Add(entry);
        foreach (string packageKey in packageKeys)
            LeasePackage(packageKey);
        return new ScopeAdmission(new ScopeUse(entry, packageKeys), IsNew: true);
    }

    static string ScopeCapacityRejection()
    {
        string[] failures =
        [
            .. Scopes
                .Select(entry => entry.Failure)
                .Where(failure => failure is not null)
                .Select(failure => failure!.ToString()),
        ];
        return failures.Length == 0
            ? "The browser workspace limit cannot evict an active inspection."
            : "The browser workspace limit cannot admit another workspace: "
                + $"{failures.Length} of {MaxOpenScopes} entries stay charged after a terminal "
                + $"cleanup failure and are only recovered by restarting. "
                + string.Join(" ", failures);
    }

    /// <summary>
    /// Starts the bounded construction of one reserved entry. The construction owns everything it
    /// builds until construction and cleanup have both settled: a stale or abandoned completion
    /// disposes what it built and never publishes into the registry.
    /// </summary>
    static void StartConstruction(
        ScopeEntry entry,
        Func<CancellationToken, Task<IAsyncDisposable>> factory)
    {
        entry.ConstructionCancellation = new CancellationTokenSource();
        entry.Construction = ConstructScopeAsync(entry, factory);
    }

    static async Task<IAsyncDisposable> ConstructScopeAsync(
        ScopeEntry entry,
        Func<CancellationToken, Task<IAsyncDisposable>> factory)
    {
        using CancellationTokenSource cancellation = entry.ConstructionCancellation!;
        using var deadline = new BrowserPackageOperationDeadline(
            PackageOperationTimeout,
            cancellation.Token);
        try
        {
            IAsyncDisposable built;
            try
            {
                built = await factory(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception creationFailure)
            {
                if (creationFailure is BrowserScopeConstructionException)
                    QuarantineEntry(entry, creationFailure);
                else
                    ReleaseScopeEntry(entry);

                if (deadline.HasExpired)
                    throw deadline.Timeout(creationFailure);
                throw;
            }

            // Keep ownership of late results until cleanup settles; a timed-out wait must not
            // detach the factory and return its still-live capacity to another construction.
            entry.Scope = built;
            try
            {
                deadline.ThrowIfExpired();
                if (!Scopes.Contains(entry)
                    || entry.RemovalRequested
                    || entry.State is not BrowserScopeState.Pending)
                {
                    throw new InvalidOperationException(
                        "The browser workspace was retired before its construction completed.");
                }
            }
            catch (Exception publicationFailure)
            {
                entry.RemovalRequested = true;
                try
                {
                    await CloseEntryScopeAsync(entry).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(publicationFailure, cleanupFailure);
                }

                throw;
            }

            entry.State = BrowserScopeState.Ready;
            entry.LastAccess = ++_clock;
            return built;
        }
        finally
        {
            entry.ConstructionCancellation = null;
        }
    }

    /// <summary>
    /// Awaits an entry the caller already holds protected use of, and turns that use into the
    /// caller's lease. Cancellation is independent per caller: a cancelling caller releases only
    /// its own use, and the last pending caller leaving retires the abandoned construction.
    /// </summary>
    static async Task<BrowserScopeLease<TScope>> UseScopeAsync<TScope>(
        ScopeUse use,
        CancellationToken cancellationToken)
        where TScope : class, IAsyncDisposable
    {
        ScopeEntry entry = use.Entry;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Construction is { } construction)
                await construction.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (entry.Failure is { } failure)
                throw new InvalidOperationException(failure.ToString());
            if (entry.Scope is not TScope typed)
            {
                throw new InvalidOperationException(
                    "The browser scope registry entry names a different scope kind.");
            }

            entry.LastAccess = ++_clock;
            TouchPackages(entry.PackageKeys);
            return new BrowserScopeLease<TScope>(
                typed,
                () => ReleaseUseAsync(use));
        }
        catch (Exception operationFailure)
        {
            try
            {
                await ReleaseUseAsync(use).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(operationFailure, cleanupFailure);
            }

            throw;
        }
    }

    /// <summary>
    /// Reserves a counted entry and publishes one already-built scope into it. This is the
    /// registry's own convenience path for a caller that constructs its scope outside the
    /// registry's factory; the reservation still precedes the admission decision.
    /// </summary>
    internal static async ValueTask<T> RegisterScopeAsync<T>(
        string key,
        T scope,
        ImmutableHashSet<string>? packageKeys = null,
        Action<T>? onDisposed = null)
        where T : class, IAsyncDisposable
    {
        ScopeReservation reservation = await ReserveScopeAsync().ConfigureAwait(false);
        BrowserScopeLease<T> lease = await RegisterScopeAsync(
                reservation,
                key,
                scope,
                packageKeys
                    ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                onDisposed)
            .ConfigureAwait(false);
        T registered = lease.Scope;
        await lease.DisposeAsync().ConfigureAwait(false);
        return registered;
    }

    /// <summary>
    /// Admits one built scope into the entry reserved for it before construction started. When an
    /// equal workspace was admitted while this one was being built, the built candidate is
    /// disposed and the caller joins the retained workspace instead.
    /// </summary>
    internal static async ValueTask<BrowserScopeLease<T>> RegisterScopeAsync<T>(
        ScopeReservation reservation,
        string key,
        T scope,
        ImmutableHashSet<string> packageKeys,
        Action<T>? onDisposed = null)
        where T : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(packageKeys);
        ScopeEntry entry = reservation.Entry;
        entry.Scope = scope;
        entry.PackageKeys = packageKeys;
        entry.State = BrowserScopeState.Ready;
        entry.OnDisposed = onDisposed is null
            ? null
            : disposed => onDisposed((T)disposed);
        try
        {
            return await AdmitScopeAsync(reservation, key, scope, packageKeys)
                .ConfigureAwait(false);
        }
        catch (Exception admissionFailure)
        {
            if (entry.State is BrowserScopeState.Failed || !Scopes.Contains(entry))
                throw;

            entry.Uses = 0;
            reservation.Release();
            try
            {
                await RetireEntryAsync(entry).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(admissionFailure, cleanupFailure);
            }

            throw;
        }
    }

    static async ValueTask<BrowserScopeLease<T>> AdmitScopeAsync<T>(
        ScopeReservation reservation,
        string key,
        T scope,
        ImmutableHashSet<string> packageKeys)
        where T : class, IAsyncDisposable
    {
        RetainPackageKeys(packageKeys);
        ScopeEntry entry = reservation.Entry;
        if (!Scopes.Contains(entry))
        {
            throw new InvalidOperationException(
                "The browser scope registry reservation was retired before its workspace was "
                + "admitted.");
        }

        var keyed = new KeyedScopeDemand(key);
        if (Scopes.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, entry) && keyed.Joins(candidate))
            is { } retained)
        {
            ScopeUse joined = TakeUse(retained);
            entry.Uses = 0;
            reservation.Release();
            try
            {
                await RetireEntryAsync(entry).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                try
                {
                    await ReleaseUseAsync(joined).ConfigureAwait(false);
                }
                catch (Exception releaseFailure)
                {
                    throw new AggregateException(cleanupFailure, releaseFailure);
                }

                throw;
            }
            return await UseScopeAsync<T>(joined, CancellationToken.None)
                .ConfigureAwait(false);
        }

        entry.Key = key;
        entry.Scope = scope;
        entry.PackageKeys = packageKeys;
        entry.State = BrowserScopeState.Ready;
        entry.LastAccess = ++_clock;
        foreach (string packageKey in packageKeys)
            LeasePackage(packageKey);
        var use = new ScopeUse(entry, packageKeys);
        reservation.Release();
        TouchPackages(packageKeys);
        return new BrowserScopeLease<T>(scope, () => ReleaseUseAsync(use));
    }

    /// <summary>
    /// Reserves one counted entry — and its full image allowance — for a workspace whose exact
    /// coordinate set is only known once it has been built.
    /// </summary>
    internal static async ValueTask<ScopeReservation> ReserveScopeAsync(
        CancellationToken cancellationToken = default)
    {
        ScopeAdmission admission = await ReserveScopeEntryAsync(
                "(reserved)",
                ExclusiveScopeDemand.Instance,
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                cancellationToken)
            .ConfigureAwait(false);
        return new ScopeReservation(admission.Use.Entry);
    }

    /// <summary>
    /// Retires a reservation whose builder never published a workspace, releasing its image
    /// allowance only once the retirement has settled.
    /// </summary>
    internal static ValueTask AbandonReservationAsync(ScopeReservation reservation)
    {
        ScopeEntry entry = reservation.Entry;
        reservation.Release();
        if (!Scopes.Contains(entry))
            return ValueTask.CompletedTask;

        entry.Uses = 0;
        return new ValueTask(ObserveAsync(RetireEntryAsync(entry)));
    }

    /// <summary>
    /// The number of counted entries that a terminal cleanup failure left charged and unavailable.
    /// </summary>
    internal static int QuarantinedWorkspaces =>
        Scopes.Count(entry => entry.State is BrowserScopeState.Failed);

    /// <summary>
    /// Discards every quarantined entry, modelling the runtime restart the owning design names as
    /// the recovery boundary for a terminal cleanup failure. Nothing in the product calls this: a
    /// browser session recovers by reloading.
    /// </summary>
    internal static void SimulateRuntimeRestart()
    {
        foreach (ScopeEntry quarantined in
            Scopes.Where(entry => entry.State is BrowserScopeState.Failed).ToArray())
        {
            foreach (string packageKey in quarantined.PackageKeys)
                Cache.Remove(packageKey);
            Scopes.Remove(quarantined);
        }
    }

    /// <summary>The coordinates one retained workspace was built from.</summary>
    static ImmutableArray<BrowserPackageCoordinate> RetainedCoordinates(
        BrowserInspectionScope scope) => scope.Coordinates;

    internal static bool IsScopeRetained(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return FindOpenEntry(scope) is not null;
    }

    internal static void TouchScope(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ScopeEntry entry = FindOpenEntry(scope)
            ?? throw new InvalidOperationException(
                "The browser inspection scope is no longer retained.");
        entry.LastAccess = ++_clock;
        TouchPackages(entry.PackageKeys);
    }

    /// <summary>
    /// Requests removal of one registry-owned scope. A scope with protected uses outstanding is
    /// retired when its last use is released; a scope already retiring is awaited so the caller
    /// never observes its capacity as free before its cleanup has settled.
    /// </summary>
    internal static ValueTask RemoveScopeAsync(IAsyncDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ScopeEntry? entry = Scopes.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Scope, scope));
        if (entry is null)
            return ValueTask.CompletedTask;
        if (entry.Uses != 0)
        {
            entry.RemovalRequested = true;
            return ValueTask.CompletedTask;
        }

        return new ValueTask(RetireEntryAsync(entry));
    }

    /// <summary>
    /// Takes one more protected use of a registry-owned scope and its package archives.
    /// </summary>
    internal static BrowserScopeLease<TScope> LeaseScope<TScope>(
        TScope scope)
        where TScope : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(scope);
        ScopeEntry entry = FindOpenEntry(scope)
            ?? throw new InvalidOperationException(
                "The browser inspection scope is no longer retained.");
        ScopeUse use = TakeUse(entry);
        return new BrowserScopeLease<TScope>(scope, () => ReleaseUseAsync(use));
    }

    static ScopeEntry? FindOpenEntry(IAsyncDisposable scope) =>
        Scopes.FirstOrDefault(candidate =>
            candidate.State is BrowserScopeState.Ready
            && ReferenceEquals(candidate.Scope, scope));

    /// <summary>
    /// Takes one protected use of an entry and leases the archives that use reads. The use is
    /// recorded before the caller suspends, which is what keeps the entry — pending or ready —
    /// out of every eviction candidate set for as long as the caller needs it.
    /// </summary>
    static ScopeUse TakeUse(ScopeEntry entry)
    {
        entry.Uses++;
        entry.LastAccess = ++_clock;
        ImmutableHashSet<string> leased = entry.PackageKeys;
        foreach (string packageKey in leased)
            LeasePackage(packageKey);
        return new ScopeUse(entry, leased);
    }

    /// <summary>
    /// Releases one protected use. The last use of an entry whose removal was requested — an
    /// evicted workspace or an abandoned construction — retires it, and the archives that use
    /// leased are released once that retirement settles, including when it fails.
    /// </summary>
    static async ValueTask ReleaseUseAsync(ScopeUse use)
    {
        ScopeEntry entry = use.Entry;
        if (entry.Uses <= 0)
        {
            throw new InvalidOperationException(
                "The browser inspection scope lease is not active.");
        }

        entry.Uses--;
        try
        {
            if (entry.Uses == 0
                && (entry.RemovalRequested || entry.State is BrowserScopeState.Pending))
                await RetireEntryAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            // The archives this use pinned are released once the retirement has settled, including
            // when it failed: a terminal failure stays visible through the still-charged entry
            // rather than through an unreachable lease.
            foreach (string packageKey in use.LeasedPackages)
                ReleasePackageLease(packageKey);
        }
    }

    /// <summary>Opens — or joins — the workspace for one exact package coordinate.</summary>
    public static Task<BrowserScopeLease<BrowserInspectionScope>> OpenScopeAsync(
        string packageId,
        string? version,
        string? targetFramework,
        CancellationToken cancellationToken = default)
        => OpenUnboundScopeAsync(
            new BrowserPackageRequest(packageId, version, targetFramework),
            cancellationToken);

    /// <summary>
    /// Opens one workspace for a request that carries no acquisition-issued binding. The acquired
    /// coordinate, its producer, its retained content generation, and the caller's selection
    /// request — a default selection is not an explicit one — decide which retained workspace this
    /// request joins, and that decision is made before a selection token is issued, so repeated
    /// unbound requests share one binding instead of minting a second one.
    /// </summary>
    static async Task<BrowserScopeLease<BrowserInspectionScope>> OpenUnboundScopeAsync(
        BrowserPackageRequest request,
        CancellationToken cancellationToken)
    {
        BrowserPackage package = await AcquireAsync(
                request.PackageId,
                request.Version,
                cancellationToken)
            .ConfigureAwait(false);
        string packageKey = PackageKey(package);
        using var acquired = new PackageLeaseSet();
        acquired.Lease(packageKey);

        var demand = new UnboundScopeDemand(
            packageKey,
            SelectionRequestToken(request.TargetFramework),
            package.Content.ProducerKey,
            package.Content.GenerationIdentity);
        ScopeAdmission admission = await ReserveScopeEntryAsync(
                packageKey,
                demand,
                [packageKey],
                cancellationToken)
            .ConfigureAwait(false);
        if (!admission.IsNew)
        {
            return await UseScopeAsync<BrowserInspectionScope>(admission.Use, cancellationToken)
                .ConfigureAwait(false);
        }

        BrowserPackageCoordinate coordinate;
        try
        {
            coordinate = new BrowserPackageCoordinate(
                package,
                package.CreateRootBinding(request.TargetFramework));
            RetainCoordinatePackages([coordinate]);
        }
        catch
        {
            await ReleaseUseAsync(admission.Use).ConfigureAwait(false);
            throw;
        }

        return await UseAdmittedPackageScopeAsync(admission, [coordinate], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The caller's selection request as the registry compares it. A default selection and an
    /// explicit selection that resolves to the same framework are different requests.
    /// </summary>
    static string SelectionRequestToken(string? targetFramework) =>
        string.IsNullOrWhiteSpace(targetFramework)
            ? "(default)"
            : targetFramework.Trim().ToLowerInvariant();

    /// <summary>
    /// Resolves and temporarily leases every requested coordinate until the aggregate workspace
    /// entry owns them. A later package acquisition cannot evict an earlier coordinate while a
    /// composite workspace is still being assembled.
    /// </summary>
    public static async Task<BrowserScopeResolution> ResolveAndOpenScopeAsync(
        IReadOnlyList<BrowserPackageRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("A workspace requires at least one package request.");

        if (requests.Count == 1)
        {
            BrowserScopeLease<BrowserInspectionScope> single =
                await OpenUnboundScopeAsync(requests[0], cancellationToken)
                    .ConfigureAwait(false);
            return new BrowserScopeResolution(single, RetainedCoordinates(single.Scope));
        }

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

            BrowserScopeLease<BrowserInspectionScope> lease =
                await OpenScopeAsync(coordinates, cancellationToken);
            return new BrowserScopeResolution(lease, [.. coordinates]);
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
                try
                {
                    await DisposeLateResultAsync(result).ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(exception, cleanupFailure);
                }

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
        readonly Stopwatch _elapsed = Stopwatch.StartNew();
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
        internal CancellationToken CallerCancellation => _callerCancellation;

        internal bool HasExpired =>
            _deadlineCancellation.IsCancellationRequested
            || _elapsed.Elapsed >= _timeout;

        internal TimeSpan Remaining
        {
            get
            {
                TimeSpan remaining =
                    _timeout - _elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    ThrowIfExpired();
                return remaining;
            }
        }

        internal async ValueTask WaitForConsumerAsync(
            Func<CancellationToken, ValueTask> wait)
        {
            ThrowIfExpired();
            // The sole stream consumer is idle here; no producer work is in flight.
            _deadlineCancellation.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
            _elapsed.Stop();
            try
            {
                ThrowIfExpired();
                await wait(Token).ConfigureAwait(false);
            }
            finally
            {
                _elapsed.Start();
                TimeSpan remaining = _timeout - _elapsed.Elapsed;
                if (remaining > TimeSpan.Zero)
                    _deadlineCancellation.CancelAfter(remaining);
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
    /// Reports whether the bounded cache currently has room for one more transfer of the given
    /// size. Callers that publish into the cache re-evaluate this after every suspension: the
    /// decision is only sound at the instant the entry is added.
    /// </summary>
    static bool HasCacheRoom(long additionalBytes, int additionalEntries) =>
        Cache.Count + Reservations.Count + additionalEntries <= MaxCachedPackages
        && Cache.Values.Sum(entry => entry.Bytes.LongLength)
            + Reservations.Values.Sum(reservation => reservation.ReservedBytes)
            + additionalBytes
            <= MaxCachedPackageBytes;

    /// <summary>
    /// Frees bounded cache capacity by evicting least-recently-used unleased packages, awaiting
    /// each eviction so the retained bytes are actually released before the caller's reservation
    /// is admitted.
    /// </summary>
    static async ValueTask MakeCacheRoomAsync(
        long additionalBytes,
        int additionalEntries)
    {
        while (!HasCacheRoom(additionalBytes, additionalEntries))
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
    /// evictor of the same package joins the in-flight eviction instead of racing it, a dependent
    /// scope that another removal path is already closing is awaited rather than ignored, and a
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
            ScopeEntry? dependent = Scopes.FirstOrDefault(entry =>
                entry.PackageKeys.Contains(packageKey));
            if (dependent is null)
                break;
            if (dependent.Failure is { } failure)
                throw new InvalidOperationException(failure.ToString());
            if (SettlementOf(dependent) is { } settling)
            {
                await ObserveAsync(settling).ConfigureAwait(false);
                if (Scopes.Contains(dependent))
                    return;
                continue;
            }
            if (dependent.Uses != 0)
                return;

            await ObserveAsync(RetireEntryAsync(dependent)).ConfigureAwait(false);
            if (Scopes.Contains(dependent))
                return;
        }

        Cache.Remove(packageKey);
    }

    /// <summary>
    /// Retires one counted entry. Every removal path — capacity replacement, explicit removal,
    /// the last protected use going away, and package eviction — goes through here, so a competing
    /// path joins the same retirement instead of observing capacity or an archive that a
    /// still-retained artifact session has not released. Retirement is irreversible: a later
    /// request for the same coordinates opens a new entry rather than reviving this one.
    /// </summary>
    static Task RetireEntryAsync(ScopeEntry entry)
    {
        entry.RemovalRequested = true;
        if (entry.Settlement is { } settling)
            return settling;
        if (entry.State is BrowserScopeState.Failed)
            return Task.CompletedTask;
        if (entry.State is BrowserScopeState.Pending)
        {
            // The construction observes the retirement at its publication point, disposes what it
            // built, and only then completes. Awaiting it is awaiting the whole cleanup.
            entry.ConstructionCancellation?.Cancel();
            Task pending = entry.Construction is { } construction
                ? AwaitConstructionRetirementAsync(entry, construction)
                : Task.CompletedTask;
            if (entry.Construction is null)
                ReleaseScopeEntry(entry);
            return Record(entry, pending);
        }

        entry.State = BrowserScopeState.Retiring;
        return Record(entry, CloseEntryScopeAsync(entry));
    }

    static async Task AwaitConstructionRetirementAsync(
        ScopeEntry entry,
        Task construction)
    {
        await ObserveAsync(construction).ConfigureAwait(false);
        if (entry.Failure is { } failure)
            throw new InvalidOperationException(failure.ToString());
    }

    static Task Record(ScopeEntry entry, Task settlement)
    {
        if (settlement.IsCompleted)
        {
            _ = settlement.Exception;
            return settlement;
        }

        entry.Settlement = settlement;
        _ = settlement.ContinueWith(
            completed =>
            {
                if (ReferenceEquals(entry.Settlement, completed))
                    entry.Settlement = null;
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return settlement;
    }

    /// <summary>
    /// The in-flight retirement of one entry, if it has one. An entry that is still constructing
    /// settles through its construction, so capacity waiters await that instead of treating a
    /// half-built workspace as free room.
    /// </summary>
    static Task? SettlementOf(ScopeEntry entry) =>
        entry.Settlement
        ?? (entry.State is BrowserScopeState.Pending && entry.Construction is { } construction
            ? ObserveAsync(construction)
            : null);

    /// <summary>
    /// Closes one entry's scope and frees its charge only once that close has settled. A terminal
    /// cleanup failure leaves the entry charged and unavailable with a bounded failure record: the
    /// registry does not hand its capacity to a later workspace, and restart is the recovery
    /// boundary.
    /// </summary>
    static async Task CloseEntryScopeAsync(ScopeEntry entry)
    {
        if (entry.Scope is not { } scope)
        {
            ReleaseScopeEntry(entry);
            return;
        }

        try
        {
            try
            {
                entry.OnDisposed?.Invoke(scope);
            }
            finally
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception cleanupFailure)
        {
            QuarantineEntry(entry, cleanupFailure);
            throw;
        }

        ReleaseScopeEntry(entry);
    }

    static void QuarantineEntry(ScopeEntry entry, Exception cleanupFailure)
    {
        entry.State = BrowserScopeState.Failed;
        entry.Failure = new BrowserScopeRetirementFailure(
            entry.Key,
            Describe(cleanupFailure));
        entry.Scope = null;
    }

    /// <summary>
    /// Releases one entry's charge: its counted slot, its image allowance, and the archive
    /// dependency it held. Only a settled retirement reaches here.
    /// </summary>
    static void ReleaseScopeEntry(ScopeEntry entry)
    {
        entry.Scope = null;
        Scopes.Remove(entry);
    }

    static string Describe(Exception failure) =>
        failure is AggregateException aggregate
            ? string.Join(
                "; ",
                aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : failure.Message;

    static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The failure stays recorded on the entry and is surfaced to every caller that
            // observes the entry; a capacity or eviction waiter only needs the settlement.
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

        while (!HasCacheRoom(declaredLength, additionalEntries: 1))
        {
            await MakeCacheRoomAsync(declaredLength, additionalEntries: 1)
                .ConfigureAwait(false);
        }

        if (Reservations.ContainsKey(packageKey))
            throw new InvalidOperationException("The package download is already reserved.");

        var reservation = new PackageDownloadReservation(
            packageKey,
            declaredLength);
        Reservations.Add(packageKey, reservation);
        return reservation;
    }

    internal static IReadOnlyCollection<string> ResidentPackageKeys() =>
        [.. Cache.Keys];

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
        while (!HasCacheRoom(package.RetainedBytes.LongLength, additionalEntries: 1))
        {
            await MakeCacheRoomAsync(
                    package.RetainedBytes.LongLength,
                    additionalEntries: 1)
                .ConfigureAwait(false);
        }

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

    static string PackageKey(BrowserPackage package) =>
        PackageKey(package.PackageId, package.Version);

    internal static string PackageKey(string packageId, string version) =>
        $"{packageId.ToLowerInvariant()}@{version.ToLowerInvariant()}";

    internal static string PackageScopeKey(
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
/// <summary>
/// One counted workspace entry. An entry reserves its full image allowance the moment it is
/// created — before construction starts — and keeps that reservation until its retirement has
/// settled, so pending, ready, and retiring workspaces all count against the same bound.
/// A terminal cleanup failure leaves the entry charged and unavailable for the process
/// lifetime: restart is the recovery boundary.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class ScopeEntry(string key, ScopeDemand demand, long lastAccess)
{
    internal string Key { get; set; } = key;

    internal ScopeDemand Demand { get; } = demand;

    internal BrowserScopeState State { get; set; } = BrowserScopeState.Pending;

    internal IAsyncDisposable? Scope { get; set; }

    /// <summary>
    /// The acquisition-issued binding this entry retains. A caller that already holds a
    /// binding joins only the entry that retains that exact binding.
    /// </summary>
    internal PackageRootBinding? Binding { get; set; }

    internal ImmutableArray<BrowserPackageCoordinate> Coordinates { get; set; } = [];

    internal ImmutableHashSet<string> PackageKeys { get; set; } =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    internal long LastAccess { get; set; } = lastAccess;

    /// <summary>
    /// Protected uses: one for every caller that is waiting for construction and one for
    /// every caller whose query is still running. A use is taken before the caller suspends,
    /// so no completion can hand back a scope the registry has already retired.
    /// </summary>
    internal int Uses { get; set; }

    internal bool RemovalRequested { get; set; }

    internal Task<IAsyncDisposable>? Construction { get; set; }

    internal CancellationTokenSource? ConstructionCancellation { get; set; }

    internal Task? Settlement { get; set; }

    internal BrowserScopeRetirementFailure? Failure { get; set; }

    internal Action<IAsyncDisposable>? OnDisposed { get; set; }

    /// <summary>An entry a later request may join.</summary>
    internal bool Joinable =>
        !RemovalRequested
        && State is BrowserScopeState.Pending or BrowserScopeState.Ready;
}

/// <summary>
/// One counted workspace entry's lifecycle state. Pending, Ready, and Retiring entries all count
/// against the workspace bound; a Failed entry stays charged for the process lifetime.
/// </summary>
internal enum BrowserScopeState
{
    Pending,
    Ready,
    Retiring,
    Failed,
}

/// <summary>
/// The bounded record of a terminal workspace cleanup failure. The entry it describes stays
/// charged and unavailable; a runtime restart is the recovery boundary.
/// </summary>
internal sealed record BrowserScopeRetirementFailure(string ScopeKey, string Message)
{
    public override string ToString() =>
        $"The browser workspace '{ScopeKey}' failed to release its retained content and stays "
        + $"charged until the runtime restarts: {Message}";
}

/// <summary>
/// What a caller is asking the registry for. The demand decides which retained workspace a
/// request may join, and nothing else does.
/// </summary>
[SupportedOSPlatform("browser")]
internal abstract class ScopeDemand
{
    internal bool Joins(ScopeEntry entry) => entry.Joinable && JoinsCore(entry);

    private protected abstract bool JoinsCore(ScopeEntry entry);
}

/// <summary>
/// A request that carries no acquisition-issued binding. It joins the workspace retained for the
/// same acquired package, the same producer, the same retained content generation, and the same
/// selection request — a default selection is not an explicit one.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class UnboundScopeDemand(
    string packageKey,
    string selectionRequest,
    string producerKey,
    PackageContentGenerationIdentity generation) : ScopeDemand
{
    internal string PackageKey { get; } = packageKey;

    internal string SelectionRequest { get; } = selectionRequest;

    internal string ProducerKey { get; } = producerKey;

    internal PackageContentGenerationIdentity Generation { get; } = generation;

    private protected override bool JoinsCore(ScopeEntry entry) =>
        entry.Demand is UnboundScopeDemand other
        && string.Equals(other.PackageKey, PackageKey, StringComparison.Ordinal)
        && string.Equals(other.SelectionRequest, SelectionRequest, StringComparison.Ordinal)
        && string.Equals(other.ProducerKey, ProducerKey, StringComparison.Ordinal)
        && ReferenceEquals(other.Generation, Generation);
}

/// <summary>
/// A request that already holds an acquisition-issued binding. It joins only the workspace that
/// retains that exact binding: two independently issued selection tokens for the same labels are
/// never interchangeable.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BoundScopeDemand(PackageRootBinding binding) : ScopeDemand
{
    internal PackageRootBinding Binding { get; } = binding;

    private protected override bool JoinsCore(ScopeEntry entry) =>
        entry.Binding is { } issued
        && ReferenceEquals(
            issued.ContentGenerationIdentity,
            Binding.ContentGenerationIdentity)
        && ReferenceEquals(issued.SelectionIdentity, Binding.SelectionIdentity);
}

/// <summary>
/// A legacy multi-package workspace request, joined by its exact coordinate set. The set is
/// compared in one canonical order, so two requests for the same packages join whichever order
/// their caller listed them in.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class CompositeScopeDemand(ImmutableArray<string> coordinateKeys) : ScopeDemand
{
    internal ImmutableArray<string> CoordinateKeys { get; } = coordinateKeys;

    private protected override bool JoinsCore(ScopeEntry entry) =>
        entry.Demand is CompositeScopeDemand other
        && other.CoordinateKeys.SequenceEqual(CoordinateKeys, StringComparer.Ordinal);
}

/// <summary>A workspace request identified by the registry key its builder publishes.</summary>
[SupportedOSPlatform("browser")]
internal sealed class KeyedScopeDemand(string key) : ScopeDemand
{
    private protected override bool JoinsCore(ScopeEntry entry) =>
        entry.State is BrowserScopeState.Ready
        && string.Equals(entry.Key, key, StringComparison.Ordinal);
}

/// <summary>A reservation nothing may join until its builder publishes a key for it.</summary>
[SupportedOSPlatform("browser")]
internal sealed class ExclusiveScopeDemand : ScopeDemand
{
    internal static ExclusiveScopeDemand Instance { get; } = new();

    private protected override bool JoinsCore(ScopeEntry entry) => false;
}

/// <summary>
/// One protected use of a counted entry, and the archives that use leased. The use is taken
/// before its holder suspends and released exactly once.
/// </summary>
[SupportedOSPlatform("browser")]
internal readonly record struct ScopeUse(
    ScopeEntry Entry,
    ImmutableHashSet<string> LeasedPackages);

/// <summary>
/// The outcome of one admission attempt: a protected use of a counted entry, and whether that
/// entry is a newly reserved one this caller must construct or a retained one it joined.
/// </summary>
[SupportedOSPlatform("browser")]
internal readonly record struct ScopeAdmission(ScopeUse Use, bool IsNew);

/// <summary>
/// One counted entry reserved — with its full image allowance — for a workspace whose exact
/// coordinate set is only known once it has been built. Releasing the reservation hands its
/// protected use to the workspace's caller or, when construction never published, retires it.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class ScopeReservation(ScopeEntry entry) : IAsyncDisposable
{
    bool _released;

    internal ScopeEntry Entry { get; } = entry;

    internal void Release() => _released = true;

    public ValueTask DisposeAsync() =>
        _released
            ? ValueTask.CompletedTask
            : BrowserPackageWorkspace.AbandonReservationAsync(this);
}

/// <summary>One resolved workspace and the coordinates its caller asked for.</summary>
[SupportedOSPlatform("browser")]
internal sealed record BrowserScopeResolution(
    BrowserScopeLease<BrowserInspectionScope> Lease,
    ImmutableArray<BrowserPackageCoordinate> RequestedCoordinates) : IAsyncDisposable
{
    public BrowserInspectionScope Scope => Lease.Scope;

    public ValueTask DisposeAsync() => Lease.DisposeAsync();
}

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
