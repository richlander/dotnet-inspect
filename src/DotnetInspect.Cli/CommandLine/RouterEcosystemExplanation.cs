using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>Presentation of the existing bare-target route; never selects a route.</summary>
internal static class RouterEcosystemExplanation
{
    public static void Write(string target, string command, string? platformFramework)
    {
        if (platformFramework is not null)
        {
            EcosystemPackId? ecosystemId = platformFramework switch
            {
                "runtime" => EcosystemPackIds.Runtime,
                "aspnetcore" => EcosystemPackIds.AspNetCore,
                _ => null,
            };
            if (ecosystemId is { } id
                && EcosystemPackCatalog.Lookup(id) is EcosystemPackLookupResult.Known known)
                CommandError.WriteNote($"Routing to platform library '{target}' · {known.Descriptor.Title}.");
            return;
        }
        if (command != "package"
            || CommandLineHelpers.TryClassifyAsFilePath(target, out _, out _))
            return;
        var (packageId, version) = PackageExtractor.ParsePackageReference(target);
        var coordinate = new PackageCoordinate(packageId, version);
        if (PackageCoordinateResolver.Validate(coordinate) is not null) return;

        string traversalTfm = TraversalTargetFrameworkPolicy.ProductDefault.TargetFramework;
        PlatformPruneInventory? inventory = null;
        if (PlatformResolver.TryGetFrameworkSpecsForTargetFramework(traversalTfm, out var specs)
            && specs.Count > 0)
            inventory = InstalledPlatformPruneSource.Read(specs[0]).Inventory;
        string? note = FormatPackage(target,
            EcosystemPackCatalog.IsEcosystemPackage(coordinate, traversalTfm, inventory),
            traversalTfm);
        if (note is not null) CommandError.WriteNote(note);
    }

    internal static string? FormatPackage(
        string target, EcosystemPackageResult classification, string traversalTfm)
    {
        EcosystemPackDescriptor? ecosystem = classification switch
        {
            EcosystemPackageResult.Known known => known.Package.Ecosystem,
            EcosystemPackageResult.Unavailable unavailable => unavailable.Ecosystem,
            _ => null,
        };
        if (ecosystem is null) return null;
        string pruning = classification switch
        {
            EcosystemPackageResult.Known { Package.PlatformOwnedInfo.IsPruned: true } =>
                $"; pruned for traversal target {traversalTfm}",
            EcosystemPackageResult.Known { Package.PlatformOwnedInfo.IsPruned: false } =>
                $"; not pruned for traversal target {traversalTfm}",
            EcosystemPackageResult.Known { Package.PlatformOwnedInfo: not null } =>
                $"; pruning undetermined for traversal target {traversalTfm}",
            EcosystemPackageResult.Unavailable when ecosystem.Id == EcosystemPackIds.Runtime
                || ecosystem.Id == EcosystemPackIds.AspNetCore =>
                $"; pruning unavailable for traversal target {traversalTfm}",
            _ => "",
        };
        return $"Routing to NuGet package '{target}' · {ecosystem.Title}{pruning}.";
    }
}
