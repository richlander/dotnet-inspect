using System.Collections.Immutable;
using Inspector.Artifacts;

namespace Inspector.Artifacts.Workspaces;

/// <summary>
/// Resource-free evidence for one exact published Artifact content item.
/// </summary>
/// <remarks>
/// Creating the reference is query-authorized. Once returned, its descriptor,
/// registration, provenance, and roles remain immutable evidence without
/// retaining the issuing session or query authority.
/// </remarks>
public sealed class ArtifactContentReference
{
    private readonly ImmutableArray<ArtifactWorkspaceRole> _roles;

    internal ArtifactContentReference(
        ArtifactDescriptor descriptor,
        ArtifactAcquisitionRegistration registration,
        ImmutableArray<ArtifactWorkspaceRole> roles)
    {
        Descriptor = descriptor;
        Registration = registration;
        _roles = roles;
    }

    public ArtifactDescriptor Descriptor { get; }

    public ArtifactAcquisitionRegistration Registration { get; }

    public bool HasRole(ArtifactWorkspaceRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        foreach (ArtifactWorkspaceRole candidate in _roles)
        {
            if (ReferenceEquals(candidate, role))
                return true;
        }

        return false;
    }
}
