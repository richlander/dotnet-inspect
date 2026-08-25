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
