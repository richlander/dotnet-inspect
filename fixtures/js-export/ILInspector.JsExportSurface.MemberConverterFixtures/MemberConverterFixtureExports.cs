using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.MemberConverterFixtures;

public sealed class ConverterControlledDto
{
    [JsonConverter(typeof(JsonStringEnumConverter<Status>))]
    public Status Value { get; set; } = Status.One;
}

public enum Status
{
    One,
    Two,
}

[JsonSerializable(typeof(ConverterControlledDto))]
internal sealed partial class ConverterFixtureJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class MemberConverterFixtureExports
{
    [JSExport]
    public static string GetValue() =>
        JsonSerializer.Serialize(
            new ConverterControlledDto(),
            ConverterFixtureJsonContext.Default.ConverterControlledDto);
}
