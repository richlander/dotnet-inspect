using DotnetInspector.ResearchSections;

namespace DotnetInspect.Web.Interop.Metadata;

// No static fields: taking this method group during host configuration does
// not initialize Compare registration or its production execution state.
internal static class BrowserCompareCapabilityContribution
{
    internal static BrowserCapabilityContribution Create()
    {
        var catalog = BrowserCompareCapabilityBinding.Catalog;
        return new(catalog, DiffAnalysisCapabilityResourcePaths.Create(catalog));
    }
}
