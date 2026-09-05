using DotnetInspector.Artifacts;

namespace DotnetInspector.Artifacts.Workspaces;

/// <summary>
/// An owner-issued reference to one published artifact's identity,
/// registration, roles, and retained content.
/// </summary>
/// <remarks>
/// The query lease remains caller-owned. Registration and role observations
/// and content opens revalidate it against the issuing session. Binding
/// integrity is gated by
/// <c>ArtifactContentReference_BindsIdentityRegistrationRoleAndContent</c>.
/// </remarks>
public sealed class ArtifactContentReference
{
    private readonly ArtifactSetSession _owner;
    private readonly ArtifactQueryLease _lease;

    internal ArtifactContentReference(
        ArtifactSetSession owner,
        ArtifactDescriptor descriptor,
        ArtifactQueryLease lease)
    {
        _owner = owner;
        Descriptor = descriptor;
        _lease = lease;
    }

    public ArtifactDescriptor Descriptor { get; }

    public ArtifactAcquisitionRegistration Registration =>
        _owner.GetRegistration(Descriptor.Identity, _lease);

    public bool HasRole(ArtifactWorkspaceRole role) =>
        _owner.HasRole(Descriptor.Identity, role, _lease);

    public Stream OpenRead() =>
        _owner.OpenRead(Descriptor.Identity, _lease);

    public ArtifactContentAccessOutcome<ArtifactContentDigest> GetContentDigest(
        Action<long> chargeWork,
        CancellationToken cancellationToken = default) =>
        _owner.GetContentDigest(
            Descriptor.Identity,
            _lease,
            chargeWork,
            cancellationToken);
}
