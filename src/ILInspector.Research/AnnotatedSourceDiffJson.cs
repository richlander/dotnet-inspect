using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using Inspector.Findings;
using Inspector.Text;

namespace ILInspector.Research;

public static class AnnotatedSourceDiffJson
{
    const string ContractError =
        "Annotated-source diff JSON violates the JSON contract.";

    public static string Serialize(
        AnnotatedSourceDiffDocument document,
        bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        return indented
            ? JsonSerializer.Serialize(
                document,
                AnnotatedSourceDiffJsonContext.Default
                    .AnnotatedSourceDiffDocument)
            : JsonSerializer.Serialize(
                document,
                AnnotatedSourceDiffCompactJsonContext.Default
                    .AnnotatedSourceDiffDocument);
    }

    public static AnnotatedSourceDiffDocument Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (Exception error)
            when (error is JsonException or ArgumentException)
        {
            throw new JsonException(
                "Annotated-source diff JSON is malformed.");
        }

        using (parsed)
        {
            try
            {
                ValidateDocument(parsed.RootElement);
            }
            catch (JsonException)
            {
                throw;
            }
            catch (Exception error)
                when (error is InvalidOperationException
                    or ArgumentException)
            {
                throw new JsonException(ContractError);
            }
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                AnnotatedSourceDiffStrictJsonContext.Default
                    .AnnotatedSourceDiffDocument)
                ?? throw new JsonException(
                    "Annotated-source diff document is null.");
        }
        catch (JsonException error)
            when (error.Message != ContractError)
        {
            throw new JsonException(ContractError);
        }
        catch (ArgumentException error)
        {
            throw new JsonException(
                "Annotated-source diff JSON violates the document model contract: "
                    + error.Message);
        }
    }

    static void ValidateDocument(JsonElement root)
    {
        RequireObject(
            root,
            "document",
            [
                "schema_version",
                "methodology_version",
                "subject",
                "style",
                "before",
                "after",
                "forwarders",
                "media",
            ]);
        EnumValue<AnnotatedSourceDiffStyle>(root, "style");
        if (root.TryGetProperty("before", out JsonElement before))
            ValidateSide(before, "before");
        if (root.TryGetProperty("after", out JsonElement after))
            ValidateSide(after, "after");
        if (root.TryGetProperty("forwarders", out JsonElement forwarders)
            && forwarders.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement forwarder in forwarders.EnumerateArray())
            {
                RequireObject(
                    forwarder,
                    "forwarder",
                    ["side", "hop_index", "type_name", "target_assembly"]);
                EnumValue<AnnotatedSourceDiffSideKind>(forwarder, "side");
            }
        }
        if (root.TryGetProperty("media", out JsonElement media)
            && media.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in media.EnumerateArray())
                ValidateMedium(item);
        }
    }

    static void ValidateSide(JsonElement side, string name)
    {
        RequireObject(side, name, ["outcome"]);
        EnumValue<AnnotatedSourceDiffSideOutcomeKind>(side, "outcome");
        OptionalEnumValue<AnnotatedSourceDiffSideReason>(side, "reason");
        if (side.TryGetProperty("endpoint", out JsonElement endpoint)
            && endpoint.ValueKind != JsonValueKind.Null)
        {
            RequireObject(
                endpoint,
                $"{name}.endpoint",
                [
                    "assembly_name",
                    "module_version_id",
                    "method_token",
                    "relationship_role",
                ]);
            EnumValue<ResearchTargetRelationshipRole>(
                endpoint,
                "relationship_role");
        }
        if (side.TryGetProperty("document", out JsonElement document)
            && document.ValueKind != JsonValueKind.Null)
        {
            _ = AnnotatedSourceJson.DeserializeDocument(
                document.GetRawText());
        }
    }

    static void ValidateMedium(JsonElement medium)
    {
        RequireObject(
            medium,
            "medium",
            ["medium", "before_lines", "after_lines"]);
        EnumValue<AnnotatedSourceDiffMediumKind>(medium, "medium");
        if (medium.TryGetProperty("too_complex", out JsonElement limit)
            && limit.ValueKind != JsonValueKind.Null)
        {
            RequireObject(
                limit,
                "medium.too_complex",
                ["side", "dimension", "actual", "maximum"]);
            EnumValue<AnnotatedSourceDiffSideKind>(limit, "side");
            EnumValue<AnnotatedSourceDiffLimitDimension>(
                limit,
                "dimension");
        }
        if (medium.TryGetProperty("comparison", out JsonElement comparison)
            && comparison.ValueKind != JsonValueKind.Null)
        {
            RequireObject(
                comparison,
                "medium.comparison",
                ["analysis", "characterization"]);
            if (comparison.TryGetProperty(
                    "analysis",
                    out JsonElement analysis))
            {
                ValidateAnalysis(analysis);
            }
            if (comparison.TryGetProperty(
                    "characterization",
                    out JsonElement characterization))
            {
                ValidateCharacterization(characterization);
            }
        }
    }

    static void ValidateAnalysis(JsonElement analysis)
    {
        RequireObject(
            analysis,
            "comparison.analysis",
            ["before", "after", "relations"]);
        if (!analysis.TryGetProperty(
                "relations",
                out JsonElement relations)
            || relations.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement relation in relations.EnumerateArray())
        {
            if (relation.ValueKind != JsonValueKind.Object
                || !relation.TryGetProperty(
                    "kind",
                    out JsonElement kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                throw new JsonException(ContractError);
            }
            string? value = kind.GetString();
            if (value == nameof(AnalysisDiffRelation.Correspondence))
            {
                EnumValue<AnalysisDiffContentKind>(relation, "content");
                EnumValue<AnalysisDiffPlacementKind>(
                    relation,
                    "placement");
            }
            else if (value is not nameof(AnalysisDiffRelation.Addition)
                and not nameof(AnalysisDiffRelation.Removal))
            {
                throw new JsonException(ContractError);
            }
        }
    }

    static void ValidateCharacterization(JsonElement characterization)
    {
        RequireObject(
            characterization,
            "comparison.characterization",
            ["summary", "regions", "moves"]);
        EnumValue<TextDocumentOutcome>(
            characterization,
            "summary");
        if (characterization.TryGetProperty(
                "regions",
                out JsonElement regions)
            && regions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement region in regions.EnumerateArray())
            {
                EnumValue<TextRegionOutcome>(region, "outcome");
                if (region.TryGetProperty(
                        "changes",
                        out JsonElement changes)
                    && changes.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement change in changes.EnumerateArray())
                        EnumValue<TextChangeOutcome>(change, "outcome");
                }
            }
        }
        if (characterization.TryGetProperty(
                "moves",
                out JsonElement moves)
            && moves.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement move in moves.EnumerateArray())
                EnumValue<TextMoveContent>(move, "content");
        }
    }

    static void RequireObject(
        JsonElement value,
        string name,
        IReadOnlyList<string> required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{name} must be an object.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException(
                    $"{name} contains duplicate property '{property.Name}'.");
            }
        }
        string[] missing =
        [
            .. required.Where(property => !seen.Contains(property)),
        ];
        if (missing.Length > 0)
        {
            throw new JsonException(
                $"{name} is missing required properties: "
                    + $"{string.Join(", ", missing)}.");
        }
    }

    static void OptionalEnumValue<TEnum>(
        JsonElement owner,
        string property)
        where TEnum : struct, Enum
    {
        if (owner.TryGetProperty(property, out JsonElement value)
            && value.ValueKind != JsonValueKind.Null)
        {
            EnumValue<TEnum>(owner, property);
        }
    }

    static void EnumValue<TEnum>(
        JsonElement owner,
        string property)
        where TEnum : struct, Enum
    {
        if (!owner.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } name
            || !Enum.TryParse(name, ignoreCase: false, out TEnum parsed)
            || !string.Equals(
                Enum.GetName(parsed),
                name,
                StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Annotated-source diff JSON contains an unknown "
                    + $"{typeof(TEnum).Name} value.");
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    Converters = [typeof(AnalysisDiffRelationJsonConverter)])]
[JsonSerializable(typeof(AnnotatedSourceDiffDocument))]
public partial class AnnotatedSourceDiffJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    Converters = [typeof(AnalysisDiffRelationJsonConverter)])]
[JsonSerializable(typeof(AnnotatedSourceDiffDocument))]
public partial class AnnotatedSourceDiffCompactJsonContext
    : JsonSerializerContext;

[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    Converters = [typeof(AnalysisDiffRelationJsonConverter)])]
[JsonSerializable(typeof(AnnotatedSourceDiffDocument))]
internal sealed partial class AnnotatedSourceDiffStrictJsonContext
    : JsonSerializerContext;

public sealed class AnalysisDiffRelationJsonConverter
    : JsonConverter<AnalysisDiffRelation>
{
    public override AnalysisDiffRelation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document =
            JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Analysis diff relation must be an object.");

        var properties = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new JsonException(
                    $"Analysis diff relation contains duplicate property '{property.Name}'.");
            }
        }
        if (!properties.TryGetValue("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                "Analysis diff relation requires a string kind.");
        }

        return kind.GetString() switch
        {
            nameof(AnalysisDiffRelation.Addition) =>
                new AnalysisDiffRelation.Addition(
                    Coordinates(
                        properties,
                        ["kind", "after_coordinates"],
                        "after_coordinates")),
            nameof(AnalysisDiffRelation.Removal) =>
                new AnalysisDiffRelation.Removal(
                    Coordinates(
                        properties,
                        ["kind", "before_coordinates"],
                        "before_coordinates")),
            nameof(AnalysisDiffRelation.Correspondence) =>
                new AnalysisDiffRelation.Correspondence(
                    Coordinates(
                        properties,
                        [
                            "kind",
                            "before_coordinates",
                            "after_coordinates",
                            "content",
                            "placement",
                        ],
                        "before_coordinates"),
                    Coordinates(
                        properties,
                        [
                            "kind",
                            "before_coordinates",
                            "after_coordinates",
                            "content",
                            "placement",
                        ],
                        "after_coordinates"),
                    StrictEnum<AnalysisDiffContentKind>(
                        properties,
                        "content"),
                    StrictEnum<AnalysisDiffPlacementKind>(
                        properties,
                        "placement")),
            _ => throw new JsonException(
                "Analysis diff relation contains an unknown kind."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        AnalysisDiffRelation value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case AnalysisDiffRelation.Addition addition:
                writer.WriteString(
                    "kind",
                    nameof(AnalysisDiffRelation.Addition));
                WriteCoordinates(
                    writer,
                    "after_coordinates",
                    addition.AfterCoordinates);
                break;
            case AnalysisDiffRelation.Removal removal:
                writer.WriteString(
                    "kind",
                    nameof(AnalysisDiffRelation.Removal));
                WriteCoordinates(
                    writer,
                    "before_coordinates",
                    removal.BeforeCoordinates);
                break;
            case AnalysisDiffRelation.Correspondence correspondence:
                writer.WriteString(
                    "kind",
                    nameof(AnalysisDiffRelation.Correspondence));
                WriteCoordinates(
                    writer,
                    "before_coordinates",
                    correspondence.BeforeCoordinates);
                WriteCoordinates(
                    writer,
                    "after_coordinates",
                    correspondence.AfterCoordinates);
                writer.WriteString(
                    "content",
                    correspondence.Content.ToString());
                writer.WriteString(
                    "placement",
                    correspondence.Placement.ToString());
                break;
            default:
                throw new JsonException(
                    "Analysis diff relation contains an unknown runtime type.");
        }
        writer.WriteEndObject();
    }

    static ImmutableArray<int> Coordinates(
        IReadOnlyDictionary<string, JsonElement> properties,
        IReadOnlyList<string> expected,
        string name)
    {
        RequireExact(properties, expected);
        if (!properties.TryGetValue(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"Analysis diff relation requires {name}.");
        }

        var coordinates = ImmutableArray.CreateBuilder<int>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (!item.TryGetInt32(out int coordinate))
            {
                throw new JsonException(
                    $"Analysis diff relation {name} must contain integers.");
            }
            coordinates.Add(coordinate);
        }
        return coordinates.ToImmutable();
    }

    static TEnum StrictEnum<TEnum>(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
        where TEnum : struct, Enum
    {
        if (!properties.TryGetValue(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || !Enum.TryParse(text, ignoreCase: false, out TEnum parsed)
            || !string.Equals(
                Enum.GetName(parsed),
                text,
                StringComparison.Ordinal))
        {
            throw new JsonException(
                $"Analysis diff relation contains an unknown {typeof(TEnum).Name}.");
        }
        return parsed;
    }

    static void RequireExact(
        IReadOnlyDictionary<string, JsonElement> properties,
        IReadOnlyList<string> expected)
    {
        if (properties.Count != expected.Count
            || properties.Keys.Any(property => !expected.Contains(property)))
        {
            throw new JsonException(
                "Analysis diff relation contains unknown or missing properties.");
        }
    }

    static void WriteCoordinates(
        Utf8JsonWriter writer,
        string name,
        ImmutableArray<int> coordinates)
    {
        writer.WriteStartArray(name);
        foreach (int coordinate in coordinates)
            writer.WriteNumberValue(coordinate);
        writer.WriteEndArray();
    }
}
