using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.JsExportSurface.PolymorphicContractsFixtures;

namespace ILInspector.JsExportSurface.UnsupportedPolymorphicExportFixtures;

[JsonSerializable(typeof(NumericOutcome))]
public sealed partial class UnsupportedPolymorphicJsonContext
    : JsonSerializerContext;

public static partial class UnsupportedPolymorphicExports
{
    [JSExport]
    public static string GetNumericOutcome() =>
        JsonSerializer.Serialize<NumericOutcome>(
            new NumericOutcome.Value(42),
            UnsupportedPolymorphicJsonContext.Default.NumericOutcome);
}
