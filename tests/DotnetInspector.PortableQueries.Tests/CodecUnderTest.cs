namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// The codec's entry points with the ambient test cancellation token threaded
/// through, so each test reads as the operation it is exercising.
/// </summary>
internal static class CodecUnderTest
{
    public static string Encode(PortableQueryIntent intent) =>
        PortableQueryPayloadCodec.Encode(intent, TestContext.Current.CancellationToken);

    public static PortableQueryIntent ParseJson(string json) =>
        PortableQueryPayloadCodec.ParseJson(json, TestContext.Current.CancellationToken);

    public static PortableQueryIntent Decode(string payload) =>
        PortableQueryPayloadCodec.Decode(payload, TestContext.Current.CancellationToken);

    public static PortableQueryIdentity Identity(string vocabulary, PortableQueryIntent intent) =>
        PortableQueryIdentity.Create(vocabulary, intent, TestContext.Current.CancellationToken);
}
