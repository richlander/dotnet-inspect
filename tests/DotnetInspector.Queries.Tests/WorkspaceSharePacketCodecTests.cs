using System.Text;
using System.Text.Json;
using DotnetInspector.Platforms;
using QuerySpace;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

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

    private const string CanonicalFormat2Json =
        """{"f":2,"t":[[":Platform","10.0.10","net10.0",null],["System.Text.Json","10.0.0","net10.0",null]],"g":[[0,1]],"a":1,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0},{"t":1,"r":{"k":"member","l":["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"],"y":"System.Text.Json.JsonSerializer","m":"74b6b4b321"},"u":{"k":"workspace"},"f":"workspace.overview"}]}""";

    private const string CanonicalFormat3RegistrationOnlyJson =
        """{"f":3,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";

    private const string CanonicalFormat3CompositeJson =
        """{"f":3,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"r":[["l",["p","system.text.json","10.0.0",["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"]]],["l",["t","DotNetRuntime",["System.Runtime","11.0.0.0",null,"b03f5f7f11d50a3a"]]],["e","ecosystem.platform",["System"],["system.runtime"],[["l",["p","system.text.json","10.0.0",["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"]]],["t","AspNetCore"],["p","Microsoft.Extensions."]]]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";

    private const string CanonicalFormat2QueryJson =
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["test-query/v1",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"},"f":"dependencies","q":[0],"l":[["P","1.0.0.0",null,null]]}]}""";

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
                new string(
                    ' ',
                    WorkspaceSharePacketCodec.MaxDecodedUtf8Length + 1),
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
        Assert.Equal(
            [0],
            packet.Contexts[packet.SelectedContextIndex!.Value].TabIndexes);
        Assert.DoesNotContain(
            packet.ActiveTabIndex,
            packet.Contexts[packet.SelectedContextIndex.Value].TabIndexes);
        Assert.Equal(IndependentFocusVector, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void Decode_Format2CanonicalVector_RoundTripsExactly()
    {
        string encoded = EncodeJson(CanonicalFormat2Json);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            encoded,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, packet.FormatVersion);
        Assert.Equal(1, packet.FocusedTabIndex);
        Assert.Equal(3, packet.ViewStates.Count);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            packet.ViewStates[0].Subject);
        Assert.Null(packet.ViewStates[1].Subject);
        var member =
            Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
                packet.ViewStates[2].Context);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            member.EscapedTypeIdentity);
        Assert.Equal("74b6b4b321", member.MemberAnchor);
        Assert.Equal(
            CanonicalFormat2Json,
            WorkspaceSharePacketCodec.SerializeJson(packet));
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void Decode_Format2WorkspaceSelection_PreservesNullFocus()
    {
        const string json =
            """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"},"f":"workspace.overview"},{"t":0}]}""";

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        Assert.Null(packet.FocusedTabIndex);
        Assert.Equal(-1, packet.ActiveTabIndex);
        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Fact]
    public void Decode_Format2QueryAndLibraryScope_RoundTripsExactly()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            CanonicalFormat2QueryJson,
            TestContext.Current.CancellationToken);

        Assert.Equal("test-query/v1", Assert.Single(packet.Queries).Vocabulary);
        WorkspaceShareViewState state = packet.ViewStates[1];
        Assert.Equal([0], state.QueryIndexes);
        Assert.Equal("P", Assert.Single(state.Libraries).Name);
        Assert.Equal(
            CanonicalFormat2QueryJson,
            WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Fact]
    public void Format2_RejectsSemanticallyDuplicateLibraryScope()
    {
        const string json =
            """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"},"f":"package.overview","q":[0],"l":[["System.Text.Json","10.0.0.0",null,"cc7b13ffcd2ddd51"],["system.text.json","10.0.0.0",null,"cc7b13ffcd2ddd51"]]}]}""";

        WorkspaceSharePacketException parseException =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "semantic duplicates",
            parseException.Message,
            StringComparison.Ordinal);

        PortableLibraryIdentity[] libraries =
        [
            new(
                "System.Text.Json",
                "10.0.0.0",
                null,
                "cc7b13ffcd2ddd51"),
            new(
                "system.text.json",
                "10.0.0.0",
                null,
                "cc7b13ffcd2ddd51"),
        ];
        var packet = new WorkspaceSharePacket(
            [
                new WorkspaceShareTab(
                    WorkspaceShareSourceKind.Package,
                    "P",
                    "1.0.0",
                    "net11.0",
                    null),
            ],
            [new WorkspaceShareContext([0])],
            focusedTabIndex: 0,
            selectedContextIndex: 0,
            [
                new WorkspaceShareViewState(
                    null,
                    new PortableSubjectRequest.Workspace(),
                    null,
                    null),
                new WorkspaceShareViewState(
                    0,
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    "package.overview",
                    [0],
                    libraries),
            ],
            [
                PortableQueryIdentity.FromCanonicalPayload(
                    "a",
                    "{}",
                    TestContext.Current.CancellationToken),
            ]);

        WorkspaceSharePacketException writeException =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.SerializeJson(packet));

        Assert.Contains(
            "semantic duplicates",
            writeException.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_QueryTableUsesOrdinalVocabularyOrdering()
    {
        const string json =
            """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["😀",{}],["",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"package"},"f":"package.overview","q":[0,1]}]}""";

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["😀", ""],
            packet.Queries.Select(query => query.Vocabulary));
        Assert.Equal(json, WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Fact]
    public void Decode_Format3RegistrationOnlyVector_RoundTripsExactly()
    {
        string encoded = EncodeJson(
            CanonicalFormat3RegistrationOnlyJson);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            encoded,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, packet.FormatVersion);
        Assert.Empty(packet.Tabs);
        Assert.Empty(packet.Contexts);
        Assert.Null(packet.FocusedTabIndex);
        Assert.Null(packet.SelectedContextIndex);
        var prefix = Assert.IsType<WorkspaceRegistration.PackagePrefix>(
            Assert.Single(packet.Registrations));
        Assert.Equal("Microsoft.Extensions.", prefix.Prefix.Prefix);
        Assert.Single(packet.ViewStates);
        Assert.Equal(
            CanonicalFormat3RegistrationOnlyJson,
            WorkspaceSharePacketCodec.SerializeJson(packet));
        Assert.Equal(encoded, WorkspaceSharePacketCodec.Encode(packet));
    }

    [Fact]
    public void Decode_Format3CompositeVector_PreservesRegistrationArms()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            CanonicalFormat3CompositeJson,
            TestContext.Current.CancellationToken);

        Assert.Equal(3, packet.FormatVersion);
        Assert.Equal(3, packet.Registrations.Count);
        var package = Assert.IsType<ExactLibrarySourceCoordinate.Package>(
            Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                packet.Registrations[0]).Coordinate);
        Assert.Equal(
            "system.text.json",
            package.PackageCoordinate.PackageId);
        var platform = Assert.IsType<ExactLibrarySourceCoordinate.Platform>(
            Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                packet.Registrations[1]).Coordinate);
        Assert.Equal(
            PlatformFamily.DotNetRuntime,
            platform.Population.Family);
        var ecosystem = Assert.IsType<WorkspaceRegistration.Ecosystem>(
            packet.Registrations[2]).Declaration;
        Assert.Equal("ecosystem.platform", ecosystem.Id.Value);
        Assert.Equal(["System"], ecosystem.NamespaceRoots);
        Assert.Equal(
            ["system.runtime"],
            ecosystem.CorePackages.Select(package => package.PackageId));
        Assert.Collection(
            ecosystem.Populations,
            population => Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.ExactLibrary>(
                    population),
            population => Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.Platform>(
                    population),
            population => Assert.IsType<
                WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                    population));
        Assert.Equal(
            CanonicalFormat3CompositeJson,
            WorkspaceSharePacketCodec.SerializeJson(packet));
    }

    [Theory]
    [InlineData("""{"f":3,"t":[],"g":[],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["p","P."]],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"r":[],"a":0,"x":null,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["q","P."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["p"]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["l",["p","P","1.0.0"]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["l",["x","P",["P","1.0.0.0",null,null]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["e","ecosystem.test",[],[],[["x","P."]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    [InlineData("""{"f":3,"t":[],"g":[],"r":[["e","ecosystem.test",[],[],[["t","dotnetruntime"]]]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""")]
    public void Format3_RejectsMalformedRegistrationTopology(
        string json)
    {
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            exception.Kind);
    }

    [Fact]
    public void Format3_RejectsDuplicateAndExcessRegistrations()
    {
        const string duplicate =
            """{"f":3,"t":[],"g":[],"r":[["p","P."],["p","P."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacketException duplicateFailure =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    duplicate,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            duplicateFailure.Kind);

        string registrations = string.Join(
            ",",
            Enumerable.Range(
                0,
                WorkspaceSharePacketCodec.MaxRegistrations + 1)
                .Select(index => $$"""["p","P{{index}}."]"""));
        string excessive =
            """{"f":3,"t":[],"g":[],"r":["""
            + registrations
            + """],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}""";
        WorkspaceSharePacketException excessiveFailure =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    excessive,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            excessiveFailure.Kind);
    }

    [Fact]
    public void Format2_UsesItsLargerBoundWithoutChangingFormat1()
    {
        string facet = new('a', 13 * 1024);
        string format2Json =
            """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"},"f":"""
            + JsonSerializer.Serialize(facet)
            + """},{"t":0}]}""";

        WorkspaceSharePacket format2 = WorkspaceSharePacketCodec.ParseJson(
            format2Json,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(format2);

        Assert.True(
            Encoding.UTF8.GetByteCount(format2Json)
                > WorkspaceSharePacketCodec.MaxFormat1DecodedUtf8Length);
        Assert.True(
            encoded.Length
                > WorkspaceSharePacketCodec.MaxFormat1EncodedLength);
        Assert.Equal(
            encoded,
            WorkspaceSharePacketCodec.Encode(
                WorkspaceSharePacketCodec.Decode(
                    encoded,
                    TestContext.Current.CancellationToken)));

        string format1Json =
            """{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":"""
            + JsonSerializer.Serialize(facet)
            + "}";
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    format1Json,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceSharePacketFailureKind.DecodedLimitExceeded,
            exception.Kind);
    }

    [Fact]
    public void Format2_RoundTripsPortableLibraryAndEscapedTypeIdentities()
    {
        (string Type, string Library)[] cases =
        [
            (
                @"N.Outer\+Inner",
                """["P","1.2.3.4",null,null]"""),
            (
                @"N.Type\.Part",
                """["P","1.2.3.4","fr-FR","0011223344556677"]"""),
            (
                "Outer+Inner",
                """["P","1.2.3.4",null,"0011223344556677"]"""),
        ];

        foreach ((string type, string library) in cases)
        {
            string json =
                """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"type","l":"""
                + library
                + ",\"y\":"
                + JsonSerializer.Serialize(type)
                + """},"u":{"k":"package"}}]}""";

            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken);

            var context =
                Assert.IsType<PortableRetainedSubjectContext.EscapedType>(
                    packet.ViewStates[1].Context);
            Assert.Equal(type, context.EscapedTypeIdentity);
            string encoded = WorkspaceSharePacketCodec.Encode(packet);
            Assert.Equal(
                encoded,
                WorkspaceSharePacketCodec.Encode(
                    WorkspaceSharePacketCodec.Decode(
                        encoded,
                        TestContext.Current.CancellationToken)));
        }
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
                """{"f":5,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0}"""),
            WorkspaceSharePacketFailureKind.UnsupportedFormat);
    }

    [Theory]
    [InlineData(
        """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"y":"T","v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"}}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"package"},"u":{"k":"workspace"}}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[[":Platform",null,"net10.0",null]],"g":[[0]],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"workspace"}}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[[":Platform",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":{"k":"library","l":["P","1.2.3",null,null]},"u":{"k":"package"}}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    [InlineData(
        """{"f":2,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"q":[],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""",
        WorkspaceSharePacketFailureKind.InvalidShape)]
    public void Decode_RejectsInvalidOrUnsupportedFormat2Shape(
        string json,
        WorkspaceSharePacketFailureKind expected)
    {
        AssertFailure(EncodeJson(json), expected);
    }

    [Theory]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["b",{}],["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[0,1]}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{}],["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[0]}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{"b":[]}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[0]}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[1]}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[0,0]}]}""")]
    [InlineData(
        """{"f":2,"t":[["P","1.0.0","net11.0",null]],"g":[[0]],"a":0,"x":0,"q":[["a","{}"]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"q":[0]}]}""")]
    public void Format2_RejectsMalformedQueryTables(string json)
    {
        WorkspaceSharePacketException exception =
            Assert.Throws<WorkspaceSharePacketException>(
                () => WorkspaceSharePacketCodec.ParseJson(
                    json,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            WorkspaceSharePacketFailureKind.InvalidShape,
            exception.Kind);
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

    [Theory]
    [InlineData(
        WorkspaceSharePacketCodec.LegacyFormatVersion,
        WorkspaceSharePacketCodec.MaxFormat1Tabs)]
    [InlineData(
        WorkspaceSharePacketCodec.Format2Version,
        WorkspaceSharePacketCodec.MaxFormat2Tabs)]
    [InlineData(
        WorkspaceSharePacketCodec.CurrentFormatVersion,
        WorkspaceSharePacketCodec.MaxFormat3Tabs)]
    [InlineData(
        WorkspaceSharePacketCodec.Format4Version,
        WorkspaceSharePacketCodec.MaxFormat4Tabs)]
    public void Decode_EnforcesFormatCoordinateLimit(
        int formatVersion,
        int maxTabs)
    {
        WorkspaceSharePacket accepted = WorkspaceSharePacketCodec.ParseJson(
            PacketJson(formatVersion, maxTabs),
            TestContext.Current.CancellationToken);
        Assert.Equal(maxTabs, accepted.Tabs.Count);

        AssertFailure(
            EncodeJson(PacketJson(
                tabs: "["
                    + string.Join(
                        ',',
                        Enumerable.Range(0, maxTabs + 1)
                            .Select(index =>
                                $"[\"P{index}\",null,\"net10.0\",null]"))
                    + "]",
                contexts: "[[0]]",
                formatVersion: formatVersion)),
            WorkspaceSharePacketFailureKind.InvalidShape);
    }

    [Fact]
    public void Decode_RejectsOverLimitContextTable()
    {
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
            new string(
                'A',
                WorkspaceSharePacketCodec.MaxEncodedLength + 1),
            WorkspaceSharePacketFailureKind.EncodedLimitExceeded);

        string values = string.Join(
            ',',
            Enumerable.Repeat(
                "0",
                WorkspaceSharePacketCodec.MaxFormat1JsonValues + 1));
        AssertFailure(
            EncodeJson(
                $$"""{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"z":[{{values}}]}"""),
            WorkspaceSharePacketFailureKind.JsonValueLimitExceeded);

        string nested = new string(
            '[',
            WorkspaceSharePacketCodec.MaxFormat1JsonDepth + 1)
            + "0"
            + new string(']', WorkspaceSharePacketCodec.MaxFormat1JsonDepth + 1);
        AssertFailure(
            EncodeJson(
                $$"""{"f":1,"t":[["P",null,"net10.0",null]],"g":[[0]],"a":0,"x":0,"z":{{nested}}}"""),
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

    private static string PacketJson(int formatVersion, int tabCount)
    {
        string tabs = "["
            + string.Join(
                ',',
                Enumerable.Range(0, tabCount)
                    .Select(index =>
                        $"[\"P{index}\",\"1.0.0\",\"net11.0\",null]"))
            + "]";
        string contexts = "[["
            + string.Join(',', Enumerable.Range(0, tabCount))
            + "]]";
        return PacketJson(tabs, contexts, formatVersion);
    }

    private static string PacketJson(
        string tabs,
        string contexts,
        int formatVersion)
    {
        if (formatVersion == WorkspaceSharePacketCodec.LegacyFormatVersion)
            return PacketJson(tabs, contexts);

        string viewStates = """[{"t":null,"u":{"k":"workspace"}}"""
            + string.Concat(
                Enumerable.Range(0, CountTabs(tabs))
                    .Select(index => $",{{\"t\":{index}}}"))
            + "]";
        string registrations = formatVersion >= WorkspaceSharePacketCodec.CurrentFormatVersion
            ? "\"r\":[],"
            : "";
        return $$"""{"f":{{formatVersion}},"t":{{tabs}},"g":{{contexts}},{{registrations}}"a":0,"x":0,"v":{{viewStates}}}""";
    }

    private static int CountTabs(string tabs) =>
        tabs.Count(character => character == '[') - 1;

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
