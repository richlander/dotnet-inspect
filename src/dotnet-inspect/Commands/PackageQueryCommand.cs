using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Commands;

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
        IPackageQueryContentProvider? provider = options.PackageQuery!.PackageContent
            ? new ContentProvider(source, new FileSystemPackageStore(), operation)
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
        var view = PackageQuerySections.CreateDocument(plan.Prefix.ToString(), events, options.Rows);
        FindCommand.WritePackageOutput(
            view, options, PackageQuerySections.Catalog.Pipeline, view.Results.Count);

        foreach (var failure in events.OfType<PackageQueryEvent.Failure>())
        {
            CommandError.WriteWarning(
                $"{failure.Value.PackageId ?? "Package Query"}: "
                + $"{failure.Value.Kind}: {failure.Value.Message}");
        }
        if (view.Summary.Completion != PackageQueryCompletionKind.Exhausted)
        {
            CommandError.WriteWarning(
                $"Package Query completion: {view.Summary.Completion}; "
                + $"{view.Summary.Candidates}/{view.Summary.CandidateLimit} candidates, "
                + $"{view.Summary.Matches}/{view.Summary.MatchLimit} matches. "
                + "These results are not an exhaustive Gallery total.");
        }
        return ExitCode(view.Summary);
    }

    internal static int ExitCode(PackageQuerySummary summary) =>
        summary.Failures == 0 && summary.Completion is
            PackageQueryCompletionKind.Exhausted
            or PackageQueryCompletionKind.MatchLimitReached
            or PackageQueryCompletionKind.CandidateLimitReached
            ? 0 : 1;

    internal sealed class ContentProvider(
        IPackageSourceClient source,
        IPackageStore store,
        NuGetOperationContext operation) : IPackageQueryContentProvider
    {
        public async ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package,
            CancellationToken cancellationToken)
        {
            var result = await PackagePayloadAcquisition.AcquireAsync(
                source,
                PackageSourceIdentity.NuGetOrg,
                PackageSourceCoordinate.Create(package.PackageId, package.Version),
                store,
                cancellationToken: cancellationToken,
                operationContext: operation).ConfigureAwait(false);
            return result switch
            {
                PackageSourcePayloadResult.Acquired acquired =>
                    new PackageQueryContentResult.Available(acquired.Payload.Content),
                PackageSourcePayloadResult.Unavailable unavailable =>
                    new PackageQueryContentResult.Unavailable(unavailable.Message),
                PackageSourcePayloadResult.Failed failed =>
                    new PackageQueryContentResult.Unavailable(failed.Failure.Message),
                _ => throw new InvalidOperationException("Unknown package payload acquisition result."),
            };
        }
    }
}
