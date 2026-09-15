using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiFacade.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<Flavor>))]
public enum Flavor
{
    Vanilla,
    Chocolate,
}

public sealed record Envelope(
    string Assembly,
    string Value,
    Flavor Flavor);

[JsonSerializable(typeof(Envelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CanaryJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    private static int s_identityCalls;
    private static int s_intDescribeCalls;
    private static int s_stringDescribeCalls;
    private static int s_asyncCalls;

    [JSExport]
    public static string Identity()
    {
        s_identityCalls++;
        return "beta:primary";
    }

    [JSExport]
    public static string Describe(int value)
    {
        s_intDescribeCalls++;
        return $"beta:int:{value}";
    }

    [JSExport]
    public static string Describe(string value)
    {
        s_stringDescribeCalls++;
        return $"beta:string:{value}";
    }

    [JSExport]
    public static async Task<string> GetEnvelopeAsync(
        string value,
        string flavor)
    {
        s_asyncCalls++;
        await Task.Yield();
        return JsonSerializer.Serialize(
            new Envelope("beta", value, ParseFlavor(flavor)),
            CanaryJsonContext.Default.Envelope);
    }

    [JSExport]
    public static string VerifyInvocations()
    {
        if (s_identityCalls != 1
            || s_intDescribeCalls != 1
            || s_stringDescribeCalls != 1
            || s_asyncCalls != 1
            || !ManagedOperationCanaryWasInvokedExactlyOnce()
            || SecondaryExports.IdentityCalls != 1)
        {
            throw new InvalidOperationException(
                "Beta facade did not invoke every canary operation exactly once.");
        }

        return "beta:invocations-ok";
    }

    private static Flavor ParseFlavor(string flavor) =>
        flavor switch
        {
            "vanilla" => Flavor.Vanilla,
            "chocolate" => Flavor.Chocolate,
            _ => throw new ArgumentOutOfRangeException(
                nameof(flavor),
                flavor,
                "Unknown canary flavor."),
        };
}

[SupportedOSPlatform("browser")]
public static partial class SecondaryExports
{
    internal static int IdentityCalls { get; private set; }

    [JSExport]
    public static string Identity()
    {
        IdentityCalls++;
        return "beta:secondary";
    }
}
