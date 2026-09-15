using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextConstructorFixtures;

#pragma warning disable SYSLIB1038
[SupportedOSPlatform("browser")]
public partial class NestedContextConstructorBoundDto
{
    [JsonInclude]
    private HiddenValue Hidden { get; }

    [JsonConstructor]
    private NestedContextConstructorBoundDto(HiddenValue hidden)
    {
        Hidden = hidden;
    }

    public int Read() => (int)Hidden;

    [JSExport]
    public static int ReadHidden(string json) =>
        JsonSerializer.Deserialize(
            json,
            NestedContextJsonContext.Default
                .NestedContextConstructorBoundDto)!.Read();

    private enum HiddenValue
    {
        Zero,
        One,
    }

    [JsonSerializable(typeof(NestedContextConstructorBoundDto))]
    private sealed partial class NestedContextJsonContext
        : JsonSerializerContext;
}
#pragma warning restore SYSLIB1038
