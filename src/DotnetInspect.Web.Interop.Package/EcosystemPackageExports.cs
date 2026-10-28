using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    // Resource-free annotation helper, like ClassifyPackageGraphIdentities.
    // The caller supplies catalog evidence; this path never acquires a pack.
    [JSExport]
    public static string ClassifyEcosystemPackages(
        string traversalTfm,
        string candidatesJson,
        string inventoryJson)
    {
        var candidates = JsonSerializer.Deserialize(
            candidatesJson, BrowserPackageJsonContext.Default.BrowserEcosystemPackageCandidateArray)
            ?? throw new ArgumentException("Package candidates are absent.", nameof(candidatesJson));
        var input = JsonSerializer.Deserialize(
            inventoryJson, BrowserPackageJsonContext.Default.BrowserEcosystemPackageInventory);
        PlatformPruneInventory[] families = PlatformSupplyInventories(input);
        PlatformPruneInventory? inventory = families.Length > 0
            ? PlatformPruneInventory.Compose(families)
            : null;
        BrowserEcosystemPackageClassification[] results = [.. candidates.Select(candidate =>
        {
            var coordinate = new PackageCoordinate(candidate.Id, candidate.Version);
            if (candidate.Version is not null
                && PackageCoordinateResolver.Validate(coordinate) is not null)
            {
                // Spotlight projects the row while an explicit version is still
                // being typed. Ownership remains answerable without version evidence.
                coordinate = coordinate with { Version = null };
            }
            var result = EcosystemPackCatalog.IsEcosystemPackage(
                coordinate, traversalTfm, inventory);
            var ecosystem = result switch
            {
                EcosystemPackageResult.Known known => known.Package.Ecosystem,
                EcosystemPackageResult.Unavailable unavailable => unavailable.Ecosystem,
                _ => null,
            };
            var platform = (result as EcosystemPackageResult.Known)?.Package.PlatformOwnedInfo;
            return new BrowserEcosystemPackageClassification(
                candidate.Id, candidate.Version, ecosystem?.Id.Value, ecosystem?.Title,
                platform?.LayerName, platform?.IsPruned,
                ecosystem is not null && coordinate.Version is not null && input is not null
                    && string.Equals(input.Tfm, traversalTfm, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(input.Version)
                    ? PackageVersionPrecedence.Compare(coordinate.Version, input.Version)
                    : null);
        })];
        return JsonSerializer.Serialize(
            results, BrowserPackageJsonContext.Default.BrowserEcosystemPackageClassificationArray);
    }

    private static PlatformPruneInventory[] PlatformSupplyInventories(
        BrowserEcosystemPackageInventory? input)
    {
        if (input?.Supplies is not { } supplies)
            return [];
        if (supplies.Any(supply =>
            !supply.Family.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)
            && !supply.Family.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Unsupported platform supply family.", nameof(input));
        }
        return
        [
            .. new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" }
                .Where(family => supplies.Any(supply =>
                    string.Equals(supply.Family, family, StringComparison.OrdinalIgnoreCase)))
                .Select(family => PlatformPruneInventory.FromExactFamily(
                    family, input.Tfm, input.Version,
                    supplies.Where(supply =>
                        string.Equals(supply.Family, family, StringComparison.OrdinalIgnoreCase))
                        .Select(supply => $"{supply.Package}|{supply.Version}"))),
        ];
    }
}
