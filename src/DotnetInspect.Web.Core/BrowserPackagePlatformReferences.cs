using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;

namespace DotnetInspect.Web;

internal sealed record BrowserPackagePlatformReferenceProjection(
    ImmutableArray<BrowserPackagePlatformReferenceProjection.Row> References,
    string? Error)
{
    internal sealed record Row(string Family, string Label);
}

[SupportedOSPlatform("browser")]
internal static class BrowserPackagePlatformReferences
{
    public static BrowserPackagePlatformReferenceProjection Project(
        BrowserPackageCoordinate coordinate,
        bool dependencyGroupIsEmpty)
    {
        ArgumentNullException.ThrowIfNull(coordinate);

        PackageHouseFrameworkReferenceOutcome? outcome =
            coordinate.FrameworkReferences;
        if (outcome is PackageHouseFrameworkReferenceOutcome.Selected selected)
        {
            ImmutableArray<BrowserPackagePlatformReferenceProjection.Row>
                references =
                [.. selected.Evidence.References
                    .Select(Project)
                    .Where(reference => reference is not null)
                    .Select(reference => reference!)
                    .DistinctBy(reference => reference.Family)];
            if (references.IsEmpty
                && selected.Evidence.References.IsEmpty
                && dependencyGroupIsEmpty)
            {
                references = DefaultRuntimeReference(coordinate.Framework);
            }

            return new(references, Error: null);
        }

        if (outcome
            is PackageHouseFrameworkReferenceOutcome
                .NoFrameworkReferenceGroups)
        {
            return new(
                dependencyGroupIsEmpty
                    ? DefaultRuntimeReference(coordinate.Framework)
                    : [],
                Error: null);
        }

        return new(
            [],
            outcome switch
            {
                null =>
                    "Platform reference evidence is unavailable.",
                PackageHouseFrameworkReferenceOutcome.NotRequested =>
                    "Platform reference evidence was not requested.",
                PackageHouseFrameworkReferenceOutcome.NoMatchingTargetFramework
                    noMatch =>
                    $"No platform reference group matches '{noMatch.TargetFramework}'.",
                PackageHouseFrameworkReferenceOutcome.TargetUnavailable =>
                    "The platform reference target framework is unavailable.",
                PackageHouseFrameworkReferenceOutcome.ManifestUnavailable =>
                    "The package manifest is unavailable for platform references.",
                PackageHouseFrameworkReferenceOutcome.HouseFailed =>
                    "Package acquisition failed before platform references could be projected.",
                PackageHouseFrameworkReferenceOutcome.Failed =>
                    "The package manifest's platform references are invalid.",
                _ =>
                    "Platform reference evidence is unavailable.",
            });
    }

    static BrowserPackagePlatformReferenceProjection.Row? Project(
        PackageFrameworkReferenceIdentity reference) =>
        reference.Name switch
        {
            var name when name.Equals(
                "Microsoft.NETCore.App",
                StringComparison.OrdinalIgnoreCase) =>
                Runtime,
            var name when name.Equals(
                "Microsoft.AspNetCore.App",
                StringComparison.OrdinalIgnoreCase) =>
                AspNetCore,
            _ => null,
        };

    static ImmutableArray<BrowserPackagePlatformReferenceProjection.Row>
        DefaultRuntimeReference(string targetFramework)
    {
        if (!PlatformTargetFramework.TryParse(
                targetFramework,
                out PlatformTargetFramework? target)
            || target.Form
                is not (PlatformTargetFrameworkForm.Net
                    or PlatformTargetFrameworkForm.NetCoreApp))
        {
            return [];
        }

        return [Runtime];
    }

    static BrowserPackagePlatformReferenceProjection.Row Runtime { get; } =
        new(BrowserPlatformWorkspace.RuntimeFamily, ".NET Runtime");

    static BrowserPackagePlatformReferenceProjection.Row AspNetCore { get; } =
        new(BrowserPlatformWorkspace.AspNetCoreFamily, "ASP.NET Core");
}
