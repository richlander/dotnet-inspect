using ILInspector.CSharp;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Builds dependency graph data for the depends command.
/// </summary>
internal static class DependencyGraphService
{
    private const string TempDirPrefix = "inspect-depends";
    private static readonly TimeSpan CachedVersionResolutionTimeout =
        TimeSpan.FromSeconds(1);

    public static Task<TypeDependencyResult> BuildTypeDependencyTreeAsync(
        HttpClient httpClient,
        DependsOptions options,
        VerboseLogger logger)
    {
        return WithAssemblySetAsync(
            httpClient,
            options.ToAssemblySetRequest(TempDirPrefix),
            logger,
            assemblySet =>
            {
                logger.Log($"Scanning {assemblySet.Assemblies.Count} libraries for type {options.TargetType}");
                var assemblyPaths = assemblySet.Assemblies.Select(a => a.Path).ToList();
                TypeDependencyResult result =
                    TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);
                return result.Rejections.Count == 0
                    ? result
                    : result with
                    {
                        Rejections = RelativizeRejections(
                            result.Rejections,
                            assemblySet),
                    };
            });
    }

    /// <summary>
    /// Rewrites rejection paths as package-relative paths so the reported
    /// participant matches the identity the other package commands print.
    /// </summary>
    private static IReadOnlyList<TypeDependencyRejection> RelativizeRejections(
        IReadOnlyList<TypeDependencyRejection> rejections,
        AssemblySet assemblySet)
    {
        return [.. rejections.Select(
            rejection => rejection with
            {
                AssemblyPath = PackageRelativePath(
                    rejection.AssemblyPath,
                    assemblySet.OwnedTemporaryDirectories),
            })];
    }

    // Package extraction nests the payload under this directory inside the
    // owned temporary directory (see PackageExtractor). The other package
    // commands relativize against that directory, so rejection paths have to
    // resolve against it first to print the same identity.
    private const string PackageExtractionDirectoryName = "extracted";

    private static string PackageRelativePath(
        string assemblyPath,
        IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            string[] candidates =
            [
                Path.Combine(root, PackageExtractionDirectoryName),
                root,
            ];
            foreach (var candidate in candidates)
            {
                var relative = Path.GetRelativePath(candidate, assemblyPath)
                    .Replace('\\', '/');
                if (!relative.StartsWith("../", StringComparison.Ordinal)
                    && !Path.IsPathRooted(relative))
                {
                    return relative;
                }
            }
        }

        return Path.GetFileName(assemblyPath);
    }

    public static async Task<LibraryDependencyGraphResult> BuildLibraryDependencyTreeAsync(
        HttpClient httpClient,
        string libraryName,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger)
    {
        string? assemblyPath = null;
        AssemblySet? ownedAssemblySet = null;

        try
        {
            if (File.Exists(libraryName)
                && !libraryName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest { Assemblies = [libraryName], TempDirPrefix = TempDirPrefix },
                    logger.Log);
                AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
                assemblyPath = ownedAssemblySet.Assemblies.FirstOrDefault()?.Path;
            }
            else if (PlatformResolver.IsPlatformCandidate(libraryName))
            {
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest { PlatformAssemblies = [libraryName], TempDirPrefix = TempDirPrefix },
                    logger.Log);
                if (ownedAssemblySet.Assemblies.Count > 0)
                {
                    AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
                    assemblyPath = ownedAssemblySet.Assemblies[0].Path;
                }
                else
                {
                    ownedAssemblySet.Dispose();
                    ownedAssemblySet = null;
                }
            }

            if (assemblyPath == null)
            {
                logger.Log($"Resolving package: {libraryName}");
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest
                    {
                        Packages = [libraryName],
                        SourceOptions = sourceOptions,
                        TempDirPrefix = TempDirPrefix,
                        PackageSelectionMode = AssemblySetPackageSelectionMode.LibAssembliesDescending,
                    },
                    logger.Log);

                assemblyPath = ownedAssemblySet.Assemblies.FirstOrDefault()?.Path;
                if (assemblyPath == null)
                {
                    AssemblySetDiagnosticWriter.Write(ownedAssemblySet, includeErrors: false);
                    var errorDiagnostic = ownedAssemblySet.Diagnostics
                        .FirstOrDefault(static d => d.Severity == AssemblySetDiagnosticSeverity.Error);
                    return new LibraryDependencyGraphResult.Error(
                        errorDiagnostic?.Message
                            ?? $"Could not resolve '{libraryName}' as a file, platform library, or NuGet package.",
                        libraryName);
                }

                AssemblySetDiagnosticWriter.Write(ownedAssemblySet);
            }

            var (refs, _) =
                AssemblyInspector.ExtractReferenceIdentitiesAndCompany(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            if (refs.Count == 0)
                return new LibraryDependencyGraphResult.Empty(assemblyName);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyName };
            var refNodes = LibraryMetadataService.BuildTransitiveReferences(
                refs, assemblyPath, visited, logger, deduplicate: true);

            return new LibraryDependencyGraphResult.Graph(assemblyName, refNodes);
        }
        finally
        {
            ownedAssemblySet?.Dispose();
        }
    }

    public static async Task<PackageDependencyGraphResult> BuildPackageDependencyTreeAsync(
        HttpClient httpClient,
        string packageRef,
        string? requestedTfm,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        bool includePrerelease = false,
        bool allowCompatibleFallbackForRequestedTfm = true)
    {
        PackageNuspecResolution resolution =
            await ResolvePackageNuspecAsync(
                httpClient,
                packageRef,
                sourceOptions,
                logger,
                includePrerelease).ConfigureAwait(false);
        if (resolution.ErrorMessage is { } error)
            return new PackageDependencyGraphResult.Error(error);

        NuspecData? nuspec = resolution.Nuspec;
        if (nuspec == null)
        {
            return new PackageDependencyGraphResult.Empty(
                resolution.PackageName,
                resolution.Version,
                resolution.ManifestPackageName,
                resolution.ManifestVersion,
                "No dependencies declared in package.",
                PackageDependencyGraphResult.EmptyKind.NoDependencyGroups);
        }

        var selection = DependencyResolutionService.SelectDependencyGroup(
            nuspec.DependencyGroups,
            requestedTfm,
            allowCompatibleFallbackForRequestedTfm);
        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoDependencyGroups)
        {
            return new PackageDependencyGraphResult.Empty(
                resolution.PackageName,
                resolution.Version,
                resolution.ManifestPackageName,
                resolution.ManifestVersion,
                "No dependencies declared in package.",
                PackageDependencyGraphResult.EmptyKind.NoDependencyGroups);
        }
        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoMatchingTargetFramework)
        {
            return new PackageDependencyGraphResult.Error(
                $"No dependencies found for TFM '{selection.TargetFramework}'.",
                "Available TFMs: " + string.Join(", ", selection.AvailableTargetFrameworks));
        }

        var group = selection.Group!;
        var tfm = selection.TargetFramework ?? group.TargetFramework;
        if (group.Dependencies.Count == 0)
        {
            return new PackageDependencyGraphResult.Empty(
                resolution.PackageName,
                resolution.Version,
                resolution.ManifestPackageName,
                resolution.ManifestVersion,
                $"No additional dependencies for {tfm}.",
                PackageDependencyGraphResult.EmptyKind.SelectedGroup);
        }

        var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
            httpClient,
            group.Dependencies,
            tfm,
            globalSeen,
            logger.Log,
            sourceOptions);

        return new PackageDependencyGraphResult.Graph(
            resolution.PackageName,
            resolution.Version,
            resolution.ManifestPackageName,
            resolution.ManifestVersion,
            depNodes);
    }

    private static async Task<PackageNuspecResolution> ResolvePackageNuspecAsync(
        HttpClient httpClient,
        string packageRef,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        bool includePrerelease)
    {
        // Package dependency mode inspects nuspec dependency groups, not assembly sets.
        var (packageName, version) =
            PackageExtractor.ParsePackageReference(packageRef);
        logger.Log($"Resolving package: {packageRef}");

        bool floatingSelector =
            version is null
            || string.Equals(
                version,
                "latest",
                StringComparison.OrdinalIgnoreCase);
        bool requiresArchive =
            packageRef.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase)
            || version?.Contains('*', StringComparison.Ordinal) == true;
        if (requiresArchive)
        {
            return await ResolvePackageNuspecFromArchiveAsync(
                httpClient,
                packageRef,
                packageName,
                sourceOptions,
                logger).ConfigureAwait(false);
        }

        using var feedFailureScope = FeedFailureTelemetry.Scope();
        IReadOnlyList<string> cachedVersions = floatingSelector
            ? GetCachedPackageVersions(
                packageName,
                sourceOptions,
                includePrerelease)
            : [];
        bool forceLatest = string.Equals(
            version,
            "latest",
            StringComparison.OrdinalIgnoreCase);
        CancellationTokenSource? latestTimeout = null;
        if (!forceLatest
            && floatingSelector
            && !Core.HttpClientFactory.IsOffline
            && cachedVersions.Count > 0)
        {
            latestTimeout = new CancellationTokenSource(
                CachedVersionResolutionTimeout);
        }

        PackageCoordinateResolution coordinateResolution;
        try
        {
            coordinateResolution =
                await PackageCoordinateResolver.ResolveUsingSourcePolicyAsync(
                    httpClient,
                    new PackageCoordinate(
                        packageName,
                        floatingSelector ? null : version),
                    sourceOptions,
                    logger.Log,
                    includePrerelease,
                    useVersionCache: !forceLatest,
                    cancellationToken: latestTimeout?.Token ?? default)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (latestTimeout?.IsCancellationRequested == true)
        {
            if (DescribeFeedFailure(packageName)
                is { } feedFailure)
            {
                return PackageNuspecResolution.Error(
                    packageName,
                    feedFailure);
            }

            return PackageNuspecResolution.Error(
                packageName,
                DescribeCachedVersionFallback(
                    packageName,
                    cachedVersions,
                    offline: false));
        }
        finally
        {
            latestTimeout?.Dispose();
        }

        if (coordinateResolution
            is not PackageCoordinateResolution.Resolved resolved)
        {
            if (floatingSelector && Core.HttpClientFactory.IsOffline)
            {
                string offlineMessage = cachedVersions.Count > 0
                    ? DescribeCachedVersionFallback(
                        packageName,
                        cachedVersions,
                        offline: true)
                    : $"Package '{packageName}' is not available offline; "
                        + "no cached version was found.";
                return PackageNuspecResolution.Error(
                    packageName,
                    offlineMessage);
            }

            string? feedFailure =
                DescribeFeedFailure(packageName);
            string message = coordinateResolution switch
            {
                PackageCoordinateResolution.Invalid invalid =>
                    invalid.Message,
                PackageCoordinateResolution.Unavailable
                    when feedFailure is not null =>
                    feedFailure,
                PackageCoordinateResolution.Unavailable unavailable =>
                    unavailable.Message,
                _ => $"Package '{packageRef}' could not be resolved.",
            };
            return PackageNuspecResolution.Error(packageName, message);
        }

        ResolvedPackageCoordinate coordinate = resolved.Coordinate;
        NuGetSourceOptions reportingSources =
            NuGetSourceResolver.RestrictToResolvedSources(
                sourceOptions,
                coordinate.Sources);
        string? nuspecXml = await PackageExtractor.TryGetNuspecXmlAsync(
            httpClient,
            coordinate.PackageId,
            coordinate.Version,
            logger.Log,
            reportingSources).ConfigureAwait(false);
        if (nuspecXml is null)
        {
            return PackageNuspecResolution.Error(
                packageName,
                await DescribeUnavailableNuspecAsync(
                    httpClient,
                    packageName,
                    coordinate.Version,
                    coordinate.WasFloating,
                    reportingSources).ConfigureAwait(false));
        }

        NuspecData nuspec = NuspecParser.ParseContent(nuspecXml);
        if (nuspec.IsToolPackage)
        {
            return await ResolvePackageNuspecFromArchiveAsync(
                httpClient,
                $"{coordinate.PackageId}@{coordinate.Version}",
                packageName,
                reportingSources,
                logger).ConfigureAwait(false);
        }

        return new PackageNuspecResolution(
            packageName,
            coordinate.Version,
            nuspec.PackageName ?? packageName,
            nuspec.Version ?? coordinate.Version,
            nuspec,
            ErrorMessage: null);
    }

    private static async Task<PackageNuspecResolution>
        ResolvePackageNuspecFromArchiveAsync(
            HttpClient httpClient,
            string packageRef,
            string packageName,
            NuGetSourceOptions? sourceOptions,
            VerboseLogger logger)
    {
        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                httpClient,
                packageRef,
                logger.Log,
                sourceOptions: sourceOptions).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            return PackageNuspecResolution.Error(
                packageName,
                outcome.ErrorMessage
                ?? $"Package '{packageRef}' could not be resolved.");
        }

        PackageExtractionResult extracted = outcome.Result!;
        try
        {
            NuspecData? nuspec =
                NuspecParser.FindAndParse(extracted.ExtractPath);
            string resolvedPackageName =
                extracted.PackageName
                ?? packageName;
            string resolvedVersion =
                extracted.Version
                ?? "";
            return new PackageNuspecResolution(
                packageName,
                resolvedVersion,
                nuspec?.PackageName
                    ?? resolvedPackageName,
                nuspec?.Version
                    ?? resolvedVersion,
                nuspec,
                ErrorMessage: null);
        }
        finally
        {
            CleanupTempDir(extracted.TempDir);
        }
    }

    private static IReadOnlyList<string> GetCachedPackageVersions(
        string packageName,
        NuGetSourceOptions? sourceOptions,
        bool includePrerelease)
    {
        try
        {
            return NuGetCache.GetCachedVersions(
                packageName,
                NuGetSourceResolver.ResolveSourceKeysForPackage(
                    sourceOptions,
                    packageName),
                includePrerelease);
        }
        catch (PackageSourceMappingException)
        {
            return [];
        }
    }

    private static async Task<string> DescribeUnavailableNuspecAsync(
        HttpClient httpClient,
        string packageName,
        string version,
        bool versionExistenceKnown,
        NuGetSourceOptions sourceOptions)
    {
        if (Core.HttpClientFactory.IsOffline)
        {
            return InertString.Format(
                TextPolicy.Field,
                $"Package '{packageName}' version '{version}' is not available offline; no cached package was found.")
                .ToString();
        }

        if (DescribeFeedFailure(packageName)
            is { } acquisitionFailure)
        {
            return acquisitionFailure;
        }

        if (versionExistenceKnown)
        {
            return InertString.Format(
                TextPolicy.Field,
                $"Nuspec for package '{packageName}' version '{version}' could not be resolved.")
                .ToString();
        }

        List<PackageVersionInfo>? knownVersions =
            await PackageExtractor.GetVersionListingsAsync(
                httpClient,
                packageName,
                includePrerelease: true,
                includeUnlisted: true,
                limit: null,
                log: null,
                sourceOptions: sourceOptions,
                useVersionCache: false).ConfigureAwait(false);

        if (DescribeFeedFailure(packageName)
            is { } listingFailure)
        {
            return listingFailure;
        }

        if (knownVersions is not { Count: > 0 })
        {
            return InertString.Format(
                TextPolicy.Field,
                $"Package '{packageName}' not found.")
                .ToString();
        }

        if (!knownVersions.Any(candidate =>
                string.Equals(
                    candidate.Version,
                    version,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return InertString.Format(
                TextPolicy.Field,
                $"Version '{version}' of package '{packageName}' not found. Use --versions to see available versions.")
                .ToString();
        }

        return InertString.Format(
            TextPolicy.Field,
            $"Nuspec for package '{packageName}' version '{version}' could not be resolved.")
            .ToString();
    }

    private static string? DescribeFeedFailure(
        string packageName)
    {
        if (FeedFailureTelemetry.Current
            is not { HasFailures: true } failures)
        {
            return null;
        }

        return (failures.DescribeFailure(packageName)
            ?? InertString.Format(
                TextPolicy.Field,
                $"Package '{packageName}' could not be fully resolved from every authorized source."))
            .ToString();
    }

    private static string DescribeCachedVersionFallback(
        string packageName,
        IReadOnlyList<string> cachedVersions,
        bool offline)
    {
        const int DisplayLimit = 5;
        string displayed =
            string.Join(", ", cachedVersions.Take(DisplayLimit));
        string remainder = cachedVersions.Count > DisplayLimit
            ? $" (+{cachedVersions.Count - DisplayLimit} more)"
            : "";
        string reason = offline
            ? $"Package '{packageName}' cannot resolve its latest version "
                + "while offline."
            : $"Package '{packageName}' could not resolve its latest version "
                + "before the online lookup timed out.";

        return $"{reason}{Environment.NewLine}"
            + $"Locally cached versions: {displayed}{remainder}"
            + Environment.NewLine
            + "Use an exact version to skip version discovery, for example: "
            + $"dotnet-inspect package {packageName}@{cachedVersions[0]}";
    }

    private static async Task<TResult> WithAssemblySetAsync<TResult>(
        HttpClient httpClient,
        AssemblySetRequest request,
        VerboseLogger logger,
        Func<AssemblySet, TResult> operation)
    {
        using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
        AssemblySetDiagnosticWriter.Write(assemblySet);
        return operation(assemblySet);
    }

    private static void CleanupTempDir(string? tempDir)
    {
        if (tempDir is null)
            return;

        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    private sealed record PackageNuspecResolution(
        string PackageName,
        string Version,
        string ManifestPackageName,
        string ManifestVersion,
        NuspecData? Nuspec,
        string? ErrorMessage)
    {
        public static PackageNuspecResolution Error(
            string packageName,
            string message) =>
            new(
                packageName,
                "",
                packageName,
                "",
                Nuspec: null,
                ErrorMessage: message);
    }
}

internal abstract record LibraryDependencyGraphResult
{
    public sealed record Graph(string AssemblyName, List<AssemblyReferenceNode> References) : LibraryDependencyGraphResult;
    public sealed record Empty(string AssemblyName) : LibraryDependencyGraphResult;
    /// <summary>
    /// A resolution failure whose message embeds the caller's subject.
    /// </summary>
    /// <remarks>
    /// The subject is untrusted: an agent composes a <c>depends</c> invocation
    /// from a type or package name it read out of metadata, so a name carrying
    /// a bidi override or line separator reaches this message and then stderr.
    /// Containment lives on the record rather than at each writer, so a new
    /// call site cannot reopen it. <see cref="HintInput"/> stays raw: it is
    /// matched against namespace prefixes, not rendered (issue #3319).
    /// </remarks>
    public sealed record Error(string Message, string? HintInput = null) : LibraryDependencyGraphResult
    {
        public string Message { get; init; } = CSharpIdentifier.ContainRenderedText(Message);

        /// <inheritdoc cref="Error"/>
        public string? HintInput { get; init; } = HintInput;
    }
}

internal abstract record PackageDependencyGraphResult
{
    public enum EmptyKind
    {
        NoDependencyGroups,
        SelectedGroup,
    }

    public sealed record Graph(
        string PackageName,
        string Version,
        string ManifestPackageName,
        string ManifestVersion,
        List<DependencyNode> Dependencies) : PackageDependencyGraphResult
    {
        public string Title => $"{PackageName} ({Version})";
    }

    public sealed record Empty(
        string PackageName,
        string Version,
        string ManifestPackageName,
        string ManifestVersion,
        string Message,
        EmptyKind Kind) : PackageDependencyGraphResult;

    public sealed record Error(string Message, string? Detail = null) : PackageDependencyGraphResult;
}
