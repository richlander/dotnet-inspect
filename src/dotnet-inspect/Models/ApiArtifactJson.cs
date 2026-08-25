using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ILInspector.Metadata;
using InertText;
using DotnetInspector.Output;

namespace DotnetInspector.Models;

/// <summary>
/// JSON write projections that visually encode every semantic API string
/// before the structural serializer writes it.
/// </summary>
/// <remarks>
/// <c>SemanticTypeOutputContainmentTests</c> gates decoded values and schema
/// neutrality; the metadata-confusion case in <c>PackageFixtureTests</c> is the
/// real-artifact gate.
/// </remarks>
internal static class ApiArtifactJson
{
    public static JsonTypeInfo<ApiSurface> Surface { get; } =
        CreateTypeInfo<ApiSurface>(ApiJsonContext.Default.Options);

    public static JsonTypeInfo<ApiType> Type { get; } =
        CreateTypeInfo<ApiType>(ApiTypeJsonContext.Default.Options);

    public static JsonTypeInfo<ApiType> CompactType { get; } =
        CreateTypeInfo<ApiType>(
            ApiTypeCompactJsonContext.Default.Options);

    private static JsonTypeInfo<T> CreateTypeInfo<T>(
        JsonSerializerOptions baseline)
    {
        JsonSerializerOptions options = CreateOptions(baseline);
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    private static JsonSerializerOptions CreateOptions(
        JsonSerializerOptions baseline)
    {
        var options = new JsonSerializerOptions(baseline);
        options.Converters.Insert(
            0,
            ApiArtifactMetadataTypeNameJsonConverter.Instance);
        options.Converters.Insert(0, ApiArtifactStringJsonConverter.Instance);
        options.TypeInfoResolver = options.TypeInfoResolver!
            .WithAddedModifier(ConfigurePropertyConverters);
        return options;
    }

    private static void ConfigurePropertyConverters(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(DocComment))
        {
            SetEncodedStrings(typeInfo, "summary", "remarks", "returns");
        }
        else if (typeInfo.Type == typeof(TypeParameter))
        {
            SetCSharpStrings(
                typeInfo,
                "display_name",
                "constraints_summary");
            SetCSharpStringLists(typeInfo, "constraints");
        }
        else if (typeInfo.Type == typeof(ApiType))
        {
            SetCSharpStrings(
                typeInfo,
                "enum_underlying_type",
                "base_type");
            SetCSharpStringLists(
                typeInfo,
                "attributes",
                "interfaces",
                "derived_types");
        }
        else if (typeInfo.Type == typeof(ApiMember))
        {
            SetCSharpStrings(
                typeInfo,
                "return_type",
                "signature",
                "extended_type",
                "declaring_type");
            SetCSharpStringLists(typeInfo, "attributes");
        }
        else if (typeInfo.Type == typeof(ApiSignature))
        {
            SetCSharpStrings(
                typeInfo,
                "return_type",
                "canonical_return_type",
                "parameter_types_summary",
                "canonical_parameter_types_summary",
                "effective_canonical_return_type",
                "public_accessors_summary");
            SetCSharpStringLists(typeInfo, "return_attributes");
        }
        else if (typeInfo.Type == typeof(ApiParameter))
        {
            SetCSharpStrings(
                typeInfo,
                "type",
                "canonical_type",
                "default_value_text",
                "type_with_modifier",
                "effective_canonical_type",
                "canonical_type_with_modifier");
            SetCSharpStringLists(typeInfo, "attributes");
        }
        else if (typeInfo.Type == typeof(ApiAccessor))
        {
            SetCSharpStringLists(typeInfo, "return_attributes");
        }
    }

    private static void SetCSharpStrings(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactCSharpStringJsonConverter.Instance);
        }
    }

    private static void SetEncodedStrings(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactEncodedStringJsonConverter.Instance);
        }
    }

    private static void SetCSharpStringLists(
        JsonTypeInfo typeInfo,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            SetConverter(
                typeInfo,
                propertyName,
                ApiArtifactCSharpStringListJsonConverter.Instance);
        }
    }

    private static void SetConverter(
        JsonTypeInfo typeInfo,
        string propertyName,
        JsonConverter converter)
    {
        JsonPropertyInfo property = typeInfo.Properties.Single(
            property => WireNamesEqual(
                property.Name,
                propertyName));
        property.CustomConverter = converter;
    }

    private static bool WireNamesEqual(
        string left,
        string right)
    {
        int leftIndex = 0;
        int rightIndex = 0;
        while (true)
        {
            while (leftIndex < left.Length && left[leftIndex] == '_')
                leftIndex++;
            while (rightIndex < right.Length && right[rightIndex] == '_')
                rightIndex++;

            if (leftIndex == left.Length || rightIndex == right.Length)
            {
                return leftIndex == left.Length
                    && rightIndex == right.Length;
            }

            if (char.ToUpperInvariant(left[leftIndex])
                != char.ToUpperInvariant(right[rightIndex]))
            {
                return false;
            }

            leftIndex++;
            rightIndex++;
        }
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

internal sealed class ApiArtifactCSharpStringJsonConverter
    : JsonConverter<string>
{
    public static ApiArtifactCSharpStringJsonConverter Instance { get; } =
        new();

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
        writer.WriteStringValue(Contain(value));

    internal static string Contain(string value)
        => ApiPresentationText.CSharpField(value).ToString();
}

internal sealed class ApiArtifactEncodedStringJsonConverter
    : JsonConverter<string>
{
    public static ApiArtifactEncodedStringJsonConverter Instance { get; } =
        new();

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
            ApiPresentationText.EncodedField(value).ToString());
}

internal sealed class ApiArtifactCSharpStringListJsonConverter
    : JsonConverter<List<string>>
{
    public static ApiArtifactCSharpStringListJsonConverter Instance { get; } =
        new();

    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Contained API output is a presentation projection and cannot be read as raw identity.");

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string? item in value)
        {
            if (item is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(
                    ApiArtifactCSharpStringJsonConverter.Contain(item));
        }
        writer.WriteEndArray();
    }
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
