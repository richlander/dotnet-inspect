using System.CodeDom.Compiler;
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
