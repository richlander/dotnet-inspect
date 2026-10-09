using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

/// <summary>A bounded reading selection over one validated, installed catalog.</summary>
public sealed class ResourceExplanationDataset
{
    private readonly ResourceExplanationCatalog _catalog;
    private readonly ResourceExplanationResource _root;
    private readonly ImmutableArray<ResourceExplanationResource> _resources;
    private readonly string _kind;

    private ResourceExplanationDataset(ResourceExplanationCatalog catalog,
        ResourceExplanationResource root, ImmutableArray<ResourceExplanationResource> resources,
        string kind)
    {
        _catalog = catalog;
        _root = root;
        _resources = resources;
        _kind = kind;
    }

    public ImmutableArray<ResourceExplanationResource> Resources => _resources;

    public static ResourceExplanationDataset Create(ResourceExplanationCatalog catalog,
        ResourcePathResolution.Resolved resolution)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resolution);
        var byKey = catalog.Resources.ToDictionary(resource => resource.Key);
        ResourceExplanationResource root = byKey[resolution.Key];
        var selected = new HashSet<ExplanationResourceKey>();
        var queue = new Queue<ExplanationResourceKey>();
        int targetCount = 0;
        queue.Enqueue(root.Key);
        bool vocabulary = root.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType;
        bool facets = root.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
            || root.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType;
        if (!vocabulary && !facets)
            throw new InvalidOperationException("Selected data requires a vocabulary, query space, or query facet resource.");
        while (queue.TryDequeue(out ExplanationResourceKey? key))
        {
            if (!selected.Add(key))
                continue;
            if (selected.Count > ResourceExplanationRequest.HostResourceLimit)
                throw new InvalidOperationException("Selected data exceeds the host resource limit; no partial dataset was emitted.");
            ResourceExplanationResource resource = byKey[key];
            string[] relations = vocabulary
                ? resource.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType
                    ? ["vocabulary-value", "term-map-target"] : []
                : resource.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
                    ? ["query-facet"]
                    : resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType
                        ? ["required-context", "exposed-by"] : [];
            foreach (string relation in relations)
            {
                foreach (ResourceExplanationRelationship edge in catalog.Relationships.Where(edge =>
                    edge.Source == key && edge.Relationship.Value == relation))
                {
                    RequireComplete(edge);
                    foreach (ResourceExplanationRelationshipTarget target in edge.Targets)
                    {
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
            vocabulary ? "vocabulary-data" : "query-facets");
    }

    public JsonElement ToJson(Func<ResourcePath, string> bindAddress, bool hal = false)
    {
        ArgumentNullException.ThrowIfNull(bindAddress);
        ImmutableArray<JsonElement> lowered = ResourceExplanationDataProjection.CreateResources(
            _catalog, _resources, bindAddress);
        var nodes = lowered.Select(value => JsonNode.Parse(value.GetRawText())!.AsObject()).ToArray();
        var byKey = _resources.Select((resource, i) => (resource.Key, Node: nodes[i]))
            .ToDictionary(pair => pair.Key, pair => pair.Node);
        var result = new JsonObject
        {
            ["format_version"] = 1,
            ["selection"] = new JsonObject { ["kind"] = _kind, ["completeness"] = "Complete" },
            ["identity"] = nodes[0]["identity"]!.DeepClone(),
            ["path"] = _root.Path?.Value,
        };
        if (_kind == "vocabulary-data")
            WriteVocabularies(result, byKey);
        else
            WriteFacets(result, byKey);
        if (hal)
        {
            // Every selected record has the same data in both representations.
            // Navigation is added beside it, rather than copying it into _embedded.
            AddResourceLinks(result, byKey, bindAddress);
        }
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
        return document.RootElement.Clone();
    }

    public static string RelationUri(ExplanationRelationshipIdentity relation) =>
        "urn:dotnet-inspect:relation:"
        + Uri.EscapeDataString(relation.ResourceType.Schema.Owner.Value) + ":"
        + Uri.EscapeDataString(relation.ResourceType.Schema.Value) + ":"
        + Uri.EscapeDataString(relation.ResourceType.Value) + ":"
        + Uri.EscapeDataString(relation.Value);

    private void WriteVocabularies(JsonObject result,
        Dictionary<ExplanationResourceKey, JsonObject> nodes)
    {
        var vocabularies = new JsonObject();
        foreach (ResourceExplanationResource vocabulary in _resources.Where(r =>
            r.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType))
        {
            JsonObject node = nodes[vocabulary.Key];
            JsonObject facts = node["facts"]!.AsObject();
            string id = Text(facts, "identity");
            JsonArray declarations = facts["maps"]?.AsArray()
                ?? throw new InvalidOperationException("Selected vocabulary map declarations are unavailable.");
            ResourceExplanationResource[] values = Targets(vocabulary.Key, "vocabulary-value")
                .Select(key => _resources.Single(resource => resource.Key == key)).ToArray();
            var records = new JsonArray();
            var sets = new JsonObject();
            var groups = new JsonObject();
            foreach (JsonNode? declaration in declarations)
            {
                if (Text(declaration!, "coverage") != "complete")
                    throw new InvalidOperationException("Sparse data requires complete property coverage.");
                string map = Text(declaration!, "identity");
                if (Text(declaration!, "value-kind") == "boolean")
                    sets[map] = new JsonArray();
                else if (Text(declaration!, "value-kind") == "text"
                    && Text(declaration!, "cardinality") == "optional-one")
                    groups[map] = new JsonObject();
            }
            foreach (ResourceExplanationResource value in values)
            {
                JsonObject valueNode = nodes[value.Key];
                JsonObject valueFacts = valueNode["facts"]!.AsObject();
                string valueId = Text(valueFacts, "identity");
                JsonObject record = Metadata(valueNode);
                record["id"] = valueId;
                foreach ((string name, JsonNode? fact) in valueFacts)
                    if (name is not ("identity" or "map-entries"))
                        record[name] = fact?.DeepClone();
                JsonArray entries = valueFacts["map-entries"]?.AsArray()
                    ?? throw new InvalidOperationException("Selected vocabulary property observations are unavailable.");
                foreach (JsonNode? declaration in declarations)
                {
                    string map = Text(declaration!, "identity");
                    string cardinality = Text(declaration!, "cardinality");
                    JsonNode[] observed = entries.Where(entry => Text(entry!, "map") == map)
                        .Select(entry => entry!["value"]!["value"]?.DeepClone()
                            ?? throw new InvalidOperationException("A selected property value is unavailable.")).ToArray();
                    if (cardinality == "exactly-one" && observed.Length != 1
                        || cardinality == "optional-one" && observed.Length > 1
                        || cardinality == "one-or-more" && observed.Length == 0)
                        throw new InvalidOperationException("Selected property observations disagree with their cardinality.");
                    if (sets[map] is JsonArray set)
                    {
                        if (cardinality is not ("exactly-one" or "optional-one") || observed.Length != 1)
                            throw new InvalidOperationException("Sparse boolean properties require one available observation per value.");
                        if (observed[0].GetValue<bool>())
                            set.Add((JsonNode?)JsonValue.Create(valueId));
                    }
                    else if (groups[map] is JsonObject grouped)
                    {
                        if (observed.Length == 1)
                        {
                            string group = observed[0].GetValue<string>();
                            if (grouped[group] is not JsonArray members)
                                grouped[group] = members = new JsonArray();
                            members.Add((JsonNode?)JsonValue.Create(valueId));
                        }
                    }
                    else
                    {
                        if (record.ContainsKey(map))
                            throw new InvalidOperationException("A vocabulary property collides with resource metadata.");
                        record[map] = cardinality is "zero-or-more" or "one-or-more"
                            ? new JsonArray(observed) : observed.FirstOrDefault();
                    }
                }
                records.Add((JsonNode)record);
            }
            var entry = Metadata(node);
            entry["facts"] = facts.DeepClone();
            entry["values"] = records;
            entry["property_sets"] = sets;
            entry["property_groups"] = groups;
            vocabularies.Add(id, entry);
        }
        result["vocabularies"] = vocabularies;
    }

    private void WriteFacets(JsonObject result,
        Dictionary<ExplanationResourceKey, JsonObject> nodes)
    {
        var facets = new JsonObject();
        var keys = new Dictionary<ExplanationResourceKey, string>();
        var order = new JsonArray();
        foreach (ResourceExplanationResource resource in _resources.Where(r =>
            r.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType))
        {
            JsonObject node = nodes[resource.Key];
            string key = Text(node["facts"]!, "key");
            keys.Add(resource.Key, key);
            var record = Metadata(node);
            record["facts"] = node["facts"]!.DeepClone();
            if (facets.ContainsKey(key))
                throw new InvalidOperationException("Selected facets have colliding local keys.");
            facets.Add(key, record);
            order.Add((JsonNode?)JsonValue.Create(key));
        }
        foreach ((ExplanationResourceKey identity, string key) in keys)
            facets[key]!["requires"] = new JsonArray(Targets(identity, "required-context")
                .Select(target => (JsonNode?)JsonValue.Create(keys[target])).ToArray());
        var bindings = new JsonObject();
        foreach (ResourceExplanationResource resource in _resources.Where(r =>
            r.Key.ResourceType == ResourceExplanationVocabulary.ConsumerBindingType))
        {
            JsonObject node = nodes[resource.Key];
            var record = Metadata(node);
            record["facts"] = node["facts"]!.DeepClone();
            record["exposed_facets"] = new JsonArray(Targets(resource.Key, "exposes")
                .Where(keys.ContainsKey).Select(key => (JsonNode?)JsonValue.Create(keys[key])).ToArray());
            bindings.Add(Text(node["facts"]!, "identity"), record);
        }
        result["facts"] = nodes[_root.Key]["facts"]!.DeepClone();
        result["fact_states"] = nodes[_root.Key]["fact_states"]!.DeepClone();
        result["facets"] = facets;
        result["facet_order"] = order;
        result["bindings"] = bindings;
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

    private void AddResourceLinks(JsonObject result,
        Dictionary<ExplanationResourceKey, JsonObject> nodes, Func<ResourcePath, string> bindAddress)
    {
        foreach (JsonObject node in Descendants(result))
        {
            if (node["identity"] is not JsonObject identity)
                continue;
            ResourceExplanationResource? resource = _resources.FirstOrDefault(r =>
                JsonNode.DeepEquals(nodes[r.Key]["identity"], identity));
            if (resource is null)
                continue;
            var links = new JsonObject();
            if (resource.Path is not null)
                links["self"] = new JsonObject { ["href"] = bindAddress(resource.Path) };
            foreach (ResourceExplanationRelationship edge in _catalog.Relationships.Where(edge => edge.Source == resource.Key))
            {
                var targets = new JsonArray();
                foreach (ResourceExplanationRelationshipTarget target in edge.Targets)
                {
                    ResourceExplanationResource? selected = _resources.FirstOrDefault(r => r.Key == target.Resource);
                    if (selected?.Path is not null)
                        targets.Add((JsonNode)new JsonObject { ["href"] = bindAddress(selected.Path) });
                }
                if (targets.Count > 0)
                    links[RelationUri(edge.Relationship)] = targets;
            }
            node["_links"] = links;
        }
    }

    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (JsonNode child in obj.Select(pair => pair.Value).OfType<JsonNode>().ToArray())
                foreach (JsonObject descendant in Descendants(child))
                    yield return descendant;
        }
        else if (node is JsonArray array)
            foreach (JsonNode child in array.OfType<JsonNode>().ToArray())
                foreach (JsonObject descendant in Descendants(child))
                    yield return descendant;
    }

    private static JsonObject Metadata(JsonObject node) => new()
    {
        ["identity"] = node["identity"]!.DeepClone(),
        ["addresses"] = node["addresses"]!.DeepClone(),
        ["fact_states"] = node["fact_states"]!.DeepClone(),
    };

    private static string Text(JsonNode node, string name) => node[name]?.GetValue<string>()
        ?? throw new InvalidOperationException("A selected declaration or identity fact is unavailable: " + name);

    private static void RequireComplete(ResourceExplanationRelationship relationship)
    {
        if (relationship.State != ExplanationObservationState.Available
            || relationship.TargetCompleteness != ResourceExplanationTargetProjectionCompleteness.Complete)
            throw new InvalidOperationException("Selected relationship observations must be available and complete.");
    }
}
