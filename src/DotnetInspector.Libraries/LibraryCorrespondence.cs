using Inspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.Libraries;

/// <summary>
/// Source-owner evidence associating exact Artifact registrations with the API
/// and optional implementation views of one managed Library.
/// </summary>
public sealed class LibraryAssemblyCorrespondence
{
    public LibraryAssemblyCorrespondence(
        ArtifactContentReference apiAssembly,
        ManagedMetadataIdentity.Assembly apiIdentity,
        ArtifactContentReference? implementationAssembly = null,
        ManagedMetadataIdentity.Assembly? implementationIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(apiAssembly);
        LibraryContractValidation.ValidateExactIdentity(
            apiIdentity,
            nameof(apiIdentity));

        if ((implementationAssembly is null)
            != (implementationIdentity is null))
        {
            throw new ArgumentException(
                "Implementation content and identity must either both be present or both be absent.",
                implementationAssembly is null
                    ? nameof(implementationIdentity)
                    : nameof(implementationAssembly));
        }

        if (implementationAssembly is not null)
        {
            LibraryContractValidation.ValidateExactIdentity(
                implementationIdentity!,
                nameof(implementationIdentity));
            if (!ReferenceEquals(
                    apiAssembly.Generation,
                    implementationAssembly.Generation))
            {
                throw new ArgumentException(
                    "API and implementation content must belong to the same Artifact generation.",
                    nameof(implementationAssembly));
            }

            if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    apiIdentity.Identity,
                    implementationIdentity!.Identity))
            {
                throw new ArgumentException(
                    "API and implementation content must identify the same managed Library.",
                    nameof(implementationIdentity));
            }

        }

        ApiAssembly = apiAssembly;
        ApiIdentity = apiIdentity;
        ImplementationAssembly = implementationAssembly;
        ImplementationIdentity = implementationIdentity;
    }

    public ArtifactContentReference ApiAssembly { get; }
    public ManagedMetadataIdentity.Assembly ApiIdentity { get; }
    public ArtifactContentReference? ImplementationAssembly { get; }
    public ManagedMetadataIdentity.Assembly? ImplementationIdentity { get; }

    internal bool Contains(ArtifactContentReference content) =>
        ReferenceEquals(ApiAssembly, content)
        || ReferenceEquals(ImplementationAssembly, content);
}

/// <summary>
/// Source-owner evidence associating one exact companion Artifact with one
/// exact assembly Artifact.
/// </summary>
public sealed class LibraryCompanionCorrespondence
{
    public LibraryCompanionCorrespondence(
        ArtifactContentReference content,
        LibraryContentRole role,
        ArtifactContentReference assembly)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(assembly);
        if (role is not LibraryContentRole.CompiledXmlDocumentation
            and not LibraryContentRole.PortablePdb)
        {
            throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "A companion must have a compiled XML documentation or Portable PDB role.");
        }

        if (!ReferenceEquals(content.Generation, assembly.Generation))
        {
            throw new ArgumentException(
                "Companion and assembly content must belong to the same Artifact generation.",
                nameof(assembly));
        }

        if (ReferenceEquals(content, assembly))
        {
            throw new ArgumentException(
                "Companion content must be distinct from its associated assembly content.",
                nameof(content));
        }

        Content = content;
        Role = role;
        Assembly = assembly;
    }

    public ArtifactContentReference Content { get; }
    public LibraryContentRole Role { get; }
    public ArtifactContentReference Assembly { get; }
}

internal static class LibraryContractValidation
{
    internal static void ValidateExactIdentity(
        ManagedMetadataIdentity.Assembly? identity,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(identity, parameterName);
        ArgumentNullException.ThrowIfNull(identity.Identity, parameterName);
        if (identity.Identity.Version is null)
        {
            throw new ArgumentException(
                "A realized Library requires an exact assembly version.",
                parameterName);
        }
    }
}
