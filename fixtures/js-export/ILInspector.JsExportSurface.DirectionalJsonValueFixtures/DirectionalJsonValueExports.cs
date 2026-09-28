using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using TsJsExport;

namespace ILInspector.JsExportSurface.DirectionalJsonValueFixtures;

public sealed record ConditionalJsonValueDto(
    [property: JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingDefault)]
    JsonElement Payload);

[JsonSerializable(typeof(ConditionalJsonValueDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ConditionalJsonValueContext
    : JsonSerializerContext;

[JsExportRoot(typeof(DirectionalJsonValueExports))]
public sealed class ConditionalJsonValueExportContext;

[SupportedOSPlatform("browser")]
public static partial class DirectionalJsonValueExports
{
    [JSExport]
    public static string ReemitConditionalJsonValue(string payloadJson)
    {
        ConditionalJsonValueDto payload = JsonSerializer.Deserialize(
            payloadJson,
            ConditionalJsonValueContext.Default.ConditionalJsonValueDto)!;
        return JsonSerializer.Serialize(
            payload,
            ConditionalJsonValueContext.Default.ConditionalJsonValueDto);
    }
}
