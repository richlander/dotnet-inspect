using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.PlatformHouse;

internal sealed class PlatformLibraryContentSelectionEvidence
{
    internal PlatformLibraryContentSelectionEvidence(
        ArtifactContentReference content,
        ArtifactAssemblyProjection projection)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(projection);
        if (content.Provenance
                is not PlatformLibraryArtifactProvenance provenance)
        {
            throw new ArgumentException(
                "Selected Library content must retain its Platform source realization as Artifact provenance.",
                nameof(content));
        }
        if (!ReferenceEquals(
                projection.Registration.Generation,
                content.Generation)
            || !ReferenceEquals(
                projection.Registration.Artifact,
                content.Artifact))
        {
            throw new ArgumentException(
                "Selected Library content requires the Metadata projection issued for its exact Artifact.",
                nameof(projection));
        }

        var assemblyIdentity =
            new ManagedMetadataIdentity.Assembly(projection.Identity);
        if (assemblyIdentity.Identity.Version is null)
        {
            throw new ArgumentException(
                "Selected Library content requires an exact assembly version.",
                nameof(projection));
        }

        Contribution = provenance.Contribution;
        Content = content;
        Projection = projection;
        AssemblyIdentity = assemblyIdentity;
    }

    internal PlatformSourceContribution.Realization Contribution { get; }
    internal ArtifactContentReference Content { get; }
    internal ArtifactAssemblyProjection Projection { get; }
    internal ManagedMetadataIdentity.Assembly AssemblyIdentity { get; }
}

/// <summary>
/// Resource-free certification of one exact Library realized by PlatformHouse.
/// </summary>
public sealed class PlatformLibraryReference
{
    private readonly PlatformSourceContribution.Realization[] _contributions;

    internal PlatformLibraryReference(
        LibraryReference library,
        PlatformFamilyTarget target,
        IEnumerable<PlatformLibraryContentSelectionEvidence> selections,
        PlatformViewCorrespondenceIdentity? viewCorrespondenceIdentity)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selections);

        PlatformLibraryContentSelectionEvidence[] selected = [.. selections];
        if (selected.Length == 0 || selected.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A Platform Library certificate requires selected content evidence.",
                nameof(selections));
        }
        if (library.SourceCoordinate
                is not ExactLibrarySourceCoordinate.Platform coordinate
            || coordinate.Population.Family != target.Family)
        {
            throw new ArgumentException(
                "A Platform Library certificate requires an exact matching Platform source coordinate.",
                nameof(library));
        }
        if (selected.Any(
                selection => !library.Contents.Any(
                    content => ReferenceEquals(
                        content.ArtifactReference,
                        selection.Content)))
            || !Contains(selected, library.ApiAssembly.ArtifactReference)
            || library.ImplementationAssembly is { } implementation
                && !Contains(selected, implementation.ArtifactReference))
        {
            throw new ArgumentException(
                "A Platform Library certificate requires exact selected evidence for every assembly view.",
                nameof(selections));
        }

        bool hasImplementation = library.ImplementationAssembly is not null;
        if (hasImplementation != (viewCorrespondenceIdentity is not null))
        {
            throw new ArgumentException(
                "Implementation-bearing Platform Libraries require exact view-correspondence identity.",
                nameof(viewCorrespondenceIdentity));
        }

        _contributions =
        [
            .. selected.Select(static item => item.Contribution)
                .Distinct<PlatformSourceContribution.Realization>(
                    ReferenceEqualityComparer.Instance),
        ];
        Library = library;
        Target = target;
        Contributions = Array.AsReadOnly(_contributions);
        ViewCorrespondenceIdentity = viewCorrespondenceIdentity;
    }

    public LibraryReference Library { get; }
    public PlatformFamilyTarget Target { get; }
    public IReadOnlyList<PlatformSourceContribution.Realization>
        Contributions { get; }
    public PlatformViewCorrespondenceIdentity? ViewCorrespondenceIdentity
    {
        get;
    }

    private static bool Contains(
        IReadOnlyList<PlatformLibraryContentSelectionEvidence> selections,
        ArtifactContentReference content) =>
        selections.Any(
            selection => ReferenceEquals(selection.Content, content));
}
