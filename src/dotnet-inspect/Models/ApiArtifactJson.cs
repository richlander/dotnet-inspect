using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Models;

/// <summary>
/// JSON contexts that visually encode every semantic API string before the
/// structural serializer writes it.
/// </summary>
/// <remarks>
/// <c>SemanticTypeOutputContainmentTests</c> gates decoded values and schema
/// neutrality; the metadata-confusion case in <c>PackageFixtureTests</c> is the
/// real-artifact gate.
/// </remarks>
internal static class ApiArtifactJson
{
    public static ApiJsonContext SurfaceContext { get; } =
        new(CreateOptions(ApiJsonContext.Default.Options));

    public static ApiTypeJsonContext TypeContext { get; } =
        new(CreateOptions(ApiTypeJsonContext.Default.Options));

    public static ApiTypeCompactJsonContext CompactTypeContext { get; } =
        new(CreateOptions(ApiTypeCompactJsonContext.Default.Options));

    private static JsonSerializerOptions CreateOptions(
        JsonSerializerOptions baseline)
    {
        var options = new JsonSerializerOptions(baseline);
        options.Converters.Insert(
            0,
            ApiArtifactMetadataTypeNameJsonConverter.Instance);
        options.Converters.Insert(0, ApiArtifactStringJsonConverter.Instance);
        return options;
    }
}

/// <summary>
/// Output-only conversion from untreated API model strings to inert JSON
/// string values.
/// </summary>
internal sealed class ApiArtifactStringJsonConverter : JsonConverter<string>
{
    public static ApiArtifactStringJsonConverter Instance { get; } = new();

    public override string Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            new InertString(TextPolicy.Field, value).ToString());
}

internal sealed class ApiArtifactMetadataTypeNameJsonConverter
    : JsonConverter<MetadataTypeDefinitionName>
{
    public static ApiArtifactMetadataTypeNameJsonConverter Instance { get; } =
        new();

    public override MetadataTypeDefinitionName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        MetadataTypeDefinitionName value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(
            "namespace",
            new InertString(TextPolicy.Field, value.Namespace).ToString());
        writer.WriteStartArray("segments");
        foreach (string segment in value.Segments)
        {
            writer.WriteStringValue(
                new InertString(TextPolicy.Field, segment).ToString());
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
