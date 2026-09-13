using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Core;

public sealed class InertStringJsonConverter : JsonConverter<InertString>
{
    public override InertString Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected an inert string value.");

        return InertString.FromEncoded(
            TextPolicy.Field,
            reader.GetString()!);
    }

    public override void Write(
        Utf8JsonWriter writer,
        InertString value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
