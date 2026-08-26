using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.ScalarFixtures;

/// <summary>
/// Uses the generated default <c>Int32</c> property rather than a custom
/// source-generation name. The runtime assertion in the consumer test pins
/// the wire effect of <see cref="JsonNumberHandling.WriteAsString"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class ScalarContextOptionsFixtureExports
{
    [JSExport]
    public static string SerializeWriteAsStringInt() =>
        JsonSerializer.Serialize(
            42,
            UnsupportedScalarContextOptions.Default.Int32);

    [JSExport]
    public static string SerializeVector() =>
        JsonSerializer.Serialize(
            new[] { 1, 2 },
            SupportedScalarContextOptions.Default.Int32Array);

    [JSExport]
    public static string SerializeCustomInstanceInt()
    {
        var options = new JsonSerializerOptions
        {
            NumberHandling = JsonNumberHandling.WriteAsString,
        };
        var context = new SupportedScalarContextOptions(options);
        return JsonSerializer.Serialize(
            42,
            context.Int32);
    }

    [JSExport]
    public static string SerializeExtraStaticsInt() =>
        JsonSerializer.Serialize(
            42,
            ExtraStaticsScalarContext.Default.Int32);
}

[JsonSerializable(typeof(int))]
[JsonSourceGenerationOptions(
    NumberHandling = JsonNumberHandling.WriteAsString)]
public sealed partial class UnsupportedScalarContextOptions
    : JsonSerializerContext;

/// <summary>
/// Deliberately has no serializer call. Unsupported context options must not
/// affect a different registered root merely because this context is present.
/// </summary>
[JsonSerializable(typeof(int))]
[JsonSourceGenerationOptions(
    NumberHandling = JsonNumberHandling.WriteAsString)]
public sealed partial class UnusedUnsupportedScalarContextOptions
    : JsonSerializerContext;

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int[]))]
public sealed partial class SupportedScalarContextOptions
    : JsonSerializerContext;

[JsonSerializable(typeof(int))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
public sealed partial class UnsupportedWebDefaultsContext
    : JsonSerializerContext;

/// <summary>
/// A source-generated context whose user-written partial adds an unrelated
/// static <see cref="JsonSerializerOptions"/>. The compiler merges that
/// initializer into the same generated <c>.cctor</c>, so the default-instance
/// chain has to be authenticated by following its links rather than by counting
/// constructors in the body.
/// </summary>
[JsonSerializable(typeof(int))]
public sealed partial class ExtraStaticsScalarContext : JsonSerializerContext
{
    public static readonly JsonSerializerOptions Extra = new();
}
