using System.Collections.Immutable;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Commands;

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
/// and states what this command then did with it, without reaching a live feed. Production
/// binds it to <see cref="DesktopPackageSourceComposition.GetVersionsAsync"/>.
/// </remarks>
internal delegate Task<PackageVersionDiscoveryResult> DependencyEvidenceVersionDiscovery(
    string packageId,
    bool includePrerelease,
    CancellationToken cancellationToken);

/// <summary>
/// Thin acquisition adapters for <c>dependency-evidence</c> roots.
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

    /// <summary>Acquires the explicitly named package, nuspec, and project roots.</summary>
    /// <remarks>
    /// <para>
    /// Every named root is one explicit gesture, so one unusable gesture is one typed failed
    /// root: no root aborts the request, and none is silently rebound to a different input.
    /// </para>
    /// <para>
    /// One package-owned source composition serves the whole request. It is the same lifetime
    /// <see cref="CommandContext.CreatePackageSourceComposition"/> gives other commands — one
    /// composition over this request's deadline, owned and disposed exactly once — and it is
    /// created only when a remote package root asks a version or manifest question, so a
    /// nuspec-only or archive-only request builds no source runtime at all.
    /// </para>
    /// </remarks>
    public static async Task<PackageDependencyEvidenceRequest> AcquireExplicitRootsAsync(
        DependencyEvidenceOptions options,
        HttpClient httpClient,
        Action<string>? log,
        CancellationToken cancellationToken,
        IPackageSourceAuthorization? authorization = null,
        DependencyEvidenceCoordinateResolver? resolveCoordinate = null,
        DependencyEvidenceVersionDiscovery? discoverVersions = null,
        Func<TimeSpan, DesktopPackageSourceComposition>? createComposition = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        IPackageSourceAuthorization sourceAuthorization = authorization
            ?? new SourcePolicyPackageSourceAuthorization(options.SourceOptions);
        DesktopPackageSourceComposition? composition = null;
        DesktopPackageSourceComposition GetComposition() =>
            composition ??= createComposition?.Invoke(httpClient.Timeout)
                ?? new DesktopPackageSourceComposition(httpClient.Timeout);
        DependencyEvidenceVersionDiscovery discovery = discoverVersions
            ?? ((packageId, includePrerelease, token) =>
            {
                return GetComposition().GetVersionsAsync(
                    packageId,
                    includePrerelease,
                    // The composition sorts every authority's evidence together before it
                    // limits, so one row is the global latest acceptable version rather than
                    // the first authority's.
                    limit: 1,
                    options.SourceOptions,
                    log,
                    token);
            });
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

        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();

        try
        {
            foreach (string package in options.Packages)
            {
                await AcquirePackageAsync(
                    package,
                    options,
                    sourceAuthorization,
                    resolver,
                    GetComposition,
                    httpClient,
                    roots,
                    failures,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (string nuspec in options.Nuspecs)
            {
                await AcquireNuspecAsync(
                    nuspec,
                    options.Tfm,
                    roots,
                    failures,
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (string project in options.Projects)
            {
                await AcquireProjectAsync(
                    project,
                    options.Tfm,
                    roots,
                    failures,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (composition is not null)
                await composition.DisposeAsync().ConfigureAwait(false);
        }

        return new PackageDependencyEvidenceRequest(
            roots.ToImmutable(),
            failures.ToImmutable());
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

    /// <summary>Whether a package target names a local archive rather than a remote coordinate.</summary>
    public static bool IsLocalArchiveTarget(string package) =>
        package.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// This command's coordinate resolution: package-owned version discovery for a floating
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
    /// complete the aggregate is. This command therefore asks for one row and neither infers
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
    /// exist. Each becomes the typed unavailable resolution this command classifies as an
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
    /// strict resolution, and <c>requireStableFloating</c> stays on so this command's contract
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
    /// producer. Its message is the resolver's and is never surfaced; the reason this command
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
        DependencyEvidenceOptions options,
        IPackageSourceAuthorization authorization,
        DependencyEvidenceCoordinateResolver resolveCoordinate,
        Func<DesktopPackageSourceComposition> getComposition,
        HttpClient httpClient,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
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
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcquireSourceManifestAsync(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<ConfiguredPackageAuthority> authorities,
        DependencyEvidenceOptions options,
        InertString label,
        DesktopPackageSourceComposition composition,
        HttpClient httpClient,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(httpClient.Timeout);
        using var operationContext = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            cancellationToken);
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
    /// When no source succeeds, the reported failure is the most informative one this command
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
                    PackageDependencyEvidenceSourceKind
                        .PackageSourceManifest,
                    targetFramework,
                    label,
                    value.Source));
            return;
        }

        failures.Add(
            manifestFailure is { } terminal
                ? new PackageDependencyEvidenceRootFailure.Package(
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
                    coordinate,
                    terminal,
                    label)
                : Acquisition(
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
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
                    PackageDependencyEvidenceSourceKind.PackageArchive,
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
                    PackageDependencyEvidenceSourceKind.PackageArchive,
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
                        PackageDependencyEvidenceSourceKind.PackageArchive,
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
                    PackageDependencyEvidenceSourceKind.PackageArchive,
                    PackageDependencyEvidenceAcquisitionFailureReason
                        .AcquisitionFailed,
                    label));
            return;
        }

        AddManifestRoot(
            manifestBytes,
            PackageDependencyEvidenceSourceKind.PackageArchive,
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
                    PackageDependencyEvidenceSourceKind.DirectNuspec,
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
                    PackageDependencyEvidenceSourceKind.DirectNuspec,
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
            PackageDependencyEvidenceSourceKind.DirectNuspec,
            targetFramework,
            label,
            roots,
            failures);
    }

    private static async Task AcquireProjectAsync(
        string path,
        string? targetFramework,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        InertString label = Label(path);
        bool isDirectAssets = IsDirectAssetsPath(path);
        PackageDependencyEvidenceSourceKind sourceKind = isDirectAssets
            ? PackageDependencyEvidenceSourceKind.ProjectAssets
            : PackageDependencyEvidenceSourceKind.ProjectLocator;

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
            return;
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
                return;
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
            return;
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
            return;
        }

        RestoredProjectDependencyFactsResult result =
            RestoredProjectDependencyFactsQuery.Execute(
                assetsBytes,
                string.IsNullOrWhiteSpace(targetFramework)
                    ? null
                    : new RestoredProjectTargetRequest(targetFramework));
        if (result is RestoredProjectDependencyFactsResult.Failed failed)
        {
            failures.Add(
                new PackageDependencyEvidenceRootFailure.RestoredProject(
                    sourceKind,
                    failed.Failure,
                    label));
            return;
        }

        roots.Add(
            PackageDependencyEvidenceQuery.CreateRestoredProjectInput(
                ((RestoredProjectDependencyFactsResult.Available)result).Value,
                sourceKind,
                label));
    }

    private static void AddManifestRoot(
        byte[] manifestBytes,
        PackageDependencyEvidenceSourceKind sourceKind,
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

    /// <summary>Whether a project root names a restored assets document directly.</summary>
    private static bool IsDirectAssetsPath(string path) =>
        Path.GetFileName(path.AsSpan())
            .Equals("project.assets.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an explicit path gesture names nothing. A blank spelling is a contract failure
    /// rather than an implicit binding to the current directory.
    /// </summary>
    private static bool IsBlankPath(string path) =>
        string.IsNullOrWhiteSpace(path);

    private static PackageDependencyEvidenceRootFailure.Acquisition Acquisition(
        PackageDependencyEvidenceSourceKind sourceKind,
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
