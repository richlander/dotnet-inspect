using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    /// <summary>
    /// Returns the first product-ordered ecosystem that authors an
    /// assembly-name claim, or null when no shipped ecosystem claims it.
    /// </summary>
    public static EcosystemDependencyDescriptor? FindAssemblyNameClaim(
        string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        foreach (EcosystemDependencyProfileEntry entry in
                 DependencyRecognitionProfile.Entries)
        {
            if (entry.ClaimsAssemblyName(assemblyName))
                return entry.Ecosystem;
        }

        return null;
    }

    /// <summary>
    /// Returns whether the first ecosystem claiming an assembly name belongs
    /// to the product's platform-curated lineage.
    /// </summary>
    public static bool ShouldSearchPlatform(string assemblyName) =>
        FindAssemblyNameClaim(assemblyName) is { } ecosystem
        && ProductEcosystemPacks.PlatformEcosystems.Contains(ecosystem.Id);

    /// <summary>
    /// Returns the Platform library population that the first claiming
    /// ecosystem itself declares, when that claimant is Platform-search
    /// eligible. Null means no eligible claim, or an eligible claimant that
    /// declares no Platform population of its own; lineage is not consulted
    /// and no other family is substituted.
    /// </summary>
    public static PlatformLibraryPopulationDeclaration?
        FindPlatformSearchPopulation(string assemblyName)
    {
        if (FindAssemblyNameClaim(assemblyName) is not { } ecosystem
            || !ProductEcosystemPacks.PlatformEcosystems.Contains(ecosystem.Id)
            || ProductEcosystemPacks.Registry.SelectWorkspaceRegistration(ecosystem.Id)
                is not EcosystemWorkspaceRegistrationSelectionResult.Known known)
        {
            return null;
        }

        PlatformLibraryPopulationDeclaration[] populations =
        [
            .. known.Declaration.Populations
                .OfType<WorkspaceEcosystemPopulationDeclaration.Platform>()
                .Select(static platform => platform.Population),
        ];
        return populations.Length == 1 ? populations[0] : null;
    }
}
