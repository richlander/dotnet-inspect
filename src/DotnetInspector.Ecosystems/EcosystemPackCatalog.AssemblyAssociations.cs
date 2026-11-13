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
}
