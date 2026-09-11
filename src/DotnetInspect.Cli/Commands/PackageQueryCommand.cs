using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class PackageQueryCommand
{
    internal static async Task<int> ExecuteAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(),
            DotnetInspector.Core.HttpClientFactory.CreateCredentialFreeHandler(),
            fetchOptions);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(fetchOptions.OperationTimeout);
        using var operation = new NuGetOperationContext(
            fetchOptions.RequestTimeout, fetchOptions.OperationTimeout, deadline.Token);
        await using ContentProvider? provider = options.PackageQuery!.PackageContent
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
        FindOptions options,
        IPackageSourceClient source,
        IPackageQueryContentProvider? contentProvider,
        CancellationToken cancellationToken = default)
    {
        PackageQueryPlan plan = options.PackageQuery!.Plan;
        var events = await PackageQuery.ExecuteToArrayAsync(
            source, plan, contentProvider, cancellationToken).ConfigureAwait(false);
        if (!FindCommand.TrySelectRowsPreservingContext(
                options.RowSelection,
                events,
                static queryEvent =>
                    queryEvent is PackageQueryEvent.Match,
                "package",
                out IReadOnlyList<PackageQueryEvent> displayEvents,
                out int packageRowCount))
        {
            return 1;
        }
        PackageQuerySummary summary = events
            .OfType<PackageQueryEvent.Completed>()
            .Single()
            .Value;
        bool sourceComplete = summary.Completion is
            PackageQueryCompletionKind.Exhausted
            or PackageQueryCompletionKind.GalleryResponseComplete
            or PackageQueryCompletionKind.ExactPackageComplete;
        if (options.Count
            && (summary.Failures > 0
                || !CliSemanticRowSelection.ProvidesExactCount(
                    options.RowSelection,
                    packageRowCount,
                    sourceComplete)))
        {
            WriteDiagnostics(events, summary);
            CommandError.Write(
                "Cannot count Package Query rows because candidate evaluation is incomplete; "
                + "use -n or a closed --rows range that is satisfied by the observed rows.");
            return 1;
        }
        var view = PackageQuerySections.CreateDocument(
            plan.Prefix.ToString(),
            displayEvents);
        FindCommand.WritePackageOutput(
            view, options, PackageQuerySections.Catalog.Pipeline, view.Results.Count);
        WriteDiagnostics(events, summary);

        return ExitCode(summary);
    }

    private static void WriteDiagnostics(
        IReadOnlyList<PackageQueryEvent> events,
        PackageQuerySummary summary)
    {
        foreach (var failure in events.OfType<PackageQueryEvent.Failure>())
        {
            CommandError.WriteWarning(
                $"{failure.Value.PackageId ?? "Package Query"}: "
                + $"{failure.Value.Kind}: {failure.Value.Message}");
        }
        if (summary.Completion != PackageQueryCompletionKind.Exhausted)
        {
            string matchProgress = summary.MatchLimit is int matchLimit
                ? $"{summary.Matches}/{matchLimit} matches"
                : $"{summary.Matches} matches";
            CommandError.WriteWarning(
                $"Package Query completion: {summary.Completion}; "
                + $"{summary.Candidates}/{summary.CandidateLimit} candidates, "
                + $"{matchProgress}. "
                + "These results are not an exhaustive Gallery total.");
        }
    }

    internal static int ExitCode(PackageQuerySummary summary) =>
        summary.Failures == 0 && summary.Completion is
            PackageQueryCompletionKind.Exhausted
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
