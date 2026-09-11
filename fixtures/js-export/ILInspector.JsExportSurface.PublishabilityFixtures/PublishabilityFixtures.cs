using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ILInspector.JsExportSurface.PublishabilityFixtures;

[SupportedOSPlatform("browser")]
public partial interface BodylessInterfaceExportFixture
{
    [JSExport]
    static abstract int Compute(int value);
}

[SupportedOSPlatform("browser")]
public static partial class BodylessExternExportFixture
{
    [JSExport]
    [DllImport("__Internal")]
    public static extern int Compute(int value);
}

#pragma warning disable SYSLIB1071
[SupportedOSPlatform("browser")]
public static class NonPartialExportFixture
{
    [JSExport]
    public static int AddOne(int value) => value + 1;
}
#pragma warning restore SYSLIB1071

#pragma warning disable SYSLIB1071
[SupportedOSPlatform("browser")]
public static class HandwrittenWrapperCandidateFixture
{
    [JSExport]
    public static int AddOne(int value) => value + 1;

    [DynamicDependency(
        "__Wrapper_AddOne_1",
        "ILInspector.JsExportSurface.PublishabilityFixtures.HandwrittenWrapperCandidateFixture",
        "ILInspector.JsExportSurface.PublishabilityFixtures")]
    private static unsafe void __Wrapper_AddOne_1(
        JSMarshalerArgument* arguments)
    {
    }
}

[SupportedOSPlatform("browser")]
public static partial class WrapperPrefixCollisionFixture
{
    [JSExport]
    public static partial int Foo(int value);

    public static partial int Foo(int value) => value + 1;

    [JSExport]
    public static int Foo_Bar(int value) => value + 2;
}

[SupportedOSPlatform("browser")]
public static partial class OverloadedExportFixture
{
    [JSExport]
    public static string Identify(int value) => $"int:{value}";

    [JSExport]
    public static string Identify(string value) => $"string:{value}";
}

public static partial class NestedExportContainer
{
    [SupportedOSPlatform("browser")]
    public static partial class NestedExports
    {
        [JSExport]
        public static int AddOne(int value) => value + 1;
    }
}
#pragma warning restore SYSLIB1071

public static class LambdaExportFixture
{
    public static Func<int, int> Create() =>
        [JSExport] static (int value) => value + 1;
}

public sealed record HandwrittenPayload(string Value);

[JsonSerializable(
    typeof(HandwrittenPayload),
    TypeInfoPropertyName = "RegisteredPayload")]
[GeneratedCode("Another.SourceGenerator", "1.0")]
public sealed class HandwrittenJsonContext : JsonSerializerContext
{
    public static HandwrittenJsonContext Default { get; } =
        new(new JsonSerializerOptions());

    public HandwrittenJsonContext(JsonSerializerOptions options)
        : base(options)
    {
    }

    protected override JsonSerializerOptions? GeneratedSerializerOptions =>
        null;

    public override JsonTypeInfo? GetTypeInfo(Type type) =>
        new DefaultJsonTypeInfoResolver().GetTypeInfo(type, Options);

    public JsonTypeInfo<HandwrittenPayload> RegisteredPayload =>
        (JsonTypeInfo<HandwrittenPayload>)GetTypeInfo(
            typeof(HandwrittenPayload))!;
}

[SupportedOSPlatform("browser")]
public static partial class HandwrittenContextExports
{
    [JSExport]
    public static string GetPayload() =>
        JsonSerializer.Serialize(
            new HandwrittenPayload("handwritten"),
            HandwrittenJsonContext.Default.RegisteredPayload);
}

public sealed class ConstructorBoundInput
{
    [JsonConstructor]
    public ConstructorBoundInput(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

[JsonSerializable(typeof(ConstructorBoundInput))]
public sealed partial class ConstructorBoundJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class ConstructorBoundExports
{
    [JSExport]
    public static int ReadValue(string json) =>
        JsonSerializer.Deserialize(
            json,
            ConstructorBoundJsonContext.Default
                .ConstructorBoundInput)!
            .Value;
}

public sealed class PrivateSetterConstructorBoundInput
{
    [JsonConstructor]
    public PrivateSetterConstructorBoundInput(int value)
    {
        Value = value;
    }

    public int Value { get; private set; }
}

[JsonSerializable(typeof(PrivateSetterConstructorBoundInput))]
public sealed partial class PrivateSetterConstructorBoundJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class PrivateSetterConstructorBoundExports
{
    [JSExport]
    public static int ReadValue(string json) =>
        JsonSerializer.Deserialize(
            json,
            PrivateSetterConstructorBoundJsonContext.Default
                .PrivateSetterConstructorBoundInput)!
            .Value;
}

#pragma warning disable SYSLIB1071
[SupportedOSPlatform("browser")]
public static class TargetIdentitySpoofFixture
{
    [JSExport]
    public static int ReadValue(string value) => value.Length;

    private static unsafe void __Wrapper_ReadValue_764966221(
        JSMarshalerArgument* arguments)
    {
        __Stub();

        static void __Stub()
        {
            _ = ReadValue("");
        }
    }
}
#pragma warning restore SYSLIB1071

public sealed class PopulateInput
{
    public List<int> Values { get; private set; } = [1];
}

[JsonSourceGenerationOptions(
    PreferredObjectCreationHandling =
        JsonObjectCreationHandling.Populate)]
[JsonSerializable(typeof(PopulateInput))]
public sealed partial class PopulateJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class PopulateExports
{
    [JSExport]
    public static int CountValues(string json) =>
        JsonSerializer.Deserialize(
            json,
            PopulateJsonContext.Default.PopulateInput)!
            .Values.Count;
}

[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class TypeAttributePopulateContract
{
    public List<int> Values { get; private set; } = [1];
}

[JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
public sealed class TypeAttributeReplaceContract
{
    public List<int> Values { get; set; } = [1];
}

public sealed class PropertyAttributePopulateContract
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<int> Values { get; private set; } = [1];
}

[JsonSerializable(typeof(TypeAttributePopulateContract))]
[JsonSerializable(typeof(TypeAttributeReplaceContract))]
[JsonSerializable(typeof(PropertyAttributePopulateContract))]
public sealed partial class AttributePopulateJsonContext
    : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class AttributePopulateExports
{
    [JSExport]
    public static int CountTypeValues(string json) =>
        JsonSerializer.Deserialize(
            json,
            AttributePopulateJsonContext.Default
                .TypeAttributePopulateContract)!
            .Values.Count;

    [JSExport]
    public static int CountPropertyValues(string json) =>
        JsonSerializer.Deserialize(
            json,
            AttributePopulateJsonContext.Default
                .PropertyAttributePopulateContract)!
            .Values.Count;
}

public sealed record DuplicateRootDto(string DisplayName);

[JsonSerializable(
    typeof(DuplicateRootDto),
    TypeInfoPropertyName = "RealRoot")]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class DuplicateRootJsonContext
    : JsonSerializerContext
{
    public JsonTypeInfo<DuplicateRootDto> EvilRoot
    {
        get
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = resolver,
                PropertyNamingPolicy =
                    JsonNamingPolicy.SnakeCaseLower,
            };
            return (JsonTypeInfo<DuplicateRootDto>)
                resolver.GetTypeInfo(
                    typeof(DuplicateRootDto),
                    options);
        }
    }
}

[SupportedOSPlatform("browser")]
public static partial class DuplicateRootExports
{
    [JSExport]
    public static string Serialize() =>
        JsonSerializer.Serialize(
            new DuplicateRootDto("probe"),
            DuplicateRootJsonContext.Default.EvilRoot);
}

public sealed class IndexedRootDto
{
    public int Value { get; set; }
}

[JsonSerializable(
    typeof(IndexedRootDto),
    TypeInfoPropertyName = "Root")]
public sealed partial class IndexedRootJsonContext
    : JsonSerializerContext
{
    [IndexerName("Fake")]
    public JsonTypeInfo<IndexedRootDto> this[int index]
    {
        get
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            var options = new JsonSerializerOptions
            {
                TypeInfoResolver = resolver,
                NumberHandling =
                    JsonNumberHandling.WriteAsString,
            };
            return (JsonTypeInfo<IndexedRootDto>)
                resolver.GetTypeInfo(
                    typeof(IndexedRootDto),
                    options);
        }
    }
}

[SupportedOSPlatform("browser")]
public static partial class IndexedRootExports
{
    [JSExport]
    public static string Serialize() =>
        JsonSerializer.Serialize(
            new IndexedRootDto { Value = 42 },
            IndexedRootJsonContext.Default[0]);
}

/// <summary>
/// A <c>long</c> export marshaled as a JavaScript <c>BigInt</c>. TypeScript emission maps every
/// <c>long</c> to <c>number</c>, which silently misdescribes this wire shape, so the surface must
/// reject it visibly rather than publish it.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class BigIntMarshalFixture
{
    [JSExport]
    [return: JSMarshalAs<JSType.BigInt>]
    public static long EchoBigInt(
        [JSMarshalAs<JSType.BigInt>] long value) => value;
}

/// <summary>
/// The close negative: the same <c>long</c> export marshaled as a JavaScript number, which
/// <c>number</c> does describe and which must keep publishing.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Int52MarshalFixture
{
    [JSExport]
    [return: JSMarshalAs<JSType.Number>]
    public static long EchoInt52(
        [JSMarshalAs<JSType.Number>] long value) => value;
}
