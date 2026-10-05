using System.Collections.Concurrent;
using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Type and member output capabilities, per <c>docs/design/section-shapes.md</c>,
/// restricted to the formats the CLI executes for each section today. The declared
/// shape fixes the family: a Table lowers to the row formats and a Text to its
/// payload. The JSON column is derived from the JSON output code's own section
/// tables rather than from the shape, because the type document carries only
/// the sections <see cref="ApiCommand.DocumentProjectedSections"/> projects, plus
/// the dedicated lowerings in <see cref="ApiCommand.DedicatedJsonSections"/>;
/// every other section would leave <c>--json</c> as the identity object, and
/// Metrics, Performance Triage, and Body Shapes are rejected outright.
/// <para>
/// A Text reaches the row formats only through its fact row, which the type and
/// member owners do not lower yet, so a Text advertises Markdown and plain text
/// (plus JSON where a dedicated lowering exists). <c>Call Graph</c> is a Graph,
/// not a shape, and keeps its edge table, tree, and Mermaid formats. The
/// executed set depends on the route, not only the catalog: the <c>type</c>
/// command lowers the type API declarations and whole-type Source to JSON,
/// while the <c>member</c> command's type view does not.
/// </para>
/// </summary>
internal static class ApiOutputCapabilities
{
    /// <summary>The formats an API Text payload executes today (no fact row yet).</summary>
    internal static ImmutableArray<DiscoveryOutputMode> TextFormats { get; } =
    [
        DiscoveryOutputMode.Markdown,
        DiscoveryOutputMode.PlainText,
    ];

    /// <summary>The formats an API Table executes today; JSON is added per section.</summary>
    internal static ImmutableArray<DiscoveryOutputMode> TableFormats { get; } =
    [
        DiscoveryOutputMode.Markdown,
        DiscoveryOutputMode.PlainText,
        DiscoveryOutputMode.Table,
        DiscoveryOutputMode.Tsv,
        DiscoveryOutputMode.Jsonl,
    ];

    private static readonly ConcurrentDictionary<
        (StructuralViewIdentity View, InspectionCatalogIdentity Catalog),
        OutputCapabilityCatalog> Catalogs = new();

    /// <summary>The executed-format catalog for one structural route.</summary>
    public static OutputCapabilityCatalog For(StructuralRoute route) =>
        Catalogs.GetOrAdd(
            (route.View.Identity, route.Catalog),
            static key => Create(key.View, key.Catalog));

    private static OutputCapabilityCatalog Create(
        StructuralViewIdentity view,
        InspectionCatalogIdentity catalog)
    {
        IReadOnlyDictionary<string, SectionShape> shapes = catalog switch
        {
            InspectionCatalogIdentity.ApiType =>
                ApiTypeSectionDescriptors.CreatePipeline().SectionShapes,
            InspectionCatalogIdentity.ApiMember
                or InspectionCatalogIdentity.ApiMemberOverload
                or InspectionCatalogIdentity.ApiMemberDetail =>
                ApiInspectionCatalogRegistry.CreateMemberPipeline(catalog).SectionShapes,
            _ => throw new ArgumentOutOfRangeException(
                nameof(catalog),
                catalog,
                "Not an API catalog."),
        };
        var sections = new Dictionary<string, SectionOutputCapabilities>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string section, SectionShape shape) in shapes)
        {
            ImmutableArray<DiscoveryOutputMode> formats =
                shape == SectionShape.Text ? TextFormats : TableFormats;
            if (ExecutesDocumentJson(view, catalog, section))
                formats = [.. formats, DiscoveryOutputMode.Json];
            sections[section] = SectionOutputCapabilities.Create(formats);
        }

        if (catalog != InspectionCatalogIdentity.ApiType)
        {
            sections[SectionNames.CallGraph] = SectionOutputCapabilities.Create(
                [
                    .. OutputCapabilityCatalog.StandardSectionFormats,
                    DiscoveryOutputMode.Tree,
                    DiscoveryOutputMode.Mermaid,
                ]);
        }

        return new OutputCapabilityCatalog(sections);
    }

    /// <summary>
    /// Whether <c>-S &lt;section&gt; --json</c> on this route carries the section: the type
    /// listing serializes its whole document; a single type or member carries the
    /// projected and dedicated sections, and the type-command-only ones on the
    /// <c>type</c> command's own view.
    /// </summary>
    internal static bool ExecutesDocumentJson(
        StructuralViewIdentity view,
        InspectionCatalogIdentity catalog,
        string section)
    {
        if (catalog == InspectionCatalogIdentity.ApiType)
            return true;
        if (ApiCommand.TypeCommandJsonSections.Contains(section)
            && catalog == InspectionCatalogIdentity.ApiMember)
        {
            return view == StructuralViewIdentity.Type;
        }

        return ApiCommand.DocumentProjectedSections.Contains(section)
            || ApiCommand.DedicatedJsonSections.Contains(section);
    }
}
