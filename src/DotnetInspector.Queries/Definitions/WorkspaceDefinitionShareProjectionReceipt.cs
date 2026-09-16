namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Identifies which Definitions-owned route produced one canonical Workspace
/// Share projection.
/// </summary>
public enum WorkspaceDefinitionShareProjectionSource
{
    PacketInput,
    DefinitionInput,
}

/// <summary>
/// Resource-free evidence that Workspace Definitions associated one canonical
/// packet with one exact realized definition basis.
/// </summary>
public sealed class WorkspaceDefinitionShareProjectionReceipt
{
    internal WorkspaceDefinitionShareProjectionReceipt(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceDefinitionShareProjectionSource source,
        WorkspaceSharePacket packet)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(packet);
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));

        Workspace = definition.Workspace;
        RegistrationRevision = definition.Registrations.Identity;
        ScopeRevision = definition.Scope.Identity;
        Source = source;
        CanonicalPacket = WorkspaceSharePacketCodec.Encode(packet);
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceRegistrationRevisionIdentity RegistrationRevision { get; }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public WorkspaceDefinitionShareProjectionSource Source { get; }

    public string CanonicalPacket { get; }
}
