using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Member-catalog section cardinality under
/// <c>docs/design/section-cardinality.md</c>, declared for the single-type,
/// overload, and exact-member catalogs together because a name means the same
/// section in each. Field-set records (<c>Type Info</c>, <c>Summary</c>) and
/// Text payloads whose owner declares no inventory are scalar.
/// <c>Signature</c> is not a field set: it is a one-row Table whose
/// row unit is the resolved member, so Count observes that row (1) and it stays
/// an inventory. That includes <c>Source</c> for now: the ordered <c>Lines</c>
/// inventory that <c>docs/design/source-document-cardinality.md</c> owns is
/// not yet executed by the CLI (Count and the row formats do not observe
/// lines), and a declaration must not advertise terminals ahead of the
/// behavior. The adoption that lands <c>Lines</c> flips <c>Source</c> to an
/// inventory. Every listing is an inventory with Rows and Count.
/// </summary>
internal static class ApiMemberSectionCardinality
{
    private static readonly string[] ScalarSections =
    [
        SectionNames.TypeInfo,
        SectionNames.Summary,
        SectionNames.ApiDeclarations,
        SectionNames.Source,
        SectionNames.DecompiledSource,
        SectionNames.AnnotatedSource,
        SectionNames.AnnotatedSourceDocument,
        SectionNames.PdbSource,
        SectionNames.SourceDiff,
        SectionNames.IL,
        SectionNames.CostOverlay,
        SectionNames.SemanticsOverlay,
        SectionNames.FindingCensus,
    ];

    public static IReadOnlyDictionary<string, SectionCardinalityDeclaration>
        Declarations { get; } = CreateDeclarations();

    /// <summary>
    /// The declarations for one member catalog's sections, since a structural
    /// schema accepts declarations only for the sections it carries.
    /// </summary>
    public static IReadOnlyDictionary<string, SectionCardinalityDeclaration> For(
        IEnumerable<string> sections) =>
        sections
            .Where(Declarations.ContainsKey)
            .ToDictionary(
                static section => section,
                section => Declarations[section],
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, SectionCardinalityDeclaration>
        CreateDeclarations()
    {
        var scalar = new HashSet<string>(
            ScalarSections,
            StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> names =
        [
            .. ApiMemberSectionDescriptors.CreatePipeline().AllSectionNames,
            .. ApiMemberOverloadSectionDescriptors.CreatePipeline().AllSectionNames,
            .. ApiMemberDetailSectionDescriptors.CreatePipeline().AllSectionNames,
        ];
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static section => section,
                section => scalar.Contains(section)
                    ? SectionCardinalityDeclaration.Scalar
                    : SectionCardinalityDeclaration.Inventory,
                StringComparer.OrdinalIgnoreCase);
    }
}
