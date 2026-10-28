using System.Collections.Immutable;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>
/// Builds resource-free structural declarations from the owner's generated
/// or composed schema. Hosts retain selection diagnostics and gestures.
/// </summary>
public static class StructuralDiscoveryFactory
{
    public static StructuralDiscoveryProjection Create(
        string catalog,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        OutputCapabilityCatalog capabilities,
        IReadOnlyDictionary<string, SectionCardinalityDeclaration>? sectionCardinalities = null,
        IReadOnlyDictionary<string, SectionShape>? sectionShapes = null,
        IReadOnlyDictionary<string, SectionDescription>? sectionDescriptions = null,
        bool includeResourcePaths = true,
        IReadOnlyDictionary<string, ImmutableArray<StructuralDiscoveryItem>>? additionalItems = null)
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
        IReadOnlyDictionary<string, SectionShape> shapes =
            NormalizeShapes(sectionShapes, schema.SectionNames);
        IReadOnlyDictionary<string, SectionDescription> descriptions =
            NormalizeDescriptions(sectionDescriptions, schema.SectionNames);
        List<DiscoveryResource> resources =
            CreateResources(
                catalog,
                schema,
                categories,
                capabilities,
                cardinalities,
                shapes,
                descriptions,
                includeResourcePaths ? resourcePaths : null,
                additionalItems);
        List<DiscoveryResourceIdentity> catalogEntries =
            CreateCatalogEntries(
                schema,
                categories,
                catalogHiddenSections,
                listedCategoryDoors,
                sectionCostAnnotations);
        return new StructuralDiscoveryProjection(
            new DiscoveryDocument(
                catalog,
                resources,
                catalogEntries,
                new DiscoverySelection(true, [], catalogEntries)),
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
        IReadOnlyDictionary<string, SectionShape> shapes,
        IReadOnlyDictionary<string, SectionDescription> descriptions,
        List<StructuralResourcePathRegistration>? resourcePaths,
        IReadOnlyDictionary<string, ImmutableArray<StructuralDiscoveryItem>>? additionalItems)
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
            List<StructuralDiscoveryItem> items =
                section is null
                    ? []
                    :
                    [
                        .. section.Items.Select(item =>
                            new StructuralDiscoveryItem(
                                item.Name,
                                item.Kind,
                                MachineKeyPathSegment(item.Key))),
                    ];
            if (additionalItems is not null
                && additionalItems.TryGetValue(sectionName, out var extra))
            {
                items.AddRange(extra);
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
            SectionShape? shape =
                shapes.TryGetValue(sectionName, out SectionShape declaredShape)
                    ? declaredShape
                    : null;
            descriptions.TryGetValue(
                sectionName,
                out SectionDescription? description);
            resources.Add(
                new DiscoveryResource(
                    sectionIdentity,
                    members: itemIdentities,
                    outputModes:
                        capabilities.FormatsForSection(sectionName),
                    cardinality: cardinality,
                    shape: shape,
                    description: description));
            if (resourcePaths is not null)
            {
                AddPath(
                    resourcePaths,
                    sectionIdentity,
                    new ResourcePath(
                        $"{catalog}/sections/{sectionSegment}"));
            }

            foreach (StructuralDiscoveryItem item in items)
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

    private static IReadOnlyDictionary<string, SectionShape> NormalizeShapes(
        IReadOnlyDictionary<string, SectionShape>? shapes,
        IReadOnlyList<string> sectionNames)
    {
        var normalized = new Dictionary<string, SectionShape>(
            StringComparer.OrdinalIgnoreCase);
        if (shapes is null)
            return normalized;

        var known = sectionNames.ToDictionary(
            static name => name,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string name, SectionShape shape) in shapes)
        {
            // A shape declared for a section outside this schema (for example a
            // section hidden from the current route) is simply not advertised.
            if (known.TryGetValue(name, out string? canonicalName))
                normalized[canonicalName] = shape;
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, SectionDescription>
        NormalizeDescriptions(
            IReadOnlyDictionary<string, SectionDescription>? descriptions,
            IReadOnlyList<string> sectionNames)
    {
        var normalized = new Dictionary<string, SectionDescription>(
            StringComparer.OrdinalIgnoreCase);
        if (descriptions is null)
            return normalized;

        var known = sectionNames.ToDictionary(
            static name => name,
            StringComparer.OrdinalIgnoreCase);
        foreach ((string name, SectionDescription description) in descriptions)
        {
            // As with shapes, a description for a section outside this schema
            // is simply not advertised.
            if (known.TryGetValue(name, out string? canonicalName))
                normalized[canonicalName] = description;
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
            SectionCategoryNames.Ecosystem => "ecosystem",
            SectionCategoryNames.Libraries => "libraries",
            SectionCategoryNames.Query => "query",
            SectionCategoryNames.Audit => "audit",
            SectionCategoryNames.Dependencies => "dependencies",
            SectionCategoryNames.Calls => "calls",
            SectionCategoryNames.Relations => "relations",
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

}
