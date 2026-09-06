using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.TypeScriptFixtures;

public sealed record WidgetDto(string Name, int Count);

public sealed record RuntimeAPI(string Value);

public sealed record BlobDto(
    byte[] Blob,
    byte[]? MaybeBlob,
    byte[]?[] Blobs,
    IReadOnlyDictionary<string, byte[]?> BlobsByName);

public sealed class HiddenTypeJsonIncludeDto
{
    public string Public { get; set; } = "public";

    [JsonInclude]
    private HiddenValue HiddenProperty { get; set; } = HiddenValue.Value;

    [JsonInclude]
    private HiddenValue HiddenField = HiddenValue.Value;

    private enum HiddenValue
    {
        Value,
    }

    public int Read() => (int)HiddenField + (int)HiddenProperty;
}

[JsonSerializable(typeof(WidgetDto))]
[JsonSerializable(typeof(RuntimeAPI))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(HiddenTypeJsonIncludeDto))]
[JsonSerializable(typeof(global::@string), TypeInfoPropertyName = "StringDto")]
[JsonSerializable(typeof(global::@byte), TypeInfoPropertyName = "ByteDto")]
[JsonSerializable(typeof(global::KeywordHolder))]
[JsonSerializable(
    typeof(IReadOnlyDictionary<string, global::@string>),
    TypeInfoPropertyName = "StringDtoMap")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(BlobDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BlobFixtureJsonContext : JsonSerializerContext;

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
    public static string Undefined(string value) => value;

    [JSExport]
    public static string Then(string value) => value;

    [JSExport]
    public static void ObserveValue(
        [JSMarshalAs<JSType.Function<JSType.Number>>]
        Action<int> callback) =>
        callback(42);

    [JSExport]
    public static bool TransformValue(
        [JSMarshalAs<JSType.Function<
            JSType.Number,
            JSType.String,
            JSType.Boolean>>]
        Func<int, string, bool> callback) =>
        callback(42, "answer");

    [JSExport]
    public static string GetJsonElement() =>
        JsonSerializer.Serialize(
            JsonDocument.Parse("""{"value":"json"}""").RootElement,
            FixtureJsonContext.Default.JsonElement);

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
            new global::@string(value),
            FixtureJsonContext.Default.StringDto);
    }

    [JSExport]
    public static async Task<string> GetKeywordHolderAsync(string title)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new global::KeywordHolder(
                title,
                new global::@string(title),
                [new global::@string(title)],
                new Dictionary<string, global::@string>
                {
                    [title] = new(title),
                },
                [new global::@byte(title)]),
            FixtureJsonContext.Default.KeywordHolder);
    }

    [JSExport]
    public static async Task<string> GetKeywordMapAsync(string value)
    {
        await Task.Yield();
        IReadOnlyDictionary<string, global::@string> map =
            new Dictionary<string, global::@string>
            {
                [value] = new(value),
            };
        return JsonSerializer.Serialize(
            map,
            FixtureJsonContext.Default.StringDtoMap);
    }

    [JSExport]
    public static async Task<string> GetBlobAsync()
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new BlobDto(
                [1],
                null,
                [[1], null],
                new Dictionary<string, byte[]?>
                {
                    ["none"] = null,
                }),
            BlobFixtureJsonContext.Default.BlobDto);
    }

    [JSExport]
    public static async Task<string> GetHiddenTypeJsonIncludeAsync()
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new HiddenTypeJsonIncludeDto(),
            FixtureJsonContext.Default.HiddenTypeJsonIncludeDto);
    }

    [JSExport]
    public static async Task<string?> GetNullableWidgetAsync(string name)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new WidgetDto(name, 1),
            FixtureJsonContext.Default.WidgetDto);
    }
}
