using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotnetInspector.JsonSchema;

static class JsonSchemaCanonicalizer
{
    public static byte[] Write(JsonNode node, bool omitRootId = false)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteNode(
                writer,
                node,
                omitRootId,
                isRoot: true);
        }
        return buffer.WrittenSpan.ToArray();
    }

    public static string Digest(ReadOnlySpan<byte> canonicalJson) =>
        $"sha256:{Convert.ToHexString(
            SHA256.HashData(canonicalJson)).ToLowerInvariant()}";

    static void WriteNode(
        Utf8JsonWriter writer,
        JsonNode? node,
        bool omitRootId,
        bool isRoot)
    {
        switch (node)
        {
            case JsonObject value:
                writer.WriteStartObject();
                foreach ((string name, JsonNode? child) in value
                    .Where(property =>
                        !(isRoot
                            && omitRootId
                            && property.Key == "$id"))
                    .OrderBy(
                        property => property.Key,
                        StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    WriteNode(
                        writer,
                        child,
                        omitRootId: false,
                        isRoot: false);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (JsonNode? item in value)
                {
                    WriteNode(
                        writer,
                        item,
                        omitRootId: false,
                        isRoot: false);
                }
                writer.WriteEndArray();
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonSchemaVocabularyException(
                    "canonical JSON",
                    $"unsupported JSON node type '{node.GetType().Name}'");
        }
    }
}
