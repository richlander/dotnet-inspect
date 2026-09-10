using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.Libraries;

/// <summary>
/// One Metadata-projected assembly bound to its owner-retained content.
/// </summary>
public sealed class SourceReadyAssembly
{
    public SourceReadyAssembly(
        ArtifactContentReference content,
        ArtifactAssemblyProjection projection)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(projection);

        ArtifactAcquisitionRegistration registration = content.Registration;
        if (!ReferenceEquals(
                projection.Registration.Generation,
                registration.Generation)
            || !ReferenceEquals(
                projection.Registration.Artifact,
                registration.Artifact))
        {
            throw new ArgumentException(
                "The assembly projection must describe the retained assembly artifact.",
                nameof(projection));
        }

        Content = content;
        Projection = projection;
        Registration = registration;
    }

    public ArtifactContentReference Content { get; }

    public ArtifactAssemblyProjection Projection { get; }

    public ArtifactAcquisitionRegistration Registration { get; }

    public IArtifactProvenance Provenance => Registration.Provenance;
}

/// <summary>
/// Owner-retained content designated as the companion Portable PDB candidate
/// for one exact assembly.
/// </summary>
/// <remarks>
/// This association does not claim that the PDB content matches the assembly.
/// The PDB owner must validate applicability before use.
/// </remarks>
public sealed class SourceReadyPortablePdbCandidate
{
    internal SourceReadyPortablePdbCandidate(
        AssemblyProjectionRegistration assembly,
        ArtifactContentReference content,
        ArtifactAcquisitionRegistration registration)
    {
        Assembly = assembly;
        Content = content;
        Registration = registration;
    }

    public AssemblyProjectionRegistration Assembly { get; }

    public ArtifactContentReference Content { get; }

    public ArtifactAcquisitionRegistration Registration { get; }

    public IArtifactProvenance Provenance => Registration.Provenance;
}

/// <summary>
/// Owner-retained source content addressable by its artifact identity.
/// </summary>
public sealed class SourceReadySourceContent
{
    internal SourceReadySourceContent(ArtifactContentReference content)
    {
        Content = content;
        Registration = content.Registration;
    }

    public ArtifactContentReference Content { get; }

    public ArtifactAcquisitionRegistration Registration { get; }

    public IArtifactProvenance Provenance => Registration.Provenance;
}

/// <summary>
/// One exact source target backed by retained assembly content and an optional
/// companion Portable PDB candidate.
/// </summary>
public sealed class SourceReadyLibrary
{
    public SourceReadyLibrary(
        SourceReadyAssembly assembly,
        ArtifactContentReference? portablePdbContent = null,
        IEnumerable<ArtifactContentReference>? retainedSourceContent = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Assembly = assembly;
        if (portablePdbContent is not null)
        {
            ArtifactAcquisitionRegistration registration =
                portablePdbContent.Registration;
            if (ReferenceEquals(
                    registration.Artifact,
                    assembly.Registration.Artifact))
            {
                throw new ArgumentException(
                    "The companion Portable PDB must be distinct from the assembly artifact.",
                    nameof(portablePdbContent));
            }
            PortablePdbCandidate =
                new SourceReadyPortablePdbCandidate(
                    assembly.Projection.Registration,
                    portablePdbContent,
                    registration);
        }
        RetainedSourceContent = SnapshotSourceContent(
            retainedSourceContent,
            assembly.Registration.Artifact,
            PortablePdbCandidate?.Registration.Artifact);
    }

    public SourceReadyAssembly Assembly { get; }

    public SourceReadyPortablePdbCandidate? PortablePdbCandidate { get; }

    public IReadOnlyList<SourceReadySourceContent> RetainedSourceContent
    {
        get;
    }

    private static IReadOnlyList<SourceReadySourceContent>
        SnapshotSourceContent(
            IEnumerable<ArtifactContentReference>? content,
            ArtifactIdentity assembly,
            ArtifactIdentity? portablePdb)
    {
        if (content is null)
            return Array.Empty<SourceReadySourceContent>();

        var seen = new HashSet<ArtifactIdentity>(
            ReferenceEqualityComparer.Instance);
        var snapshot = new List<SourceReadySourceContent>();
        foreach (ArtifactContentReference reference in content)
        {
            ArgumentNullException.ThrowIfNull(reference, nameof(content));
            var source = new SourceReadySourceContent(reference);
            ArtifactIdentity artifact = source.Registration.Artifact;
            if (ReferenceEquals(artifact, assembly)
                || ReferenceEquals(artifact, portablePdb))
            {
                throw new ArgumentException(
                    "Retained source content must be distinct from the assembly and Portable PDB artifacts.",
                    nameof(content));
            }
            if (!seen.Add(artifact))
            {
                throw new ArgumentException(
                    "Retained source content cannot contain the same artifact more than once.",
                    nameof(content));
            }
            snapshot.Add(source);
        }

        return snapshot.AsReadOnly();
    }
}
