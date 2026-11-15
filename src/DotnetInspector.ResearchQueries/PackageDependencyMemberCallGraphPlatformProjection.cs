using System.Collections.Immutable;

using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;

namespace DotnetInspector.ResearchQueries;

/// <summary>
/// Retains one source-issued population member beside its exact Workspace
/// admission, without replacing actual attribution with the requesting family.
/// </summary>
public sealed class PackageDependencyMemberCallGraphPlatformLibrary
{
    public PackageDependencyMemberCallGraphPlatformLibrary(
        PlatformPopulationMember member,
        WorkspaceLibraryOccurrence library,
        bool definesCoreLibraryRoot = false)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(library);
        if (!ReferenceEquals(member.PlatformLibrary.Library, library.Library))
        {
            throw new ArgumentException(
                "The admitted Library must be the exact Platform population member.",
                nameof(library));
        }
        Member = member;
        Library = library;
        DefinesCoreLibraryRoot = definesCoreLibraryRoot;
    }

    public PlatformPopulationMember Member { get; }
    public WorkspaceLibraryOccurrence Library { get; }
    public bool DefinesCoreLibraryRoot { get; }
}

internal sealed class PackageDependencyMemberCallGraphPlatformProjection
{
    PlatformImplementationArtifactIdentity? _coreLibrary;
    PlatformFamilyTarget? _coreLibraryTarget;

    internal ImmutableArray<PackageAssemblyContextPlatformLibrary> Project(
        IEnumerable<PackageDependencyMemberCallGraphPlatformLibrary> libraries)
    {
        var selected = new List<(
            PackageDependencyMemberCallGraphPlatformLibrary Source,
            bool DefinesCoreLibraryRoot)>();
        var indices = new Dictionary<(
            PlatformFamilyTarget Target,
            PlatformImplementationArtifactIdentity Artifact), int>();
        foreach (PackageDependencyMemberCallGraphPlatformLibrary library
            in libraries)
        {
            PlatformFamilyTarget target = library.Member.Target;
            PlatformImplementationArtifactIdentity? artifact =
                (library.Library.Library.ImplementationAssembly?
                    .ArtifactReference.Provenance
                    as PlatformLibraryArtifactProvenance)?
                    .ImplementationArtifactIdentity;
            if (library.DefinesCoreLibraryRoot && artifact is not null)
            {
                // Only an explicit intrinsic continuation can establish this
                // request-local authority; correspondence merely preserves it.
                _coreLibrary = artifact;
                _coreLibraryTarget = target;
            }
            bool definesCoreLibraryRoot =
                library.DefinesCoreLibraryRoot
                || artifact is not null
                    && target == _coreLibraryTarget
                    && artifact.Equals(_coreLibrary);
            if (artifact is not null
                && indices.TryGetValue((target, artifact), out int index))
            {
                var previous = selected[index];
                selected[index] = (
                    previous.Source.Member.Role
                            == PlatformPopulationMemberRole.BindingSupport
                        && library.Member.Role
                            == PlatformPopulationMemberRole.Focus
                        ? library
                        : previous.Source,
                    previous.DefinesCoreLibraryRoot
                        || definesCoreLibraryRoot);
            }
            else
            {
                if (artifact is not null)
                    indices.Add((target, artifact), selected.Count);
                selected.Add((library, definesCoreLibraryRoot));
            }
        }
        return
        [
            .. selected.Select(
                item => new PackageAssemblyContextPlatformLibrary(
                    item.Source.Member.Target,
                    item.Source.Library,
                    item.DefinesCoreLibraryRoot)),
        ];
    }
}
