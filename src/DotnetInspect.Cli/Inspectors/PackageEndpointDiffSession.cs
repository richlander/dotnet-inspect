using System.Collections.Immutable;

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
/// 1 to 5): the House's version and framework selection, as
/// <c>package</c> has it, and ranged House access over the
/// legacy-compatible per-authority store.
/// </summary>
internal sealed class PackageEndpointDiffSession : IAsyncDisposable
{
    readonly DesktopPackageSourceComposition _composition;
    readonly SearchPackageStores _stores;
    readonly NuGetSourceOptions? _sourceOptions;
    readonly Lock _forwardedTargetHouseLock = new();
    PackageHouse? _forwardedTargetHouse;
    bool _closed;

    PackageEndpointDiffSession(
        DesktopPackageSourceComposition composition,
        SearchPackageStores stores,
        NuGetSourceOptions? sourceOptions,
        PackageEndpointScope from,
        PackageEndpointScope to)
    {
        _composition = composition;
        _stores = stores;
        _sourceOptions = sourceOptions;
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
    /// The House demands one pairwise package diff request declares: each
    /// endpoint is an exact version, <c>latest</c>, or a <c>*</c> pattern,
    /// as <c>package</c> reads it, and <c>--tfm</c> names one framework or is
    /// absent, for the House's owner-default compile selection. A request the
    /// House can't express returns <see langword="null"/> with its
    /// <paramref name="error"/>. Offline, an endpoint the House can settle
    /// only by discovery returns <see langword="null"/> with no error, and
    /// the whole request keeps the legacy path: it answers a <c>*</c>
    /// pattern from cached version listings, and reports why <c>latest</c>
    /// can't be resolved (docs/design/package-endpoint-scope.md#offline).
    /// </summary>
    internal static PackageEndpointDiffSelection? TryCreateSelection(
        string packageId,
        string fromVersion,
        string toVersion,
        string? targetFramework,
        out string? error)
    {
        error = null;
        if (string.Equals(targetFramework, "all", StringComparison.OrdinalIgnoreCase))
        {
            error = "--tfm all applies only to 'package' discovery.";
            return null;
        }
        if (!DotnetInspector.Packages.PackageExtractor.IsValidPackageId(packageId))
        {
            error = $"Invalid package name: '{packageId}'";
            return null;
        }
        if (!PackageHouseDemand.TryCreate(
                packageId, fromVersion, includePrerelease: false, out PackageHouseDemand? from, out _))
        {
            error = InvalidVersion(fromVersion);
            return null;
        }
        if (!PackageHouseDemand.TryCreate(
                packageId, toVersion, includePrerelease: false, out PackageHouseDemand? to, out _))
        {
            error = InvalidVersion(toVersion);
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
            error = $"Invalid target framework '{targetFramework}'.";
            return null;
        }

        return new PackageEndpointDiffSelection(
            packageId, fromVersion, from, toVersion, to, target);

        static string InvalidVersion(string version) =>
            $"Error resolving v{version}: '{version}' is not a NuGet version, latest, or a * pattern.";

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
    /// When an endpoint does not open, returns no session and the report of
    /// that endpoint's typed failure: the before endpoint's when it failed on
    /// its own, otherwise the after endpoint's.
    /// </summary>
    internal static async Task<(PackageEndpointDiffSession? Session, string? Error)> OpenAsync(
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
            Task<PackageEndpointScopeOutcome> fromTask =
                OpenOrCancelSiblingAsync(selection.FromVersion, selection.From);
            Task<PackageEndpointScopeOutcome> toTask =
                OpenOrCancelSiblingAsync(selection.ToVersion, selection.To);
            try
            {
                await Task.WhenAll(fromTask, toTask).ConfigureAwait(false);
            }
            catch
            {
                // Each task's outcome is inspected below.
            }
            from = Opened(fromTask);
            to = Opened(toTask);
            if (from is null || to is null)
            {
                closed = true;
                await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Exception? exception = new[] { fromTask, toTask }
                    .Where(static task => task.IsFaulted)
                    .Select(static task => task.Exception!.InnerException!)
                    .FirstOrDefault(static exception => exception is not OperationCanceledException);
                if (exception is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
                // A failed endpoint cancels its sibling, so only a failure
                // that completed is reported: the before endpoint's when it
                // failed on its own, otherwise the after endpoint's.
                (string version, PackageHouseDemand demand, PackageEndpointScopeOutcome.Failed? failed) =
                    Failed(fromTask) is { } fromFailure
                        ? (selection.FromVersion, selection.From, fromFailure)
                        : (selection.ToVersion, selection.To, Failed(toTask));
                if (failed is null)
                {
                    return (null,
                        $"Error resolving {packageId}@{selection.FromVersion}..{selection.ToVersion}: "
                            + "neither package endpoint completed.");
                }
                return (null, DescribeFailure(packageId, version, demand, target, failed));
            }

            async Task<PackageEndpointScopeOutcome> OpenOrCancelSiblingAsync(
                string version,
                PackageHouseDemand demand)
            {
                try
                {
                    PackageEndpointScopeOutcome outcome = await OpenEndpointAsync(
                            composition, house, packageId, version, demand, target, assetDemand, logger, sibling.Token)
                        .ConfigureAwait(false);
                    if (outcome is not PackageEndpointScopeOutcome.Opened)
                        await sibling.CancelAsync().ConfigureAwait(false);
                    return outcome;
                }
                catch
                {
                    await sibling.CancelAsync().ConfigureAwait(false);
                    throw;
                }
            }

            return (new PackageEndpointDiffSession(composition, stores, sourceOptions, from, to), null);
        }
        catch
        {
            if (!closed)
                await CloseAsync(from, to, composition, stores).ConfigureAwait(false);
            throw;
        }
    }

    static PackageEndpointScope? Opened(Task<PackageEndpointScopeOutcome> task) =>
        task.IsCompletedSuccessfully
            && task.Result is PackageEndpointScopeOutcome.Opened opened
                ? opened.Scope
                : null;

    static PackageEndpointScopeOutcome.Failed? Failed(Task<PackageEndpointScopeOutcome> task) =>
        task.IsCompletedSuccessfully
            ? task.Result as PackageEndpointScopeOutcome.Failed
            : null;

    /// <summary>
    /// The user-visible report of one endpoint that did not open, from its
    /// typed failure and the House's evidence: each source failure and stage
    /// reason follows the summary on its own line.
    /// </summary>
    static string DescribeFailure(
        string packageId,
        string version,
        PackageHouseDemand demand,
        PackageHouseTargetContext target,
        PackageEndpointScopeOutcome.Failed failed)
    {
        string unacquired = demand is PackageHouseDemand.Exact
            ? $"Package '{packageId}' version '{version}' could not be acquired."
            : $"Package '{packageId}' selection '{version}' could not be acquired.";
        string summary = failed.Kind switch
        {
            PackageEndpointScopeFailureKind.NotFound => demand is PackageHouseDemand.Exact
                ? unacquired
                : $"Package '{packageId}' selection '{version}' matched no available version.",
            // The House settled no payload: a source failed or refused it.
            PackageEndpointScopeFailureKind.RealizationFailed
                when failed.HouseResult is not PackageHouseResult.Settled => unacquired,
            // A package whose assemblies are only outside lib/ and ref/
            // (tools/, build/, analyzers/) has no compile assets, and isn't
            // compared until the House selects those layouts.
            PackageEndpointScopeFailureKind.NoCompatibleFramework
                when failed.Selection?.Status == PackageCompileAssetSelectionStatus.NoCompileAssets =>
                $"Package '{packageId}@{version}' has no compile assets; "
                    + "assemblies outside lib/ and ref/, such as tools/, build/, or analyzers/, aren't compared.",
            // An empty group (_._) for the framework the owner default
            // selects means that framework provides the Library.
            PackageEndpointScopeFailureKind.NoCompatibleFramework
                when target.RequestedFramework is null
                    && failed.Selection is
                    {
                        Status: PackageCompileAssetSelectionStatus.EmptyCompileGroup,
                        TargetFramework: { } selected,
                    } empty =>
                $"Package '{packageId}@{version}' has an empty compile group for {selected}, "
                    + "the framework it selects by default, which provides the Library itself. "
                    + "Use --tfm to compare it for "
                    + (empty.AvailableTargetFrameworks.Where(framework =>
                            !empty.ExplicitEmptyTargetFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
                        .ToArray() is { Length: > 0 } frameworks
                            ? "one of: " + string.Join(", ", frameworks) + "."
                            : "another framework."),
            PackageEndpointScopeFailureKind.NoCompatibleFramework => target.RequestedFramework is { } framework
                ? $"No assemblies found for target framework '{framework}' in package '{packageId}@{version}'."
                : $"Package '{packageId}@{version}' has no compile assets for its selected framework "
                    + $"({failed.Selection?.Status}).",
            _ => failed.Message,
        };
        var lines = new List<string> { summary };
        ImmutableArray<PackageHouseFailure> failures =
            failed.HouseResult?.Evidence.Failures ?? ImmutableArray<PackageHouseFailure>.Empty;
        foreach (PackageHouseFailure failure in failures)
        {
            string? detail = failure switch
            {
                PackageHouseFailure.Authority authority =>
                    authority.Failure.Authority.ToString() is { Length: > 0 } name
                        ? $"{name}: {authority.Failure.Message}"
                        : authority.Failure.Message,
                PackageHouseFailure.Timeout timeout =>
                    $"{timeout.Kind} timed out after {timeout.Duration.TotalSeconds:0.#} s.",
                PackageHouseFailure.Stage stage => stage.Reason.ToString(),
                _ => null,
            };
            if (detail is not null && !lines.Contains(detail))
                lines.Add(detail);
        }
        // A source that didn't answer says nothing about the package, as the
        // legacy path's feed-failure report put it.
        if (failures.Any(static failure => failure is PackageHouseFailure.Authority
            {
                Failure.Kind: PackageAuthorityFailureKind.AuthenticationRequired,
            }))
        {
            lines.Add("The package may exist; the source was not readable. Supply credentials for this source and retry.");
        }
        else if (failures.Any(static failure => failure is PackageHouseFailure.Timeout
            or PackageHouseFailure.Authority
            {
                Failure.Kind: PackageAuthorityFailureKind.Transport
                    or PackageAuthorityFailureKind.Timeout
                    or PackageAuthorityFailureKind.InvalidResponse
                    or PackageAuthorityFailureKind.ResponseRejected,
            }))
        {
            lines.Add("The package may exist; the source was not readable. Retry, or check the source URL.");
        }
        else if (lines.Count == 1 && failed.Kind == PackageEndpointScopeFailureKind.NotFound)
        {
            lines.Add(demand is PackageHouseDemand.Exact
                ? "No eligible source supplied this exact coordinate. Use --versions to see available versions."
                : "No eligible source listed a matching version. Use --versions to see available versions.");
        }
        return $"Error resolving v{version}: {string.Join(Environment.NewLine, lines)}";
    }

    static async Task<PackageEndpointScopeOutcome> OpenEndpointAsync(
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
            logger.Log(
                $"Package endpoint {packageId}@{version} did not open "
                    + $"({failed.Kind}): {failed.Message}");
            return failed;
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
        return outcome;
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

    /// <summary>
    /// Opens the package <paramref name="endpoint"/>'s Root declares for the
    /// assembly <paramref name="assemblyName"/>, to which the Root forwards a
    /// Type (docs/design/package-endpoint-scope.md, step 4 part 5). The
    /// Root's manifest comes from the endpoint's payload, or else as one
    /// exact File from the producer that served the endpoint. The declared package opens through a content House over the
    /// endpoints' composition and stores, which chooses ranged or complete
    /// access, narrowed to the one Library named for the assembly, with
    /// <c>SurfaceAndImplementation</c>. Returns <see langword="null"/>,
    /// and logs why, when the package declares no such dependency or it does
    /// not open.
    /// </summary>
    internal async Task<PackageEndpointScope?> OpenForwardedTargetAsync(
        PackageEndpointScope endpoint,
        string targetFramework,
        string assemblyName,
        VerboseLogger logger,
        CancellationToken cancellationToken)
    {
        string root = $"{endpoint.Root.Root.PackageId}@{endpoint.PackageVersion}";
        byte[]? nuspec = await ReadEndpointManifestAsync(endpoint, cancellationToken)
            .ConfigureAwait(false);
        if (nuspec is null)
        {
            PackageHouseSettlement manifest = await _composition.AcquireContentAsync(
                    PackageSourceCoordinate.Create(endpoint.Root.Root.PackageId, endpoint.PackageVersion),
                    PackageHouseContentQuery.PackageNuspec(),
                    _stores.GetLegacyCompatibleStore,
                    _sourceOptions,
                    logger.Log,
                    cancellationToken,
                    requiredProducerKey: endpoint.Payload.ProducerKey)
                .ConfigureAwait(false);
            if (manifest.Result is not PackageHouseResult.Settled { Evidence.Nuspec: { } settled })
            {
                logger.Log(
                    $"The forwarded target {assemblyName} of {root} is unavailable: "
                        + $"its manifest was not acquired ({manifest.Result.GetType().Name}).");
                return null;
            }
            nuspec = settled.ToArray();
        }

        if (PackageDeclaredForwardTarget.Select(
                nuspec,
                endpoint.Root.Root.PackageId,
                endpoint.PackageVersion,
                targetFramework,
                assemblyName,
                out string? failure)
            is not { } coordinate)
        {
            logger.Log($"The forwarded target {assemblyName} of {root} is unavailable: {failure}.");
            return null;
        }

        string version = coordinate.Version;
        long realizing = System.Diagnostics.Stopwatch.GetTimestamp();
        PackageEndpointScopeOutcome outcome =
            await PackageEndpointScope.OpenAsync(
                    ForwardedTargetHouse(logger),
                    _composition.IssueSettlementOperation(cancellationToken),
                    new PackageEndpointScopeRequest(
                        new PackageHouseDemand.Exact(coordinate),
                        PackageHouseTargetContext.Exact(targetFramework),
                        PackageAssetDemand.SurfaceAndImplementation,
                        librarySelector: new PackageLibrarySelector(assemblyName)),
                    cancellationToken)
                .ConfigureAwait(false);
        LogTransfer(
            logger,
            assemblyName,
            version,
            "forwarded target realization",
            outcome switch
            {
                PackageEndpointScopeOutcome.Opened opened => opened.Scope.Contribution.Result,
                PackageEndpointScopeOutcome.Failed rejected => rejected.HouseResult,
                _ => null,
            },
            realizing);
        if (outcome is not PackageEndpointScopeOutcome.Opened { Scope: { } scope })
        {
            var failed = (PackageEndpointScopeOutcome.Failed)outcome;
            logger.Log(
                $"The forwarded target {assemblyName} of {root} is unavailable: "
                    + $"{assemblyName}@{version} did not open ({failed.Kind}): {failed.Message}");
            return null;
        }

        logger.Log(
            $"Using package endpoint scope for forwarded target {assemblyName}@{version} "
                + $"({scope.ImplementationParticipants.Length} implementation "
                + (scope.ImplementationParticipants.Length == 1 ? "assembly" : "assemblies")
                + $" for {scope.TargetFramework}; payload {scope.Payload.Origin}).");
        return scope;
    }

    /// <summary>
    /// The Root's manifest as the endpoint's own payload holds it, or
    /// <see langword="null"/> when that payload did not materialize it, as a
    /// ranged read of only the asset folders does not.
    /// </summary>
    static async Task<byte[]?> ReadEndpointManifestAsync(
        PackageEndpointScope endpoint,
        CancellationToken cancellationToken)
    {
        IPackageContent content = endpoint.Payload.Content;
        try
        {
            if (PackageManifestContent.FindRootManifest(content) is not { } path
                || !content.TryOpenEntry(
                    path,
                    PackageManifestFactsProjection.MaxManifestBytes,
                    out Stream? stream))
            {
                return null;
            }

            await using (stream.ConfigureAwait(false))
            {
                return await BoundedContentReader.ReadAllBytesAsync(
                        stream,
                        PackageManifestFactsProjection.MaxManifestBytes,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is PackageEntryNotMaterializedException
                or InvalidDataException
                or IOException
                or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The content House both forwarded targets open through, which chooses
    /// cache, ranged, or complete access for each Library demand.
    /// </summary>
    PackageHouse ForwardedTargetHouse(VerboseLogger logger)
    {
        lock (_forwardedTargetHouseLock)
        {
            return _forwardedTargetHouse ??= _composition.CreateDependencyContentQueryHouse(
                _stores.GetLegacyCompatibleStore,
                _sourceOptions,
                logger.Log);
        }
    }

    /// <summary>
    /// Settles the Portable PDB of an endpoint participant. Its package-local
    /// candidate is an exact Files request to a content House over the
    /// endpoint's composition and stores, restricted to the producer that
    /// served it (docs/design/package-endpoint-scope.md, step 4 part 4).
    /// </summary>
    internal IPortablePdbSettlementCapability CreatePortablePdbSettlement(
        HttpClient httpClient,
        VerboseLogger logger) =>
        new PackageEndpointPdbSettlement(
            _composition,
            _stores,
            _sourceOptions,
            [From.Payload, To.Payload],
            httpClient,
            logger);

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
