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

[JsonSerializable(typeof(ControlFieldPropertyNameFixture))]
internal sealed partial class ControlPropertyNameFixtureJsonContext : JsonSerializerContext;
