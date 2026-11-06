namespace DotnetInspector.Sections;

public enum InspectionCatalogIdentity
{
    Package,
    Library,
    LibraryAggregate,
    ApiType,
    ApiMember,
    ApiMemberOverload,
    ApiMemberDetail,
    Diff,
}

/// <summary>Canonical structural catalog path segments.</summary>
public static class StructuralCatalogPaths
{
    /// <summary>
    /// The resource-path catalog segment for a structural catalog, as in
    /// <c>library/sections/...</c> or <c>package/sections/...</c>. The three
    /// member catalogs keep the names the contextual member explanation
    /// already publishes, so <c>member-detail/sections/source</c> names the
    /// exact-member Source and never collides with the single-type catalog.
    /// The Diff catalog is the comparison sections <c>diff -S</c> selects.
    /// </summary>
    public static string Name(InspectionCatalogIdentity catalog) =>
        catalog switch
        {
            InspectionCatalogIdentity.Package => "package",
            InspectionCatalogIdentity.Library
                or InspectionCatalogIdentity.LibraryAggregate => "library",
            InspectionCatalogIdentity.ApiType => "type",
            InspectionCatalogIdentity.ApiMember => "member",
            InspectionCatalogIdentity.ApiMemberOverload => "member-overload",
            InspectionCatalogIdentity.ApiMemberDetail => "member-detail",
            InspectionCatalogIdentity.Diff => "diff",
            _ => throw new ArgumentOutOfRangeException(
                nameof(catalog),
                catalog,
                "Unknown structural catalog."),
        };
}
