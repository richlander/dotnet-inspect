using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DiscoveryDocumentFactory
{
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
        bool requireExactSelection = false)
    {
        IReadOnlyDictionary<string, string[]> categories =
            FilterCategories(sectionCategories, schema.SectionNames);
        List<DiscoveryResource> resources =
            CreateResources(schema, categories, capabilities);
        List<DiscoveryResourceIdentity> catalogEntries =
            CreateCatalogEntries(
                schema,
                categories,
                catalogHiddenSections,
                listedCategoryDoors,
                sectionCostAnnotations);
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

        return new DiscoveryDocument(
            catalog,
            resources,
            catalogEntries,
            selection);
    }

    private static List<DiscoveryResource> CreateResources(
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        OutputCapabilityCatalog capabilities)
    {
        var resources = new List<DiscoveryResource>();
        foreach ((string name, string[] members) in categories
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            DiscoveryResourceIdentity[] memberIdentities =
            [
                .. members
                    .OrderBy(
                        member => member,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(SectionIdentity),
            ];
            resources.Add(
                new DiscoveryResource(
                    CategoryIdentity(name),
                    members: memberIdentities,
                    outputModes: capabilities.FormatsForSelection(members)));
        }

        foreach (string sectionName in schema.SectionNames)
        {
            SectionSchema? section = schema.GetSection(sectionName);
            List<(string Name, string Kind)> items =
                section is null
                    ? []
                    :
                    [
                        .. section.Items.Select(item =>
                            (Name: item.Name, Kind: item.Kind)),
                    ];
            if (string.Equals(
                    sectionName,
                    SectionNames.PerformanceTriage,
                    StringComparison.OrdinalIgnoreCase)
                || PerformanceKinds.Sections.Contains(
                    sectionName,
                    StringComparer.OrdinalIgnoreCase))
            {
                items.AddRange(
                    PerformanceTriageOptions.DiscoveryItems());
            }

            DiscoveryResourceIdentity[] itemIdentities =
            [
                .. items.Select(item =>
                    ItemIdentity(
                        sectionName,
                        item.Name,
                        item.Kind)),
            ];
            resources.Add(
                new DiscoveryResource(
                    SectionIdentity(sectionName),
                    members: itemIdentities,
                    outputModes:
                        capabilities.FormatsForSection(sectionName)));

            resources.AddRange(
                items.Select(item =>
                    new DiscoveryResource(
                        ItemIdentity(
                            sectionName,
                            item.Name,
                            item.Kind))));
        }

        return resources;
    }

    private static List<DiscoveryResourceIdentity> CreateCatalogEntries(
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations)
    {
        if (listedCategoryDoors is not null)
        {
            IEnumerable<DiscoveryResourceIdentity> doors = categories.Keys
                .Where(listedCategoryDoors.Contains)
                .OrderBy(
                    name => name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(CategoryIdentity);
            IEnumerable<DiscoveryResourceIdentity> sections =
                schema.SectionNames
                    .Where(name =>
                        catalogHiddenSections is null
                        || !catalogHiddenSections.Contains(name))
                    .OrderBy(
                        name => name,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(SectionIdentity);
            return [.. doors, .. sections];
        }

        IEnumerable<DiscoveryResourceIdentity> categoryEntries =
            categories.Keys
                .Where(name =>
                    catalogHiddenSections is null
                    || !string.Equals(
                        name,
                        SectionCategoryNames.Hidden,
                        StringComparison.OrdinalIgnoreCase))
                .Select(CategoryIdentity);
        IEnumerable<DiscoveryResourceIdentity> sectionEntries =
            schema.SectionNames
                .Where(name =>
                    catalogHiddenSections is null
                    || !catalogHiddenSections.Contains(name))
                .Select(SectionIdentity);

        return
        [
            .. categoryEntries
                .Concat(sectionEntries)
                .OrderBy(identity =>
                    GetCatalogSortRank(
                        identity,
                        sectionCostAnnotations))
                .ThenBy(
                    identity => identity.Name,
                    StringComparer.OrdinalIgnoreCase),
        ];
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

    private static IReadOnlyDictionary<string, string[]> FilterCategories(
        IReadOnlyDictionary<string, string[]>? categories,
        IReadOnlyList<string> knownSections)
    {
        var filtered =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);
        if (categories is null)
            return filtered;

        var known =
            knownSections.ToDictionary(
                section => section,
                StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string[] sections) in categories)
        {
            string[] existing =
            [
                .. sections
                    .Where(known.ContainsKey)
                    .Select(section => known[section])
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
            if (existing.Length > 0)
                filtered[name] = existing;
        }

        return filtered;
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

    private static int GetCatalogSortRank(
        DiscoveryResourceIdentity identity,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations)
    {
        if (identity.Kind == DiscoveryResourceKind.Category)
            return 0;
        if (sectionCostAnnotations is not null
            && sectionCostAnnotations.TryGetValue(
                identity.Name,
                out string? annotation)
            && annotation.Contains(
                SectionAnnotations.OptIn,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static DiscoveryResourceIdentity CategoryIdentity(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity SectionIdentity(string name) =>
        new(DiscoveryResourceKind.Section, name);

    private static DiscoveryResourceIdentity ItemIdentity(
        string section,
        string name,
        string itemKind) =>
        new(
            DiscoveryResourceKind.Item,
            name,
            section,
            itemKind);

}
