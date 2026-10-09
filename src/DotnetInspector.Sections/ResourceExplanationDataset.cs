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

    public JsonElement ToJson(Func<ResourcePath, string> bindAddress, bool hal = false,
        Func<ResourcePath, string>? bindHalAddress = null,
        Func<ResourcePath, string>? bindContractAddress = null)
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
            result = WriteHal(result, byKey, bindAddress, bindHalAddress ?? bindAddress, bindContractAddress);
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
        return document.RootElement.Clone();
    }

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

    private JsonObject WriteHal(JsonObject data, Dictionary<ExplanationResourceKey, JsonObject> nodes,
        Func<ResourcePath, string> bindAddress, Func<ResourcePath, string> bindHalAddress,
        Func<ResourcePath, string>? bindContractAddress)
    {
        var rendered = new Dictionary<ExplanationResourceKey, JsonObject>();
        foreach (ResourceExplanationResource resource in _resources)
        {
            JsonObject source;
            if (resource.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType)
                source = data["vocabularies"]![Text(nodes[resource.Key]["facts"]!, "identity")]!.AsObject();
            else if (resource.Key.ResourceType == ResourceExplanationVocabulary.VocabularyValueType)
                source = data["vocabularies"]!.AsObject().SelectMany(pair => pair.Value!["values"]!.AsArray())
                    .OfType<JsonObject>().Single(value => JsonNode.DeepEquals(value["identity"], nodes[resource.Key]["identity"]));
            else if (resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType)
                source = data["facets"]![Text(nodes[resource.Key]["facts"]!, "key")]!.AsObject();
            else if (resource.Key.ResourceType == ResourceExplanationVocabulary.ConsumerBindingType)
                source = data["bindings"]![Text(nodes[resource.Key]["facts"]!, "identity")]!.AsObject();
            else
                source = nodes[resource.Key];

            JsonObject facts = source["facts"]?.AsObject() ?? source;
            var state = new JsonObject { ["kind"] = resource.Key.ResourceType.Value };
            foreach (string field in new[] { "name", "summary" })
                if (facts[field] is JsonNode value)
                    state[field] = value.DeepClone();
            // Navigation is visible before the larger state and embedded populations.
            state["_links"] = Links(resource);
            foreach ((string name, JsonNode? value) in facts)
            {
                if (name is "name" or "summary" or "identity" or "addresses" or "fact_states")
                    continue;
                if (resource.Key.ResourceType != ResourceExplanationVocabulary.VocabularyValueType
                    && name is "values" or "property_sets" or "property_groups")
                    continue;
                string field = name.Replace('-', '_');
                if (state.ContainsKey(field))
                    throw new InvalidOperationException("A HAL state property collides with resource navigation: " + field);
                state[field] = value?.DeepClone();
            }
            // Facet listed operand values are state, unlike vocabulary resource members.
            if (resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType)
                state["values"] = facts["values"]!.DeepClone();
            if (facts["identity"] is JsonValue id)
                state["id"] = id.DeepClone();
            foreach (string field in new[] { "requires", "exposed_facets", "property_sets", "property_groups" })
                if (source[field] is JsonNode value)
                    state[field] = value.DeepClone();
            // Available state needs no receipt. Non-available outcomes remain beside affected data.
            var outcomes = new JsonObject();
            foreach ((string name, JsonNode? outcome) in source["fact_states"]!.AsObject())
                if (outcome!["state"]!.GetValue<string>() != "Absent")
                    outcomes[name.Replace('-', '_')] = outcome.DeepClone();
            if (outcomes.Count > 0)
                state["data_states"] = outcomes;
            rendered.Add(resource.Key, state);
        }

        foreach (ResourceExplanationResource vocabulary in _resources.Where(resource =>
            resource.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType))
            rendered[vocabulary.Key]["_embedded"] = new JsonObject
            {
                ["inspect:values"] = new JsonArray(Targets(vocabulary.Key, "vocabulary-value")
                    .Select(key => (JsonNode?)rendered[key].DeepClone()).ToArray()),
            };
        JsonObject root = rendered[_root.Key];
        root["data_scope"] = new JsonObject { ["completeness"] = "Complete" };
        JsonObject embedded = root["_embedded"]?.AsObject() ?? new JsonObject();
        if (_kind == "vocabulary-data")
            embedded["inspect:vocabularies"] = new JsonArray(_resources.Where(resource =>
                resource.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType && resource.Key != _root.Key)
                .Select(resource => (JsonNode?)rendered[resource.Key]).ToArray());
        else
        {
            embedded["inspect:facets"] = new JsonArray(_resources.Where(resource =>
                resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType && resource.Key != _root.Key)
                .Select(resource => (JsonNode?)rendered[resource.Key]).ToArray());
            embedded["inspect:bindings"] = new JsonArray(_resources.Where(resource =>
                resource.Key.ResourceType == ResourceExplanationVocabulary.ConsumerBindingType)
                .Select(resource => (JsonNode?)rendered[resource.Key]).ToArray());
        }
        if (root["_embedded"] is null)
            root["_embedded"] = embedded;
        return root;

        JsonObject Links(ResourceExplanationResource resource)
        {
            var links = new JsonObject { ["self"] = Link(resource) };
            if (resource.Key == _root.Key)
            {
                links["curies"] = new JsonArray((JsonNode)new JsonObject
                {
                    ["name"] = "inspect", ["href"] = "urn:dotnet-inspect:reading:{rel}", ["templated"] = true,
                });
                if (bindContractAddress is not null)
                    links["describedby"] = new JsonObject
                    {
                        ["href"] = Bind(bindContractAddress, resource.Path!),
                        ["title"] = "Schema and observation details", ["type"] = "application/json",
                    };
            }
            foreach (ResourceExplanationRelationship edge in _catalog.Relationships.Where(edge => edge.Source == resource.Key))
            {
                string? relation = edge.Relationship.Value switch
                {
                    // Complete member inventories are already embedded with their own self links.
                    "term-map-target" => "vocabularies", "required-context" => "required-context",
                    "exposed-by" => "bindings", "exposes" => "exposed-facets", _ => null,
                };
                if (relation is null)
                    continue;
                var targets = new JsonArray(edge.Targets.Select(target => _resources.FirstOrDefault(r => r.Key == target.Resource))
                    .OfType<ResourceExplanationResource>().Select(target => (JsonNode?)Link(target)).ToArray());
                if (targets.Count > 0)
                    links["inspect:" + relation] = targets;
            }
            return links;
        }

        static string Bind(Func<ResourcePath, string> binder, ResourcePath path)
        {
            string address = binder(path);
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException("A HAL link requires a usable host address.");
            return address;
        }

        JsonObject Link(ResourceExplanationResource resource)
        {
            bool selectedHal = resource.Key.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType
                || resource.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
                || resource.Key.ResourceType == ResourceExplanationVocabulary.QueryFacetType;
            JsonObject facts = nodes[resource.Key]["facts"]!.AsObject();
            return new JsonObject
            {
                ["href"] = Bind(selectedHal ? bindHalAddress : bindAddress, resource.Path!),
                ["title"] = facts["name"]?.DeepClone() ?? JsonValue.Create(resource.Key.ResourceType.Value),
                ["type"] = selectedHal ? "application/hal+json" : "application/json",
            };
        }
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
