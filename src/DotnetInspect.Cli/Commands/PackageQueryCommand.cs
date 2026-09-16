using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
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
                semanticRowSelection: options.RowSelection,
                semanticSelectionName: "Package Query");
        }

        if (options.LibraryLiteralPlan is not null)
        {
            return await ExecuteLibraryLiteralAsync(
                options,
                context,
                cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> ExecuteLibraryLiteralAsync(
        PackageQueryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        PackageAssemblySemanticQueryCliPlan plan =
            options.LibraryLiteralPlan
            ?? throw new InvalidOperationException(
                "A library-literal query requires its semantic plan.");
        PackageAssemblySemanticFindBudget budget =
            PackageAssemblySemanticFindBudget.Default;
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(
                context.HttpClient.Timeout);
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        ConfiguredPackageAuthority galleryAuthority =
            authorization.Authorities[0];
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                galleryAuthority.Association,
                DotnetInspector.Networking.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions);
        await using PackageSourceSettlementLease settlement =
            PackageSourceSettlementService.IssueLease(
                authority =>
                    ReferenceEquals(authority, galleryAuthority)
                        ? source
                        : throw new InvalidOperationException(
                            "Library-literal Package Query requested an unauthorized package source."));
        using var stores = new AssemblySemanticQueryStores();
        PackageSourceOperationLease? operation =
            settlement.IssueOperationLease(
                cancellationToken,
                fetchOptions.RequestTimeout,
                budget.MaximumDuration);

        PackageAssemblySemanticQueryDocument document;
        try
        {
            PackageAcquisitionPopulation population =
                plan.Population switch
                {
                    PackageAssemblySemanticQueryPopulationPlan.Exact exact =>
                        await PackageAcquisitionPopulationResolver
                            .ResolveGalleryExactAsync(
                                operation,
                                exact.PackageId,
                                authorization,
                                plan.IncludePrerelease).ConfigureAwait(false),
                    PackageAssemblySemanticQueryPopulationPlan.Prefix prefix =>
                        await PackageAcquisitionPopulationResolver
                            .ResolveGalleryPrefixAsync(
                                operation,
                                prefix.Declaration,
                                prefix.MaximumCandidates,
                                authorization,
                                plan.IncludePrerelease).ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal Package Query population plan."),
                };
            var request = new PackageAssemblySemanticFindRequest(
                population,
                plan.Target,
                plan.Pattern,
                budget);
            PackageSourceOperationLease transferredOperation =
                operation;
            operation = null;
            InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
                await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    request,
                    transferredOperation,
                    new PackagePayloadAcquisitionPlan(
                        stores.GetStore,
                        log: context.Logger.Log),
                    new AssemblySemanticQueryProgressSink(
                        context.Logger,
                        population.Candidates.Length),
                    cancellationToken).ConfigureAwait(false);
            document = envelope.Content;
        }
        finally
        {
            operation?.Dispose();
        }

        return CompleteLibraryLiteralExecution(
            options,
            plan,
            document);
    }

    internal static int CompleteLibraryLiteralExecution(
        PackageQueryOptions options,
        PackageAssemblySemanticQueryCliPlan plan,
        PackageAssemblySemanticQueryDocument document)
    {
        if (document.Completion.IsOperationDeadlineExpired)
        {
            WriteLibraryLiteralDiagnostics(document);
            return 1;
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
                out IReadOnlyList<PackageAssemblySemanticQueryResult>
                    displayResults))
        {
            WriteLibraryLiteralDiagnostics(document);
            return 1;
        }

        bool complete =
            document.Completion.IsRequestedPopulationComplete
            && document.Completion.IsSemanticEvaluationComplete;
        bool countIsExact =
            !options.Count
            || CliSemanticRowSelection.ProvidesExactCount(
                options.RowSelection,
                document.Results.Length,
                sourceComplete: complete);
        if (!countIsExact)
        {
            WriteLibraryLiteralDiagnostics(document);
            CommandError.Write(
                "Cannot count Package Query rows because package population "
                + "formation or candidate evaluation is incomplete; omit "
                + "--count to inspect package outcomes.");
            return 1;
        }

        PackageAssemblySemanticQueryView view =
            PackageAssemblySemanticQuerySections.CreateDocument(
                plan.Pattern.Operand.DisplayText.ToString(),
                plan.Target.RequestedFramework!,
                displayResults,
                document);
        WriteLibraryLiteralOutput(view, options);
        WriteLibraryLiteralDiagnostics(document);
        return complete || options.Count ? 0 : 1;
    }

    internal static async Task<int> ExecuteAsync(
        PackageQueryOptions options,
        IPackageSourceClient source,
        IPackageQueryContentProvider? contentProvider,
        CancellationToken cancellationToken = default)
    {
        PackageQueryPlan plan = options.Plan;
        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider,
                cancellationToken).ConfigureAwait(false);
        PackageQueryDocument document = envelope.Content;
        PackageQuerySummary summary = document.Summary;
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
        var view = PackageQuerySections.CreateDocument(
            plan.Prefix.ToString(),
            displayResults,
            summary);
        WriteOutput(view, options);
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

    internal static void WriteLibraryLiteralOutput(
        PackageAssemblySemanticQueryView view,
        PackageQueryOptions options)
    {
        HashSet<string> includeSections =
            PackageAssemblySemanticQuerySections.Catalog.Pipeline
                .GetCandidateSections(
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

    private static void WriteLibraryLiteralDiagnostics(
        PackageAssemblySemanticQueryDocument document)
    {
        WriteLibraryLiteralPopulationDiagnostics(
            document.Population,
            document.EvaluatedCandidateCount);

        foreach (PackageAssemblySemanticQueryCandidateOutcome.Failure failure
            in document.CandidateOutcomes
                .OfType<
                    PackageAssemblySemanticQueryCandidateOutcome.Failure>())
        {
            string subject =
                $"{failure.Coordinate.PackageId}@{failure.Coordinate.Version}";
            switch (failure.Reason)
            {
                case PackageAssemblySemanticQueryFailureReason.Acquisition
                    acquisition:
                    foreach (PackageAuthorityFailure item
                        in acquisition.Evidence.Failures)
                    {
                        CommandError.WriteWarning(
                            $"{subject}: {item.Authority} ({item.Kind}): "
                            + item.Message);
                    }
                    foreach (var authority
                        in acquisition.Evidence.NotFoundAuthorities)
                    {
                        CommandError.WriteWarning(
                            $"{subject}: package payload was not found at "
                            + authority.ToString());
                    }
                    break;
                case PackageAssemblySemanticQueryFailureReason.Evaluation
                    evaluation:
                    CommandError.WriteWarning(
                        $"{subject}: "
                        + PackageAssemblySemanticQuerySections.Describe(
                            evaluation.Evidence));
                    break;
            }
        }
    }

    private static void WriteLibraryLiteralPopulationDiagnostics(
        PackageAcquisitionPopulation population,
        int evaluatedCandidateCount)
    {
        foreach (PackageAcquisitionPopulationFailure failure
            in population.Failures)
        {
            string subject = failure.Coordinate is { } coordinate
                ? $"{coordinate.PackageId}@{coordinate.Version}"
                : failure.PackageId ?? "Package population";
            CommandError.WriteWarning(
                $"{subject}: {failure.Failure.Authority} "
                + $"({failure.Failure.Kind}): "
                + failure.Failure.Message);
        }

        if (population.Completion is not (
                PackageAcquisitionPopulationCompletionKind.ExactPackageComplete
                or PackageAcquisitionPopulationCompletionKind.PrefixExhausted))
        {
            CommandError.WriteWarning(
                $"Package Query population completion: "
                + $"{population.Completion}; evaluated "
                + $"{evaluatedCandidateCount}/"
                + $"{population.RequestedCandidates} candidates. "
                + "These results do not exhaust the requested package-ID scope.");
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
        : IPackageAssemblySemanticQueryNonterminalSink
    {
        public ValueTask ReportAsync(
            PackageAssemblySemanticQueryCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.Log(
                $"Evaluated {outcome.CandidateOrdinal} of "
                + $"{candidateCount} package candidates");
            return ValueTask.CompletedTask;
        }
    }
}
