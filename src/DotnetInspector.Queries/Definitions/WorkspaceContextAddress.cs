namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Portable address of one context within an activated canonical workspace
/// definition composition.
/// </summary>
/// <remarks>
/// The address is relative to its activation. Equal addresses from different
/// activations do not by themselves prove context correspondence.
/// </remarks>
public sealed record WorkspaceContextAddress
{
    public WorkspaceContextAddress(
        string workspaceId,
        string contextName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextName);
        WorkspaceId = workspaceId;
        ContextName = contextName;
    }

    public string WorkspaceId { get; }

    public string ContextName { get; }
}

/// <summary>
/// Compact owner-issued facts for identifying and displaying one resolved
/// workspace context.
/// </summary>
public sealed record WorkspaceContextDescriptor
{
    public WorkspaceContextDescriptor(
        WorkspaceContextAddress address,
        string? framework,
        string? runtimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
        Framework = framework;
        RuntimeIdentifier = runtimeIdentifier;
    }

    public WorkspaceContextAddress Address { get; }

    public string Name => Address.ContextName;

    public string? Framework { get; }

    public string? RuntimeIdentifier { get; }
}
