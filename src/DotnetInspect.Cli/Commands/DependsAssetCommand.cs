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

    public static async Task<int> ExecuteAssetDependsAsync(
        DependsOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (DependsShareProjection.ValidateOptions(options) is { } shareError)
        {
            CommandError.Write(shareError);
            return 1;
        }
        if (options.ShareFormat is not null)
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
                tree: options.Tree,
                json: options.JsonOutput,
                tsv: options.Tsv,
                jsonl: options.Jsonl,
                sectionCostAnnotations:
                    catalog.Pipeline.GetCostAnnotations(),
                sectionCategories: catalog.SelectionCategoryMap,
                projection: options);
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
            DependsAssetProjection projection =
                await AcquireAssetProjectionAsync(
                    options,
                    context,
                    plan,
                    options.Effective && options.Depth is null
                        ? 1
                        : options.Depth,
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
                    tree: options.Tree,
                    markdown: !options.Tabular && !options.JsonOutput,
                    json: options.JsonOutput,
                    tsv: options.Tsv,
                    jsonl: options.Jsonl,
                    verbosity: (int)options.Verbosity,
                    fullSchema: DependsAssetSections.CreateSchema(),
                    sectionCostAnnotations:
                        catalog.Pipeline.GetCostAnnotations(),
                    sectionCategories: catalog.SelectionCategoryMap,
                    projection: options);
                WriteAssetDiagnostics(projection);
                return Math.Max(
                    discoveryExitCode,
                    AssetExitCode(projection));
            }
            if (!WriteAssetProjection(
                    projection,
                    options,
                    includeSections))
            {
                return 1;
            }

            WriteAssetDiagnostics(projection);
            return AssetExitCode(projection);
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
        bool graphRequested,
        bool discoveryMode)
    {
        if (options.Tfm is { } platformTfm
            && options.AssetRoots.Any(root =>
                root.Kind == DependsAssetRootKind.Library
                && !File.Exists(root.Value)
                && PlatformResolver.IsPlatformCandidate(root.Value))
            && !PlatformResolver.TryGetFrameworkSpecsForTargetFramework(
                platformTfm,
                out _))
        {
            CommandError.Write(
                $"Target framework '{platformTfm}' cannot select an installed platform library.");
            return false;
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
            root.Kind is DependsAssetRootKind.Package
                or DependsAssetRootKind.Nuspec
                or DependsAssetRootKind.Project);
        bool hasRemotePackage = options.AssetRoots.Any(root =>
            root.Kind == DependsAssetRootKind.Package
            && !DependencyEvidenceAcquisition.IsLocalArchiveTarget(
                root.Value));
        bool hasLocalPackage = options.AssetRoots.Any(root =>
            root.Kind == DependsAssetRootKind.Package
            && DependencyEvidenceAcquisition.IsLocalArchiveTarget(
                root.Value));
        bool hasNuspec = options.AssetRoots.Any(root =>
            root.Kind == DependsAssetRootKind.Nuspec);
        bool hasPackageBackedLibrary = options.AssetRoots.Any(root =>
            root.Kind == DependsAssetRootKind.Library
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
            if (root.Kind != DependsAssetRootKind.Package
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
            && !(hasLocalPackage && graphRequested)
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
                root => root.Kind == DependsAssetRootKind.Library)
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
                DependsAssetSections.SectionOrder,
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
            DependsAssetRootKind.Nuspec or DependsAssetRootKind.Project => true,
            DependsAssetRootKind.Library =>
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
            CancellationToken cancellationToken)
    {
        DependencyEvidenceOptions evidenceOptions = EvidenceOptions(options);
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
                await DependencyEvidenceCommand.AcquirePrefixAsync(
                    evidenceOptions,
                    prefix,
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

        DependencyEvidenceProjection evidence =
            DependencyEvidenceProjection.Create(
                evidenceOutcome,
                admittedIndexes,
                failedIndexes);
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
                         root.Root.Kind == DependsAssetRootKind.Project
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

        List<LibraryAssetResult> libraries =
            await AcquireLibraryRootsAsync(
                options,
                context,
                plan.Traversal,
                traversalDepth,
                cancellationToken).ConfigureAwait(false);
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
        ImmutableArray<DependsFailureRow> failures =
            BuildFailures(
                evidence,
                packageTraversal,
                packageOccurrences,
                libraries,
                acquisition,
                plan);
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
        ImmutableArray<DependsDependencyRow> dependencies =
            plan.Declarations
                ? BuildDependencies(evidence)
                : [];
        DependsAssetSummary summary = BuildSummary(
            options,
            evidence,
            roots,
            graph,
            plan,
            traversalDepth);

        return new DependsAssetProjection(
            summary,
            graph,
            graphRows,
            roots,
            dependencies,
            plan.RestoredRelationships
                ? evidence.RestoredEdges
                : [],
            failures,
            plan.Declarations
                ? evidence.DependencyGroups
                : [],
            plan.RestoredRelationships
                ? evidence.RestoredPackages
                : []);
    }

    private static DependencyEvidenceOptions EvidenceOptions(
        DependsOptions options) =>
        new()
        {
            Packages =
            [
                .. options.AssetRoots
                    .Where(root => root.Kind == DependsAssetRootKind.Package)
                    .Select(root => root.Value),
            ],
            Nuspecs =
            [
                .. options.AssetRoots
                    .Where(root => root.Kind == DependsAssetRootKind.Nuspec)
                    .Select(root => root.Value),
            ],
            Projects =
            [
                .. options.AssetRoots
                    .Where(root => root.Kind == DependsAssetRootKind.Project)
                    .Select(root => root.Value),
            ],
            PackagePrefix = options.PackagePrefix,
            Tfm = options.Tfm,
            IncludePrerelease = options.IncludePrerelease,
            MaxPackages = options.MaxPackages,
            Verbose = options.Verbose,
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
                     root => root.Kind == DependsAssetRootKind.Library))
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

    private static ImmutableArray<DependsFailureRow> BuildFailures(
        DependencyEvidenceProjection evidence,
        PackageDependencyTraversalOutcome? packageTraversal,
        IReadOnlyList<int> packageOccurrences,
        IReadOnlyList<LibraryAssetResult> libraries,
        DependencyEvidenceAcquisitionBatch? acquisition,
        DependsAssetRequestPlan plan)
    {
        var failures = ImmutableArray.CreateBuilder<DependsFailureRow>();
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
                    new DependsFailureRow.Evidence(failure)));

        if (plan.Traversal && packageTraversal is not null)
        {
            foreach (PackageDependencyTraversalFailedResolutionNode failed in
                     packageTraversal.FailedResolutions)
            {
                failures.Add(
                    new DependsFailureRow.Traversal(
                        new DependsTraversalFailureRow(
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
                    new DependsFailureRow.Traversal(
                        new DependsTraversalFailureRow(
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
                    new DependsFailureRow.Traversal(
                        new DependsTraversalFailureRow(
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
                                new DependsFailureRow.Traversal(
                                    new DependsTraversalFailureRow(
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
                                        new DependsRestoredTraversalFailure
                                            .Graph(graphFailure),
                                        [acquired.Root.OccurrenceIndex])));
                        }
                        break;
                    case RestoredProjectDependencyTraversalResult.Failed
                        failed:
                        failures.Add(
                            new DependsFailureRow.Traversal(
                                new DependsTraversalFailureRow(
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
                                    new DependsRestoredTraversalFailure
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
                        new DependsFailureRow.Traversal(
                            new DependsTraversalFailureRow(
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
                                new DependsAssemblyBindingFailure(
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
                new DependsFailureRow.Evidence(
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

    private static ImmutableArray<DependsDependencyRow> BuildDependencies(
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
                return new DependsDependencyRow(
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
        var packageCompletion = new Dictionary<int, DependsTraversalCompletion>();
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
                    DependsTraversalCompletion traversal =
                        plan.Traversal
                            ? RootTraversal(
                                acquired,
                                packageCompletion,
                                evidenceRoot)
                            : DependsTraversalCompletion.NotRequested;
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
                            DependsRootState.Admitted,
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
                                : DependsEvidenceAvailability.NotRequested,
                            plan.Declarations
                                ? DeclarationCompletion(evidenceRow)
                                : DependsEvidencePhaseCompletion.NotRequested,
                            selection.Status,
                            plan.RestoredRelationships
                                ? RelationshipState(evidenceRow)
                                : DependsEvidenceAvailability.NotRequested,
                            plan.RestoredRelationships
                                ? RelationshipCompletion(evidenceRow)
                                : DependsEvidencePhaseCompletion.NotRequested)
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
                            DependsRootState.Failed,
                            IdentityKind: null,
                            Identity: null,
                            plan.Traversal
                                ? DependsTraversalCompletion.Failed
                                : DependsTraversalCompletion.NotRequested,
                            plan.Declarations
                                ? DependsEvidenceAvailability.Failed
                                : DependsEvidenceAvailability.NotRequested,
                            plan.Declarations
                                ? DependsEvidencePhaseCompletion.Failed
                                : DependsEvidencePhaseCompletion.NotRequested,
                            selectionRequested
                                ? DependsSelectionStatus.Unavailable
                                : DependsSelectionStatus.NotRequested,
                            plan.RestoredRelationships
                                ? DependsEvidenceAvailability.Failed
                                : DependsEvidenceAvailability.NotRequested,
                            plan.RestoredRelationships
                                ? DependsEvidencePhaseCompletion.Failed
                                : DependsEvidencePhaseCompletion.NotRequested));
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
                        DependsAssetRootKind.Package,
                        evidenceRoot.Display,
                        "PackagePrefix",
                        DependsRootState.Admitted,
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
                                DependsTraversalCompletion.Partial)
                            : DependsTraversalCompletion.NotRequested,
                        plan.Declarations
                            ? DeclarationState(evidenceRow)
                            : DependsEvidenceAvailability.NotRequested,
                        plan.Declarations
                            ? DeclarationCompletion(evidenceRow)
                            : DependsEvidencePhaseCompletion.NotRequested,
                        selection.Status,
                        plan.RestoredRelationships
                            ? RelationshipState(evidenceRow)
                            : DependsEvidenceAvailability.NotRequested,
                        plan.RestoredRelationships
                            ? RelationshipCompletion(evidenceRow)
                            : DependsEvidencePhaseCompletion.NotRequested)
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
                    DependsAssetRootKind.Library,
                    new InertString(
                        TextPolicy.Field,
                        library.Root.Value),
                    LibrarySource(library.Result),
                    admitted
                        ? DependsRootState.Admitted
                        : DependsRootState.Failed,
                    identity is null
                        ? null
                        : DependencyGraphOutputAdapter.Kind(identity),
                    identity is null
                        ? null
                        : DependencyGraphOutputAdapter.IdentityText(identity),
                    !plan.Traversal
                        ? DependsTraversalCompletion.NotRequested
                        : LibraryCompletion(library),
                    plan.Declarations
                        ? DependsEvidenceAvailability.NotApplicable
                        : DependsEvidenceAvailability.NotRequested,
                    plan.Declarations
                        ? DependsEvidencePhaseCompletion.NotApplicable
                        : DependsEvidencePhaseCompletion.NotRequested,
                    selectionRequested
                        ? DependsSelectionStatus.NotApplicable
                        : DependsSelectionStatus.NotRequested,
                    plan.RestoredRelationships
                        ? DependsEvidenceAvailability.NotApplicable
                        : DependsEvidenceAvailability.NotRequested,
                    plan.RestoredRelationships
                        ? DependsEvidencePhaseCompletion.NotApplicable
                        : DependsEvidencePhaseCompletion.NotRequested)
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

    private static DependsEvidenceAvailability DeclarationState(
        DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependsEvidenceAvailability.NotApplicable,
            DependencyEvidenceDeclarationState.Available =>
                DependsEvidenceAvailability.Available,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependsEvidenceAvailability.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependsEvidenceAvailability.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration state."),
        };

    private static DependsEvidencePhaseCompletion DeclarationCompletion(
        DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependsEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceDeclarationState.Available
                when root.DeclarationCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependsEvidencePhaseCompletion.Complete,
            DependencyEvidenceDeclarationState.Available =>
                DependsEvidencePhaseCompletion.Partial,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependsEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependsEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration completion."),
        };

    private static DependsEvidenceAvailability RelationshipState(
        DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependsEvidenceAvailability.NotApplicable,
            DependencyEvidenceGraphState.Available =>
                DependsEvidenceAvailability.Available,
            DependencyEvidenceGraphState.Unavailable =>
                DependsEvidenceAvailability.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependsEvidenceAvailability.Failed,
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
                DependsSelectionStatus.NotRequested,
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
                    ? DependsSelectionStatus.Unavailable
                    : DependsSelectionStatus.NoMatchingTargetFramework,
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
            DependsSelectionStatus.Selected,
            selectedGroup?.Identity,
            selectedGroup?.GroupIndex,
            selectedOccurrence,
            requestedFrameworkText,
            root.RestoredTargetFrameworkSpelling);
    }

    private static DependsSelectionStatus PackageSelectionStatus(
        DependencyEvidenceRootRow root) =>
        root.SelectionStatus switch
        {
            PackageDependencyEvidenceSelectionStatus.Selected =>
                DependsSelectionStatus.Selected,
            PackageDependencyEvidenceSelectionStatus.NoDependencyGroups =>
                DependsSelectionStatus.NoDependencyGroups,
            PackageDependencyEvidenceSelectionStatus
                .NoMatchingTargetFramework =>
                    DependsSelectionStatus.NoMatchingTargetFramework,
            PackageDependencyEvidenceSelectionStatus.Unavailable =>
                DependsSelectionStatus.Unavailable,
            _ => throw new InvalidOperationException(
                "Unknown dependency selection status."),
        };

    private static DependsEvidencePhaseCompletion RelationshipCompletion(
        DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependsEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceGraphState.Available
                when root.GraphCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependsEvidencePhaseCompletion.Complete,
            DependencyEvidenceGraphState.Available =>
                DependsEvidencePhaseCompletion.Partial,
            DependencyEvidenceGraphState.Unavailable =>
                DependsEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependsEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown restored-relationship completion."),
        };

    private static DependsTraversalCompletion LibraryCompletion(
        LibraryAssetResult library) =>
        library.Result switch
        {
            LibraryDependencyGraphResult.Graph
            {
                ReferenceGraph.HasFailures: true,
            } => DependsTraversalCompletion.Partial,
            LibraryDependencyGraphResult.Graph
            {
                ReferenceGraph.DepthBounded: true,
            } => DependsTraversalCompletion.DepthBounded,
            LibraryDependencyGraphResult.Graph
                or LibraryDependencyGraphResult.Empty =>
                DependsTraversalCompletion.Complete,
            _ => DependsTraversalCompletion.Failed,
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

    private static DependsTraversalCompletion RootTraversal(
        DependencyEvidenceAcquiredRoot acquired,
        IReadOnlyDictionary<int, DependsTraversalCompletion> packageCompletion,
        PackageDependencyEvidenceRoot root)
    {
        if (root.Identity is PackageDependencyEvidenceRootIdentity.Package)
        {
            return packageCompletion.GetValueOrDefault(
                acquired.Root.OccurrenceIndex,
                DependsTraversalCompletion.Partial);
        }

        return acquired.RestoredTraversal switch
        {
            RestoredProjectDependencyTraversalResult.Available available =>
                Convert(available.Value.Completion),
            RestoredProjectDependencyTraversalResult.Unavailable =>
                DependsTraversalCompletion.Partial,
            RestoredProjectDependencyTraversalResult.Failed =>
                DependsTraversalCompletion.Failed,
            _ => DependsTraversalCompletion.Partial,
        };
    }

    private static DependsAssetSummary BuildSummary(
        DependsOptions options,
        DependencyEvidenceProjection evidence,
        ImmutableArray<DependsRootRow> roots,
        DependencyGraphDocument graph,
        DependsAssetRequestPlan plan,
        int? traversalDepth)
    {
        int admitted = roots.Count(
            static root => root.State == DependsRootState.Admitted);
        int failed = options.PackagePrefix is null
            ? roots.Length - admitted
            : evidence.Summary.FailedRootCount
                + evidence.Summary.RejectedRootCount;
        int requested = options.PackagePrefix is null
            ? options.AssetRoots.Length
            : evidence.Summary.AdmittedRootCount
                + evidence.Summary.FailedRootCount
                + evidence.Summary.RejectedRootCount;
        DependsRootSetCompletion rootSet =
            failed == 0
            && IsCompleteCommandRootSet(
                evidence.Summary.RootSetCompletion,
                evidence.Summary.RejectedRootCount,
                evidence.Summary.FailedRootCount,
                evidence.Summary.IsTruncated,
                evidence.Summary.PackagePrefix?.TruncationReason)
                ? DependsRootSetCompletion.Complete
                : admitted == 0
                    ? DependsRootSetCompletion.Failed
                    : DependsRootSetCompletion.Partial;
        DependsTraversalCompletion traversal = plan.Traversal
            ? AggregateTraversal(roots)
            : DependsTraversalCompletion.NotRequested;
        return new DependsAssetSummary(
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

    private static DependsEvidencePhaseCompletion AggregateEvidencePhase(
        ImmutableArray<DependsRootRow> roots,
        Func<DependsRootRow, DependsEvidencePhaseCompletion> select,
        bool requested)
    {
        if (!requested)
            return DependsEvidencePhaseCompletion.NotRequested;

        DependsEvidencePhaseCompletion[] states =
        [
            .. roots.Select(select),
        ];
        if (states.Length == 0
            || states.All(static state =>
                state == DependsEvidencePhaseCompletion.Failed))
        {
            return DependsEvidencePhaseCompletion.Failed;
        }
        if (states.Any(static state =>
            state is DependsEvidencePhaseCompletion.Partial
                or DependsEvidencePhaseCompletion.Failed)
            || states.Any(static state =>
                state == DependsEvidencePhaseCompletion.Unavailable)
            && states.Any(static state =>
                state == DependsEvidencePhaseCompletion.Complete))
        {
            return DependsEvidencePhaseCompletion.Partial;
        }
        if (states.Any(static state =>
            state == DependsEvidencePhaseCompletion.Unavailable))
        {
            return DependsEvidencePhaseCompletion.Unavailable;
        }
        if (states.Any(static state =>
            state == DependsEvidencePhaseCompletion.Complete))
        {
            return DependsEvidencePhaseCompletion.Complete;
        }
        return DependsEvidencePhaseCompletion.NotApplicable;
    }

    private static DependsTraversalCompletion AggregateTraversal(
        ImmutableArray<DependsRootRow> roots)
    {
        if (roots.IsEmpty
            || roots.All(static root =>
                root.Traversal == DependsTraversalCompletion.Failed))
        {
            return DependsTraversalCompletion.Failed;
        }
        if (roots.Any(static root =>
            root.Traversal is DependsTraversalCompletion.Partial
                or DependsTraversalCompletion.Failed))
        {
            return DependsTraversalCompletion.Partial;
        }
        if (roots.Any(static root =>
            root.Traversal == DependsTraversalCompletion.DepthBounded))
        {
            return DependsTraversalCompletion.DepthBounded;
        }
        if (roots.Any(static root =>
            root.Traversal == DependsTraversalCompletion.SourceBounded))
        {
            return DependsTraversalCompletion.SourceBounded;
        }
        return DependsTraversalCompletion.Complete;
    }

    private static DependsTraversalCompletion Convert(
        PackageDependencyTraversalRootCompletion completion) =>
        completion switch
        {
            PackageDependencyTraversalRootCompletion.Complete =>
                DependsTraversalCompletion.Complete,
            PackageDependencyTraversalRootCompletion.DepthBounded =>
                DependsTraversalCompletion.DepthBounded,
            PackageDependencyTraversalRootCompletion.SourceBounded =>
                DependsTraversalCompletion.SourceBounded,
            PackageDependencyTraversalRootCompletion.Partial =>
                DependsTraversalCompletion.Partial,
            _ => throw new InvalidOperationException(
                "Unknown package traversal completion."),
        };

    private static DependsTraversalCompletion Convert(
        RestoredProjectTraversalCompletion completion) =>
        completion switch
        {
            RestoredProjectTraversalCompletion.Complete =>
                DependsTraversalCompletion.Complete,
            RestoredProjectTraversalCompletion.DepthBounded =>
                DependsTraversalCompletion.DepthBounded,
            RestoredProjectTraversalCompletion.Partial =>
                DependsTraversalCompletion.Partial,
            _ => throw new InvalidOperationException(
                "Unknown restored traversal completion."),
        };

    private static string SourceFor(DependsAssetRoot root) =>
        root.Kind switch
        {
            DependsAssetRootKind.Package =>
                DependencyEvidenceAcquisition.IsLocalArchiveTarget(root.Value)
                    ? PackageDependencyEvidenceAcquisitionForm.PackageArchive
                        .ToString()
                    : PackageDependencyEvidenceAcquisitionForm
                        .PackageSourceManifest.ToString(),
            DependsAssetRootKind.Nuspec =>
                PackageDependencyEvidenceAcquisitionForm.DirectNuspec
                    .ToString(),
            DependsAssetRootKind.Project =>
                Path.GetFileName(root.Value.AsSpan()).Equals(
                    "project.assets.json",
                    StringComparison.OrdinalIgnoreCase)
                    ? PackageDependencyEvidenceAcquisitionForm.ProjectAssets
                        .ToString()
                    : PackageDependencyEvidenceAcquisitionForm.ProjectLocator
                        .ToString(),
            DependsAssetRootKind.Library => "Library",
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
            static failure => failure is DependsFailureRow.Traversal);
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
            DependencyEvidenceCommand.SummarySection);
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
                is DependsTraversalCompletion.Complete
                    or DependsTraversalCompletion.DepthBounded
                    or DependsTraversalCompletion.SourceBounded;
        }
        if (section.Equals(
                DependsAssetSections.Roots,
                StringComparison.OrdinalIgnoreCase))
        {
            return !projection.Summary.IsPrefixRootSet
                || projection.Summary.RootSetCompletion
                    == DependsRootSetCompletion.Complete;
        }
        if (section.Equals(
                DependsAssetSections.Dependencies,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                DependsAssetSections.DependencyGroups,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.DeclarationCompletion
                is DependsEvidencePhaseCompletion.Complete
                    or DependsEvidencePhaseCompletion.NotApplicable;
        }
        if (section.Equals(
                DependsAssetSections.RestoredEdges,
                StringComparison.OrdinalIgnoreCase)
            || section.Equals(
                DependsAssetSections.RestoredPackages,
                StringComparison.OrdinalIgnoreCase))
        {
            return projection.Summary.RestoredRelationshipCompletion
                is DependsEvidencePhaseCompletion.Complete
                    or DependsEvidencePhaseCompletion.NotApplicable;
        }
        return false;
    }

    private static DependsAssetView BuildAssetView(
        DependsAssetProjection projection,
        IReadOnlySet<string> sections,
        RowWindow? rows,
        bool embeddedMermaid)
    {
        DependsAssetSummary summary = projection.Summary;
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
        IReadOnlyList<TRow> selected = Window(rows, window);
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
            is DependsTraversalCompletion.Partial
                or DependsTraversalCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Dependency traversal completed as {projection.Summary.TraversalCompletion}.");
        }
        if (projection.Summary.DeclarationCompletion
            is DependsEvidencePhaseCompletion.Partial
                or DependsEvidencePhaseCompletion.Unavailable
                or DependsEvidencePhaseCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Dependency declaration evidence completed as {projection.Summary.DeclarationCompletion}.");
        }
        if (projection.Summary.RestoredRelationshipCompletion
            is DependsEvidencePhaseCompletion.Partial
                or DependsEvidencePhaseCompletion.Unavailable
                or DependsEvidencePhaseCompletion.Failed)
        {
            CommandError.WriteWarning(
                $"Restored relationship evidence completed as {projection.Summary.RestoredRelationshipCompletion}.");
        }
    }

    private static int AssetExitCode(DependsAssetProjection projection) =>
        projection.Summary.TraversalCompletion
                is DependsTraversalCompletion.Partial
                    or DependsTraversalCompletion.Failed
            || projection.Summary.DeclarationCompletion
                is DependsEvidencePhaseCompletion.Partial
                    or DependsEvidencePhaseCompletion.Unavailable
                    or DependsEvidencePhaseCompletion.Failed
            || projection.Summary.RestoredRelationshipCompletion
                is DependsEvidencePhaseCompletion.Partial
                    or DependsEvidencePhaseCompletion.Unavailable
                    or DependsEvidencePhaseCompletion.Failed
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
        DependsSelectionStatus Status,
        PackageDependencyEvidenceGroupIdentity? SelectedGroup,
        int? SelectedGroupIndex,
        PackageDependencyEvidenceGroupOccurrence? SelectedSourceOccurrence,
        InertString? RequestedFramework,
        InertString? SelectedFramework);
}
