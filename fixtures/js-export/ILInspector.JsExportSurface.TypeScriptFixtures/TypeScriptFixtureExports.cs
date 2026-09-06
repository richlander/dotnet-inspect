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

    [JSExport]
    public static string GetWidgetSelection(bool widget) =>
        JsonSerializer.Serialize(
            widget
                ? new WidgetSelection(new WidgetDto("selected", 2))
                : new WidgetSelection("fallback"),
            UnionFixtureJsonContext.Default.WidgetSelection);

    [JSExport]
    public static string GetDefaultSelection() =>
        JsonSerializer.Serialize(
            default(WidgetSelection),
            UnionFixtureJsonContext.Default.WidgetSelection);

    [JSExport]
    public static string GetFlagSelection(bool flag) =>
        JsonSerializer.Serialize(
            flag
                ? new FlagSelection((bool?)true)
                : new FlagSelection(new WidgetDto("flagged", 3)),
            UnionFixtureJsonContext.Default.FlagSelection);

    [JSExport]
    public static string GetOutcomeSelection(bool nested) =>
        JsonSerializer.Serialize(
            nested
                ? new OutcomeSelection(new WidgetSelection("nested"))
                : new OutcomeSelection(true),
            UnionFixtureJsonContext.Default.OutcomeSelection);

    [JSExport]
    public static string GetKindSelection(bool declared) =>
        JsonSerializer.Serialize(
            declared
                ? new KindSelection(WidgetKind.Deluxe)
                : new KindSelection("unknown"),
            UnionFixtureJsonContext.Default.KindSelection);

    [JSExport]
    public static string GetBoxedCount(int count) =>
        JsonSerializer.Serialize(
            new Boxed<int>(count),
            UnionFixtureJsonContext.Default.BoxedInt32);

    [JSExport]
    public static string GetBoxedWidget(string name) =>
        JsonSerializer.Serialize(
            new Boxed<WidgetDto>(new WidgetDto(name, 4)),
            UnionFixtureJsonContext.Default.BoxedWidgetDto);

    [JSExport]
    public static string GetCollectionSelection(int choice) =>
        JsonSerializer.Serialize(
            choice switch
            {
                // The array case declares non-nullable entries, yet a producer
                // can still write a null entry into that JSON array.
                0 => new CollectionSelection(
                    [new WidgetDto("listed", 10), null!]),
                1 => new CollectionSelection(
                    new Dictionary<string, WidgetDto?>
                    {
                        ["present"] = new WidgetDto("mapped", 11),
                        ["absent"] = null,
                    }),
                2 => new CollectionSelection(12),
                _ => default,
            },
            UnionFixtureJsonContext.Default.CollectionSelection);

    [JSExport]
    public static string GetWrappedBlob() =>
        JsonSerializer.Serialize(
            new Wrapped<byte[]>([1, 2, 3]),
            UnionFixtureJsonContext.Default.WrappedByteArray);

    [JSExport]
    public static async Task<string> GetSelectionEnvelopeAsync(string name)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new SelectionEnvelope(
                new WidgetSelection(new WidgetDto(name, 5)),
                [new WidgetSelection("first"), default],
                new Dictionary<string, WidgetSelection>
                {
                    ["named"] = new WidgetSelection(new WidgetDto(name, 6)),
                    ["missing"] = default,
                },
                new OutcomeSelection(new WidgetSelection("outcome")),
                new KindSelection(WidgetKind.Basic),
                WidgetKind.Deluxe,
                new Boxed<int>(7),
                new Boxed<WidgetDto>(new WidgetDto(name, 8)),
                new Boxed<WidgetDto[]>([new WidgetDto(name, 9), null!]),
                new Wrapped<byte[]>([4, 5])),
            UnionFixtureJsonContext.Default.SelectionEnvelope);
    }
}
