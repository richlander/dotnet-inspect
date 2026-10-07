using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Library section cardinality under
/// <c>docs/design/section-cardinality.md</c>. On an exact Library route, the
/// field-set records that describe one subject are scalar: single-Library
/// <c>Library Info</c>, the SourceLink availability and integrity records,
/// <c>Symbols</c>, and <c>Metadata: Image</c>. The coordinate-scoped
/// <c>Context:</c> sections are not field sets. Those that locate one thing
/// (source location, member, instruction, callsite, return address) are,
/// like a member <c>Signature</c>, one-row Tables whose row unit is the
/// located coordinate, so Count observes that row (1); the exception,
/// allocation, safety, and cost contexts list one row per region or fact at
/// the coordinate. <c>Metadata: Heap</c> rows are heap entries. Every listing, including the Graph
/// <c>Dependency Structure</c>, is an inventory with Rows and Count. The
/// all-libraries survey declares its own library-row inventory.
/// </summary>
internal static class LibrarySectionCardinality
{
    private static readonly string[] ScalarSections =
    [
        SectionNames.LibraryInfo,
        SectionNames.SourceLinkAvailability,
        SectionNames.SourceLinkIntegrity,
        SectionNames.Symbols,
        MetadataSectionNames.Image,
    ];

    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> ExactDeclarations
    { get; } = CreateExactDeclarations();

    private static Dictionary<string, SectionCardinalityDeclaration>
        CreateExactDeclarations()
    {
        var scalar = new HashSet<string>(
            ScalarSections,
            StringComparer.OrdinalIgnoreCase);
        return LibrarySections.CreateCatalog()
            .Sections
            .AllSectionNames
            .ToDictionary(
                static section => section,
                section => scalar.Contains(section)
                    ? SectionCardinalityDeclaration.Scalar
                    : SectionCardinalityDeclaration.Inventory,
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> AggregateDeclarations
    { get; } =
        new Dictionary<string, SectionCardinalityDeclaration>(
            StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.LibraryInfo] =
                SectionCardinalityDeclaration.Inventory,
        };

    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration>? ExactDeclarationsFor(
        LibraryOptions options) =>
        IsAllTfmPackageSelection(options)
            ? null
            : ExactDeclarations;

    public static bool IsAllTfmPackageSelection(
        LibraryOptions options) =>
        string.IsNullOrEmpty(options.PlatformAssembly)
        && !string.IsNullOrEmpty(options.PackagePath)
        && string.Equals(
            options.Tfm,
            "all",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Admits Count or Rows on an exact Library route. A scalar section has
    /// no rows, so a lone scalar, a selection of scalars only, or an implicit
    /// selection that includes one (the default overview always includes
    /// <c>Library Info</c>) fails before acquisition. An explicit selection
    /// that mixes scalars with inventories observes the inventories: the
    /// scalars leave the selection, so a field count is never reported as a
    /// row count.
    /// </summary>
    public static ExactTerminalAdmission AdmitExactTerminals(
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
            return default;

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
            return default;
        if (resolution.Sections is not null)
            selected = resolution.Sections;

        HashSet<string> candidates =
            catalog.Pipeline.GetCandidateSections(
                verbosity,
                selected,
                fixedOverview);
        // Candidates are unordered; read them in catalog order so the
        // diagnostic names a stable section.
        string[] scalars = [.. catalog.Sections.AllSectionNames
            .Where(section =>
                candidates.Contains(section, StringComparer.OrdinalIgnoreCase)
                && IsScalar(section))];
        if (scalars.Length == 0)
            return default;

        if (selected is { Count: > 0 }
            && selected.Any(static section => !IsScalar(section)))
        {
            // Prune the caller's normalized selection when it has one: a
            // category already dropped the coordinate sections it cannot
            // address, and re-resolving the selector would bring them back.
            HashSet<string> inventories = (includeSections ?? selected)
                .Where(static section => !IsScalar(section))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (inventories.Count > 0)
                return new ExactTerminalAdmission(null, inventories);
        }

        string terminal = count ? "--count" : "--rows";
        return new ExactTerminalAdmission(
            $"Section '{scalars[0]}' is scalar and does "
                + $"not support {terminal}. Select an inventory section.",
            null);
    }

    private static bool IsScalar(string section) =>
        ExactDeclarations.TryGetValue(
            section,
            out SectionCardinalityDeclaration? declaration)
        && declaration.Kind == SectionCardinalityKind.Scalar;
}

/// <summary>
/// The outcome of admitting Count or Rows on an exact Library route: a
/// visible failure, or, for an explicit selection that mixes scalars with
/// inventories, the inventory sections that the terminal observes.
/// </summary>
internal readonly record struct ExactTerminalAdmission(
    string? Error,
    HashSet<string>? InventorySections);
