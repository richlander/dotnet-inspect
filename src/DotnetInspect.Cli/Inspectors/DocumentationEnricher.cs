using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Models;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Direct;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Installed;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Sections.Installed;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Artifacts.Local;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

internal static class DocumentationEnricher
{
    private static readonly ApiSurfaceExtractionBounds s_apiSurfaceBounds =
        new(
            maxTypes: 100_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 1_024,
            maxTypeForwarders: 100_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 32_000_000);

    private static readonly DocumentationHouseLimits s_documentationLimits =
        new(
            maximumCompiledXmlContributions: 1,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);

    private static readonly DocumentationHouseLimits
        s_monolithicPlatformDocumentationLimits =
            new(
                maximumCompiledXmlContributions: 1,
                maximumCompiledXmlBytes: 32 * 1024 * 1024,
                XmlDocumentationReadLimits.Default);

    internal static async Task EnrichAsync(
        IEnumerable<ApiType> types,
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded,
        ApiOptions options,
        HttpClient httpClient,
        bool includeMembers = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        ApiType[] requestedTypes = [.. types];
        if (requestedTypes.Length == 0)
            return;

        bool isPlatform =
            source.ApiSource == SourceKind.Platform
            || !string.IsNullOrEmpty(options.PlatformAssembly);
        PlatformFamily platformFamily = default;
        bool hasPlatformFamily =
            isPlatform
            && TryGetPlatformFamily(
                source.PlatformFramework,
                out platformFamily);
        ResolvedAssemblyReference? selectedPlatformAssembly =
            isPlatform
                ? TryResolvePlatformReferenceAssembly(source, options)
                : null;
        if (isPlatform && selectedPlatformAssembly is null)
            return;
        ResolvedAssemblyReference? documentationPlatformAssembly =
            isPlatform && !hasPlatformFamily
                ? TryResolveNetStandardContractAssembly(
                    source,
                    selectedPlatformAssembly!)
                : selectedPlatformAssembly;
        if (isPlatform && documentationPlatformAssembly is null)
            return;

        foreach (IGrouping<string, ApiType> group in requestedTypes.GroupBy(
                     type => isPlatform
                         ? documentationPlatformAssembly!.Path!
                         : loaded.TryGetSourceAssembly(type)?.Path
                             ?? loaded.ApiDllPath,
                     PathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentationTargetSet targets =
                DocumentationTargetSet.Create(group, includeMembers);
            if (targets.Ids.Count == 0)
                continue;

            ResolvedAssemblyReference? assembly = isPlatform
                ? documentationPlatformAssembly
                : loaded.TryGetSourceAssembly(group.First());
            ResolvedAssemblyReference? platformAssembly =
                hasPlatformFamily
                    ? selectedPlatformAssembly
                    : null;
            bool hasCompiledDocumentationEvidence;
            if (platformAssembly is not null)
            {
                IReadOnlyDictionary<string, CompiledDocumentationOutcome>
                    outcomes = await QueryPlatformAsync(
                        source,
                        platformAssembly.Path
                            ?? throw new InvalidOperationException(
                                "The selected platform reference assembly "
                                    + "has no local path."),
                        platformAssembly.Identity,
                        platformFamily,
                        targets.Ids,
                        options,
                        cancellationToken)
                    .ConfigureAwait(false);
                ApplyOutcomes(
                    targets,
                    outcomes,
                    source.Context.Logger);
                hasCompiledDocumentationEvidence =
                    outcomes.Values.Any(HasCompiledDocumentationEvidence);
            }
            else
            {
                PackageDocumentationRoute packageRoute =
                    GetPackageDocumentationRoute(source, group.Key);
                if (packageRoute == PackageDocumentationRoute.PackageHouse)
                {
                    IReadOnlyDictionary<string, DocumentationQueryOutcome>
                        outcomes = await QueryPackageAsync(
                            source,
                            group.Key,
                            targets.Ids,
                            options,
                            httpClient,
                            cancellationToken)
                        .ConfigureAwait(false);
                    ApplyOutcomes(
                        targets,
                        outcomes,
                        source.Context.Logger);
                    hasCompiledDocumentationEvidence =
                        outcomes.Values.Any(
                            HasCompiledDocumentationEvidence);
                }
                else if (packageRoute
                    == PackageDocumentationRoute.UnsupportedPackageAsset)
                {
                    source.Context.Logger.Log(
                        "Compiled documentation requires an exact selected "
                            + $"compile asset; skipping '{group.Key}'.");
                    continue;
                }
                else
                {
                    if (assembly is null
                        && !ResolvedAssemblyReference.TryCreateFromPath(
                            group.Key,
                            AssemblyResolutionProvenance.Local(
                                "compiled documentation"),
                            out assembly))
                    {
                        source.Context.Logger.Log(
                            "Compiled documentation requires an assembly "
                                + $"descriptor; skipping '{group.Key}'.");
                        continue;
                    }
                    IReadOnlyDictionary<string, CompiledDocumentationOutcome>
                        outcomes = await QueryDirectLibraryAsync(
                            group.Key,
                            assembly.Identity,
                            targets.Ids,
                            options.IncludeAll
                                ? ApiSurfaceExtractionScope.IncludeAll
                                : ApiSurfaceExtractionScope
                                    .PublicWithNonPublicTypes,
                            isPlatform
                                ? s_monolithicPlatformDocumentationLimits
                                : s_documentationLimits,
                            requireAllSubjects: !isPlatform,
                            cancellationToken)
                        .ConfigureAwait(false);
                    ApplyOutcomes(
                        targets,
                        outcomes,
                        source.Context.Logger);
                    hasCompiledDocumentationEvidence =
                        outcomes.Values.Any(
                            HasCompiledDocumentationEvidence);
                }
            }

            if (hasCompiledDocumentationEvidence)
            {
                foreach (ApiType type in group)
                    type.SourceResolution = "XmlDoc";
            }
        }
    }

    private static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        QueryPlatformAsync(
            ApiSourceResult source,
            string assemblyPath,
            AssemblyReferenceIdentity assemblyIdentity,
            PlatformFamily family,
            IReadOnlyCollection<string> documentationIds,
            ApiOptions options,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ApiVersion)
            || string.IsNullOrWhiteSpace(source.SelectedTfm))
        {
            throw new InvalidOperationException(
                "Platform compiled documentation requires an exact platform "
                    + "version and target framework.");
        }

        if (!TryGetPlatformReferenceLocation(
                assemblyPath,
                source.PlatformFramework!,
                source.ApiVersion,
                source.SelectedTfm,
                out PlatformReferenceLocation location))
        {
            throw new InvalidOperationException(
                $"'{assemblyPath}' is not in the exact selected reference "
                    + $"pack {source.PlatformFramework} {source.ApiVersion} "
                    + $"for {source.SelectedTfm}.");
        }
        var target = new PlatformFamilyTarget(
            family,
            PlatformTargetFramework.Parse(source.SelectedTfm),
            PlatformVersion.Parse(source.ApiVersion));

        return location.IsPackageBacked
            ? await QueryPackagePlatformAsync(
                    source,
                    target,
                    assemblyIdentity,
                    documentationIds,
                    options,
                    cancellationToken)
                .ConfigureAwait(false)
            : await QueryInstalledPlatformAsync(
                    target,
                    assemblyIdentity,
                    location.DotnetRoot!,
                    documentationIds,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        QueryInstalledPlatformAsync(
            PlatformFamilyTarget target,
            AssemblyReferenceIdentity assemblyIdentity,
            string dotnetRoot,
            IReadOnlyCollection<string> documentationIds,
            ApiOptions options,
            CancellationToken cancellationToken)
    {
        InstalledDotnetHiveIdentity hive =
            InstalledDotnetHiveIdentity.Create(
                "cli-platform-documentation");
        var adapter = new InstalledPlatformHouseAdapter(
            new InstalledReferencePackSource(hive, dotnetRoot),
            new InstalledImplementationPlatformSource(hive, dotnetRoot),
            "cli-platform-documentation");
        var request =
            new PlatformCompiledDocumentationInspectionRequest(
                target,
                assemblyIdentity,
                documentationIds,
                PlatformCompiledDocumentationSubjectSelection
                    .AvailableOnly);
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome> envelope =
                await InstalledPlatformCompiledDocumentationInspection
                    .ExecuteAsync(
                        request,
                        adapter,
                        CreatePlatformDocumentationWork(
                            TimeSpan.FromSeconds(30)),
                        CreatePlatformDocumentationQueryLimits(options),
                        cancellationToken)
                    .ConfigureAwait(false);
        return GetPlatformDocumentationOutcomes(
            envelope,
            "Installed");
    }

    private static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        QueryPackagePlatformAsync(
            ApiSourceResult source,
            PlatformFamilyTarget target,
            AssemblyReferenceIdentity assemblyIdentity,
            IReadOnlyCollection<string> documentationIds,
            ApiOptions options,
            CancellationToken cancellationToken)
    {
        await using var packageRuntime =
            new DesktopPlatformPackageSourceRuntime(
                source.Context.CreatePackageSourceComposition,
                options.SourceOptions,
                "inspect-cli-docs");
        PackagePlatformHouseAdapter adapter =
            packageRuntime.CreateAdapter(
                "cli-platform-documentation");
        var request =
            new PlatformCompiledDocumentationInspectionRequest(
                target,
                assemblyIdentity,
                documentationIds,
                PlatformCompiledDocumentationSubjectSelection
                    .AvailableOnly);
        InspectionEnvelope<
            PlatformCompiledDocumentationInspectionOutcome> envelope =
                await PlatformCompiledDocumentationInspection
                    .ExecutePackageBackedAsync(
                        request,
                        adapter,
                        packageRuntime.IssueOperation(
                            cancellationToken),
                        CreatePlatformDocumentationWork(
                            TimeSpan.FromMinutes(10)),
                        CreatePlatformDocumentationQueryLimits(
                            options),
                        cancellationToken)
                    .ConfigureAwait(false);
        return GetPlatformDocumentationOutcomes(
            envelope,
            "Package-backed");
    }

    private static IReadOnlyDictionary<
        string,
        CompiledDocumentationOutcome> GetPlatformDocumentationOutcomes(
            InspectionEnvelope<
                PlatformCompiledDocumentationInspectionOutcome> envelope,
            string sourceDescription) =>
        envelope.Content switch
        {
            PlatformCompiledDocumentationInspectionOutcome.Completed
                completed =>
                completed.Document.Outcomes.ToDictionary(
                    static outcome =>
                        outcome.Subject.DocumentationId,
                    StringComparer.Ordinal),
            PlatformCompiledDocumentationInspectionOutcome.NotAvailable
                notAvailable =>
                throw new InvalidOperationException(
                    $"{sourceDescription} Platform compiled documentation "
                        + "could "
                        + $"not be settled at "
                        + $"{notAvailable.Failure.Stage}: "
                        + notAvailable.Failure.Summary),
            _ => throw new InvalidOperationException(
                "Unknown Platform compiled-documentation inspection outcome."),
        };

    private static PlatformHouseWorkBudget
        CreatePlatformDocumentationWork(
            TimeSpan maximumDuration) =>
        new(
            maxSourceOperations: 1,
            maxTargetCandidates: 0,
            maxAssemblies: 1,
            maxXmlDocuments: 1,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 520L * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: maximumDuration);

    private static PlatformCompiledDocumentationQueryLimits
        CreatePlatformDocumentationQueryLimits(
            ApiOptions options) =>
        new()
        {
            ApiSurface = s_apiSurfaceBounds,
            ApiSurfaceScope = options.IncludeAll
                ? ApiSurfaceExtractionScope.IncludeAll
                : ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
            Documentation = s_documentationLimits,
        };

    private static bool TryGetPlatformFamily(
        string? framework,
        out PlatformFamily family)
    {
        if (string.Equals(
                framework,
                "runtime",
                StringComparison.OrdinalIgnoreCase))
        {
            family = PlatformFamily.DotNetRuntime;
            return true;
        }
        if (string.Equals(
                framework,
                "aspnetcore",
                StringComparison.OrdinalIgnoreCase))
        {
            family = PlatformFamily.AspNetCore;
            return true;
        }

        family = default;
        return false;
    }

    private static ResolvedAssemblyReference?
        TryResolvePlatformReferenceAssembly(
            ApiSourceResult source,
            ApiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PlatformAssembly)
            || string.IsNullOrWhiteSpace(source.PlatformFramework)
            || string.IsNullOrWhiteSpace(source.ApiVersion))
        {
            throw new InvalidOperationException(
                "Platform compiled documentation requires the exact selected "
                    + "reference assembly, framework, and version.");
        }

        string frameworkSpec =
            $"{source.PlatformFramework}@{source.ApiVersion}";
        var (path, _, _, error) = PlatformResolver.ResolveAssembly(
            options.PlatformAssembly,
            frameworkSpec);
        if (path is null || error is not null)
        {
            source.Context.Logger.Log(
                $"No reference assembly was selected for "
                    + $"'{options.PlatformAssembly}' compiled documentation: "
                    + $"{error ?? "the assembly is absent"}.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(source.SelectedTfm)
            || !TryGetPlatformReferenceLocation(
                path,
                source.PlatformFramework,
                source.ApiVersion,
                source.SelectedTfm,
                out _))
        {
            source.Context.Logger.Log(
                $"'{options.PlatformAssembly}' has no exact selected "
                    + "reference-pack Library for compiled documentation.");
            return null;
        }

        return ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Platform(
                source.PlatformFramework!,
                source.ApiVersion,
                "CLI compiled documentation"));
    }

    private static ResolvedAssemblyReference?
        TryResolveNetStandardContractAssembly(
            ApiSourceResult source,
            ResolvedAssemblyReference selectedAssembly)
    {
        string path = Path.Combine(
            Path.GetDirectoryName(selectedAssembly.Path!)!,
            "netstandard.dll");
        if (!File.Exists(path))
        {
            source.Context.Logger.Log(
                "The selected netstandard reference pack has no "
                    + "monolithic contract Library.");
            return null;
        }

        return ResolvedAssemblyReference.CreateFromPath(
            path,
            AssemblyResolutionProvenance.Platform(
                source.PlatformFramework!,
                source.ApiVersion,
                "CLI compiled documentation"));
    }

    internal static bool TryGetPlatformReferenceLocation(
        string assemblyPath,
        string framework,
        string version,
        string targetFramework,
        out PlatformReferenceLocation location)
    {
        location = null!;
        if (!PlatformResolver.FrameworkMappings.TryGetValue(
                framework,
                out string? packName))
        {
            return false;
        }
        if (string.Equals(
                framework,
                "netstandard",
                StringComparison.OrdinalIgnoreCase)
            && targetFramework.StartsWith(
                "net",
                StringComparison.OrdinalIgnoreCase)
            && !targetFramework.StartsWith(
                "netstandard",
                StringComparison.OrdinalIgnoreCase))
        {
            targetFramework = $"netstandard{targetFramework[3..]}";
        }

        string assemblyDirectory =
            Path.GetFullPath(Path.GetDirectoryName(assemblyPath)!);
        foreach (string packsDirectory
            in PlatformResolver.GetAllPacksDirectories())
        {
            string expectedDirectory = Path.GetFullPath(
                Path.Combine(
                    packsDirectory,
                    packName,
                    version,
                    "ref",
                    targetFramework));
            if (!PathComparer().Equals(
                    assemblyDirectory,
                    expectedDirectory))
            {
                continue;
            }

            string fullPacksDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(packsDirectory));
            string? cachePacksDirectory =
                PlatformPackService.GetPacksCachePath();
            bool isPackageBacked =
                cachePacksDirectory is not null
                && PathComparer().Equals(
                    fullPacksDirectory,
                    Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(cachePacksDirectory)));
            location = new PlatformReferenceLocation(
                isPackageBacked
                    ? null
                    : Path.GetDirectoryName(fullPacksDirectory)!);
            return true;
        }

        return false;
    }

    internal sealed record PlatformReferenceLocation(string? DotnetRoot)
    {
        internal bool IsPackageBacked => DotnetRoot is null;
    }

    private static PackageDocumentationRoute GetPackageDocumentationRoute(
        ApiSourceResult source,
        string assemblyPath)
    {
        if (source.ApiSource != SourceKind.NuGet
            || source.PackageExtractPath is null
            || source.PackageAuthority is null)
        {
            return PackageDocumentationRoute.DirectLibrary;
        }

        string relative = Path.GetRelativePath(
            source.PackageExtractPath,
            assemblyPath);
        if (relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return PackageDocumentationRoute.DirectLibrary;
        }

        if (string.IsNullOrWhiteSpace(source.PackageName)
            || string.IsNullOrWhiteSpace(source.PackageVersion)
            || string.IsNullOrWhiteSpace(source.SelectedTfm)
            || string.IsNullOrWhiteSpace(source.PackageProducerKey))
        {
            return PackageDocumentationRoute.UnsupportedPackageAsset;
        }

        var content = new FileSystemPackageContent(
            source.PackageExtractPath,
            nupkgPath: null,
            fromCache: true,
            source.PackageProducerKey);
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(
                content,
                source.PackageName,
                source.SelectedTfm);
        if (!selection.IsSelected)
            return PackageDocumentationRoute.UnsupportedPackageAsset;

        string selectedPath = Path.GetFullPath(assemblyPath);
        return selection.Assets.Any(asset =>
                PathComparer().Equals(
                    AssetPath(source.PackageExtractPath, asset),
                    selectedPath))
            ? PackageDocumentationRoute.PackageHouse
            : PackageDocumentationRoute.UnsupportedPackageAsset;
    }

    private static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        QueryPackageAsync(
            ApiSourceResult source,
            string assemblyPath,
            IReadOnlyCollection<string> documentationIds,
            ApiOptions options,
            HttpClient httpClient,
            CancellationToken cancellationToken)
    {
        using var stores = new DesktopPackageStoreScope(
            "inspect-cli-docs");
        await using DesktopPackageSourceComposition composition =
            source.Context.CreatePackageSourceComposition();
        PackageHouseSettlement settlement =
            await composition.RealizePinnedCompileAsync(
                    PackageSourceCoordinate.Create(
                        source.PackageName!,
                        source.PackageVersion!),
                    source.SelectedTfm!,
                    stores.Get,
                    options.SourceOptions,
                    source.PackageProducerKey,
                    source.Context.Logger.Log,
                    cancellationToken)
                .ConfigureAwait(false);
        if (settlement is not PackageHouseSettlement.Acquired acquired
            || settlement.Result is not PackageHouseResult.Settled
            || settlement.Result.Evidence.Realization
                is not PackageHouseRealizationReceipt.Compile realization)
        {
            throw new InvalidOperationException(
                $"PackageHouse could not realize {source.PackageName} "
                    + $"{source.PackageVersion} for {source.SelectedTfm} "
                    + $"({DescribePackageHouseResult(settlement.Result)}).");
        }

        PackageCompileAsset? asset =
            realization.Selection.Assets.FirstOrDefault(
                candidate => PathComparer().Equals(
                    AssetPath(source.PackageExtractPath!, candidate),
                    Path.GetFullPath(assemblyPath)));
        if (asset is null)
        {
            throw new InvalidOperationException(
                $"'{assemblyPath}' is not a selected compile assembly of "
                    + $"{source.PackageName} {source.PackageVersion}.");
        }

        PackageHouseLibraryHandoff.Compile handoff =
            realization.LibraryHandoffs
                .OfType<PackageHouseLibraryHandoff.Compile>()
                .Single(candidate => ReferenceEquals(candidate.Asset, asset));
        bool includeAuthoredSource =
            AuthorizesAuthoredDocumentation(options);
        AssemblyContextSourceQueryContext? sourceContext =
            includeAuthoredSource
                ? new(
                    httpClient,
                    FileSystemPdbStore.CreateDefault(),
                    new SourcePolicyPackageSourceAuthorization(
                        options.SourceOptions),
                    new SourceFetch(
                        DotnetInspector.Networking.HttpClientFactory
                            .SharedUntrustedFetch))
                {
                    RepositoryPaths = options.SourceRepositories,
                    NuGetSourceOptions = options.SourceOptions,
                    PdbFallbackPackage = new(
                        source.PackageName!,
                        source.PackageVersion!),
                    AllowLocalSourceReads = true,
                    AllowAdjacentPdbReads = true,
                    Log = source.Context.Logger.Log,
                }
                : null;
        InspectionEnvelope<
            IReadOnlyDictionary<string, DocumentationQueryOutcome>>
            inspection =
                await PackageDocumentationInspection.ExecuteManyAsync(
                    acquired,
                    handoff,
                    documentationIds,
                    includeAuthoredSource
                        ? DocumentationDemand
                            .CompiledXmlAndAuthoredSourceDocumentation
                        : DocumentationDemand.CompiledXml,
                    sourceContext,
                    new PackageDocumentationQueryLimits
                    {
                        ApiSurface = s_apiSurfaceBounds,
                        ApiSurfaceScope = options.IncludeAll
                            ? ApiSurfaceExtractionScope.IncludeAll
                            : ApiSurfaceExtractionScope
                                .PublicWithNonPublicTypes,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        return inspection.Content;
    }

    internal static bool AuthorizesAuthoredDocumentation(
        ApiOptions options) =>
        options.ShowSamples
        || options.DocsExplicitlySet && options.ShowDocs
        || options.VerbosityExplicitlySet
            && options.UserVerbosity == Verbosity.Detailed;

    private static async ValueTask<
        IReadOnlyDictionary<string, CompiledDocumentationOutcome>>
        QueryDirectLibraryAsync(
            string assemblyPath,
            AssemblyReferenceIdentity assemblyIdentity,
            IReadOnlyCollection<string> documentationIds,
            ApiSurfaceExtractionScope apiSurfaceScope,
            DocumentationHouseLimits documentationLimits,
            bool requireAllSubjects,
            CancellationToken cancellationToken)
    {
            string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        bool hasXml = File.Exists(xmlPath);
        await using var artifacts = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = hasXml ? 2 : 1,
                MaxArtifactBytes = 512L * 1024 * 1024,
                MaxRetainedBytes = 512L * 1024 * 1024,
            });
        await artifacts.AddRequiredAcquisitionAsync(
            (scope, token) => LocalArtifactSource.AcquireFileAsync(
                scope,
                assemblyPath,
                cancellationToken: token),
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);
        if (hasXml)
        {
            await artifacts.AddRequiredAcquisitionAsync(
                (scope, token) => LocalArtifactSource.AcquireFileAsync(
                    scope,
                    xmlPath,
                    cancellationToken: token),
                [ArtifactWorkspaceRole.CallerDesignated],
                cancellationToken);
        }

        ArtifactSetPublicationOutcome publication =
            await artifacts.SealAsync(cancellationToken)
                .ConfigureAwait(false);
        if (publication is ArtifactSetPublicationOutcome.NotPublished rejected)
        {
            throw new InvalidOperationException(
                "The direct Library could not be published: "
                    + string.Join(
                        "; ",
                        rejected.Failures.Select(
                            failure => failure.Diagnostic.Summary)));
        }

        ArtifactQueryAuthorization authorization =
            artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease queryLease =
            artifacts.IssueLease(authorization);
        ArtifactDescriptor[] catalog =
            [.. artifacts.GetCatalog(queryLease)];
        ArtifactContentReference assemblyContent =
            artifacts.GetContentReference(
                catalog[0].Identity,
                queryLease);
        ArtifactContentReference? xmlContent = hasXml
            ? artifacts.GetContentReference(
                catalog[1].Identity,
                queryLease)
            : null;
        var identity =
            new ManagedMetadataIdentity.Assembly(assemblyIdentity);
        LibraryReference library =
            LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    assemblyContent,
                    identity,
                    assemblyContent,
                    identity),
                xmlContent is null
                    ? null
                    :
                    [
                        new LibraryCompanionCorrespondence(
                            xmlContent,
                            LibraryContentRole.CompiledXmlDocumentation,
                            assemblyContent),
                    ]);
        List<ArtifactContentLease> contentLeases =
        [
            artifacts.IssueContentLease(
                assemblyContent,
                queryLease),
        ];
        if (xmlContent is not null)
        {
            contentLeases.Add(
                artifacts.IssueContentLease(
                    xmlContent,
                    queryLease));
        }

        await using LibraryContentOwner owner =
            CreateContentOwner(library, contentLeases);
        using LibraryOperationLease inspectionOperation =
            IssueOperation(owner, library);
        LibraryApiSurfaceInspectionOutcome inspection =
            LibraryApiSurfaceInspection.Execute(
                new(
                    library,
                    apiSurfaceScope,
                    s_apiSurfaceBounds),
                inspectionOperation,
                cancellationToken);
        if (inspection
            is not LibraryApiSurfaceInspectionOutcome.Completed completed)
        {
            throw new InvalidOperationException(
                $"The direct Library API surface could not be inspected "
                    + $"({inspection.GetType().Name}).");
        }

        IReadOnlyDictionary<string, DocumentationSubjectReference> subjects =
            ResolveSubjects(
                completed.Correspondence,
                documentationIds,
                requireAllSubjects);
        var outcomes =
            new Dictionary<string, CompiledDocumentationOutcome>(
                documentationIds.Count,
                StringComparer.Ordinal);
        var requests =
            new List<DocumentationHouseRequest>(
                documentationIds.Count);
        string[] resolvedIds =
            [.. documentationIds.Where(subjects.ContainsKey)];
        foreach (string documentationId in resolvedIds)
        {
            DocumentationSubjectReference subject =
                subjects[documentationId];
            IReadOnlyList<CompiledXmlContribution> contributions =
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(
                        library,
                        subject);
            var plan = new DocumentationHouseOperationPlan(
                DocumentationHouseOperationPlanIdentity.Create(
                    "cli-direct-compiled-documentation"),
                DocumentationHousePolicyGeneration.Create(
                    "cli-direct-compiled-documentation-v1"),
                documentationLimits,
                DateTimeOffset.UtcNow.AddSeconds(10),
                contributions);
            var request = new DocumentationHouseRequest(
                DocumentationHouseRequestIdentity.Create(
                    "cli-direct-compiled-documentation"),
                subject,
                DocumentationDemand.CompiledXml,
                plan);
            requests.Add(request);
        }
        using LibraryOperationLease operation =
            IssueOperation(owner, library);
        IReadOnlyList<CompiledDocumentationQueryResult> results =
            await CompiledDocumentationQuery.ExecuteManyAsync(
                    requests,
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        int index = 0;
        foreach (string documentationId in resolvedIds)
        {
            outcomes.Add(
                documentationId,
                results[index].Content);
            index++;
        }
        return outcomes;
    }

    private static LibraryContentOwner CreateContentOwner(
        LibraryReference library,
        IReadOnlyList<ArtifactContentLease> contentLeases)
    {
        try
        {
            return new LibraryContentOwner(library, contentLeases);
        }
        catch
        {
            foreach (ArtifactContentLease contentLease in contentLeases)
                contentLease.Dispose();
            throw;
        }
    }

    private static IReadOnlyDictionary<
        string,
        DocumentationSubjectReference> ResolveSubjects(
        LibraryApiSurfaceCorrespondence correspondence,
        IReadOnlyCollection<string> documentationIds,
        bool requireAllSubjects)
    {
        var requested =
            new HashSet<string>(documentationIds, StringComparer.Ordinal);
        var subjects =
            new Dictionary<string, DocumentationSubjectReference>(
                requested.Count,
                StringComparer.Ordinal);
        foreach (ApiType type in correspondence.Surface.Types)
        {
            if (ApiMemberIdentity.TryGetXmlDocTypeIdentity(
                    type,
                    out XmlDocMemberIdentity typeIdentity)
                && requested.Contains(typeIdentity.Value))
            {
                Add(
                    typeIdentity.Value,
                    DocumentationSubjectReference.ForType(
                        correspondence,
                        type));
            }

            foreach (ApiMember member in type.Members)
            {
                if (member.DeclaringTypeDefinitionName is not null
                    || !ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        type,
                        member,
                        out XmlDocMemberIdentity memberIdentity)
                    || !requested.Contains(memberIdentity.Value))
                {
                    continue;
                }
                Add(
                    memberIdentity.Value,
                    DocumentationSubjectReference.ForMember(
                        correspondence,
                        type,
                        member));
            }
        }

        string? missing = requireAllSubjects
            ? requested.FirstOrDefault(id => !subjects.ContainsKey(id))
            : null;
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"The direct Library has no subject '{missing}'.");
        }
        return subjects;

        void Add(
            string documentationId,
            DocumentationSubjectReference subject)
        {
            if (!subjects.TryAdd(documentationId, subject))
            {
                throw new InvalidOperationException(
                    $"The direct Library has multiple subjects "
                        + $"'{documentationId}'.");
            }
        }
    }

    private static void ApplyOutcomes(
        DocumentationTargetSet targets,
        IReadOnlyDictionary<string, CompiledDocumentationOutcome> outcomes,
        VerboseLogger logger)
    {
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string documentationId, CompiledDocumentationOutcome outcome)
            in outcomes)
        {
            if (outcome is CompiledDocumentationOutcome.Available available)
            {
                targets.Apply(
                    documentationId,
                    CreateDocComment(available.Documentation));
                continue;
            }

            if (outcome is CompiledDocumentationOutcome.Absent)
                continue;
            if (outcome is CompiledDocumentationOutcome.Unavailable)
            {
                logger.Log(
                    $"Compiled documentation is unavailable for "
                        + $"'{documentationId}'.");
                continue;
            }

            string reason = DescribeFailure(outcome);
            failures[reason] =
                failures.TryGetValue(reason, out int count)
                    ? count + 1
                    : 1;
        }

        foreach ((string reason, int count) in failures)
        {
            CommandError.WriteWarning(
                count == 1
                    ? $"Compiled documentation {reason}."
                    : $"Compiled documentation {reason} for {count} subjects.");
        }
    }

    private static void ApplyOutcomes(
        DocumentationTargetSet targets,
        IReadOnlyDictionary<string, DocumentationQueryOutcome> outcomes,
        VerboseLogger logger)
    {
        var compiledFailures =
            new Dictionary<string, int>(StringComparer.Ordinal);
        var authoredFailures =
            new Dictionary<string, int>(StringComparer.Ordinal);
        var settlementFailures =
            new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string documentationId, DocumentationQueryOutcome outcome)
            in outcomes)
        {
            if (outcome is DocumentationQueryOutcome.Completed completed)
            {
                targets.Apply(
                    documentationId,
                    CreateDocComment(completed.Fields));
                if (completed.CompiledXml is { } compiled)
                {
                    DescribeCompiledNonSuccess(
                        documentationId,
                        compiled,
                        logger,
                        compiledFailures);
                }
                DescribeAuthoredNonSuccess(
                    documentationId,
                    completed.AuthoredSource,
                    logger,
                    authoredFailures);
                continue;
            }

            string reason = outcome switch
            {
                DocumentationQueryOutcome.RequestRejected rejected =>
                    $"request was rejected ({rejected.Reason})",
                DocumentationQueryOutcome.Failed failed =>
                    $"failed ({failed.Reason})",
                DocumentationQueryOutcome.Incomplete incomplete =>
                    $"was incomplete ({incomplete.Reason})",
                _ => $"failed ({outcome.GetType().Name})",
            };
            AddFailure(settlementFailures, reason);
        }

        WriteFailures("Compiled documentation", compiledFailures);
        WriteFailures("Authored documentation", authoredFailures);
        WriteFailures("Documentation settlement", settlementFailures);
    }

    private static DocComment CreateDocComment(
        CompiledDocumentationEntry documentation) =>
        new()
        {
            Summary = documentation.Summary,
            Remarks = documentation.Remarks,
            Returns = documentation.Returns,
            Parameters = documentation.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.Description,
                StringComparer.Ordinal),
            Samples =
            [
                .. documentation.Samples.Select(
                    static sample => new SampleReference
                    {
                        RelativePath = sample.Code,
                        Description = sample.Title,
                        Region = sample.Region,
                    }),
            ],
        };

    private static DocComment CreateDocComment(
        DocumentationQueryFieldSettlement fields) =>
        new()
        {
            Summary = Select(fields.Summary),
            Remarks = Select(fields.Remarks),
            Returns = Select(fields.Returns),
            Parameters = fields.Parameters
                .Select(
                    static parameter => (
                        parameter.Name,
                        Value: Select(parameter.Evidence)))
                .Where(static parameter => parameter.Value is not null)
                .ToDictionary(
                    static parameter => parameter.Name,
                    static parameter => parameter.Value!,
                    StringComparer.Ordinal),
            Samples =
            [
                .. (fields.Samples.Contributions.FirstOrDefault()?.Value
                    ?? [])
                    .Select(
                        static sample => new SampleReference
                        {
                            RelativePath = sample.Code,
                            Description = sample.Title,
                            Region = sample.Region,
                        }),
            ],
        };

    private static string? Select(
        DocumentationQueryTextFieldEvidence field) =>
        field.Contributions.FirstOrDefault()?.Value;

    private static void DescribeAuthoredNonSuccess(
        string documentationId,
        AuthoredDocumentationOutcome? outcome,
        VerboseLogger logger,
        Dictionary<string, int> failures)
    {
        switch (outcome)
        {
            case null:
            case AuthoredDocumentationOutcome.Available:
            case AuthoredDocumentationOutcome.Absent:
                return;
            case AuthoredDocumentationOutcome.Unavailable unavailable:
                logger.Log(
                    $"Authored documentation is unavailable for "
                        + $"'{documentationId}' ({unavailable.Reason}).");
                return;
            case AuthoredDocumentationOutcome.Ambiguous ambiguous:
                AddFailure(
                    failures,
                    $"was ambiguous ({ambiguous.Reason})");
                return;
            case AuthoredDocumentationOutcome.Rejected rejected:
                AddFailure(
                    failures,
                    $"was rejected ({rejected.Reason})");
                return;
            case AuthoredDocumentationOutcome.Failed failed:
                AddFailure(
                    failures,
                    $"failed ({failed.Reason})");
                return;
            case AuthoredDocumentationOutcome.Incomplete incomplete:
                AddFailure(
                    failures,
                    $"was incomplete ({incomplete.Reason})");
                return;
            default:
                AddFailure(
                    failures,
                    $"failed ({outcome.GetType().Name})");
                return;
        }
    }

    private static void DescribeCompiledNonSuccess(
        string documentationId,
        CompiledDocumentationOutcome outcome,
        VerboseLogger logger,
        Dictionary<string, int> failures)
    {
        if (outcome is CompiledDocumentationOutcome.Available
            or CompiledDocumentationOutcome.Absent)
        {
            return;
        }
        if (outcome is CompiledDocumentationOutcome.Unavailable)
        {
            logger.Log(
                $"Compiled documentation is unavailable for "
                    + $"'{documentationId}'.");
            return;
        }

        AddFailure(failures, DescribeFailure(outcome));
    }

    private static void AddFailure(
        Dictionary<string, int> failures,
        string reason) =>
        failures[reason] =
            failures.TryGetValue(reason, out int count)
                ? count + 1
                : 1;

    private static void WriteFailures(
        string channel,
        IReadOnlyDictionary<string, int> failures)
    {
        foreach ((string reason, int count) in failures)
        {
            CommandError.WriteWarning(
                count == 1
                    ? $"{channel} {reason}."
                    : $"{channel} {reason} for {count} subjects.");
        }
    }

    private static bool HasCompiledDocumentationEvidence(
        CompiledDocumentationOutcome outcome) =>
        outcome is CompiledDocumentationOutcome.Available
        || outcome
            is CompiledDocumentationOutcome.Absent absent
            && absent.Sources.Any(
                static source =>
                    source.Kind
                        == CompiledDocumentationSourceEvidenceKind
                            .Candidate);

    private static bool HasCompiledDocumentationEvidence(
        DocumentationQueryOutcome outcome) =>
        outcome is DocumentationQueryOutcome.Completed
        {
            CompiledXml: { } compiled,
        }
        && HasCompiledDocumentationEvidence(compiled);

    private static string DescribeFailure(
        CompiledDocumentationOutcome outcome) =>
        outcome switch
        {
            CompiledDocumentationOutcome.Ambiguous =>
                "was ambiguous",
            CompiledDocumentationOutcome.ContributionsRejected =>
                "contributions were rejected",
            CompiledDocumentationOutcome.MalformedOrUnreadableDocument =>
                "was malformed or unreadable",
            CompiledDocumentationOutcome.Incomplete incomplete =>
                $"was incomplete ({incomplete.Reason})",
            CompiledDocumentationOutcome.RequestRejected rejected =>
                $"request was rejected ({rejected.Reason})",
            CompiledDocumentationOutcome.ContentAccessFailed =>
                "content access failed",
            _ => $"failed ({outcome.GetType().Name})",
        };

    private static string DescribePackageHouseResult(
        PackageHouseResult result) =>
        result switch
        {
            PackageHouseResult.NotFound value =>
                value.Reason.ToString(),
            PackageHouseResult.NoMatch value =>
                value.Reason.ToString(),
            PackageHouseResult.Ambiguous value =>
                value.Reason.ToString(),
            PackageHouseResult.Rejected value =>
                value.Reason.ToString(),
            PackageHouseResult.Unavailable value =>
                value.Reason.ToString(),
            PackageHouseResult.Incomplete value =>
                value.Reason.ToString(),
            _ => result.GetType().Name,
        };

    private static LibraryOperationLease IssueOperation(
        LibraryContentOwner owner,
        LibraryReference library) =>
        owner.IssueOperationLease(library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    $"The direct Library rejected documentation access "
                        + $"({outcome.GetType().Name})."),
        };

    private static string AssetPath(
        string packageRoot,
        PackageCompileAsset asset) =>
        Path.GetFullPath(
            Path.Combine(
                packageRoot,
                asset.Path.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private enum PackageDocumentationRoute
    {
        DirectLibrary,
        PackageHouse,
        UnsupportedPackageAsset,
    }

    private sealed class DocumentationTargetSet
    {
        private readonly Dictionary<string, List<Action<DocComment>>> _targets;

        private DocumentationTargetSet(
            Dictionary<string, List<Action<DocComment>>> targets) =>
            _targets = targets;

        internal IReadOnlyCollection<string> Ids => _targets.Keys;

        internal static DocumentationTargetSet Create(
            IEnumerable<ApiType> types,
            bool includeMembers)
        {
            var targets =
                new Dictionary<string, List<Action<DocComment>>>(
                    StringComparer.Ordinal);
            foreach (ApiType type in types)
            {
                if (ApiMemberIdentity.TryGetXmlDocTypeIdentity(
                        type,
                        out XmlDocMemberIdentity typeIdentity))
                {
                    Add(
                        typeIdentity.Value,
                        documentation => type.Documentation = documentation);
                }
                if (!includeMembers)
                    continue;

                foreach (ApiMember member in type.Members)
                {
                    if (!ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                            type,
                            member,
                            out XmlDocMemberIdentity memberIdentity))
                    {
                        continue;
                    }
                    Add(
                        memberIdentity.Value,
                        documentation =>
                            member.Documentation = documentation);
                }
            }
            return new(targets);

            void Add(
                string documentationId,
                Action<DocComment> apply)
            {
                if (!targets.TryGetValue(
                        documentationId,
                        out List<Action<DocComment>>? applications))
                {
                    applications = [];
                    targets.Add(documentationId, applications);
                }
                applications.Add(apply);
            }
        }

        internal void Apply(
            string documentationId,
            DocComment documentation)
        {
            foreach (Action<DocComment> apply
                in _targets[documentationId])
            {
                apply(documentation);
            }
        }
    }

}
