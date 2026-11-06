using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Source-certified, resource-free identity of one exact implementation
/// artifact, independent of its requesting population and registration.
/// </summary>
/// <remarks>
/// Source adapters compare their own authority, actual source member,
/// immutable content evidence, and managed identity. Different source domains
/// must not compare equal. This identity grants no content access, population
/// membership, source authorization, or core-library entitlement.
/// </remarks>
public abstract class PlatformImplementationArtifactIdentity :
    IEquatable<PlatformImplementationArtifactIdentity>
{
    protected PlatformImplementationArtifactIdentity(
        AssemblyReferenceIdentity assemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(assemblyIdentity);
        AssemblyIdentity = assemblyIdentity;
    }

    public AssemblyReferenceIdentity AssemblyIdentity { get; }

    public abstract bool Equals(
        PlatformImplementationArtifactIdentity? other);

    public sealed override bool Equals(object? obj) =>
        obj is PlatformImplementationArtifactIdentity other && Equals(other);

    public abstract override int GetHashCode();
}
