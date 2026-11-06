using DotnetInspector.Diff;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>
/// The Browser consumer of Finding Diff Any's Library route: the
/// <c>queryLibraryFindingDiffAny</c> Worker operation. The Worker runs the
/// comparison in its own bounded steps and projects the completed result
/// through the route's document.
/// </summary>
internal static class BrowserFindingDiffAnyCapabilityBinding
{
    internal const string BindingIdentity = "dotnet-inspect.web/finding-diff-any";

    internal static InspectionConsumerBinding<FindingDiffAnyLibraryRequest,
        FindingDiffAnyLibraryDocument> Binding { get; } = new(
            new(BindingIdentity, InspectionConsumerKind.Browser,
                "dotnet-inspect Browser", "queryLibraryFindingDiffAny"),
            FindingDiffAnyLibraryCapability.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Select one exact package compile asset and a target version.",
                "Body comparison (ApiAndBody) requires one exact runtime identifier and compares implementation assemblies; Api compares the compile assets.",
                "The Worker compares in bounded steps and yields between them.",
            ]);

    internal static InspectionCapabilityModule Module { get; } = new(
        "dotnet-inspect.web/finding-diff-any-capability", bindings: [Binding]);

    internal static InspectionCapabilityCatalog Catalog { get; } =
        InspectionCapabilityCatalog.Create([FindingDiffAnyLibraryCapability.ProductModule, Module]);
}

// No static fields: taking this method group during host configuration does
// not initialize Finding Diff Any registration.
internal static class BrowserFindingDiffAnyCapabilityContribution
{
    internal static BrowserCapabilityContribution Create()
    {
        var catalog = BrowserFindingDiffAnyCapabilityBinding.Catalog;
        return new(catalog, FindingDiffAnyLibraryCapabilityResourcePaths.Create(catalog));
    }
}
