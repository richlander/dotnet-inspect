using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// CLI host policy for the two endpoints of a pairwise
/// <c>diff --package ID@A..B</c> Library API Diff opened as package endpoint
/// scopes (docs/design/package-endpoint-scope.md, adoption step 2 and step 4
/// part 1): the House's version and framework selection, as <c>package</c>
/// has it, ranged House access over the legacy-compatible per-authority
/// store, and the admission rule that keeps the output identical to the
/// legacy extraction path.
/// </summary>
internal sealed class PackageEndpointDiffSession : IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly SearchPackageStores _stores;
    bool _closed;

    PackageEndpointDiffSession(
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores,
        PackageEndpointScope from,
        PackageEndpointScope to)
    {
        _composition = composition;
        _stores = stores;
        From = from;
        To = to;
    }

    internal PackageEndpointScope From { get; }

    internal PackageEndpointScope To { get; }

    /// <summary>
    /// The House demands one pairwise package diff request declares, or
    /// <see langword="null"/> when it keeps the legacy path: each endpoint
    /// is an exact version, <c>latest</c>, or a <c>*</c> pattern, as
    /// <c>package</c> reads it, and <c>--tfm</c> names one framework or is
    /// absent, for the House's owner-default compile selection. Offline, an
    /// endpoint the House can settle only by discovery keeps the whole
    /// request on the legacy path: it answers a <c>*</c> pattern from cached
    /// version listings, and reports why <c>latest</c> can't be resolved.
    /// </summary>
    internal static PackageEndpointDiffSelection? TryCreateSelection(
        string packageId,
        string fromVersion,
        string toVersion,
        string? targetFramework)
    {
        if (string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase)
            || !DotnetInspector.Packages.PackageExtractor.IsValidPackageId(packageId)
            || !PackageHouseDemand.TryCreate(
                packageId, fromVersion, includePrerelease: false, out PackageHouseDemand? from, out _)
            || !PackageHouseDemand.TryCreate(
                packageId, toVersion, includePrerelease: false, out PackageHouseDemand? to, out _))
        {
            return null;
        }

        if (DotnetInspector.Networking.HttpClientFactory.IsOffline
            && (RequiresDiscovery(from) || RequiresDiscovery(to)))
        {
            return null;
        }

        PackageHouseTargetContext target;
        try
        {
            target = string.IsNullOrWhiteSpace(targetFramework)
                ? PackageHouseTargetContext.OwnerDefault()
                : PackageHouseTargetContext.Exact(targetFramework);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return new PackageEndpointDiffSelection(
            packageId, fromVersion, from, toVersion, to, target);

        static bool RequiresDiscovery(PackageHouseDemand demand) =>
            demand is PackageHouseDemand.Selecting
            {
                Request: PackageVersionSelectionRequest.Wildcard
                    or PackageVersionSelectionRequest.AlwaysLatest,
            };
    }

    /// <summary>
    /// Opens both endpoints with the <c>Surface</c> demand by range, or
    /// returns <see langword="null"/> when either endpoint must take the
    /// legacy path: its demand did not settle, the legacy selector would
    /// pick other assemblies from its archive than the scope's surface
    /// participants, its surface is not one Library, or it did not open.
    /// </summary>
    internal static async Task<PackageEndpointDiffSession?> TryOpenAsync(
        HttpClient httpClient,
        PackageEndpointDiffSelection selection,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        string packageId = selection.PackageId;
        PackageHouseTargetContext target = selection.Target;
        var composition = new DesktopPackageSourceComposition(httpClient.Timeout);
        var stores = new SearchPackageStores("inspect-diff");
        PackageEndpointScope? from = null;
        PackageEndpointScope? to = null;
        bool closed = false;
        try
        {
            PackageHouse house = composition.CreateRealizationHouse(
                new PackagePayloadAcquisitionPlan(
                    stores.GetLegacyCompatibleStore,
                    PackagePayloadLimits.Default,
                    log: logger.Log,
                    access: PackagePayloadAccess.Ranged),
                sourceOptions,
                logger.Log);
            // The admission check reads by range whatever the archive's size:
            // a zero size cut, so a fallback never downloads an archive the
            // legacy path then downloads again. Realization keeps size first.
            // This transitional exception is registered in the package cache
            // policy's Size first contract; endpoint-scope step 4 retires it.
            PackageHouse checkHouse = composition.CreateRealizationHouse(
                new PackagePayloadAcquisitionPlan(
                    stores.GetLegacyCompatibleStore,
                    PackagePayloadLimits.Default,
                    log: logger.Log,
                    access: PackagePayloadAccess.Ranged,
                    rangedSizeCut: 0),
                sourceOptions,
                logger.Log);
            // Admission first: both directory checks run to completion at once,
            // neither cancelling the other, so each archive's directory and
            // root folder reach the entry cache and a repeated request makes
            // no package request, whichever endpoint falls back.
            Task<PackageSourceCoordinate?> fromCheck = LegacySelectionMatchesAsync(
                composition, checkHouse, packageId, selection.FromVersion, selection.From, target, logger, cancellationToken);
            Task<PackageSourceCoordinate?> toCheck = LegacySelectionMatchesAsync(
                composition, checkHouse, packageId, selection.ToVersion, selection.To, target, logger, cancellationToken);
            try
            {
                await Task.WhenAll(fromCheck, toCheck).ConfigureAwait(false);
            }
            catch
            {
                // Each check's outcome is inspected below.
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new[] { fromCheck, toCheck }
                    .Where(static task => task.IsFaulted)
                    .Select(static task => task.Exception!.InnerException!)
                    .FirstOrDefault() is { } checkFailure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(checkFailure);
            }
            if (fromCheck.Result is not { } fromCoordinate
                || toCheck.Result is not { } toCoordinate)
            {
                closed = true;
                await CloseAsync(null, null, composition, stores).ConfigureAwait(false);
                return null;
            }

            // Then both surfaces realize at once. A realization that fails
            // cancels its sibling, whose work the legacy path would discard.
            using var sibling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Each endpoint realizes the exact version its check settled, so
            // a floating demand is settled once.
            Task<PackageEndpointScope?> fromTask = OpenOrCancelSiblingAsync(fromCoordinate);
            Task<PackageEndpointScope?> toTask = OpenOrCancelSiblingAsync(toCoordinate);
            try
            {
                await Task.WhenAll(fromTask, toTask).ConfigureAwait(false);
            }
            catch
            {
                // Each task's outcome is inspected below.
            }
            from = fromTask.IsCompletedSuccessfully ? fromTask.Result : null;
            to = toTask.IsCompletedSuccessfully ? toTask.Result : null;
            if (from is null || to is null)
            {
                closed = true;
                await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Exception? failure = new[] { fromTask, toTask }
                    .Where(static task => task.IsFaulted)
                    .Select(static task => task.Exception!.InnerException!)
                    .FirstOrDefault(static exception => exception is not OperationCanceledException);
                if (failure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
                return null;
            }

            async Task<PackageEndpointScope?> OpenOrCancelSiblingAsync(
                PackageSourceCoordinate coordinate)
            {
                try
                {
                    PackageEndpointScope? scope = await OpenAsync(
                            composition, house, packageId, coordinate, target, logger, sibling.Token)
                        .ConfigureAwait(false);
                    if (scope is null)
                        await sibling.CancelAsync().ConfigureAwait(false);
                    return scope;
                }
                catch
                {
                    await sibling.CancelAsync().ConfigureAwait(false);
                    throw;
                }
            }

            return new PackageEndpointDiffSession(composition, stores, from, to);
        }
        catch
        {
            if (!closed)
                await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
            throw;
        }
    }

    static async Task<PackageEndpointScope?> OpenAsync(
        DesktopPackageSourceComposition composition,
        PackageHouse house,
        string packageId,
        PackageSourceCoordinate coordinate,
        PackageHouseTargetContext target,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        string version = coordinate.Version;
        var request = new PackageEndpointScopeRequest(
            new PackageHouseDemand.Exact(coordinate),
            target,
            PackageAssetDemand.Surface);

        long realizing = System.Diagnostics.Stopwatch.GetTimestamp();
        PackageEndpointScopeOutcome outcome =
            await PackageEndpointScope.OpenAsync(
                    house,
                    composition.IssueSettlementOperation(cancellationToken),
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        LogTransfer(
            logger,
            packageId,
            version,
            "surface realization",
            outcome switch
            {
                PackageEndpointScopeOutcome.Opened opened => opened.Scope.Contribution.Result,
                PackageEndpointScopeOutcome.Failed rejected => rejected.HouseResult,
                _ => null,
            },
            realizing);
        if (outcome is PackageEndpointScopeOutcome.Failed failed)
        {
            // The legacy path owns the user-visible report of every endpoint
            // failure, so its diagnostics stay unchanged.
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path "
                    + $"({failed.Kind}): {failed.Message}");
            return null;
        }

        // Admission already matched the legacy selection and one Library
        // over this archive's directory.
        PackageEndpointScope scope = ((PackageEndpointScopeOutcome.Opened)outcome).Scope;
        logger.Log(
            $"Using package endpoint scope for {packageId}@{version} "
                + $"({scope.SurfaceParticipants.Length} surface assembly for {scope.TargetFramework}; "
                + $"payload {scope.Payload.Origin}).");
        return scope;
    }

    /// <summary>
    /// Settles one endpoint's demand and decides, from the archive directory
    /// alone, whether the legacy selector picks exactly the compile surface
    /// the endpoint scope would realize, before any surface-folder byte is
    /// read. Returns the settled coordinate, or <see langword="null"/> when
    /// the endpoint takes the legacy path.
    /// </summary>
    /// <remarks>
    /// Transitional (docs/design/package-endpoint-scope.md, adoption step 2):
    /// the House reads a directory by range only for a Realize or a file
    /// demand, so this reads the directory with a file demand for the
    /// nuspec, which also reads the archive's root folder. It retires with
    /// the legacy selector in step 4. The Realize that follows an admitted
    /// check reads the cached directory's tail again.
    /// </remarks>
    static async Task<PackageSourceCoordinate?> LegacySelectionMatchesAsync(
        DesktopPackageSourceComposition composition,
        PackageHouse house,
        string packageId,
        string version,
        PackageHouseDemand demand,
        PackageHouseTargetContext target,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        PackageSourceOperationLease operation =
            composition.IssueSettlementOperation(cancellationToken);
        PackageHouseRequest request;
        try
        {
            request = new PackageHouseRequest(
                demand,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Acquire,
                    operation.RequestTimeout,
                    operation.OperationTimeout),
                fileDemand: PackageFileDemand.Create([$"{packageId}.nuspec"]));
        }
        catch
        {
            operation.Dispose();
            throw;
        }

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(request, operation).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        LogTransfer(logger, packageId, version, "directory check", settlement.Result, started);
        if (settlement is not PackageHouseSettlement.Acquired
            {
                Result: PackageHouseResult.Settled,
            } acquired)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: its archive "
                    + "directory was not read.");
            return null;
        }

        PackageSourceCoordinate settled = acquired.Payload.Coordinate;
        if (demand is PackageHouseDemand.Selecting)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} settled to {settled.Version}.");
            version = settled.Version;
        }

        IPackageContent content = acquired.Payload.Content;
        string? targetFramework = target.RequestedFramework;
        string[] legacy =
        [
            .. targetFramework is null
                ? TfmSelector.SelectHighestAssembliesFromEntries(content.EnumerateEntries())
                : TfmSelector.SelectAssembliesByTfmFromEntries(
                    content.EnumerateEntries(),
                    targetFramework),
        ];
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Evaluate(
                content,
                packageId,
                targetFramework is null
                    ? PackageCompileAssetSelectionPolicy.HighestAvailable
                    : PackageCompileAssetSelectionPolicy.ExplicitTarget,
                targetFramework).Selection;
        string[] surface =
        [
            .. selection.Assets
                .Select(static asset => asset.Path)
                .Order(StringComparer.Ordinal),
        ];
        if (legacy.SequenceEqual(surface, StringComparer.Ordinal))
        {
            if (surface.Length == 1)
                return settled;
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: its surface "
                    + $"has {surface.Length} Libraries, and the endpoint scope serves "
                    + "single-Library API Diff only.");
            return null;
        }

        logger.Log(
            $"Package endpoint {packageId}@{version} takes the legacy path: the legacy "
                + $"selector picks [{string.Join(", ", legacy)}], the endpoint scope's "
                + $"surface is [{string.Join(", ", surface)}].");
        return null;
    }

    /// <summary>
    /// Logs one House read's transfer receipt and elapsed time in verbose
    /// mode: the request count by purpose and the bytes received.
    /// </summary>
    static void LogTransfer(
        VerboseLogger logger,
        string packageId,
        string version,
        string phase,
        PackageHouseResult? result,
        long started)
    {
        if (!logger.Enabled)
            return;
        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        PackageTransferReceipt? transfer = result?.Evidence.Acquisition?.Transfer;
        string requests = transfer is null
            ? "no receipt"
            : $"{transfer.Path}; {transfer.RequestCount} requests"
                + (transfer.Requests.Count == 0
                    ? ""
                    : " ("
                        + string.Join(
                            ", ",
                            transfer.Requests
                                .GroupBy(static request => request.Purpose)
                                .Select(static group =>
                                    $"{group.Key} x{group.Count()} {group.Sum(static request => request.BytesReceived)} B"))
                        + ")")
                + $", {transfer.BytesReceived} bytes received";
        logger.Log(
            $"Package endpoint {packageId}@{version} {phase}: {requests}, "
                + $"{elapsed.TotalMilliseconds:F0} ms.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;
        await CloseAsync(From, To, _composition, _stores).ConfigureAwait(false);
    }

    static async Task CloseAsync(
        PackageEndpointScope? from,
        PackageEndpointScope? to,
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores)
    {
        try
        {
            try
            {
                if (from is not null)
                    await from.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (to is not null)
                    await to.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Temporary authority content stays readable until both
            // Workspaces that admitted it have closed.
            stores.Dispose();
            await composition.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The House demands of one pairwise package diff: one package demand per
/// endpoint and one target context that applies to both.
/// </summary>
internal sealed record PackageEndpointDiffSelection(
    string PackageId,
    string FromVersion,
    PackageHouseDemand From,
    string ToVersion,
    PackageHouseDemand To,
    PackageHouseTargetContext Target)
{
    /// <summary>Whether both endpoints are exact coordinates.</summary>
    public bool IsExact =>
        From is PackageHouseDemand.Exact && To is PackageHouseDemand.Exact;
}
