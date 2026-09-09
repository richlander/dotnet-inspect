using System.Text;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceSharePacketCodecTests
{
    private const string CanonicalVector =
        "eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEwIiwibmV0MTAuMCIsbnVsbF0s"
        + "WyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpb"
        + "WzAsMV1dLCJhIjoxLCJ4IjowLCJ2IjoiYXBpIiwieSI6IlN5c3RlbS5UZXh0Lkpzb24u"
        + "SnNvblNlcmlhbGl6ZXIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0";

    private const string UnicodeVector =
        "eyJmIjoxLCJ0IjpbWyJDb250b3NvLkpzb24iLCIyLjAuMCIsIm5ldDEwLjAiLCJsaW51"
        + "eC14NjQiXV0sImciOltbMF1dLCJhIjowLCJ4IjowLCJ2IjoibWVtYmVyIiwieSI6IuS-"
        + "iy5Kc29uU2VyaWFsaXplcjxUPiIsInMiOiJEZXNlcmlhbGl6ZUFzeW5jKFN5c3RlbS5T"
        + "dHJpbmcsIFN5c3RlbS5UaHJlYWRpbmcuQ2FuY2VsbGF0aW9uVG9rZW4pIiwiYyI6IlNv"
        + "dXJjZSIsImwiOlsiQ29udG9zby5Kc29uIl19";

    private const string IndependentFocusVector =
        "eyJmIjoxLCJ0IjpbWyJQIixudWxsLCJuZXQxMC4wIixudWxsXSxbIlEiLG51bGwsIm5l"
        + "dDEwLjAiLG51bGxdXSwiZyI6W1swXSxbMV1dLCJhIjoxLCJ4IjowfQ";

    [Fact]
    public void Decode_CanonicalVector_RoundTripsExactly()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            CanonicalVector,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, packet.FormatVersion);
        Assert.Equal(2, packet.Tabs.Count);
        Assert.Equal(WorkspaceShareSourceKind.Group, packet.Tabs[0].SourceKind);
        Assert.Equal(":Platform", packet.Tabs[0].Source);
        Assert.Equal("10.0.10", packet.Tabs[0].Version);
        Assert.Equal(WorkspaceShareSourceKind.Package, packet.Tabs[1].SourceKind);
        Assert.Equal("System.Text.Json", packet.Tabs[1].Source);
        Assert.Single(packet.Contexts);
        Assert.Equal([0, 1], packet.Contexts[0].TabIndexes);
        Assert.Equal(1, packet.ActiveTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal("api", packet.Lens);
        Assert.Equal("System.Text.Json.JsonSerializer", packet.Type);
        Assert.Null(packet.MemberAnchor);
        Assert.Null(packet.MemberSignature);
        Assert.Null(packet.Section);
        Assert.Equal(["System.Text.Json"], packet.Libraries);
        Assert.Equal(CanonicalVector, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void JsonConversion_AcceptsEquivalentInputAndRestoresCanonicalPacket()
    {
        const string equivalentJson =
            """
            {
              "x": 0,
              "a": 1,
              "g": [[0, 1]],
              "t": [
                [":Platform", "10.0.10", "net10.0", null],
                ["System.Text.Json", "10.0.0", "net10.0", null]
              ],
              "f": 1,
              "v": "\u0061pi",
              "y": "System.Text.Json.JsonSerializer",
              "l": ["System.Text.Json"]
            }
            """;

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            equivalentJson,
            TestContext.Current.CancellationToken);

        Assert.Equal(CanonicalVector, WorkspaceSharePacketCodec.Encode(packet));
        Assert.Equal(
            DecodeJson(CanonicalVector),
            WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Theory]
    [InlineData("library:overview", "Contoso.Library", "compile:ref/net11.0/Contoso.Library.dll")]
    [InlineData("library:metadata", "Contoso.Library", "compile:lib/net11.0/Contoso.Library.dll")]
    [InlineData("library:overview", ":Platform", "System.Text.Json")]
    [InlineData("library:metadata", ":Platform", "System.Text.Json")]
    public void LibraryLensAndSelection_RoundTripCanonicalProductPacket(
        string lens,
        string source,
        string library)
    {
        string json =
            $$"""{"f":1,"t":[["{{source}}","11.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"v":"{{lens}}","l":["{{library}}"]}""";
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        WorkspaceSharePacket decoded = WorkspaceSharePacketCodec.Decode(
            encoded,
            TestContext.Current.CancellationToken);

        Assert.Equal(lens, decoded.Lens);
        Assert.Equal([library], decoded.Libraries);
        Assert.Equal(source, Assert.Single(decoded.Tabs).Source);
        Assert.Null(decoded.Type);
        Assert.Null(decoded.MemberAnchor);
        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(decoded));
        Assert.Equal(EncodeJson(json), encoded);
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(decoded));
    }

    [Fact]
    public void JsonConversion_UsesTheSameTypedValidityAndCancellationGates()
    {
        WorkspaceSharePacketException duplicate = Assert.Throws<WorkspaceSharePacketException>(
            () => WorkspaceSharePacketCodec.ParseJson(
                """{"f":1,"f":1}""",
                TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceSharePacketFailureKind.InvalidJson, duplicate.Kind);

        WorkspaceSharePacketException oversized = Assert.Throws<WorkspaceSharePacketException>(
            () => WorkspaceSharePacketCodec.ParseJson(
                new string(' ', WorkspaceSharePacketCodec.MaxDecodedUtf8Length + 1),
                TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
            oversized.Kind);

        WorkspaceSharePacketException oversizedInvalidUnicode =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    "\uD800" + new string(
                        ' ',
                        WorkspaceSharePacketCodec.MaxDecodedUtf8Length),
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
            oversizedInvalidUnicode.Kind);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => WorkspaceSharePacketCodec.ParseJson(
                "{}",
                cancellation.Token));
    }

    [Fact]
    public void Decode_UnicodeAndSignatureVector_RoundTripsExactly()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            UnicodeVector,
            TestContext.Current.CancellationToken);

        Assert.Equal("例.JsonSerializer<T>", packet.Type);
        Assert.Equal(
            "DeserializeAsync(System.String, System.Threading.CancellationToken)",
            packet.MemberSignature);
        Assert.Equal("linux-x64", packet.Tabs[0].RuntimeIdentifier);
        Assert.Equal(UnicodeVector, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void Decode_IndependentFocusVector_PreservesFocusOutsideSelectedContext()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            IndependentFocusVector,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, packet.ActiveTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal([0], packet.Contexts[packet.SelectedContextIndex].TabIndexes);
        Assert.DoesNotContain(
            packet.ActiveTabIndex,
            packet.Contexts[packet.SelectedContextIndex].TabIndexes);
        Assert.Equal(IndependentFocusVector, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void Encode_UsesPinnedCanonicalStringEscaping()
    {
        const string value =
            "\"\\\b\t\n\f\r\u0000\u001f\u007f\u0085\u2028\u2029\U000E0074";
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "P",
                    version: null,
                    framework: "net10.0",
                    runtimeIdentifier: null),
            ],
            [new WorkspaceShareContext([0])],
            activeTabIndex: 0,
            selectedContextIndex: 0,
            lens: value,
            type: null,
            memberAnchor: null,
            memberSignature: null,
            section: null,
            libraries: []);

        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        string json = DecodeJson(encoded);
        Assert.Equal(
            "{\"f\":1,\"t\":[[\"P\",null,\"net10.0\",null]],"
            + "\"g\":[[0]],\"a\":0,\"x\":0,\"v\":\""
            + "\\\"\\\\\\b\\t\\n\\f\\r\\u0000\\u001f"
            + "\u007f\u0085\u2028\u2029\U000E0074\"}",
            json);
        Assert.Equal(
            encoded,
            WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.Decode(
                encoded,
                TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(""" { "f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}""")]
    [InlineData("""{"t":[["P",null,"net10.0",null]],"f":1,"g":[[0]],"a":0,"x":0}""")]
    [InlineData("""{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"\u0061pi"}""")]
    [InlineData("""{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"\u001F"}""")]
    public void Decode_RejectsValidButNonCanonicalJson(string json)
    {
        AssertFailure(
            EncodeJson(json),
            WorkspaceSharePacketFailureKind.NonCanonical);
    }

    [Theory]
    [InlineData("=")]
    [InlineData("+")]
    [InlineData("/")]
    [InlineData("A")]
    [InlineData("not.base64")]
    public void Decode_RejectsInvalidBase64Url(string encoded)
    {
        AssertFailure(
            encoded,
            WorkspaceSharePacketFailureKind.InvalidBase64Url);
    }

    [Fact]
    public void Decode_RejectsLegacyAndUnsupportedFormats()
    {
        AssertFailure(
            EncodeJson("""[["P","1.0.0","net10.0"]]"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(
                """{"f":1.0,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(
                """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}"""),
            WorkspaceSharePacketFailureKind.UnsupportedFormat);
    }

    [Fact]
    public void Decode_RejectsMalformedAndDuplicateJson()
    {
        AssertFailure(
            EncodeJson("""{"f":1"""),
            WorkspaceSharePacketFailureKind.InvalidJson);
        AssertFailure(
            EncodeJson(
                """{"f":1,"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}"""),
            WorkspaceSharePacketFailureKind.InvalidJson);
        AssertFailure(
            EncodeBytes([0x7B, 0x22, 0x66, 0x22, 0x3A, 0xC3, 0x28, 0x7D]),
            WorkspaceSharePacketFailureKind.InvalidJson);
        AssertFailure(
            "eyJmIjoxLCJ0IjpbWyJQIixudWxsLCJuZXQxMC4wIixudWxsXV0sImciOltbMF1d"
            + "LCJhIjowLCJ4IjowLCKAIjoxfQ",
            WorkspaceSharePacketFailureKind.InvalidJson);
    }

    [Theory]
    [InlineData("""[["bad/id",null,"net10.0",null]]""")]
    [InlineData("""[["P","1.0","net10.0",null]]""")]
    [InlineData("""[["P","1.0.0+build","net10.0",null]]""")]
    [InlineData("""[["P",null,"NET10.0",null]]""")]
    [InlineData("""[["P",null,"net10.0","Linux-X64"]]""")]
    [InlineData("""[[":Custom","1.0.0","net10.0",null]]""")]
    [InlineData("""[[":PlatformX","1.0.0","net10.0",null]]""")]
    [InlineData("""[[":Platform@10.0.0",null,"net10.0",null]]""")]
    [InlineData("""[[":Platform:",null,"net10.0",null]]""")]
    [InlineData("""[[":Platform+",null,"net10.0",null]]""")]
    [InlineData("""[[":Platform:+Extensions",null,"net10.0",null]]""")]
    [InlineData("""[[":Platform+Extensions:",null,"net10.0",null]]""")]
    [InlineData("""[[":Platform++Extensions",null,"net10.0",null]]""")]
    [InlineData("""[["P",null,"net10.0",null],["p",null,"net10.0",null]]""")]
    public void Decode_RejectsInvalidOrDuplicateTabTuples(string tabs)
    {
        string contexts = tabs.Contains("],[", StringComparison.Ordinal)
            ? "[[0,1]]"
            : "[[0]]";
        AssertFailure(
            EncodeJson(PacketJson(tabs, contexts)),
            WorkspaceSharePacketFailureKind.InvalidShape);
    }

    [Theory]
    [InlineData(
        """[["P",null,"net10.0",null]]""",
        """[[1]]""")]
    [InlineData(
        """[["P",null,"net10.0",null],["Q",null,"net10.0",null]]""",
        """[[0]]""")]
    [InlineData(
        """[["P",null,"net10.0",null]]""",
        """[[0,0]]""")]
    [InlineData(
        """[["P",null,"net10.0",null],[":Platform",null,"net10.0",null]]""",
        """[[0,1]]""")]
    [InlineData(
        """[["P",null,"net10.0",null],["Q",null,"net9.0",null]]""",
        """[[0,1]]""")]
    [InlineData(
        """[["P",null,"net10.0",null]]""",
        """[[0],[0]]""")]
    public void Decode_RejectsInvalidContextTopology(string tabs, string contexts)
    {
        AssertFailure(
            EncodeJson(PacketJson(tabs, contexts)),
            WorkspaceSharePacketFailureKind.InvalidShape);
    }

    [Theory]
    [InlineData(",\"m\":\"anchor\"")]
    [InlineData(",\"y\":\"T\",\"m\":\"anchor\",\"s\":\"signature\"")]
    [InlineData(",\"l\":[]")]
    [InlineData(",\"l\":[\"B\",\"A\"]")]
    [InlineData(",\"l\":[\"A\",\"A\"]")]
    [InlineData(",\"v\":\" \"")]
    [InlineData(",\"y\":\"\\t\"")]
    [InlineData(",\"c\":\" \"")]
    [InlineData(",\"l\":[\" \"]")]
    [InlineData(",\"unknown\":1")]
    public void Decode_RejectsInvalidViewAndLibraryShapes(string suffix)
    {
        string json =
            """{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0"""
            + suffix
            + "}";
        AssertFailure(
            EncodeJson(json),
            WorkspaceSharePacketFailureKind.InvalidShape);
    }

    [Fact]
    public void Decode_AcceptsPlatformSubgroupPin()
    {
        string json = PacketJson(
            """[[":Platform:AspNetCore","10.0.10","net10.0",null]]""",
            """[[0]]""");

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            EncodeJson(json),
            TestContext.Current.CancellationToken);

        Assert.Equal(":Platform:AspNetCore", packet.Tabs[0].Source);
        Assert.Equal("10.0.10", packet.Tabs[0].Version);
    }

    [Fact]
    public void Decode_RejectsMissingFieldsBoundsAndTrailingContent()
    {
        AssertFailure("", WorkspaceSharePacketFailureKind.Empty);
        AssertFailure(
            EncodeJson(
                """{"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(PacketJson(
                """[["P",null,"net10.0",null]]""",
                """[[0]]""").Replace("\"a\":0", "\"a\":1", StringComparison.Ordinal)),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(PacketJson(
                """[["P",null,"net10.0",null]]""",
                """[[0]]""").Replace("\"x\":0", "\"x\":1", StringComparison.Ordinal)),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(PacketJson(
                """[["P",null,"net10.0",null]]""",
                """[[0]]""") + "null"),
            WorkspaceSharePacketFailureKind.InvalidJson);
        AssertFailure(
            CanonicalVector + "=",
            WorkspaceSharePacketFailureKind.InvalidBase64Url);
    }

    [Fact]
    public void Decode_RejectsOverLimitTables()
    {
        string tabs = "["
            + string.Join(
                ',',
                Enumerable.Range(0, WorkspaceSharePacketCodec.MaxTabs + 1)
                    .Select(index => $"[\"P{index}\",null,\"net10.0\",null]"))
            + "]";
        string singletonContexts = "["
            + string.Join(
                ',',
                Enumerable.Range(0, WorkspaceSharePacketCodec.MaxTabs + 1)
                    .Select(index => $"[{index}]"))
            + "]";
        AssertFailure(
            EncodeJson(PacketJson(tabs, singletonContexts)),
            WorkspaceSharePacketFailureKind.InvalidShape);

        const int contextTabCount = 5;
        string contextTabs = "["
            + string.Join(
                ',',
                Enumerable.Range(0, contextTabCount)
                    .Select(index => $"[\"P{index}\",null,\"net10.0\",null]"))
            + "]";
        string contexts = "["
            + string.Join(
                ',',
                Enumerable.Range(0, contextTabCount)
                    .Select(index => $"[{index}]")
                    .Concat(
                        from first in Enumerable.Range(0, contextTabCount)
                        from second in Enumerable.Range(0, contextTabCount)
                        where first != second
                        select $"[{first},{second}]"))
            + "]";
        AssertFailure(
            EncodeJson(PacketJson(contextTabs, contexts)),
            WorkspaceSharePacketFailureKind.InvalidShape);
    }

    [Fact]
    public void Decode_EnforcesEncodedValueAndDepthBoundsBeforeBinding()
    {
        AssertFailure(
            new string('A', WorkspaceSharePacketCodec.MaxEncodedLength + 1),
            WorkspaceSharePacketFailureKind.EncodedLimitExceeded);

        string values = string.Join(',', Enumerable.Repeat("0", 1025));
        AssertFailure(
            EncodeJson(
                $$"""{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"z":[{{values}}]}"""),
            WorkspaceSharePacketFailureKind.JsonValueLimitExceeded);

        string nested = new string('[', WorkspaceSharePacketCodec.MaxJsonDepth + 1)
            + "0"
            + new string(']', WorkspaceSharePacketCodec.MaxJsonDepth + 1);
        AssertFailure(
            EncodeJson(nested),
            WorkspaceSharePacketFailureKind.InvalidJson);
    }

    [Fact]
    public void Decode_RejectsInvalidUnicodeAndChecksCancellationFirst()
    {
        AssertFailure(
            EncodeJson(
                """{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"\uD800"}"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            EncodeJson(
                """{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"\uDC00"}"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        AssertFailure(
            "eyJcdUQ4MDAiOjAsImYiOjEsInQiOltbIlAiLG51bGwsIm5ldDEwLjAiLG51bGxdXSwi"
            + "ZyI6W1swXV0sImEiOjAsIngiOjB9",
            WorkspaceSharePacketFailureKind.InvalidJson);

        WorkspaceSharePacketException exception = AssertFailure(
            EncodeJson(
                """{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"\u001b[2J\nspoof":1}"""),
            WorkspaceSharePacketFailureKind.InvalidShape);
        Assert.Equal(
            "Workspace share state contains an unknown property.",
            exception.Message);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => WorkspaceSharePacketCodec.Decode("", cancellation.Token));
    }

    [Fact]
    public void Decode_ReturnsMutationResistantCollections()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            CanonicalVector,
            TestContext.Current.CancellationToken);

        Assert.True(Assert.IsAssignableFrom<IList<WorkspaceShareTab>>(packet.Tabs).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<WorkspaceShareContext>>(packet.Contexts).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<int>>(packet.Contexts[0].TabIndexes).IsReadOnly);
        Assert.True(Assert.IsAssignableFrom<IList<string>>(packet.Libraries).IsReadOnly);
    }

    private static string PacketJson(string tabs, string contexts) =>
        $$"""{"f":1,"t":{{tabs}},"g":{{contexts}},"a":0,"x":0}""";

    private static WorkspaceSharePacketException AssertFailure(
        string encoded,
        WorkspaceSharePacketFailureKind expected)
    {
        WorkspaceSharePacketException exception = Assert.Throws<WorkspaceSharePacketException>(
            () => WorkspaceSharePacketCodec.Decode(
                encoded,
                TestContext.Current.CancellationToken));
        Assert.Equal(expected, exception.Kind);
        return exception;
    }

    private static string EncodeJson(string json) =>
        EncodeBytes(Encoding.UTF8.GetBytes(json));

    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DecodeJson(string encoded)
    {
        string padded = encoded.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
