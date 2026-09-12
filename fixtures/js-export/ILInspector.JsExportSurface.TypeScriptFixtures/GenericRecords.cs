using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.TypeScriptFixtures;

public sealed record GenericEnvelope<TContent>(
    TContent Content,
    string Label);

public sealed record T(string Value);

public sealed record GenericCollision<T>(
    T Content,
    global::ILInspector.JsExportSurface.TypeScriptFixtures.T Other)
{
    [JsonInclude]
    public global::ILInspector.JsExportSurface.TypeScriptFixtures.T Included =
        new("included");
}

public sealed record NullableEnvelope<T>(T? Content)
    where T : struct;

public sealed record NullableTextRoot(
    GenericEnvelope<string?> Result);

[JsonSerializable(
    typeof(GenericEnvelope<WidgetDto>),
    TypeInfoPropertyName = "WidgetEnvelope")]
[JsonSerializable(
    typeof(GenericEnvelope<byte[]>),
    TypeInfoPropertyName = "BlobEnvelope")]
[JsonSerializable(
    typeof(GenericEnvelope<string>),
    TypeInfoPropertyName = "DirectNullableTextEnvelope")]
[JsonSerializable(
    typeof(GenericCollision<int>),
    TypeInfoPropertyName = "CollisionEnvelope")]
[JsonSerializable(
    typeof(NullableEnvelope<int>),
    TypeInfoPropertyName = "NullableIntEnvelope")]
[JsonSerializable(typeof(NullableTextRoot))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GenericRecordFixtureJsonContext :
    JsonSerializerContext;
