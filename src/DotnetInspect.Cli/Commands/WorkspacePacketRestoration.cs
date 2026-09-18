using DotnetInspect.Cli.Output;
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

internal sealed class WorkspacePacketRestoration : IAsyncDisposable
{
    readonly WorkspaceCommandRestorationHost _host;
    readonly WorkspaceRealizationOperationLease _authority;

    WorkspacePacketRestoration(
        WorkspaceCommandRestorationHost host,
        WorkspaceRealizationOperationLease authority,
        CompleteWorkspaceActivation workspace)
    {
        _host = host;
        _authority = authority;
        Workspace = workspace;
    }

    internal WorkspaceRealizationOperationLease Authority => _authority;

    internal CompleteWorkspaceActivation Workspace { get; }

    internal static async Task<WorkspacePacketRestorationResult> RestoreAsync(
        string input,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken,
        string optionName = "--workspace")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
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
        CompleteRestorationResult<WorkspaceRealizationOperationLease> result =
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
                WorkspaceRealizationOperationLease>.Activated activated)
        {
            return new WorkspacePacketRestorationResult.Restored(
                new WorkspacePacketRestoration(
                    host,
                    activated.Activation,
                    activated.Workspace));
        }

        await host.DisposeAsync().ConfigureAwait(false);
        return new WorkspacePacketRestorationResult.Failed(
            "The Workspace packet could not be restored.",
            result switch
            {
                CompleteRestorationResult<
                    WorkspaceRealizationOperationLease>.Failed failed =>
                    RestorationFailureDetails(failed.Failure),
                CompleteRestorationResult<
                    WorkspaceRealizationOperationLease>.Superseded =>
                    ["The restoration request was superseded."],
                _ => ["The restoration returned an unsupported result."],
            });
    }

    internal static string GetPacketInput(
        string value,
        string optionName)
    {
        if (value.StartsWith(
                WorkspaceShareOutput.UrlPrefix,
                StringComparison.Ordinal))
        {
            string packet =
                value[WorkspaceShareOutput.UrlPrefix.Length..];
            if (packet.Length == 0)
            {
                throw new InvalidDataException(
                    "The Workspace URL does not contain a packet.");
            }
            return packet;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                $"{optionName} accepts a canonical packet or an exact "
                    + $"{WorkspaceShareOutput.UrlPrefix}<packet> URL.");
        }

        return value;
    }

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

    public async ValueTask DisposeAsync()
    {
        _authority.Dispose();
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}
