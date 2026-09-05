using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextFixtures;

public sealed record SimpleDto(string Value);

[JsonSerializable(typeof(SimpleDto))]
internal sealed partial class TopLevelFixtureJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class NestedContextFixtureExports
{
    [JSExport]
    public static string GetSimple() =>
        JsonSerializer.Serialize(
            new SimpleDto("simple"),
            TopLevelFixtureJsonContext.Default.SimpleDto);
}

#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
[SupportedOSPlatform("browser")]
public partial class NestedContextSafeDto
{
    public string Public { get; set; } = "public";

    [JsonInclude]
    [JsonIgnore]
    private HiddenValue Ignored = HiddenValue.Value;

    [JsonInclude]
    private static HiddenValue Shared = HiddenValue.Value;

    [JsonInclude]
    private HiddenValue this[int index]
    {
        get => HiddenValue.Value;
        set
        {
        }
    }

    private enum HiddenValue
    {
        Value,
    }

    [JSExport]
    public static string GetNestedSafe() =>
        JsonSerializer.Serialize(
            new NestedContextSafeDto(),
            NestedContextJsonContext.Default.NestedContextSafeDto);

    [JsonSerializable(typeof(NestedContextSafeDto))]
    private sealed partial class NestedContextJsonContext
        : JsonSerializerContext;
}

internal sealed partial class UnreachedNestedContextDto
{
    [JsonInclude]
    private HiddenValue Hidden = HiddenValue.Value;

    private enum HiddenValue
    {
        Value,
    }

    [JsonSerializable(typeof(UnreachedNestedContextDto))]
    private sealed partial class UnreachedNestedContextJsonContext
        : JsonSerializerContext;
}
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414
