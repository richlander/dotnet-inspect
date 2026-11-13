using System.Text.Json;
using System.Text.Json.Nodes;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

public sealed partial class ResourceExplanationDataset
{
    private static ResourceExplanationDataset CreateVocabularyReading(
        ResourceExplanationCatalog catalog, ResourceExplanationResource root,
        Dictionary<ExplanationResourceKey, ResourceExplanationResource> resources,
        IEnumerable<string>? valueIds)
    {
        var ownership = catalog.Relationships.Where(edge => edge.Relationship.Value == "vocabulary-value")
            .SelectMany(edge => edge.Targets.Select(target => (target.Resource, Owner: edge.Source)))
            .ToDictionary(pair => pair.Resource, pair => pair.Owner);
        var members = catalog.Relationships.Where(edge => edge.Source == root.Key
            && edge.Relationship.Value == "vocabulary-value").SelectMany(edge =>
            {
                RequireComplete(edge);
                return edge.Targets;
            }).Select(target => target.Resource).ToArray();
        IEnumerable<ExplanationResourceKey> requested = members;
        if (valueIds is not null)
        {
            var byId = members.ToDictionary(key => VocabularyIdentity(resources[key]), key => key,
                StringComparer.Ordinal);
            var keys = new HashSet<ExplanationResourceKey>();
            foreach (string id in valueIds)
            {
                if (id is null || !byId.TryGetValue(id, out ExplanationResourceKey? key))
                    throw new InvalidOperationException("Unknown vocabulary value identity: " + id);
                keys.Add(key);
            }
            if (keys.Count == 0)
                throw new InvalidOperationException("Value-identity narrowing requires at least one identity.");
            requested = members.Where(keys.Contains);
        }
        var queue = new Queue<ExplanationResourceKey>(requested.Prepend(root.Key));
        var selected = new HashSet<ExplanationResourceKey>();
        int targetCount = 0;
        while (queue.TryDequeue(out ExplanationResourceKey? key))
        {
            if (!resources.TryGetValue(key, out ResourceExplanationResource? resource))
                throw new InvalidOperationException("A selected term target has no local resource snapshot.");
            if (!selected.Add(key))
                continue;
            if (selected.Count > ResourceExplanationRequest.HostResourceLimit)
                throw new InvalidOperationException("Selected data exceeds the host resource limit; no partial dataset was emitted.");
            if (resource.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType)
            {
                // Validate this included vocabulary's complete membership, but
                // do not expand its entire population merely to explain a term.
                foreach (var edge in catalog.Relationships.Where(edge => edge.Source == key
                    && edge.Relationship.Value == "vocabulary-value"))
                    RequireComplete(edge);
                continue;
            }
            if (resource.ResourceType != ResourceExplanationVocabulary.VocabularyValueType
                || !ownership.TryGetValue(key, out ExplanationResourceKey? owner))
                throw new InvalidOperationException("Selected term context requires an owning vocabulary.");
            queue.Enqueue(owner);
            foreach (var edge in catalog.Relationships.Where(edge => edge.Source == key
                && edge.Relationship.Value == "term-map-value"))
            {
                RequireComplete(edge);
                foreach (var target in edge.Targets)
                {
                    if (++targetCount > 4096)
                        throw new InvalidOperationException("Selected data exceeds the host target limit; no partial dataset was emitted.");
                    queue.Enqueue(target.Resource);
                }
            }
        }
        return new(catalog, root,
            [root, .. catalog.Resources.Where(resource => selected.Contains(resource.Key) && resource.Key != root.Key)],
            "vocabulary-data", valueIds is not null);
    }

    private JsonElement WriteVocabularyReading(bool hal, Func<ResourcePath, string> bindHalAddress)
    {
        var facts = ResourceExplanationDataProjection.CreateFactResources(_catalog, _resources)
            .Select((projection, i) => (_resources[i].Key,
                Node: JsonNode.Parse(projection.GetRawText())!.AsObject()))
            .ToDictionary(pair => pair.Key, pair => pair.Node);
        var selected = _resources.Select(resource => resource.Key).ToHashSet();
        var vocabularies = new JsonObject();
        foreach (var vocabulary in _resources.Where(resource =>
            resource.ResourceType == ResourceExplanationVocabulary.ValueVocabularyType))
        {
            JsonObject source = facts[vocabulary.Key]["facts"]!.AsObject();
            JsonObject states = facts[vocabulary.Key]["fact_states"]!.AsObject();
            string id = Text(source, "identity");
            var declarations = RequireFact(source, "maps").AsArray();
            var maps = new JsonObject();
            var sets = new JsonObject();
            var groups = new JsonObject();
            foreach (JsonNode? declaration in declarations)
            {
                if (Text(declaration!, "coverage") != "complete")
                    throw new InvalidOperationException("Sparse data requires complete property coverage.");
                string map = Text(declaration!, "identity");
                var descriptor = new JsonObject();
                foreach ((string field, JsonNode? value) in declaration!.AsObject())
                    if (field != "identity" && value is not null)
                        descriptor[field] = value.DeepClone();
                maps.Add(map, descriptor);
                if (Text(declaration, "value-kind") == "boolean")
                    sets[map] = new JsonArray();
                else if (Text(declaration, "value-kind") == "text"
                    && Text(declaration, "cardinality") == "optional-one")
                    groups[map] = new JsonObject();
            }
            var values = new JsonObject();
            var members = Targets(vocabulary.Key, "vocabulary-value").ToArray();
            foreach (ExplanationResourceKey key in members.Where(selected.Contains))
            {
                JsonObject valueFacts = facts[key]["facts"]!.AsObject();
                JsonObject valueStates = facts[key]["fact_states"]!.AsObject();
                string valueId = Text(valueFacts, "identity");
                string name = Text(valueFacts, "name");
                var record = new JsonObject();
                if (name != valueId)
                    record["name"] = name;
                if (AvailableOrAbsent(valueFacts, valueStates, "summary") is JsonNode summary)
                    record["summary"] = summary;
                JsonArray entries = RequireFact(valueFacts, "map-entries").AsArray();
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
                            if (grouped[group] is not JsonArray groupMembers)
                                grouped[group] = groupMembers = new JsonArray();
                            groupMembers.Add((JsonNode?)JsonValue.Create(valueId));
                        }
                    }
                    else
                    {
                        if (map is "id" or "name" or "summary" or "_links" or "_embedded")
                            throw new InvalidOperationException("A vocabulary property collides with reading metadata: " + map);
                        record[map] = cardinality is "zero-or-more" or "one-or-more"
                            ? new JsonArray(observed) : observed.FirstOrDefault();
                    }
                }
                values.Add(valueId, record);
            }
            var entry = new JsonObject
            {
                ["name"] = RequireFact(source, "name").DeepClone(),
                ["catalog_values"] = members.Length,
                ["accepted_by"] = RequireFact(source, "accepted-by").DeepClone(),
                ["maps"] = maps, ["map_order"] = Order(maps), ["values"] = values,
                ["defaults"] = new JsonArray(RequireFact(source, "defaults").AsArray()
                    .Where(value => values.ContainsKey(value!.GetValue<string>()))
                    .Select(value => value!.DeepClone()).ToArray()),
                ["property_sets"] = sets, ["property_groups"] = groups,
            };
            if (AvailableOrAbsent(source, states, "summary") is JsonNode vocabularySummary)
                entry["summary"] = vocabularySummary;
            vocabularies.Add(id, entry);
        }
        var scope = new JsonObject
        {
            ["profile"] = "vocabulary-reading/v1", ["vocabulary"] = VocabularyIdentity(_root),
            ["population"] = "selected-values", ["completeness"] = "Complete",
        };
        JsonObject result;
        if (hal)
        {
            string href = bindHalAddress(_root.Path
                ?? throw new InvalidOperationException("Vocabulary navigation requires a public resource path."));
            if (string.IsNullOrWhiteSpace(href))
                throw new InvalidOperationException("A HAL link requires a usable host address.");
            var embedded = new JsonArray();
            foreach ((string id, JsonNode? node) in vocabularies)
            {
                var vocabulary = new JsonObject { ["id"] = id };
                foreach ((string field, JsonNode? value) in node!.AsObject())
                    if (field != "values")
                        vocabulary[field] = value?.DeepClone();
                vocabulary["_embedded"] = new JsonObject
                {
                    ["values"] = new JsonArray(node["values"]!.AsObject().Select(pair =>
                    {
                        var row = new JsonObject { ["id"] = pair.Key };
                        foreach ((string field, JsonNode? value) in pair.Value!.AsObject())
                            row[field] = value?.DeepClone();
                        return (JsonNode?)row;
                    }).ToArray()),
                };
                embedded.Add((JsonNode)vocabulary);
            }
            result = new JsonObject
            {
                ["_links"] = new JsonObject
                {
                    [_narrowed ? "collection" : "self"] = new JsonObject
                    {
                        ["href"] = href, ["type"] = "application/hal+json",
                    },
                },
                ["_embedded"] = new JsonObject { ["vocabularies"] = embedded }, ["scope"] = scope,
            };
        }
        else
        {
            foreach (var node in vocabularies.Select(pair => pair.Value!.AsObject()))
                node["value_order"] = Order(node["values"]!.AsObject());
            result = new JsonObject { ["scope"] = scope, ["vocabularies"] = vocabularies };
        }
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonArray Order(JsonObject table) =>
        new(table.Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray());

    private static string VocabularyIdentity(ResourceExplanationResource resource)
    {
        var fact = resource.Facts.FirstOrDefault(fact => fact.Fact.Value == "identity");
        if (fact is not { State: ExplanationObservationState.Available, Values.Length: 1 }
            || fact.Values[0] is not ExplanationValue.Scalar { Value.Text: { } id })
            throw new InvalidOperationException("A selected vocabulary identity is unavailable.");
        return id;
    }
}
