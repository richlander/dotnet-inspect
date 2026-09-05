using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextFixtures;

public sealed record SimpleDto(string Value);

public sealed partial class CrossContextTopDto
{
    public string Public { get; set; } = "";

#pragma warning disable CS0414
    [JsonInclude]
    private HiddenValue Hidden = HiddenValue.Value;
#pragma warning restore CS0414

    private enum HiddenValue
    {
        Value,
    }

    [JsonSerializable(typeof(CrossContextNestedDto))]
    internal sealed partial class CrossContextNestedJsonContext
        : JsonSerializerContext;
}

public sealed record CrossContextNestedDto(int Value);

[JsonSerializable(typeof(SimpleDto))]
[JsonSerializable(typeof(CrossContextTopDto))]
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

    [JSExport]
    public static int ReadThroughDifferentContexts(
        string first,
        string second)
    {
        CrossContextTopDto top = JsonSerializer.Deserialize(
            first,
            TopLevelFixtureJsonContext.Default.CrossContextTopDto)!;
        CrossContextNestedDto nested = JsonSerializer.Deserialize(
            second,
            CrossContextTopDto.CrossContextNestedJsonContext.Default
                .CrossContextNestedDto)!;
        return top.Public.Length + nested.Value;
    }
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
