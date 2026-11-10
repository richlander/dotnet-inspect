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

        if (IsMemberOverviewBinding(selected) && options.Count)
        {
            CommandError.Write(CountOutput.SectionRequiredMessage);
            return 1;
        }
        if (IsNativeTypeOverviewBinding(packet, selected) && options.Count)
        {
            CommandError.Write(
                "The saved Type overview has no standalone Count terminal.");
            return 1;
        }
        if (IsNativeBinding(packet, selected)
            && !options.Count
            && options.Format is not (OutputFormat.Markdown or OutputFormat.PlainText or OutputFormat.Json))
        {
            CommandError.Write(
                "Native hierarchy replay supports tree and JSON output.");
            return 1;
        }
        if (IsNativeTypeOverviewBinding(packet, selected)
            && options.RowSelection is not null)
        {
            CommandError.Write(
                "Semantic row selection is not supported by the Type overview hierarchy.");
            return 1;
        }

        WorkspaceOptions inventory = options with { Inventory = true };
        if (IsPackageLibrariesBinding(packet, selected)
            || IsTypeApiBinding(packet, selected)
            || IsMemberOverviewBinding(packet, selected)
            || IsNativeBinding(packet, selected))
        {
            return capabilities is null
                ? await WorkspaceCommand.ExecutePacketOperationAsync(
                    inventory,
                    load => ExecuteSubjectAsync(packet, options, load, cancellationToken),
                    cancellationToken).ConfigureAwait(false)
                : await ExecuteSubjectAsync(packet, options, capabilities, cancellationToken)
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
        if (IsPackageLibrariesBinding(packet, state)
            || IsTypeApiBinding(packet, state)
            || IsMemberOverviewBinding(packet, state)
            || IsNativeBinding(packet, state))
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

    internal static bool IsTypeApiBinding(WorkspaceShareViewState? state) =>
        state is { Subject: PortableSubjectRequest.Type, QueryIndexes.Count: 0 }
        && state.Facet == TypeCommand.SavedApiFacet.Value;

    internal static bool IsMemberOverviewBinding(WorkspaceShareViewState? state) =>
        state is { Subject: PortableSubjectRequest.Member, QueryIndexes.Count: 0 }
        && state.Facet == MemberCommand.SavedOverviewFacet.Value;

    private static bool IsPackageLibrariesBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        packet.FormatVersion != InspectionDefinitionSchema.Version6
        && IsPackageLibrariesBinding(state);

    private static bool IsTypeApiBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        packet.FormatVersion != InspectionDefinitionSchema.Version6
        && IsTypeApiBinding(state);

    private static bool IsMemberOverviewBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        packet.FormatVersion != InspectionDefinitionSchema.Version6
        && IsMemberOverviewBinding(state);

    internal static bool IsNativeBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        IsNativeLibraryTypesBinding(packet, state)
        || IsNativeTypeOverviewBinding(packet, state);

    private static bool IsNativeLibraryTypesBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        packet.FormatVersion == InspectionDefinitionSchema.Version6
        && state is
        {
            Subject: PortableSubjectRequest.Library,
            QueryIndexes.Count: 0,
            Facet: "library.types",
        };

    private static bool IsNativeTypeOverviewBinding(
        WorkspaceSharePacket packet, WorkspaceShareViewState? state) =>
        packet.FormatVersion == InspectionDefinitionSchema.Version6
        && state is
        {
            Subject: PortableSubjectRequest.Type,
            QueryIndexes.Count: 0,
            Facet: "type.overview",
        };

    // Presentation lowering runs before command dispatch, without restoration.
    internal static bool UsesRenderedLineOutput(string? input)
    {
        if (input is null)
            return false;
        try
        {
            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(input);
            WorkspaceShareViewState? selected = packet.ViewStates.SingleOrDefault(
                state => state.TabIndex == packet.FocusedTabIndex);
            return IsPackageLibrariesBinding(packet, selected)
                || IsTypeApiBinding(packet, selected)
                || IsMemberOverviewBinding(packet, selected)
                || IsNativeBinding(packet, selected);
        }
        catch (WorkspaceSharePacketException)
        {
            return false;
        }
    }

    static async Task<int> ExecuteSubjectAsync(
        WorkspaceSharePacket packet,
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
                IsNativeBinding(packet, packet.ViewStates.Single(
                    state => state.TabIndex == packet.FocusedTabIndex))
                ? await CompleteRestorationSelectedSubjectOperation
                    .ExecuteAdmittedAsync(
                        active.Workspace, active.Activation,
                        (target, lease, token) =>
                            WorkspacePacketNativeInspection.ExecuteAsync(
                                options, target, lease, token),
                        cancellationToken).ConfigureAwait(false)
                : await CompleteRestorationSelectedSubjectOperation.ExecuteAsync(
                    active.Workspace, active.Activation,
                    (target, roles, token) => target switch
                    {
                        CompleteRestorationSelectedSubject.Member member =>
                            MemberCommand.ExecuteSavedOverviewAsync(options, active.Workspace, member, roles, token),
                        CompleteRestorationSelectedSubject.Type type =>
                            TypeCommand.ExecuteSavedApiAsync(options, active.Workspace, type, roles, token),
                        _ => PackageCommand.ExecuteSavedLibrariesAsync(options, target, token),
                    }, cancellationToken).ConfigureAwait(false);
            if (result is CompleteRestorationSubjectResult<int>.Completed completed)
                return completed.Content;
            var unavailable = (CompleteRestorationSubjectResult<int>.Unavailable)result;
            CommandError.Write(
                $"The saved subject is unavailable: {unavailable.Failure.Reason}.");
            return 1;
        }).ConfigureAwait(false);
    }

}
