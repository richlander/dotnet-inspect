using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using Markout;
using Markout.Formatting;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public partial class DependsCommand
{
    private const int PackageManifestTraversalBudget = 1_024;
    private const int PackageDeclarationTraversalBudget = 16_384;
    private const string SummarySection = "Summary";
    private static readonly
        InspectionEnvelopeJsonContract<DependencyInspectionContent>
        AssetDependencyJson = new(
            "asset-dependencies",
            1,
            DependencyInspectionJsonContext.Default
                .DependencyInspectionContent);

    public static async Task<int> ExecuteAssetDependsAsync(
        DependsOptions options,
        CancellationToken cancellationToken = default) =>
        await ExecuteAssetDependsAsync(
            options,
            static frameworkSpec =>
                InstalledPlatformPruneSource.Read(frameworkSpec),
            cancellationToken).ConfigureAwait(false);

    internal static async Task<int> ExecuteAssetDependsAsync(
        DependsOptions options,
        Func<string, InstalledPlatformPruneSource.Result> pruneSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pruneSource);

        bool evidenceEnvelopeRequested =
            options.EvidenceEnvelopePath is not null;
        if (DependsShareProjection.ValidateOptions(options)
            is { } shareError)
        {
            CommandError.Write(shareError);
            return 1;
        }
        if (!evidenceEnvelopeRequested
            && options.ShareFormat is not null)
        {
            var shareContext = new CommandContext(options.Verbose);
            return await DependsShareProjection.WriteAsync(
                options,
                shareContext.HttpClient,
                shareContext.Logger,
                cancellationToken).ConfigureAwait(false);
        }

        SectionCatalog<DependsAssetProjection> catalog =
            DependsAssetSections.Catalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.SelectableSectionNames,
            catalog.BareSelectSectionNames,
            catalog.SelectionCategoryMap,
            options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        if (options.Effective && options.Discover is null)
        {
            CommandError.Write("--effective requires -D/--discover.");
            return 1;
        }

        if (options.Discover is { } discover && !options.Effective)
        {
            return DiscoverOutput.Execute(
                discover,
                DependsAssetSections.CreateSchema(),
                DiscoveryOutputRequest.Create(
                    OutputFormatResolver.ResolveStored(
                        options.Format,
                        options.JsonOutput,
                        plainText: false,
                        options.Tabular,
                        options.Tsv,
                        options.Jsonl),
                    options.Tree,
                    options.Tabular,
                    options.NoHeader,
                    (int)options.Verbosity,
                    options),
                sectionCostAnnotations:
                    catalog.Pipeline.GetCostAnnotations(),
                sectionCategories: catalog.SelectionCategoryMap,
                listedCategoryDoors:
                    catalog.Pipeline.GetListedCategoryDoors());
        }

        if (options.Schema)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        HashSet<string> includeSections =
            options.Effective
                ? EffectiveDiscoveryPlanSections(
                    options.Discover!,
                    catalog)
                : catalog.Pipeline.GetCandidateSections(
                    options.Verbosity,
                    selection.Sections,
                    fixedOverview: options.SelectDefault);
        if (options.SelectDefault && IsNetworkFreeAssetHierarchy(options))
            includeSections.Add(DependsAssetSections.DependencyHierarchy);
        DependsAssetRequestPlan plan =
            DependsAssetRequestPlan.FromSections(includeSections);
        if (evidenceEnvelopeRequested)
        {
            plan = plan with
            {
                SupplementalEvidence = true,
            };
        }
        if (!ValidateAssetOptions(
                options,
                selection.Sections,
                includeSections,
                plan.Traversal,
                discoveryMode: options.Effective))
        {
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        try
        {
            DependsShareProjection.AssetSharePreparation? sharePreparation =
                options.ShareFormat is null
                    ? null
                    : await DependsShareProjection.PrepareAssetAsync(
                        options,
                        context.HttpClient,
                        context.Logger,
                        cancellationToken).ConfigureAwait(false);
            CommandContext inspectionContext =
                evidenceEnvelopeRequested
                && sharePreparation is not null
                    ? context.WithVerboseLogging(enabled: false)
                    : context;
            using IDisposable? networkTrafficLogSuppression =
                evidenceEnvelopeRequested
                && sharePreparation is not null
                    ? DotnetInspector.Networking.HttpClientFactory
                        .SuppressNetworkTrafficLogging()
                    : null;
            DependsAssetProjection projection =
                await AcquireAssetProjectionAsync(
                    options,
                    inspectionContext,
                    plan,
                    options.Effective
                        && EffectiveDepth(options) is null
                        ? 1
                        : EffectiveDepth(options),
                    sharePreparation,
                    pruneSource,
                    cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Effective)
            {
                List<string> effective =
                    catalog.Pipeline.GetDiscoverableSections(projection);
                int discoveryExitCode = DiscoverOutput.ExecuteEffective(
                    options.Discover,
                    effective,
                    DependsAssetSections.CreateSchema(),
                    DiscoveryOutputRequest.Create(
                        OutputFormatResolver.ResolveStored(
                            options.Format,
                            options.JsonOutput,
                            plainText: false,
                            options.Tabular,
                            options.Tsv,
                            options.Jsonl),
                        options.Tree,
                        options.Tabular,
                        options.NoHeader,
                        (int)options.Verbosity,
                        options),
                    fullSchema: DependsAssetSections.CreateSchema(),
                    sectionCostAnnotations:
                        catalog.Pipeline.GetCostAnnotations(),
                    sectionCategories: catalog.SelectionCategoryMap,
                    listedCategoryDoors:
                        catalog.Pipeline.GetListedCategoryDoors());
                WriteAssetDiagnostics(projection);
                return Math.Max(
                    discoveryExitCode,
                    AssetExitCode(projection));
            }

            byte[] evidencePayload = [];
            Exception? evidenceSerializationError = null;
            if (evidenceEnvelopeRequested)
            {
                if (projection.Enriched is null)
                {
                    evidenceSerializationError =
                        new InvalidOperationException(
                            "The dependency inspection did not produce its requested evidence.");
                }
                else
                {
                    InspectionEnvelopeOutput.TrySerializeEvidence(
                        projection.Enriched,
                        AssetDependencyJson,
                        DependencyInspectionJsonContext.Default
                            .DependencyInspectionEvidenceDocument,
                        options.CompactJson,
                        out evidencePayload,
                        out evidenceSerializationError);
                }
            }

            bool ordinaryOutputWritten = true;
            if (options.ShareFormat is null)
            {
                if (options.EnvelopeOutput)
                {
                    ordinaryOutputWritten =
                        InspectionEnvelopeOutput.TryWrite(
                            projection.Inspection,
                            AssetDependencyJson,
                            includeEnvelope: true,
                            options.CompactJson,
                            options.OutputPath);
                }
                else
                {
                    OutputDestination.Write(
                        options.OutputPath,
                        options.Rows,
                        output => ordinaryOutputWritten =
                            WriteAssetProjection(
                                projection,
                                options,
                                includeSections,
                                output));
                }
            }
            if (!ordinaryOutputWritten)
            {
                return 1;
            }

            int exitCode = 0;
            if (options.ShareFormat is null)
            {
                WriteAssetDiagnostics(projection);
                exitCode = AssetExitCode(projection);
            }
            if (evidenceEnvelopeRequested)
            {
                string evidencePath = options.EvidenceEnvelopePath!;
                if (evidenceSerializationError is not null)
                {
                    CommandError.Write(
                        $"Evidence envelope serialization failed for '{evidencePath}': {evidenceSerializationError.Message}");
                    exitCode = Math.Max(exitCode, 1);
                }
                else if (!EvidenceEnvelopeOutput.TryPublish(
                        evidencePath,
                        evidencePayload,
                        out Exception? publicationError))
                {
                    CommandError.Write(
                        $"Evidence envelope publication failed for '{evidencePath}': {publicationError!.Message}");
                    exitCode = Math.Max(exitCode, 1);
                }
                else
                {
                    CommandError.WriteLine(
                        $"Evidence envelope: {evidencePath}");
                }
            }

            if (options.ShareFormat is { } shareFormat)
            {
                exitCode = Math.Max(
                    exitCode,
                    DependsShareProjection.WriteAsset(
                        projection.Inspection.Share,
                        shareFormat));
            }

            return exitCode;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CommandError.Write(exception);
            return 1;
        }
    }

    private static bool ValidateAssetOptions(
        DependsOptions options,
        HashSet<string>? selectedSections,
        IReadOnlyCollection<string> candidateSections,
        bool hierarchyRequested,
        bool discoveryMode)
    {
        if (options.PruningPlatformFamily is not null
            && !candidateSections.Contains(
                DependsAssetSections.Pruning,
                StringComparer.OrdinalIgnoreCase))
        {
            CommandError.Write(
                "--platform-family requires the Pruning section.");
            return false;
        }

        if (candidateSections.Contains(
                DependsAssetSections.Pruning,
                StringComparer.OrdinalIgnoreCase))
        {
            if (options.Tfm is null)
            {
                CommandError.Write(
                    $"The {DependsAssetSections.Pruning} section requires --tfm.");
                return false;
            }
            string family = options.PruningPlatformFamily ?? "runtime";
            if (!family.Equals(
                    "runtime",
                    StringComparison.OrdinalIgnoreCase)
                && !family.Equals(
                    "aspnetcore",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    "--platform-family must be 'runtime' or 'aspnetcore'.");
                return false;
            }
            if (!PlatformTargetFramework.TryParse(
                    options.Tfm.ToLowerInvariant(),
                    out _))
            {
                CommandError.Write(
                    $"The {DependsAssetSections.Pruning} section requires a base .NET target framework such as net11.0.");
                return false;
            }
        }

        if (EffectiveDepth(options) is not null && !hierarchyRequested)
        {
            CommandError.Write(
                "--depth requires the Dependency Hierarchy section.");
            return false;
        }

        if (!discoveryMode
            && (options.Tree || options.MermaidOutput)
            && (candidateSections.Count != 1
                || !candidateSections.Contains(
                    DependsAssetSections.DependencyHierarchy,
                    StringComparer.OrdinalIgnoreCase)))
        {
            CommandError.Write(
                $"{(options.Tree ? "--tree" : "--format mermaid")} requires exactly '-S \"{DependsAssetSections.DependencyHierarchy}\"'.");
            return false;
        }
        if (!discoveryMode
            && (options.Tree || options.MermaidOutput)
            && (options.Count || IsColumnProjectionRequested(options)))
        {
            CommandError.Write(
                $"{(options.Tree ? "--tree" : "--format mermaid")} cannot combine with --count, --columns, or --fields.");
            return false;
        }

        bool hierarchyFailuresJsonl =
            options.Jsonl
            && candidateSections.Count == 2
            && candidateSections.Contains(
                DependsAssetSections.DependencyHierarchy,
                StringComparer.OrdinalIgnoreCase)
            && candidateSections.Contains(
                DependsAssetSections.Failures,
                StringComparer.OrdinalIgnoreCase);
        if (!discoveryMode
            && hierarchyFailuresJsonl
            && IsColumnProjectionRequested(options))
        {
            CommandError.Write(
                "Projected JSONL columns cannot represent the discriminated Dependency Hierarchy and Failures records; remove --columns/--fields.");
            return false;
        }
        DocumentSchema projectionSchema =
            options.Tabular && !options.Count
                ? DependsAssetSections.CreateTableSchema()
                : DependsAssetSections.CreateSchema();
        if (!discoveryMode
            && !ProjectionDiagnostics.ValidateProjection(
                projectionSchema,
                candidateSections,
                options.Fields,
                options.Columns))
        {
            return false;
        }
        if (!discoveryMode
            && options.Tabular
            && !options.Count
            && candidateSections.Count != 1
            && !hierarchyFailuresJsonl)
        {
            string format = options.Jsonl
                ? "--format jsonl"
                : options.Tsv
                    ? "--format tsv"
                    : "--format table";
            CommandError.Write(
                $"{format} requires exactly one selected table section; this view selects {candidateSections.Count}.");
            return false;
        }

        bool hasPrefix = options.PackagePrefix is not null;
        bool hasEvidenceRoots = options.AssetRoots.Any(root =>
            root.Kind is DependencyInspectionRootKind.Package
                or DependencyInspectionRootKind.Nuspec
                or DependencyInspectionRootKind.Project);
        bool hasRemotePackage = options.AssetRoots.Any(root =>
            root.Kind == DependencyInspectionRootKind.Package
            && !DependencyEvidenceAcquisition.IsLocalArchiveTarget(
                root.Value));
        bool hasLocalPackage = options.AssetRoots.Any(root =>
            root.Kind == DependencyInspectionRootKind.Package
            && DependencyEvidenceAcquisition.IsLocalArchiveTarget(
                root.Value));
        bool hasNuspec = options.AssetRoots.Any(root =>
            root.Kind == DependencyInspectionRootKind.Nuspec);
        bool hasProject = options.AssetRoots.Any(root =>
            root.Kind == DependencyInspectionRootKind.Project);
        bool licensesRequested = candidateSections.Contains(
            DependsAssetSections.Licenses,
            StringComparer.OrdinalIgnoreCase);
        bool hasPackageBackedLibrary = options.AssetRoots.Any(root =>
            root.Kind == DependencyInspectionRootKind.Library
            && LibraryMayConsumeSources(root.Value, options.Tfm));
        bool hasSourceOverrides = options.SourceOptions is { } sourceOptions
            && (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null);

        if (hasPrefix
            && !PackageProfileQuery.IsValidPrefix(options.PackagePrefix))
        {
            CommandError.Write(
                "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.");
            return false;
        }
        if (hasPrefix && options.IncludePrerelease)
        {
            CommandError.Write(
                "--preview applies only to latest remote --package resolution; --package-prefix admits the versions its profile producer returns.");
            return false;
        }
        if (hasPrefix && hasSourceOverrides)
        {
            CommandError.Write(
                "--package-prefix currently uses the NuGet Gallery source and cannot be combined with source overrides.");
            return false;
        }
        if (hasPrefix && licensesRequested)
        {
            CommandError.Write(
                "The Licenses section requires explicit package, nuspec, or project roots and cannot be combined with --package-prefix.");
            return false;
        }
        if (licensesRequested && !hasEvidenceRoots)
        {
            CommandError.Write(
                "The Licenses section requires at least one --package, --nuspec, or --project root.");
            return false;
        }

        int maximumPackages = options.MaxPackages
            ?? DependencyEvidenceAcquisition.PackageProfileDefaultLimit;
        if (hasPrefix
            && maximumPackages is <= 0
                or > DependencyEvidenceAcquisition.PackageProfileMaximumLimit)
        {
            CommandError.Write(
                $"--max-packages must be between 1 and {DependencyEvidenceAcquisition.PackageProfileMaximumLimit} (got {maximumPackages}).");
            return false;
        }
        if (!hasPrefix && options.MaxPackages is not null)
        {
            CommandError.Write(
                "--max-packages bounds --package-prefix discovery and cannot be used without it.");
            return false;
        }

        bool hasLatestRemote = options.AssetRoots.Any(root =>
        {
            if (root.Kind != DependencyInspectionRootKind.Package
                || DependencyEvidenceAcquisition.IsLocalArchiveTarget(
                    root.Value))
            {
                return false;
            }
            (_, string? version) =
                DotnetInspector.Packages.PackageExtractor
                    .ParsePackageReference(root.Value);
            return string.IsNullOrWhiteSpace(version);
        });
        if (options.IncludePrerelease && !hasLatestRemote)
        {
            CommandError.Write(
                hasRemotePackage
                    ? "--preview applies only to latest remote --package resolution; every remote --package target already names an exact version."
                    : "--preview applies only to latest remote --package resolution.");
            return false;
        }

        if (hasSourceOverrides
            && !hasRemotePackage
            && !(hasLocalPackage
                && (hierarchyRequested
                    || licensesRequested
                    || candidateSections.Contains(
                        DependsAssetSections.Pruning,
                        StringComparer.OrdinalIgnoreCase)))
            && !(licensesRequested && (hasNuspec || hasProject))
            && !hasPackageBackedLibrary)
        {
            CommandError.Write(
                "--source, --add-source, and --nugetconfig require a remote --package root, package traversal from a local .nupkg root, or the Licenses section for a nuspec or project root.");
            return false;
        }

        if ((hierarchyRequested || licensesRequested)
            && (hasRemotePackage
                || hasLocalPackage
                || hasNuspec
                || hasPrefix)
            && options.Tfm is { } framework
            && !TryCreateTraversalTargetPolicy(
                framework,
                out _))
        {
            CommandError.Write(
                $"Target framework '{framework}' is not a valid NuGet framework.");
            return false;
        }

        if (!discoveryMode
            && options.Count
            && hasPrefix
            && !IsSingleFailureSelection(selectedSections))
        {
            CommandError.Write(
                $"--count cannot report an exact count for --package-prefix input unless exactly '-S {DependsAssetSections.Failures}' is selected.");
            return false;
        }

        if (hasEvidenceRoots
            || options.AssetRoots.Any(
                root => root.Kind == DependencyInspectionRootKind.Library)
            || hasPrefix)
        {
            return true;
        }

        CommandError.Write(
            "Asset-mode depends requires at least one --package, --nuspec, --library, --project, or --package-prefix root.");
        return false;
    }

    private static int? EffectiveDepth(DependsOptions options) =>
        options.QueryPlan?.MaximumDepth
        ?? options.Depth;

    private static HashSet<string> EffectiveDiscoveryPlanSections(
        string[] discover,
        SectionCatalog<DependsAssetProjection> catalog)
    {
        if (discover.Length == 0)
        {
            return new HashSet<string>(
                DependsAssetSections.SectionOrder.Where(section =>
                    !section.Equals(
                        DependsAssetSections.Pruning,
                        StringComparison.OrdinalIgnoreCase)
                    && !section.Equals(
                        DependsAssetSections.Licenses,
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
        }

        SelectResult resolved = SelectResolver.ResolveSelectAsSections(
            discover,
            catalog.SelectableSectionNames,
            catalog.InfoSectionNames,
            catalog.SelectionCategoryMap,
            selectDefault: false);
        return resolved.Sections
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LibraryMayConsumeSources(
        string library,
        string? targetFramework)
    {
        if (File.Exists(library))
            return false;
        if (!PlatformResolver.IsPlatformCandidate(library))
            return true;
        return !CanResolveInstalledPlatformLibrary(
            library,
            targetFramework);
    }

    private static bool IsNetworkFreeAssetHierarchy(DependsOptions options) =>
        options.PackagePrefix is null
        && options.AssetRoots.All(root => root.Kind switch
        {
            DependencyInspectionRootKind.Nuspec or DependencyInspectionRootKind.Project => true,
            DependencyInspectionRootKind.Library =>
                File.Exists(root.Value)
                || CanResolveInstalledPlatformLibrary(
                    root.Value,
                    options.Tfm),
            _ => false,
        });

    private static bool CanResolveInstalledPlatformLibrary(
        string library,
        string? targetFramework)
    {
        if (!PlatformResolver.IsPlatformCandidate(library))
            return false;
        if (targetFramework is null)
        {
            return PlatformResolver.ResolveAssembly(library)
                .AssemblyPath is not null;
        }
        if (!PlatformResolver.TryGetFrameworkSpecsForTargetFramework(
                targetFramework,
                out IReadOnlyList<string> frameworkSpecs))
        {
            return false;
        }

        return frameworkSpecs.Any(frameworkSpec =>
            PlatformResolver.ResolveAssembly(
                library,
                frameworkSpec).AssemblyPath is not null);
    }

    private static bool IsSingleFailureSelection(
        HashSet<string>? selectedSections) =>
        selectedSections is { Count: 1 }
        && selectedSections.Contains(DependsAssetSections.Failures);

    internal static Task<DependsAssetProjection>
        AcquirePackageSubjectProjectionAsync(
            string packageReference,
            string? targetFramework,
            bool includePrerelease,
            NuGetSourceOptions? sourceOptions,
            DependencyQueryPlan queryPlan,
            CommandContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageReference);
        ArgumentNullException.ThrowIfNull(queryPlan);
        ArgumentNullException.ThrowIfNull(context);

        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Package,
                    packageReference),
            ],
            Tfm = targetFramework,
            IncludePrerelease = includePrerelease,
            SourceOptions = sourceOptions,
            Depth = queryPlan.MaximumDepth,
            QueryPlan = queryPlan,
        };
        DependsAssetRequestPlan plan =
            DependsAssetRequestPlan.FromSections(
                new HashSet<string>(
                    [DependsAssetSections.DependencyHierarchy],
                    StringComparer.OrdinalIgnoreCase));
        return AcquireAssetProjectionAsync(
            options,
            context,
            plan,
            traversalDepth: queryPlan.MaximumDepth,
            sharePreparation: null,
            static frameworkSpec =>
                InstalledPlatformPruneSource.Read(frameworkSpec),
            cancellationToken);
    }

    internal static Task<DependsAssetProjection>
        AcquireAdmittedPackageSubjectProjectionAsync(
            string packageId,
            string packageVersion,
            byte[]? manifestBytes,
            string? targetFramework,
            bool includePrerelease,
            NuGetSourceOptions? sourceOptions,
            DependencyQueryPlan queryPlan,
            CommandContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        ArgumentNullException.ThrowIfNull(queryPlan);
        ArgumentNullException.ThrowIfNull(context);

        string packageReference = $"{packageId}@{packageVersion}";
        var root = new DependsAssetRoot(
            1,
            DependencyInspectionRootKind.Package,
            packageReference);
        var options = new DependsOptions
        {
            AssetRoots = [root],
            Tfm = targetFramework,
            IncludePrerelease = includePrerelease,
            SourceOptions = sourceOptions,
            Depth = queryPlan.MaximumDepth,
            QueryPlan = queryPlan,
        };
        DependsAssetRequestPlan plan =
            DependsAssetRequestPlan.FromSections(
                new HashSet<string>(
                    [DependsAssetSections.DependencyHierarchy],
                    StringComparer.OrdinalIgnoreCase));
        DependencyEvidenceAcquisitionBatch acquisition =
            DependencyEvidenceAcquisition.AdmitPackageManifestRoot(
                root,
                PackageSourceCoordinate.Create(packageId, packageVersion),
                manifestBytes,
                targetFramework);
        return AcquireAssetProjectionAsync(
            options,
            context,
            plan,
            traversalDepth: queryPlan.MaximumDepth,
            sharePreparation: null,
            static frameworkSpec =>
                InstalledPlatformPruneSource.Read(frameworkSpec),
            cancellationToken,
            acquisition);
    }

    internal static Task<DependsAssetProjection>
        AcquireLibrarySubjectProjectionAsync(
            string assemblyPath,
            string? targetFramework,
            NuGetSourceOptions? sourceOptions,
            int? traversalDepth,
            CommandContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(context);

        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependencyInspectionRootKind.Library,
                    assemblyPath),
            ],
            Tfm = targetFramework,
            SourceOptions = sourceOptions,
        };
        DependsAssetRequestPlan plan =
            DependsAssetRequestPlan.FromSections(
                new HashSet<string>(
                    [DependsAssetSections.DependencyHierarchy],
                    StringComparer.OrdinalIgnoreCase));
        return AcquireAssetProjectionAsync(
            options,
            context,
            plan,
            traversalDepth,
            sharePreparation: null,
            static frameworkSpec =>
                InstalledPlatformPruneSource.Read(frameworkSpec),
            cancellationToken);
    }

    private static async Task<DependsAssetProjection>
        AcquireAssetProjectionAsync(
            DependsOptions options,
            CommandContext context,
            DependsAssetRequestPlan plan,
            int? traversalDepth,
            DependsShareProjection.AssetSharePreparation? sharePreparation,
            Func<string, InstalledPlatformPruneSource.Result> pruneSource,
            CancellationToken cancellationToken,
            DependencyEvidenceAcquisitionBatch? suppliedAcquisition = null)
    {
        DependencyEvidenceAcquisitionOptions evidenceOptions =
            EvidenceOptions(options);
        DependencyEvidenceAcquisitionBatch? acquisition = null;
        PackageDependencyEvidenceRequest evidenceRequest;

        await using DesktopPackageSourceComposition composition =
            context.CreatePackageSourceComposition();
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using var operationContext = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            cancellationToken);

        if (options.PackagePrefix is { } prefix)
        {
            (evidenceRequest, _) =
                await DependencyEvidenceAcquisition
                    .AcquireGalleryPackagePrefixAsync(
                        prefix,
                        evidenceOptions,
                        context,
                        cancellationToken).ConfigureAwait(false);
        }
        else
        {
            acquisition = suppliedAcquisition
                ?? await DependencyEvidenceAcquisition.AcquireDependsRootsAsync(
                        options.AssetRoots,
                        evidenceOptions,
                        context.HttpClient,
                        context.Logger.Log,
                        composition,
                        operationContext,
                        plan.PackageTraversal,
                        traversalDepth,
                        cancellationToken,
                        settledPackageCoordinate:
                            sharePreparation?.Coordinate,
                        settledPackageAuthorization:
                            sharePreparation?.Authorization)
                    .ConfigureAwait(false);
            evidenceRequest = acquisition.Request;
        }

        PackageDependencyEvidenceOutcome evidenceOutcome =
            PackageDependencyEvidenceQuery.Execute(evidenceRequest);
        int[] admittedIndexes;
        int?[] failedIndexes;
        if (acquisition is null)
        {
            admittedIndexes =
            [
                .. Enumerable.Range(1, evidenceOutcome.Roots.Length),
            ];
            failedIndexes = new int?[evidenceOutcome.FailedRoots.Length];
        }
        else
        {
            admittedIndexes = new int[evidenceOutcome.Roots.Length];
            failedIndexes = new int?[evidenceOutcome.FailedRoots.Length];
            foreach (DependencyEvidenceAcquiredRoot root in acquisition.Roots)
            {
                if (root.InputIndex is { } inputIndex)
                    admittedIndexes[inputIndex] = root.Root.OccurrenceIndex;
                if (root.FailureIndex is { } failureIndex)
                    failedIndexes[failureIndex] =
                        root.Root.OccurrenceIndex;
            }
        }

        List<LibraryAssetResult> libraries =
            await AcquireLibraryRootsAsync(
                options,
                context,
                plan.Traversal,
                traversalDepth,
                cancellationToken).ConfigureAwait(false);
        DependsPruningProjectionResult pruning =
            plan.Pruning
                ? await AcquirePruningProjectionAsync(
                    options,
                    evidenceOutcome,
                    admittedIndexes,
                    libraries,
                    composition,
                    operationContext,
                    context,
                    pruneSource,
                    cancellationToken).ConfigureAwait(false)
                : new DependsPruningProjectionResult(
                    [],
                    [],
                    DependencyInspectionPruningSummary.NotRequested);
        var graphDocuments = new List<DependencyGraphDocument>();
        PackageDependencyTraversalOutcome? packageTraversal = null;
        var packageOccurrences = new List<int>();
        var packageInputIndexes = new List<int>();
        for (int inputIndex = 0;
             inputIndex < evidenceOutcome.Roots.Length;
             inputIndex++)
        {
            PackageDependencyEvidenceRoot root =
                evidenceOutcome.Roots[inputIndex];
            if (root.Identity
                    is not PackageDependencyEvidenceRootIdentity.Package
                || root.Declaration
                    is not PackageDependencyEvidenceDeclarationResult
                        .Available)
            {
                continue;
            }

            int occurrence = admittedIndexes[inputIndex];
            packageOccurrences.Add(occurrence);
            packageInputIndexes.Add(inputIndex);
        }

        if (plan.PackageTraversal && packageInputIndexes.Count > 0)
        {
            TraversalTargetFrameworkPolicy traversalTargetPolicy =
                options.Tfm is { } framework
                    ? CreateTraversalTargetPolicy(framework)
                    : TraversalTargetFrameworkPolicy.ProductDefault;
            var candidateSource =
                new DesktopPackageDependencyCandidateSource(
                    composition,
                    options.SourceOptions,
                    context.Logger.Log);
            packageTraversal =
                await PackageDependencyTraversalQuery.ExecuteAsync(
                    new PackageDependencyTraversalRequest(
                        [
                            .. packageInputIndexes.Select(inputIndex =>
                                new PackageDependencyTraversalRootOccurrence(
                                    evidenceOutcome.Roots[inputIndex],
                                    options.PackagePrefix is not null
                                    || evidenceOutcome.Roots[inputIndex]
                                            .Provenance.AcquisitionForm
                                        == PackageDependencyEvidenceAcquisitionForm
                                            .DirectNuspec
                                        && !plan.Licenses
                                        ? PackageDependencyTraversalExpansionAuthority
                                            .DirectDeclarationsOnly
                                        : PackageDependencyTraversalExpansionAuthority
                                            .RecursiveSources,
                                    suppliedAcquisition is null
                                        ? PackageDependencyTraversalRootRecurrenceAuthority
                                            .None
                                        : PackageDependencyTraversalRootRecurrenceAuthority
                                            .ExactCoordinate)),
                        ],
                        traversalTargetPolicy,
                        new PackageDependencyTraversalCandidateAdapter(
                            candidateSource),
                        new DesktopPackageDependencyTraversalManifestSource(
                            composition),
                        new PackageDependencyTraversalWorkBudget(
                            PackageManifestTraversalBudget,
                            PackageDeclarationTraversalBudget),
                        traversalDepth),
                    cancellationToken,
                    operationContext).ConfigureAwait(false);
            if (plan.Traversal)
            {
                graphDocuments.Add(
                    DependencyGraphProjection.Package(
                        packageTraversal,
                        packageOccurrences));
            }
        }
        if (plan.Traversal)
        {
            foreach (int inputIndex in Enumerable.Range(
                         0,
                         evidenceOutcome.Roots.Length))
            {
                PackageDependencyEvidenceRoot root =
                    evidenceOutcome.Roots[inputIndex];
                if (root.Identity
                        is PackageDependencyEvidenceRootIdentity.Package
                    && !packageInputIndexes.Contains(inputIndex))
                {
                    graphDocuments.Add(
                        DependencyGraphProjection.PackageRoot(
                            root,
                            admittedIndexes[inputIndex]));
                }
            }
        }

        if (plan.Traversal && acquisition is not null)
        {
            foreach (DependencyEvidenceAcquiredRoot acquired in
                     acquisition.Roots.Where(root =>
                         root.Root.Kind == DependencyInspectionRootKind.Project
                         && root.InputIndex is not null))
            {
                int inputIndex = acquired.InputIndex!.Value;
                PackageDependencyEvidenceRoot evidenceRoot =
                    evidenceOutcome.Roots[inputIndex];
                int occurrence = acquired.Root.OccurrenceIndex;
                switch (acquired.RestoredTraversal)
                {
                    case RestoredProjectDependencyTraversalResult.Available
                        available:
                        graphDocuments.Add(
                            DependencyGraphProjection.RestoredProject(
                                available.Value,
                                occurrence,
                                evidenceRoot.Display));
                        break;
                    case RestoredProjectDependencyTraversalResult.Unavailable
                        unavailable:
                        graphDocuments.Add(
                            DependencyGraphProjection.RestoredRoot(
                                unavailable.Facts,
                                evidenceRoot.Display,
                                occurrence));
                        break;
                    case RestoredProjectDependencyTraversalResult.Failed
                    {
                        Failure:
                            RestoredProjectDependencyTraversalFailure.Graph
                                graphFailure,
                    }:
                        graphDocuments.Add(
                            DependencyGraphProjection.RestoredRoot(
                                graphFailure.Facts,
                                evidenceRoot.Display,
                                occurrence));
                        break;
                }
            }
        }

        if (plan.Traversal)
        {
            graphDocuments.AddRange(
                libraries
                    .Where(static library => library.Document is not null)
                    .Select(static library => library.Document!));
        }

        DependencyGraphDocument graph = DependencyGraphProjection.Combine(
            graphDocuments);
        ImmutableArray<DependencyInspectionFailure> additionalFailures =
            BuildAdditionalFailures(
                packageTraversal,
                packageOccurrences,
                libraries,
                acquisition,
                plan);
        ImmutableArray<DependencyInspectionRootInput> rootInputs =
            BuildRootInputs(
                evidenceOutcome,
                admittedIndexes,
                acquisition,
                packageTraversal,
                packageOccurrences,
                libraries,
                plan,
                graph);
        ImmutableArray<DependencyRootOccurrenceIdentity>
            admittedRootOccurrences =
        [
            .. admittedIndexes.Select(static occurrence =>
                new DependencyRootOccurrenceIdentity(occurrence)),
        ];
        ImmutableArray<DependencyRootOccurrenceIdentity?>
            failedRootOccurrences =
        [
            .. failedIndexes.Select(static occurrence =>
                occurrence is { } value
                    ? new DependencyRootOccurrenceIdentity(value)
                    : (DependencyRootOccurrenceIdentity?)null),
        ];
        int requestedRoots = options.PackagePrefix is null
            ? options.AssetRoots.Length
            : evidenceOutcome.RootSet.AdmittedRootCount
                + evidenceOutcome.RootSet.FailedRootCount
                + evidenceOutcome.RootSet.RejectedRootCount;
        var liveEvidenceDocument = new DependencyInspectionEvidenceDocument(
            evidenceOutcome,
            admittedRootOccurrences,
            failedRootOccurrences);
        DependencyEvidenceProjection liveEvidence =
            DependencyEvidenceProjection.Create(liveEvidenceDocument);
        DependsLicenseProjectionResult licenses =
            await AcquireLicenseProjectionAsync(
                options,
                plan,
                packageTraversal,
                liveEvidence,
                evidenceOutcome,
                composition,
                operationContext,
                context,
                cancellationToken).ConfigureAwait(false);
        additionalFailures =
        [
            .. additionalFailures,
            .. licenses.Failures,
        ];
        var operationRequest = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                plan.Declarations,
                plan.RestoredRelationships,
                plan.Traversal,
                plan.Pruning,
                options.Tfm is null
                    ? null
                    : new InertString(TextPolicy.Field, options.Tfm),
                plan.Traversal ? traversalDepth : null)
            {
                Licenses = plan.Licenses,
            },
            requestedRoots,
            options.PackagePrefix is not null,
            evidenceOutcome,
            admittedRootOccurrences,
            failedRootOccurrences,
            rootInputs,
            graph,
            additionalFailures,
            pruning.Rows,
            pruning.Failures,
            pruning.Summary,
            sharePreparation?.Share)
        {
            Licenses = licenses.Rows,
            LicenseSummary = licenses.Summary,
        };
        var builder = new EvidenceInspectionBuilder<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument>();
        builder.RequestEvidence(plan.SupplementalEvidence);
        (
            InspectionEnvelope<DependencyInspectionContent> inspection,
            EvidenceInspectionEnvelope<
                DependencyInspectionContent,
                DependencyInspectionEvidenceDocument>? enriched) =
            builder.Build(
                operationRequest,
                static request =>
                    DependencyInspectionOperation.Execute(request),
                static request =>
                    DependencyInspectionOperation.ExecuteWithEvidence(
                        request));
        DependencyEvidenceProjection? evidence = enriched is null
            ? null
            : DependencyEvidenceProjection.Create(enriched.Evidence);
        ImmutableArray<DependencyInspectionFailure> liveFailures =
        [
            .. liveEvidence.Failures
                .Where(failure =>
                    IsSelectedEvidenceFailure(failure.Phase, plan))
                .Select(static failure =>
                    new DependencyInspectionFailure.Evidence(failure)),
            .. additionalFailures,
            .. pruning.Failures,
        ];
        ImmutableArray<DependsRootRow> roots = BuildPresentationRoots(
            evidenceOutcome,
            evidence,
            acquisition,
            libraries,
            inspection.Content.Roots);
        DependencyHierarchyDocument hierarchy = plan.Traversal
            ? inspection.Content.Hierarchy with { BackingGraph = graph }
            : DependencyHierarchyDocument.Empty;

        return new DependsAssetProjection(
            inspection,
            inspection.Content.Summary with
            {
                PackagePrefix =
                    evidenceOutcome.RootSet.PackagePrefixCompletion,
            },
            graph,
            hierarchy,
            [.. DependencyHierarchyOutputAdapter.Rows(hierarchy)],
            roots,
            inspection.Content.Dependencies,
            pruning.Rows,
            evidence?.RestoredEdges ?? [],
            liveFailures,
            evidence?.DependencyGroups ?? [],
            evidence?.RestoredPackages ?? [],
            enriched);
    }

    private static bool IsSelectedEvidenceFailure(
        DependencyEvidenceFailurePhase phase,
        DependsAssetRequestPlan plan) =>
        phase switch
        {
            DependencyEvidenceFailurePhase.Root
                or DependencyEvidenceFailurePhase.PackageProfile
                or DependencyEvidenceFailurePhase.Library => true,
            DependencyEvidenceFailurePhase.Declaration => plan.Declarations,
            DependencyEvidenceFailurePhase.Graph =>
                plan.RestoredRelationships,
            DependencyEvidenceFailurePhase.Traversal => plan.Traversal,
            DependencyEvidenceFailurePhase.License => plan.Licenses,
            DependencyEvidenceFailurePhase.Pruning => plan.Pruning,
            _ => false,
        };

    private static DependencyEvidenceAcquisitionOptions EvidenceOptions(
        DependsOptions options) =>
        new()
        {
            Tfm = options.Tfm,
            IncludePrerelease = options.IncludePrerelease,
            MaxPackages = options.MaxPackages,
            SourceOptions = options.SourceOptions,
        };

    private static async Task<DependsLicenseProjectionResult>
        AcquireLicenseProjectionAsync(
            DependsOptions options,
            DependsAssetRequestPlan plan,
            PackageDependencyTraversalOutcome? packageTraversal,
            DependencyEvidenceProjection evidence,
            PackageDependencyEvidenceOutcome evidenceOutcome,
            DesktopPackageSourceComposition composition,
            NuGetOperationContext operationContext,
            CommandContext context,
            CancellationToken cancellationToken)
    {
        if (!plan.Licenses)
        {
            return new DependsLicenseProjectionResult(
                [],
                DependencyInspectionLicenseSummary.NotRequested,
                []);
        }

        var coordinates = new HashSet<PackageSourceCoordinate>();
        if (packageTraversal is not null)
        {
            var explicitRootCoordinates = packageTraversal.Roots
                .Select(root =>
                    packageTraversal.Nodes[root.NodeIndex].Coordinate)
                .ToHashSet();
            foreach (PackageDependencyTraversalEdge edge in
                     packageTraversal.Edges)
            {
                if (edge.Target
                    is not PackageDependencyTraversalEdgeTarget.Node target)
                {
                    continue;
                }
                PackageSourceCoordinate coordinate =
                    packageTraversal.Nodes[target.NodeIndex].Coordinate;
                if (!explicitRootCoordinates.Contains(coordinate))
                    coordinates.Add(coordinate);
            }
        }
        foreach (DependencyEvidenceRestoredPackageRow package in
                 evidence.RestoredPackages)
        {
            coordinates.Add(package.Identity.Coordinate);
        }

        var source = new DesktopPackageLicenseManifestSource(
            composition,
            options.SourceOptions,
            context.Logger.Log);
        PackageLicenseInventoryResult inventory =
            await PackageLicenseInventoryQuery.ExecuteAsync(
                coordinates,
                source,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        bool sourceComplete =
            evidenceOutcome.RootSet.Completion
                == PackageDependencyEvidenceRootSetCompletion.Complete
            && IsComplete(evidenceOutcome.Phases.Declarations)
            && IsComplete(evidenceOutcome.Phases.Relationships)
            && (packageTraversal is null
                || packageTraversal.IsSuccessful);
        int unavailable = inventory.Items.Count(
            static item => item.Failure is not null);
        DependencyInspectionLicenseCompletion completion =
            sourceComplete
            && inventory.Completion
                == PackageLicenseInventoryCompletion.Complete
                ? DependencyInspectionLicenseCompletion.Complete
                : DependencyInspectionLicenseCompletion.Partial;
        return new DependsLicenseProjectionResult(
            [.. inventory.Items.Select(
                DependencyInspectionLicense.Create)],
            new DependencyInspectionLicenseSummary(
                completion,
                inventory.Items.Length,
                inventory.Items.Length - unavailable,
                unavailable),
            [
                .. inventory.Items
                    .Where(static item => item.Failure is not null)
                    .Select(static item =>
                    {
                        PackageLicenseInventoryFailure failure =
                            item.Failure!;
                        return new DependencyInspectionFailure.Evidence(
                            new DependencyEvidenceFailureRow(
                                DependencyEvidenceFailurePhase.License,
                                failure.Reason.ToString(),
                                SourceKind: null,
                                RootIndex: null,
                                RootIdentity: null,
                                Group: null,
                                GroupIndex: null,
                                Source: null,
                                Subject: null,
                                item.Coordinate.PackageId,
                                item.Coordinate.Version,
                                SourceLabel: null,
                                new InertString(
                                    TextPolicy.Prose,
                                    failure.Message),
                                Occurrences: 1));
                    }),
            ]);
    }

    private static bool IsComplete(
        PackageDependencyEvidencePhaseCounts counts) =>
        counts.Incomplete == 0
        && counts.Unavailable == 0
        && counts.Failed == 0;

    private static TraversalTargetFrameworkPolicy
        CreateTraversalTargetPolicy(string framework)
    {
        if (TryCreateTraversalTargetPolicy(
                framework,
                out TraversalTargetFrameworkPolicy policy))
        {
            return policy;
        }

        throw new InvalidOperationException(
            $"Target framework '{framework}' was not validated.");
    }

    private static bool TryCreateTraversalTargetPolicy(
        string framework,
        out TraversalTargetFrameworkPolicy policy)
    {
        try
        {
            policy = new TraversalTargetFrameworkPolicy(framework);
            return true;
        }
        catch (ArgumentException)
        {
            policy = null!;
            return false;
        }
    }

    private static async Task<List<LibraryAssetResult>>
        AcquireLibraryRootsAsync(
            DependsOptions options,
            CommandContext context,
            bool hierarchyRequested,
            int? traversalDepth,
            CancellationToken cancellationToken)
    {
        var results = new List<LibraryAssetResult>();
        foreach (DependsAssetRoot root in options.AssetRoots.Where(
                     root => root.Kind == DependencyInspectionRootKind.Library))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LibraryDependencyGraphResult result;
            try
            {
                result =
                    await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                        context.HttpClient,
                        root.Value,
                        options.SourceOptions,
                        context.Logger,
                        hierarchyRequested ? traversalDepth : 0,
                        cancellationToken,
                        traverseReferences: hierarchyRequested,
                        requestedTfm: options.Tfm)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException
                or BadImageFormatException
                or PackageSourceMappingException
                or UnsupportedSourceException
                or System.Security.SecurityException)
            {
                result = new LibraryDependencyGraphResult.Error(
                    "The library root could not be acquired or inspected.",
                    root.Value);
            }
            DependencyGraphDocument? document = result switch
            {
                LibraryDependencyGraphResult.Graph graph =>
                    DependencyGraphProjection.WithRootOccurrence(
                        DependencyGraphProjection.Library(graph),
                        root.OccurrenceIndex),
                LibraryDependencyGraphResult.Empty empty =>
                    DependencyGraphProjection.WithRootOccurrence(
                        DependencyGraphProjection.Library(empty),
                        root.OccurrenceIndex),
                _ => null,
            };
            results.Add(new LibraryAssetResult(root, result, document));
        }

        return results;
    }

    private static ImmutableArray<DependencyInspectionFailure>
        BuildAdditionalFailures(
        PackageDependencyTraversalOutcome? packageTraversal,
        IReadOnlyList<int> packageOccurrences,
        IReadOnlyList<LibraryAssetResult> libraries,
        DependencyEvidenceAcquisitionBatch? acquisition,
        DependsAssetRequestPlan plan)
    {
        var failures = ImmutableArray.CreateBuilder<DependencyInspectionFailure>();
        if (plan.PackageTraversal && packageTraversal is not null)
        {
            foreach (PackageDependencyTraversalFailedResolutionNode failed in
                     packageTraversal.FailedResolutions)
            {
                failures.Add(
                    new DependencyInspectionFailure.Traversal(
                        new DependencyInspectionTraversalFailure(
                            CandidateFailureReason(failed.Outcome),
                            failed.SourceProjectionIndex,
                            NodeIndex: null,
                            ProjectionIndex: null,
                            failed.DeclarationIdentity,
                            failed.CanonicalPackageId,
                            failed.CanonicalVersionConstraint,
                            DependencyInspectionPackageCandidateOutcome.Create(
                                failed.Outcome),
                            ManifestFailure: null,
                            BudgetKind: null,
                            BudgetLimit: null,
                            RestoredFailure: null,
                            MapAffectedRoots(
                                packageOccurrences,
                                failed.AffectedRootOccurrences))
                        {
                            RuntimeCandidateOutcome = failed.Outcome,
                        }));
            }
            foreach (PackageDependencyTraversalWorkBudgetNode budget in
                     packageTraversal.WorkBudgetDeclarations)
            {
                failures.Add(
                    new DependencyInspectionFailure.Traversal(
                        new DependencyInspectionTraversalFailure(
                            budget.BudgetKind.ToString(),
                            budget.SourceProjectionIndex,
                            NodeIndex: null,
                            ProjectionIndex: null,
                            budget.DeclarationIdentity,
                            budget.CanonicalPackageId,
                            budget.CanonicalVersionConstraint,
                            CandidateOutcome: null,
                            ManifestFailure: null,
                            budget.BudgetKind,
                            budget.Limit,
                            RestoredFailure: null,
                            MapAffectedRoots(
                                packageOccurrences,
                                budget.AffectedRootOccurrences))));
            }
            foreach (PackageDependencyTraversalFailure failure in
                     packageTraversal.Failures)
            {
                PackageSourceCoordinate coordinate =
                    packageTraversal.Nodes[failure.NodeIndex].Coordinate;
                failures.Add(
                    new DependencyInspectionFailure.Traversal(
                        new DependencyInspectionTraversalFailure(
                            failure.Detail.GetType().Name,
                            failure.ProjectionIndex,
                            failure.NodeIndex,
                            failure.ProjectionIndex,
                            DeclarationIdentity: null,
                            coordinate.PackageId,
                            coordinate.Version,
                            CandidateOutcome: null,
                            DependencyInspectionPackageManifestFailure.Create(
                                failure.Detail),
                            BudgetKind: null,
                            BudgetLimit: failure.Detail
                                is PackageDependencyTraversalManifestFailureDetail
                                    .ManifestProjectionBudgetExhausted budget
                                    ? budget.Limit
                                    : null,
                            RestoredFailure: null,
                            MapAffectedRoots(
                                packageOccurrences,
                                failure.AffectedRootOccurrences))
                        {
                            RuntimeManifestFailure = failure.Detail,
                        }));
            }
        }

        if (plan.PackageTraversal && acquisition is not null)
        {
            foreach (DependencyEvidenceAcquiredRoot acquired in
                     acquisition.Roots)
            {
                if (acquired.InputIndex is null)
                {
                    continue;
                }

                switch (acquired.RestoredTraversal)
                {
                    case RestoredProjectDependencyTraversalResult.Available
                        available:
                        foreach (RestoredProjectGraphFailure graphFailure in
                                 available.Value.Failures)
                        {
                            failures.Add(
                                new DependencyInspectionFailure.Traversal(
                                    new DependencyInspectionTraversalFailure(
                                        graphFailure.Reason.ToString(),
                                        SourceProjectionIndex: null,
                                        NodeIndex: null,
                                        ProjectionIndex: null,
                                        DeclarationIdentity: null,
                                        PackageId: null,
                                        VersionConstraint: null,
                                        CandidateOutcome: null,
                                        ManifestFailure: null,
                                        BudgetKind: null,
                                        BudgetLimit: null,
                                        new DependencyInspectionRestoredTraversalFailure
                                            .Graph(graphFailure),
                                        [acquired.Root.OccurrenceIndex])));
                        }
                        break;
                    case RestoredProjectDependencyTraversalResult.Failed
                        failed:
                        failures.Add(
                            new DependencyInspectionFailure.Traversal(
                                new DependencyInspectionTraversalFailure(
                                    failed.Failure.GetType().Name,
                                    SourceProjectionIndex: null,
                                    NodeIndex: null,
                                    ProjectionIndex: null,
                                    DeclarationIdentity: null,
                                    PackageId: null,
                                    VersionConstraint: null,
                                    CandidateOutcome: null,
                                    ManifestFailure: null,
                                    BudgetKind: null,
                                    BudgetLimit: null,
                                    new DependencyInspectionRestoredTraversalFailure
                                        .Outcome(
                                            DependencyInspectionRestoredTraversalOutcomeFailure
                                                .Create(failed.Failure)),
                                    [acquired.Root.OccurrenceIndex])));
                        break;
                }
            }
        }

        foreach (LibraryAssetResult library in libraries)
        {
            if (plan.Traversal
                && library.Result
                    is LibraryDependencyGraphResult.Graph libraryGraph)
            {
                foreach (LibraryMetadataService.AssemblyReferenceRelationship
                         relationship in
                             libraryGraph.ReferenceGraph.Relationships)
                {
                    if (relationship.MissingDisposition is not
                        { } disposition)
                    {
                        continue;
                    }

                    failures.Add(
                        new DependencyInspectionFailure.Traversal(
                            new DependencyInspectionTraversalFailure(
                                "Missing",
                                SourceProjectionIndex: null,
                                NodeIndex: null,
                                ProjectionIndex: null,
                                DeclarationIdentity: null,
                                PackageId: null,
                                VersionConstraint: null,
                                CandidateOutcome: null,
                                ManifestFailure: null,
                                BudgetKind: null,
                                BudgetLimit: null,
                                RestoredFailure: null,
                                [library.Root.OccurrenceIndex],
                                new DependencyInspectionAssemblyBindingFailure(
                                    disposition,
                                    relationship.RequestedTarget))));
                }
            }

            (DependencyEvidenceFailurePhase Phase, string? Message) failure =
                library.Result switch
                {
                    LibraryDependencyGraphResult.Error error =>
                        (DependencyEvidenceFailurePhase.Root, error.Message),
                    LibraryDependencyGraphResult.NoMetadata noMetadata =>
                        (DependencyEvidenceFailurePhase.Root,
                            $"'{noMetadata.AssemblyName}' has no managed metadata."),
                    LibraryDependencyGraphResult.Graph graph
                        when plan.Traversal
                        && (graph.ReferenceGraph.HasInspectionFailures
                            || graph.ReferenceGraph.Relationships.Any(
                                static relationship =>
                                    relationship.ResolutionFailure is not null)) =>
                            (DependencyEvidenceFailurePhase.Traversal,
                                "One or more assembly references could not be resolved or inspected."),
                    _ => default,
                };
            if (failure.Message is null)
                continue;

            failures.Add(
                new DependencyInspectionFailure.Evidence(
                    new DependencyEvidenceFailureRow(
                        failure.Phase,
                        library.Result.GetType().Name,
                        SourceKind: null,
                        library.Root.OccurrenceIndex,
                        RootIdentity: null,
                        Group: null,
                        GroupIndex: null,
                        Source: null,
                        new InertString(
                            TextPolicy.Field,
                            library.Root.Value),
                        PackageId: null,
                        PackageVersion: null,
                        SourceLabel: null,
                        new InertString(
                            TextPolicy.Prose,
                            failure.Message),
                        Occurrences: 1,
                        EvidenceIdentity: null)));
        }

        return failures.ToImmutable();
    }

    private static ImmutableArray<int> MapAffectedRoots(
        IReadOnlyList<int> packageOccurrences,
        ImmutableArray<int> affectedRoots) =>
        [
            .. affectedRoots.Select(rootIndex =>
                packageOccurrences[rootIndex]),
        ];

    private static string CandidateFailureReason(
        PackageDependencyTraversalCandidateResult outcome) =>
        outcome switch
        {
            PackageDependencyTraversalCandidateResult.Failed failed =>
                failed.Failure.GetType().Name,
            PackageDependencyTraversalCandidateResult.Incomplete incomplete =>
                incomplete.Evidence.GetType().Name,
            PackageDependencyTraversalCandidateResult.Resolved =>
                nameof(PackageDependencyTraversalCandidateResult.Resolved),
            _ => throw new InvalidOperationException(
                "Unknown package candidate outcome."),
        };

    private static ImmutableArray<DependencyInspectionRootInput>
        BuildRootInputs(
            PackageDependencyEvidenceOutcome evidenceOutcome,
            IReadOnlyList<int> admittedIndexes,
            DependencyEvidenceAcquisitionBatch? acquisition,
            PackageDependencyTraversalOutcome? packageTraversal,
            IReadOnlyList<int> packageOccurrences,
            IReadOnlyList<LibraryAssetResult> libraries,
            DependsAssetRequestPlan plan,
            DependencyGraphDocument graph)
    {
        var roots =
            ImmutableArray.CreateBuilder<DependencyInspectionRootInput>();
        var packageCompletion =
            new Dictionary<
                int,
                DependencyInspectionTraversalCompletion>();
        if (packageTraversal is not null)
        {
            for (int index = 0; index < packageTraversal.Roots.Length; index++)
            {
                packageCompletion[packageOccurrences[index]] =
                    Convert(packageTraversal.Roots[index].Completion);
            }
        }

        var graphIdentityByOccurrence = graph.Roots.ToDictionary(
            static root => root.OccurrenceIndex,
            root => graph.Nodes[root.NodeId].Identity);
        if (acquisition is not null)
        {
            foreach (DependencyEvidenceAcquiredRoot acquired in acquisition.Roots)
            {
                if (acquired.InputIndex is { } inputIndex)
                {
                    PackageDependencyEvidenceRoot evidenceRoot =
                        evidenceOutcome.Roots[inputIndex];
                    DependencyInspectionTraversalCompletion traversal =
                        plan.Traversal
                            ? RootTraversal(
                                acquired,
                                packageCompletion,
                                evidenceRoot)
                            : DependencyInspectionTraversalCompletion.NotRequested;
                    DependencyGraphNodeIdentity? identity =
                        graphIdentityByOccurrence.GetValueOrDefault(
                            acquired.Root.OccurrenceIndex)
                        ?? DependencyIdentity(evidenceRoot);
                    roots.Add(
                        new DependencyInspectionRootInput(
                            new DependencyRootOccurrenceIdentity(
                                acquired.Root.OccurrenceIndex),
                            acquired.Root.Kind,
                            new InertString(
                                TextPolicy.Field,
                                acquired.Root.Value),
                            DependencyInspectionRootState.Admitted,
                            identity,
                            traversal));
                }
                else
                {
                    roots.Add(
                        new DependencyInspectionRootInput(
                            new DependencyRootOccurrenceIdentity(
                                acquired.Root.OccurrenceIndex),
                            acquired.Root.Kind,
                            new InertString(
                                TextPolicy.Field,
                                acquired.Root.Value),
                            DependencyInspectionRootState.Failed,
                            DependencyIdentity: null,
                            plan.Traversal
                                ? DependencyInspectionTraversalCompletion.Failed
                                : DependencyInspectionTraversalCompletion
                                    .NotRequested));
                }
            }
        }
        else
        {
            for (int inputIndex = 0;
                 inputIndex < evidenceOutcome.Roots.Length;
                 inputIndex++)
            {
                PackageDependencyEvidenceRoot evidenceRoot =
                    evidenceOutcome.Roots[inputIndex];
                int occurrence = admittedIndexes[inputIndex];
                DependencyGraphNodeIdentity? identity =
                    graphIdentityByOccurrence.GetValueOrDefault(occurrence)
                    ?? DependencyIdentity(evidenceRoot);
                roots.Add(
                    new DependencyInspectionRootInput(
                        new DependencyRootOccurrenceIdentity(occurrence),
                        DependencyInspectionRootKind.Package,
                        evidenceRoot.Display,
                        DependencyInspectionRootState.Admitted,
                        identity,
                        plan.Traversal
                            ? packageCompletion.GetValueOrDefault(
                                occurrence,
                                DependencyInspectionTraversalCompletion.Partial)
                            : DependencyInspectionTraversalCompletion
                                .NotRequested));
            }
        }

        foreach (LibraryAssetResult library in libraries)
        {
            DependencyGraphNodeIdentity? identity =
                graphIdentityByOccurrence.GetValueOrDefault(
                    library.Root.OccurrenceIndex)
                ?? library.Document?.Nodes[
                    library.Document.Roots[0].NodeId].Identity;
            bool admitted = identity is not null;
            roots.Add(
                new DependencyInspectionRootInput(
                    new DependencyRootOccurrenceIdentity(
                        library.Root.OccurrenceIndex),
                    DependencyInspectionRootKind.Library,
                    new InertString(
                        TextPolicy.Field,
                        library.Root.Value),
                    admitted
                        ? DependencyInspectionRootState.Admitted
                        : DependencyInspectionRootState.Failed,
                    identity,
                    !plan.Traversal
                        ? DependencyInspectionTraversalCompletion.NotRequested
                        : LibraryCompletion(library)));
        }

        return
        [
            .. roots.OrderBy(static root => root.Identity.Value),
        ];
    }

    private static ImmutableArray<DependsRootRow> BuildPresentationRoots(
        PackageDependencyEvidenceOutcome evidenceOutcome,
        DependencyEvidenceProjection? evidence,
        DependencyEvidenceAcquisitionBatch? acquisition,
        IReadOnlyList<LibraryAssetResult> libraries,
        ImmutableArray<DependencyInspectionRoot> contentRoots)
    {
        Dictionary<int, DependencyEvidenceAcquiredRoot>? acquiredRoots =
            acquisition?.Roots.ToDictionary(
                static root => root.Root.OccurrenceIndex);
        Dictionary<int, LibraryAssetResult> libraryRoots =
            libraries.ToDictionary(
                static root => root.Root.OccurrenceIndex);
        Dictionary<int, DependencyEvidenceRootRow>? evidenceRows =
            evidence?.Roots.ToDictionary(static root => root.RootIndex);
        var roots =
            ImmutableArray.CreateBuilder<DependsRootRow>(
                contentRoots.Length);

        foreach (DependencyInspectionRoot content in contentRoots)
        {
            string source;
            DependencyEvidenceRootRow? evidenceRoot = null;
            if (content.Kind == DependencyInspectionRootKind.Library)
            {
                source = LibrarySource(
                    libraryRoots[content.Identity.Value].Result);
            }
            else if (acquiredRoots is null)
            {
                source = "PackagePrefix";
                evidenceRows?.TryGetValue(
                    content.Identity.Value,
                    out evidenceRoot);
            }
            else
            {
                DependencyEvidenceAcquiredRoot acquired =
                    acquiredRoots[content.Identity.Value];
                source = acquired.InputIndex is { } inputIndex
                    ? evidenceOutcome.Roots[inputIndex]
                        .Provenance.AcquisitionForm.ToString()
                    : SourceFor(acquired.Root);
                evidenceRows?.TryGetValue(
                    content.Identity.Value,
                    out evidenceRoot);
            }

            DependencyGraphNodeIdentity? identity =
                content.DependencyIdentity;
            var row = new DependsRootRow(
                content,
                source,
                identity is null
                    ? null
                    : DependencyGraphOutputAdapter.Kind(identity),
                identity is null
                    ? null
                    : DependencyGraphOutputAdapter.IdentityText(identity))
            {
                Evidence = evidenceRoot,
            };
            if (evidenceRoot is not null && evidence is not null)
            {
                DependsRootSelection selection = RootSelection(
                    evidenceRoot,
                    evidence);
                row = row with
                {
                    SelectedGroup = selection.SelectedGroup,
                    SelectedGroupIndex = selection.SelectedGroupIndex,
                    SelectedSourceOccurrence =
                        selection.SelectedSourceOccurrence,
                };
            }
            roots.Add(row);
        }

        return roots.ToImmutable();
    }

    private static string LibrarySource(
        LibraryDependencyGraphResult result) =>
        result switch
        {
            LibraryDependencyGraphResult.Graph graph =>
                graph.SourceKind.ToString(),
            LibraryDependencyGraphResult.Empty empty =>
                empty.SourceKind.ToString(),
            LibraryDependencyGraphResult.NoMetadata noMetadata =>
                noMetadata.SourceKind.ToString(),
            _ => "Library",
        };

    private static DependsRootSelection RootSelection(
        DependencyEvidenceRootRow root,
        DependencyEvidenceProjection evidence)
    {
        if (root.Owner
            != PackageDependencyEvidenceInputKind.RestoredProject)
        {
            return new DependsRootSelection(
                root.SelectedGroup,
                root.SelectedGroupIndex,
                root.SelectedSourceOccurrence);
        }

        if (root.RestoredTargetFrameworkIdentity is not { } selectedFramework)
        {
            return new DependsRootSelection(
                SelectedGroup: null,
                SelectedGroupIndex: null,
                SelectedSourceOccurrence: null);
        }

        PackageDependencyEvidenceGroupOccurrence.RestoredProject?
            selectedOccurrence = null;
        DependencyEvidenceGroupRow? selectedGroup = null;
        if (root.RestoredSelection is { } selection)
        {
            foreach (DependencyEvidenceGroupRow group in
                     evidence.DependencyGroups.Where(group =>
                         group.RootIndex == root.RootIndex))
            {
                selectedOccurrence = group.SourceOccurrences
                    .OfType<PackageDependencyEvidenceGroupOccurrence
                        .RestoredProject>()
                    .FirstOrDefault(occurrence =>
                        occurrence.Identity.Selection == selection
                        && string.Equals(
                            occurrence.Identity.PivotIdentity,
                            selectedFramework,
                            StringComparison.Ordinal));
                if (selectedOccurrence is null)
                    continue;
                selectedGroup = group;
                break;
            }
        }
        return new DependsRootSelection(
            selectedGroup?.Identity,
            selectedGroup?.GroupIndex,
            selectedOccurrence);
    }

    private static DependencyInspectionTraversalCompletion LibraryCompletion(
        LibraryAssetResult library) =>
        library.Result switch
        {
            LibraryDependencyGraphResult.Graph
            {
                ReferenceGraph.HasFailures: true,
            } => DependencyInspectionTraversalCompletion.Partial,
            LibraryDependencyGraphResult.Graph
            {
                ReferenceGraph.DepthBounded: true,
            } => DependencyInspectionTraversalCompletion.DepthBounded,
            LibraryDependencyGraphResult.Graph
                or LibraryDependencyGraphResult.Empty =>
                DependencyInspectionTraversalCompletion.Complete,
            _ => DependencyInspectionTraversalCompletion.Failed,
        };

    private static DependencyGraphNodeIdentity DependencyIdentity(
        PackageDependencyEvidenceRoot root) =>
        root.Identity switch
        {
            PackageDependencyEvidenceRootIdentity.Package package =>
                new DependencyGraphNodeIdentity.Package(
                    package.Coordinate.PackageId,
                    package.Coordinate.Version),
            PackageDependencyEvidenceRootIdentity.RestoredProject restored =>
                new DependencyGraphNodeIdentity.RestoredRoot(
                    restored.Identity),
            _ => throw new InvalidOperationException(
                "Asset-mode depends supports package and restored-project evidence roots."),
        };

    private static DependencyInspectionTraversalCompletion RootTraversal(
        DependencyEvidenceAcquiredRoot acquired,
        IReadOnlyDictionary<int, DependencyInspectionTraversalCompletion> packageCompletion,
        PackageDependencyEvidenceRoot root)
    {
        if (root.Identity is PackageDependencyEvidenceRootIdentity.Package)
        {
            return packageCompletion.GetValueOrDefault(
                acquired.Root.OccurrenceIndex,
                DependencyInspectionTraversalCompletion.Partial);
        }

        return acquired.RestoredTraversal switch
        {
            RestoredProjectDependencyTraversalResult.Available available =>
                Convert(available.Value.Completion),
            RestoredProjectDependencyTraversalResult.Unavailable =>
                DependencyInspectionTraversalCompletion.Partial,
            RestoredProjectDependencyTraversalResult.Failed =>
                DependencyInspectionTraversalCompletion.Failed,
            _ => DependencyInspectionTraversalCompletion.Partial,
        };
    }

    private static DependencyInspectionTraversalCompletion Convert(
        PackageDependencyTraversalRootCompletion completion) =>
        completion switch
        {
            PackageDependencyTraversalRootCompletion.Complete =>
                DependencyInspectionTraversalCompletion.Complete,
            PackageDependencyTraversalRootCompletion.DepthBounded =>
                DependencyInspectionTraversalCompletion.DepthBounded,
            PackageDependencyTraversalRootCompletion.SourceBounded =>
                DependencyInspectionTraversalCompletion.SourceBounded,
            PackageDependencyTraversalRootCompletion.Partial =>
                DependencyInspectionTraversalCompletion.Partial,
            _ => throw new InvalidOperationException(
                "Unknown package traversal completion."),
        };

    private static DependencyInspectionTraversalCompletion Convert(
        RestoredProjectTraversalCompletion completion) =>
        completion switch
        {
            RestoredProjectTraversalCompletion.Complete =>
                DependencyInspectionTraversalCompletion.Complete,
            RestoredProjectTraversalCompletion.DepthBounded =>
                DependencyInspectionTraversalCompletion.DepthBounded,
            RestoredProjectTraversalCompletion.Partial =>
                DependencyInspectionTraversalCompletion.Partial,
            _ => throw new InvalidOperationException(
                "Unknown restored traversal completion."),
        };

    private static string SourceFor(DependsAssetRoot root) =>
        root.Kind switch
        {
            DependencyInspectionRootKind.Package =>
                DependencyEvidenceAcquisition.IsLocalArchiveTarget(root.Value)
                    ? PackageDependencyEvidenceAcquisitionForm.PackageArchive
                        .ToString()
                    : PackageDependencyEvidenceAcquisitionForm
                        .PackageSourceManifest.ToString(),
            DependencyInspectionRootKind.Nuspec =>
                PackageDependencyEvidenceAcquisitionForm.DirectNuspec
                    .ToString(),
            DependencyInspectionRootKind.Project =>
                Path.GetFileName(root.Value.AsSpan()).Equals(
                    "project.assets.json",
                    StringComparison.OrdinalIgnoreCase)
                    ? PackageDependencyEvidenceAcquisitionForm.ProjectAssets
                        .ToString()
                    : PackageDependencyEvidenceAcquisitionForm.ProjectLocator
                        .ToString(),
            DependencyInspectionRootKind.Library => "Library",
            _ => throw new InvalidOperationException(
                "Unknown dependency root kind."),
        };

    internal static bool WriteAssetProjection(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        TextWriter? output = null)
    {
        output ??= Console.Out;
        DocumentSchema schema = options.Tabular && !options.Count
            ? DependsAssetSections.CreateTableSchema()
            : DependsAssetSections.CreateSchema();
        if (!TrySelectHierarchyRows(
                projection,
                options.QueryPlan,
                options.Rows,
                options.LegacyHierarchyWindowStageIndex,
                out IReadOnlyList<DependencyHierarchyOccurrenceRow>
                    hierarchyRows))
        {
            return false;
        }
        if (options.Count)
            return WriteAssetCount(
                projection,
                options,
                includeSections,
                schema,
                hierarchyRows,
                output);

        if (options.Tree || options.MermaidOutput)
        {
            DependencyHierarchyOutputAdapter.Write(
                projection.Hierarchy,
                hierarchyRows,
                options.MermaidOutput
                    ? OutputFormat.Mermaid
                    : OutputFormat.PlainText,
                tree: options.Tree,
                embeddedMermaid: false,
                options.NoHeader,
                options.CompactJson,
                output);
            return true;
        }

        if (options.JsonOutput
            && !IsColumnProjectionRequested(options))
        {
            DependsAssetDocument document = DependsAssetDocument.Create(
                projection,
                includeSections,
                options.Rows,
                hierarchyRows);
            output.WriteLine(
                JsonSerializer.Serialize(
                    document,
                    options.CompactJson
                        ? DependsAssetCompactJsonContext.Default
                            .DependsAssetDocument
                        : DependsAssetJsonContext.Default
                            .DependsAssetDocument));
            return true;
        }

        bool hasTraversalFailures = projection.Failures.Any(
            static failure => failure is DependencyInspectionFailure.Traversal);
        bool failuresSelected =
            includeSections.Contains(DependsAssetSections.Failures);
        bool hierarchyFailuresJsonl = options.Jsonl
            && includeSections.Count == 2
            && includeSections.Contains(
                DependsAssetSections.DependencyHierarchy)
            && failuresSelected;
        if (hierarchyFailuresJsonl)
        {
            WriteAssetHierarchyFailuresJsonLines(
                projection,
                options.Rows,
                hierarchyRows,
                output);
            return true;
        }
        if (hasTraversalFailures
            && failuresSelected
            && IsColumnProjectionRequested(options)
            && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write(
                "Projected JSON and JSONL columns cannot represent typed traversal failure detail; use unprojected --format json.");
            return false;
        }
        DependsAssetView view = BuildAssetView(
            projection,
            includeSections,
            options.Rows,
            hierarchyRows,
            options.EmbeddedMermaid);
        DependsAssetTableView tableView = BuildAssetTableView(
            projection,
            includeSections,
            options.Rows,
            hierarchyRows);
        if (options.JsonOutput)
        {
            MarkoutField[] summary = MarkoutFieldRecorder.Record(
                BuildAssetView(
                    projection,
                    NoAssetSections,
                    options.Rows,
                    hierarchyRows,
                    embeddedMermaid: false),
                DependsAssetViewContext.Default);
            OutputFormatter.WriteProjectedJson(
                output,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    WriteAssetSummarySection(
                        writer,
                        formatter,
                        writerOptions,
                        summary);
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        tableView,
                        writer,
                        formatter,
                        DependsAssetViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson);
            return true;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(
                output,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        tableView,
                        writer,
                        formatter,
                        DependsAssetViewContext.Default,
                        writerOptions);
                });
            return true;
        }

        if (options.Format == OutputFormat.Markdown)
        {
            if (IsColumnProjectionRequested(options))
            {
                WriteProjectedAssetMarkdown(
                    options,
                    includeSections,
                    tableView,
                    output);
            }
            else
            {
                WriteAssetMarkdown(
                    projection,
                    options,
                    includeSections,
                    hierarchyRows,
                    output);
            }
            return true;
        }
        if (options.Format == OutputFormat.PlainText
            && IsColumnProjectionRequested(options))
        {
            WriteProjectedAssetPlainText(
                projection,
                options,
                includeSections,
                tableView,
                hierarchyRows,
                output);
            return true;
        }

        var writerOptions = OutputFormatter.CreateWindowedOptions(
            rows: null,
            options.Columns,
            options.Fields);
        writerOptions.IncludeSections = includeSections;
        var writer = new MarkoutWriter(
            output,
            options.Format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(
                    options.EmbeddedMermaid
                        ? MarkdownGraphMode.Mermaid
                        : MarkdownGraphMode.EdgeTable),
            writerOptions);
        DependsAssetViewContext.Default.Serialize(view, writer);
        writer.Flush();
        return true;
    }

    private static void WriteAssetSummarySection(
        TextWriter writer,
        IMarkoutFormatter formatter,
        MarkoutWriterOptions writerOptions,
        MarkoutField[] summary)
    {
        if (summary.Length == 0)
            return;
        MarkoutWriter summaryWriter = MarkoutWriter.Create(
            writer,
            formatter,
            new MarkoutWriterOptions
            {
                HeadingLevelOffset = writerOptions.HeadingLevelOffset,
            });
        summaryWriter.WriteSectionStart(
            2,
            SummarySection);
        summaryWriter.WriteFields(summary);
        summaryWriter.WriteSectionEnd();
        summaryWriter.Flush();
    }

    private static readonly HashSet<string> NoAssetSections =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> HierarchyFailureSections =
        new(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Failures,
            ],
            StringComparer.OrdinalIgnoreCase);

    private static void WriteAssetHierarchyFailuresJsonLines(
        DependsAssetProjection projection,
        RowWindow? rows,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        TextWriter output)
    {
        DependsAssetDocument document = DependsAssetDocument.Create(
            projection,
            HierarchyFailureSections,
            rows,
            hierarchyRows);
        foreach (DependencyHierarchyJsonOccurrence occurrence in
                 document.DependencyHierarchy?.Occurrences ?? [])
        {
            output.WriteLine(
                JsonSerializer.Serialize(
                    new DependsAssetJsonLine
                    {
                        Kind = "dependency-hierarchy",
                        DependencyHierarchy = occurrence,
                    },
                    DependsAssetCompactJsonContext.Default
                        .DependsAssetJsonLine));
        }
        foreach (DependsFailureJson failure in document.Failures ?? [])
        {
            output.WriteLine(
                JsonSerializer.Serialize(
                    new DependsAssetJsonLine
                    {
                        Kind = "failure",
                        Failure = failure,
                    },
                    DependsAssetCompactJsonContext.Default
                        .DependsAssetJsonLine));
        }
    }

    private static void WriteAssetMarkdown(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        TextWriter output)
    {
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = includeSections,
            SectionOrder = DependsAssetSections.SectionOrder,
        };
        DependsAssetView view = BuildAssetView(
            projection,
            includeSections,
            options.Rows,
            hierarchyRows,
            options.EmbeddedMermaid);
        MarkoutSerializer.Serialize(
            DependsAssetMarkdownView.From(view),
            output,
            new MarkdownFormatter(
                options.EmbeddedMermaid
                    ? MarkdownGraphMode.Mermaid
                    : MarkdownGraphMode.FencedTree),
            DependsAssetViewContext.Default,
            writerOptions);
    }

    private static void WriteProjectedAssetMarkdown(
        DependsOptions options,
        HashSet<string> includeSections,
        DependsAssetTableView tableView,
        TextWriter output)
    {
        var writerOptions = OutputFormatter.CreateWindowedOptions(
            rows: null,
            options.Columns,
            options.Fields);
        writerOptions.IncludeSections = includeSections;
        writerOptions.SectionOrder = DependsAssetSections.SectionOrder;
        MarkoutSerializer.Serialize(
            tableView,
            output,
            new MarkdownFormatter(MarkdownGraphMode.EdgeTable),
            DependsAssetViewContext.Default,
            writerOptions);
    }

    private static void WriteProjectedAssetPlainText(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        DependsAssetTableView tableView,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        TextWriter output)
    {
        var summaryWriter = new MarkoutWriter(
            new PlainTextFormatter(),
            new MarkoutWriterOptions());
        DependsAssetViewContext.Default.Serialize(
            BuildAssetView(
                projection,
                NoAssetSections,
                options.Rows,
                hierarchyRows,
                embeddedMermaid: false),
            summaryWriter);

        var writerOptions = OutputFormatter.CreateWindowedOptions(
            rows: null,
            options.Columns,
            options.Fields);
        writerOptions.IncludeSections = includeSections;
        var tableWriter = new MarkoutWriter(
            new PlainTextFormatter(),
            writerOptions);
        DependsAssetViewContext.Default.Serialize(tableView, tableWriter);

        output.WriteLine(
            JoinMarkdown(
                summaryWriter.ToString(),
                tableWriter.ToString()));
    }

    internal static string RenderHierarchySection(
        DependsAssetProjection projection,
        RowWindow? rows,
        bool embeddedMermaid,
        string sectionName = DependsAssetSections.DependencyHierarchy)
        => RenderHierarchySection(
            projection,
            Window(projection.HierarchyRows, rows),
            embeddedMermaid,
            sectionName);

    internal static string RenderHierarchySection(
        DependsAssetProjection projection,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        bool embeddedMermaid,
        string sectionName = DependsAssetSections.DependencyHierarchy)
    {
        var writer = new MarkoutWriter(
            embeddedMermaid
                ? new MarkdownFormatter(MarkdownGraphMode.Mermaid)
                : new PlainTextFormatter());
        writer.WriteGraph(
            DependencyHierarchyOutputAdapter.ToGraph(
                projection.Hierarchy,
                hierarchyRows,
                markWindowedFragments: !embeddedMermaid));
        string hierarchy = writer.ToString().TrimEnd();
        if (embeddedMermaid)
            return $"## {sectionName}\n\n{hierarchy}";
        return $"## {sectionName}\n\n```text\n{hierarchy}\n```";
    }

    internal static string RenderHierarchyPlainTextSection(
        DependsAssetProjection projection,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        string sectionName = DependsAssetSections.DependencyHierarchy)
    {
        var writer = new MarkoutWriter(
            new PlainTextFormatter());
        writer.WriteHeading(2, sectionName);
        writer.WriteGraph(
            DependencyHierarchyOutputAdapter.ToGraph(
                projection.Hierarchy,
                hierarchyRows,
                markWindowedFragments: true));
        return writer.ToString().TrimEnd();
    }

    private static string JoinMarkdown(params string[] fragments) =>
        string.Join(
            Environment.NewLine + Environment.NewLine,
            fragments
                .Select(static fragment => fragment.Trim())
                .Where(static fragment => fragment.Length > 0));

    private static bool WriteAssetCount(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        DocumentSchema schema,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        TextWriter output)
    {
        string[] ordered =
        [
            .. DependsAssetSections.SectionOrder.Where(
                includeSections.Contains),
        ];
        if (ordered.Length == 0)
            ordered = [DependsAssetSections.DependencyHierarchy];
        foreach (string section in ordered)
        {
            if (IsExactAssetRowSet(projection, section))
                continue;
            CommandError.Write(
                $"--count cannot report an exact '{section}' count because the requested dependency evidence is incomplete.");
            return false;
        }

        var counts = new CountProjection();
        foreach (string section in ordered)
        {
            if (section.Equals(
                    DependsAssetSections.DependencyHierarchy,
                    StringComparison.OrdinalIgnoreCase))
            {
                counts.SetRows(section, hierarchyRows.Count);
                continue;
            }
            int count = DependsAssetSections.CountRows(projection, section);
            counts.SetRows(
                section,
                options.Rows is { IsUnlimited: false } window
                && DependsAssetSections.AppliesRowWindow(
                    includeSections,
                    section)
                    ? WindowCount(window, count)
                    : count);
        }

        CountOutput.Write(
            counts,
            ordered.Length > 1 ? ordered : null,
            options.JsonOutput ? OutputFormat.Json
                : options.Jsonl ? OutputFormat.Jsonl
                : options.Tsv ? OutputFormat.Tsv
                : options.Tabular ? OutputFormat.Table
                : OutputFormat.Markdown,
            options.NoHeader,
            output);
        return true;
    }

    internal static bool IsExactAssetRowSet(
        DependsAssetProjection projection,
        string section)
    {
        if (section.Equals(
                DependsAssetSections.Failures,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (section.Equals(
                DependsAssetSections.DependencyHierarchy,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.TraversalCompletion
                is DependencyInspectionTraversalCompletion.Complete
                    or DependencyInspectionTraversalCompletion.DepthBounded
                    or DependencyInspectionTraversalCompletion.SourceBounded;
        }
        if (section.Equals(
                DependsAssetSections.Roots,
                StringComparison.OrdinalIgnoreCase))
        {
            return !projection.Summary.IsPrefixRootSet
                || projection.Summary.RootSetCompletion
                    == DependencyInspectionRootSetCompletion.Complete;
        }
        if (section.Equals(
                DependsAssetSections.Dependencies,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                DependsAssetSections.DependencyGroups,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.DeclarationCompletion
                is DependencyInspectionEvidencePhaseCompletion.Complete
                    or DependencyInspectionEvidencePhaseCompletion.NotApplicable;
        }
        if (section.Equals(
                DependsAssetSections.Pruning,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.Pruning.Completion
                is DependencyInspectionPruningCompletion.Complete
                    or DependencyInspectionPruningCompletion.SourceBounded;
        }
        if (section.Equals(
                DependsAssetSections.Licenses,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.Licenses.Completion
                == DependencyInspectionLicenseCompletion.Complete;
        }
        if (section.Equals(
                DependsAssetSections.RestoredEdges,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                DependsAssetSections.RestoredPackages,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.RestoredRelationshipCompletion
                is DependencyInspectionEvidencePhaseCompletion.Complete
                    or DependencyInspectionEvidencePhaseCompletion.NotApplicable;
        }
        return false;
    }

    private static DependsAssetView BuildAssetView(
        DependsAssetProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows,
        bool embeddedMermaid)
    {
        DependencyInspectionSummary summary = projection.Summary;
        DependencyEvidenceSourceTokens sourceTokens =
            RootSourceTokens(projection);
        PackageDependencyEvidencePackagePrefixCompletion? prefix =
            summary.PackagePrefix;
        return new DependsAssetView
        {
            Description =
                sections.Contains(DependsAssetSections.DependencyHierarchy)
                && hierarchyRows.Count == 0
                ? "No dependency relationships."
                : null,
            RootSet = summary.RootSetCompletion.ToString(),
            RequestedRoots = summary.RequestedRoots,
            AdmittedRoots = summary.AdmittedRoots,
            FailedRoots = summary.FailedRoots,
            Traversal = summary.TraversalCompletion.ToString(),
            Declarations = summary.DeclarationCompletion.ToString(),
            RestoredRelationships =
                summary.RestoredRelationshipCompletion.ToString(),
            Pruning = summary.Pruning.Completion.ToString(),
            LicenseInventory = summary.Licenses.Completion.ToString(),
            LicensePackages = summary.Licenses.Completion
                    == DependencyInspectionLicenseCompletion.NotRequested
                ? null
                : summary.Licenses.Packages,
            LicenseAvailable = summary.Licenses.Completion
                    == DependencyInspectionLicenseCompletion.NotRequested
                ? null
                : summary.Licenses.Available,
            LicenseUnavailable = summary.Licenses.Completion
                    == DependencyInspectionLicenseCompletion.NotRequested
                ? null
                : summary.Licenses.Unavailable,
            PruningRoots = summary.Pruning.Completion
                    == DependencyInspectionPruningCompletion.NotRequested
                ? null
                : summary.Pruning.Roots,
            PruningDeclarations = summary.Pruning.Completion
                    == DependencyInspectionPruningCompletion.NotRequested
                ? null
                : summary.Pruning.Declarations,
            PruningEvaluated = summary.Pruning.Completion
                    == DependencyInspectionPruningCompletion.NotRequested
                ? null
                : summary.Pruning.Evaluated,
            PruningDelegated = summary.Pruning.Completion
                    == DependencyInspectionPruningCompletion.NotRequested
                ? null
                : summary.Pruning.Delegated,
            PruningRetained = summary.Pruning.Completion
                    == DependencyInspectionPruningCompletion.NotRequested
                ? null
                : summary.Pruning.Retained,
            RequestedDepth = summary.RequestedDepth,
            HierarchyOccurrences = summary.HierarchyOccurrences,
            CanonicalNodes = summary.CanonicalNodes,
            Relationships = summary.Relationships,
            PrefixText = prefix?.Prefix,
            PrefixCandidates = prefix?.Candidates,
            PrefixMatches = prefix?.Matches,
            PrefixFailures = prefix?.Failures,
            PrefixTruncation = prefix?.TruncationReason
                is { } reason and not PackageSearchTruncationReason.None
                    ? reason.ToString()
                    : null,
            DependencyHierarchy =
                sections.Contains(DependsAssetSections.DependencyHierarchy)
                    ? DependencyHierarchyOutputAdapter.ToGraph(
                        projection.Hierarchy,
                        hierarchyRows,
                        markWindowedFragments: !embeddedMermaid)
                    : null,
            Roots = Rows(
                sections,
                DependsAssetSections.Roots,
                projection.Roots,
                rows,
                row => DependsRootView.From(row, sourceTokens)),
            Dependencies = Rows(
                sections,
                DependsAssetSections.Dependencies,
                projection.Dependencies,
                rows,
                DependsDependencyView.From),
            Licenses = Rows(
                sections,
                DependsAssetSections.Licenses,
                projection.Licenses,
                rows,
                DependsLicenseView.From),
            PruningRows = Rows(
                sections,
                DependsAssetSections.Pruning,
                projection.Pruning,
                rows,
                DependsPruningView.From),
            RestoredEdges = Rows(
                sections,
                DependsAssetSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependsRestoredEdgeView.From),
            Failures = Rows(
                sections,
                DependsAssetSections.Failures,
                projection.Failures,
                rows,
                DependsFailureView.From),
            DependencyGroups = Rows(
                sections,
                DependsAssetSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependsDependencyGroupView.From),
            RestoredPackages = Rows(
                sections,
                DependsAssetSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependsRestoredPackageView.From),
        };
    }

    private static DependsAssetTableView BuildAssetTableView(
        DependsAssetProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> hierarchyRows)
    {
        DependencyEvidenceSourceTokens sourceTokens =
            RootSourceTokens(projection);
        return new DependsAssetTableView
        {
            DependencyHierarchy = Rows(
                sections,
                DependsAssetSections.DependencyHierarchy,
                hierarchyRows,
                window: null,
                DependsHierarchyOccurrenceView.From),
            Roots = Rows(
                sections,
                DependsAssetSections.Roots,
                projection.Roots,
                rows,
                row => DependsRootView.From(row, sourceTokens)),
            Dependencies = Rows(
                sections,
                DependsAssetSections.Dependencies,
                projection.Dependencies,
                rows,
                DependsDependencyView.From),
            Licenses = Rows(
                sections,
                DependsAssetSections.Licenses,
                projection.Licenses,
                rows,
                DependsLicenseView.From),
            Pruning = Rows(
                sections,
                DependsAssetSections.Pruning,
                projection.Pruning,
                rows,
                DependsPruningView.From),
            RestoredEdges = Rows(
                sections,
                DependsAssetSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependsRestoredEdgeView.From),
            Failures = Rows(
                sections,
                DependsAssetSections.Failures,
                projection.Failures,
                rows,
                DependsFailureView.From),
            DependencyGroups = Rows(
                sections,
                DependsAssetSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependsDependencyGroupView.From),
            RestoredPackages = Rows(
                sections,
                DependsAssetSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependsRestoredPackageView.From),
        };
    }

    private static DependencyEvidenceSourceTokens RootSourceTokens(
        DependsAssetProjection projection) =>
        DependencyEvidenceSourceTokens.Create(
        [
            projection.Summary.PackagePrefix?.Source,
            .. projection.Roots.Select(static root =>
                root.Evidence?.Source),
        ]);

    private static List<TView>? Rows<TRow, TView>(
        IReadOnlySet<string> sections,
        string section,
        IReadOnlyList<TRow> rows,
        RowWindow? window,
        Func<TRow, TView> select)
    {
        if (!sections.Contains(section))
            return null;
        IReadOnlyList<TRow> selected =
            DependsAssetSections.AppliesRowWindow(sections, section)
                ? Window(rows, window)
                : rows;
        return [.. selected.Select(select)];
    }

    private static IReadOnlyList<T> Window<T>(
        IReadOnlyList<T> rows,
        RowWindow? window) =>
        window is { IsUnlimited: false } bounded
            ? bounded.Apply(rows)
            : rows;

    internal static bool TrySelectHierarchyRows(
        DependsAssetProjection projection,
        DependencyQueryPlan? plan,
        RowWindow? legacyRows,
        int? legacyWindowStageIndex,
        out IReadOnlyList<DependencyHierarchyOccurrenceRow> selected)
    {
        if (projection.HierarchyRows.IsEmpty)
        {
            selected = projection.HierarchyRows;
            return true;
        }

        if (plan?.HierarchyRows is not { Operations.Count: > 0 } intent)
        {
            selected = Window(projection.HierarchyRows, legacyRows);
            return true;
        }

        if (legacyWindowStageIndex is null)
        {
            return CliSemanticRowSelection.TrySelect(
                intent,
                projection.HierarchyRows,
                DependsAssetSections.DependencyHierarchy,
                failure =>
                    HierarchyRowSelectionFailure(
                        failure.Failure.StageNumber,
                        failure.Failure.RequiredPosition,
                        failure.Failure.AvailableCount),
                out selected);
        }

        int legacyStage = legacyWindowStageIndex.Value;
        if (legacyStage < 0
            || legacyStage >= intent.Operations.Count
            || intent.Operations[legacyStage].Kind
                != RowSelectionStageKind.Window)
        {
            throw new InvalidOperationException(
                "The legacy hierarchy window stage does not identify "
                    + "a Window operation.");
        }

        IReadOnlyList<DependencyHierarchyOccurrenceRow> current =
            projection.HierarchyRows;
        for (int index = 0;
            index < intent.Operations.Count;
            index++)
        {
            RowSelectionIntentOperation<string> operation =
                intent.Operations[index];
            if (index == legacyStage)
            {
                current =
                    RowWindow.Range(
                            operation.Start ?? 1,
                            operation.End)
                        .Apply(current);
                continue;
            }

            int stageNumber = index + 1;
            if (!CliSemanticRowSelection.TrySelect(
                    RowSelectionIntent<string>.Create([operation]),
                    current,
                    DependsAssetSections.DependencyHierarchy,
                    failure =>
                        HierarchyRowSelectionFailure(
                            stageNumber,
                            failure.Failure.RequiredPosition,
                            failure.Failure.AvailableCount),
                    out current))
            {
                selected = Array.Empty<
                    DependencyHierarchyOccurrenceRow>();
                return false;
            }
        }

        selected = current;
        return true;
    }

    private static string HierarchyRowSelectionFailure(
        int stageNumber,
        int requiredPosition,
        int availableCount) =>
        $"Dependency Hierarchy row selection stage {stageNumber} "
            + $"requires row {requiredPosition}, but only "
            + $"{availableCount} hierarchy rows are available.";

    private static int WindowCount(RowWindow window, int rows)
    {
        (int start, int end) = window.Resolve(rows);
        return end - start;
    }

    private static bool IsColumnProjectionRequested(
        DependsOptions options) =>
        options.Fields is { Length: > 0 }
        || options.Columns is { Length: > 0 };

    internal static void WriteAssetDiagnostics(
        DependsAssetProjection projection)
    {
        if (!projection.Failures.IsEmpty)
        {
            CommandError.WriteWarning(
                $"{projection.Failures.Length} typed failure record(s) are reported; run with '-S {DependsAssetSections.Failures}' for the rows.");
        }
        if (projection.Summary.TraversalCompletion
            is DependencyInspectionTraversalCompletion.Partial
                or DependencyInspectionTraversalCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Dependency traversal completed as {projection.Summary.TraversalCompletion}.");
        }
        if (projection.Summary.DeclarationCompletion
            is DependencyInspectionEvidencePhaseCompletion.Partial
                or DependencyInspectionEvidencePhaseCompletion.Unavailable
                or DependencyInspectionEvidencePhaseCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Dependency declaration evidence completed as {projection.Summary.DeclarationCompletion}.");
        }
        if (projection.Summary.RestoredRelationshipCompletion
            is DependencyInspectionEvidencePhaseCompletion.Partial
                or DependencyInspectionEvidencePhaseCompletion.Unavailable
                or DependencyInspectionEvidencePhaseCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Restored relationship evidence completed as {projection.Summary.RestoredRelationshipCompletion}.");
        }
        if (projection.Summary.Pruning.Completion
            is DependencyInspectionPruningCompletion.Partial
                or DependencyInspectionPruningCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Package pruning evidence completed as {projection.Summary.Pruning.Completion}.");
        }
        if (projection.Summary.Licenses.Completion
            == DependencyInspectionLicenseCompletion.Partial)
        {
            CommandError.WriteWarning(
                "Package license inventory completed as Partial.");
        }
    }

    internal static int AssetExitCode(DependsAssetProjection projection) =>
        projection.Summary.TraversalCompletion
                is DependencyInspectionTraversalCompletion.Partial
                    or DependencyInspectionTraversalCompletion.Failed
            || projection.Summary.DeclarationCompletion
                is DependencyInspectionEvidencePhaseCompletion.Partial
                    or DependencyInspectionEvidencePhaseCompletion.Unavailable
                    or DependencyInspectionEvidencePhaseCompletion.Failed
            || projection.Summary.RestoredRelationshipCompletion
                is DependencyInspectionEvidencePhaseCompletion.Partial
                    or DependencyInspectionEvidencePhaseCompletion.Unavailable
                    or DependencyInspectionEvidencePhaseCompletion.Failed
            || projection.Summary.Pruning.Completion
                is DependencyInspectionPruningCompletion.Partial
                    or DependencyInspectionPruningCompletion.Failed
            || projection.Summary.Licenses.Completion
                == DependencyInspectionLicenseCompletion.Partial
            || projection.Summary.PackagePrefix is { } prefix
            && IsFailedPrefixTruncation(prefix.TruncationReason)
            || projection.Summary.FailedRoots > 0
                ? 1
                : 0;

    internal static bool IsFailedPrefixTruncation(
        PackageSearchTruncationReason reason) =>
        reason is not PackageSearchTruncationReason.None
            and not PackageSearchTruncationReason.RequestedLimit;

    private sealed record LibraryAssetResult(
        DependsAssetRoot Root,
        LibraryDependencyGraphResult Result,
        DependencyGraphDocument? Document);

    private sealed record DependsRootSelection(
        PackageDependencyEvidenceGroupIdentity? SelectedGroup,
        int? SelectedGroupIndex,
        PackageDependencyEvidenceGroupOccurrence? SelectedSourceOccurrence);

    private sealed record DependsLicenseProjectionResult(
        ImmutableArray<DependencyInspectionLicense> Rows,
        DependencyInspectionLicenseSummary Summary,
        ImmutableArray<DependencyInspectionFailure> Failures);
}
