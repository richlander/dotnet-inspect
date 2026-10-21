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
        bool workspace = state.Subject is PortableSubjectRequest.Workspace
            || (state.Subject is null && state.TabIndex is null);
        if (!workspace || state.Facet is not null)
            return $"The saved {state.Subject?.Kind} facet '{state.Facet}' has no CLI activation execution binding.";
        return null;
    }
}
