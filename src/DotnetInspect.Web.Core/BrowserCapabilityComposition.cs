using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

/// <summary>One facade's resource-free registered capability contribution.</summary>
internal sealed record BrowserCapabilityContribution(
    InspectionCapabilityCatalog Catalog,
    ImmutableArray<InspectionCapabilityResourcePathRegistration> Paths);

/// <summary>Composes the providers selected by host configuration.</summary>
internal static class BrowserCapabilityComposition
{
    private static ImmutableArray<Func<BrowserCapabilityContribution>> _providers;

    internal static void Configure(
        ImmutableArray<Func<BrowserCapabilityContribution>> providers) =>
        _providers = providers;

    internal static (InspectionCapabilityCatalog Catalog, ResourceExplanationCatalog Explanation)
        CreateSearchComposition()
    {
        if (_providers.IsDefault)
            throw new InvalidOperationException("Browser capability composition is not configured.");
        BrowserCapabilityContribution[] contributions =
            [.. _providers.Select(static provider => provider())];
        InspectionCapabilityCatalog catalog = InspectionCapabilityCatalog.Create(
            contributions.SelectMany(static contribution => contribution.Catalog.Modules));
        return (catalog, ResourceExplanationCatalog.CreateCapabilities(catalog,
            contributions.SelectMany(static contribution => contribution.Paths)));
    }

    internal static InspectionCapabilityCatalog CreatePackageQueryCatalog() =>
        InspectionCapabilityCatalog.Create(
            [PackageQueryCapability.ProductModule, PackageQueryCapabilityBinding.Module]);

    internal static BrowserCapabilityContribution CreatePackageQueryContribution()
    {
        InspectionCapabilityCatalog catalog = CreatePackageQueryCatalog();
        return new(catalog, PackageQueryCapabilityResourcePaths.Create(catalog));
    }
}
