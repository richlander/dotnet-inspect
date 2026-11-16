using DotnetInspector.Diff;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>
/// The Browser consumers of Finding Diff Any's Library and Type routes: the
/// <c>queryLibraryFindingDiffAny</c> and <c>queryTypeFindingDiffAny</c> Worker
/// operations. The Worker runs each comparison in its own bounded steps and
/// projects the completed result through the route's document.
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

    internal const string TypeBindingIdentity = "dotnet-inspect.web/finding-diff-any-members";

    /// <summary>
    /// The Browser consumer of the Type route: the <c>queryTypeFindingDiffAny</c>
    /// Worker operation behind the Type view's Member cues.
    /// </summary>
    internal static InspectionConsumerBinding<FindingDiffAnyTypeRequest,
        FindingDiffAnyTypeDocument> TypeBinding { get; } = new(
            new(TypeBindingIdentity, InspectionConsumerKind.Browser,
                "dotnet-inspect Browser", "queryTypeFindingDiffAny"),
            FindingDiffAnyTypeCapability.Route,
            exposedQueryTerms: [],
            inputRules:
            [
                "Select one exact package compile asset, a target version, and one runtime identifier; both axes compare implementation assemblies.",
                "Address one Type by its exact escaped definition identity.",
                "The Worker compares in bounded steps and yields between them.",
            ]);

    internal static InspectionCapabilityModule Module { get; } = new(
        "dotnet-inspect.web/finding-diff-any-capability", bindings: [Binding, TypeBinding]);

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
