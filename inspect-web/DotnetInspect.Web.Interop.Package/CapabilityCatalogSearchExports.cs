using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
internal static class BrowserCapabilityCatalogSearch
{
    internal static InspectionEnvelope<CapabilityCatalogSearchDocument> Search(
        string text,
        int maximumResults)
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCapabilityBinding.Module,
                ]);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                PackageQueryCapabilityResourcePaths.Create(catalog));
        return CapabilityCatalogSearch.Search(
            catalog,
            explanation,
            new(text, maximumResults));
    }
}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static string SearchCapabilities(
        string text,
        int maximumResults)
    {
        InspectionEnvelope<CapabilityCatalogSearchDocument> envelope =
            BrowserCapabilityCatalogSearch.Search(text, maximumResults);
        return JsonSerializer.Serialize(
            envelope,
            BrowserPackageJsonContext.Default
                .InspectionEnvelopeCapabilityCatalogSearchDocument);
    }
}
