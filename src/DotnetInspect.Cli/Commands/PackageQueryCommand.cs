using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class PackageQueryCommand
{
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
                tree: options.Tree,
                json: options.JsonOutput,
                tsv: options.Tsv,
                jsonl: options.Jsonl,
                sectionCostAnnotations:
                    PackageQuerySections.Catalog.Pipeline.GetCostAnnotations(),
                sectionCategories:
                    PackageQuerySections.Catalog.SelectionCategoryMap,
                projection: options,
                semanticRowSelection: options.RowSelection,
                semanticSelectionName: "Package Query");
        }

        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(),
            DotnetInspector.Networking.HttpClientFactory.CreateCredentialFreeHandler(),
            fetchOptions);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(fetchOptions.OperationTimeout);
        using var operation = new NuGetOperationContext(
            fetchOptions.RequestTimeout, fetchOptions.OperationTimeout, deadline.Token);
        await using ContentProvider? provider = options.Plan.Facets.Any(facet =>
            facet.Tier == PackageQueryFacetTier.PackageContent)
            ? new ContentProvider(new DesktopPackageSourceComposition(fetchOptions.RequestTimeout), operation)
            : null;
        try
        {
            return await ExecuteAsync(options, source, provider, deadline.Token).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        PackageQueryPlan plan = options.Plan;
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, plan, contentProvider, cancellationToken).ConfigureAwait(false);
        PackageQuerySummary summary = events
            .OfType<PackageQueryEvent.Completed>()
            .Single()
            .Value;
        if (!CliSemanticRowSelection.TrySelectPreservingContext(
                options.RowSelection,
                events,
                static queryEvent =>
                    queryEvent is PackageQueryEvent.Match,
                "package",
                failure =>
                    $"Package Query row selection stage "
                    + $"{failure.Failure.StageNumber} requires package row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} package rows are available.",
                out IReadOnlyList<PackageQueryEvent> displayEvents,
                out int packageRowCount))
        {
            WriteDiagnostics(displayEvents, summary, options.SemanticHeadPushedDown);
            return 1;
        }
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
            WriteDiagnostics(events, summary, options.SemanticHeadPushedDown);
            CommandError.Write(
                "Cannot count Package Query rows because candidate evaluation is incomplete; "
                + "use -n or a closed --rows range that is satisfied by the observed rows.");
            return 1;
        }
        var view = PackageQuerySections.CreateDocument(
            plan.Prefix.ToString(),
            displayEvents);
        WriteOutput(view, options);
        WriteDiagnostics(events, summary, options.SemanticHeadPushedDown);

        return ExitCode(summary);
    }

    private static void WriteDiagnostics(
        IReadOnlyList<PackageQueryEvent> events,
        PackageQuerySummary summary,
        bool semanticHeadPushedDown)
    {
        foreach (var failure in events.OfType<PackageQueryEvent.Failure>())
        {
            CommandError.WriteWarning(
                $"{failure.Value.PackageId ?? "Package Query"}: "
                + $"{failure.Value.Kind}: {failure.Value.Message}");
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
        PackageQueryOptions options)
    {
        HashSet<string> includeSections =
            PackageQuerySections.Catalog.Pipeline.GetCandidateSections(
                Verbosity.Normal,
                [PackageProfileSections.Packages]);

        if (options.Count)
        {
            CountOutput.WriteCount(view.Results.Count);
        }
        else if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson,
                maxRows: null);
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
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions);
                },
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
                    return MarkoutSerializer.Serialize(
                        view,
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
}
