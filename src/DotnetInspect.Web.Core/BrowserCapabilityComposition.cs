using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

/// <summary>The resources selected by installed Browser production bindings.</summary>
internal static class BrowserCapabilityComposition
{
    internal static InspectionCapabilityCatalog CreateCatalog() =>
        InspectionCapabilityCatalog.Create(
            [PackageQueryCapability.ProductModule, PackageQueryCapabilityBinding.Module,
                DiffAnalysisCatalog.ProductModule, BrowserCompareCapabilityBinding.Module]);

    internal static InspectionCapabilityCatalog CreatePackageQueryCatalog() =>
        InspectionCapabilityCatalog.Create(
            [PackageQueryCapability.ProductModule, PackageQueryCapabilityBinding.Module]);

    internal static InspectionCapabilityCatalog CreateCompareCatalog() =>
        InspectionCapabilityCatalog.Create(
            [DiffAnalysisCatalog.ProductModule, BrowserCompareCapabilityBinding.Module]);

    internal static ResourceExplanationCatalog CreateExplanation(
        InspectionCapabilityCatalog catalog) =>
        ResourceExplanationCatalog.CreateCapabilities(catalog,
            [.. PackageQueryCapabilityResourcePaths.Create(CreatePackageQueryCatalog()),
                .. DiffAnalysisCapabilityResourcePaths.Create(CreateCompareCatalog())]);
}
