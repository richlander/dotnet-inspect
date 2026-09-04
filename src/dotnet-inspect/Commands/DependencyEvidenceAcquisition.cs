using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Commands;

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
    public static async Task<PackageDependencyEvidenceRequest> AcquireExplicitRootsAsync(
        DependencyEvidenceOptions options,
        HttpClient httpClient,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        var roots = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures =
            ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();

        foreach (string package in options.Packages)
        {
            await AcquirePackageAsync(
                package,
                options,
                httpClient,
                log,
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

    private static async Task AcquirePackageAsync(
        string package,
        DependencyEvidenceOptions options,
        HttpClient httpClient,
        Action<string>? log,
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
        IReadOnlyList<PackageSource> authorizedSources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                options.SourceOptions,
                packageId);
        PackageCoordinateResolution resolution;
        try
        {
            resolution = await PackageCoordinateResolver.ResolveAsync(
                httpClient,
                new PackageCoordinate(packageId, version),
                authorizedSources,
                log,
                includePrerelease: options.IncludePrerelease,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
            failures.Add(
                Acquisition(
                    PackageDependencyEvidenceSourceKind.PackageSourceManifest,
                    resolution is PackageCoordinateResolution.Invalid
                        ? PackageDependencyEvidenceAcquisitionFailureReason
                            .ProducerContract
                        : PackageDependencyEvidenceAcquisitionFailureReason
                            .NotFound,
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
    /// coordinate is absent, so the root reports <c>NotFound</c>. One transport exception or
    /// one non-<c>NotFound</c> typed failure makes the set non-authoritative — some source was
    /// never heard from — and the generic <c>AcquisitionFailed</c> reason is retained. No
    /// client at all remains <c>SourceUnavailable</c>.
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
                continue;

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
    /// Splits <c>ID</c> or <c>ID@VERSION</c> with the shared package-target grammar, so
    /// admissibility and acquisition cannot disagree about what a target names.
    /// </summary>
    private static (string PackageId, string? Version) SplitPackageTarget(
        string package)
    {
        (string name, string? version) =
            DotnetInspector.Packages.PackageExtractor.ParsePackageReference(
                package);
        return (name, string.IsNullOrWhiteSpace(version) ? null : version);
    }

    /// <summary>Whether a project root names a restored assets document directly.</summary>
    private static bool IsDirectAssetsPath(string path) =>
        Path.GetFileName(path.AsSpan())
            .Equals("project.assets.json", StringComparison.OrdinalIgnoreCase);

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
