using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace ILInspector.JsExportSurface.TypeScriptFixtures;

public sealed record WidgetDto(string Name, int Count);

public sealed record InertWidgetDto(
    string Name,
    [property: JsonConverter(typeof(InertStringWriteConverter))]
    InertString Display);

public sealed record TimestampDto(
    DateTimeOffset ObservedAt,
    DateTimeOffset? CompletedAt,
    TimestampSelection Selection,
    NullableTimestampSelection NullableSelection);

public sealed class InertStringWriteConverter : JsonConverter<InertString>
{
    public override InertString Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "The fixture exposes inert text only as an output contract.");

    public override void Write(
        Utf8JsonWriter writer,
        InertString value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed record RuntimeAPI(string Value);

public sealed record GenericNested<TValue>(TValue Value);

public sealed record GenericNestedEnvelope(
    GenericNested<string?> Item);

public sealed record WrappedGenericNestedEnvelope(
    Wrapped<GenericNested<string?>> Item,
    Wrapped<GenericNested<string?>[]> Items,
    Wrapped<IReadOnlyDictionary<string, GenericNested<string?>>> Lookup);

public readonly record struct GenericNestedValue<TValue>(TValue Value);

public sealed record NullableWrappedGenericNestedEnvelope(
    Wrapped<GenericNestedValue<string?>?> Item);

public readonly record struct NullablePair<TFirst, TSecond>(
    TFirst First,
    TSecond Second);

public sealed record MixedNullableValueEnvelope(
    NullablePair<int?, string?> Item);

public union GenericNestedChoice(GenericNested<string?>, int);

public sealed record GenericRecord<TValue>(
    TValue Content,
    GenericNested<TValue> Nested,
    GenericNested<TValue>[] Items,
    IReadOnlyDictionary<string, TValue> Lookup,
    Boxed<TValue> Choice);

public sealed record BlobDto(
    byte[] Blob,
    byte[]? MaybeBlob,
    byte[]?[] Blobs,
    IReadOnlyDictionary<string, byte[]?> BlobsByName);

public sealed record InspectionEvidence(JsonElement? Payload);

public sealed record ConditionalOutputDto(string Name)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AlwaysNullable { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DefaultHidden { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? NullableDefaultHidden { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NullHidden { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string NonNullableNullHidden { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WidgetDto?[]? NullableItems { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Payload { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? NullablePayload { get; init; }
}

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
[JsonSerializable(typeof(WidgetDto[]))]
[JsonSerializable(typeof(InertWidgetDto))]
[JsonSerializable(typeof(TimestampDto))]
[JsonSerializable(typeof(RuntimeAPI))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ConditionalOutputDto))]
[JsonSerializable(typeof(HiddenTypeJsonIncludeDto))]
[JsonSerializable(typeof(global::@string), TypeInfoPropertyName = "StringDto")]
[JsonSerializable(typeof(global::@byte), TypeInfoPropertyName = "ByteDto")]
[JsonSerializable(typeof(global::KeywordHolder))]
[JsonSerializable(
    typeof(IReadOnlyDictionary<string, global::@string>),
    TypeInfoPropertyName = "StringDtoMap")]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(InspectionEvidence))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class InspectionEvidenceJsonContext
    : JsonSerializerContext;

[JsonSerializable(typeof(BlobDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BlobFixtureJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(GenericRecord<int>))]
[JsonSerializable(typeof(GenericRecord<WidgetDto>))]
[JsonSerializable(
    typeof(GenericNested<string>),
    TypeInfoPropertyName = "NullableGenericNested")]
[JsonSerializable(typeof(GenericNestedEnvelope))]
[JsonSerializable(typeof(WrappedGenericNestedEnvelope))]
[JsonSerializable(typeof(NullableWrappedGenericNestedEnvelope))]
[JsonSerializable(typeof(MixedNullableValueEnvelope))]
[JsonSerializable(typeof(GenericNestedChoice))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class GenericRecordJsonContext : JsonSerializerContext;

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
    public static bool MatchWidgetCandidates(
        string requestedName,
        string candidatesJson)
    {
        WidgetDto[] candidates = JsonSerializer.Deserialize(
            candidatesJson,
            FixtureJsonContext.Default.WidgetDtoArray)!;
        return candidates.Any(candidate =>
            candidate.Name == requestedName);
    }

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
    public static string GetConditionalOutput(string name) =>
        JsonSerializer.Serialize(
            new ConditionalOutputDto(name),
            FixtureJsonContext.Default.ConditionalOutputDto);

    [JSExport]
    public static string GetInspectionEvidence(bool includePayload)
    {
        using JsonDocument document =
            JsonDocument.Parse("""{"source":"package.xml"}""");
        JsonElement? payload = includePayload
            ? document.RootElement.Clone()
            : null;
        return JsonSerializer.Serialize(
            new InspectionEvidence(payload),
            InspectionEvidenceJsonContext.Default.InspectionEvidence);
    }

    [JSExport]
    public static async Task<string> GetInertWidgetAsync(string name)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new InertWidgetDto(
                name,
                new InertString(
                    TextPolicy.Field,
                    "line\u202Egpj")),
            FixtureJsonContext.Default.InertWidgetDto);
    }

    [JSExport]
    public static async Task<string> GetTimestampAsync()
    {
        await Task.Yield();
        var observedAt = new DateTimeOffset(
            2026,
            9,
            21,
            10,
            30,
            45,
            TimeSpan.FromHours(-7))
            .AddTicks(1_234_567);
        return JsonSerializer.Serialize(
            new TimestampDto(
                observedAt,
                null,
                new TimestampSelection(observedAt),
                new NullableTimestampSelection((DateTimeOffset?)null)),
            FixtureJsonContext.Default.TimestampDto);
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
    public static async Task<string> GetGenericRecordIntAsync()
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new GenericRecord<int>(
                7,
                new GenericNested<int>(8),
                [new(1), new(2)],
                new Dictionary<string, int> { ["missing"] = 0 },
                new Boxed<int>(9)),
            GenericRecordJsonContext.Default.GenericRecordInt32);
    }

    [JSExport]
    public static async Task<string> GetGenericRecordWidgetAsync(
        string name)
    {
        await Task.Yield();
        return JsonSerializer.Serialize(
            new GenericRecord<WidgetDto>(
                new WidgetDto(name, 10),
                new GenericNested<WidgetDto>(new WidgetDto(name, 11)),
                [new(new WidgetDto(name, 12))],
                new Dictionary<string, WidgetDto>
                {
                    ["missing"] = null!,
                },
                new Boxed<WidgetDto>(new WidgetDto(name, 13))),
            GenericRecordJsonContext.Default.GenericRecordWidgetDto);
    }

    [JSExport]
    public static string GetNullableGenericNested() =>
        JsonSerializer.Serialize(
            new GenericNested<string?>(null),
            GenericRecordJsonContext.Default.NullableGenericNested);

    [JSExport]
    public static string GetGenericNestedEnvelope() =>
        JsonSerializer.Serialize(
            new GenericNestedEnvelope(
                new GenericNested<string?>(null)),
            GenericRecordJsonContext.Default.GenericNestedEnvelope);

    [JSExport]
    public static string GetWrappedGenericNestedEnvelope() =>
        JsonSerializer.Serialize(
            new WrappedGenericNestedEnvelope(
                new Wrapped<GenericNested<string?>>(
                    new GenericNested<string?>(null)),
                new Wrapped<GenericNested<string?>[]>(
                    [new GenericNested<string?>(null)]),
                new Wrapped<IReadOnlyDictionary<
                    string,
                    GenericNested<string?>>>(
                        new Dictionary<string, GenericNested<string?>>
                        {
                            ["missing"] = new(null),
                        })),
            GenericRecordJsonContext.Default
                .WrappedGenericNestedEnvelope);

    [JSExport]
    public static string GetNullableWrappedGenericNestedEnvelope() =>
        JsonSerializer.Serialize(
            new NullableWrappedGenericNestedEnvelope(
                new Wrapped<GenericNestedValue<string?>?>(
                    new GenericNestedValue<string?>(null))),
            GenericRecordJsonContext.Default
                .NullableWrappedGenericNestedEnvelope);

    [JSExport]
    public static string GetMixedNullableValueEnvelope() =>
        JsonSerializer.Serialize(
            new MixedNullableValueEnvelope(
                new NullablePair<int?, string?>(null, null)),
            GenericRecordJsonContext.Default.MixedNullableValueEnvelope);

    [JSExport]
    public static string GetGenericNestedChoice() =>
        JsonSerializer.Serialize(
            new GenericNestedChoice(
                new GenericNested<string?>(null)),
            GenericRecordJsonContext.Default.GenericNestedChoice);

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
