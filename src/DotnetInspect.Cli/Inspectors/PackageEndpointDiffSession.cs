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
/// scopes (docs/design/package-endpoint-scope.md, adoption step 2): ranged
/// House access, the per-authority durable store, and the admission rule that
/// keeps the output identical to the legacy extraction path.
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
    /// Whether one pairwise package diff request may take the scope route:
    /// both endpoints exact, one explicit target framework, online.
    /// </summary>
    internal static bool IsEligible(
        string packageId,
        string fromVersion,
        string toVersion,
        string? targetFramework) =>
        !DotnetInspector.Networking.HttpClientFactory.IsOffline
        && !string.IsNullOrWhiteSpace(targetFramework)
        && !string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase)
        && DotnetInspector.Packages.PackageExtractor.IsValidPackageId(packageId)
        && IsExactVersion(fromVersion)
        && IsExactVersion(toVersion);

    static bool IsExactVersion(string version) =>
        !version.Contains('*')
        && !version.Equals("latest", StringComparison.OrdinalIgnoreCase)
        && NuGetVersion.TryParse(version, out _);

    /// <summary>
    /// Opens both endpoints with the <c>Surface</c> demand by range, or
    /// returns <see langword="null"/> when either endpoint must take the
    /// legacy path: it did not open, the legacy selector would pick other
    /// assemblies from its archive than the scope's surface participants, or
    /// its surface is not one Library.
    /// </summary>
    internal static async Task<PackageEndpointDiffSession?> TryOpenAsync(
        HttpClient httpClient,
        string packageId,
        string fromVersion,
        string toVersion,
        string targetFramework,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        CancellationToken cancellationToken = default)
    {
        var composition = new DesktopPackageSourceComposition(httpClient.Timeout);
        var stores = new SearchPackageStores("inspect-diff");
        PackageEndpointScope? from = null;
        PackageEndpointScope? to = null;
        bool closed = false;
        try
        {
            PackageHouse house = composition.CreateRealizationHouse(
                new PackagePayloadAcquisitionPlan(
                    stores.GetStore,
                    PackagePayloadLimits.Default,
                    log: logger.Log,
                    access: PackagePayloadAccess.Ranged),
                sourceOptions,
                logger.Log);
            // Both endpoints open at once. The first that falls back or fails
            // cancels its sibling, whose work the legacy path would discard.
            using var sibling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<PackageEndpointScope?> fromTask = OpenOrCancelSiblingAsync(fromVersion);
            Task<PackageEndpointScope?> toTask = OpenOrCancelSiblingAsync(toVersion);
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

            async Task<PackageEndpointScope?> OpenOrCancelSiblingAsync(string version)
            {
                try
                {
                    PackageEndpointScope? scope = await OpenAsync(
                            composition, house, packageId, version, targetFramework, logger, sibling.Token)
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
        string targetFramework,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        PackageEndpointScopeRequest request;
        try
        {
            request = new PackageEndpointScopeRequest(
                PackageSourceCoordinate.Create(packageId, version),
                targetFramework,
                PackageAssetDemand.Surface);
            _ = PackageHouseTargetContext.Exact(targetFramework);
        }
        catch (ArgumentException exception)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: {exception.Message}");
            return null;
        }

        if (!await LegacySelectionMatchesAsync(
                composition, house, packageId, version, targetFramework, logger, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

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

        PackageEndpointScope scope = ((PackageEndpointScopeOutcome.Opened)outcome).Scope;
        string[] legacy =
        [
            .. scope.Root.Root.UseContent(content =>
                TfmSelector.SelectAssembliesByTfmFromEntries(
                    content.EnumerateEntries(),
                    targetFramework)),
        ];
        string[] selected =
        [
            .. scope.SurfaceParticipants
                .Select(static participant => participant.Asset.Path)
                .Order(StringComparer.Ordinal),
        ];
        if (!legacy.SequenceEqual(selected, StringComparer.Ordinal)
            || selected.Length != 1)
        {
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: the legacy "
                    + $"selector picks [{string.Join(", ", legacy)}], the endpoint scope's "
                    + $"surface is [{string.Join(", ", selected)}].");
            await scope.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        logger.Log(
            $"Using package endpoint scope for {packageId}@{version} "
                + $"({selected.Length} surface assemblies; payload {scope.Payload.Origin}).");
        return scope;
    }

    /// <summary>
    /// Decides, from the archive directory alone, whether the legacy
    /// selector picks exactly the compile surface the endpoint scope would
    /// realize, before any surface-folder byte is read.
    /// </summary>
    /// <remarks>
    /// Transitional (docs/design/package-endpoint-scope.md, adoption step 2):
    /// the House reads a directory by range only for a Realize or a document
    /// demand, so this reads the directory with a document demand for the
    /// nuspec, which also reads the archive's root folder. It retires with
    /// the legacy selector in step 4. The Realize that follows an admitted
    /// check reads the cached directory's tail again.
    /// </remarks>
    static async Task<bool> LegacySelectionMatchesAsync(
        DesktopPackageSourceComposition composition,
        PackageHouse house,
        string packageId,
        string version,
        string targetFramework,
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
                new PackageHouseDemand.Exact(
                    PackageSourceCoordinate.Create(packageId, version)),
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Acquire,
                    operation.RequestTimeout,
                    operation.OperationTimeout),
                documentDemand: PackageDocumentDemand.Create([$"{packageId}.nuspec"]));
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
            return false;
        }

        IPackageContent content = acquired.Payload.Content;
        string[] legacy =
        [
            .. TfmSelector.SelectAssembliesByTfmFromEntries(
                content.EnumerateEntries(),
                targetFramework),
        ];
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Evaluate(
                content,
                packageId,
                PackageCompileAssetSelectionPolicy.ExplicitTarget,
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
                return true;
            logger.Log(
                $"Package endpoint {packageId}@{version} takes the legacy path: its surface "
                    + $"has {surface.Length} Libraries, and the endpoint scope serves "
                    + "single-Library API Diff only.");
            return false;
        }

        logger.Log(
            $"Package endpoint {packageId}@{version} takes the legacy path: the legacy "
                + $"selector picks [{string.Join(", ", legacy)}], the endpoint scope's "
                + $"surface is [{string.Join(", ", surface)}].");
        return false;
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
