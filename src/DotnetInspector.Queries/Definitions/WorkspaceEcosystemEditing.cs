using QuerySpace;

namespace DotnetInspector.Queries.Definitions;

/// <summary>Resource-free, immutable editing of portable Ecosystem registrations.</summary>
public static class WorkspaceEcosystemEditor
{
    public static WorkspaceSharePacketProjectionResult Add(
        WorkspaceSharePacket? packet,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (packet is not null && Find(packet, declaration.Id) >= 0)
            return Invalid($"Ecosystem '{declaration.Id}' already exists.");

        return Project(packet, [.. packet?.Registrations ?? [],
            new WorkspaceRegistration.Ecosystem(declaration)]);
    }

    /// <summary>Replaces one declaration in place, without changing its identity.</summary>
    public static WorkspaceSharePacketProjectionResult Update(
        WorkspaceSharePacket packet,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(declaration);
        int index = Find(packet, declaration.Id);
        if (index < 0)
            return Invalid($"Ecosystem '{declaration.Id}' is absent.");

        WorkspaceRegistration[] registrations = [.. packet.Registrations];
        registrations[index] = new WorkspaceRegistration.Ecosystem(declaration);
        return Project(packet, registrations);
    }

    public static WorkspaceSharePacketProjectionResult Remove(
        WorkspaceSharePacket packet,
        WorkspaceEcosystemRegistrationId id)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(id);
        int index = Find(packet, id);
        if (index < 0)
            return Invalid($"Ecosystem '{id}' is absent.");

        var registrations = new List<WorkspaceRegistration>(packet.Registrations);
        registrations.RemoveAt(index);
        return Project(packet, [.. registrations]);
    }

    private static int Find(
        WorkspaceSharePacket packet,
        WorkspaceEcosystemRegistrationId id)
    {
        for (int index = 0; index < packet.Registrations.Count; index++)
        {
            if (packet.Registrations[index] is WorkspaceRegistration.Ecosystem ecosystem
                && ecosystem.Declaration.Id == id)
                return index;
        }
        return -1;
    }

    private static WorkspaceSharePacketProjectionResult Project(
        WorkspaceSharePacket? source,
        WorkspaceRegistration[] registrations)
    {
        if (source is not null && source.FormatVersion < 3)
            return Invalid("Ecosystem editing requires Workspace packet format 3, 4, or 5.");

        // Scanner bindings are executable owner-issued capabilities, never packet data.
        foreach (WorkspaceRegistration registration in registrations)
        {
            if (registration is WorkspaceRegistration.Ecosystem
                { Declaration.IntegrationScanner: not null } ecosystem)
            {
                return WorkspaceSharePacketProjectionResult.Failed(
                    WorkspaceSharePacketProjectionFailureKind.NonProjectable,
                    "registrations",
                    $"Ecosystem '{ecosystem.Declaration.Id}' has an integration scanner "
                        + "that cannot be preserved in a portable packet.");
            }
        }

        WorkspaceShareTab[] tabs = [.. source?.Tabs ?? []];
        WorkspaceShareContext[] contexts = [.. source?.Contexts ?? []];
        WorkspaceShareViewState[] views = source is null
            ? [new(null, new PortableSubjectRequest.Workspace(), null, null)]
            : [.. source.ViewStates];
        PortableQueryIdentity[] queries = [.. source?.Queries ?? []];
        try
        {
            WorkspaceSharePacket candidate = source?.FormatVersion switch
            {
                WorkspaceSharePacketCodec.Format5Version => WorkspaceSharePacket.CreateV5(
                    tabs, contexts, registrations, [.. source.PackageSources],
                    source.FocusedTabIndex, source.SelectedContextIndex, views, queries),
                WorkspaceSharePacketCodec.Format4Version => WorkspaceSharePacket.CreateV4(
                    tabs, contexts, registrations,
                    source.FocusedTabIndex, source.SelectedContextIndex, views, queries),
                _ => new WorkspaceSharePacket(
                    tabs, contexts, registrations,
                    source?.FocusedTabIndex, source?.SelectedContextIndex, views, queries),
            };
            // The canonical codec validates the complete derived composition and limits.
            return WorkspaceSharePacketProjectionResult.Success(
                WorkspaceSharePacketCodec.Decode(WorkspaceSharePacketCodec.Encode(candidate)));
        }
        catch (Exception error) when (error is WorkspaceSharePacketException
            or InvalidDataException or ArgumentException)
        {
            return Invalid(error.Message);
        }
    }

    private static WorkspaceSharePacketProjectionResult Invalid(string detail) =>
        WorkspaceSharePacketProjectionResult.Failed(
            WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet,
            "registrations",
            detail);
}
