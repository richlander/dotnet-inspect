using System.Runtime.CompilerServices;

using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal static class EcosystemChangesCommand
{
    internal static async Task<int> ExecuteAsync(
        EcosystemChangesOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSource.NuGetOrg,
            PackageSourceAssociation.Create(),
            fetchOptions);
        if (source is not INuGetCatalogPackageSourceClient catalog)
        {
            CommandError.Write(
                "The built-in nuget.org source does not expose Catalog activity.");
            return 1;
        }

        var advisoryService = new GitHubNuGetAdvisoryService(
            context.HttpClient,
            new GitHubNuGetAdvisoryOptions
            {
                OperationTimeout = fetchOptions.OperationTimeout,
            });
        using var operation = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            cancellationToken);
        using IDisposable advisoryAccess =
            NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData);
        return await ExecuteAsync(
            options,
            catalog,
            advisoryService,
            context.Logger,
            TimeProvider.System,
            cancellationToken,
            operation).ConfigureAwait(false);
    }

    internal static async Task<int> ExecuteAsync(
        EcosystemChangesOptions options,
        INuGetCatalogPackageSourceClient source,
        GitHubNuGetAdvisoryService advisoryService,
        VerboseLogger logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(advisoryService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (options.Format is not (
                OutputFormat.Markdown
                or OutputFormat.PlainText
                or OutputFormat.Json))
        {
            CommandError.Write(
                $"Output format '{options.Format}' is not supported with --changes; use Markdown, plain text, or JSON.");
            return 1;
        }

        if (!EcosystemCommand.TryResolveFocus(
                options.Ecosystem,
                EcosystemPackCatalog.Discover(),
                out EcosystemPackDescriptor? ecosystem)
            || ecosystem is null)
        {
            return 1;
        }

        if (ecosystem.PackageSet is not { } packageSetId)
        {
            CommandError.Write(
                $"Ecosystem '{options.Ecosystem}' does not define an exact package set for change reporting.");
            return 1;
        }

        if (PackageSetCatalog.Lookup(packageSetId)
            is not PackageSetLookupResult.Known known)
        {
            throw new InvalidOperationException(
                $"Shipped package set '{packageSetId}' is not registered.");
        }

        NuGetCatalogRequest? interval =
            options.FromExclusive is { } from
                && options.ThroughInclusive is { } through
                ? new NuGetCatalogRequest(from, through)
                : null;
        var request = new EcosystemChangeReportRequest(
            new EcosystemChangePackageSelection.PackageSet(
                known.Descriptor.Id.Value,
                known.Descriptor.Members),
            interval,
            options.SecurityOnly
                ? EcosystemChangeSecuritySelection.SecurityRelevant
                : EcosystemChangeSecuritySelection.AllActivity,
            options.MaximumRows);
        EcosystemChangeReportPlan plan =
            EcosystemChangeReportPlan.Resolve(request, timeProvider);
        IAsyncEnumerable<EcosystemChangeReportEvent> events =
            EcosystemChangeReportQuery.ExecuteAsync(
                source,
                advisoryService,
                plan,
                cancellationToken,
                operationContext);
        EcosystemChangeReportDocument document =
            await EcosystemChangeReportPresentation.CollectAsync(
                ObserveProgress(events, logger, cancellationToken),
                cancellationToken).ConfigureAwait(false);

        WriteOutput(document, options);
        return document.Failures.IsEmpty
            && document.Summary.Completion
                != EcosystemChangeReportCompletionKind.Failed
            ? 0
            : 1;
    }

    private static void WriteOutput(
        EcosystemChangeReportDocument document,
        EcosystemChangesOptions options)
    {
        switch (options.Format)
        {
            case OutputFormat.Json:
                Console.WriteLine(
                    EcosystemChangeReportJson.Serialize(
                        document,
                        options.CompactJson));
                return;
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(
                    EcosystemChangeReportView.Create(document),
                    Console.Out,
                    new PlainTextFormatter(),
                    EcosystemChangeReportViewContext.Default);
                return;
            case OutputFormat.Markdown:
                MarkoutSerializer.Serialize(
                    EcosystemChangeReportView.Create(document),
                    Console.Out,
                    EcosystemChangeReportViewContext.Default);
                return;
            default:
                throw new InvalidOperationException(
                    "Unsupported ecosystem change report output format.");
        }
    }

    private static async IAsyncEnumerable<EcosystemChangeReportEvent>
        ObserveProgress(
            IAsyncEnumerable<EcosystemChangeReportEvent> events,
            VerboseLogger logger,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EcosystemChangeReportProgressPhase? activePhase = null;
        int lastCatalogPageReport = 0;
        await foreach (EcosystemChangeReportEvent item
            in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item is EcosystemChangeReportEvent.Progress progress)
            {
                if (activePhase != progress.Value.Phase)
                {
                    activePhase = progress.Value.Phase;
                    logger.Log(ProgressStart(progress.Value));
                }
                else if (progress.Value.Phase
                        == EcosystemChangeReportProgressPhase.Catalog
                    && progress.Value.CatalogPagesAcquired
                        >= lastCatalogPageReport + 50)
                {
                    lastCatalogPageReport =
                        progress.Value.CatalogPagesAcquired;
                    logger.Log(
                        $"Catalog: inspected {progress.Value.Completed:N0} activity events "
                        + $"across {progress.Value.CatalogPagesAcquired:N0} pages.");
                }
            }

            yield return item;
        }
    }

    private static string ProgressStart(
        EcosystemChangeReportProgress progress) =>
        progress.Phase switch
        {
            EcosystemChangeReportProgressPhase.Catalog =>
                "Catalog: scanning the requested nuget.org interval.",
            EcosystemChangeReportProgressPhase.Advisory =>
                $"Advisory: evaluating {(progress.Total ?? 0):N0} package coordinates.",
            EcosystemChangeReportProgressPhase.PackageReceipt =>
                $"Package receipt: evaluating {(progress.Total ?? 0):N0} exact coordinates.",
            _ => throw new InvalidOperationException(
                "Unknown ecosystem report progress phase."),
        };
}
