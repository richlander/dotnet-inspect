using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Inspector.Findings;

namespace DotnetInspector.Presentation;

/// <summary>NativeAOT-safe structured serialization for Library API diff outcomes.</summary>
[JsonSourceGenerationOptions(
    Converters =
    [
        typeof(InertStringJsonConverter),
        typeof(LibraryApiDiffOutcomeJsonConverter),
        typeof(LibraryApiDiffEndpointIssueJsonConverter),
        typeof(LibraryApiTypeComparisonDocumentJsonConverter),
    ],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LibraryApiDiffOutcome))]
[JsonSerializable(typeof(LibraryApiDiffDocument))]
[JsonSerializable(typeof(LibraryApiDiffEndpointSummary))]
[JsonSerializable(typeof(LibraryApiDiffInspectionFailure))]
[JsonSerializable(typeof(LibraryApiTypeDiff))]
[JsonSerializable(typeof(ApiSurfaceProjectionTruncation))]
public sealed partial class LibraryApiDiffJsonContext : JsonSerializerContext;

sealed class LibraryApiDiffOutcomeJsonConverter
    : JsonConverter<LibraryApiDiffOutcome>
{
    public override LibraryApiDiffOutcome Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument parsed = JsonDocument.ParseValue(ref reader);
        JsonElement root = parsed.RootElement;
        string outcome = RequiredString(root, "outcome");
        return outcome switch
        {
            "available" => new LibraryApiDiffOutcome.Available(
                Required(
                    root,
                    "document",
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffDocument)),
            "unavailable" => new LibraryApiDiffOutcome.Unavailable(
                RequiredEnum<LibraryApiDiffUnavailableKind>(root, "kind"),
                Required(
                    root,
                    "before",
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffEndpointSummary),
                Required(
                    root,
                    "after",
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffEndpointSummary)),
            "rejected" => new LibraryApiDiffOutcome.Rejected(
                RequiredEnum<LibraryApiDiffRejectionKind>(root, "kind"),
                Required(
                    root,
                    "before",
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffEndpointSummary),
                Required(
                    root,
                    "after",
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffEndpointSummary)),
            _ => throw new JsonException(
                $"Unknown Library API diff outcome '{outcome}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        LibraryApiDiffOutcome value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case LibraryApiDiffOutcome.Available available:
                writer.WriteString("outcome", "available");
                writer.WritePropertyName("document");
                JsonSerializer.Serialize(
                    writer,
                    available.Document,
                    LibraryApiDiffJsonContext.Default.LibraryApiDiffDocument);
                break;
            case LibraryApiDiffOutcome.Unavailable unavailable:
                writer.WriteString("outcome", "unavailable");
                writer.WriteNumber("kind", (int)unavailable.Kind);
                WriteEndpoint(writer, "before", unavailable.Before);
                WriteEndpoint(writer, "after", unavailable.After);
                break;
            case LibraryApiDiffOutcome.Rejected rejected:
                writer.WriteString("outcome", "rejected");
                writer.WriteNumber("kind", (int)rejected.Kind);
                WriteEndpoint(writer, "before", rejected.Before);
                WriteEndpoint(writer, "after", rejected.After);
                break;
            default:
                throw new JsonException("Unknown Library API diff outcome.");
        }
        writer.WriteEndObject();
    }

    static void WriteEndpoint(
        Utf8JsonWriter writer,
        string propertyName,
        LibraryApiDiffEndpointSummary endpoint)
    {
        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(
            writer,
            endpoint,
            LibraryApiDiffJsonContext.Default.LibraryApiDiffEndpointSummary);
    }

    internal static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                $"Library API diff JSON requires string property '{propertyName}'.");
        }
        return property.GetString()!;
    }

    internal static T Required<T>(
        JsonElement root,
        string propertyName,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : notnull
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new JsonException(
                $"Library API diff JSON requires property '{propertyName}'.");
        }
        return property.Deserialize(typeInfo)
            ?? throw new JsonException(
                $"Library API diff property '{propertyName}' must not be null.");
    }

    internal static TEnum RequiredEnum<TEnum>(
        JsonElement root,
        string propertyName)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int value)
            || !Enum.IsDefined(typeof(TEnum), value))
        {
            throw new JsonException(
                $"Library API diff JSON requires a known numeric '{propertyName}'.");
        }
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }
}

sealed class LibraryApiDiffEndpointIssueJsonConverter
    : JsonConverter<LibraryApiDiffEndpointIssue>
{
    public override LibraryApiDiffEndpointIssue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument parsed = JsonDocument.ParseValue(ref reader);
        JsonElement root = parsed.RootElement;
        string issue = LibraryApiDiffOutcomeJsonConverter.RequiredString(
            root,
            "issue");
        return issue switch
        {
            "truncated" => new LibraryApiDiffEndpointIssue.Truncated(
                LibraryApiDiffOutcomeJsonConverter.Required(
                    root,
                    "truncation",
                    LibraryApiDiffJsonContext.Default.ApiSurfaceProjectionTruncation)),
            "rejected" => new LibraryApiDiffEndpointIssue.Rejected(
                LibraryApiDiffOutcomeJsonConverter
                    .RequiredEnum<ILInspector.Metadata.CandidateOpenFailureKind>(
                        root,
                        "kind"),
                ReadInertString(root, "detail"),
                ReadNullableEnum<ILInspector.Metadata.MetadataRootMalformedReason>(
                    root,
                    "metadataRootReason")),
            "failed" => new LibraryApiDiffEndpointIssue.Failed(
                ReadInertString(root, "detail")),
            "inspectionFailures" => ReadInspectionFailures(root),
            "degradedSignatures" => new LibraryApiDiffEndpointIssue.DegradedSignatures(
                RequiredInt32(root, "count")),
            "unexpectedAssemblyPopulation" =>
                new LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation(
                    RequiredInt32(root, "count")),
            _ => throw new JsonException(
                $"Unknown Library API diff endpoint issue '{issue}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        LibraryApiDiffEndpointIssue value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case LibraryApiDiffEndpointIssue.Truncated truncated:
                writer.WriteString("issue", "truncated");
                writer.WritePropertyName("truncation");
                JsonSerializer.Serialize(
                    writer,
                    truncated.Truncation,
                    LibraryApiDiffJsonContext.Default.ApiSurfaceProjectionTruncation);
                break;
            case LibraryApiDiffEndpointIssue.Rejected rejected:
                writer.WriteString("issue", "rejected");
                writer.WriteNumber("kind", (int)rejected.Kind);
                writer.WriteString("detail", rejected.Detail.ToString());
                if (rejected.MetadataRootReason is { } metadataRootReason)
                {
                    writer.WriteNumber(
                        "metadataRootReason",
                        (int)metadataRootReason);
                }
                else
                {
                    writer.WriteNull("metadataRootReason");
                }
                break;
            case LibraryApiDiffEndpointIssue.Failed failed:
                writer.WriteString("issue", "failed");
                writer.WriteString("detail", failed.Detail.ToString());
                break;
            case LibraryApiDiffEndpointIssue.InspectionFailures failures:
                writer.WriteString("issue", "inspectionFailures");
                writer.WriteNumber("count", failures.Count);
                writer.WritePropertyName("details");
                writer.WriteStartArray();
                foreach (LibraryApiDiffInspectionFailure detail in failures.Details)
                {
                    JsonSerializer.Serialize(
                        writer,
                        detail,
                        LibraryApiDiffJsonContext.Default.LibraryApiDiffInspectionFailure);
                }
                writer.WriteEndArray();
                break;
            case LibraryApiDiffEndpointIssue.DegradedSignatures signatures:
                writer.WriteString("issue", "degradedSignatures");
                writer.WriteNumber("count", signatures.Count);
                break;
            case LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation population:
                writer.WriteString("issue", "unexpectedAssemblyPopulation");
                writer.WriteNumber("count", population.Count);
                break;
            default:
                throw new JsonException(
                    "Unknown Library API diff endpoint issue.");
        }
        writer.WriteEndObject();
    }

    static LibraryApiDiffEndpointIssue.InspectionFailures ReadInspectionFailures(
        JsonElement root)
    {
        int count = RequiredInt32(root, "count");
        if (!root.TryGetProperty("details", out JsonElement details)
            || details.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Library API diff inspection failures require array property 'details'.");
        }

        var builder =
            System.Collections.Immutable.ImmutableArray
                .CreateBuilder<LibraryApiDiffInspectionFailure>();
        foreach (JsonElement detail in details.EnumerateArray())
        {
            builder.Add(
                detail.Deserialize(
                    LibraryApiDiffJsonContext.Default
                        .LibraryApiDiffInspectionFailure)
                ?? throw new JsonException(
                    "A Library API diff inspection failure must not be null."));
        }
        return new LibraryApiDiffEndpointIssue.InspectionFailures(count)
        {
            Details = builder.ToImmutable(),
        };
    }

    static InertText.InertString ReadInertString(
        JsonElement root,
        string propertyName)
    {
        string value =
            LibraryApiDiffOutcomeJsonConverter.RequiredString(root, propertyName);
        return InertText.InertString.FromEncoded(
            InertText.TextPolicy.Field,
            value);
    }

    static int RequiredInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int value))
        {
            throw new JsonException(
                $"Library API diff JSON requires integer property '{propertyName}'.");
        }
        return value;
    }

    static TEnum? ReadNullableEnum<TEnum>(
        JsonElement root,
        string propertyName)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            throw new JsonException(
                $"Library API diff JSON requires property '{propertyName}'.");
        }
        if (property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int value)
            || !Enum.IsDefined(typeof(TEnum), value))
        {
            throw new JsonException(
                $"Library API diff JSON requires a known numeric or null '{propertyName}'.");
        }
        return (TEnum)Enum.ToObject(typeof(TEnum), value);
    }
}

sealed class LibraryApiTypeComparisonDocumentJsonConverter
    : JsonConverter<ComparisonDocument<LibraryApiTypeDiff>>
{
    public override ComparisonDocument<LibraryApiTypeDiff> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument parsed = JsonDocument.ParseValue(ref reader);
        return ComparisonDocumentJson.Deserialize(
            parsed.RootElement.GetRawText(),
            LibraryApiDiffJsonContext.Default.LibraryApiTypeDiff);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ComparisonDocument<LibraryApiTypeDiff> value,
        JsonSerializerOptions options)
    {
        string json = ComparisonDocumentJson.Serialize(
            value,
            LibraryApiDiffJsonContext.Default.LibraryApiTypeDiff);
        using JsonDocument parsed = JsonDocument.Parse(json);
        parsed.RootElement.WriteTo(writer);
    }
}
