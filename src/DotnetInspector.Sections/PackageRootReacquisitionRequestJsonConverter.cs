using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed class PackageRootReacquisitionRequestJsonConverter :
    JsonConverter<PackageRootReacquisitionRequest>
{
    public override PackageRootReacquisitionRequest Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !PackageRootReacquisitionRequest.TryDecode(
                reader.GetString(),
                out PackageRootReacquisitionRequest? request))
        {
            throw new JsonException(
                "A Package Root reopening request must be a valid opaque token.");
        }

        return request;
    }

    public override void Write(
        Utf8JsonWriter writer,
        PackageRootReacquisitionRequest value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Encode());
}
