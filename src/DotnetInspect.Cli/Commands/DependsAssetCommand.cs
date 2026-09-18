using System.Collections.Immutable;
using System.Text.Json;
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
    private static readonly InspectionEnvelopeJsonContract<
        DependencyInspectionContent> AssetDependencyJson =
            new(
                "asset-dependencies",
                1,
                DependencyInspectionJsonContext.Default
                    .DependencyInspectionContent);
    private static readonly EvidenceInspectionEnvelopeJsonContract<
        DependencyInspectionContent,
        DependencyInspectionEvidenceDocument> AssetDependencyEvidenceJson =
            new(
                AssetDependencyJson,
                DependencyInspectionJsonContext.Default
                    .DependencyInspectionEvidenceDocument);

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

        if (DependsShareProjection.ValidateOptions(options) is { } shareError)
        {
            CommandError.Write(shareError);
            return 1;
        }
        if (options.EnvelopeOutput
            && options.EvidenceEnvelopePath is null)
        {
            CommandError.Write(
                "--envelope currently requires --evidence-envelope in asset-mode depends.");
            return 1;
        }
        if (options.ShareFormat is not null
            && options.EvidenceEnvelopePath is null)
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
        if (options.SelectDefault && IsNetworkFreeAssetGraph(options))
            includeSections.Add(DependsAssetSections.DependencyGraph);
        DependsAssetRequestPlan plan =
            DependsAssetRequestPlan.FromSections(includeSections);
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
            var builder = new EvidenceInspectionBuilder<
                DependencyInspectionContent,
                DependencyInspectionEvidenceDocument>();
            builder.RequestEvidence(
                options.EvidenceEnvelopePath is not null
                || DependsAssetSections.RequestsEvidence(includeSections));
            var state = new DependsAssetInspectionState(
                options,
                context,
                plan,
                options.Effective && options.Depth is null
                    ? 1
                    : options.Depth,
                pruneSource);
            (
                InspectionEnvelope<DependencyInspectionContent> inspection,
                EvidenceInspectionEnvelope<
                    DependencyInspectionContent,
                    DependencyInspectionEvidenceDocument>? evidence) =
                await builder.BuildAsync(
                    state,
                    static (operation, token) =>
                        ExecuteAssetInspectionAsync(operation, token),
                    static (operation, token) =>
                        ExecuteAssetInspectionWithEvidenceAsync(
                            operation,
                            token),
                    cancellationToken)
                    .ConfigureAwait(false);
            DependsAssetProjection projection =
                state.Projection
                ?? throw new InvalidOperationException(
                    "Dependency inspection completed without its host projection.");
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

            int primaryExitCode = options.ShareFormat is { } shareFormat
                ? options.EnvelopeOutput
                    ? InspectionEnvelopeOutput.TryWrite(
                        inspection,
                        AssetDependencyJson,
                        includeEnvelope: true,
                        options.CompactJson)
                        ? 0
                        : 1
                    : WorkspaceShareOutput.WriteScalar(
                        inspection.Share,
                        shareFormat)
                : options.EnvelopeOutput
                    ? InspectionEnvelopeOutput.TryWrite(
                        inspection,
                        AssetDependencyJson,
                        includeEnvelope: true,
                        options.CompactJson)
                        ? 0
                        : 1
                    : WriteAssetProjection(
                        projection,
                        options,
                        includeSections)
                        ? 0
                        : 1;
            if (primaryExitCode != 0)
            {
                return primaryExitCode;
            }

            WriteAssetDiagnostics(projection);
            int exitCode = AssetExitCode(projection);
            if (options.EvidenceEnvelopePath is { } evidencePath)
            {
                if (evidence is null)
                {
                    CommandError.Write(
                        "Evidence envelopes are unavailable in this build.");
                    exitCode = 1;
                }
                else if (!InspectionEnvelopeOutput.TryWriteEvidence(
                    evidence,
                    AssetDependencyEvidenceJson,
                    evidencePath,
                    options.CompactJson))
                {
                    exitCode = 1;
                }
            }

            if (options is
                {
                    EnvelopeOutput: true,
                    ShareFormat: { } envelopeShareFormat,
                })
            {
                exitCode = Math.Max(
                    exitCode,
                    WorkspaceShareOutput.Write(
                        inspection.Share,
                        envelopeShareFormat));
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

    private static async ValueTask<
        InspectionEnvelope<DependencyInspectionContent>>
        ExecuteAssetInspectionAsync(
            DependsAssetInspectionState operation,
            CancellationToken cancellationToken)
    {
        (DependencyInspectionResult result, InspectionShare share) =
            await PrepareAssetInspectionAsync(
                operation,
                cancellationToken)
                .ConfigureAwait(false);
        return DependencyInspectionOperation.Execute(
            result,
            share);
    }

    private static async ValueTask<EvidenceInspectionEnvelope<
        DependencyInspectionContent,
        DependencyInspectionEvidenceDocument>>
        ExecuteAssetInspectionWithEvidenceAsync(
            DependsAssetInspectionState operation,
            CancellationToken cancellationToken)
    {
        (DependencyInspectionResult result, InspectionShare share) =
            await PrepareAssetInspectionAsync(
                operation,
                cancellationToken)
                .ConfigureAwait(false);
        return DependencyInspectionOperation.ExecuteWithEvidence(
            result,
            share);
    }

    private static async ValueTask<(
        DependencyInspectionResult Result,
        InspectionShare Share)> PrepareAssetInspectionAsync(
            DependsAssetInspectionState operation,
            CancellationToken cancellationToken)
    {
        DependsAssetProjection projection =
            await AcquireAssetProjectionAsync(
                operation.Options,
                operation.Context,
                operation.Plan,
                operation.TraversalDepth,
                operation.PruneSource,
                cancellationToken).ConfigureAwait(false);
        operation.Projection = projection;
        return (
            projection.Result,
            CreateAssetInspectionShare(
                operation.Options,
                projection));
    }

    private static InspectionShare CreateAssetInspectionShare(
        DependsOptions options,
        DependsAssetProjection projection) =>
        options.ShareFormat is not null
            && projection.Evidence.PackageInputs.Roots
                is [PackageDependencyEvidenceRoot
                {
                    Identity:
                        PackageDependencyEvidenceRootIdentity.Package
                            package,
                }]
            ? DependsShareProjection.ProjectAsset(
                options,
                package.Coordinate)
            : new InspectionShare.NonProjectable(
                "asset-dependencies/share",
                options.ShareFormat is null
                    ? "Share projection was not requested."
                    : "The asset dependency request is not one exact package root.");

    private sealed class DependsAssetInspectionState(
        DependsOptions options,
        CommandContext context,
        DependsAssetRequestPlan plan,
        int? traversalDepth,
        Func<string, InstalledPlatformPruneSource.Result> pruneSource)
    {
        internal DependsOptions Options { get; } = options;

        internal CommandContext Context { get; } = context;

        internal DependsAssetRequestPlan Plan { get; } = plan;

        internal int? TraversalDepth { get; } = traversalDepth;

        internal Func<string, InstalledPlatformPruneSource.Result>
            PruneSource { get; } = pruneSource;

        internal DependsAssetProjection? Projection { get; set; }
    }

    private static bool ValidateAssetOptions(
        DependsOptions options,
        HashSet<string>? selectedSections,
        IReadOnlyCollection<string> candidateSections,
        bool graphRequested,
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

        if (options.Depth is not null && !graphRequested)
        {
            CommandError.Write(
                "--depth requires the Dependency Graph section.");
            return false;
        }

        if (!discoveryMode
            && (options.Tree || options.MermaidOutput)
            && (candidateSections.Count != 1
                || !candidateSections.Contains(
                    DependsAssetSections.DependencyGraph,
                    StringComparer.OrdinalIgnoreCase)))
        {
            CommandError.Write(
                $"{(options.Tree ? "--tree" : "--mermaid")} requires exactly '-S \"{DependsAssetSections.DependencyGraph}\"'.");
            return false;
        }
        if (!discoveryMode
            && (options.Tree || options.MermaidOutput)
            && (options.Count || IsColumnProjectionRequested(options)))
        {
            CommandError.Write(
                $"{(options.Tree ? "--tree" : "--mermaid")} cannot combine with --count, --columns, or --fields.");
            return false;
        }

        bool graphFailuresJsonl =
            options.Jsonl
            && candidateSections.Count == 2
            && candidateSections.Contains(
                DependsAssetSections.DependencyGraph,
                StringComparer.OrdinalIgnoreCase)
            && candidateSections.Contains(
                DependsAssetSections.Failures,
                StringComparer.OrdinalIgnoreCase);
        if (!discoveryMode
            && options.Tabular
            && !options.Count
            && candidateSections.Count != 1
            && !graphFailuresJsonl)
        {
            string format = options.Jsonl
                ? "--jsonl"
                : options.Tsv
                    ? "--tsv"
                    : "--table";
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
                && (graphRequested
                    || candidateSections.Contains(
                        DependsAssetSections.Pruning,
                        StringComparer.OrdinalIgnoreCase)))
            && !hasPackageBackedLibrary)
        {
            CommandError.Write(
                "--source, --add-source, and --nugetconfig require a remote --package root or graph traversal from a local .nupkg root.");
            return false;
        }

        if (graphRequested
            && (hasRemotePackage
                || hasLocalPackage
                || hasNuspec
                || hasPrefix)
            && options.Tfm is { } framework
            && !PackageDependencyTraversalFrameworkMode.TryCreateExact(
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

    private static bool IsNetworkFreeAssetGraph(DependsOptions options) =>
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

    private static async Task<DependsAssetProjection>
        AcquireAssetProjectionAsync(
            DependsOptions options,
            CommandContext context,
            DependsAssetRequestPlan plan,
            int? traversalDepth,
            Func<string, InstalledPlatformPruneSource.Result> pruneSource,
            CancellationToken cancellationToken)
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
            acquisition =
                await DependencyEvidenceAcquisition.AcquireDependsRootsAsync(
                    options.AssetRoots,
                    evidenceOptions,
                    context.HttpClient,
                    context.Logger.Log,
                    composition,
                    operationContext,
                    plan.Traversal,
                    traversalDepth,
                    cancellationToken).ConfigureAwait(false);
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

        var evidenceDocument = new DependencyInspectionEvidenceDocument(
            evidenceOutcome,
            [
                .. admittedIndexes.Select(static occurrence =>
                    new DependencyRootOccurrenceIdentity(occurrence)),
            ],
            [
                .. failedIndexes.Select(static occurrence =>
                    occurrence is { } value
                        ? new DependencyRootOccurrenceIdentity(value)
                        : (DependencyRootOccurrenceIdentity?)null),
            ]);
        DependencyEvidenceProjection evidence =
            DependencyEvidenceProjection.Create(evidenceDocument);
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

        if (plan.Traversal && packageInputIndexes.Count > 0)
        {
            PackageDependencyTraversalFrameworkMode frameworkMode =
                options.Tfm is { } framework
                    ? CreateFrameworkMode(framework)
                    : new PackageDependencyTraversalFrameworkMode
                        .ManifestDefault();
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
                                        ? PackageDependencyTraversalExpansionAuthority
                                            .DirectDeclarationsOnly
                                        : PackageDependencyTraversalExpansionAuthority
                                            .RecursiveSources)),
                        ],
                        frameworkMode,
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
            graphDocuments.Add(
                DependencyGraphProjection.Package(
                    packageTraversal,
                    packageOccurrences));
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
        ImmutableArray<DependencyGraphEdgeRow> graphRows =
            [.. DependencyGraphOutputAdapter.EdgeRows(graph)];
        ImmutableArray<DependencyInspectionFailure> failures =
        [
            .. BuildFailures(
                evidence,
                packageTraversal,
                packageOccurrences,
                libraries,
                acquisition,
                plan),
            .. pruning.Failures,
        ];
        ImmutableArray<DependsRootRow> roots = BuildRoots(
            options,
            evidenceOutcome,
            evidence,
            admittedIndexes,
            acquisition,
            packageTraversal,
            packageOccurrences,
            libraries,
            plan,
            graph);
        ImmutableArray<DependencyInspectionDependency> dependencies =
            plan.Declarations
                ? BuildDependencies(evidence)
                : [];
        DependencyInspectionSummary summary = BuildSummary(
            options,
            evidence,
            roots,
            graph,
            plan,
            traversalDepth,
            pruning.Summary);

        return new DependsAssetProjection(
            summary,
            graph,
            graphRows,
            roots,
            dependencies,
            pruning.Rows,
            plan.RestoredRelationships
                ? evidence.RestoredEdges
                : [],
            failures,
            plan.Declarations
                ? evidence.DependencyGroups
                : [],
            plan.RestoredRelationships
                ? evidence.RestoredPackages
                : [],
            evidenceDocument);
    }

    private static DependencyEvidenceAcquisitionOptions EvidenceOptions(
        DependsOptions options) =>
        new()
        {
            Tfm = options.Tfm,
            IncludePrerelease = options.IncludePrerelease,
            MaxPackages = options.MaxPackages,
            SourceOptions = options.SourceOptions,
        };

    private static PackageDependencyTraversalFrameworkMode.Exact
        CreateFrameworkMode(string framework)
    {
        if (PackageDependencyTraversalFrameworkMode.TryCreateExact(
                framework,
                out PackageDependencyTraversalFrameworkMode.Exact mode))
        {
            return mode;
        }

        throw new InvalidOperationException(
            $"Target framework '{framework}' was not validated.");
    }

    private static async Task<List<LibraryAssetResult>>
        AcquireLibraryRootsAsync(
            DependsOptions options,
            CommandContext context,
            bool graphRequested,
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
                        graphRequested ? traversalDepth : 0,
                        cancellationToken,
                        traverseReferences: graphRequested,
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

    private static ImmutableArray<DependencyInspectionFailure> BuildFailures(
        DependencyEvidenceProjection evidence,
        PackageDependencyTraversalOutcome? packageTraversal,
        IReadOnlyList<int> packageOccurrences,
        IReadOnlyList<LibraryAssetResult> libraries,
        DependencyEvidenceAcquisitionBatch? acquisition,
        DependsAssetRequestPlan plan)
    {
        var failures = ImmutableArray.CreateBuilder<DependencyInspectionFailure>();
        failures.AddRange(
            evidence.Failures
                .Where(failure => failure.Phase switch
                {
                    DependencyEvidenceFailurePhase.Root
                        or DependencyEvidenceFailurePhase.PackageProfile =>
                            true,
                    DependencyEvidenceFailurePhase.Declaration =>
                        plan.Declarations,
                    DependencyEvidenceFailurePhase.Graph =>
                        plan.RestoredRelationships,
                    _ => false,
                })
                .Select(static failure =>
                    new DependencyInspectionFailure.Evidence(failure)));

        if (plan.Traversal && packageTraversal is not null)
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
                            failed.Outcome,
                            ManifestFailure: null,
                            BudgetKind: null,
                            BudgetLimit: null,
                            RestoredFailure: null,
                            MapAffectedRoots(
                                packageOccurrences,
                                failed.AffectedRootOccurrences))));
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
                            failure.Detail,
                            BudgetKind: null,
                            BudgetLimit: failure.Detail
                                is PackageDependencyTraversalManifestFailureDetail
                                    .ManifestProjectionBudgetExhausted budget
                                    ? budget.Limit
                                    : null,
                            RestoredFailure: null,
                            MapAffectedRoots(
                                packageOccurrences,
                                failure.AffectedRootOccurrences))));
            }
        }

        if (plan.Traversal && acquisition is not null)
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
                                        .Outcome(failed.Failure),
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

    private static ImmutableArray<DependencyInspectionDependency> BuildDependencies(
        DependencyEvidenceProjection evidence)
    {
        var restoredByDeclaration = evidence.RestoredEdges
            .Where(static edge => edge.DeclarationAssociation is not null)
            .ToDictionary(
                static edge => (
                    edge.RootIndex,
                    edge.RootIdentity,
                    edge.DeclarationAssociation!.Value),
                static edge => edge);
        return
        [
            .. evidence.Dependencies.Select(declaration =>
            {
                DependencyEvidenceRestoredEdgeRow? restored =
                    restoredByDeclaration.TryGetValue(
                        (
                            declaration.RootIndex,
                            declaration.RootIdentity,
                            declaration.DeclarationIdentity),
                        out DependencyEvidenceRestoredEdgeRow? associated)
                    && associated.Dependency.Coordinate.PackageId.Equals(
                        declaration.PackageId,
                        StringComparison.Ordinal)
                        ? associated
                        : null;
                return new DependencyInspectionDependency(
                    declaration,
                    restored?.PackageVersion,
                    restored?.Dependency,
                    restored?.Identity);
            }),
        ];
    }

    private static ImmutableArray<DependsRootRow> BuildRoots(
        DependsOptions options,
        PackageDependencyEvidenceOutcome evidenceOutcome,
        DependencyEvidenceProjection evidence,
        IReadOnlyList<int> admittedIndexes,
        DependencyEvidenceAcquisitionBatch? acquisition,
        PackageDependencyTraversalOutcome? packageTraversal,
        IReadOnlyList<int> packageOccurrences,
        IReadOnlyList<LibraryAssetResult> libraries,
        DependsAssetRequestPlan plan,
        DependencyGraphDocument graph)
    {
        Dictionary<int, DependencyEvidenceRootRow> evidenceRows =
            evidence.Roots.ToDictionary(static root => root.RootIndex);
        var roots = ImmutableArray.CreateBuilder<DependsRootRow>();
        var packageCompletion = new Dictionary<int, DependencyInspectionTraversalCompletion>();
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
        bool selectionRequested = plan.Declarations || plan.Traversal;
        if (acquisition is not null)
        {
            foreach (DependencyEvidenceAcquiredRoot acquired in acquisition.Roots)
            {
                if (acquired.InputIndex is { } inputIndex)
                {
                    PackageDependencyEvidenceRoot evidenceRoot =
                        evidenceOutcome.Roots[inputIndex];
                    DependencyEvidenceRootRow evidenceRow =
                        evidenceRows[acquired.Root.OccurrenceIndex];
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
                        ?? GraphIdentity(evidenceRoot);
                    DependsRootSelection selection = RootSelection(
                        evidenceRow,
                        evidence,
                        options.Tfm,
                        selectionRequested);
                    roots.Add(
                        new DependsRootRow(
                            acquired.Root.OccurrenceIndex,
                            acquired.Root.Kind,
                            new InertString(
                                TextPolicy.Field,
                                acquired.Root.Value),
                            evidenceRoot.Provenance.AcquisitionForm.ToString(),
                            DependencyInspectionRootState.Admitted,
                            identity is null
                                ? null
                                : DependencyGraphOutputAdapter.Kind(identity),
                            identity is null
                                ? null
                                : DependencyGraphOutputAdapter.IdentityText(
                                    identity),
                            traversal,
                            plan.Declarations
                                ? DeclarationState(evidenceRow)
                                : DependencyInspectionEvidenceAvailability.NotRequested,
                            plan.Declarations
                                ? DeclarationCompletion(evidenceRow)
                                : DependencyInspectionEvidencePhaseCompletion.NotRequested,
                            selection.Status,
                            plan.RestoredRelationships
                                ? RelationshipState(evidenceRow)
                                : DependencyInspectionEvidenceAvailability.NotRequested,
                            plan.RestoredRelationships
                                ? RelationshipCompletion(evidenceRow)
                                : DependencyInspectionEvidencePhaseCompletion.NotRequested)
                        {
                            GraphIdentity = identity,
                            Evidence = evidenceRow,
                            SelectedGroup = selection.SelectedGroup,
                            SelectedGroupIndex =
                                selection.SelectedGroupIndex,
                            SelectedSourceOccurrence =
                                selection.SelectedSourceOccurrence,
                            RequestedFramework =
                                selection.RequestedFramework,
                            SelectedFramework =
                                selection.SelectedFramework,
                        });
                }
                else
                {
                    roots.Add(
                        new DependsRootRow(
                            acquired.Root.OccurrenceIndex,
                            acquired.Root.Kind,
                            new InertString(
                                TextPolicy.Field,
                                acquired.Root.Value),
                            SourceFor(acquired.Root),
                            DependencyInspectionRootState.Failed,
                            identityKind: null,
                            identity: null,
                            plan.Traversal
                                ? DependencyInspectionTraversalCompletion.Failed
                                : DependencyInspectionTraversalCompletion.NotRequested,
                            plan.Declarations
                                ? DependencyInspectionEvidenceAvailability.Failed
                                : DependencyInspectionEvidenceAvailability.NotRequested,
                            plan.Declarations
                                ? DependencyInspectionEvidencePhaseCompletion.Failed
                                : DependencyInspectionEvidencePhaseCompletion.NotRequested,
                            selectionRequested
                                ? DependencyInspectionSelectionStatus.Unavailable
                                : DependencyInspectionSelectionStatus.NotRequested,
                            plan.RestoredRelationships
                                ? DependencyInspectionEvidenceAvailability.Failed
                                : DependencyInspectionEvidenceAvailability.NotRequested,
                            plan.RestoredRelationships
                                ? DependencyInspectionEvidencePhaseCompletion.Failed
                                : DependencyInspectionEvidencePhaseCompletion.NotRequested));
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
                DependencyEvidenceRootRow evidenceRow =
                    evidenceRows[occurrence];
                DependencyGraphNodeIdentity? identity =
                    graphIdentityByOccurrence.GetValueOrDefault(occurrence)
                    ?? GraphIdentity(evidenceRoot);
                DependsRootSelection selection = RootSelection(
                    evidenceRow,
                    evidence,
                    options.Tfm,
                    selectionRequested);
                roots.Add(
                    new DependsRootRow(
                        occurrence,
                        DependencyInspectionRootKind.Package,
                        evidenceRoot.Display,
                        "PackagePrefix",
                        DependencyInspectionRootState.Admitted,
                        identity is null
                            ? null
                            : DependencyGraphOutputAdapter.Kind(identity),
                        identity is null
                            ? null
                            : DependencyGraphOutputAdapter.IdentityText(
                                identity),
                        plan.Traversal
                            ? packageCompletion.GetValueOrDefault(
                                occurrence,
                                DependencyInspectionTraversalCompletion.Partial)
                            : DependencyInspectionTraversalCompletion.NotRequested,
                        plan.Declarations
                            ? DeclarationState(evidenceRow)
                            : DependencyInspectionEvidenceAvailability.NotRequested,
                        plan.Declarations
                            ? DeclarationCompletion(evidenceRow)
                            : DependencyInspectionEvidencePhaseCompletion.NotRequested,
                        selection.Status,
                        plan.RestoredRelationships
                            ? RelationshipState(evidenceRow)
                            : DependencyInspectionEvidenceAvailability.NotRequested,
                        plan.RestoredRelationships
                            ? RelationshipCompletion(evidenceRow)
                            : DependencyInspectionEvidencePhaseCompletion.NotRequested)
                    {
                        GraphIdentity = identity,
                        Evidence = evidenceRow,
                        SelectedGroup = selection.SelectedGroup,
                        SelectedGroupIndex = selection.SelectedGroupIndex,
                        SelectedSourceOccurrence =
                            selection.SelectedSourceOccurrence,
                        RequestedFramework = selection.RequestedFramework,
                        SelectedFramework = selection.SelectedFramework,
                    });
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
                new DependsRootRow(
                    library.Root.OccurrenceIndex,
                    DependencyInspectionRootKind.Library,
                    new InertString(
                        TextPolicy.Field,
                        library.Root.Value),
                    LibrarySource(library.Result),
                    admitted
                        ? DependencyInspectionRootState.Admitted
                        : DependencyInspectionRootState.Failed,
                    identity is null
                        ? null
                        : DependencyGraphOutputAdapter.Kind(identity),
                    identity is null
                        ? null
                        : DependencyGraphOutputAdapter.IdentityText(identity),
                    !plan.Traversal
                        ? DependencyInspectionTraversalCompletion.NotRequested
                        : LibraryCompletion(library),
                    plan.Declarations
                        ? DependencyInspectionEvidenceAvailability.NotApplicable
                        : DependencyInspectionEvidenceAvailability.NotRequested,
                    plan.Declarations
                        ? DependencyInspectionEvidencePhaseCompletion.NotApplicable
                        : DependencyInspectionEvidencePhaseCompletion.NotRequested,
                    selectionRequested
                        ? DependencyInspectionSelectionStatus.NotApplicable
                        : DependencyInspectionSelectionStatus.NotRequested,
                    plan.RestoredRelationships
                        ? DependencyInspectionEvidenceAvailability.NotApplicable
                        : DependencyInspectionEvidenceAvailability.NotRequested,
                    plan.RestoredRelationships
                        ? DependencyInspectionEvidencePhaseCompletion.NotApplicable
                        : DependencyInspectionEvidencePhaseCompletion.NotRequested)
                {
                    GraphIdentity = identity,
                });
        }

        return [.. roots.OrderBy(static root => root.Occurrence)];
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

    private static DependencyInspectionEvidenceAvailability DeclarationState(
        DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependencyInspectionEvidenceAvailability.NotApplicable,
            DependencyEvidenceDeclarationState.Available =>
                DependencyInspectionEvidenceAvailability.Available,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependencyInspectionEvidenceAvailability.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependencyInspectionEvidenceAvailability.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration state."),
        };

    private static DependencyInspectionEvidencePhaseCompletion DeclarationCompletion(
        DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceDeclarationState.Available
                when root.DeclarationCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependencyInspectionEvidencePhaseCompletion.Complete,
            DependencyEvidenceDeclarationState.Available =>
                DependencyInspectionEvidencePhaseCompletion.Partial,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependencyInspectionEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependencyInspectionEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration completion."),
        };

    private static DependencyInspectionEvidenceAvailability RelationshipState(
        DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependencyInspectionEvidenceAvailability.NotApplicable,
            DependencyEvidenceGraphState.Available =>
                DependencyInspectionEvidenceAvailability.Available,
            DependencyEvidenceGraphState.Unavailable =>
                DependencyInspectionEvidenceAvailability.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependencyInspectionEvidenceAvailability.Failed,
            _ => throw new InvalidOperationException(
                "Unknown restored-relationship state."),
        };

    private static DependsRootSelection RootSelection(
        DependencyEvidenceRootRow root,
        DependencyEvidenceProjection evidence,
        string? requestedFramework,
        bool requested)
    {
        if (!requested)
        {
            return new DependsRootSelection(
                DependencyInspectionSelectionStatus.NotRequested,
                SelectedGroup: null,
                SelectedGroupIndex: null,
                SelectedSourceOccurrence: null,
                RequestedFramework: null,
                SelectedFramework: null);
        }

        if (root.Owner
            != PackageDependencyEvidenceInputKind.RestoredProject)
        {
            return new DependsRootSelection(
                PackageSelectionStatus(root),
                root.SelectedGroup,
                root.SelectedGroupIndex,
                root.SelectedSourceOccurrence,
                root.RequestedFramework,
                root.SelectedFramework);
        }

        InertString? requestedFrameworkText =
            requestedFramework is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    requestedFramework);
        if (root.RestoredTargetFrameworkIdentity is not { } selectedFramework)
        {
            return new DependsRootSelection(
                requestedFramework is null
                    ? DependencyInspectionSelectionStatus.Unavailable
                    : DependencyInspectionSelectionStatus.NoMatchingTargetFramework,
                SelectedGroup: null,
                SelectedGroupIndex: null,
                SelectedSourceOccurrence: null,
                requestedFrameworkText,
                SelectedFramework: null);
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
            DependencyInspectionSelectionStatus.Selected,
            selectedGroup?.Identity,
            selectedGroup?.GroupIndex,
            selectedOccurrence,
            requestedFrameworkText,
            root.RestoredTargetFrameworkSpelling);
    }

    private static DependencyInspectionSelectionStatus PackageSelectionStatus(
        DependencyEvidenceRootRow root) =>
        root.SelectionStatus switch
        {
            PackageDependencyEvidenceSelectionStatus.Selected =>
                DependencyInspectionSelectionStatus.Selected,
            PackageDependencyEvidenceSelectionStatus.NoDependencyGroups =>
                DependencyInspectionSelectionStatus.NoDependencyGroups,
            PackageDependencyEvidenceSelectionStatus
                .NoMatchingTargetFramework =>
                    DependencyInspectionSelectionStatus.NoMatchingTargetFramework,
            PackageDependencyEvidenceSelectionStatus.Unavailable =>
                DependencyInspectionSelectionStatus.Unavailable,
            _ => throw new InvalidOperationException(
                "Unknown dependency selection status."),
        };

    private static DependencyInspectionEvidencePhaseCompletion RelationshipCompletion(
        DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceGraphState.Available
                when root.GraphCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependencyInspectionEvidencePhaseCompletion.Complete,
            DependencyEvidenceGraphState.Available =>
                DependencyInspectionEvidencePhaseCompletion.Partial,
            DependencyEvidenceGraphState.Unavailable =>
                DependencyInspectionEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependencyInspectionEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown restored-relationship completion."),
        };

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

    private static DependencyGraphNodeIdentity GraphIdentity(
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

    private static DependencyInspectionSummary BuildSummary(
        DependsOptions options,
        DependencyEvidenceProjection evidence,
        ImmutableArray<DependsRootRow> roots,
        DependencyGraphDocument graph,
        DependsAssetRequestPlan plan,
        int? traversalDepth,
        DependencyInspectionPruningSummary pruning)
    {
        int admitted = roots.Count(
            static root => root.State == DependencyInspectionRootState.Admitted);
        int failed = options.PackagePrefix is null
            ? roots.Length - admitted
            : evidence.Summary.FailedRootCount
                + evidence.Summary.RejectedRootCount;
        int requested = options.PackagePrefix is null
            ? options.AssetRoots.Length
            : evidence.Summary.AdmittedRootCount
                + evidence.Summary.FailedRootCount
                + evidence.Summary.RejectedRootCount;
        DependencyInspectionRootSetCompletion rootSet =
            failed == 0
            && IsCompleteCommandRootSet(
                evidence.Summary.RootSetCompletion,
                evidence.Summary.RejectedRootCount,
                evidence.Summary.FailedRootCount,
                evidence.Summary.IsTruncated,
                evidence.Summary.PackagePrefix?.TruncationReason)
                ? DependencyInspectionRootSetCompletion.Complete
                : admitted == 0
                    ? DependencyInspectionRootSetCompletion.Failed
                    : DependencyInspectionRootSetCompletion.Partial;
        DependencyInspectionTraversalCompletion traversal = plan.Traversal
            ? AggregateTraversal(roots)
            : DependencyInspectionTraversalCompletion.NotRequested;
        return new DependencyInspectionSummary(
            rootSet,
            requested,
            admitted,
            failed,
            traversal,
            plan.Traversal ? traversalDepth : null,
            graph.Nodes.Length,
            graph.Edges.Length,
            AggregateEvidencePhase(
                roots,
                static root => root.DeclarationCompletion,
                plan.Declarations),
            AggregateEvidencePhase(
                roots,
                static root => root.RestoredRelationshipCompletion,
                plan.RestoredRelationships),
            pruning,
            options.PackagePrefix is not null,
            evidence.Summary.PackagePrefix);
    }

    internal static bool IsCompleteCommandRootSet(
        PackageDependencyEvidenceRootSetCompletion completion,
        int rejectedRootCount,
        int failedRootCount,
        bool isTruncated,
        PackageSearchTruncationReason? truncationReason) =>
        completion == PackageDependencyEvidenceRootSetCompletion.Complete
        || rejectedRootCount == 0
        && failedRootCount == 0
        && isTruncated
        && truncationReason == PackageSearchTruncationReason.RequestedLimit;

    private static DependencyInspectionEvidencePhaseCompletion AggregateEvidencePhase(
        ImmutableArray<DependsRootRow> roots,
        Func<DependsRootRow, DependencyInspectionEvidencePhaseCompletion> select,
        bool requested)
    {
        if (!requested)
            return DependencyInspectionEvidencePhaseCompletion.NotRequested;

        DependencyInspectionEvidencePhaseCompletion[] states =
        [
            .. roots.Select(select),
        ];
        if (states.Length == 0
            || states.All(static state =>
                state == DependencyInspectionEvidencePhaseCompletion.Failed))
        {
            return DependencyInspectionEvidencePhaseCompletion.Failed;
        }
        if (states.Any(static state =>
            state is DependencyInspectionEvidencePhaseCompletion.Partial
                or DependencyInspectionEvidencePhaseCompletion.Failed)
            || states.Any(static state =>
                state == DependencyInspectionEvidencePhaseCompletion.Unavailable)
            && states.Any(static state =>
                state == DependencyInspectionEvidencePhaseCompletion.Complete))
        {
            return DependencyInspectionEvidencePhaseCompletion.Partial;
        }
        if (states.Any(static state =>
            state == DependencyInspectionEvidencePhaseCompletion.Unavailable))
        {
            return DependencyInspectionEvidencePhaseCompletion.Unavailable;
        }
        if (states.Any(static state =>
            state == DependencyInspectionEvidencePhaseCompletion.Complete))
        {
            return DependencyInspectionEvidencePhaseCompletion.Complete;
        }
        return DependencyInspectionEvidencePhaseCompletion.NotApplicable;
    }

    private static DependencyInspectionTraversalCompletion AggregateTraversal(
        ImmutableArray<DependsRootRow> roots)
    {
        if (roots.IsEmpty
            || roots.All(static root =>
                root.Traversal == DependencyInspectionTraversalCompletion.Failed))
        {
            return DependencyInspectionTraversalCompletion.Failed;
        }
        if (roots.Any(static root =>
            root.Traversal is DependencyInspectionTraversalCompletion.Partial
                or DependencyInspectionTraversalCompletion.Failed))
        {
            return DependencyInspectionTraversalCompletion.Partial;
        }
        if (roots.Any(static root =>
            root.Traversal == DependencyInspectionTraversalCompletion.DepthBounded))
        {
            return DependencyInspectionTraversalCompletion.DepthBounded;
        }
        if (roots.Any(static root =>
            root.Traversal == DependencyInspectionTraversalCompletion.SourceBounded))
        {
            return DependencyInspectionTraversalCompletion.SourceBounded;
        }
        return DependencyInspectionTraversalCompletion.Complete;
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

    private static bool WriteAssetProjection(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections)
    {
        DocumentSchema schema = options.Tabular && !options.Count
            ? DependsAssetSections.CreateTableSchema()
            : DependsAssetSections.CreateSchema();
        if (options.Count)
            return WriteAssetCount(
                projection,
                options,
                includeSections,
                schema);

        IReadOnlyList<DependencyGraphEdgeRow> graphRows =
            Window(projection.GraphRows, options.Rows);
        if (options.Tree || options.MermaidOutput)
        {
            DependencyGraphOutputAdapter.Write(
                projection.Graph,
                graphRows,
                options.MermaidOutput
                    ? OutputFormat.Mermaid
                    : OutputFormat.PlainText,
                tree: options.Tree,
                embeddedMermaid: false,
                options.NoHeader,
                options.CompactJson);
            return true;
        }

        if (options.JsonOutput
            && !IsColumnProjectionRequested(options))
        {
            DependsAssetDocument document = DependsAssetDocument.Create(
                projection,
                includeSections,
                options.Rows);
            Console.WriteLine(
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
        bool graphFailuresJsonl = options.Jsonl
            && includeSections.Count == 2
            && includeSections.Contains(
                DependsAssetSections.DependencyGraph)
            && failuresSelected;
        if (graphFailuresJsonl)
        {
            if (IsColumnProjectionRequested(options))
            {
                CommandError.Write(
                    "Projected JSONL columns cannot represent the discriminated Dependency Graph and Failures records; remove --columns/--fields.");
                return false;
            }
            WriteAssetGraphFailuresJsonLines(
                projection,
                options.Rows);
            return true;
        }
        if (hasTraversalFailures
            && failuresSelected
            && IsColumnProjectionRequested(options)
            && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write(
                "Projected JSON and JSONL columns cannot represent typed traversal failure detail; use unprojected --json.");
            return false;
        }
        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                includeSections,
                options.Fields,
                options.Columns))
        {
            return false;
        }

        DependsAssetView view = BuildAssetView(
            projection,
            includeSections,
            options.Rows,
            options.EmbeddedMermaid);
        DependsAssetTableView tableView = BuildAssetTableView(
            projection,
            includeSections,
            options.Rows);
        if (options.JsonOutput)
        {
            MarkoutField[] summary = MarkoutFieldRecorder.Record(
                BuildAssetView(
                    projection,
                    NoAssetSections,
                    options.Rows,
                    embeddedMermaid: false),
                DependsAssetViewContext.Default);
            OutputFormatter.WriteProjectedJson(
                Console.Out,
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
                Console.Out,
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
                    projection,
                    options,
                    includeSections,
                    tableView);
            }
            else
            {
                WriteAssetMarkdown(
                    projection,
                    options,
                    includeSections);
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
                tableView);
            return true;
        }

        var writerOptions = OutputFormatter.CreateWindowedOptions(
            rows: null,
            options.Columns,
            options.Fields);
        writerOptions.IncludeSections = includeSections;
        var writer = new MarkoutWriter(
            Console.Out,
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

    private static readonly HashSet<string> GraphFailureSections =
        new(
            [
                DependsAssetSections.DependencyGraph,
                DependsAssetSections.Failures,
            ],
            StringComparer.OrdinalIgnoreCase);

    private static void WriteAssetGraphFailuresJsonLines(
        DependsAssetProjection projection,
        RowWindow? rows)
    {
        DependsAssetDocument document = DependsAssetDocument.Create(
            projection,
            GraphFailureSections,
            rows);
        foreach (DependencyGraphJsonEdge edge in
                 document.DependencyGraph?.Edges ?? [])
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new DependsAssetJsonLine
                    {
                        Kind = "dependency-graph",
                        DependencyGraph = edge,
                    },
                    DependsAssetCompactJsonContext.Default
                        .DependsAssetJsonLine));
        }
        foreach (DependsFailureJson failure in document.Failures ?? [])
        {
            Console.WriteLine(
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
        HashSet<string> includeSections)
    {
        string summary = MarkoutSerializer.Serialize(
            BuildAssetView(
                projection,
                NoAssetSections,
                options.Rows,
                options.EmbeddedMermaid),
            DependsAssetViewContext.Default,
            new MarkoutWriterOptions());
        var sections = new HashSet<string>(
            includeSections,
            StringComparer.OrdinalIgnoreCase);
        bool includeGraph = sections.Remove(
            DependsAssetSections.DependencyGraph);
        string graph = includeGraph
            ? RenderGraphSection(
                projection,
                options.Rows,
                options.EmbeddedMermaid)
            : "";
        string evidence = "";
        if (sections.Count > 0)
        {
            var writerOptions = new MarkoutWriterOptions
            {
                IncludeSections = sections,
            };
            var writer = new MarkoutWriter(
                new MarkdownFormatter(MarkdownGraphMode.EdgeTable),
                writerOptions);
            DependsAssetViewContext.Default.Serialize(
                BuildAssetTableView(
                    projection,
                    sections,
                    options.Rows),
                writer);
            evidence = writer.ToString();
        }

        Console.Out.WriteLine(
            JoinMarkdown(summary, graph, evidence));
    }

    private static void WriteProjectedAssetMarkdown(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        DependsAssetTableView tableView)
    {
        string summary = MarkoutSerializer.Serialize(
            BuildAssetView(
                projection,
                NoAssetSections,
                options.Rows,
                embeddedMermaid: false),
            DependsAssetViewContext.Default,
            new MarkoutWriterOptions());
        var writerOptions = OutputFormatter.CreateWindowedOptions(
            rows: null,
            options.Columns,
            options.Fields);
        writerOptions.IncludeSections = includeSections;
        var writer = new MarkoutWriter(
            new MarkdownFormatter(MarkdownGraphMode.EdgeTable),
            writerOptions);
        DependsAssetViewContext.Default.Serialize(tableView, writer);
        Console.Out.WriteLine(
            JoinMarkdown(summary, writer.ToString()));
    }

    private static void WriteProjectedAssetPlainText(
        DependsAssetProjection projection,
        DependsOptions options,
        HashSet<string> includeSections,
        DependsAssetTableView tableView)
    {
        var summaryWriter = new MarkoutWriter(
            new PlainTextFormatter(),
            new MarkoutWriterOptions());
        DependsAssetViewContext.Default.Serialize(
            BuildAssetView(
                projection,
                NoAssetSections,
                options.Rows,
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

        Console.Out.WriteLine(
            JoinMarkdown(
                summaryWriter.ToString(),
                tableWriter.ToString()));
    }

    private static string RenderGraphSection(
        DependsAssetProjection projection,
        RowWindow? rows,
        bool embeddedMermaid)
    {
        var writer = new MarkoutWriter(
            embeddedMermaid
                ? new MarkdownFormatter(MarkdownGraphMode.Mermaid)
                : new PlainTextFormatter());
        writer.WriteGraph(
            DependencyGraphOutputAdapter.ToGraph(
                projection.Graph,
                Window(projection.GraphRows, rows),
                markWindowedFragments: !embeddedMermaid,
                occurrenceAwareRoots: !embeddedMermaid));
        string graph = writer.ToString().TrimEnd();
        if (embeddedMermaid)
            return $"## {DependsAssetSections.DependencyGraph}\n\n{graph}";
        return $"## {DependsAssetSections.DependencyGraph}\n\n```text\n{graph}\n```";
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
        DocumentSchema schema)
    {
        string[] ordered =
        [
            .. DependsAssetSections.SectionOrder.Where(
                includeSections.Contains),
        ];
        if (ordered.Length == 0)
            ordered = [DependsAssetSections.DependencyGraph];
        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                ordered,
                options.Fields,
                options.Columns))
        {
            return false;
        }

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
            options.NoHeader);
        return true;
    }

    private static bool IsExactAssetRowSet(
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
                DependsAssetSections.DependencyGraph,
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
                sections.Contains(DependsAssetSections.DependencyGraph)
                && projection.GraphRows.IsEmpty
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
            GraphNodes = summary.GraphNodes,
            GraphEdges = summary.GraphEdges,
            PrefixText = prefix?.Prefix,
            PrefixCandidates = prefix?.Candidates,
            PrefixMatches = prefix?.Matches,
            PrefixFailures = prefix?.Failures,
            PrefixTruncation = prefix?.TruncationReason
                is { } reason and not PackageSearchTruncationReason.None
                    ? reason.ToString()
                    : null,
            DependencyGraph =
                sections.Contains(DependsAssetSections.DependencyGraph)
                    ? DependencyGraphOutputAdapter.ToGraph(
                        projection.Graph,
                        Window(projection.GraphRows, rows),
                        markWindowedFragments: !embeddedMermaid,
                        occurrenceAwareRoots: !embeddedMermaid)
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
        RowWindow? rows)
    {
        DependencyEvidenceSourceTokens sourceTokens =
            RootSourceTokens(projection);
        return new DependsAssetTableView
        {
            DependencyGraph = Rows(
                sections,
                DependsAssetSections.DependencyGraph,
                projection.GraphRows,
                rows,
                DependsGraphEdgeView.From),
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
        ImmutableArray<TRow> rows,
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

    private static int WindowCount(RowWindow window, int rows)
    {
        (int start, int end) = window.Resolve(rows);
        return end - start;
    }

    private static bool IsColumnProjectionRequested(
        DependsOptions options) =>
        options.Fields is { Length: > 0 }
        || options.Columns is { Length: > 0 };

    private static void WriteAssetDiagnostics(
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
    }

    private static int AssetExitCode(DependsAssetProjection projection) =>
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
        DependencyInspectionSelectionStatus Status,
        PackageDependencyEvidenceGroupIdentity? SelectedGroup,
        int? SelectedGroupIndex,
        PackageDependencyEvidenceGroupOccurrence? SelectedSourceOccurrence,
        InertString? RequestedFramework,
        InertString? SelectedFramework);
}
