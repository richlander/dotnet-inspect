using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

internal abstract record WorkspacePacketRestorationResult
{
    private WorkspacePacketRestorationResult()
    {
    }

    internal sealed record Restored(WorkspacePacketRestoration Value)
        : WorkspacePacketRestorationResult;

    internal sealed record Failed(string Summary, string[] Details)
        : WorkspacePacketRestorationResult;
}

internal sealed class WorkspacePacketRestoration
{
    readonly InspectionWorkspace _workspace;

    WorkspacePacketRestoration(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation)
    {
        _workspace = workspace;
        Activation = activation;
    }

    internal InspectionWorkspace Workspace => _workspace;

    internal CompleteWorkspaceActivation Activation { get; }

    internal Task<TResult> ExecuteAsync<TResult>(
        Func<WorkspacePacketRestoration, Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return WorkspacePacketRestorationLifetime.ExecuteAsync(
            _workspace,
            () => operation(this));
    }

    internal static async Task<WorkspacePacketRestorationResult> RestoreAsync(
        string input,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken,
        string optionName = "--workspace")
    {
        ArgumentNullException.ThrowIfNull(loadOptions);

        string packet;
        try
        {
            packet = GetPacketInput(input, optionName);
        }
        catch (InvalidDataException ex)
        {
            return new WorkspacePacketRestorationResult.Failed(
                "The Workspace packet input is invalid.",
                [ex.Message]);
        }

        var intent = new WorkspaceCommandRestorationIntent(cancellationToken);
        CompleteRestorationPreparationResult preparation =
            CompleteRestorationPreparation.FromPacket(
                packet,
                intent,
                cancellationToken);
        var host = new WorkspaceCommandRestorationHost();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot executableEntries =
            CurrentCatalogEntriesExecutable(registry);
        CompleteRestorationResult<InspectionWorkspace> result =
            await CompleteRestorationCoordinator.RestoreAsync(
                preparation,
                intent,
                host,
                new CompleteRestorationExecutionOptions
                {
                    ContextLoad = loadOptions,
                    ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(5),
                    Facets = registry,
                    FacetAvailability = (_, _) => executableEntries,
                },
                cancellationToken).ConfigureAwait(false);
        if (result
            is CompleteRestorationResult<
                InspectionWorkspace>.Activated activated)
        {
            return new WorkspacePacketRestorationResult.Restored(
                new WorkspacePacketRestoration(
                    activated.Activation,
                    activated.Workspace));
        }

        return new WorkspacePacketRestorationResult.Failed(
            "The Workspace packet could not be restored.",
            result switch
            {
                CompleteRestorationResult<
                    InspectionWorkspace>.Failed failed =>
                    RestorationFailureDetails(failed.Failure),
                CompleteRestorationResult<
                    InspectionWorkspace>.Superseded =>
                    ["The restoration request was superseded."],
                _ => ["The restoration returned an unsupported result."],
            });
    }

    internal static string GetPacketInput(
        string value,
        string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"{optionName} requires a non-empty canonical Base64URL "
                    + "Workspace packet string.");
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
            throw UrlNotSupported(optionName);

        return value;
    }

    static InvalidDataException UrlNotSupported(string optionName) =>
        new(
            $"{optionName} accepts a canonical Base64URL Workspace packet "
                + "string; URLs are not supported.");

    static string[] RestorationFailureDetails(
        CompleteRestorationFailure failure) =>
        failure switch
        {
            CompleteRestorationFailure.ContextLoadFailed
            {
                Outcome: WorkspaceContextLoadOutcome.Failed failed,
            } =>
            [
                failure.Message,
                .. failed.Failures.Select(static item =>
                    $"{item.Kind}: {item.Message}"),
            ],
            CompleteRestorationFailure.ScopeMutationFailed scope =>
                [
                    failure.Message,
                    scope.Outcome.ToString()
                        ?? "Unknown Scope outcome.",
                ],
            _ => [failure.Message],
        };

    static ViewFacetAvailabilitySnapshot CurrentCatalogEntriesExecutable(
        ViewFacetRegistry registry) =>
        new(
            registry.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    ViewFacetAvailability.Available.Instance)));

}

internal static class WorkspacePacketRestorationLifetime
{
    const string CleanupReportKey =
        "DotnetInspect.Cli.WorkspaceCleanupReport";
    const string CleanupFailureKey =
        "DotnetInspect.Cli.WorkspaceCleanupFailure";

    internal static async Task<TResult> ExecuteAsync<TResult>(
        InspectionWorkspace workspace,
        Func<Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(operation);

        TResult result;
        try
        {
            result = await operation().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure)
                .ConfigureAwait(false);
            throw;
        }

        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!report.Succeeded)
        {
            var failure = new InvalidOperationException(
                "The restored Workspace could not release every participant.");
            failure.Data[CleanupReportKey] = report;
            throw failure;
        }

        return result;
    }

    static async Task CloseAfterFailureAsync(
        InspectionWorkspace workspace,
        Exception failure)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.Succeeded)
                failure.Data[CleanupReportKey] = report;
        }
        catch (Exception cleanupFailure)
        {
            failure.Data[CleanupFailureKey] = cleanupFailure;
        }
    }
}
