using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal static class LibrarySectionCardinality
{
    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> ExactDeclarations { get; } =
        new Dictionary<string, SectionCardinalityDeclaration>(
            StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.LibraryInfo] =
                LibraryOverviewInspectionOperation.Cardinality,
        };

    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> AggregateDeclarations { get; } =
        new Dictionary<string, SectionCardinalityDeclaration>(
            StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.LibraryInfo] =
                SectionCardinalityDeclaration.Inventory,
        };

    public static string? ValidateExactTerminals(
        string[]? select,
        bool selectDefault,
        IReadOnlySet<string>? includeSections,
        bool fixedOverview,
        Verbosity verbosity,
        bool count,
        bool rows,
        bool discovery)
    {
        if (discovery || (!count && !rows))
            return null;

        if (selectDefault)
        {
            selectDefault = false;
            if (select is null && verbosity == Verbosity.Minimal)
            {
                verbosity = Verbosity.Normal;
                fixedOverview = true;
            }
        }

        LibrarySectionCatalog catalog = LibrarySections.CreateCatalog();
        HashSet<string>? selected =
            includeSections is null
                ? null
                : new HashSet<string>(
                    includeSections,
                    StringComparer.OrdinalIgnoreCase);
        SelectResult resolution = SelectResolver.ResolveSelectAsSections(
            select,
            catalog.Sections.SelectableSectionNames,
            catalog.Sections.InfoSectionNames,
            catalog.Sections.SelectionCategoryMap,
            selectDefault);
        if (resolution.HasError)
            return null;
        if (resolution.Sections is not null)
            selected = resolution.Sections;

        HashSet<string> candidates =
            catalog.Pipeline.GetCandidateSections(
                verbosity,
                selected,
                fixedOverview);
        if (!candidates.Contains(
                SectionNames.LibraryInfo,
                StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string terminal = count ? "--count" : "--rows";
        return $"Section '{SectionNames.LibraryInfo}' is scalar and does "
            + $"not support {terminal}. Select an inventory section.";
    }
}
