using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.Tests;

internal sealed record ControlPropertyNameFixture
{
    [JsonPropertyName("line\nbreak\r\t\u0001")]
    public string Value { get; init; } = "";
}

[JsonSerializable(typeof(ControlPropertyNameFixture))]
internal sealed partial class ControlPropertyNameFixtureJsonContext : JsonSerializerContext;
