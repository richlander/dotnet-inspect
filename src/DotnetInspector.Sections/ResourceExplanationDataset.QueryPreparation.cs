using System.Text.Json;
using System.Text.Json.Nodes;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

public sealed partial class ResourceExplanationDataset
{
    private JsonElement WriteQueryPreparation(bool hal, Func<ResourcePath, string> bindHalAddress)
    {
        ResourceExplanationResource querySpace = _root.Key.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
            ? _root : QuerySpaceForFacet();
        ExplanationResourceKey[] members = Targets(querySpace.Key, "query-facet").ToArray();
        var resources = _resources.Prepend(querySpace).DistinctBy(resource => resource.Key).ToArray();
        var factResources = ResourceExplanationDataProjection.CreateFactResources(_catalog, resources);
        var facts = resources.Select((resource, i) => (resource.Key,
                Facts: JsonNode.Parse(factResources[i].GetRawText())!["facts"]!.AsObject()))
            .ToDictionary(pair => pair.Key, pair => pair.Facts);
        var localKeys = _resources.Where(resource => resource.ResourceType == ResourceExplanationVocabulary.QueryFacetType)
            .ToDictionary(resource => resource.Key, FacetKey);
        if (localKeys.Values.Distinct(StringComparer.Ordinal).Count() != localKeys.Count)
            throw new InvalidOperationException("Selected facets have colliding local keys.");
        if (localKeys.Keys.Any(key => !members.Contains(key)))
            throw new InvalidOperationException("Selected context must belong to the owning query space.");
        var facets = new JsonObject();
        foreach (ExplanationResourceKey identity in members.Where(localKeys.ContainsKey))
        {
            var record = new JsonObject();
            foreach (string field in new[] { "summary", "operators", "value-kind", "examples", "effects", "input-rules", "values" })
            {
                JsonNode value = RequireFact(facts[identity], field);
                if (value is JsonArray { Count: 0 } && field != "operators")
                    continue;
                record[field.Replace('-', '_')] = value.DeepClone();
            }
            var requires = new JsonArray(Targets(identity, "required-context")
                .Select(key => (JsonNode?)JsonValue.Create(localKeys[key])).ToArray());
            if (requires.Count > 0)
                record["requires"] = requires;
            facets.Add(localKeys[identity], record);
        }
        var bindings = new JsonObject();
        foreach (ResourceExplanationResource binding in _resources.Where(resource =>
            resource.ResourceType == ResourceExplanationVocabulary.ConsumerBindingType))
        {
            JsonObject source = facts[binding.Key];
            var record = new JsonObject
            {
                ["consumer_kind"] = RequireFact(source, "consumer-kind").DeepClone(),
                ["gesture"] = RequireFact(source, "gesture").DeepClone(),
                ["input_rules"] = RequireFact(source, "input-rules").DeepClone(),
                ["exposed_facets"] = new JsonArray(Targets(binding.Key, "exposes")
                    .Where(localKeys.ContainsKey).Select(key => (JsonNode?)JsonValue.Create(localKeys[key])).ToArray()),
            };
            bindings.Add(RequireFact(source, "identity").GetValue<string>(), record);
        }
        var scope = new JsonObject
        {
            ["profile"] = "query-preparation/v1",
            ["resource"] = RequireFact(facts[querySpace.Key], "identity").DeepClone(),
            ["root"] = _root.Path?.Value,
            ["population"] = "selected-facets", ["completeness"] = "Complete",
            ["catalog_facets"] = members.Length,
        };
        JsonObject result;
        if (hal)
        {
            var links = new JsonObject();
            if (!_narrowed)
                links["self"] = Link(_root);
            if (_narrowed || querySpace.Key != _root.Key)
                links["collection"] = Link(querySpace);
            result = new JsonObject
            {
                ["_links"] = links,
                ["_embedded"] = new JsonObject
                {
                    ["facets"] = new JsonArray(facets.Select(pair => (JsonNode?)WithKey(pair)).ToArray()),
                    ["bindings"] = new JsonArray(bindings.Select(pair => (JsonNode?)WithKey(pair)).ToArray()),
                },
                ["scope"] = scope,
            };
        }
        else
        {
            result = new JsonObject
            {
                ["scope"] = scope, ["facets"] = facets,
                ["facet_order"] = new JsonArray(facets.Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray()),
                ["bindings"] = bindings,
            };
        }
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());
        return document.RootElement.Clone();

        JsonObject Link(ResourceExplanationResource resource)
        {
            if (resource.Path is null)
                throw new InvalidOperationException("Query preparation navigation requires a public resource path.");
            string href = bindHalAddress(resource.Path);
            if (string.IsNullOrWhiteSpace(href))
                throw new InvalidOperationException("A HAL link requires a usable host address.");
            return new JsonObject { ["href"] = href, ["type"] = "application/hal+json" };
        }
    }

    private ResourceExplanationResource QuerySpaceForFacet()
    {
        ExplanationResourceKey[] owners = _catalog.Relationships.Where(edge =>
                edge.Source.ResourceType == ResourceExplanationVocabulary.QuerySpaceType
                && edge.Relationship.Value == "query-facet"
                && edge.Targets.Any(target => target.Resource == _root.Key))
            .Select(edge => edge.Source).Distinct().ToArray();
        if (owners.Length != 1)
            throw new InvalidOperationException("A selected facet requires one owning query space.");
        return _catalog.Resources.Single(resource => resource.Key == owners[0]);
    }

    private static string FacetKey(ResourceExplanationResource resource)
    {
        ExplanationFactObservation? key = resource.Facts.FirstOrDefault(fact => fact.Fact.Value == "key");
        if (key is not { State: ExplanationObservationState.Available, Values.Length: 1 }
            || key.Values[0] is not ExplanationValue.Scalar { Value.Text: { } text })
            throw new InvalidOperationException("A selected query facet key is unavailable.");
        return text;
    }

    private static JsonNode RequireFact(JsonObject facts, string field) => facts[field]
        ?? throw new InvalidOperationException("A selected query-preparation fact is unavailable: " + field);

    private static JsonObject WithKey(KeyValuePair<string, JsonNode?> pair)
    {
        var result = new JsonObject { ["key"] = pair.Key };
        foreach ((string name, JsonNode? value) in pair.Value!.AsObject())
            result[name] = value?.DeepClone();
        return result;
    }
}
