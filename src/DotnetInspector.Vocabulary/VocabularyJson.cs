using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetInspector.Vocabulary;

/// <summary>
/// One field's discoverable contract, exactly as it appears in a vocabulary document's JSON wire
/// contract.
/// </summary>
public sealed record VocabularyWireField(
    string Id,
    string Label,
    string Summary,
    [property: JsonPropertyName("type")] string Kind,
    string[] Operators);

/// <summary>One vocabulary section, exactly as it appears in a vocabulary document's JSON wire contract.</summary>
public sealed record VocabularyWireSection(
    string Id,
    string Name,
    string Summary,
    string[] Categories,
    [property: JsonPropertyName("accepted_by")] string[] AcceptedBy,
    VocabularyWireField[] Fields,
    Dictionary<string, JsonNode?>[] Values);

/// <summary>The complete vocabulary document's JSON wire contract.</summary>
public sealed record VocabularyWireDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    VocabularyWireSection[] Sections);

/// <summary>
/// The <see cref="JsonSerializerContext"/> for <see cref="VocabularyJson"/>'s NativeAOT-safe,
/// reflection-free serialization of <see cref="VocabularyWireDocument"/>. The context's camel-case
/// policy and the explicit <see cref="JsonPropertyNameAttribute"/> names above define the wire
/// shape.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VocabularyWireDocument))]
public sealed partial class VocabularyWireJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VocabularyWireDocument))]
public sealed partial class VocabularyWireCompactJsonContext : JsonSerializerContext;

/// <summary>NativeAOT-safe JSON projection for vocabulary documents.</summary>
public static class VocabularyJson
{
    /// <summary>Serializes the selected vocabulary sections as one structured document.</summary>
    public static string Serialize(
        VocabularyDocument document,
        IEnumerable<VocabularySection>? sections = null,
        bool indented = true)
    {
        VocabularyWireDocument wire = ToWireDocument(document, sections);
        JsonTypeInfo<VocabularyWireDocument> typeInfo = indented
            ? VocabularyWireJsonContext.Default.VocabularyWireDocument
            : VocabularyWireCompactJsonContext.Default.VocabularyWireDocument;
        return JsonSerializer.Serialize(wire, typeInfo);
    }

    /// <summary>
    /// Projects <paramref name="document"/>'s selected <paramref name="sections"/> to the JSON
    /// wire-contract shape, without serializing. Exposed so a <c>[JSExport]</c> method can call
    /// <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonTypeInfo{TValue})"/> directly in its
    /// own IL body — required for <c>tsbindgen</c>'s <c>JsonWireContractResolver</c> to discover
    /// <see cref="VocabularyWireDocument"/> as the return DTO (it only reads <c>Serialize&lt;T&gt;</c>
    /// call sites in the exported method's own body, not through an indirect helper call).
    /// </summary>
    public static VocabularyWireDocument ToWireDocument(
        VocabularyDocument document,
        IEnumerable<VocabularySection>? sections = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.SchemaVersion,
            [.. (sections ?? document.Sections).Select(ToWireSection)]);
    }

    private static VocabularyWireSection ToWireSection(VocabularySection section) =>
        new(
            section.Id,
            section.Name,
            section.Summary,
            [.. section.Categories],
            [.. section.AcceptedBy],
            [.. section.Fields.Select(ToWireField)],
            [.. section.Values.Select(row => ToWireRow(section.Fields, row))]);

    private static VocabularyWireField ToWireField(VocabularyField field) =>
        new(
            field.Id,
            field.Label,
            field.Summary,
            Name(field.Kind),
            [.. field.Operators.Select(Name)]);

    private static Dictionary<string, JsonNode?> ToWireRow(
        ImmutableArray<VocabularyField> fields,
        VocabularyRow row)
    {
        var cells = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (VocabularyField field in fields)
        {
            if (row.TryGetValue(field.Id, out VocabularyValue value))
                cells[field.Id] = ToWireValue(value);
        }
        return cells;
    }

    private static JsonNode? ToWireValue(VocabularyValue value) => value.Kind switch
    {
        VocabularyValueKind.Text => JsonValue.Create(value.Text),
        VocabularyValueKind.Integer => JsonValue.Create(value.Integer),
        VocabularyValueKind.Boolean => JsonValue.Create(value.Boolean),
        VocabularyValueKind.TextList => new JsonArray([.. value.TextList.Select(item => (JsonNode?)JsonValue.Create(item))]),
        _ => throw new InvalidOperationException($"Unsupported vocabulary value kind '{value.Kind}'."),
    };

    private static string Name(VocabularyValueKind value) => value switch
    {
        VocabularyValueKind.Text => "text",
        VocabularyValueKind.Integer => "integer",
        VocabularyValueKind.Boolean => "boolean",
        VocabularyValueKind.TextList => "text-list",
        _ => throw new InvalidOperationException($"Unsupported vocabulary value kind '{value}'."),
    };

    private static string Name(VocabularyOperator value) => value switch
    {
        VocabularyOperator.Equals => "equals",
        VocabularyOperator.NotEquals => "not-equals",
        VocabularyOperator.In => "in",
        VocabularyOperator.LessThan => "less-than",
        VocabularyOperator.GreaterThan => "greater-than",
        VocabularyOperator.Glob => "glob",
        VocabularyOperator.Contains => "contains",
        _ => throw new InvalidOperationException($"Unsupported vocabulary operator '{value}'."),
    };
}
