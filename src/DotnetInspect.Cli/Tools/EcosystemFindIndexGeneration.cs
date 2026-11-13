using System.Collections.Immutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tools;

public sealed record EcosystemFindIndexGenerationRequest(
    string OutputDirectory,
    string TargetFramework,
    int MaximumPackages = 25);

public sealed record EcosystemFindIndexGenerationResult(
    EcosystemPackId Ecosystem,
    string Path,
    int Packages,
    int Libraries,
    int Symbols,
    int Gaps);

public static class EcosystemFindIndexGeneration
{
    public static async Task<IReadOnlyList<EcosystemFindIndexGenerationResult>>
        GenerateAsync(
            HttpClient httpClient,
            EcosystemFindIndexGenerationRequest request,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFramework);
        if (request.MaximumPackages is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(request));

        Directory.CreateDirectory(request.OutputDirectory);
        GenerationTarget[] targets =
        [
            new(
                EcosystemPackIds.Aspire,
                EcosystemIntegrationScanner.GeneralBinding,
                IntegrationConceptCatalog.EcosystemObserved.Concepts,
                ["AddRedis", "AddPostgres", "AddRedisClient"],
                "aspire.json"),
            new(
                EcosystemPackIds.MicrosoftExtensions,
                EcosystemIntegrationScanner.GeneralBinding,
                IntegrationConceptCatalog.EcosystemObserved.Concepts,
                ["AddHttpClient"],
                "microsoft-extensions.json"),
        ];
        var results = new List<EcosystemFindIndexGenerationResult>(
            targets.Length);
        foreach (GenerationTarget target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await GenerateAsync(
                httpClient,
                request,
                target,
                log,
                cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    private static async Task<EcosystemFindIndexGenerationResult>
        GenerateAsync(
            HttpClient httpClient,
            EcosystemFindIndexGenerationRequest request,
            GenerationTarget target,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
        EcosystemWorkspaceRegistrationSelectionResult selection =
            EcosystemPackCatalog.SelectWorkspaceRegistration(target.Ecosystem);
        WorkspaceEcosystemRegistrationDeclaration declaration =
            selection is EcosystemWorkspaceRegistrationSelectionResult.Known known
                ? known.Declaration
                : throw new InvalidOperationException(
                    $"Ecosystem '{target.Ecosystem}' has no Workspace registration.");
        PackagePrefixDeclaration[] prefixes =
        [
            .. declaration.Populations
                .OfType<
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix>()
                .Select(static population => population.Prefix),
        ];
        var sources = new List<EcosystemFindIndexSourceInput>();
        var gaps = new List<EcosystemFindIndexGapInput>();
        int packageCount = 0;
        for (int prefixOrdinal = 0;
            prefixOrdinal < prefixes.Length;
            prefixOrdinal++)
        {
            PackagePrefixDeclaration prefix = prefixes[prefixOrdinal];
            IReadOnlyList<NuGetSearchResult> packages =
                await EnumeratePackagesAsync(
                    httpClient,
                    prefix,
                    prefixOrdinal,
                    request.MaximumPackages,
                    gaps,
                    log,
                    cancellationToken).ConfigureAwait(false);
            packageCount += packages.Count;
            for (int packageOrdinal = 0;
                packageOrdinal < packages.Count;
                packageOrdinal++)
            {
                NuGetSearchResult package = packages[packageOrdinal];
                log?.Invoke(
                    $"Scanning {target.Ecosystem}: "
                    + $"{package.PackageId}@{package.Version}");
                ConfiguredPackageIntegrationScan? scan =
                    await ScanPackageAsync(
                        httpClient,
                        package,
                        request.TargetFramework,
                        target.Binding,
                        log,
                        cancellationToken).ConfigureAwait(false);
                if (scan is null)
                {
                    gaps.Add(new(
                        EcosystemFindIndexGapKind.PackageRejected,
                        prefixOrdinal,
                        new(package.PackageId, package.Version),
                        packageOrdinal,
                        Library: null,
                        LibraryOrdinal: null,
                        "The exact package could not be realized."));
                    continue;
                }

                for (int libraryOrdinal = 0;
                    libraryOrdinal < scan.Sources.Length;
                    libraryOrdinal++)
                {
                    ConfiguredPackageIntegrationSource source =
                        scan.Sources[libraryOrdinal];
                    sources.Add(new(
                        new EcosystemFindIndexSourceCoordinate(
                            prefixOrdinal,
                            packageOrdinal,
                            libraryOrdinal,
                            new(
                                package.PackageId,
                                package.Version),
                            request.TargetFramework,
                            source.Asset.AssemblySimpleName,
                            new(
                                source.Asset.Path,
                                source.Asset.AssemblySimpleName),
                            source.Assembly,
                            source.ModuleVersionId),
                        source.Signals));
                }
                foreach (ConfiguredPackageIntegrationFailure failure
                    in scan.Failures)
                {
                    gaps.Add(new(
                        EcosystemFindIndexGapKind.LibraryFailed,
                        prefixOrdinal,
                        new(package.PackageId, package.Version),
                        packageOrdinal,
                        Library: null,
                        LibraryOrdinal: null,
                        $"{failure.Library}: {failure.Message}"));
                }
            }
        }

        EcosystemFindIndexArtifact artifact =
            EcosystemFindIndexBuilder.Build(new(
                target.Ecosystem,
                target.Concepts,
                prefixes,
                sources,
                gaps));
        EnsureKnownAnswers(artifact, target.KnownAnswers);
        byte[] bytes = EcosystemFindIndexJson.Serialize(artifact);
        string path = Path.Combine(request.OutputDirectory, target.FileName);
        await File.WriteAllBytesAsync(
            path,
            bytes,
            cancellationToken).ConfigureAwait(false);
        return new(
            target.Ecosystem,
            path,
            packageCount,
            artifact.Sources.Length,
            artifact.Symbols.Length,
            artifact.Gaps.Length);
    }

    private static async Task<IReadOnlyList<NuGetSearchResult>>
        EnumeratePackagesAsync(
            HttpClient httpClient,
            PackagePrefixDeclaration prefix,
            int prefixOrdinal,
            int maximumPackages,
            List<EcosystemFindIndexGapInput> gaps,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
        var packages = new List<NuGetSearchResult>(maximumPackages);
        await foreach (NuGetPrefixSearchPage page
            in NuGetSearchService.SearchByPrefixPagesAsync(
                httpClient,
                prefix.Prefix,
                maximumPackages,
                prerelease: false,
                log,
                cancellationToken: cancellationToken))
        {
            if (page.Failure is not null)
            {
                gaps.Add(new(
                    EcosystemFindIndexGapKind.PrefixEnumerationBounded,
                    prefixOrdinal,
                    Package: null,
                    PackageOrdinal: null,
                    Library: null,
                    LibraryOrdinal: null,
                    $"{page.Source}: {page.Failure}"));
            }
            foreach (NuGetSearchResult package in page.Results)
            {
                if (packages.Count == maximumPackages)
                    break;
                packages.Add(package);
            }
            if (page.Completion is { } completion
                && completion is not PrefixSearchCompletion.Complete)
            {
                gaps.Add(new(
                    EcosystemFindIndexGapKind.PrefixEnumerationBounded,
                    prefixOrdinal,
                    Package: null,
                    PackageOrdinal: null,
                    Library: null,
                    LibraryOrdinal: null,
                    $"{page.Source}: {completion}."));
            }
        }
        return packages;
    }

    private static async Task<ConfiguredPackageIntegrationScan?>
        ScanPackageAsync(
            HttpClient httpClient,
            NuGetSearchResult package,
            string targetFramework,
            EcosystemIntegrationScannerBinding binding,
            Action<string>? log,
            CancellationToken cancellationToken)
    {
        var assemblyRequest = new AssemblySetRequest
        {
            Packages = [$"{package.PackageId}@{package.Version}"],
            Tfm = targetFramework,
            TempDirPrefix = "inspect-find-index",
        };
        await using ConfiguredPackageSearchWorkspace? workspace =
            await ConfiguredPackageSearchWorkspace.OpenAsync(
                httpClient,
                assemblyRequest,
                targetFramework,
                log,
                cancellationToken).ConfigureAwait(false);
        return workspace is null
            ? null
            : await workspace.ScanIntegrationsAsync(
                binding,
                cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureKnownAnswers(
        EcosystemFindIndexArtifact artifact,
        IReadOnlyList<string> knownAnswers)
    {
        HashSet<string> memberNames =
        [
            .. artifact.Symbols
                .Where(static symbol =>
                    symbol.Kind is EcosystemFindIndexSymbolKind.Member)
                .Select(static symbol =>
                    symbol.Member!.Member.MemberName),
        ];
        string[] missing =
        [
            .. knownAnswers.Where(answer => !memberNames.Contains(answer)),
        ];
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Generated artifact '{artifact.Ecosystem}' is missing "
                + $"known answers: {string.Join(", ", missing)}.");
        }
    }

    private sealed record GenerationTarget(
        EcosystemPackId Ecosystem,
        EcosystemIntegrationScannerBinding Binding,
        IReadOnlyList<IntegrationConceptDescriptor> Concepts,
        IReadOnlyList<string> KnownAnswers,
        string FileName);
}
