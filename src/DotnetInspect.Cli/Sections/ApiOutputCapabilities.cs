using System.Collections.Immutable;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Type and member output capabilities derived from each section's declared
/// <see cref="SectionShape"/>, per <c>docs/design/section-shapes.md</c>. One
/// catalog serves the type listing and the three member catalogs because a
/// section name declares the same shape wherever it appears. <c>Call Graph</c>
/// is a Graph, not a shape, so it keeps its own edge-table, tree, and Mermaid
/// formats beside the shape-derived entries.
/// <para>
/// A Text reaches the row formats only through its fact row, and the type and
/// member owners have no fact-row lowering yet (a row format on a lone source
/// or decompiler payload returns nothing or is rejected), so an API Text
/// advertises only the formats the CLI executes today: Markdown, plain text,
/// and JSON. The adoption that lands the fact row extends them.
/// </para>
/// </summary>
internal static class ApiOutputCapabilities
{
    /// <summary>The formats an API Text payload executes today (no fact row yet).</summary>
    internal static ImmutableArray<DiscoveryOutputMode> TextFormats { get; } =
    [
        DiscoveryOutputMode.Markdown,
        DiscoveryOutputMode.PlainText,
        DiscoveryOutputMode.Json,
    ];

    public static OutputCapabilityCatalog Catalog { get; } = Create();

    private static OutputCapabilityCatalog Create()
    {
        IEnumerable<KeyValuePair<string, SectionShape>> shapes =
        [
            .. ApiTypeSectionDescriptors.CreatePipeline().SectionShapes,
            .. ApiMemberSectionDescriptors.CreatePipeline().SectionShapes,
            .. ApiMemberOverloadSectionDescriptors.CreatePipeline().SectionShapes,
            .. ApiMemberDetailSectionDescriptors.CreatePipeline().SectionShapes,
        ];
        var sections = new Dictionary<string, SectionOutputCapabilities>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string section, SectionShape shape) in shapes)
        {
            sections[section] = SectionOutputCapabilities.Create(
                shape == SectionShape.Text
                    ? TextFormats
                    : OutputCapabilityCatalog.FormatsForShape(shape));
        }

        sections[SectionNames.CallGraph] = SectionOutputCapabilities.Create(
            [
                .. OutputCapabilityCatalog.StandardSectionFormats,
                DiscoveryOutputMode.Tree,
                DiscoveryOutputMode.Mermaid,
            ]);
        return new OutputCapabilityCatalog(sections);
    }
}
