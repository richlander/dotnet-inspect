using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Output;

/// <summary>
/// Writes an <see cref="InertString"/> as its encoded text so adopting the typed currency does
/// not change the established JSON string shape.
/// </summary>
/// <remarks>
/// Gated by <c>DependencyNode_CarriesInertTextThroughMarkdownAndJsonSinks</c>.
/// </remarks>
internal sealed class InertStringJsonConverter : JsonConverter<InertString>
{
    public override InertString Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException("The inspection JSON context is output-only.");

    public override void Write(
        Utf8JsonWriter writer,
        InertString value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
