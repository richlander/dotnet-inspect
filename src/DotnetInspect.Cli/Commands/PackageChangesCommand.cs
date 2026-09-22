using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;
using NuGetFetch;

using NetworkHttpClientFactory =
    DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Commands;

internal static class PackageChangesCommand
{
    internal const string Name = "activity";
    private static readonly InspectionEnvelopeJsonContract<
        EcosystemChangeReportDocument> JsonContract =
            new(
                "ecosystem-change-report",
                2,
                EcosystemChangeReportJsonContext.Default
                    .EcosystemChangeReportDocument);

    internal static async Task<int> ExecuteAsync(
        PackageChangesOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
        using IPackageSourceClient source = CreateCatalogSource(fetchOptions);
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

    private static IPackageSourceClient CreateCatalogSource(
        NuGetFetchOptions fetchOptions)
    {
        HttpMessageHandler transport =
            NetworkHttpClientFactory.CreateCredentialFreePackageSourceHandler(
                PackageSource.NuGetOrg.Url);
        try
        {
            return PackageSourceClientFactory.Create(
                PackageSource.NuGetOrg,
                PackageSourceAssociation.Create(),
                transport,
                fetchOptions);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    internal static async Task<int> ExecuteAsync(
        PackageChangesOptions options,
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
                $"Output format '{options.Format}' is not supported with package activity; use Markdown, plain text, or JSON.");
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
        InspectionEnvelope<EcosystemChangeReportDocument> inspection =
            await EcosystemChangeReportInspection.ExecuteAsync(
                events,
                new VerboseProgressSink(logger),
                cancellationToken).ConfigureAwait(false);
        EcosystemChangeReportDocument document = inspection.Content;

        if (!WriteOutput(inspection, options))
            return 1;
        return document.Failures.IsEmpty
            && document.Summary.Completion
                != EcosystemChangeReportCompletionKind.Failed
            ? 0
            : 1;
    }

    private static bool WriteOutput(
        InspectionEnvelope<EcosystemChangeReportDocument> inspection,
        PackageChangesOptions options)
    {
        if (options.EnvelopeOutput || options.Format == OutputFormat.Json)
        {
            return InspectionEnvelopeOutput.TryWrite(
                inspection,
                JsonContract,
                options.EnvelopeOutput,
                options.CompactJson);
        }

        EcosystemChangeReportDocument document = inspection.Content;
        switch (options.Format)
        {
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(
                    EcosystemChangeReportView.Create(document),
                    Console.Out,
                    new PlainTextFormatter(),
                    EcosystemChangeReportViewContext.Default);
                return true;
            case OutputFormat.Markdown:
                MarkoutSerializer.Serialize(
                    EcosystemChangeReportView.Create(document),
                    Console.Out,
                    EcosystemChangeReportViewContext.Default);
                return true;
            default:
                throw new InvalidOperationException(
                    "Unsupported package activity output format.");
        }
    }

    private sealed class VerboseProgressSink(VerboseLogger logger)
        : IEcosystemChangeReportNonterminalSink
    {
        EcosystemChangeReportProgressPhase? _activePhase;
        int _lastCatalogPageReport;

        public ValueTask ReportAsync(
            EcosystemChangeReportNonterminalEvent reportEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reportEvent
                is EcosystemChangeReportNonterminalEvent.Progress progress)
            {
                if (_activePhase != progress.Value.Phase)
                {
                    _activePhase = progress.Value.Phase;
                    logger.Log(ProgressStart(progress.Value));
                }
                else if (progress.Value.Phase
                        == EcosystemChangeReportProgressPhase.Catalog
                    && progress.Value.CatalogPagesAcquired
                        >= _lastCatalogPageReport + 50)
                {
                    _lastCatalogPageReport =
                        progress.Value.CatalogPagesAcquired;
                    logger.Log(
                        $"Catalog: inspected {progress.Value.Completed:N0} activity events "
                        + $"across {progress.Value.CatalogPagesAcquired:N0} pages.");
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private static string ProgressStart(
        EcosystemChangeReportProgressPresentation progress) =>
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
