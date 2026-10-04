using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Package output capabilities derived from each section's declared
/// <see cref="DotnetInspector.Sections.SectionShape"/>, per
/// <c>docs/design/section-shapes.md</c>. No homogeneous row family is declared
/// yet: the design makes the package file family one Path/Size listing, but the
/// command does not render a multi-section row stream until the presentation
/// adoption lands, and discovery must not advertise a format the command
/// rejects.
/// </summary>
internal static class PackageOutputCapabilities
{
    public static OutputCapabilityCatalog Catalog { get; } =
        OutputCapabilityCatalog.FromShapes(
            PackageSectionDescriptors.CreateCatalog().Sections.SectionShapes);
}
