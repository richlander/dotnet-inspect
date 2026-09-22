using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DiscoveryDocumentFactory
{
    public sealed record Projection(
        DiscoveryDocument Document,
        ImmutableArray<StructuralResourcePathRegistration> ResourcePaths);

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
            SectionCardinalityDeclaration>? sectionCardinalities = null) =>
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
            includeResourcePaths: false)?.Document;

    public static Projection? CreateProjection(
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
            SectionCardinalityDeclaration>? sectionCardinalities = null) =>
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
            includeResourcePaths: true);

    private static Projection? CreateCore(
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
        bool includeResourcePaths)
    {
        IReadOnlyDictionary<string, string[]> categories =
            FilterCategories(sectionCategories, schema.SectionNames);
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration> cardinalities =
            NormalizeCardinalities(
                sectionCardinalities,
                schema.SectionNames);
        var resourcePaths =
            new List<StructuralResourcePathRegistration>();
        List<DiscoveryResource> resources =
            CreateResources(
                catalog,
                schema,
                categories,
                capabilities,
                cardinalities,
                includeResourcePaths ? resourcePaths : null);
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

        var document = new DiscoveryDocument(
                catalog,
                resources,
                catalogEntries,
                selection);
        return new Projection(
            document,
            [.. resourcePaths]);
    }

    private static List<DiscoveryResource> CreateResources(
        string catalog,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        OutputCapabilityCatalog capabilities,
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration> cardinalities,
        List<StructuralResourcePathRegistration>? resourcePaths)
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
            DiscoveryResourceIdentity categoryIdentity =
                CategoryIdentity(name);
            resources.Add(
                new DiscoveryResource(
                    categoryIdentity,
                    members: memberIdentities,
                    outputModes: capabilities.FormatsForSelection(members)));
            if (resourcePaths is not null)
            {
                AddPath(
                    resourcePaths,
                    categoryIdentity,
                    new ResourcePath(
                        $"{catalog}/categories/"
                        + CategoryPathSegment(name)));
            }
        }

        foreach (string sectionName in schema.SectionNames)
        {
            SectionSchema? section = schema.GetSection(sectionName);
            string sectionSegment =
                section is null
                    ? throw new InvalidOperationException(
                        $"Section '{sectionName}' has no schema.")
                    : MachineKeyPathSegment(section.Key);
            List<DiscoveryItem> items =
                section is null
                    ? []
                    :
                    [
                        .. section.Items.Select(item =>
                            new DiscoveryItem(
                                item.Name,
                                item.Kind,
                                MachineKeyPathSegment(item.Key))),
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
                    PerformanceTriageOptions.DiscoveryItems()
                        .Select(static item =>
                            new DiscoveryItem(
                                item.Name,
                                item.Kind,
                                item.PathSegment)));
            }

            DiscoveryResourceIdentity[] itemIdentities =
            [
                .. items.Select(item =>
                    ItemIdentity(
                        sectionName,
                        item.Name,
                        item.Kind)),
            ];
            DiscoveryResourceIdentity sectionIdentity =
                SectionIdentity(sectionName);
            cardinalities.TryGetValue(
                sectionName,
                out SectionCardinalityDeclaration? cardinality);
            resources.Add(
                new DiscoveryResource(
                    sectionIdentity,
                    members: itemIdentities,
                    outputModes:
                        capabilities.FormatsForSection(sectionName),
                    cardinality: cardinality));
            if (resourcePaths is not null)
            {
                AddPath(
                    resourcePaths,
                    sectionIdentity,
                    new ResourcePath(
                        $"{catalog}/sections/{sectionSegment}"));
            }

            foreach (DiscoveryItem item in items)
            {
                DiscoveryResourceIdentity itemIdentity =
                    ItemIdentity(
                        sectionName,
                        item.Name,
                        item.Kind);
                resources.Add(new DiscoveryResource(itemIdentity));
                if (resourcePaths is not null)
                {
                    AddPath(
                        resourcePaths,
                        itemIdentity,
                        new ResourcePath(
                            $"{catalog}/sections/{sectionSegment}/items/"
                            + $"{item.Kind}/{item.PathSegment}"));
                }
            }
        }

        return resources;
    }

    private static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> NormalizeCardinalities(
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? cardinalities,
        IReadOnlyList<string> sectionNames)
    {
        var normalized =
            new Dictionary<
                string,
                SectionCardinalityDeclaration>(
                StringComparer.OrdinalIgnoreCase);
        if (cardinalities is null)
            return normalized;

        var known = sectionNames.ToDictionary(
            static name => name,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string name, SectionCardinalityDeclaration declaration)
                 in cardinalities)
        {
            if (!known.TryGetValue(name, out string? canonicalName))
            {
                throw new ArgumentException(
                    $"Section cardinality declaration '{name}' does not "
                        + "name a section in the structural schema.",
                    nameof(cardinalities));
            }
            ArgumentNullException.ThrowIfNull(declaration);
            if (!normalized.TryAdd(canonicalName, declaration))
            {
                throw new ArgumentException(
                    $"Section cardinality declaration '{name}' duplicates "
                        + $"section '{canonicalName}'.",
                    nameof(cardinalities));
            }
        }

        return normalized;
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

    private static void AddPath(
        ICollection<StructuralResourcePathRegistration>? registrations,
        DiscoveryResourceIdentity identity,
        ResourcePath path) =>
        registrations?.Add(
            new StructuralResourcePathRegistration(identity, path));

    private static string MachineKeyPathSegment(string key)
    {
        string segment = key.Replace('_', '-');
        if (!ResourcePath.IsCanonicalSegment(segment))
        {
            throw new InvalidOperationException(
                $"Machine key '{key}' cannot be represented as a "
                + "resource-path segment.");
        }
        return segment;
    }

    private static string CategoryPathSegment(string category) =>
        category switch
        {
            SectionCategoryNames.Library => "library",
            SectionCategoryNames.Package => "package",
            SectionCategoryNames.Member => "member",
            SectionCategoryNames.Diff => "diff",
            SectionCategoryNames.Project => "project",
            SectionCategoryNames.Vocabulary => "vocabulary",
            SectionCategoryNames.Ecosystem => "ecosystem",
            SectionCategoryNames.Libraries => "libraries",
            SectionCategoryNames.Query => "query",
            SectionCategoryNames.Api => "api",
            SectionCategoryNames.Audit => "audit",
            SectionCategoryNames.Dependencies => "dependencies",
            SectionCategoryNames.Calls => "calls",
            SectionCategoryNames.Source => "source",
            SectionCategoryNames.SourceLink => "source-link",
            SectionCategoryNames.Surface => "surface",
            SectionCategoryNames.Context => "context",
            SectionCategoryNames.Integrations => "integrations",
            SectionCategoryNames.Files => "files",
            SectionCategoryNames.Hidden => "hidden",
            SectionCategoryNames.Performance => "performance",
            SectionCategoryNames.Decompiler => "decompiler",
            SectionCategoryNames.Metadata => "metadata",
            SectionCategoryNames.ReadyToRun => "ready-to-run",
            _ => throw new InvalidOperationException(
                $"Category '{category}' has no resource-path registration."),
        };

    private sealed record DiscoveryItem(
        string Name,
        string Kind,
        string PathSegment);

}
