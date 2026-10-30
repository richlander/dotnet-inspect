using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// The formats each Diff section renders. Diff admits no plain-text output,
/// so its Tables carry the standard formats without it, and Annotated Source
/// Diff emits only its JSON document. No two Diff sections share one row
/// family, so a selection of several sections composes a Document.
/// </summary>
internal static class DiffOutputCapabilities
{
    public static OutputCapabilityCatalog Catalog { get; } = CreateCatalog();

    private static OutputCapabilityCatalog CreateCatalog()
    {
        SectionOutputCapabilities table = SectionOutputCapabilities.Create(
            OutputCapabilityCatalog.StandardSectionFormats
                .Where(static format =>
                    format != DiscoveryOutputMode.PlainText));
        Dictionary<string, SectionOutputCapabilities> sections =
            DiffSections.SectionCatalog.SelectableSectionNames.ToDictionary(
                static section => section,
                _ => table,
                StringComparer.OrdinalIgnoreCase);
        sections[DiffSections.AnnotatedSourceDiff.Name] =
            SectionOutputCapabilities.Create([DiscoveryOutputMode.Json]);
        return new OutputCapabilityCatalog(sections);
    }
}
