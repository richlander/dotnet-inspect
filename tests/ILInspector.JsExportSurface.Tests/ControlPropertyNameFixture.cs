using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.Tests;

internal sealed record ControlPropertyNameFixture
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("line\nbreak\r\t\u0001")]
    public string Value { get; init; } = "";
}

internal sealed class ControlFieldPropertyNameFixture
{
    [JsonInclude]
    [JsonPropertyName("field\nbreak\r\t\u0001")]
    public string Value = "";
}

internal sealed class BackingFieldControlPropertyNameFixture
{
    [field: JsonPropertyName("backing\nbreak\r\t\u0001")]
    public string Value { get; set; } = "";
}

internal sealed class SafeBackingFieldPropertyNameFixture
{
    [field: JsonPropertyName("not_the_property_name")]
    public string Value { get; set; } = "";
}

internal enum ControlPropertyNameEnumFixture
{
    [JsonPropertyName("enum\nbreak\r\t\u0001")]
    Value,
}

[JsonSerializable(typeof(ControlFieldPropertyNameFixture))]
internal sealed partial class ControlPropertyNameFixtureJsonContext : JsonSerializerContext;
