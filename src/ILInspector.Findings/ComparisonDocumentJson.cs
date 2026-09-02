using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ILInspector.Findings;

/// <summary>
/// AOT-safe structured JSON for comparison documents with caller-generated payload metadata.
/// </summary>
public static class ComparisonDocumentJson
{
    static readonly HashSet<string> DocumentPropertyNames =
    [
        "schema_version",
        "subject_coordinate_basis",
        "identifier",
        "display",
        "change_kinds",
        "change_id",
        "comparison",
        "subjects",
        "change_descriptions",
    ];

    static readonly HashSet<string> SubjectPropertyNames =
    [
        "identifier",
        "display",
        "change_kinds",
        "change_id",
        "comparison",
    ];

    static readonly HashSet<string> DescriptionPropertyNames =
    [
        "id",
        "change_kinds",
        "before",
        "after",
        "transformations",
    ];

    static readonly HashSet<string> EndpointPropertyNames =
    [
        "identifier",
        "display",
    ];

    static readonly HashSet<string> TransformationPropertyNames =
    [
        "identifier",
        "display",
    ];

    /// <summary>Serializes one document in its canonical structured form.</summary>
    public static string Serialize<T>(
        ComparisonDocument<T> document,
        JsonTypeInfo<T> payloadTypeInfo,
        bool indented = false)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = indented }))
        {
            WriteDocument(writer, document, payloadTypeInfo);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Deserializes one strict structured form and constructs the public model through
    /// its normal validation path.
    /// </summary>
    public static ComparisonDocument<T> Deserialize<T>(
        string json,
        JsonTypeInfo<T> payloadTypeInfo)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(payloadTypeInfo);

        using JsonDocument parsed = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });

        try
        {
            return ReadDocument(parsed.RootElement, payloadTypeInfo);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(
                "Comparison-document JSON violates the comparison document contract.",
                ex);
        }
    }

    static void WriteDocument<T>(
        Utf8JsonWriter writer,
        ComparisonDocument<T> document,
        JsonTypeInfo<T> payloadTypeInfo)
        where T : notnull
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", document.SchemaVersion);
        writer.WriteString(
            "subject_coordinate_basis",
            document.SubjectCoordinateBasis switch
            {
                SubjectCoordinateBasis.OuterContext => "outer-context",
                SubjectCoordinateBasis.RootRelative => "root-relative",
                _ => throw new InvalidOperationException(
                    "Comparison document has an unknown coordinate basis."),
            });
        writer.WriteString("identifier", document.Identifier);
        writer.WriteString("display", document.Display);
        WriteChange(writer, document.Change);

        if (document.Comparison is ComparisonRootComparison<T>.Present present)
        {
            writer.WritePropertyName("comparison");
            JsonSerializer.Serialize(writer, present.Comparison, payloadTypeInfo);
        }
        else if (document.Comparison is not ComparisonRootComparison<T>.NotApplicable)
        {
            throw new InvalidOperationException(
                "Comparison document has an unknown root comparison case.");
        }

        writer.WritePropertyName("subjects");
        writer.WriteStartArray();
        foreach (ComparisonSubject<T> subject in document.Subjects)
        {
            writer.WriteStartObject();
            writer.WriteString("identifier", subject.Identifier);
            writer.WriteString("display", subject.Display);
            WriteChange(writer, subject.Change);
            writer.WritePropertyName("comparison");
            JsonSerializer.Serialize(writer, subject.Comparison, payloadTypeInfo);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("change_descriptions");
        writer.WriteStartArray();
        foreach (ComparisonChangeDescription description in document.ChangeDescriptions)
            WriteDescription(writer, description);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    static void WriteChange(Utf8JsonWriter writer, ComparisonSubjectChange change)
    {
        switch (change)
        {
            case ComparisonSubjectChange.Diff:
                return;
            case ComparisonSubjectChange.Addition:
                WriteKinds(writer, "addition");
                return;
            case ComparisonSubjectChange.Deletion:
                WriteKinds(writer, "deletion");
                return;
            case ComparisonSubjectChange.Rename rename:
                WriteKinds(writer, "rename");
                writer.WriteString("change_id", rename.ChangeId);
                return;
            case ComparisonSubjectChange.Move move:
                WriteKinds(writer, "move");
                writer.WriteString("change_id", move.ChangeId);
                return;
            case ComparisonSubjectChange.RenameAndMove renameAndMove:
                WriteKinds(writer, "rename", "move");
                writer.WriteString("change_id", renameAndMove.ChangeId);
                return;
            default:
                throw new InvalidOperationException(
                    "Comparison document has an unknown subject change case.");
        }
    }

    static void WriteKinds(Utf8JsonWriter writer, params string[] kinds)
    {
        writer.WritePropertyName("change_kinds");
        writer.WriteStartArray();
        foreach (string kind in kinds)
            writer.WriteStringValue(kind);
        writer.WriteEndArray();
    }

    static void WriteDescription(
        Utf8JsonWriter writer,
        ComparisonChangeDescription description)
    {
        writer.WriteStartObject();
        writer.WriteString("id", description.Id);
        switch (description.Kind)
        {
            case ComparisonExceptionalChangeKind.Rename:
                WriteKinds(writer, "rename");
                break;
            case ComparisonExceptionalChangeKind.Move:
                WriteKinds(writer, "move");
                break;
            case ComparisonExceptionalChangeKind.RenameAndMove:
                WriteKinds(writer, "rename", "move");
                break;
            default:
                throw new InvalidOperationException(
                    "Comparison document has an unknown description kind.");
        }
        WriteEndpoint(writer, "before", description.Before);
        WriteEndpoint(writer, "after", description.After);

        writer.WritePropertyName("transformations");
        writer.WriteStartArray();
        foreach (ComparisonTransformationDescriptor transformation in description.Transformations)
        {
            writer.WriteStartObject();
            writer.WriteString("identifier", transformation.Identifier);
            writer.WriteString("display", transformation.Display);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    static void WriteEndpoint(
        Utf8JsonWriter writer,
        string propertyName,
        ComparisonSubjectEndpoint endpoint)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("identifier", endpoint.Identifier);
        writer.WriteString("display", endpoint.Display);
        writer.WriteEndObject();
    }

    static ComparisonDocument<T> ReadDocument<T>(
        JsonElement element,
        JsonTypeInfo<T> payloadTypeInfo)
        where T : notnull
    {
        Dictionary<string, JsonElement> properties =
            ReadObject(element, DocumentPropertyNames, "$");
        int schemaVersion = ReadInt32(
            Required(properties, "schema_version", "$"),
            "$.schema_version");
        SubjectCoordinateBasis basis = ReadCoordinateBasis(
            Required(properties, "subject_coordinate_basis", "$"));
        string identifier = ReadString(
            Required(properties, "identifier", "$"),
            "$.identifier");
        string display = ReadString(
            Required(properties, "display", "$"),
            "$.display");
        ComparisonSubjectChange change = ReadChange(properties, "$");
        ComparisonRootComparison<T> comparison =
            properties.TryGetValue("comparison", out JsonElement comparisonElement)
                ? new ComparisonRootComparison<T>.Present(
                    ReadPayload(comparisonElement, payloadTypeInfo, "$.comparison"))
                : new ComparisonRootComparison<T>.NotApplicable();
        ImmutableArray<ComparisonSubject<T>> subjects = ReadSubjects(
            Required(properties, "subjects", "$"),
            payloadTypeInfo);
        ImmutableArray<ComparisonChangeDescription> descriptions = ReadDescriptions(
            Required(properties, "change_descriptions", "$"));

        return new ComparisonDocument<T>(
            schemaVersion,
            basis,
            identifier,
            display,
            change,
            comparison,
            subjects,
            descriptions);
    }

    static ImmutableArray<ComparisonSubject<T>> ReadSubjects<T>(
        JsonElement element,
        JsonTypeInfo<T> payloadTypeInfo)
        where T : notnull
    {
        EnsureKind(element, JsonValueKind.Array, "$.subjects");
        var subjects = ImmutableArray.CreateBuilder<ComparisonSubject<T>>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string path = $"$.subjects[{index}]";
            Dictionary<string, JsonElement> properties =
                ReadObject(item, SubjectPropertyNames, path);
            subjects.Add(
                new ComparisonSubject<T>(
                    ReadString(
                        Required(properties, "identifier", path),
                        $"{path}.identifier"),
                    ReadString(
                        Required(properties, "display", path),
                        $"{path}.display"),
                    ReadChange(properties, path),
                    ReadPayload(
                        Required(properties, "comparison", path),
                        payloadTypeInfo,
                        $"{path}.comparison")));
            index++;
        }
        return subjects.ToImmutable();
    }

    static ImmutableArray<ComparisonChangeDescription> ReadDescriptions(
        JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "$.change_descriptions");
        var descriptions = ImmutableArray.CreateBuilder<ComparisonChangeDescription>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string path = $"$.change_descriptions[{index}]";
            Dictionary<string, JsonElement> properties =
                ReadObject(item, DescriptionPropertyNames, path);
            descriptions.Add(
                new ComparisonChangeDescription(
                    ReadString(
                        Required(properties, "id", path),
                        $"{path}.id"),
                    ReadExceptionalKind(
                        Required(properties, "change_kinds", path),
                        $"{path}.change_kinds"),
                    ReadEndpoint(
                        Required(properties, "before", path),
                        $"{path}.before"),
                    ReadEndpoint(
                        Required(properties, "after", path),
                        $"{path}.after"),
                    ReadTransformations(
                        Required(properties, "transformations", path),
                        $"{path}.transformations")));
            index++;
        }
        return descriptions.ToImmutable();
    }

    static ComparisonSubjectEndpoint ReadEndpoint(
        JsonElement element,
        string path)
    {
        Dictionary<string, JsonElement> properties =
            ReadObject(element, EndpointPropertyNames, path);
        return new ComparisonSubjectEndpoint(
            ReadString(
                Required(properties, "identifier", path),
                $"{path}.identifier"),
            ReadString(
                Required(properties, "display", path),
                $"{path}.display"));
    }

    static ImmutableArray<ComparisonTransformationDescriptor> ReadTransformations(
        JsonElement element,
        string path)
    {
        EnsureKind(element, JsonValueKind.Array, path);
        var transformations =
            ImmutableArray.CreateBuilder<ComparisonTransformationDescriptor>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string itemPath = $"{path}[{index}]";
            Dictionary<string, JsonElement> properties =
                ReadObject(item, TransformationPropertyNames, itemPath);
            transformations.Add(
                new ComparisonTransformationDescriptor(
                    ReadString(
                        Required(properties, "identifier", itemPath),
                        $"{itemPath}.identifier"),
                    ReadString(
                        Required(properties, "display", itemPath),
                        $"{itemPath}.display")));
            index++;
        }
        return transformations.ToImmutable();
    }

    static ComparisonSubjectChange ReadChange(
        Dictionary<string, JsonElement> properties,
        string path)
    {
        bool hasKinds = properties.TryGetValue("change_kinds", out JsonElement kinds);
        bool hasChangeId = properties.TryGetValue("change_id", out JsonElement changeId);
        if (!hasKinds)
        {
            if (hasChangeId)
            {
                throw new JsonException(
                    $"{path}.change_id is not valid when change_kinds is omitted.");
            }
            return new ComparisonSubjectChange.Diff();
        }

        string[] values = ReadKindStrings(kinds, $"{path}.change_kinds");
        if (values is ["addition"])
        {
            RejectChangeId(hasChangeId, path);
            return new ComparisonSubjectChange.Addition();
        }
        if (values is ["deletion"])
        {
            RejectChangeId(hasChangeId, path);
            return new ComparisonSubjectChange.Deletion();
        }
        if (values is not (["rename"] or ["move"] or ["rename", "move"]))
        {
            throw new JsonException(
                $"{path}.change_kinds is not a canonical comparison change.");
        }

        string id = ReadString(
            hasChangeId
                ? changeId
                : throw new JsonException(
                    $"{path}.change_id is required for rename and move changes."),
            $"{path}.change_id");
        if (values is ["rename"])
            return new ComparisonSubjectChange.Rename(id);
        if (values is ["move"])
            return new ComparisonSubjectChange.Move(id);
        return new ComparisonSubjectChange.RenameAndMove(id);
    }

    static void RejectChangeId(bool hasChangeId, string path)
    {
        if (hasChangeId)
        {
            throw new JsonException(
                $"{path}.change_id is valid only for rename and move changes.");
        }
    }

    static ComparisonExceptionalChangeKind ReadExceptionalKind(
        JsonElement element,
        string path)
        => ReadKindStrings(element, path) switch
        {
            ["rename"] => ComparisonExceptionalChangeKind.Rename,
            ["move"] => ComparisonExceptionalChangeKind.Move,
            ["rename", "move"] => ComparisonExceptionalChangeKind.RenameAndMove,
            _ => throw new JsonException(
                $"{path} must contain rename, move, or rename followed by move."),
        };

    static string[] ReadKindStrings(JsonElement element, string path)
    {
        EnsureKind(element, JsonValueKind.Array, path);
        var values = new List<string>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            values.Add(ReadString(item, $"{path}[{index}]"));
            index++;
        }
        return [.. values];
    }

    static SubjectCoordinateBasis ReadCoordinateBasis(JsonElement element)
        => ReadString(element, "$.subject_coordinate_basis") switch
        {
            "outer-context" => SubjectCoordinateBasis.OuterContext,
            "root-relative" => SubjectCoordinateBasis.RootRelative,
            _ => throw new JsonException(
                "$.subject_coordinate_basis has an unknown value."),
        };

    static T ReadPayload<T>(
        JsonElement element,
        JsonTypeInfo<T> payloadTypeInfo,
        string path)
        where T : notnull
    {
        if (element.ValueKind == JsonValueKind.Null)
            throw new JsonException($"{path} must not be null.");

        T? payload = element.Deserialize(payloadTypeInfo);
        return payload is not null
            ? payload
            : throw new JsonException($"{path} deserialized to null.");
    }

    static Dictionary<string, JsonElement> ReadObject(
        JsonElement element,
        HashSet<string> allowedNames,
        string path)
    {
        EnsureKind(element, JsonValueKind.Object, path);
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name))
                throw new JsonException($"{path} contains unknown property '{property.Name}'.");
            if (!properties.TryAdd(property.Name, property.Value))
                throw new JsonException($"{path} repeats property '{property.Name}'.");
        }
        return properties;
    }

    static JsonElement Required(
        Dictionary<string, JsonElement> properties,
        string name,
        string path)
        => properties.TryGetValue(name, out JsonElement value)
            ? value
            : throw new JsonException($"{path} is missing required property '{name}'.");

    static string ReadString(JsonElement element, string path)
    {
        EnsureKind(element, JsonValueKind.String, path);
        return element.GetString()
            ?? throw new JsonException($"{path} must not be null.");
    }

    static int ReadInt32(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int value))
        {
            throw new JsonException($"{path} must be a 32-bit integer.");
        }
        return value;
    }

    static void EnsureKind(
        JsonElement element,
        JsonValueKind expected,
        string path)
    {
        if (element.ValueKind != expected)
        {
            throw new JsonException(
                $"{path} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }
}
