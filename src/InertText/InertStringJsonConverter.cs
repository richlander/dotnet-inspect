using System.Text.Json;
using System.Text.Json.Serialization;

namespace InertText.Json;

/// <summary>
/// Writes an <see cref="InertString"/> as its encoded JSON string representation.
/// </summary>
/// <remarks>
/// Reading is unsupported because the wire value does not carry the policy required by
/// <see cref="InertString.FromEncoded(TextPolicy, string)"/>. A contract that needs to restore
/// an inert value must carry that policy explicitly rather than guessing it here.
/// </remarks>
public sealed class InertStringJsonConverter : JsonConverter<InertString>
{
    /// <inheritdoc />
    public override InertString Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "An inert string cannot be restored without an explicit text policy.");

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        InertString value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
