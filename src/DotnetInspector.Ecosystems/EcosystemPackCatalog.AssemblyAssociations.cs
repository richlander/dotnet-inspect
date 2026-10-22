namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    /// <summary>
    /// Returns whether an assembly simple name matches an authored association
    /// in any selected ecosystem lineage.
    /// </summary>
    public static bool HasAssemblyNameAssociation(
        string assemblyName,
        IEnumerable<EcosystemPackId> ecosystems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(ecosystems);

        HashSet<EcosystemPackId> selected =
        [
            .. ProductEcosystemPacks.Registry.ExpandLineages(ecosystems),
        ];
        foreach (EcosystemDependencyProfileEntry entry in
                 DependencyRecognitionProfile.Entries)
        {
            if (!selected.Contains(entry.Ecosystem.Id))
                continue;

            if (entry.Associations.Any(association =>
                    association.Matches(
                        EcosystemDependencyIdentityDomain.AssemblyName,
                        assemblyName)))
            {
                return true;
            }
        }

        return false;
    }
}
