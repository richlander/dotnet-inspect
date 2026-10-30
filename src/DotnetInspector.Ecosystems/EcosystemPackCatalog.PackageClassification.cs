using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    /// <summary>
    /// Classifies a Package using product associations and platform supply for
    /// the traversal TFM, independently of the coordinate's selection framework.
    /// </summary>
    public static EcosystemPackageResult IsEcosystemPackage(
        PackageCoordinate coordinate,
        string traversalTfm,
        PlatformPruneInventory? inventory)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (PackageCoordinateResolver.Validate(coordinate) is { } invalid)
            throw new ArgumentException(invalid.Message, nameof(coordinate));

        var traversal = new TraversalTargetFrameworkPolicy(traversalTfm);
        EcosystemPackDescriptor? ecosystem = PackageOwner(coordinate.PackageId);
        if (inventory is null)
        {
            return new EcosystemPackageResult.Unavailable(
                EcosystemPackageUnavailableReason.InventoryUnavailable,
                ecosystem);
        }
        if (!string.Equals(
                inventory.TargetFramework,
                traversal.TargetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            return new EcosystemPackageResult.Unavailable(
                EcosystemPackageUnavailableReason.InventoryTargetMismatch,
                ecosystem);
        }

        PlatformSupplyReceipt receipt = PlatformPrunePolicy.Evaluate(
            inventory,
            coordinate with { Framework = traversal.TargetFramework });
        PlatformOwnedInfo? platform = null;
        if (receipt.Supply.Family is { } family)
        {
            PlatformFamily layer = family switch
            {
                _ when family.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase) =>
                    PlatformFamily.DotNetRuntime,
                _ when family.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase) =>
                    PlatformFamily.AspNetCore,
                _ => throw new ArgumentException(
                    $"Unsupported ecosystem platform family '{family}'.",
                    nameof(inventory)),
            };
            platform = new PlatformOwnedInfo(layer, receipt);
            ecosystem ??= RequirePackageOwner(layer == PlatformFamily.DotNetRuntime
                ? EcosystemPackIds.Runtime
                : EcosystemPackIds.AspNetCore);
        }

        return ecosystem is null
            ? new EcosystemPackageResult.NotEcosystem()
            : new EcosystemPackageResult.Known(new EcosystemPackage(ecosystem, platform));
    }

    private static EcosystemPackDescriptor? PackageOwner(string packageId)
    {
        EcosystemDependencyProfileEntry? owner = null;
        bool exact = false;
        int specificity = -1;
        foreach (EcosystemDependencyProfileEntry entry in DependencyRecognitionProfile.Entries)
        {
            foreach (EcosystemDependencyAssociation association in entry.Associations)
            {
                if (!association.Matches(EcosystemDependencyIdentityDomain.PackageId, packageId))
                    continue;

                bool candidateExact = association.Kind == EcosystemDependencyAssociationKind.Exact;
                if (owner is null
                    || candidateExact && !exact
                    || candidateExact == exact && association.Value.Length > specificity
                    || candidateExact == exact && association.Value.Length == specificity
                        && entry.Ecosystem.Order < owner.Ecosystem.Order)
                {
                    owner = entry;
                    exact = candidateExact;
                    specificity = association.Value.Length;
                }
            }
        }

        return owner is null ? null : RequirePackageOwner(owner.Ecosystem.Id);
    }

    private static EcosystemPackDescriptor RequirePackageOwner(EcosystemPackId id) =>
        Lookup(id) is EcosystemPackLookupResult.Known known
            ? known.Descriptor
            : throw new InvalidOperationException($"Package ecosystem '{id}' is not registered.");
}
