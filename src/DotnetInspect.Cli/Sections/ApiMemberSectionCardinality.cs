using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Member-catalog section cardinality under
/// <c>docs/design/section-cardinality.md</c>, declared for the single-type,
/// overload, and exact-member catalogs together because a name means the same
/// section in each. Field-set records (<c>Type Info</c>, <c>Summary</c>,
/// <c>Signature</c>) and Text payloads whose owner declares no inventory are
/// scalar. <c>Source</c> is the one Text with a declared inventory: its rows
/// are the ordered <c>Lines</c> owned by
/// <c>docs/design/source-document-cardinality.md</c>. Every listing is an
/// inventory with Rows and Count.
/// </summary>
internal static class ApiMemberSectionCardinality
{
    private static readonly string[] ScalarSections =
    [
        SectionNames.TypeInfo,
        SectionNames.Summary,
        SectionNames.Signature,
        SectionNames.ApiDeclarations,
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
