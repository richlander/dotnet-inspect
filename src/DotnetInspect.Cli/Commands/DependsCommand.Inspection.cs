using System.Collections.Immutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public static partial class DependsCommand
{
    internal static async Task<int> ExecuteAsync(
        DependsOptions options,
        CancellationToken cancellationToken = default,
        Func<Task<DependsOptions>>? bindTypeScope = null)
    {
        bool typeMode = options.Roots is [{ Kind: DependencyRootKind.Type }];
        bool networkGraph = !typeMode && options.Packages.Length > 0;
        SectionCatalog<DependencyEvidenceProjection> catalog =
            DependencySections.CreateCatalog(networkGraph);
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Effective && options.Discover is { Length: > 0 } ? options.Discover : options.Select,
            catalog.SelectableSectionNames, catalog.InfoSectionNames,
            catalog.SelectionCategoryMap, options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;
        if (options.Discover is not null && options.Depth is not null)
            return Invalid("--depth requests traversal, not schema discovery.");
        if (options.Discover is not null && (options.Format == OutputFormat.Mermaid || options.EmbeddedMermaid))
            return Invalid("--mermaid renders Dependency Graph content, not schema discovery.");
        if (options.Discover is { } discover && !options.Effective)
        {
            return DiscoverOutput.Execute(discover, DependencySections.CreateSchema(),
                tree: options.Tree, json: options.JsonOutput,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                plainText: options.Format == OutputFormat.PlainText,
                sectionCategories: catalog.SelectionCategoryMap,
                sectionCostAnnotations: catalog.Pipeline.GetCostAnnotations(), projection: options);
        }
        if (options.Schema && options.Discover is null)
            return Invalid("--schema requires -D/--discover.");
        if (options.Effective && options.Discover is null)
            return Invalid("--effective requires -D/--discover.");
        if (!typeMode && options.Roots.Length == 0 && options.PackagePrefix is null)
            return Invalid("depends requires at least one --package, --nuspec, --library, --project, or --package-prefix root.");

        HashSet<string> sections = catalog.Pipeline.GetCandidateSections(
            options.Verbosity, selection.Sections, fixedOverview: options.SelectDefault);
        // The explicit package gesture authorizes the ordinary graph preset. Bare -S
        // remains network-free and therefore does not get this promotion.
        if (selection.Sections is null && !options.SelectDefault
            && options.Verbosity != Verbosity.Quiet)
            sections.Add(DependencySections.Graph);
        if (options.Depth is not null && !sections.Contains(DependencySections.Graph))
            return Invalid("--depth requires Dependency Graph to be selected.");
        if (options.Depth is <= 0)
            return Invalid("--depth must be a positive integer.");
        if (options.Tfm is { } target && !typeMode
            && (options.Packages.Length > 0 || options.Nuspecs.Length > 0 || options.PackagePrefix is not null)
            && !PackageDependencyTraversalFrameworkMode.TryCreateExact(target, out _))
            return Invalid("--tfm requires one exact valid target framework.");
        if (options.Discover is null && (options.Tree || options.Format == OutputFormat.Mermaid)
            && (options.Columns is not null || options.Fields is not null))
            return Invalid("Standalone graphs cannot project columns or fields; use --table or --json.");
        if (options.Count && sections.Count == 0)
            return Invalid("--count requires at least one selected section.");
        if (!options.Count && options.Discover is null
            && (options.Tree || options.Format == OutputFormat.Mermaid)
            && (sections.Count != 1 || !sections.Contains(DependencySections.Graph)))
            return Invalid("--tree and standalone --mermaid require only Dependency Graph.");
        if (options.Discover is null
            && !DependencyEvidenceCommand.ValidateTabularArity(options.EvidenceOptions, sections))
            return 1;
        if (options.Discover is null && !ProjectionDiagnostics.ValidateProjection(
            DependencySections.CreateSchema(), sections, options.Fields, options.Columns))
            return 1;
        if (options.ShareFormat is not null)
        {
            if (options.Nuspecs.Length > 0 || options.PackagePrefix is not null
                || options.Depth is not null || options.IncludePrerelease
                || options.MaxPackages is not null || options.Select is not null
                || options.SelectDefault || options.Columns is not null || options.Fields is not null
                || options.Discover is not null || options.Verbosity != Verbosity.Minimal)
                return Invalid("--share cannot be combined with dependency operation or section options.");
            if (DependsShareProjection.ValidateOptions(options) is { } shareError)
                return Invalid(shareError);
            var context = new CommandContext(options.Verbose);
            return await DependsShareProjection.WriteAsync(
                options, context.HttpClient, context.Logger, cancellationToken);
        }
        if (typeMode)
        {
            if (HasSourceOverrides(options) && !options.HasPackageScopeGesture && options.Packages.Length == 0)
                return Invalid("NuGet source options require --package type search scope.");
            if (bindTypeScope is not null)
                options = await bindTypeScope().ConfigureAwait(false);
        }
        else if (!DependencyEvidenceCommand.Validate(
            options.EvidenceOptions, sections, options.Assemblies.Length,
            expandLocalPackages: sections.Contains(DependencySections.Graph),
            commandName: "depends"))
        {
            return 1;
        }
        DependencyDocument document = await BuildDocumentAsync(
            options, sections.Contains(DependencySections.Graph), cancellationToken);
        if (options.Discover is not null)
        {
            // Effective discovery consumes this one admitted snapshot, never a second acquisition.
            return DependencyDocumentOutput.WriteDiscovery(document, options, catalog);
        }
        bool written = DependencyDocumentOutput.Write(document, options, sections);
        if (!document.IsSuccessful)
            CommandError.WriteWarning("Dependency inspection is partial or failed; select Failures for the retained outcomes.");
        return written && document.IsSuccessful ? 0
            : document.TraversalFailures.Any(failure => failure.MetadataFailure is not null)
                ? UncertifiedScanExitCode : 1;
    }

    internal static async Task<DependencyDocument> BuildDocumentAsync(
        DependsOptions options,
        bool traverse,
        CancellationToken cancellationToken = default,
        IPackageDependencyTraversalCandidateResolver? candidateResolver = null,
        IPackageDependencyTraversalManifestAcquirer? manifestAcquirer = null,
        IPackageSourceClient? packagePrefixSource = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new CommandContext(options.Verbose);
        var graph = new DependencyGraphComposition();
        var roots = new List<DependencyRootOccurrence>();
        var inputs = ImmutableArray.CreateBuilder<PackageDependencyEvidenceInput>();
        var failures = ImmutableArray.CreateBuilder<PackageDependencyEvidenceRootFailure>();
        var traversalFailures = ImmutableArray.CreateBuilder<DependencyTraversalFailure>();
        var restored = ImmutableDictionary.CreateBuilder<int, RestoredProjectDependencyTraversalResult>();
        var evidenceOccurrences = new List<int>();
        var failedOccurrences = new List<int>();
        PackageDependencyEvidenceRequest request;
        if (options.PackagePrefix is { } prefix)
        {
            (request, _) = packagePrefixSource is null
                ? await DependencyEvidenceCommand.AcquirePrefixAsync(
                    options.EvidenceOptions, prefix, context, cancellationToken)
                : await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
                    packagePrefixSource,
                    new PackagePrefixProfileRequest(prefix,
                        options.MaxPackages ?? DependencyEvidenceAcquisition.PackageProfileDefaultLimit),
                    options.Tfm, operationContext: null, cancellationToken);
            for (int i = 0; i < request.Roots.Length; i++)
            {
                evidenceOccurrences.Add(i);
                roots.Add(new(i, DependencyRootKind.Package, Field(prefix), null, i,
                    DependencyCompletion.Complete, InitialTraversal()));
            }
        }
        else
        {
            foreach (DependencyRootRequest root in options.Roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = roots.Count;
                roots.Add(new(index, root.Kind, Field(root.Value), null, null,
                    DependencyCompletion.Failed, InitialTraversal()));
                if (root.Kind is DependencyRootKind.Library or DependencyRootKind.Type)
                {
                    await AcquireMetadataRootAsync(root, index);
                    continue;
                }
                DependencyEvidenceOptions rootOptions = options.EvidenceOptions with
                {
                    Packages = root.Kind == DependencyRootKind.Package ? [root.Value] : [],
                    Nuspecs = root.Kind == DependencyRootKind.Nuspec ? [root.Value] : [],
                    Projects = root.Kind == DependencyRootKind.Project ? [root.Value] : [],
                };
                PackageDependencyEvidenceRequest acquired =
                    await DependencyEvidenceAcquisition.AcquireExplicitRootsAsync(
                        rootOptions, context.HttpClient, context.Logger.Log, cancellationToken,
                        onRestoredTraversal: traverse ? value => restored.Add(index, value) : null,
                        maximumDepth: options.Depth);
                if (acquired.Roots.Length > 0)
                {
                    roots[index] = roots[index] with
                    {
                        Admission = DependencyCompletion.Complete,
                        EvidenceRootIndex = inputs.Count,
                    };
                }
                evidenceOccurrences.AddRange(acquired.Roots.Select(_ => index));
                failedOccurrences.AddRange(acquired.FailedRoots.Select(_ => index));
                inputs.AddRange(acquired.Roots);
                failures.AddRange(acquired.FailedRoots);
            }
            request = new(inputs.ToImmutable(), failures.ToImmutable());
        }

        PackageDependencyEvidenceOutcome outcome = PackageDependencyEvidenceQuery.Execute(request);
        DependencyEvidenceProjection evidence = DependencyEvidenceProjection.Create(outcome);
        evidence = evidence with
        {
            Roots = [.. evidence.Roots.Select(row => row with { RootIndex = evidenceOccurrences[row.RootIndex] })],
            Dependencies = [.. evidence.Dependencies.Select(row => row with { RootIndex = evidenceOccurrences[row.RootIndex] })],
            DependencyGroups = [.. evidence.DependencyGroups.Select(row => row with { RootIndex = evidenceOccurrences[row.RootIndex] })],
            RestoredPackages = [.. evidence.RestoredPackages.Select(row => row with { RootIndex = evidenceOccurrences[row.RootIndex] })],
            RestoredEdges = [.. evidence.RestoredEdges.Select(row => row with { RootIndex = evidenceOccurrences[row.RootIndex] })],
            Failures = [.. evidence.Failures.Select((row, i) => row with
            {
                RootIndex = row.RootIndex is { } r ? evidenceOccurrences[r]
                    : i < failedOccurrences.Count ? failedOccurrences[i] : null,
            })],
        };
        var packageRoots = ImmutableArray.CreateBuilder<PackageDependencyTraversalRootOccurrence>();
        var packageOccurrences = new List<int>();
        for (int i = 0; i < outcome.Roots.Length; i++)
        {
            int index = evidenceOccurrences[i];
            PackageDependencyEvidenceRoot root = outcome.Roots[i];
            if (options.PackagePrefix is not null)
                roots[index] = roots[index] with { Locator = root.Display };
            if (root.Identity is PackageDependencyEvidenceRootIdentity.Package package)
            {
                int node = graph.Node(new DependencyGraphNodeIdentity.Coordinate(package.Coordinate),
                    Field($"{package.Coordinate.PackageId} {package.Coordinate.Version}"));
                roots[index] = roots[index] with { NodeId = node };
                if (traverse && root.Declaration is PackageDependencyEvidenceDeclarationResult.Available)
                {
                    packageRoots.Add(new(root, options.PackagePrefix is not null
                        || root.Provenance.AcquisitionForm == PackageDependencyEvidenceAcquisitionForm.DirectNuspec
                        ? PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly
                        : PackageDependencyTraversalExpansionAuthority.RecursiveSources));
                    packageOccurrences.Add(index);
                }
                else
                {
                    graph.Root(index, node);
                    if (traverse)
                    {
                        roots[index] = roots[index] with { Traversal = DependencyCompletion.Partial };
                        AddFailure(index, "Traversal", "DeclarationsUnavailable", "No available package declarations to traverse.");
                    }
                }
            }
            else if (root.Identity is PackageDependencyEvidenceRootIdentity.RestoredProject project)
            {
                if (project.Identity.Selection is null)
                {
                    if (traverse)
                        AddFailure(index, "Traversal", "RestoredSelectionUnavailable",
                            "The assets owner could not select a restored graph identity.");
                    continue;
                }
                int node = graph.Node(new DependencyGraphNodeIdentity.Restored(
                    new RestoredProjectGraphParentIdentity.Root(project.Identity)), root.Display);
                roots[index] = roots[index] with { NodeId = node };
                if (restored.TryGetValue(index, out var result)
                    && result is RestoredProjectDependencyTraversalResult.Available available)
                {
                    graph.Append(index, available.Value);
                    roots[index] = roots[index] with { Traversal = available.Value.Completion switch
                    {
                        RestoredProjectTraversalCompletion.Complete => DependencyCompletion.Complete,
                        RestoredProjectTraversalCompletion.DepthBounded => DependencyCompletion.DepthBounded,
                        _ => DependencyCompletion.Partial,
                    }};
                    foreach (RestoredProjectGraphFailure failure in available.Value.Failures)
                        traversalFailures.Add(new([index], "Traversal", "RestoredGraph", Field("Restored graph evidence is incomplete.")) { RestoredGraph = failure });
                }
                else
                {
                    graph.Root(index, node);
                    if (traverse)
                    {
                        roots[index] = roots[index] with { Traversal = DependencyCompletion.Partial };
                        if (result is RestoredProjectDependencyTraversalResult.Failed
                            { Failure: RestoredProjectDependencyTraversalFailure.Graph failed })
                            traversalFailures.Add(new([index], "Traversal", failed.Failure.Reason.ToString(),
                                Field(failed.Failure.Message)) { RestoredGraph = failed.Failure });
                        else
                            AddFailure(index, "Traversal", "RestoredGraphUnavailable", "The selected assets do not provide an available traversal.");
                    }
                }
            }
        }

        PackageDependencyTraversalOutcome? packageTraversal = null;
        if (packageRoots.Count > 0)
        {
            PackageDependencyTraversalFrameworkMode mode = new PackageDependencyTraversalFrameworkMode.ManifestDefault();
            if (options.Tfm is { } tfm)
            {
                if (!PackageDependencyTraversalFrameworkMode.TryCreateExact(tfm, out var exact))
                    throw new InvalidOperationException("The dependency request contains an invalid target framework.");
                mode = exact;
            }
            await using var composition = new DesktopPackageSourceComposition(context.HttpClient.Timeout);
            candidateResolver ??= new PackageDependencyTraversalCandidateAdapter(
                new DesktopPackageDependencyCandidateSource(composition, options.SourceOptions));
            manifestAcquirer ??= new DesktopPackageDependencyTraversalManifestSource(composition);
            NuGetFetchOptions fetchOptions = NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
            using var operation = new NuGetOperationContext(fetchOptions.RequestTimeout, fetchOptions.OperationTimeout, cancellationToken);
            packageTraversal = await PackageDependencyTraversalQuery.ExecuteAsync(
                new(packageRoots.ToImmutable(), mode, candidateResolver, manifestAcquirer,
                    new PackageDependencyTraversalWorkBudget(10_000, 100_000), options.Depth),
                cancellationToken, operation);
            graph.Append(packageTraversal, packageOccurrences);
            foreach (var result in packageTraversal.Roots)
            {
                int index = packageOccurrences[result.OccurrenceIndex];
                roots[index] = roots[index] with { Traversal = result.Completion switch
                {
                    PackageDependencyTraversalRootCompletion.Complete => DependencyCompletion.Complete,
                    PackageDependencyTraversalRootCompletion.DepthBounded => DependencyCompletion.DepthBounded,
                    PackageDependencyTraversalRootCompletion.SourceBounded => DependencyCompletion.SourceBounded,
                    _ => DependencyCompletion.Partial,
                }};
            }
            foreach (var failure in packageTraversal.Failures)
                traversalFailures.Add(new(Map(failure.AffectedRootOccurrences), "Traversal",
                    failure.Detail.GetType().Name, Field("Package manifest expansion failed.")) { PackageManifest = failure });
            foreach (var failure in packageTraversal.FailedResolutions)
                traversalFailures.Add(new(Map(failure.AffectedRootOccurrences), "Traversal",
                    "CandidateResolution", Field("A declared dependency could not be resolved.")) { PackageResolution = failure });
            foreach (var failure in packageTraversal.WorkBudgetDeclarations)
                traversalFailures.Add(new(Map(failure.AffectedRootOccurrences), "Traversal",
                    "WorkBudget", Field("The declaration resolution budget was exhausted.")) { PackageBudget = failure });
        }
        DependencyGraphDocument completedGraph = graph.Build();
        if (traverse && completedGraph.Roots.IsEmpty && traversalFailures.Count == 0 && outcome.FailedRoots.IsEmpty)
            traversalFailures.Add(new([], "Traversal", "NoApplicableRoots",
                Field("No dependency roots were admitted for the requested graph.")));
        return new([.. roots], completedGraph, outcome, evidence, traversalFailures.ToImmutable(),
            packageTraversal, restored.ToImmutable(), Completion(), options.Depth)
        {
            PackageRootOccurrences = [.. packageOccurrences],
        };

        ImmutableArray<int> Map(ImmutableArray<int> indexes) =>
            [.. indexes.Select(index => packageOccurrences[index])];
        DependencyCompletion InitialTraversal() =>
            traverse ? DependencyCompletion.Failed : DependencyCompletion.NotRequested;
        DependencyCompletion Completion()
        {
            if (!traverse)
                return DependencyCompletion.NotRequested;
            if (completedGraph.Roots.IsEmpty)
                return DependencyCompletion.Failed;
            if (roots.Any(root => root.Admission == DependencyCompletion.Failed
                    || root.Traversal is DependencyCompletion.Partial or DependencyCompletion.Failed)
                || traversalFailures.Count > 0 || !outcome.FailedRoots.IsEmpty
                || outcome.RootSet.RejectedRootCount > 0
                || outcome.RootSet.PackagePrefixCompletion is { TruncationReason:
                    not (PackageSearchTruncationReason.None or PackageSearchTruncationReason.RequestedLimit) })
                return DependencyCompletion.Partial;
            if (roots.Any(root => root.Traversal == DependencyCompletion.SourceBounded))
                return DependencyCompletion.SourceBounded;
            return roots.Any(root => root.Traversal == DependencyCompletion.DepthBounded)
                ? DependencyCompletion.DepthBounded : DependencyCompletion.Complete;
        }
        void AddFailure(int index, string phase, string reason, string message) =>
            traversalFailures.Add(new([index], phase, reason, Field(message)));

        async Task AcquireMetadataRootAsync(DependencyRootRequest root, int index)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root.Value))
                {
                    AddFailure(index, "Root", "InvalidRoot", "The explicit root is blank.");
                    return;
                }
                DependencyGraphDocument acquired;
                bool bounded;
                if (root.Kind == DependencyRootKind.Type)
                {
                    var result = await DependencyGraphService.BuildTypeDependencyTreeAsync(
                        context.HttpClient,
                        traverse ? options : options with { Depth = 0 },
                        context.Logger,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteRejectionWarnings(result.Diagnostics);
                    foreach (var diagnostic in result.Diagnostics)
                        traversalFailures.Add(new([index], "Root", diagnostic.Failure.Kind.ToString(),
                            Field($"Excluded '{diagnostic.Subject}' from the type scope."))
                        {
                            MetadataFailure = diagnostic.Failure,
                        });
                    if (!result.IsAvailable)
                    {
                        AddFailure(index, "Root", "TypeScopeUnavailable",
                            "The selected type scope could not be acquired.");
                        return;
                    }
                    if (!result.Dependency.Found)
                    {
                        CommandError.Write($"Type '{root.Value}' not found in the specified scope.");
                        AddFailure(index, "Root", "TypeNotFound", $"Type '{root.Value}' not found in the specified scope.");
                        return;
                    }
                    acquired = DependencyGraphProjection.Type(result.Dependency, options.Depth);
                    bounded = result.Dependency.IsDepthBounded;
                }
                else
                {
                    var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                        context.HttpClient, root.Value, options.SourceOptions, context.Logger,
                        maximumDepth: traverse ? options.Depth : 0,
                        targetFramework: options.Tfm);
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (result)
                    {
                        case LibraryDependencyGraphResult.Graph value:
                            acquired = DependencyGraphProjection.Library(value);
                            break;
                        case LibraryDependencyGraphResult.Empty value:
                            acquired = DependencyGraphProjection.Library(value);
                            break;
                        case LibraryDependencyGraphResult.Partial value:
                            acquired = DependencyGraphProjection.Library(new LibraryDependencyGraphResult.Empty(
                                value.AssemblyName, value.Identity));
                            AddFailure(index, "Traversal", value.Failure.ToString(),
                                "The admitted library's references could not be decoded.");
                            break;
                        case LibraryDependencyGraphResult.Error error:
                            AddFailure(index, "Root", "LibraryUnavailable", error.Message);
                            return;
                        default:
                            AddFailure(index, "Root", "NoMetadata", "The library has no supported managed metadata.");
                            return;
                    }
                    bounded = traverse && options.Depth is { } depth
                        && acquired.Edges.Any(edge => edge.MinimumDepth >= depth);
                    if (bounded)
                        acquired = acquired with
                        {
                            Boundaries = [.. acquired.Edges.Where(edge => edge.MinimumDepth == options.Depth)
                                .Select(edge => new DependencyGraphBoundary(0, edge.TargetNodeId, "Depth", options.Depth)).Distinct()],
                        };
                }
                foreach (DependencyGraphEdge edge in acquired.Edges)
                {
                    bool unresolved = root.Kind == DependencyRootKind.Library
                        && edge.Resolution == DependencyGraphResolutionState.Declared
                        && (options.Depth is null || edge.MinimumDepth < options.Depth);
                    if (unresolved || edge.Resolution is DependencyGraphResolutionState.Unavailable or DependencyGraphResolutionState.Rejected)
                        traversalFailures.Add(new([index], "Traversal", unresolved ? "UnresolvedReference" : edge.Resolution.ToString(),
                            Field("An assembly reference could not be resolved or inspected."))
                        {
                            SourceIdentity = acquired.Nodes[edge.SourceNodeId].Identity,
                            TargetIdentity = acquired.Nodes[edge.TargetNodeId].Identity,
                            EvidenceIdentity = edge.EvidenceIdentity,
                        });
                }
                if (!traverse)
                    acquired = acquired with
                    {
                        Edges = [],
                        Boundaries = [],
                    };
                int node = graph.Append(index, acquired);
                roots[index] = roots[index] with
                {
                    NodeId = node,
                    Admission = DependencyCompletion.Complete,
                    Traversal = !traverse ? DependencyCompletion.NotRequested
                        : traversalFailures.Any(failure => failure.Roots.Contains(index)) ? DependencyCompletion.Partial
                        : bounded ? DependencyCompletion.DepthBounded : DependencyCompletion.Complete,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or BadImageFormatException or AllCandidatesRejectedException
                or HttpRequestException or NuGetRequestTimeoutException or NuGetOperationTimeoutException
                or OfflineException)
            {
                AddFailure(index, "Root", "MetadataRejected", exception.Message);
            }
        }
    }

    private static bool HasSourceOverrides(DependsOptions options) =>
        options.SourceOptions is { } source
        && (source.Sources.Length > 0 || source.AdditionalSources.Length > 0 || source.ConfigFile is not null);

    private static InertString Field(string value) => new(TextPolicy.Field, value);
    private static int Invalid(string message)
    {
        CommandError.Write(message);
        return 1;
    }
}
