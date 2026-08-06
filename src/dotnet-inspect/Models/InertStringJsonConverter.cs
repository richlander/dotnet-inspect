using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Models;

/// <summary>
/// Writes a contained string as a JSON string at the serializer boundary.
/// </summary>
internal sealed class InertStringJsonConverter : JsonConverter<InertString?>
{
    public override InertString? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException("Inspection results are output-only.");

    public override void Write(
        Utf8JsonWriter writer,
        InertString? value,
        JsonSerializerOptions options)
    {
        if (value is { } text)
            writer.WriteStringValue(text.ToString());
        else
            writer.WriteNullValue();
    }
}
