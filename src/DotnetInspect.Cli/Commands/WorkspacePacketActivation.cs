using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

// CLI execution bindings are deliberately narrower than Navigation availability.
internal static class WorkspacePacketActivation
{
    internal static async Task<int> ExecuteAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken,
        WorkspaceContextLoadOptions? capabilities = null)
    {
        WorkspaceSharePacket packet;
        try
        {
            packet = WorkspaceSharePacketCodec.Decode(
                WorkspacePacketRestoration.GetPacketInput(options.Packet!, "-w"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is WorkspaceSharePacketException or InvalidDataException)
        {
            CommandError.Write(ex.Message);
            return 1;
        }

        WorkspaceShareViewState? selected = packet.ViewStates
            .SingleOrDefault(state => state.TabIndex == packet.FocusedTabIndex);
        string? refusal = BindingError(packet, selected);
        if (refusal is not null)
        {
            CommandError.Write(refusal);
            return 1;
        }

        WorkspaceOptions inventory = options with { Inventory = true };
        if (IsPackageLibrariesBinding(selected))
        {
            return capabilities is null
                ? await WorkspaceCommand.ExecutePacketOperationAsync(
                    inventory,
                    load => ExecutePackageAsync(options, load, cancellationToken),
                    cancellationToken).ConfigureAwait(false)
                : await ExecutePackageAsync(options, capabilities, cancellationToken)
                    .ConfigureAwait(false);
        }
        return capabilities is null
            ? await WorkspaceCommand.ExecuteAsync(inventory, cancellationToken).ConfigureAwait(false)
            : await WorkspaceCommand.ExecuteAsync(inventory, capabilities, cancellationToken).ConfigureAwait(false);
    }

    internal static string? BindingError(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state)
    {
        if (!CompleteRestorationPreparation.SupportsPacketFormat(packet.FormatVersion))
            return $"CLI activation does not support Workspace packet format {packet.FormatVersion}.";
        if (state is null || state.QueryIndexes.Count != 0)
            return "The saved query has no CLI activation execution binding.";
        if (IsPackageLibrariesBinding(state))
            return null;
        bool workspace = state.Subject is PortableSubjectRequest.Workspace
            || (state.Subject is null && state.TabIndex is null);
        if (!workspace || state.Facet is not null)
            return $"The saved {state.Subject?.Kind} facet '{state.Facet}' has no CLI activation execution binding.";
        return null;
    }

    internal static bool IsPackageLibrariesBinding(WorkspaceShareViewState? state) =>
        state is { Subject: PortableSubjectRequest.Package, QueryIndexes.Count: 0 }
        && state.Facet == PackageCommand.SavedLibrariesFacet.Value;

    // Presentation lowering runs before command dispatch, without restoration.
    internal static bool UsesPackageLibrariesOutput(string? input)
    {
        if (input is null)
            return false;
        try
        {
            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(input);
            return IsPackageLibrariesBinding(packet.ViewStates.SingleOrDefault(
                state => state.TabIndex == packet.FocusedTabIndex));
        }
        catch (WorkspaceSharePacketException)
        {
            return false;
        }
    }

    static async Task<int> ExecutePackageAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions capabilities,
        CancellationToken cancellationToken)
    {
        WorkspacePacketRestorationResult restored =
            await WorkspacePacketRestoration.RestoreAsync(
                options.Packet!, capabilities, cancellationToken, "-w",
                captureSelectedSubject: true).ConfigureAwait(false);
        if (restored is WorkspacePacketRestorationResult.Failed failed)
        {
            CommandError.Write(failed.Summary, failed.Details);
            return 1;
        }
        WorkspacePacketRestoration restoration =
            ((WorkspacePacketRestorationResult.Restored)restored).Value;
        return await restoration.ExecuteAsync(async active =>
        {
            CompleteRestorationSubjectResult<int> result =
                await CompleteRestorationSelectedSubjectOperation.ExecuteAsync(
                    active.Workspace, active.Activation,
                    (target, token) => PackageCommand.ExecuteSavedLibrariesAsync(
                        options, target, token), cancellationToken).ConfigureAwait(false);
            if (result is CompleteRestorationSubjectResult<int>.Completed completed)
                return completed.Content;
            var unavailable = (CompleteRestorationSubjectResult<int>.Unavailable)result;
            CommandError.Write(
                $"The saved Package subject is unavailable: {unavailable.Failure.Reason}.");
            return 1;
        }).ConfigureAwait(false);
    }

}
