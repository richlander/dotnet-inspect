using ILInspector.CSharp;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Builds dependency graph data for the depends command.
/// </summary>
internal static class DependencyGraphService
{
    private const string TempDirPrefix = "inspect-depends";

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
                return TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);
            });
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
        VerboseLogger logger)
    {
        PackageNuspecResolution resolution =
            await ResolvePackageNuspecAsync(
                httpClient,
                packageRef,
                sourceOptions,
                logger).ConfigureAwait(false);
        if (resolution.ErrorMessage is { } error)
            return new PackageDependencyGraphResult.Error(error);

        NuspecData? nuspec = resolution.Nuspec;
        if (nuspec == null)
            return new PackageDependencyGraphResult.Empty("No dependencies declared in package.");

        var selection = DependencyResolutionService.SelectDependencyGroup(nuspec.DependencyGroups, requestedTfm);
        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoDependencyGroups)
            return new PackageDependencyGraphResult.Empty("No dependencies declared in package.");
        if (selection.Status == DependencyResolutionService.DependencyGroupSelectionStatus.NoMatchingTargetFramework)
        {
            return new PackageDependencyGraphResult.Error(
                $"No dependencies found for TFM '{selection.TargetFramework}'.",
                "Available TFMs: " + string.Join(", ", selection.AvailableTargetFrameworks));
        }

        var group = selection.Group!;
        var tfm = selection.TargetFramework ?? group.TargetFramework;
        if (group.Dependencies.Count == 0)
            return new PackageDependencyGraphResult.Empty($"No additional dependencies for {tfm}.");

        var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
            httpClient,
            group.Dependencies,
            tfm,
            globalSeen,
            logger.Log,
            sourceOptions);

        return new PackageDependencyGraphResult.Graph(
            $"{resolution.PackageName} ({resolution.Version})",
            depNodes);
    }

    private static async Task<PackageNuspecResolution> ResolvePackageNuspecAsync(
        HttpClient httpClient,
        string packageRef,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger)
    {
        // Package dependency mode inspects nuspec dependency groups, not assembly sets.
        var (packageName, version) =
            PackageExtractor.ParsePackageReference(packageRef);
        logger.Log($"Resolving package: {packageRef}");

        // Local inputs and wildcard selectors still need archive acquisition.
        bool requiresArchive =
            (packageRef.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                && File.Exists(packageRef))
            || version?.Contains('*', StringComparison.Ordinal) == true;
        if (requiresArchive)
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
                return new PackageNuspecResolution(
                    packageName,
                    extracted.Version ?? "",
                    NuspecParser.FindAndParse(extracted.ExtractPath),
                    ErrorMessage: null);
            }
            finally
            {
                CleanupTempDir(extracted.TempDir);
            }
        }

        bool forceLatest = string.Equals(
            version,
            "latest",
            StringComparison.OrdinalIgnoreCase);
        PackageCoordinateResolution coordinateResolution =
            await PackageCoordinateResolver.ResolveUsingSourcePolicyAsync(
                httpClient,
                new PackageCoordinate(packageName, forceLatest ? null : version),
                sourceOptions,
                logger.Log,
                useVersionCache: !forceLatest).ConfigureAwait(false);
        if (coordinateResolution
            is not PackageCoordinateResolution.Resolved resolved)
        {
            string message = coordinateResolution switch
            {
                PackageCoordinateResolution.Invalid invalid =>
                    invalid.Message,
                PackageCoordinateResolution.Unavailable unavailable =>
                    unavailable.Message,
                _ => $"Package '{packageRef}' could not be resolved.",
            };
            return PackageNuspecResolution.Error(packageName, message);
        }

        ResolvedPackageCoordinate coordinate = resolved.Coordinate;
        NuGetSourceOptions reportingSources =
            (sourceOptions ?? NuGetSourceOptions.Default) with
            {
                Sources =
                [
                    .. coordinate.Sources.Select(source => source.Url),
                ],
                AdditionalSources = [],
            };
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
                $"Nuspec for package '{packageName}' version "
                + $"'{coordinate.Version}' could not be resolved.");
        }

        return new PackageNuspecResolution(
            packageName,
            coordinate.Version,
            NuspecParser.ParseContent(nuspecXml),
            ErrorMessage: null);
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
        NuspecData? Nuspec,
        string? ErrorMessage)
    {
        public static PackageNuspecResolution Error(
            string packageName,
            string message) =>
            new(packageName, "", Nuspec: null, ErrorMessage: message);
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
    public sealed record Graph(string Title, List<DependencyNode> Dependencies) : PackageDependencyGraphResult;
    public sealed record Empty(string Message) : PackageDependencyGraphResult;
    public sealed record Error(string Message, string? Detail = null) : PackageDependencyGraphResult;
}
