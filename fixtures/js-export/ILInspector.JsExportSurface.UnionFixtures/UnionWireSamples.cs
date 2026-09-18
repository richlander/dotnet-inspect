using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.UnionFixtures;

public union ScalarUnion(int, string);
public union NullableUnion(int?, string);
public union DtoUnion(PackageSummary, string);
public union InspectionResult<T>(T, string);
public union PackageReadResult<T>(T, PackageProblem);
public union ItemsOrCount<T>(T[], int);
public union ParameterNameUnion<T>(T, T0);
public union UnsupportedCaseUnion(Guid, int);
public union initializeRuntime(int, string);
public union RecursiveUnion(RecursiveOtherUnion);
public union RecursiveOtherUnion(RecursiveUnion);
public union ReferenceArrayUnion(string?[], int);
public union NestedUnion(ScalarUnion, bool);

// These output scenarios intentionally prove boundaries where JSON token shape
// cannot identify the original case. A classifier would invent a read contract
// that the bridge neither consumes nor supports.
#pragma warning disable SYSLIB1227
public union PackageResolutionResult(PackageSummary, PackageProblem);
public union MetricValue(int, double);
public union InspectionValueOrCount(JsonElement, int);
public union RawInspectionResult<T>(T, string);
#pragma warning restore SYSLIB1227

public union InspectionRowsOrCount(JsonElement[], int);

[JsonConverter(typeof(CustomUnionConverter))]
public union CustomUnion(int, string);

public sealed record PackageSummary(string Id);
public sealed record PackageProblem(int Code);
public sealed record UnionEnvelope(DtoUnion Result, ScalarUnion[] Items);
public sealed record OrdinaryValue(int Value);
public sealed record T0(int Value);
public sealed record NonParametricArrayRecord<T>(T[]? Items);
public sealed record NonParametricNestedArrayRecord<T>(
    IReadOnlyDictionary<string, T[]> Items);
public sealed record NonParametricAnnotatedArrayRecord<T>(T?[] Items);
public sealed record NonParametricNestedAnnotatedArrayRecord<T>(
    IReadOnlyDictionary<string, T?[]> Items);
public sealed record ParametricNullableValueArrayRecord<T>(T?[] Items)
    where T : struct;
public sealed record ConditionalGenericRecord<T>(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    T Payload);
public sealed record ConditionalInspectionValueRecord(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    InspectionValueOrCount Payload);
public sealed record ConditionalRawInspectionResultRecord(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    RawInspectionResult<JsonElement> Payload);
public sealed record ConditionalNestedRawInspectionResultRecord(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    RawInspectionResult<RawInspectionResult<JsonElement>> Payload);
public sealed record ConditionalOpenRawInspectionResultRecord<T>(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    RawInspectionResult<T> Payload);
public sealed record ConditionalInspectionRowsRecord(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    InspectionRowsOrCount Payload);
public sealed record ConcreteArrayRecord<T>(T Value, T0[] Items);
public sealed record GlobalConcreteArrayRecord<Collision>(
    Collision Value,
    global::Collision[] Items);

public static partial class UnionExports
{
    [JSExport]
    public static string GetScalar(int choice) =>
        JsonSerializer.Serialize(
            choice == 0 ? new ScalarUnion(42)
                : choice == 1 ? new ScalarUnion("hello") : default,
            UnionJsonContext.Default.ScalarUnion);

    [JSExport]
    public static string GetDto() =>
        JsonSerializer.Serialize(
            new DtoUnion(new PackageSummary("Example.Package")),
            UnionJsonContext.Default.DtoUnion);

    [JSExport]
    public static string GetNullable() =>
        JsonSerializer.Serialize(
            new NullableUnion((int?)42),
            UnionJsonContext.Default.NullableUnion);

    [JSExport]
    public static string GetInspectionResult() =>
        JsonSerializer.Serialize(
            new InspectionResult<int>(7),
            UnionJsonContext.Default.InspectionResultInt32);

    [JSExport]
    public static string GetPackageReadBytes() =>
        JsonSerializer.Serialize(
            new PackageReadResult<byte[]>([1, 2, 3]),
            UnionJsonContext.Default.PackageReadResultByteArray);

    [JSExport]
    public static string GetInspectionDictionary() =>
        JsonSerializer.Serialize(
            new InspectionResult<Dictionary<string, int?>>(new Dictionary<string, int?>
            {
                ["value"] = 42,
                ["empty"] = null,
            }),
            UnionJsonContext.Default.InspectionResultDictionaryStringNullableInt32);

    [JSExport]
    public static string GetItemsOrCountBytes() =>
        JsonSerializer.Serialize(
            new ItemsOrCount<byte>([1, 2, 3]),
            UnionJsonContext.Default.ItemsOrCountByte);

    [JSExport]
    public static string GetItemsOrCountNumbers() =>
        JsonSerializer.Serialize(
            new ItemsOrCount<int>([1, 2, 3]),
            UnionJsonContext.Default.ItemsOrCountInt32);

    [JSExport]
    public static string GetParameterNameUnion() =>
        JsonSerializer.Serialize(
            new ParameterNameUnion<int>(new T0(3)),
            UnionJsonContext.Default.ParameterNameUnionInt32);

    [JSExport]
    public static string GetUnsupportedCase() =>
        JsonSerializer.Serialize(
            new UnsupportedCaseUnion(Guid.Empty),
            UnionJsonContext.Default.UnsupportedCaseUnion);

    [JSExport]
    public static string GetReservedUnionName() =>
        JsonSerializer.Serialize(
            new initializeRuntime(42),
            UnionJsonContext.Default.initializeRuntime);

    [JSExport]
    public static string GetRecursiveUnion() =>
        JsonSerializer.Serialize(
            default(RecursiveUnion),
            UnionJsonContext.Default.RecursiveUnion);

    [JSExport]
    public static string GetReferenceArrayUnion() =>
        JsonSerializer.Serialize(
            new ReferenceArrayUnion(["value", null]),
            UnionJsonContext.Default.ReferenceArrayUnion);

    [JSExport]
    public static string GetInspectionReferenceArray() =>
        JsonSerializer.Serialize(
            new InspectionResult<string?[]>(["value", null]),
            UnionJsonContext.Default.InspectionResultStringArray);

    [JSExport]
    public static string GetNested() =>
        JsonSerializer.Serialize(
            new NestedUnion(new ScalarUnion(42)),
            UnionJsonContext.Default.NestedUnion);

    [JSExport]
    public static string GetPackageResolution() =>
        JsonSerializer.Serialize(
            new PackageResolutionResult(new PackageProblem(404)),
            UnionJsonContext.Default.PackageResolutionResult);

    [JSExport]
    public static string GetMetricValue() =>
        JsonSerializer.Serialize(
            new MetricValue(1.5),
            UnionJsonContext.Default.MetricValue);

    [JSExport]
    public static string GetEnvelope() =>
        JsonSerializer.Serialize(
            new UnionEnvelope(
                new DtoUnion("missing"),
                [new ScalarUnion(7), new ScalarUnion("ok"), default]),
            UnionJsonContext.Default.UnionEnvelope);

    [JSExport]
    public static string GetCustom() =>
        JsonSerializer.Serialize(
            new CustomUnion(42),
            UnionJsonContext.Default.CustomUnion);

    [JSExport]
    public static string GetOrdinary() =>
        JsonSerializer.Serialize(
            new OrdinaryValue(42),
            UnionJsonContext.Default.OrdinaryValue);

    [JSExport]
    public static string GetPlain() => "plain";

    [JSExport]
    public static string GetNonParametricArrayRecord() =>
        JsonSerializer.Serialize(
            new NonParametricArrayRecord<byte>([1, 2, 3]),
            UnionJsonContext.Default.NonParametricArrayRecordByte);

    [JSExport]
    public static string GetNonParametricNestedArrayRecord() =>
        JsonSerializer.Serialize(
            new NonParametricNestedArrayRecord<byte>(
                new Dictionary<string, byte[]>
                {
                    ["value"] = [1, 2, 3],
                }),
            UnionJsonContext.Default.NonParametricNestedArrayRecordByte);

    [JSExport]
    public static string GetNonParametricAnnotatedArrayRecord() =>
        JsonSerializer.Serialize(
            new NonParametricAnnotatedArrayRecord<byte>([1, 2, 3]),
            UnionJsonContext.Default.NonParametricAnnotatedArrayRecordByte);

    [JSExport]
    public static string GetNonParametricNestedAnnotatedArrayRecord() =>
        JsonSerializer.Serialize(
            new NonParametricNestedAnnotatedArrayRecord<byte>(
                new Dictionary<string, byte[]>
                {
                    ["value"] = [1, 2, 3],
                }),
            UnionJsonContext.Default
                .NonParametricNestedAnnotatedArrayRecordByte);

    [JSExport]
    public static string GetParametricNullableValueArrayRecord() =>
        JsonSerializer.Serialize(
            new ParametricNullableValueArrayRecord<int>([1, null]),
            UnionJsonContext.Default
                .ParametricNullableValueArrayRecordInt32);

    [JSExport]
    public static string GetConditionalGenericRecord()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalGenericRecord<JsonElement>(
                document.RootElement.Clone()),
            UnionJsonContext.Default.ConditionalGenericRecordJsonElement);
    }

    [JSExport]
    public static string GetConditionalInspectionValue()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalInspectionValueRecord(
                new InspectionValueOrCount(document.RootElement.Clone())),
            UnionJsonContext.Default.ConditionalInspectionValueRecord);
    }

    [JSExport]
    public static string GetConditionalRawInspectionResult()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalRawInspectionResultRecord(
                new RawInspectionResult<JsonElement>(
                    document.RootElement.Clone())),
            UnionJsonContext.Default.ConditionalRawInspectionResultRecord);
    }

    [JSExport]
    public static string GetConditionalNestedRawInspectionResult()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalNestedRawInspectionResultRecord(
                new RawInspectionResult<RawInspectionResult<JsonElement>>(
                    new RawInspectionResult<JsonElement>(
                        document.RootElement.Clone()))),
            UnionJsonContext.Default
                .ConditionalNestedRawInspectionResultRecord);
    }

    [JSExport]
    public static string GetConditionalOpenRawInspectionResult()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalOpenRawInspectionResultRecord<JsonElement>(
                new RawInspectionResult<JsonElement>(
                    document.RootElement.Clone())),
            UnionJsonContext.Default
                .ConditionalOpenRawInspectionResultRecordJsonElement);
    }

    [JSExport]
    public static string GetConditionalInspectionRows()
    {
        using JsonDocument document = JsonDocument.Parse("""{"value":1}""");
        return JsonSerializer.Serialize(
            new ConditionalInspectionRowsRecord(
                new InspectionRowsOrCount(
                    new[] { document.RootElement.Clone() })),
            UnionJsonContext.Default
                .ConditionalInspectionRowsRecord);
    }

    [JSExport]
    public static string GetConcreteArrayRecord() =>
        JsonSerializer.Serialize(
            new ConcreteArrayRecord<int>(7, [new(8)]),
            UnionJsonContext.Default.ConcreteArrayRecordInt32);

    [JSExport]
    public static string GetGlobalConcreteArrayRecord() =>
        JsonSerializer.Serialize(
            new GlobalConcreteArrayRecord<int>(7, [new(8)]),
            UnionJsonContext.Default.GlobalConcreteArrayRecordInt32);

    [JSExport]
    public static void ReadScalar(string json) =>
        _ = JsonSerializer.Deserialize(json, UnionJsonContext.Default.ScalarUnion);

    [JSExport]
    public static void ReadPackageResolution(string json) =>
        _ = JsonSerializer.Deserialize(
            json,
            UnionJsonContext.Default.PackageResolutionResult);
}

public sealed class CustomUnionConverter : JsonConverter<CustomUnion>
{
    public override CustomUnion Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException());

    public override void Write(
        Utf8JsonWriter writer, CustomUnion value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"custom:{value.Value}");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScalarUnion))]
[JsonSerializable(typeof(NullableUnion))]
[JsonSerializable(typeof(DtoUnion))]
[JsonSerializable(typeof(InspectionResult<int>))]
[JsonSerializable(typeof(PackageReadResult<byte[]>))]
[JsonSerializable(typeof(InspectionResult<Dictionary<string, int?>>))]
[JsonSerializable(typeof(ItemsOrCount<byte>))]
[JsonSerializable(typeof(ItemsOrCount<int>))]
[JsonSerializable(typeof(ParameterNameUnion<int>))]
[JsonSerializable(typeof(UnsupportedCaseUnion))]
[JsonSerializable(typeof(initializeRuntime))]
[JsonSerializable(typeof(RecursiveUnion))]
[JsonSerializable(typeof(RecursiveOtherUnion))]
[JsonSerializable(typeof(ReferenceArrayUnion))]
[JsonSerializable(typeof(InspectionResult<string?[]>))]
[JsonSerializable(typeof(NestedUnion))]
[JsonSerializable(typeof(PackageResolutionResult))]
[JsonSerializable(typeof(MetricValue))]
[JsonSerializable(typeof(InspectionRowsOrCount))]
[JsonSerializable(typeof(CustomUnion))]
[JsonSerializable(typeof(UnionEnvelope))]
[JsonSerializable(typeof(OrdinaryValue))]
[JsonSerializable(typeof(NonParametricArrayRecord<byte>))]
[JsonSerializable(typeof(NonParametricNestedArrayRecord<byte>))]
[JsonSerializable(typeof(NonParametricAnnotatedArrayRecord<byte>))]
[JsonSerializable(typeof(NonParametricNestedAnnotatedArrayRecord<byte>))]
[JsonSerializable(typeof(ParametricNullableValueArrayRecord<int>))]
[JsonSerializable(typeof(ConditionalGenericRecord<JsonElement>))]
[JsonSerializable(typeof(ConditionalInspectionValueRecord))]
[JsonSerializable(typeof(ConditionalRawInspectionResultRecord))]
[JsonSerializable(typeof(ConditionalNestedRawInspectionResultRecord))]
[JsonSerializable(typeof(ConditionalOpenRawInspectionResultRecord<JsonElement>))]
[JsonSerializable(typeof(ConditionalInspectionRowsRecord))]
[JsonSerializable(typeof(ConcreteArrayRecord<int>))]
[JsonSerializable(typeof(GlobalConcreteArrayRecord<int>))]
public sealed partial class UnionJsonContext : JsonSerializerContext;
