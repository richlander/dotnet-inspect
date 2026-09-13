using System.Collections.Immutable;
using Inspector.Artifacts;

namespace Inspector.Artifacts.Workspaces;

/// <summary>
/// Owner-issued resource-free evidence for one published immutable artifact.
/// </summary>
/// <remarks>
/// Content and digest access require separate explicit query or content-lease
/// authority.
/// </remarks>
public sealed class ArtifactContentReference
{
    internal ArtifactContentReference(
        ArtifactDescriptor descriptor,
        ArtifactAcquisitionRegistration registration,
        ImmutableArray<ArtifactWorkspaceRole> roles)
    {
        if (!ReferenceEquals(
                descriptor.Identity,
                registration.Artifact))
        {
            throw new ArgumentException(
                "The artifact descriptor and registration must name the same identity.",
                nameof(registration));
        }

        Descriptor = descriptor;
        Registration = registration;
        Roles = roles;
    }

    public ArtifactIdentity Artifact => Descriptor.Identity;
    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactDescriptor Descriptor { get; }
    public ArtifactAcquisitionRegistration Registration { get; }
    public IArtifactProvenance Provenance => Registration.Provenance;
    public ImmutableArray<ArtifactWorkspaceRole> Roles { get; }

    public bool HasRole(ArtifactWorkspaceRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        foreach (ArtifactWorkspaceRole candidate in Roles)
        {
            if (ReferenceEquals(candidate, role))
                return true;
        }

        return false;
    }
}
