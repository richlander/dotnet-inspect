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
            if (File.Exists(libraryName))
            {
                ownedAssemblySet = await AssemblySetResolver.CollectAsync(
                    httpClient,
                    new AssemblySetRequest { Assemblies = [libraryName], TempDirPrefix = TempDirPrefix },
                    logger.Log);
                WriteDiagnostics(ownedAssemblySet);
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
                    WriteDiagnostics(ownedAssemblySet);
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
                    },
                    logger.Log);
                WriteDiagnostics(ownedAssemblySet);

                assemblyPath = ownedAssemblySet.Assemblies.FirstOrDefault()?.Path;
                if (assemblyPath == null)
                {
                    return new LibraryDependencyGraphResult.Error(
                        $"Could not resolve '{libraryName}' as a file, platform library, or NuGet package.",
                        libraryName);
                }
            }

            var (refs, _) = AssemblyInspector.ExtractReferencesAndCompany(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            if (refs.Count == 0)
                return new LibraryDependencyGraphResult.Empty(assemblyName);

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyName };
            var sourceDir = Path.GetDirectoryName(assemblyPath);
            var refNodes = LibraryMetadataService.BuildTransitiveReferences(
                refs, sourceDir, visited, logger, deduplicate: true);

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
        List<string> tempDirs = [];
        try
        {
            var (packageName, _) = PackageExtractor.ParsePackageReference(packageRef);

            logger.Log($"Resolving package: {packageRef}");
            var outcome = await PackageExtractor.ExtractPackageAsync(
                httpClient, packageRef, logger.Log,
                sourceOptions: sourceOptions);
            if (!outcome.IsSuccess)
            {
                return new PackageDependencyGraphResult.Error(outcome.ErrorMessage ?? $"Package '{packageRef}' could not be resolved.");
            }

            var extracted = outcome.Result!;
            if (extracted.TempDir != null)
                tempDirs.Add(extracted.TempDir);

            var version = extracted.Version ?? "";

            var nuspec = NuspecParser.FindAndParse(extracted.ExtractPath);
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
                httpClient, group.Dependencies, tfm, globalSeen, logger.Log);

            return new PackageDependencyGraphResult.Graph($"{packageName} ({version})", depNodes);
        }
        finally
        {
            CleanupTempDirs(tempDirs);
        }
    }

    private static async Task<TResult> WithAssemblySetAsync<TResult>(
        HttpClient httpClient,
        AssemblySetRequest request,
        VerboseLogger logger,
        Func<AssemblySet, TResult> operation)
    {
        using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
        WriteDiagnostics(assemblySet);
        return operation(assemblySet);
    }

    private static void WriteDiagnostics(AssemblySet assemblySet)
    {
        foreach (var diagnostic in assemblySet.Diagnostics)
            Console.Error.WriteLine($"Warning: {diagnostic.Message}");
    }

    private static void CleanupTempDirs(List<string> tempDirs)
    {
        foreach (var dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

internal abstract record LibraryDependencyGraphResult
{
    public sealed record Graph(string AssemblyName, List<AssemblyReferenceNode> References) : LibraryDependencyGraphResult;
    public sealed record Empty(string AssemblyName) : LibraryDependencyGraphResult;
    public sealed record Error(string Message, string? HintInput = null) : LibraryDependencyGraphResult;
}

internal abstract record PackageDependencyGraphResult
{
    public sealed record Graph(string Title, List<DependencyNode> Dependencies) : PackageDependencyGraphResult;
    public sealed record Empty(string Message) : PackageDependencyGraphResult;
    public sealed record Error(string Message, string? Detail = null) : PackageDependencyGraphResult;
}
