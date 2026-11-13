using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Markout;
using DotnetInspector.Presentation;

namespace DotnetInspect.Cli.Output;

internal static class DiscoveryDocumentFactory
{
    private static readonly OutputCapabilityCatalog s_emptyCapabilities =
        new(
            new Dictionary<
                string,
                SectionOutputCapabilities>(
                StringComparer.OrdinalIgnoreCase));

    public static DiscoveryDocument? Create(
        string catalog,
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlySet<string>? exactOnlySections,
        OutputCapabilityCatalog capabilities,
        bool requireExactSelection = false,
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? sectionCardinalities = null,
        IReadOnlyDictionary<string, SectionShape>? sectionShapes = null,
        IReadOnlyDictionary<
            string,
            SectionDescription>? sectionDescriptions = null) =>
        CreateCore(
            catalog,
            discover,
            schema,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            sectionCostAnnotations,
            exactOnlySections,
            capabilities,
            requireExactSelection,
            sectionCardinalities,
            sectionShapes,
            sectionDescriptions,
            includeResourcePaths: false,
            includeQueryItems: true)?.Document;

    public static StructuralDiscoveryProjection? CreateProjection(
        string catalog,
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlySet<string>? exactOnlySections,
        OutputCapabilityCatalog capabilities,
        bool requireExactSelection = false,
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? sectionCardinalities = null,
        IReadOnlyDictionary<string, SectionShape>? sectionShapes = null,
        IReadOnlyDictionary<
            string,
            SectionDescription>? sectionDescriptions = null) =>
        CreateCore(
            catalog,
            discover,
            schema,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            sectionCostAnnotations,
            exactOnlySections,
            capabilities,
            requireExactSelection,
            sectionCardinalities,
            sectionShapes,
            sectionDescriptions,
            includeResourcePaths: true,
            includeQueryItems: true);

    public static DiscoveryDocument? CreateTreeDocument(
        string catalog,
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlySet<string>? exactOnlySections) =>
        CreateCore(
            catalog,
            discover,
            schema,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            sectionCostAnnotations,
            exactOnlySections,
            s_emptyCapabilities,
            requireExactSelection: false,
            sectionCardinalities: null,
            sectionShapes: null,
            sectionDescriptions: null,
            includeResourcePaths: false,
            includeQueryItems: false)?.Document;

    private static StructuralDiscoveryProjection? CreateCore(
        string catalog,
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlySet<string>? exactOnlySections,
        OutputCapabilityCatalog capabilities,
        bool requireExactSelection,
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? sectionCardinalities,
        IReadOnlyDictionary<string, SectionShape>? sectionShapes,
        IReadOnlyDictionary<
            string,
            SectionDescription>? sectionDescriptions,
        bool includeResourcePaths,
        bool includeQueryItems)
    {
        var additionalItems =
            new Dictionary<string, ImmutableArray<StructuralDiscoveryItem>>(
                StringComparer.OrdinalIgnoreCase);
        if (includeQueryItems)
        {
            foreach (string section in schema.SectionNames.Where(name =>
                string.Equals(
                    name,
                    SectionNames.PerformanceTriage,
                    StringComparison.OrdinalIgnoreCase)
                || PerformanceKinds.Sections.Contains(
                    name,
                    StringComparer.OrdinalIgnoreCase)))
            {
                additionalItems.Add(section,
                [
                    .. PerformanceTriageOptions.DiscoveryItems()
                        .Select(static item => new StructuralDiscoveryItem(
                            item.Name,
                            item.Kind,
                            item.PathSegment)),
                ]);
            }
        }
        StructuralDiscoveryProjection projection = StructuralDiscoveryFactory.Create(
            catalog,
            schema,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            sectionCostAnnotations,
            capabilities,
            sectionCardinalities,
            sectionShapes,
            sectionDescriptions,
            includeResourcePaths,
            additionalItems);
        if (discover is null or { Length: 0 })
            return projection;

        ImmutableArray<DiscoveryResource> resources = projection.Document.Resources;
        ImmutableArray<DiscoveryResourceIdentity> catalogEntries =
            projection.Document.CatalogEntries;
        var categories = resources
            .Where(static resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Category)
            .ToDictionary(
                static resource => resource.Identity.Name,
                static resource => resource.Members
                    .Select(static member => member.Name).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        DiscoverySelection? selection = ResolveSelection(
            discover,
            schema,
            categories,
            catalogEntries,
            exactOnlySections,
            requireExactSelection,
            resources);
        if (selection is null)
            return null;

        var document = new DiscoveryDocument(
                catalog,
                resources,
                catalogEntries,
                selection);
        return new StructuralDiscoveryProjection(
            document,
            projection.ResourcePaths);
    }

    private static DiscoverySelection? ResolveSelection(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        IReadOnlyList<DiscoveryResourceIdentity> catalogEntries,
        IReadOnlySet<string>? exactOnlySections,
        bool requireExactSelection,
        IReadOnlyList<DiscoveryResource> resources)
    {
        if (discover is null or { Length: 0 })
        {
            return new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows: catalogEntries);
        }

        var addressed = new List<DiscoveryResourceIdentity>();
        var rows = new List<DiscoveryResourceIdentity>();
        foreach (string selector in discover)
        {
            if (SelectResolver.TryResolveCategory(
                    selector,
                    categories,
                    schema.SectionNames,
                    out string category,
                    out string[] members))
            {
                DiscoveryResourceIdentity identity =
                    CategoryIdentity(category);
                addressed.Add(identity);
                rows.AddRange(
                    resources
                        .First(resource =>
                            resource.Identity == identity)
                        .Members);
                continue;
            }

            if (!requireExactSelection
                && selector.StartsWith("@", StringComparison.Ordinal))
            {
                WriteCategoryNotFound(selector, categories);
                return null;
            }

            if (requireExactSelection)
            {
                var resolution =
                    SelectResolver.ResolveSingleWithProvenance(
                        selector,
                        schema.SectionNames);
                if (resolution.Miss is not null)
                {
                    SelectOutput.WriteUnresolved(
                        new SelectResult(
                            null,
                            [resolution.Miss]));
                    return null;
                }
                if (!resolution.IsExact
                    || resolution.Matches.Count != 1)
                {
                    CommandError.Write(
                        "--details requires an exact category or section "
                        + "selector; glob expansion is not supported.");
                    return null;
                }
            }

            var (matches, miss) = ResolveMatches(
                selector,
                schema.SectionNames,
                exactOnlySections);
            if (miss is null && matches.Count == 1)
            {
                DiscoveryResourceIdentity identity =
                    SectionIdentity(matches[0]);
                addressed.Add(identity);
                rows.AddRange(
                    resources
                        .First(resource =>
                            resource.Identity == identity)
                        .Members);
                continue;
            }

            if (miss is not null && miss.IsGlob && matches.Count > 1)
            {
                CommandError.Write(
                    $"'{selector}' matches {matches.Count} sections: "
                    + $"{string.Join(", ", matches)}.");
                CommandError.WriteLine(
                    "Discovery requires exactly one section. Be more specific.");
                return null;
            }

            if (miss is not null)
            {
                CommandError.Write(
                    $"Section '{miss.Value}' not found.");
                if (miss.Suggestions.Count > 0)
                {
                    CommandError.WriteBlankLine();
                    CommandError.WriteLine("Did you mean:");
                    foreach (string suggestion in miss.Suggestions)
                        CommandError.WriteLine($"  {suggestion}");
                }
            }
            return null;
        }

        return new DiscoverySelection(
            isCatalog: false,
            addressed,
            rows);
    }

    private static (
        List<string> Matches,
        SelectMiss? Miss)
        ResolveMatches(
            string selector,
            IReadOnlyList<string> knownSections,
            IReadOnlySet<string>? exactOnlySections)
    {
        var direct = SelectResolver.ResolveSingleWithProvenance(
            selector,
            knownSections,
            singleGlob: true);
        if (direct.IsExact
            || exactOnlySections is null
            || exactOnlySections.Count == 0
            || (!selector.Contains('*')
                && !selector.Contains('?')))
        {
            return (direct.Matches, direct.Miss);
        }

        string[] wildcardSections =
        [
            .. knownSections.Where(section =>
                !exactOnlySections.Contains(section)),
        ];
        return SelectResolver.ResolveSingle(
            selector,
            wildcardSections,
            singleGlob: true);
    }

    private static void WriteCategoryNotFound(
        string name,
        IReadOnlyDictionary<string, string[]> categories)
    {
        CommandError.Write($"Category '{name}' not found.");
        if (categories.Count == 0)
            return;

        CommandError.WriteBlankLine();
        CommandError.WriteLine("Available categories:");
        foreach (string category in categories.Keys.OrderBy(
                     value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            CommandError.WriteLine($"  {category}");
        }
    }


    private static DiscoveryResourceIdentity CategoryIdentity(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity SectionIdentity(string name) =>
        new(DiscoveryResourceKind.Section, name);
}
