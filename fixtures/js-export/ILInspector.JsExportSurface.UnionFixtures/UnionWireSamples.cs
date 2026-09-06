using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.UnionFixtures;

public union ScalarUnion(int, string);
public union NullableUnion(int?, string);
public union DtoUnion(PackageSummary, string);
public union GenericUnion<T>(T, string);
public union NestedUnion(ScalarUnion, bool);
public union ObjectUnion(PackageSummary, PackageProblem);
public union NumberUnion(int, double);

[JsonConverter(typeof(CustomUnionConverter))]
public union CustomUnion(int, string);

public sealed record PackageSummary(string Id);
public sealed record PackageProblem(int Code);
public sealed record UnionEnvelope(DtoUnion Result, ScalarUnion[] Items);
public sealed record OrdinaryValue(int Value);

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
[JsonSerializable(typeof(NestedUnion))]
[JsonSerializable(typeof(ObjectUnion))]
[JsonSerializable(typeof(NumberUnion))]
[JsonSerializable(typeof(CustomUnion))]
[JsonSerializable(typeof(UnionEnvelope))]
[JsonSerializable(typeof(OrdinaryValue))]
public sealed partial class UnionJsonContext : JsonSerializerContext;
