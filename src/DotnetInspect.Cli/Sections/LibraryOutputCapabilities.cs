using DotnetInspect.Cli.Output;

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

        sections[SectionNames.ImplementationProfiles] =
            SectionOutputCapabilities.Create(
                OutputCapabilityCatalog.StandardSectionFormats
                    .Where(format => format != OutputMode.Json));
        sections[SectionNames.ReferenceHierarchy] =
            SectionOutputCapabilities.Create(
                [
                    .. OutputCapabilityCatalog.StandardSectionFormats,
                    OutputMode.Tree,
                    OutputMode.Mermaid,
                ],
                [OutputMode.Json]);

        return new OutputCapabilityCatalog(
            sections,
            [PerformanceKinds.Sections]);
    }
}
