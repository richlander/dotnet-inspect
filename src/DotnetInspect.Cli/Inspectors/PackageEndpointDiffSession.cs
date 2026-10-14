using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// CLI host policy for the two endpoints of a pairwise
/// <c>diff --package ID@A..B</c> view opened as package endpoint scopes
/// (docs/design/package-endpoint-scope.md, adoption step 2 and step 4 parts
/// 1 to 3): the House's version and framework selection, as
/// <c>package</c> has it, and ranged House access over the
/// legacy-compatible per-authority store.
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

#if DEBUG
    /// <summary>
    /// The House acquisitions that opened the two endpoints, before then
    /// after (docs/design/package-transfer-receipt.md#delivery).
    /// </summary>
    internal PackageAcquisitionEvidence AcquisitionEvidence =>
        new(
        [
            PackageAcquisitionEvidenceEntry.From(From.Contribution.Acquisition),
            PackageAcquisitionEvidenceEntry.From(To.Contribution.Acquisition),
        ]);
#endif

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
    /// Opens both endpoints by range with <paramref name="assetDemand"/>:
    /// <c>Surface</c> for the API views, <c>SurfaceAndImplementation</c> for
    /// the body views. Each endpoint settles its own demand in the House.
    /// Returns
    /// <see langword="null"/> when either endpoint did not open, so the
    /// legacy path reports the failure.
    /// </summary>
    internal static async Task<PackageEndpointDiffSession?> TryOpenAsync(
        HttpClient httpClient,
        PackageEndpointDiffSelection selection,
        PackageAssetDemand assetDemand,
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

            // Both surfaces realize at once. A realization that fails
            // cancels its sibling, whose work the legacy path would discard.
            using var sibling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<PackageEndpointScope?> fromTask =
                OpenOrCancelSiblingAsync(selection.FromVersion, selection.From);
            Task<PackageEndpointScope?> toTask =
                OpenOrCancelSiblingAsync(selection.ToVersion, selection.To);
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
                string version,
                PackageHouseDemand demand)
            {
                try
                {
                    PackageEndpointScope? scope = await OpenAsync(
                            composition, house, packageId, version, demand, target, assetDemand, logger, sibling.Token)
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
        string version,
        PackageHouseDemand demand,
        PackageHouseTargetContext target,
        PackageAssetDemand assetDemand,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        var request = new PackageEndpointScopeRequest(
            demand,
            target,
            assetDemand);

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
            assetDemand == PackageAssetDemand.Surface
                ? "surface realization"
                : "surface and implementation realization",
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

        PackageEndpointScope scope = ((PackageEndpointScopeOutcome.Opened)outcome).Scope;
        if (demand is PackageHouseDemand.Selecting)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} settled to {scope.PackageVersion}.");
        }
        logger.Log(
            $"Using package endpoint scope for {packageId}@{version} "
                + $"({scope.SurfaceParticipants.Length} surface "
                + (scope.SurfaceParticipants.Length == 1 ? "assembly" : "assemblies")
                + (assetDemand == PackageAssetDemand.Surface
                    ? ""
                    : $" and {scope.ImplementationParticipants.Length} implementation "
                        + (scope.ImplementationParticipants.Length == 1 ? "assembly" : "assemblies"))
                + $" for {scope.TargetFramework}; payload {scope.Payload.Origin}).");
        return scope;
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
    PackageHouseTargetContext Target);
