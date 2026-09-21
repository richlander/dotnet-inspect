using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal static class LibraryOutputCapabilities
{
    public static OutputCapabilityCatalog Catalog { get; } =
        CreateCatalog();

    private static OutputCapabilityCatalog CreateCatalog()
    {
        Dictionary<string, SectionOutputCapabilities> sections =
            LibrarySections.SectionCatalog.SelectableSectionNames
                .ToDictionary(
                    section => section,
                    _ => SectionOutputCapabilities.Create(
                        OutputCapabilityCatalog.StandardSectionFormats),
                    StringComparer.OrdinalIgnoreCase);

        sections[SectionNames.MemberMetrics] =
            SectionOutputCapabilities.Create(
                OutputCapabilityCatalog.StandardSectionFormats
                    .Where(format =>
                        format != DiscoveryOutputMode.Json));
        sections[SectionNames.ReferenceHierarchy] =
            SectionOutputCapabilities.Create(
                [
                    .. OutputCapabilityCatalog.StandardSectionFormats,
                    DiscoveryOutputMode.Tree,
                    DiscoveryOutputMode.Mermaid,
                ],
                [DiscoveryOutputMode.Json]);

        return new OutputCapabilityCatalog(
            sections,
            [PerformanceKinds.Sections]);
    }
}
