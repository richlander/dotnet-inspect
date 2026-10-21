using System.Text;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

// PR-fast: bounded in-memory codec inputs; no acquisition or assembly analysis.
public sealed class WorkspaceSharePacketRecognitionTests
{
    const string PackageJson =
        """{"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";

    const string TypeJson =
        """{"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonSerializer"},"u":{"k":"type"},"f":"type.api"}]}""";

    const string RuntimeJson =
        """{"f":3,"t":[],"g":[],"r":[["e","ecosystem.runtime",["System"],[],[["t","DotNetRuntime"],["p","System."]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";

    [Theory]
    [InlineData(PackageJson)]
    [InlineData(TypeJson)]
    [InlineData(RuntimeJson)]
    [InlineData("""{"f":1,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0}""")]
    [InlineData("""{"f":2,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData("""{"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    public void RecognizesCanonicalPacketsWithoutChangingTheirBytes(string json)
    {
        string encoded = Encode(json);
        var result = Assert.IsType<WorkspaceSharePacketRecognition.Recognized>(
            WorkspaceSharePacketCodec.Recognize(
                encoded, TestContext.Current.CancellationToken));

        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(result.Packet));
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(result.Packet));
        Assert.Equal(
            json,
            WorkspaceSharePacketCodec.SerializeJson(
                WorkspaceSharePacketCodec.Decode(
                    encoded, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("System.Text.Json@10.0.0")]
    [InlineData("System")]
    [InlineData("Contoso")]
    [InlineData("Zm9v")]
    [InlineData("e30")]
    [InlineData("https://dotnet-inspect.net/?w=packet")]
    [InlineData("eyJmIjo2fQ=")]
    public void OrdinaryTargetsAndInvalidEncodingAreUnrecognized(string input)
    {
        Assert.IsType<WorkspaceSharePacketRecognition.Unrecognized>(
            WorkspaceSharePacketCodec.Recognize(
                input, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("\"f\"")]
    [InlineData("""{"other":{"f":3}}""")]
    [InlineData("""[{"f":3}]""")]
    [InlineData("""{"other": ["f",3]}""")]
    [InlineData("""{"other":"f","broken":""")]
    public void EncodedJsonWithoutAnObservedRootMarkerIsUnrecognized(string json)
    {
        Assert.IsType<WorkspaceSharePacketRecognition.Unrecognized>(
            WorkspaceSharePacketCodec.Recognize(
                Encode(json), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"f":6}""", WorkspaceSharePacketFailureKind.UnsupportedFormat)]
    [InlineData("""{"f":null}""", WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData("""{"f":"3"}""", WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData("""{"f":1.0}""", WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData("""{"f":3}""", WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData("""{"other":null,"f":6}""", WorkspaceSharePacketFailureKind.UnsupportedFormat)]
    [InlineData("""{"\u0066":6}""", WorkspaceSharePacketFailureKind.UnsupportedFormat)]
    [InlineData("""{"f":3,"f":3}""", WorkspaceSharePacketFailureKind.InvalidJson)]
    [InlineData("""{"f":3,"broken":""", WorkspaceSharePacketFailureKind.InvalidJson)]
    [InlineData("""{"\uD800":null,"f":6}""", WorkspaceSharePacketFailureKind.InvalidJson)]
    public void ObservedMarkersRetainTheExplicitDecodeFailure(
        string json,
        WorkspaceSharePacketFailureKind expected)
    {
        AssertRecognizedFailure(Encode(json), expected);
    }

    [Theory]
    [InlineData(" " + PackageJson)]
    [InlineData("""{"t":[["System.Text.Json","10.0.0","net10.0",null]],"f":1,"g":[[0]],"a":0,"x":0}""")]
    public void NoncanonicalPacketsAreRecognizedFailures(string json)
    {
        AssertRecognizedFailure(
            Encode(json), WorkspaceSharePacketFailureKind.NonCanonical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalJsonBudgetsDoNotScanPastTheirBoundToFindTheMarker(
        bool markerFirst)
    {
        string values = string.Join(',', Enumerable.Repeat(
            "0", WorkspaceSharePacketCodec.MaxJsonValues + 1));
        string nested = new string('[', WorkspaceSharePacketCodec.MaxJsonDepth + 1)
            + "0" + new string(']', WorkspaceSharePacketCodec.MaxJsonDepth + 1);
        foreach ((string content, WorkspaceSharePacketFailureKind kind) in new[]
        {
            ($"[{values}]", WorkspaceSharePacketFailureKind.JsonValueLimitExceeded),
            (nested, WorkspaceSharePacketFailureKind.InvalidJson),
        })
        {
            string json = markerFirst
                ? $$"""{"f":3,"other":{{content}}}"""
                : $$"""{"other":{{content}},"f":3}""";
            string encoded = Encode(json);
            if (markerFirst)
                AssertRecognizedFailure(encoded, kind);
            else
                Assert.IsType<WorkspaceSharePacketRecognition.Unrecognized>(
                    WorkspaceSharePacketCodec.Recognize(
                        encoded, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void GlobalEncodedLimitRequiresExplicitAdmission()
    {
        string encoded = Encode(PackageJson)
            + new string('A', WorkspaceSharePacketCodec.MaxEncodedLength);
        Assert.IsType<WorkspaceSharePacketRecognition.Unrecognized>(
            WorkspaceSharePacketCodec.Recognize(
                encoded, TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.EncodedLimitExceeded,
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.Decode(
                    encoded, TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public void LegacySizeLimitRetainsItsTypedFailureAfterRecognition()
    {
        string large = new('a', WorkspaceSharePacketCodec.MaxFormat1DecodedUtf8Length);
        AssertRecognizedFailure(
            Encode($$"""{"f":1,"other":"{{large}}"}"""),
            WorkspaceSharePacketFailureKind.DecodedLimitExceeded);
    }

    [Fact]
    public void InvalidUtf8DoesNotEstablishIntentFromAPartialPrefix()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("{\"f\":6}"), 0xff];
        string encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.IsType<WorkspaceSharePacketRecognition.Unrecognized>(
            WorkspaceSharePacketCodec.Recognize(
                encoded, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CancellationIsNotClassifiedAsAnOrdinaryTarget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (string input in new[] { "Newtonsoft.Json", Encode(PackageJson) })
        {
            Assert.Throws<OperationCanceledException>(() =>
                WorkspaceSharePacketCodec.Recognize(input, cancellation.Token));
        }
        Assert.Throws<ArgumentNullException>(() =>
            WorkspaceSharePacketCodec.Recognize(
                null!, TestContext.Current.CancellationToken));
    }

    static void AssertRecognizedFailure(
        string encoded,
        WorkspaceSharePacketFailureKind expected)
    {
        var result = Assert.IsType<WorkspaceSharePacketRecognition.Invalid>(
            WorkspaceSharePacketCodec.Recognize(
                encoded, TestContext.Current.CancellationToken));
        Assert.Equal(expected, result.Kind);
        WorkspaceSharePacketException direct = Assert.Throws<WorkspaceSharePacketException>(
            () => WorkspaceSharePacketCodec.Decode(
                encoded, TestContext.Current.CancellationToken));
        Assert.Equal(direct.Kind, result.Kind);
        Assert.Equal(direct.Message, result.Message);
    }

    static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
