using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Package section cardinality under
/// <c>docs/design/section-cardinality.md</c>: field-set records and single
/// Text payloads are scalar; every listing is an inventory with Rows and Count.
/// Declared for discovery; terminal enforcement follows in the presentation
/// adoption.
/// </summary>
internal static class PackageSectionCardinality
{
    private static readonly string[] ScalarSections =
    [
        PackageSections.Summary,
        PackageSections.PackageInfo,
        PackageSections.Statistics,
        PackageSections.Signature,
        PackageSections.SourceLinkAvailability,
        PackageSections.SourceLinkIntegrity,
        PackageSections.FilesNuspec,
        PackageSections.FilesReadme,
    ];

    public static IReadOnlyDictionary<string, SectionCardinalityDeclaration>
        Declarations { get; } = CreateDeclarations();

    private static Dictionary<string, SectionCardinalityDeclaration>
        CreateDeclarations()
    {
        var scalar = new HashSet<string>(
            ScalarSections,
            StringComparer.OrdinalIgnoreCase);
        return PackageSectionDescriptors.CreateCatalog()
            .Sections
            .AllSectionNames
            .ToDictionary(
                static section => section,
                section => scalar.Contains(section)
                    ? SectionCardinalityDeclaration.Scalar
                    : SectionCardinalityDeclaration.Inventory,
                StringComparer.OrdinalIgnoreCase);
    }
}
