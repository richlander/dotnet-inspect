using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Package output capabilities derived from each section's declared
/// <see cref="DotnetInspector.Sections.SectionShape"/>, per
/// <c>docs/design/section-shapes.md</c>. The package file family is the one
/// homogeneous row family: its Text members lower to the same Path/Size fact
/// row as its Table members, so <c>-S @Files --tsv</c> streams one listing.
/// </summary>
internal static class PackageOutputCapabilities
{
    public static OutputCapabilityCatalog Catalog { get; } =
        OutputCapabilityCatalog.FromShapes(
            PackageSectionDescriptors.CreateCatalog().Sections.SectionShapes,
            [PackageFileFamily.SectionNames]);
}
