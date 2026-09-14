using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Libraries;

/// <summary>
/// Resource-free evidence for one content item in one exact realized Library.
/// </summary>
public sealed class LibraryContentReference
{
    private readonly LibraryContentRole[] _roles;

    internal LibraryContentReference(
        LibraryReference library,
        ArtifactContentReference artifactReference,
        IEnumerable<LibraryContentRole> roles,
        ManagedMetadataIdentity.Assembly? assemblyIdentity,
        LibraryContentReference? associatedAssembly)
    {
        Library = library;
        ArtifactReference = artifactReference;
        _roles = [.. roles];
        Roles = Array.AsReadOnly(_roles);
        AssemblyIdentity = assemblyIdentity;
        AssociatedAssembly = associatedAssembly;
    }

    public LibraryReference Library { get; }
    public ArtifactContentReference ArtifactReference { get; }
    public ArtifactIdentity Artifact => ArtifactReference.Artifact;
    public ArtifactGenerationIdentity Generation => ArtifactReference.Generation;
    public ArtifactAcquisitionRegistration Registration =>
        ArtifactReference.Registration;
    public IArtifactProvenance Provenance => ArtifactReference.Provenance;
    public IReadOnlyList<LibraryContentRole> Roles { get; }
    public ManagedMetadataIdentity.Assembly? AssemblyIdentity { get; }
    public LibraryContentReference? AssociatedAssembly { get; }

    public bool HasRole(LibraryContentRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        return Array.IndexOf(_roles, role) >= 0;
    }
}

/// <summary>
/// Immutable resource-free reference to one exact realized managed Library.
/// </summary>
public sealed class LibraryReference
{
    private LibraryReference(
        ExactLibrarySourceCoordinate? sourceCoordinate,
        LibraryAssemblyCorrespondence assemblyCorrespondence,
        IEnumerable<LibraryCompanionCorrespondence>? companionCorrespondences)
    {
        ArgumentNullException.ThrowIfNull(assemblyCorrespondence);
        LibraryCompanionCorrespondence[] companions =
            companionCorrespondences?.ToArray() ?? [];
        if (companions.Any(companion => companion is null))
        {
            throw new ArgumentException(
                "Companion correspondence cannot contain null.",
                nameof(companionCorrespondences));
        }

        if (sourceCoordinate is not null
            && !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                sourceCoordinate.LibraryIdentity.Identity,
                assemblyCorrespondence.ApiIdentity.Identity))
        {
            throw new ArgumentException(
                "The source coordinate and API assembly must identify the same managed Library.",
                nameof(sourceCoordinate));
        }

        ValidateCompanions(assemblyCorrespondence, companions);

        SourceCoordinate = sourceCoordinate;
        AssemblyCorrespondence = assemblyCorrespondence;
        CompanionCorrespondences = Array.AsReadOnly(companions);

        var contents = new List<LibraryContentReference>(
            2 + companions.Length);
        var assemblyReferences =
            new Dictionary<
                ArtifactContentReference,
                LibraryContentReference>(
                    ReferenceEqualityComparer.Instance);

        bool oneAssemblyServesBothRoles = ReferenceEquals(
            assemblyCorrespondence.ApiAssembly,
            assemblyCorrespondence.ImplementationAssembly);
        ApiAssembly = new LibraryContentReference(
            this,
            assemblyCorrespondence.ApiAssembly,
            oneAssemblyServesBothRoles
                ? [
                    LibraryContentRole.ApiAssembly,
                    LibraryContentRole.ImplementationAssembly,
                ]
                : [LibraryContentRole.ApiAssembly],
            assemblyCorrespondence.ApiIdentity,
            associatedAssembly: null);
        contents.Add(ApiAssembly);
        assemblyReferences.Add(
            assemblyCorrespondence.ApiAssembly,
            ApiAssembly);

        if (oneAssemblyServesBothRoles)
        {
            ImplementationAssembly = ApiAssembly;
        }
        else if (assemblyCorrespondence.ImplementationAssembly is not null)
        {
            ImplementationAssembly = new LibraryContentReference(
                this,
                assemblyCorrespondence.ImplementationAssembly,
                [LibraryContentRole.ImplementationAssembly],
                assemblyCorrespondence.ImplementationIdentity,
                associatedAssembly: null);
            contents.Add(ImplementationAssembly);
            assemblyReferences.Add(
                assemblyCorrespondence.ImplementationAssembly,
                ImplementationAssembly);
        }

        foreach (LibraryCompanionCorrespondence companion in companions)
        {
            var reference = new LibraryContentReference(
                this,
                companion.Content,
                [companion.Role],
                assemblyIdentity: null,
                associatedAssembly:
                    assemblyReferences[companion.Assembly]);
            contents.Add(reference);
        }

        Contents = Array.AsReadOnly(contents.ToArray());
    }

    public ExactLibrarySourceCoordinate? SourceCoordinate { get; }
    public bool IsDirectArtifact => SourceCoordinate is null;
    public LibraryAssemblyCorrespondence AssemblyCorrespondence { get; }
    public IReadOnlyList<LibraryCompanionCorrespondence>
        CompanionCorrespondences { get; }
    public LibraryContentReference ApiAssembly { get; }
    public LibraryContentReference? ImplementationAssembly { get; }
    public IReadOnlyList<LibraryContentReference> Contents { get; }

    public static LibraryReference CreateFromSource(
        ExactLibrarySourceCoordinate sourceCoordinate,
        LibraryAssemblyCorrespondence assemblyCorrespondence,
        IEnumerable<LibraryCompanionCorrespondence>?
            companionCorrespondences = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCoordinate);
        return new LibraryReference(
            sourceCoordinate,
            assemblyCorrespondence,
            companionCorrespondences);
    }

    public static LibraryReference CreateDirect(
        LibraryAssemblyCorrespondence assemblyCorrespondence,
        IEnumerable<LibraryCompanionCorrespondence>?
            companionCorrespondences = null) =>
        new(
            null,
            assemblyCorrespondence,
            companionCorrespondences);

    private static void ValidateCompanions(
        LibraryAssemblyCorrespondence assembly,
        IReadOnlyList<LibraryCompanionCorrespondence> companions)
    {
        var usedContent = new HashSet<ArtifactContentReference>(
            ReferenceEqualityComparer.Instance)
        {
            assembly.ApiAssembly,
        };
        if (assembly.ImplementationAssembly is not null)
            usedContent.Add(assembly.ImplementationAssembly);

        foreach (LibraryCompanionCorrespondence companion in companions)
        {
            if (!ReferenceEquals(
                    assembly.ApiAssembly.Generation,
                    companion.Content.Generation))
            {
                throw new ArgumentException(
                    "Every Library content item must belong to the same Artifact generation.",
                    nameof(companions));
            }

            if (!assembly.Contains(companion.Assembly))
            {
                throw new ArgumentException(
                    "A companion must name an assembly in the same Library.",
                    nameof(companions));
            }

            if (companion.Role == LibraryContentRole.PortablePdb
                && !ReferenceEquals(
                    assembly.ImplementationAssembly,
                    companion.Assembly))
            {
                throw new ArgumentException(
                    "A Portable PDB companion must name the exact implementation assembly.",
                    nameof(companions));
            }

            if (!usedContent.Add(companion.Content))
            {
                throw new ArgumentException(
                    "One Artifact content reference cannot occupy multiple Library content records.",
                    nameof(companions));
            }
        }
    }
}
