using System.Collections.Immutable;
using DotnetInspector.Core;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Resolves one already-validated coordinate against an already-authorized source set.
/// </summary>
/// <remarks>
/// The seam exists so a regression can state what a resolver answered — a prerelease-only
/// floating package, an unavailable listing — without depending on live NuGet state. It carries
/// no grammar of its own: validation and authorization both precede it.
/// </remarks>
internal delegate Task<PackageCoordinateResolution> DependencyEvidenceCoordinateResolver(
    PackageCoordinate coordinate,
    IReadOnlyList<PackageSource> authorizedSources,
    bool includePrerelease,
    CancellationToken cancellationToken);

/// <summary>
/// Asks the package-owned source composition which versions of one package id its configured
/// authorities publish.
/// </summary>
/// <remarks>
/// The seam is the composition call itself, not a second version policy: a regression supplies
/// one <see cref="PackageVersionDiscoveryResult"/> — partial, failed, authoritatively empty —
/// and states what dependency acquisition then did with it, without reaching a live feed. Production
/// binds it to <see cref="DesktopPackageSourceComposition.GetVersionsAsync"/>.
/// </remarks>
internal delegate Task<PackageVersionDiscoveryResult> DependencyEvidenceVersionDiscovery(
    string packageId,
    bool includePrerelease,
    CancellationToken cancellationToken);

/// <summary>
/// One ordered explicit root after acquisition, retaining the request slot and any restored
/// traversal produced from the exact bytes used for evidence.
/// </summary>
internal sealed record DependencyEvidenceAcquiredRoot(
    DependsAssetRoot Root,
    int? InputIndex,
    int? FailureIndex,
    RestoredProjectDependencyTraversalResult? RestoredTraversal);

/// <summary>
/// The evidence request plus occurrence-preserving sidecars needed by unified <c>depends</c>.
/// </summary>
internal sealed record DependencyEvidenceAcquisitionBatch(
    PackageDependencyEvidenceRequest Request,
    ImmutableArray<DependencyEvidenceAcquiredRoot> Roots);

/// <summary>Source and framework choices used while acquiring dependency roots.</summary>
internal sealed record DependencyEvidenceAcquisitionOptions(
    string? Tfm,
    bool IncludePrerelease,
    NuGetSourceOptions? SourceOptions);

/// <summary>
/// Thin acquisition adapters for dependency roots.
/// </summary>
/// <remarks>
/// Every adapter's only job is to turn one explicitly authorized input into bytes or typed facts
/// and hand them to an existing owner: package resolution and the source manifest API, the
/// package-manifest facts query, the dependency-groups query, the restored-project facts query,
/// or the package-profile query. Nothing here parses a nuspec, an assets document, or a
/// dependency range, and nothing here restores, builds, or evaluates MSBuild.
/// </remarks>
internal static class DependencyEvidenceAcquisition
{
    internal const int PackageProfileDefaultLimit = 500;
    internal const int PackageProfileMaximumLimit = 1_000;

    /// <summary>
    /// Acquires ordered package, nuspec, and restored-project roots for unified
    /// <c>depends</c> while retaining occurrence correspondence and exact restored bytes.
    /// </summary>
    internal static async Task<DependencyEvidenceAcquisitionBatch>
        AcquireDependsRootsAsync(
            IReadOnlyList<DependsAssetRoot> requestedRoots,
            DependencyEvidenceAcquisitionOptions options,
            HttpClient httpClient,
            Action<string>? log,
            DesktopPackageSourceComposition composition,
            NuGetOperationContext operationContext,
            bool graphRequested,
            int? maximumDepth,
            CancellationToken cancellationToken,
            IPackageSourceAuthorization? authorization = null,
            DependencyEvidenceCoordinateResolver? resolveCoordinate = null,
            DependencyEvidenceVersionDiscovery? discoverVersions = null)
    {
        ArgumentNullException.ThrowIfNull(requestedRoots);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(operationContext);

        IPackageSourceAuthorization sourceAuthorization = authorization
            ?? new SourcePolicyPackageSourceAuthorization(options.SourceOptions);
        DependencyEvidenceVersionDiscovery discovery = discoverVersions
            ?? ((packageId, includePrerelease, token) =>
                composition.GetVersionsAsync(
                    packageId,
                    includePrerelease,
                    limit: 1,
                    options.SourceOptions,
                    log,
                    token,
                    operationContext: operationContext));
        DependencyEvidenceCoordinateResolver resolver = resolveCoordinate
            ?? ((coordinate, sources, includePrerelease, token) =>
                ResolveCoordinateAsync(
                    httpClient,
                    coordinate,
                    sources,
                    discovery,
                    log,
                    includePrerelease,
                    token));

        var roots =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        var acquired =
            ImmutableArray.CreateBuilder<DependencyEvidenceAcquiredRoot>();

        foreach (DependsAssetRoot requested in requestedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requested.Kind == DependsAssetRootKind.Library)
                continue;

            int inputIndex = roots.Count;
            int failureIndex = failures.Count;
            RestoredProjectDependencyTraversalResult? restoredTraversal = null;
            switch (requested.Kind)
            {
                case DependsAssetRootKind.Package:
                    await AcquirePackageAsync(
                        requested.Value,
                        options,
                        sourceAuthorization,
                        resolver,
                        () => composition,
                        httpClient,
                        roots,
                        failures,
                        cancellationToken,
                        operationContext).ConfigureAwait(false);
                    break;
                case DependsAssetRootKind.Nuspec:
                    await AcquireNuspecAsync(
                        requested.Value,
                        options.Tfm,
                        roots,
                        failures,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case DependsAssetRootKind.Project:
                    restoredTraversal = await AcquireProjectAsync(
                        requested.Value,
                        options.Tfm,
                        roots,
                        failures,
                        cancellationToken,
                        graphRequested,
                        maximumDepth).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown dependency asset root kind.");
            }

            bool admitted = roots.Count == inputIndex + 1;
            bool failed = failures.Count == failureIndex + 1;
            if (admitted == failed)
            {
                throw new InvalidOperationException(
                    "Each explicit dependency root must produce exactly one admitted input or one typed failure.");
            }

            acquired.Add(
                new DependencyEvidenceAcquiredRoot(
                    requested,
                    admitted ? inputIndex : null,
                    failed ? failureIndex : null,
                    restoredTraversal));
        }

        return new DependencyEvidenceAcquisitionBatch(
            new PackageDependencyEvidenceRequest(
                roots.ToImmutable(),
                failures.ToImmutable()),
            acquired.ToImmutable());
    }

    /// <summary>
    /// Adapts one completed package-profile stream into a request, retaining the producer's
    /// terminal candidate, match, failure, and truncation accounting.
    /// </summary>
    public static async Task<(
        PackageDependencyEvidenceRequest Request,
        PackageProfileSummary Summary)> AcquirePackagePrefixAsync(
            IPackageSourceClient source,
            PackagePrefixProfileRequest request,
            string? targetFramework,
            NuGetOperationContext? operationContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<PackageProfileEvent> events =
            await PackageProfileQuery.ExecuteToArrayAsync(
                source,
                request,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        PackageProfileSummary summary = events
            .OfType<PackageProfileEvent.Completed>()
            .Single()
            .Value;
        return (
            PackageDependencyEvidenceQuery.CreatePackagePrefixRequest(
                [.. events.OfType<PackageProfileEvent.Match>()
                    .Select(match => match.Value)],
                [.. events.OfType<PackageProfileEvent.Failure>()
                    .Select(failure => failure.Value)],
                summary,
                targetFramework),
            summary);
    }

    internal static async Task<(
        PackageDependencyEvidenceRequest Request,
        PackageProfileSummary Summary)> AcquirePackagePrefixAsync(
            string prefix,
            int maximumPackages,
            string? targetFramework,
            CommandContext context,
            CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                DotnetInspector.Networking.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions);
        using var operationContext = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            cancellationToken);
        return await AcquirePackagePrefixAsync(
            source,
            new PackagePrefixProfileRequest(prefix, maximumPackages),
            targetFramework,
            operationContext,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a package target names a local archive rather than a remote coordinate.</summary>
    public static bool IsLocalArchiveTarget(string package) =>
        package.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dependency acquisition's coordinate resolution: package-owned version discovery for a floating
    /// target, and the shared resolver's exact path for everything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exact pin — prerelease or not — asks no latest-version question. It goes straight to
    /// the shared resolver, which validates the grammar, canonicalizes the version, and binds
    /// the authorized sources without consulting any producer, so a pinned prerelease stays
    /// exact without <c>--preview</c>.
    /// </para>
    /// <para>
    /// A floating <c>ID</c> target asks the package-owned
    /// <see cref="DesktopPackageSourceComposition"/> instead of a command-local listing rule.
    /// The composition is the normative owner of what a configured authority publishes: it
    /// composes HTTP and local-folder evidence together, applies listing state and prerelease
    /// policy, sorts every authority's candidates globally before it limits, and reports how
    /// complete the aggregate is. This acquisition path therefore asks for one row and neither infers
    /// which authorities can answer from source text or transport nor re-implements selection.
    /// </para>
    /// <para>
    /// The admitted root publishes the floating answer as one exact coordinate said to be
    /// latest across every authorized producer, so only an
    /// <see cref="PackageVersionDiscoveryState.Authoritative"/> aggregate that returned an
    /// acceptable version may be admitted. <see cref="PackageVersionDiscoveryState.Partial"/>,
    /// <see cref="PackageVersionDiscoveryState.Failed"/>, and an authoritative empty answer are
    /// all inconclusive rather than absence: some authority was not heard from, or none
    /// publishes a version this request accepts, and neither proves the coordinate does not
    /// exist. Each becomes the typed unavailable resolution this acquisition path classifies as an
    /// acquisition failure.
    /// </para>
    /// <para>
    /// <c>--preview</c> is the only thing that widens the accepted set to a prerelease head; the
    /// composition applies it, so an unqualified target still means latest stable.
    /// </para>
    /// </remarks>
    internal static async Task<PackageCoordinateResolution> ResolveCoordinateAsync(
        HttpClient httpClient,
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        DependencyEvidenceVersionDiscovery discoverVersions,
        Action<string>? log,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(discoverVersions);

        if (coordinate.Version is not null)
        {
            return await ResolveExactAsync(
                httpClient,
                coordinate,
                authorizedSources,
                log,
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
        }

        PackageVersionDiscoveryResult discovery = await discoverVersions(
            coordinate.PackageId,
            includePrerelease,
            cancellationToken).ConfigureAwait(false);

        if (discovery.State is not PackageVersionDiscoveryState.Authoritative
            || discovery.Versions.Count == 0)
        {
            log?.Invoke(
                "Refusing to bind a floating package version: version discovery for "
                + $"'{coordinate.PackageId}' across the configured authorities was "
                + $"{discovery.State} and returned {discovery.Versions.Count} acceptable "
                + "version(s), which cannot prove which version is latest.");
            return await InconclusiveAsync(
                httpClient,
                coordinate,
                includePrerelease,
                cancellationToken).ConfigureAwait(false);
        }

        // Discovery answered, so the remaining question is exactly the one an exact pin asks,
        // and it is asked through the same owner rather than a second construction path.
        return await ResolveExactAsync(
            httpClient,
            coordinate with { Version = discovery.Versions[0] },
            authorizedSources,
            log,
            includePrerelease,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves one exact coordinate through the shared resolver, which consults no producer
    /// for it.
    /// </summary>
    /// <remarks>
    /// The candidate cache stays off so no answer is inherited from a legacy caller's less
    /// strict resolution, and <c>requireStableFloating</c> stays on so this acquisition contract
    /// holds for any path that still reaches shared floating selection.
    /// </remarks>
    private static Task<PackageCoordinateResolution> ResolveExactAsync(
        HttpClient httpClient,
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log,
        bool includePrerelease,
        CancellationToken cancellationToken) =>
        PackageCoordinateResolver.ResolveAsync(
            httpClient,
            coordinate,
            authorizedSources,
            log,
            includePrerelease: includePrerelease,
            useVersionCache: false,
            requireStableFloating: true,
            cancellationToken: cancellationToken);

    /// <summary>
    /// The typed inconclusive resolution for a floating coordinate no authoritative aggregate
    /// selected a version for.
    /// </summary>
    /// <remarks>
    /// The refusal is stated in the resolver's own vocabulary rather than a second one: the
    /// shared resolver constructs <see cref="PackageCoordinateResolution.Unavailable"/>, which
    /// this assembly cannot construct itself, and asking it for a floating coordinate with no
    /// authorized source is the one path that returns that outcome without consulting any
    /// producer. Its message is the resolver's and is never surfaced; the reason the adapter
    /// refused is logged by the caller instead.
    /// </remarks>
    private static Task<PackageCoordinateResolution> InconclusiveAsync(
        HttpClient httpClient,
        PackageCoordinate coordinate,
        bool includePrerelease,
        CancellationToken cancellationToken) =>
        PackageCoordinateResolver.ResolveAsync(
            httpClient,
            coordinate with { Version = null },
            [],
            log: null,
            includePrerelease: includePrerelease,
            useVersionCache: false,
            requireStableFloating: true,
            cancellationToken: cancellationToken);

    private static async Task AcquirePackageAsync(
        string package,
        DependencyEvidenceAcquisitionOptions options,
        IPackageSourceAuthorization authorization,
        DependencyEvidenceCoordinateResolver resolveCoordinate,
        Func<DesktopPackageSourceComposition> getComposition,
        HttpClient httpClient,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        InertString label = Label(package);
        if (IsLocalArchiveTarget(package))
        {
            await AcquireLocalArchiveAsync(
                package,
                label,
                options.Tfm,
                roots,
                failures,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        (string packageId, string? version) = SplitPackageTarget(package);
        var requested = new PackageCoordinate(packageId, version);

        // The coordinate grammar decides admissibility before any source policy is consulted,
        // so a blank id or the empty version an 'ID@' target names is this root's typed
        // producer-contract failure. Deciding it here is what keeps the gesture honest: the
        // id would otherwise reach source resolution as an argument it rejects by throwing,
        // aborting every sibling root, and the empty version would be normalized away and
        // silently rebound to latest.
        if (PackageCoordinateResolver.Validate(requested) is not null)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return;
        }

        // Authorization is asked once, per package id, through the shared seam: a package
        // source mapping that authorizes no producer for this id is that root's typed
        // outcome, not an exception that ends the request. The denial's own message is not
        // carried into the sink, because it quotes the configuration the caller selected.
        PackageSourceAuthorization authorized =
            authorization.AuthorizeSourcesFor(packageId.ToLowerInvariant());
        if (authorized.Authorities.Count == 0)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .SourceUnavailable,
                    label));
            return;
        }

        PackageCoordinateResolution resolution;
        try
        {
            resolution = await resolveCoordinate(
                requested,
                authorized.Sources,
                options.IncludePrerelease,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or IOException
            or NuGetRequestTimeoutException
            or NuGetOperationTimeoutException
            or OfflineException)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .SourceUnavailable,
                    label));
            return;
        }

        if (resolution is not PackageCoordinateResolution.Resolved resolved)
        {
            // An unavailable resolution is inconclusive, not absence: it is reported for a
            // coordinate no authorized source is configured for, a version aggregate that was
            // partial or failed, and an authoritative aggregate that publishes nothing this
            // request accepts alike. None of those is an authoritative all-source absence
            // claim, so the conservative acquisition failure is retained. Only the later
            // source loop, where every attempted source answered with a typed NotFound,
            // states absence.
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    resolution is PackageCoordinateResolution.Invalid
                        ? PackageDependencyEvidenceAcquisitionFailureReason
                            .ProducerContract
                        : PackageDependencyEvidenceAcquisitionFailureReason
                            .AcquisitionFailed,
                    label));
            return;
        }

        PackageSourceCoordinate coordinate;
        try
        {
            coordinate = PackageSourceCoordinate.Create(
                resolved.Coordinate.PackageId,
                resolved.Coordinate.Version);
        }
        catch (ArgumentException)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return;
        }

        // The source loop consults the owner-issued authorities themselves, not the display
        // text of the sources resolution echoed back. Resolution never narrows the authorized
        // set here — version discovery is an aggregate over every eligible authority, not a
        // per-source attribution — so every authorized authority is still tried in order, and
        // each keeps the association its owner minted for it.
        await AcquireSourceManifestAsync(
            coordinate,
            authorized.Authorities,
            options,
            label,
            getComposition(),
            httpClient,
            roots,
            failures,
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    private static async Task AcquireSourceManifestAsync(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<ConfiguredPackageAuthority> authorities,
        DependencyEvidenceAcquisitionOptions options,
        InertString label,
        DesktopPackageSourceComposition composition,
        HttpClient httpClient,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        NuGetOperationContext? ownedOperation = null;
        if (operationContext is null)
        {
            NuGetFetchOptions fetchOptions =
                NuGetFetchOptions.FromRequestTimeout(httpClient.Timeout);
            ownedOperation = new NuGetOperationContext(
                fetchOptions.RequestTimeout,
                fetchOptions.OperationTimeout,
                cancellationToken);
            operationContext = ownedOperation;
        }

        using (ownedOperation)
        {
            await AcquireSourceManifestAsync(
                coordinate,
                authorities,
                (authority, requested, context, token) =>
                    composition.GetManifestAsync(
                        authority,
                        requested,
                        token,
                        context),
                options.Tfm,
                label,
                operationContext,
                roots,
                failures,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tries each authorized source in order and admits the first manifest that both arrives and
    /// establishes package facts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authorization is a list, not a single source: one source failing, omitting the coordinate,
    /// or serving a manifest the facts query rejects says nothing about the next one. Every such
    /// outcome therefore moves to the next source instead of terminating the root.
    /// </para>
    /// <para>
    /// When no source succeeds, the reported failure is the most informative one the adapter
    /// can state without widening the host-neutral failure algebra: the last typed
    /// <c>PackageManifestFailure</c> if any manifest reached validation, and otherwise the
    /// existing acquisition classification. A remote package root is never reported as a
    /// package-profile failure; that shape belongs to prefix discovery.
    /// </para>
    /// <para>
    /// That acquisition classification distinguishes absence from failure. Every attempted
    /// source answering with a typed <c>NotFound</c> is an authoritative statement that the
    /// coordinate is absent, so the root reports <c>NotFound</c>. One transport exception, one
    /// non-<c>NotFound</c> typed failure, or one authorized source this build has no client
    /// for makes the set non-authoritative — some source was never heard from — and the
    /// generic <c>AcquisitionFailed</c> reason is retained. No client at all remains
    /// <c>SourceUnavailable</c>.
    /// </para>
    /// <para>
    /// The loop is generic over what names one authorized producer and how its manifest
    /// operation is invoked. Production hands it owner-issued
    /// <see cref="ConfiguredPackageAuthority"/> values and the package-owned desktop
    /// composition; tests may hand it fake source clients. Neither path reconstructs an
    /// authority from source text, and the loop itself reads nothing off
    /// <typeparamref name="TAuthorized"/>.
    /// </para>
    /// </remarks>
    internal static async Task AcquireSourceManifestAsync<TAuthorized>(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<TAuthorized> sources,
        Func<TAuthorized, IPackageSourceClient?> createClient,
        string? targetFramework,
        InertString label,
        NuGetOperationContext? operationContext,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        await AcquireSourceManifestCoreAsync(
            coordinate,
            sources,
            async (source, requested, context, token) =>
            {
                IPackageSourceClient? client = createClient(source);
                if (client is null)
                    return null;

                using (client)
                {
                    return await client.GetManifestAsync(
                        requested.PackageId,
                        requested.Version,
                        token,
                        context).ConfigureAwait(false);
                }
            },
            targetFramework,
            label,
            operationContext,
            roots,
            failures,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcquireSourceManifestAsync<TAuthorized>(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<TAuthorized> sources,
        Func<
            TAuthorized,
            PackageSourceCoordinate,
            NuGetOperationContext?,
            CancellationToken,
            Task<PackageSourceOperationResult<PackageSourceManifest>>> acquireManifest,
        string? targetFramework,
        InertString label,
        NuGetOperationContext? operationContext,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        await AcquireSourceManifestCoreAsync(
            coordinate,
            sources,
            async (source, requested, context, token) =>
                await acquireManifest(
                    source,
                    requested,
                    context,
                    token).ConfigureAwait(false),
            targetFramework,
            label,
            operationContext,
            roots,
            failures,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcquireSourceManifestCoreAsync<TAuthorized>(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<TAuthorized> sources,
        Func<
            TAuthorized,
            PackageSourceCoordinate,
            NuGetOperationContext?,
            CancellationToken,
            Task<PackageSourceOperationResult<PackageSourceManifest>?>> acquireManifest,
        string? targetFramework,
        InertString label,
        NuGetOperationContext? operationContext,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(acquireManifest);

        bool attempted = false;
        bool everyAttemptReportedAbsence = true;
        PackageManifestFailure? manifestFailure = null;
        foreach (TAuthorized source in sources)
        {
            PackageSourceOperationResult<PackageSourceManifest>? manifest;
            try
            {
                manifest = await acquireManifest(
                    source,
                    coordinate,
                    operationContext,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (PackageSourceClientUnavailableException)
            {
                // A source with no client in this build was never heard from, so the set
                // cannot claim all-source absence. `attempted` stays false so a set with no
                // client at all still reports SourceUnavailable rather than a failure some
                // source produced.
                everyAttemptReportedAbsence = false;
                continue;
            }
            catch (Exception exception) when (exception is HttpRequestException
                or IOException
                or NuGetRequestTimeoutException
                or NuGetOperationTimeoutException
                or OfflineException)
            {
                attempted = true;
                everyAttemptReportedAbsence = false;
                continue;
            }

            if (manifest is null)
            {
                everyAttemptReportedAbsence = false;
                continue;
            }

            attempted = true;
            if (manifest.Value is not { } value)
            {
                if (manifest.Failure?.Kind
                    is not PackageSourceFailureKind.NotFound)
                {
                    everyAttemptReportedAbsence = false;
                }

                continue;
            }

            PackageManifestFactsResult facts =
                PackageManifestFactsQuery.Execute(
                    value.Content.ToArray(),
                    coordinate);
            if (facts is PackageManifestFactsResult.Failed failed)
            {
                // A manifest that arrived is not an absence claim, whatever the facts
                // query then decides about it.
                everyAttemptReportedAbsence = false;
                manifestFailure = failed.Failure;
                continue;
            }

            roots.Add(
                PackageDependencyEvidenceQuery.CreatePackageInput(
                    ((PackageManifestFactsResult.Available)facts).Value,
                    PackageDependencyEvidenceAcquisitionForm
                        .PackageSourceManifest,
                    targetFramework,
                    label,
                    value.Source));
            return;
        }

        failures.Add(
            manifestFailure is { } terminal
                ? new PackageDependencyEvidenceRootFailure.Package(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    coordinate,
                    terminal,
                    label)
                : Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                    !attempted
                        ? PackageDependencyEvidenceAcquisitionFailureReason
                            .SourceUnavailable
                        : everyAttemptReportedAbsence
                            ? PackageDependencyEvidenceAcquisitionFailureReason
                                .NotFound
                            : PackageDependencyEvidenceAcquisitionFailureReason
                                .AcquisitionFailed,
                    label,
                    coordinate));
    }

    private static async Task AcquireLocalArchiveAsync(
        string path,
        InertString label,
        string? targetFramework,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        PackagePayloadLimits limits = PackagePayloadLimits.Default;
        byte[]? archive = await TryReadBoundedFileAsync(
            path,
            limits.MaxArchiveBytes,
            cancellationToken).ConfigureAwait(false);
        if (archive is null)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                    File.Exists(path)
                        ? PackageDependencyEvidenceAcquisitionFailureReason
                            .AcquisitionFailed
                        : PackageDependencyEvidenceAcquisitionFailureReason
                            .NotFound,
                    label));
            return;
        }

        // A local .nupkg is an archive like any other: nothing it declares about its own entry
        // count, entry paths, or expansion is evidence. It is validated against the shared
        // payload bounds before any entry is enumerated, so a hostile directory is refused
        // before it becomes allocation.
        if (PackageArchiveValidator.Validate(archive, limits, cancellationToken)
            is PackageArchiveValidation.Rejected)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return;
        }

        byte[]? manifestBytes;
        try
        {
            // The ordinary copying constructor keeps the content's immutable generation
            // ownership intact: what it retains cannot diverge from what was validated.
            InMemoryPackageContent content = new(
                archive,
                fromCache: false,
                producerKey: "local-archive");
            string? manifestPath =
                PackageManifestContent.FindRootManifest(content);
            if (manifestPath is null
                || !content.TryOpenEntry(
                    manifestPath,
                    PackageManifestFactsQuery.MaxManifestBytes,
                    out Stream? manifestStream))
            {
                failures.Add(
                    Acquisition(
                        PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                        PackageDependencyEvidenceAcquisitionFailureReason
                            .AcquisitionFailed,
                        label));
                return;
            }

            using (manifestStream)
            {
                manifestBytes = await BoundedContentReader.ReadAllBytesAsync(
                    manifestStream,
                    PackageManifestFactsQuery.MaxManifestBytes,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .AcquisitionFailed,
                    label));
            return;
        }

        AddManifestRoot(
            manifestBytes,
            PackageDependencyEvidenceAcquisitionForm.PackageArchive,
            targetFramework,
            label,
            roots,
            failures);
    }

    private static async Task AcquireNuspecAsync(
        string path,
        string? targetFramework,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        InertString label = Label(path);
        if (IsBlankPath(path))
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return;
        }

        byte[]? manifestBytes = await TryReadBoundedFileAsync(
            path,
            PackageManifestFactsQuery.MaxManifestBytes,
            cancellationToken).ConfigureAwait(false);
        if (manifestBytes is null)
        {
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
                    File.Exists(path)
                        ? PackageDependencyEvidenceAcquisitionFailureReason
                            .AcquisitionFailed
                        : PackageDependencyEvidenceAcquisitionFailureReason
                            .NotFound,
                    label));
            return;
        }

        AddManifestRoot(
            manifestBytes,
            PackageDependencyEvidenceAcquisitionForm.DirectNuspec,
            targetFramework,
            label,
            roots,
            failures);
    }

    private static async Task<RestoredProjectDependencyTraversalResult?>
        AcquireProjectAsync(
        string path,
        string? targetFramework,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken,
        bool graphRequested,
        int? maximumDepth)
    {
        InertString label = Label(path);
        PackageDependencyEvidenceAcquisitionForm sourceKind =
            PackageDependencyEvidenceAcquisitionForm.ProjectLocator;

        if (IsBlankPath(path))
        {
            // The locator reads a blank path as the current directory, so it would answer for
            // a project the caller never named — and answer "not restored" or "not found"
            // about it. An explicit path gesture that names nothing is a producer-contract
            // failure for this root, decided before the locator is asked.
            failures.Add(
                Acquisition(
                    sourceKind,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return null;
        }

        string? assetsPath;
        ProjectAssetsStatus status;
        try
        {
            if (!ProjectAssetsParser.TryFindAssets(
                    path,
                    out assetsPath,
                    out status))
            {
                failures.Add(
                    Acquisition(
                        sourceKind,
                        status == ProjectAssetsStatus.AssetsNotRestored
                            ? PackageDependencyEvidenceAcquisitionFailureReason
                                .NotRestored
                            : PackageDependencyEvidenceAcquisitionFailureReason
                                .NotFound,
                        label));
                return null;
            }
            if (IsDirectAssetsPath(path, assetsPath))
            {
                sourceKind =
                    PackageDependencyEvidenceAcquisitionForm.ProjectAssets;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            // One unusable root path — malformed, too long, or unreadable while the locator
            // enumerates it — is a typed failure for that root. The remaining roots still
            // produce their evidence, and the document reports the partial outcome.
            failures.Add(
                Acquisition(
                    sourceKind,
                    PackageDependencyEvidenceAcquisitionFailureReason.NotFound,
                    label));
            return null;
        }

        byte[]? assetsBytes = await TryReadBoundedFileAsync(
            assetsPath,
            RestoredProjectDependencyFactsQuery.MaxAssetsBytes,
            cancellationToken).ConfigureAwait(false);
        if (assetsBytes is null)
        {
            failures.Add(
                Acquisition(
                    sourceKind,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .AcquisitionFailed,
                    label));
            return null;
        }

        RestoredProjectTargetRequest? target =
            string.IsNullOrWhiteSpace(targetFramework)
                ? null
                : new RestoredProjectTargetRequest(targetFramework);
        RestoredProjectDependencyTraversalResult? traversal = graphRequested
            ? RestoredProjectDependencyTraversalQuery.Execute(
                assetsBytes,
                new RestoredProjectDependencyTraversalRequest(
                    target,
                    maximumDepth))
            : null;
        RestoredProjectDependencyFactsResult? factsResult = graphRequested
            ? null
            : RestoredProjectDependencyFactsQuery.Execute(
                assetsBytes,
                target);
        RestoredProjectDependencyFacts? facts = traversal switch
        {
            RestoredProjectDependencyTraversalResult.Available available =>
                available.Value.Facts,
            RestoredProjectDependencyTraversalResult.Unavailable unavailable =>
                unavailable.Facts,
            RestoredProjectDependencyTraversalResult.Failed
            {
                Failure:
                    RestoredProjectDependencyTraversalFailure.Graph graph,
            } => graph.Facts,
            RestoredProjectDependencyTraversalResult.Failed
            {
                Failure:
                    RestoredProjectDependencyTraversalFailure.Document document,
            } => AddDocumentFailure(document.Failure),
            null when factsResult is RestoredProjectDependencyFactsResult
                .Available available => available.Value,
            null when factsResult is RestoredProjectDependencyFactsResult
                .Failed failed => AddDocumentFailure(failed.Failure),
            _ => throw new InvalidOperationException(
                "Unknown restored-project dependency result."),
        };
        if (facts is null)
            return traversal;

        roots.Add(
            PackageDependencyEvidenceQuery.CreateRestoredProjectInput(
                facts,
                sourceKind,
                label));
        return traversal;

        RestoredProjectDependencyFacts? AddDocumentFailure(
            RestoredProjectDependencyFailure failure)
        {
            failures.Add(
                new PackageDependencyEvidenceRootFailure.RestoredProject(
                    sourceKind,
                    failure,
                    label));
            return null;
        }
    }

    private static void AddManifestRoot(
        byte[] manifestBytes,
        PackageDependencyEvidenceAcquisitionForm sourceKind,
        string? targetFramework,
        InertString label,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures)
    {
        PackageManifestFactsResult facts;
        try
        {
            facts = PackageManifestFactsQuery.ExecuteSelfAttested(manifestBytes);
        }
        catch (NuspecParseException)
        {
            failures.Add(
                Acquisition(
                    sourceKind,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
            return;
        }

        if (facts is PackageManifestFactsResult.Failed failed)
        {
            failures.Add(
                new PackageDependencyEvidenceRootFailure.Package(
                    sourceKind,
                    null,
                    failed.Failure,
                    label));
            return;
        }

        try
        {
            roots.Add(
                PackageDependencyEvidenceQuery.CreatePackageInput(
                    ((PackageManifestFactsResult.Available)facts).Value,
                    sourceKind,
                    targetFramework,
                    label));
        }
        catch (InvalidDataException)
        {
            failures.Add(
                Acquisition(
                    sourceKind,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .ProducerContract,
                    label));
        }
    }

    private static async Task<byte[]?> TryReadBoundedFileAsync(
        string path,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return await BoundedContentReader.ReadAllBytesAsync(
                stream,
                maxBytes,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Splits <c>ID</c> from <c>ID@VERSION</c> with the shared package-target grammar, so
    /// admissibility and acquisition cannot disagree about what a target names.
    /// </summary>
    /// <remarks>
    /// Splitting is all this does. The version it returns is the caller's raw spelling,
    /// including the empty one an <c>ID@</c> target names, because
    /// <see cref="PackageCoordinateResolver.Validate"/> owns the grammar that decides whether
    /// that spelling is a version. Normalizing an empty spelling to "no version" here would
    /// add a second grammar and turn an explicit pin gesture into a floating one.
    /// </remarks>
    private static (string PackageId, string? Version) SplitPackageTarget(
        string package) =>
        DotnetInspector.Packages.PackageExtractor.ParsePackageReference(package);

    /// <summary>
    /// Whether the admitted existing file is itself the selected restored
    /// assets document.
    /// </summary>
    private static bool IsDirectAssetsPath(
        string path,
        string assetsPath)
    {
        if (!File.Exists(path)
            || !Path.GetFileName(path.AsSpan())
                .Equals(
                    "project.assets.json",
                    StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return Path.GetFullPath(path).Equals(
            Path.GetFullPath(assetsPath),
            comparison);
    }

    /// <summary>
    /// Whether an explicit path gesture names nothing. A blank spelling is a contract failure
    /// rather than an implicit binding to the current directory.
    /// </summary>
    private static bool IsBlankPath(string path) =>
        string.IsNullOrWhiteSpace(path);

    private static PackageDependencyEvidenceRootFailure.Acquisition Acquisition(
        PackageDependencyEvidenceAcquisitionForm sourceKind,
        PackageDependencyEvidenceAcquisitionFailureReason reason,
        InertString label,
        PackageSourceCoordinate? coordinate = null) =>
        new(sourceKind, reason, coordinate, label);

    private static InertString Label(string value) =>
        new(
            TextPolicy.Field,
            value,
            PackageManifestFactsQuery.MaxScalarCharacters);
}
