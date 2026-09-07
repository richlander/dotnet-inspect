using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.UnionFixtures;

public union ScalarUnion(int, string);
public union NullableUnion(int?, string);
public union DtoUnion(PackageSummary, string);
public union GenericUnion<T>(T, string);
public union GenericArrayUnion<T>(T[], bool);
public union ParameterNameUnion<T>(T, T0);
public union UnsupportedCaseUnion(Guid, int);
public union initializeRuntime(int, string);
public union RecursiveUnion(RecursiveOtherUnion);
public union RecursiveOtherUnion(RecursiveUnion);
public union ReferenceArrayUnion(string?[], int);
public union NestedUnion(ScalarUnion, bool);
public union ObjectUnion(PackageSummary, PackageProblem);
public union NumberUnion(int, double);

[JsonConverter(typeof(CustomUnionConverter))]
public union CustomUnion(int, string);

public sealed record PackageSummary(string Id);
public sealed record PackageProblem(int Code);
public sealed record UnionEnvelope(DtoUnion Result, ScalarUnion[] Items);
public sealed record OrdinaryValue(int Value);
public sealed record T0(int Value);

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
    public static string GetGeneric() =>
        JsonSerializer.Serialize(
            new GenericUnion<int>(7),
            UnionJsonContext.Default.GenericUnionInt32);

    [JSExport]
    public static string GetGenericBytes() =>
        JsonSerializer.Serialize(
            new GenericUnion<byte[]>([1, 2, 3]),
            UnionJsonContext.Default.GenericUnionByteArray);

    [JSExport]
    public static string GetGenericDictionary() =>
        JsonSerializer.Serialize(
            new GenericUnion<Dictionary<string, int?>>(new Dictionary<string, int?>
            {
                ["value"] = 42,
                ["empty"] = null,
            }),
            UnionJsonContext.Default.GenericUnionDictionaryStringNullableInt32);

    [JSExport]
    public static string GetGenericArrayBytes() =>
        JsonSerializer.Serialize(
            new GenericArrayUnion<byte>([1, 2, 3]),
            UnionJsonContext.Default.GenericArrayUnionByte);

    [JSExport]
    public static string GetGenericArrayNumbers() =>
        JsonSerializer.Serialize(
            new GenericArrayUnion<int>([1, 2, 3]),
            UnionJsonContext.Default.GenericArrayUnionInt32);

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
    public static string GetGenericReferenceArray() =>
        JsonSerializer.Serialize(
            new GenericUnion<string?[]>(["value", null]),
            UnionJsonContext.Default.GenericUnionStringArray);

    [JSExport]
    public static string GetNested() =>
        JsonSerializer.Serialize(
            new NestedUnion(new ScalarUnion(42)),
            UnionJsonContext.Default.NestedUnion);

    [JSExport]
    public static string GetObjects() =>
        JsonSerializer.Serialize(
            new ObjectUnion(new PackageProblem(404)),
            UnionJsonContext.Default.ObjectUnion);

    [JSExport]
    public static string GetNumbers() =>
        JsonSerializer.Serialize(
            new NumberUnion(1.5),
            UnionJsonContext.Default.NumberUnion);

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
    public static void ReadScalar(string json) =>
        _ = JsonSerializer.Deserialize(json, UnionJsonContext.Default.ScalarUnion);

    [JSExport]
    public static void ReadObjects(string json) =>
        _ = JsonSerializer.Deserialize(json, UnionJsonContext.Default.ObjectUnion);
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
[JsonSerializable(typeof(GenericUnion<int>))]
[JsonSerializable(typeof(GenericUnion<byte[]>))]
[JsonSerializable(typeof(GenericUnion<Dictionary<string, int?>>))]
[JsonSerializable(typeof(GenericArrayUnion<byte>))]
[JsonSerializable(typeof(GenericArrayUnion<int>))]
[JsonSerializable(typeof(ParameterNameUnion<int>))]
[JsonSerializable(typeof(UnsupportedCaseUnion))]
[JsonSerializable(typeof(initializeRuntime))]
[JsonSerializable(typeof(RecursiveUnion))]
[JsonSerializable(typeof(RecursiveOtherUnion))]
[JsonSerializable(typeof(ReferenceArrayUnion))]
[JsonSerializable(typeof(GenericUnion<string?[]>))]
[JsonSerializable(typeof(NestedUnion))]
[JsonSerializable(typeof(ObjectUnion))]
[JsonSerializable(typeof(NumberUnion))]
[JsonSerializable(typeof(CustomUnion))]
[JsonSerializable(typeof(UnionEnvelope))]
[JsonSerializable(typeof(OrdinaryValue))]
public sealed partial class UnionJsonContext : JsonSerializerContext;
