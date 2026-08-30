using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.TypeScriptFixtures;

public sealed record WidgetDto(string Name, int Count);

public sealed record RuntimeAPI(string Value);

public sealed record @string(string Value);

[JsonSerializable(typeof(WidgetDto))]
[JsonSerializable(typeof(RuntimeAPI))]
[JsonSerializable(typeof(@string), TypeInfoPropertyName = "StringDto")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FixtureJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class TypeScriptFixtureExports
{
    [JSExport]
    public static void ConfigureHost(string origin)
    {
    }

    [JSExport]
    public static string Echo(string value) => value;

    [JSExport]
    public static async Task<string> GetWidgetAsync(
        string name,
        int count)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new WidgetDto(name, count),
            FixtureJsonContext.Default.WidgetDto);
    }

    [JSExport]
    public static async Task<string> GetRuntimeApiAsync(string value)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new RuntimeAPI(value),
            FixtureJsonContext.Default.RuntimeAPI);
    }

    [JSExport]
    public static async Task<string> GetStringDtoAsync(string value)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new @string(value),
            FixtureJsonContext.Default.StringDto);
    }
}
