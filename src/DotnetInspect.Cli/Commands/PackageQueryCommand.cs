using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;
using Markout.Formatting;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class PackageQueryCommand
{
    private static readonly InspectionEnvelopeJsonContract<
        PackageQueryDocument> PackageQueryJsonContract =
            new(
                "package-query",
                1,
                PackageQueryJsonContext.Default.PackageQueryDocument);

    internal static async Task<int> ExecuteAsync(
        PackageQueryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
                PackageQuerySections.CreateSchema(),
                DiscoveryOutputRequest.Create(
                    options.JsonOutput ? OutputFormat.Json
                        : options.Jsonl ? OutputFormat.Jsonl
                        : options.Tsv ? OutputFormat.Tsv
                        : options.Tabular ? OutputFormat.Table
                        : OutputFormat.Markdown,
                    options.Tree,
                    options.Tabular,
                    options.NoHeader,
                    projection: options),
                sectionCostAnnotations:
                    PackageQuerySections.Catalog.Pipeline.GetCostAnnotations(),
                sectionCategories:
                    PackageQuerySections.Catalog.SelectionCategoryMap,
                catalogHiddenSections:
                    PackageQuerySections.Catalog.Pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors:
                    PackageQuerySections.Catalog.Pipeline.GetListedCategoryDoors(),
                semanticRowSelection: options.RowSelection,
                semanticSelectionName: "Package Query");
        }

        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        var sourceAuthorization =
            new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]);
        PackageSourceAuthorization authorization =
            sourceAuthorization.AuthorizeSourcesFor("package-query");
        ConfiguredPackageAuthority galleryAuthority =
            authorization.Authorities[0];
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            galleryAuthority.Association,
            DotnetInspector.Networking.HttpClientFactory.CreateCredentialFreeHandler(),
            fetchOptions);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(fetchOptions.OperationTimeout);
        using var operation = new NuGetOperationContext(
            fetchOptions.RequestTimeout, fetchOptions.OperationTimeout, deadline.Token);
        PackageQueryPlan prequalificationPlan =
            options.Plan.RequiresLibraryLiteralEvaluation
                ? options.Plan.CreatePrequalificationPlan()
                : options.Plan;
        await using ContentProvider? provider =
            prequalificationPlan.RequiresPackageContent
            ? new ContentProvider(new DesktopPackageSourceComposition(fetchOptions.RequestTimeout), operation)
            : null;
        await using DesktopPackageSourceComposition? traversalComposition =
            options.Plan.RequiresDependencyTraversal
                ? new DesktopPackageSourceComposition(fetchOptions.RequestTimeout)
                : null;
        PackageQueryDependencyTraversalServices? traversalServices =
            traversalComposition is null
                ? null
                : new(
                    new PackageDependencyTraversalCandidateAdapter(
                        new DesktopPackageDependencyCandidateSource(
                            traversalComposition,
                            new NuGetSourceOptions
                            {
                                Sources = [PackageSource.NuGetOrg.Url],
                            })),
                    new DesktopPackageDependencyTraversalManifestSource(
                        traversalComposition));
        await using PackageSourceSettlementLease? semanticSettlement =
            options.Plan.RequiresLibraryLiteralEvaluation
                ? PackageSourceSettlementService.IssueLease(
                    authority =>
                        ReferenceEquals(
                            authority.Association,
                            galleryAuthority.Association)
                            ? source
                            : throw new InvalidOperationException(
                                "Library-literal Package Query requested an unauthorized package source."))
                : null;
        using AssemblySemanticQueryStores? semanticStores =
            options.Plan.RequiresLibraryLiteralEvaluation
                ? new AssemblySemanticQueryStores()
                : null;
        PackageAssemblySemanticFindBudget? semanticBudget =
            options.Plan.RequiresLibraryLiteralEvaluation
                ? PackageAssemblySemanticFindBudget.Default
                : null;
        PackageQueryAssemblySemanticExecution? semanticExecution =
            semanticSettlement is null
                ? null
                : new(
                    sourceAuthorization,
                    token => semanticSettlement.IssueOperationLease(
                        token,
                        fetchOptions.RequestTimeout,
                        semanticBudget!.MaximumDuration),
                    new PackagePayloadAcquisitionPlan(
                        semanticStores!.GetStore,
                        log: context.Logger.Log),
                    semanticBudget!,
                    new AssemblySemanticQueryProgressSink(
                        context.Logger,
                        options.Plan.MaximumCandidates));
        try
        {
            return await ExecuteAsync(
                options,
                source,
                provider,
                traversalServices,
                semanticExecution,
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            CommandError.Write("Package Query exceeded its operation deadline; results are incomplete.");
            return 1;
        }
    }

    internal static async Task<int> ExecuteAsync(
        PackageQueryOptions options,
        IPackageSourceClient source,
        IPackageQueryContentProvider? contentProvider,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
            options,
            source,
            contentProvider,
            dependencyTraversalServices: null,
            assemblySemanticExecution: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<int> ExecuteAsync(
        PackageQueryOptions options,
        IPackageSourceClient source,
        IPackageQueryContentProvider? contentProvider,
        PackageQueryDependencyTraversalServices?
            dependencyTraversalServices,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
            options,
            source,
            contentProvider,
            dependencyTraversalServices,
            assemblySemanticExecution: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<int> ExecuteAsync(
        PackageQueryOptions options,
        IPackageSourceClient source,
        IPackageQueryContentProvider? contentProvider,
        PackageQueryDependencyTraversalServices?
            dependencyTraversalServices,
        PackageQueryAssemblySemanticExecution?
            assemblySemanticExecution,
        CancellationToken cancellationToken = default)
    {
        PackageQueryPlan plan = options.Plan;
        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider,
                dependencyTraversalServices,
                assemblySemanticExecution,
                nonterminalSink: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        PackageQueryDocument document = envelope.Content;
        PackageQuerySummary summary = document.Summary;
        if (options.EnvelopeOutput || options.IsContentJson)
        {
            bool wrote = InspectionEnvelopeOutput.TryWrite(
                envelope,
                PackageQueryJsonContract,
                options.EnvelopeOutput,
                options.CompactJson);
            WriteDiagnostics(
                document.Failures,
                summary,
                options.SemanticHeadPushedDown);
            return wrote ? ExitCode(summary) : 1;
        }

        if (!CliSemanticRowSelection.TrySelect(
                options.RowSelection,
                document.Results,
                "package",
                failure =>
                    $"Package Query row selection stage "
                    + $"{failure.Failure.StageNumber} requires package row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} package rows are available.",
                out IReadOnlyList<PackageQueryMatch> displayResults))
        {
            WriteDiagnostics(
                document.Failures,
                summary,
                options.SemanticHeadPushedDown);
            return 1;
        }
        int packageRowCount = document.Results.Length;
        bool sourceComplete = summary.Completion is
            PackageQueryCompletionKind.Exhausted
            or PackageQueryCompletionKind.ExactPackageComplete;
        if (options.Count
            && (summary.Failures > 0
                || !CliSemanticRowSelection.ProvidesExactCount(
                    options.RowSelection,
                    packageRowCount,
                    sourceComplete)))
        {
            WriteDiagnostics(
                document.Failures,
                summary,
                options.SemanticHeadPushedDown);
            CommandError.Write(
                "Cannot count Package Query rows because candidate evaluation is incomplete; "
                + "use -n or a closed --rows range that is satisfied by the observed rows.");
            return 1;
        }
        HashSet<string> includeSections = options.IncludeSections
            ?? (options.SelectDefault
                ? [.. PackageQuerySections.BareSelectSectionNames]
                : [
                    document.HasPackages
                        ? PackageProfileSections.Packages
                        : PackageQuerySections.QuerySummaryName,
                ]);
        if (plan.RequiresLibraryLiteralEvaluation)
        {
            var semanticView = PackageQuerySections.CreateSemanticDocument(
                plan.Prefix.ToString(),
                displayResults,
                summary);
            WriteOutput(semanticView, options, includeSections);
        }
        else
        {
            var view = PackageQuerySections.CreateDocument(
                plan.Prefix.ToString(),
                displayResults,
                summary);
            WriteOutput(view, options, includeSections);
        }
        WriteDiagnostics(
            document.Failures,
            summary,
            options.SemanticHeadPushedDown);

        return ExitCode(summary);
    }

    private static void WriteDiagnostics(
        IReadOnlyList<PackageQueryFailure> failures,
        PackageQuerySummary summary,
        bool semanticHeadPushedDown)
    {
        foreach (PackageQueryFailure failure in failures)
        {
            CommandError.WriteWarning(
                $"{failure.PackageId ?? "Package Query"}: "
                + $"{failure.Kind}: {failure.Message}");
        }
        if (summary.Completion is not (
                PackageQueryCompletionKind.Exhausted
                or PackageQueryCompletionKind.ExactPackageComplete)
            && !(semanticHeadPushedDown
                && summary.Completion
                    == PackageQueryCompletionKind.MatchLimitReached))
        {
            string matches = summary.MatchLimit is int matchLimit
                ? $"{summary.Matches}/{matchLimit}"
                : summary.Matches.ToString();
            CommandError.WriteWarning(
                $"Package Query completion: {summary.Completion}; "
                + $"{summary.Candidates}/{summary.CandidateLimit} candidates, "
                + $"{matches} matches. "
                + "These results do not exhaust the requested package-ID scope.");
        }
    }

    private static void WriteOutput(
        PackageQueryView view,
        PackageQueryOptions options,
        HashSet<string> includeSections)
    {
        EmptyPackageQueryView? emptyView =
            view.Results.Count == 0
            && includeSections.Contains(PackageProfileSections.Packages)
                ? EmptyPackageQueryView.From(view)
                : null;
        WriteOutputCore(
            view,
            view.Results.Count,
            emptyView,
            options,
            includeSections);
    }

    private static void WriteOutput(
        PackageQuerySemanticView view,
        PackageQueryOptions options,
        HashSet<string> includeSections)
    {
        EmptyPackageQueryView? emptyView =
            view.Results.Count == 0
            && includeSections.Contains(PackageProfileSections.Packages)
                ? EmptyPackageQueryView.From(view)
                : null;
        WriteOutputCore(
            view,
            view.Results.Count,
            emptyView,
            options,
            includeSections);
    }

    private static void WriteOutputCore<TView>(
        TView view,
        int resultCount,
        EmptyPackageQueryView? emptyView,
        PackageQueryOptions options,
        HashSet<string> includeSections)
    {

        void Serialize(
            TextWriter writer,
            IMarkoutFormatter formatter,
            MarkoutWriterOptions writerOptions)
        {
            writerOptions.IncludeSections = includeSections;
            writerOptions.SectionOrder =
                PackageQuerySections.Catalog.AlphabeticalSectionOrder;
            if (emptyView is null)
            {
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions);
            }
            else
            {
                MarkoutSerializer.Serialize(
                    emptyView,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions);
            }
        }

        if (options.Count)
        {
            CountOutput.WriteCount(resultCount);
        }
        else if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                Serialize,
                !options.CompactJson,
                maxRows: null,
                sectionOrder:
                    PackageQuerySections.Catalog.AlphabeticalSectionOrder);
        }
        else if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                Serialize,
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(
                Console.Out,
                rows: null,
                writerOptions =>
                {
                    writerOptions.IncludeSections = includeSections;
                    return emptyView is null
                        ? MarkoutSerializer.Serialize(
                            view,
                            SearchViewContext.Default,
                            writerOptions)
                        : MarkoutSerializer.Serialize(
                            emptyView,
                            SearchViewContext.Default,
                            writerOptions);
                },
                options.Columns,
                options.Fields);
        }
    }

    internal static int ExitCode(PackageQuerySummary summary) =>
        summary.Failures == 0 && summary.Completion is
            PackageQueryCompletionKind.Exhausted
            or PackageQueryCompletionKind.ExactPackageComplete
            or PackageQueryCompletionKind.MatchLimitReached
            or PackageQueryCompletionKind.CandidateLimitReached
            ? 0 : 1;

    internal sealed class ContentProvider(
        DesktopPackageSourceComposition composition,
        NuGetOperationContext operation) : IPackageQueryContentProvider, IAsyncDisposable
    {
        private readonly Dictionary<ConfiguredPackageAuthority, IPackageStore> _stores = [];
        private string? _temporaryRoot;

        public async ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package,
            CancellationToken cancellationToken)
        {
            var result = await composition.AcquirePinnedAsync(
                package.PackageId,
                package.Version,
                GetStore,
                new NuGetSourceOptions { Sources = [PackageSource.NuGetOrg.Url] },
                cancellationToken: cancellationToken,
                operationContext: operation).ConfigureAwait(false);
            if (result.Failures.Count > 0)
            {
                return new PackageQueryContentResult.Unavailable(string.Join(
                    "; ", result.Failures.Select(failure => $"{failure.Authority}: {failure.Message}")));
            }
            return result.Payload is { } payload
                ? new PackageQueryContentResult.Available(payload.Content)
                : new PackageQueryContentResult.Unavailable(
                    "NuGet.org did not supply the selected package archive.");
        }

        private IPackageStore GetStore(ConfiguredPackageAuthority authority, PackageProducerIdentity producer)
        {
            if (!_stores.TryGetValue(authority, out IPackageStore? store))
            {
                store = new AuthorityScopedFileSystemPackageStore(
                    authority, producer,
                    () => _temporaryRoot ??= Directory.CreateTempSubdirectory("inspect-query").FullName);
                _stores.Add(authority, store);
            }
            return store;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await composition.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                DotnetInspector.Packages.PackageExtractor.Cleanup(_temporaryRoot);
            }
        }
    }

    private sealed class AssemblySemanticQueryStores : IDisposable
    {
        private readonly Dictionary<
            ConfiguredPackageAuthority,
            IPackageStore> _stores =
            new(ReferenceEqualityComparer.Instance);
        private string? _temporaryRoot;

        internal IPackageStore GetStore(
            ConfiguredPackageAuthority authority,
            PackageProducerIdentity producer)
        {
            if (!_stores.TryGetValue(authority, out IPackageStore? store))
            {
                store = new AuthorityScopedFileSystemPackageStore(
                    authority,
                    producer,
                    () =>
                        _temporaryRoot ??=
                            Directory.CreateTempSubdirectory(
                                "inspect-query").FullName);
                _stores.Add(authority, store);
            }
            return store;
        }

        public void Dispose() =>
            DotnetInspector.Packages.PackageExtractor.Cleanup(
                _temporaryRoot);
    }

    private sealed class AssemblySemanticQueryProgressSink(
        VerboseLogger logger,
        int candidateCount)
        : IPackageQueryLibraryLiteralAssessmentSink
    {
        public ValueTask ReportAsync(
            PackageQueryLibraryLiteralAssessment assessment,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.Log(
                $"Evaluated {assessment.CandidateOrdinal} of "
                + $"{candidateCount} package candidates");
            return ValueTask.CompletedTask;
        }
    }
}
