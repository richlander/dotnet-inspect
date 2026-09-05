using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextUnsupportedFixtures;

#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
[SupportedOSPlatform("browser")]
public partial class NestedContextProtectedValueDto
{
    [JsonInclude]
    private HiddenProtected ProtectedField = HiddenProtected.Value;

    [JsonInclude]
    private HiddenPrivateProtected PrivateProtectedField =
        HiddenPrivateProtected.Value;

    protected enum HiddenProtected
    {
        Value,
    }

    private protected enum HiddenPrivateProtected
    {
        Value,
    }

    [JSExport]
    public static string GetProtectedValues() =>
        JsonSerializer.Serialize(
            new NestedContextProtectedValueDto(),
            NestedContextJsonContext.Default
                .NestedContextProtectedValueDto);

    [JsonSerializable(typeof(NestedContextProtectedValueDto))]
    private sealed partial class NestedContextJsonContext
        : JsonSerializerContext;
}
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414
