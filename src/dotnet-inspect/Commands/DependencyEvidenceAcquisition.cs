using System.Collections.Immutable;
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
    /// Every named root is one explicit gesture, so one unusable gesture is one typed failed
    /// root: no root aborts the request, and none is silently rebound to a different input.
    /// </remarks>
    public static async Task<PackageDependencyEvidenceRequest> AcquireExplicitRootsAsync(
        DependencyEvidenceOptions options,
        HttpClient httpClient,
        Action<string>? log,
        CancellationToken cancellationToken,
        IPackageSourceAuthorization? authorization = null,
        DependencyEvidenceCoordinateResolver? resolveCoordinate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        IPackageSourceAuthorization sourceAuthorization = authorization
            ?? new SourcePolicyPackageSourceAuthorization(options.SourceOptions);
        DependencyEvidenceCoordinateResolver resolver = resolveCoordinate
            ?? ((coordinate, sources, includePrerelease, token) =>
                ResolveCoordinateAsync(
                    httpClient,
                    coordinate,
                    sources,
                    log,
                    includePrerelease,
                    token));

        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();

        foreach (string package in options.Packages)
        {
            await AcquirePackageAsync(
                package,
                options,
                sourceAuthorization,
                resolver,
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
    /// This command's coordinate resolution: the shared resolver under its strict floating
    /// contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A floating <c>ID</c> target is documented as latest <em>stable</em>, and the admitted
    /// root then states one exact coordinate as evidence. The shared resolver's default
    /// stable-preferred behavior falls back to a prerelease when a feed publishes nothing
    /// else, which would quietly make an unqualified target mean something this command says
    /// <c>--preview</c> is required for; opting into <c>requireStableFloating</c> refuses that
    /// answer instead. <c>--preview</c> widens the same path to prerelease selection, and an
    /// exact pin — prerelease or not — never reaches it, so a pinned prerelease stays exact
    /// without <c>--preview</c>.
    /// </para>
    /// <para>
    /// The same opt-in refuses an incomplete authorized-source listing. A candidate set
    /// missing one authorized source cannot prove which version is latest, and this command
    /// would otherwise publish that unproven selection as the root's exact coordinate.
    /// </para>
    /// <para>
    /// One kind of missing answer is this command's own to decide, because the shared listing
    /// path never produces it: a source that path cannot query at all — a local folder, a
    /// <c>file://</c> URL — is skipped there, which is right for a caller that only wants some
    /// answer and wrong for this one, which publishes the floating answer as an exact
    /// coordinate said to be latest across every authorized producer. So a floating coordinate
    /// is refused here, before shared resolution, when any authorized source cannot be listed.
    /// An exact pin — including a pinned prerelease — never asks that question and still
    /// acquires from the same source.
    /// </para>
    /// <para>
    /// The refusal is stated in the resolver's own vocabulary rather than a second one: asked
    /// with no source that can answer, it returns the typed
    /// <see cref="PackageCoordinateResolution.Unavailable"/> this command classifies as an
    /// acquisition failure. Its message is the resolver's and is never surfaced; the reason
    /// this command refused is written to <paramref name="log"/> instead.
    /// </para>
    /// <para>
    /// The candidate cache stays off so a floating answer is not inherited from a legacy
    /// caller's less strict resolution.
    /// </para>
    /// </remarks>
    internal static Task<PackageCoordinateResolution> ResolveCoordinateAsync(
        HttpClient httpClient,
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log,
        bool includePrerelease,
        CancellationToken cancellationToken) =>
        PackageCoordinateResolver.ResolveAsync(
            httpClient,
            coordinate,
            SourcesThatCanAnswer(coordinate, authorizedSources, log),
            log,
            includePrerelease: includePrerelease,
            useVersionCache: false,
            requireStableFloating: true,
            cancellationToken: cancellationToken);

    /// <summary>
    /// The authorized sources this command will consult, which is all of them unless a
    /// floating coordinate asks a question none of them can answer together.
    /// </summary>
    private static IReadOnlyList<PackageSource> SourcesThatCanAnswer(
        PackageCoordinate coordinate,
        IReadOnlyList<PackageSource> authorizedSources,
        Action<string>? log)
    {
        if (coordinate.Version is not null)
            return authorizedSources;

        foreach (PackageSource source in authorizedSources)
        {
            if (IsRemotelyListable(source))
                continue;

            log?.Invoke(
                "Refusing to resolve a floating package version: authorized source "
                + $"'{PackageSourceDisplay.ForDiagnostics(source)}' cannot be listed over HTTP, "
                + "so no latest version can be proven across every authorized source.");
            return [];
        }

        return authorizedSources;
    }

    /// <summary>
    /// Whether the shared HTTP listing path can ask <paramref name="source"/> what it
    /// publishes. Only an absolute http/https URL can be listed; a local folder path, a
    /// <c>file://</c> URL, and any other spelling cannot.
    /// </summary>
    private static bool IsRemotelyListable(PackageSource source) =>
        Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task AcquirePackageAsync(
        string package,
        DependencyEvidenceOptions options,
        IPackageSourceAuthorization authorization,
        DependencyEvidenceCoordinateResolver resolveCoordinate,
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
        if (authorized.Sources.Count == 0)
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
            or NuGetOperationTimeoutException)
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
            // source that could not answer, a listing no authorized source completed, a
            // package whose only listed versions this command's stable contract refuses, and
            // a floating coordinate refused here because an authorized source cannot be
            // listed alike. None of those is an authoritative all-source absence claim, so the
            // conservative acquisition failure is retained. Only the later source loop, where
            // every attempted source answered with a typed NotFound, states absence.
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

        await AcquireSourceManifestAsync(
            coordinate,
            resolved.Coordinate.Sources,
            options,
            label,
            httpClient,
            roots,
            failures,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcquireSourceManifestAsync(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<PackageSource> sources,
        DependencyEvidenceOptions options,
        InertString label,
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
            sources,
            source => CreateSourceClient(source, fetchOptions),
            options.Tfm,
            label,
            operationContext,
            roots,
            failures,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates one authorized source client, or null when this build has no client for it.
    /// </summary>
    /// <remarks>
    /// A local folder is an ordinary authorized source under the normal <c>--source</c>,
    /// <c>--add-source</c>, and <c>--nugetconfig</c> policy, so it is routed to the local
    /// client rather than handed to the HTTP overload, which refuses a file endpoint. Each
    /// constructed client gets its own caller-created association: the association scopes one
    /// client's results, so sharing one across clients would let two sources' results claim the
    /// same scope.
    /// </remarks>
    private static IPackageSourceClient? CreateSourceClient(
        PackageSource source,
        NuGetFetchOptions fetchOptions)
    {
        PackageSourceAssociation association = PackageSourceAssociation.Create();
        try
        {
            return LocalPackageSourceIdentity.IsLocalSource(source.Url)
                ? PackageSourceClientFactory.Create(
                    LocalPackageSourceIdentity.CreateAbsolute(source.Url),
                    association,
                    options: null)
                : PackageSourceClientFactory.Create(
                    source,
                    association,
                    fetchOptions);
        }
        catch (PackageSourceClientUnavailableException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // The only local-source construction failure this classification covers: a source
            // that is empty, relative with no resolution base, or a malformed file URI has no
            // client in this build, which is the same "unavailable" outcome the typed
            // exception above reports. Nothing else is caught here.
            return null;
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
    /// The client factory is a parameter so a test can supply fake sources without duplicating
    /// source semantics here.
    /// </para>
    /// </remarks>
    internal static async Task AcquireSourceManifestAsync(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<PackageSource> sources,
        Func<PackageSource, IPackageSourceClient?> createClient,
        string? targetFramework,
        InertString label,
        NuGetOperationContext? operationContext,
        ImmutableArray<PackageDependencyEvidenceInput>.Builder roots,
        ImmutableArray<PackageDependencyEvidenceRootFailure>.Builder failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(createClient);

        bool attempted = false;
        bool everyAttemptReportedAbsence = true;
        PackageManifestFailure? manifestFailure = null;
        foreach (PackageSource source in sources)
        {
            IPackageSourceClient? client = createClient(source);
            if (client is null)
            {
                // A source with no client in this build was never heard from, so the set
                // cannot claim all-source absence. `attempted` stays false so a set with no
                // client at all still reports SourceUnavailable rather than a failure some
                // source produced.
                everyAttemptReportedAbsence = false;
                continue;
            }

            using (client)
            {
                attempted = true;
                PackageSourceOperationResult<PackageSourceManifest> manifest;
                try
                {
                    manifest = await client.GetManifestAsync(
                        coordinate.PackageId,
                        coordinate.Version,
                        cancellationToken,
                        operationContext).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException
                    or IOException
                    or NuGetRequestTimeoutException
                    or NuGetOperationTimeoutException)
                {
                    everyAttemptReportedAbsence = false;
                    continue;
                }

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
