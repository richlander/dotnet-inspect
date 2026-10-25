using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

/// <summary>A bounded reading selection over one validated, installed catalog.</summary>
public sealed partial class ResourceExplanationDataset
{
    private readonly ResourceExplanationCatalog _catalog;
    private readonly ResourceExplanationResource _root;
    private readonly ImmutableArray<ResourceExplanationResource> _resources;
    private readonly string _kind;
    private readonly bool _narrowed;
    private readonly ResourceExplanationDocument? _description;

    private ResourceExplanationDataset(ResourceExplanationCatalog catalog,
        ResourceExplanationResource root, ImmutableArray<ResourceExplanationResource> resources,
        string kind, bool narrowed = false, ResourceExplanationDocument? description = null)
    {
        _catalog = catalog;
        _root = root;
        _resources = resources;
        _kind = kind;
        _narrowed = narrowed;
        _description = description;
    }

    public ImmutableArray<ResourceExplanationResource> Resources => _resources;

    /// <summary>Selects resource data and required context. Optional facet keys or
    /// native value identities narrow their owning root before assembling records.</summary>
    public static ResourceExplanationDataset Create(ResourceExplanationCatalog catalog,
        ResourcePathResolution.Resolved resolution, IEnumerable<string>? facetKeys = null,
        IEnumerable<string>? valueIds = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resolution);
        var byKey = catalog.Resources.ToDictionary(resource => resource.Key);
        ResourceExplanationResource root = byKey[resolution.Key];
        if (IsDescriptionReading(root))
        {
            if (facetKeys is not null || valueIds is not null)
                throw new InvalidOperationException("Description readings do not admit facet or value selection.");
            int depth = root.ResourceType == ResourceExplanationVocabulary.ExplanationRootType ? 2
                : root.ResourceType == ResourceExplanationVocabulary.RoutingCollectionType ? 1 : 0;
            ResourceExplanationDocument description = catalog.Explain(resolution,
                ResourceExplanationRequest.ForHost(depth)).Content;
            return new(catalog, root, description.Resources, "description", description: description);
        }
        var selected = new HashSet<ExplanationResourceKey>();
        var queue = new Queue<ExplanationResourceKey>();
        int targetCount = 0;
        queue.Enqueue(root.Key);
        bool vocabulary = root.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType;
        bool facets = root.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
            || root.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType;
        if (!vocabulary && !facets)
            throw new InvalidOperationException("Selected data requires a vocabulary, query space, or query facet resource.");
        if (vocabulary)
        {
            if (facetKeys is not null)
                throw new InvalidOperationException("Facet-key narrowing requires a query-space root.");
            return CreateVocabularyReading(catalog, root, byKey, valueIds);
        }
        if (valueIds is not null)
            throw new InvalidOperationException("Value-identity narrowing requires a vocabulary root.");
        HashSet<ExplanationResourceKey>? requested = null;
        if (facetKeys is not null)
        {
            if (root.Key.ResourceType != ResourceExplanationVocabulary.QuerySpaceType)
                throw new InvalidOperationException("Facet-key narrowing requires a query-space root.");
            var members = catalog.Relationships.Where(edge => edge.Source == root.Key
                && edge.Relationship.Value == "query-facet").SelectMany(edge =>
                {
                    RequireComplete(edge);
                    return edge.Targets;
                }).ToDictionary(target => FacetKey(byKey[target.Resource]),
                    target => target.Resource, StringComparer.Ordinal);
            requested = [];
            foreach (string facetKey in facetKeys)
            {
                if (!members.TryGetValue(facetKey, out ExplanationResourceKey? member))
                    throw new InvalidOperationException("Unknown query facet key: " + facetKey);
                requested.Add(member);
            }
            if (requested.Count == 0)
                throw new InvalidOperationException("Facet-key narrowing requires at least one key.");
        }
        while (queue.TryDequeue(out ExplanationResourceKey? key))
        {
            if (!selected.Add(key))
                continue;
            if (selected.Count > ResourceExplanationRequest.HostResourceLimit)
                throw new InvalidOperationException("Selected data exceeds the host resource limit; no partial dataset was emitted.");
            ResourceExplanationResource resource = byKey[key];
            string[] relations = resource.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
                    ? ["query-facet"]
                    : resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType
                        ? ["required-context", "related-operation", "exposed-by"] : [];
            foreach (string relation in relations)
            {
                foreach (ResourceExplanationRelationship edge in catalog.Relationships.Where(edge =>
                    edge.Source == key && edge.Relationship.Value == relation))
                {
                    RequireComplete(edge);
                    foreach (ResourceExplanationRelationshipTarget target in edge.Targets)
                    {
                        if (key == root.Key && relation == "query-facet"
                            && requested is not null && !requested.Contains(target.Resource))
                            continue;
                        if (!byKey.ContainsKey(target.Resource))
                            throw new InvalidOperationException("A selected relationship target has no local resource snapshot.");
                        if (++targetCount > 4096)
                            throw new InvalidOperationException("Selected data exceeds the host target limit; no partial dataset was emitted.");
                        queue.Enqueue(target.Resource);
                    }
                }
            }
        }
        return new(catalog, root,
            [root, .. catalog.Resources.Where(resource => selected.Contains(resource.Key) && resource.Key != root.Key)],
            "query-facets", requested is not null);
    }

    public JsonElement ToJson(Func<ResourcePath, string> bindAddress, bool hal = false,
        Func<ResourcePath, string>? bindHalAddress = null)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        if (_kind == "description")
        {
            ResourceExplanationDocument document = _description!;
            // Discovery addresses identify resources, independently of representation.
            return hal ? ResourceExplanationDataProjection.CreateHal(document, bindAddress)
                : ResourceExplanationDataProjection.Create(document, bindAddress);
        }
        return _kind == "query-facets"
            ? WriteQueryPreparation(hal, bindHalAddress ?? bindAddress)
            : WriteVocabularyReading(hal, bindHalAddress ?? bindAddress);
    }

    private static bool IsDescriptionReading(ResourceExplanationResource resource) =>
        resource.ResourceType == ResourceExplanationVocabulary.ExplanationRootType
        || resource.ResourceType == ResourceExplanationVocabulary.RoutingCollectionType
        || resource.ResourceType == ResourceExplanationVocabulary.RoutingEntryType
        || resource.ResourceType == ResourceExplanationVocabulary.ComponentContractType
        || resource.Path?.Value == ResourceExplanationCatalog.ComponentContractsCollectionSegment;

    /// <summary>
    /// Returns registered query controls and reading facts for presentation consumers.
    /// Ordinary ToJson reads use the compact query-preparation profile instead.
    /// </summary>
    public JsonElement ToQueryPresentationJson()
    {
        if (_kind != "query-facets")
            throw new InvalidOperationException("Query presentation requires a query-facet selection.");
        return WriteQueryPreparation(false, static path => path.Value, includePresentation: true);
    }

    private IEnumerable<ExplanationResourceKey> Targets(ExplanationResourceKey source, string relation)
    {
        foreach (ResourceExplanationRelationship edge in _catalog.Relationships.Where(edge =>
            edge.Source == source && edge.Relationship.Value == relation))
        {
            RequireComplete(edge);
            foreach (ResourceExplanationRelationshipTarget target in edge.Targets)
                yield return target.Resource;
        }
    }

    private static string Text(JsonNode node, string name) => node[name]?.GetValue<string>()
        ?? throw new InvalidOperationException("A selected declaration or identity fact is unavailable: " + name);

    private static void RequireComplete(ResourceExplanationRelationship relationship)
    {
        if (relationship.State != ExplanationObservationState.Available
            || relationship.TargetCompleteness != ResourceExplanationTargetProjectionCompleteness.Complete)
            throw new InvalidOperationException("Selected relationship observations must be available and complete.");
    }
}
