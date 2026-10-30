using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public static class TypeSignatureInspectionJson
{
    public static JsonElement SerializeContent(
        TypeSignatureInspectionOutcome content) =>
        JsonSerializer.SerializeToElement(
            content,
            TypeSignatureInspectionJsonContext.Default.TypeSignatureInspectionOutcome);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(TypeSignatureNameJsonConverter),
        typeof(TypeSignatureTypeJsonConverter)])]
[JsonSerializable(typeof(TypeSignatureInspectionOutcome))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Primitive))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Named))]
[JsonSerializable(typeof(MetadataTypeSignatureType.GenericInstance))]
[JsonSerializable(typeof(MetadataTypeSignatureType.TypeParameter))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Array))]
[JsonSerializable(typeof(MetadataTypeSignatureType.SzArray))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Pointer))]
[JsonSerializable(typeof(MetadataTypeSignatureType.ByReference))]
[JsonSerializable(typeof(MetadataTypeSignatureType.FunctionPointer))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Modified))]
[JsonSerializable(typeof(MetadataTypeSignatureType.Pinned))]
internal sealed partial class TypeSignatureInspectionJsonContext : JsonSerializerContext;

internal sealed class TypeSignatureNameJsonConverter : JsonConverter<MetadataUtf8Name>
{
    public override MetadataUtf8Name Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Transported Signature names do not confer Metadata authority.");

    public override void Write(
        Utf8JsonWriter writer,
        MetadataUtf8Name value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Contain(TextPolicy.Field).ToString());
}

internal sealed class TypeSignatureTypeJsonConverter
    : JsonConverter<MetadataTypeSignatureType>
{
    public override MetadataTypeSignatureType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Signature transport is a detached presentation, not Metadata input.");

    public override void Write(
        Utf8JsonWriter writer,
        MetadataTypeSignatureType value,
        JsonSerializerOptions options)
    {
        TypeSignatureInspectionJsonContext context =
            TypeSignatureInspectionJsonContext.Default;
        JsonElement element = value switch
        {
            MetadataTypeSignatureType.Primitive item =>
                JsonSerializer.SerializeToElement(item, context.Primitive),
            MetadataTypeSignatureType.Named item =>
                JsonSerializer.SerializeToElement(item, context.Named),
            MetadataTypeSignatureType.GenericInstance item =>
                JsonSerializer.SerializeToElement(item, context.GenericInstance),
            MetadataTypeSignatureType.TypeParameter item =>
                JsonSerializer.SerializeToElement(item, context.TypeParameter),
            MetadataTypeSignatureType.Array item =>
                JsonSerializer.SerializeToElement(item, context.Array),
            MetadataTypeSignatureType.SzArray item =>
                JsonSerializer.SerializeToElement(item, context.SzArray),
            MetadataTypeSignatureType.Pointer item =>
                JsonSerializer.SerializeToElement(item, context.Pointer),
            MetadataTypeSignatureType.ByReference item =>
                JsonSerializer.SerializeToElement(item, context.ByReference),
            MetadataTypeSignatureType.FunctionPointer item =>
                JsonSerializer.SerializeToElement(item, context.FunctionPointer),
            MetadataTypeSignatureType.Modified item =>
                JsonSerializer.SerializeToElement(item, context.Modified),
            MetadataTypeSignatureType.Pinned item =>
                JsonSerializer.SerializeToElement(item, context.Pinned),
            _ => throw new InvalidOperationException("Unknown Signature Type shape."),
        };
        writer.WriteStartObject();
        writer.WriteString("shape", value.GetType().Name);
        foreach (JsonProperty property in element.EnumerateObject())
            property.WriteTo(writer);
        writer.WriteEndObject();
    }
}
