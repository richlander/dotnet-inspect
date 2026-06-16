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
        return AssemblyCollector.WithAssembliesAsync(
            httpClient,
            options,
            logger,
            TempDirPrefix,
            assemblyInfos =>
            {
                logger.Log($"Scanning {assemblyInfos.Count} libraries for type {options.TargetType}");
                var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
                return TypeDependencyScanner.BuildDependencyTree(options.TargetType, assemblyPaths);
            });
    }

    public static async Task<LibraryDependencyGraphResult> BuildLibraryDependencyTreeAsync(
        HttpClient httpClient,
        string libraryName,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger)
    {
        List<string> tempDirs = [];
        try
        {
            string? assemblyPath = null;

            if (File.Exists(libraryName))
            {
                assemblyPath = libraryName;
            }
            else if (PlatformResolver.IsPlatformCandidate(libraryName))
            {
                var (resolved, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                    libraryName, httpClient, logger.Log);
                if (error == null && resolved != null)
                    assemblyPath = resolved;
            }

            if (assemblyPath == null)
            {
                logger.Log($"Resolving package: {libraryName}");
                var outcome = await PackageExtractor.ExtractPackageAsync(
                    httpClient, libraryName, logger.Log,
                    sourceOptions: sourceOptions);
                if (!outcome.IsSuccess)
                {
                    return new LibraryDependencyGraphResult.Error(
                        $"Could not resolve '{libraryName}' as a file, platform library, or NuGet package.",
                        libraryName);
                }

                var extracted = outcome.Result!;
                if (extracted.TempDir != null)
                    tempDirs.Add(extracted.TempDir);

                var dllFiles = Directory.GetFiles(extracted.ExtractPath, "*.dll", SearchOption.AllDirectories)
                    .Where(f => f.Contains("/lib/") || f.Contains("\\lib\\"))
                    .OrderByDescending(f => f)
                    .ToArray();
                if (dllFiles.Length == 0)
                {
                    return new LibraryDependencyGraphResult.Error($"No libraries found in package '{libraryName}'.");
                }

                assemblyPath = dllFiles[0];
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
            AssemblyCollector.CleanupTempDirs(tempDirs);
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

            string[] nuspecFiles = Directory.GetFiles(extracted.ExtractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
            if (nuspecFiles.Length == 0)
                return new PackageDependencyGraphResult.Empty("No dependencies declared in package.");

            var nuspec = NuspecParser.Parse(nuspecFiles[0]);
            if (nuspec.DependencyGroups is not { Count: > 0 })
                return new PackageDependencyGraphResult.Empty("No dependencies declared in package.");

            var tfm = requestedTfm;
            DependencyGroup? group;
            if (!string.IsNullOrEmpty(tfm))
            {
                group = DependencyResolutionService.FindBestMatchingTfmGroup(nuspec.DependencyGroups, tfm);
                if (group == null)
                {
                    return new PackageDependencyGraphResult.Error(
                        $"No dependencies found for TFM '{tfm}'.",
                        "Available TFMs: " + string.Join(", ",
                            nuspec.DependencyGroups.Select(g => g.TargetFramework)));
                }
            }
            else
            {
                group = nuspec.DependencyGroups
                    .OrderByDescending(g => TfmResolver.GetTfmPriority(g.TargetFramework))
                    .First();
                tfm = group.TargetFramework;
            }

            if (group.Dependencies.Count == 0)
                return new PackageDependencyGraphResult.Empty($"No additional dependencies for {tfm}.");

            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var depNodes = await DependencyResolutionService.ResolveDependencyTreeAsync(
                httpClient, group.Dependencies, tfm, globalSeen, logger.Log);

            return new PackageDependencyGraphResult.Graph($"{packageName} ({version})", depNodes);
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
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
