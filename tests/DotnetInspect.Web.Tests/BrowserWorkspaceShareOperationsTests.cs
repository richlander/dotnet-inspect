using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Ecosystems;

using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserWorkspaceShareOperationsTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("runtime")]
    [InlineData("aspnetcore")]
    public void HierarchyRecapturePreservesExplicitWorkspaceContext(string selection)
    {
        var coordinate = new DefinitionMemberCoordinate.PackageCoordinate(
            "Microsoft.Azure.SignalR", "1.33.1", "net8.0");
        WorkspaceRegistration[] registrations = selection == "none"
            ? []
            : [.. EcosystemPackCatalog.CreateWorkspacePlan(
                [selection == "runtime" ? EcosystemPackIds.Runtime : EcosystemPackIds.AspNetCore])
                .Registrations];
        var workspace = new WorkspaceDefinition(
            3, WorkspaceSharePacketTransposer.WorkspaceId,
            [new("g0", "net8.0", members: [coordinate])],
            registrations: registrations,
            traversalTargetPolicy: new("net10.0"));
        var navigation = new CommittedNavigationDefinition(
            3, WorkspaceSharePacketTransposer.NavigationId,
            [new("t0", coordinate: coordinate)], "t0");
        string basis = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketTransposer.ToPackageHierarchyPacket(
                workspace, navigation, "g0",
                TestContext.Current.CancellationToken).Packet!);
        var state = new BrowserWorkspaceShareState(
            [new("t0", "package", "Microsoft.Azure.SignalR", "1.33.1", "net8.0", null)],
            [new("g0", ["t0"])], "t0", "g0",
            new("hierarchy", null, null, null, null, [], null),
            HierarchyWorkspacePacket: basis);

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.True(encoded.Succeeded, encoded.Failure?.Message);
        Assert.Equal(basis, encoded.Packet);
        BrowserWorkspaceShareDecodeResult decoded =
            BrowserWorkspaceShareOperations.Decode(encoded.Packet!);
        Assert.True(decoded.Succeeded, decoded.Failure?.Message);
        Assert.Equal(basis, decoded.State!.HierarchyWorkspacePacket);
        Assert.Equal(
            basis, BrowserWorkspaceShareOperations.Encode(decoded.State).Packet);
    }

    [Theory]
    [InlineData(null, TraversalTargetFrameworkPolicy.ProductDefaultTargetFramework)]
    [InlineData("net10.0", "net10.0")]
    public void HierarchyCapture_PreservesGoverningTargetWithoutChangingRoot(
        string? configuredTarget,
        string expectedTarget)
    {
        var state = new BrowserWorkspaceShareState(
            [new("t0", "package", "Microsoft.Azure.SignalR", "1.33.1", "net8.0", null)],
            [new("g0", ["t0"])],
            "t0",
            "g0",
            new("hierarchy", null, null, null, null, [], null),
            configuredTarget);

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);
        Assert.True(encoded.Succeeded, encoded.Failure?.Message);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            Assert.IsType<string>(encoded.Packet),
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedTarget, packet.TraversalTargetPolicy.TargetFramework);
        Assert.Equal("net8.0", Assert.Single(packet.Tabs).Framework);
        BrowserWorkspaceShareState decoded =
            Assert.IsType<BrowserWorkspaceShareState>(
                BrowserWorkspaceShareOperations.Decode(encoded.Packet!).State);
        Assert.Equal(configuredTarget, decoded.TraversalTargetFramework);
        Assert.Equal(
            encoded.Packet,
            BrowserWorkspaceShareOperations.Encode(decoded).Packet);
    }

    [Fact]
    public void LegacyHierarchyDecode_RecapturePreservesTargetWithExplicitContext()
    {
        string legacy = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                """{"f":1,"t":[["Microsoft.Azure.SignalR","1.33.1","net8.0",null]],"g":[[0]],"a":0,"x":0,"v":"hierarchy"}""",
                TestContext.Current.CancellationToken));

        BrowserWorkspaceShareDecodeResult decoded =
            BrowserWorkspaceShareOperations.Decode(legacy);
        Assert.True(decoded.Succeeded, decoded.Failure?.Message);
        BrowserWorkspaceShareState state =
            Assert.IsType<BrowserWorkspaceShareState>(decoded.State);
        Assert.Equal("net8.0", state.TraversalTargetFramework);
        Assert.Equal(
            legacy,
            WorkspaceSharePacketCodec.Encode(WorkspaceSharePacketCodec.Decode(
                legacy, TestContext.Current.CancellationToken)));
        WorkspaceSharePacket recaptured = WorkspaceSharePacketCodec.Decode(
            BrowserWorkspaceShareOperations.Encode(state).Packet!,
            TestContext.Current.CancellationToken);
        Assert.Equal("net8.0", recaptured.TraversalTargetPolicy.TargetFramework);
        Assert.Contains(recaptured.Registrations, registration =>
            registration is WorkspaceRegistration.Ecosystem
            {
                Declaration.Id.Value: "ecosystem.runtime",
            });
    }

    private const string CanonicalVector =
        "eyJmIjoxLCJ0IjpbWyI6UGxhdGZvcm0iLCIxMC4wLjEwIiwibmV0MTAuMCIsbnVsbF0s"
        + "WyJTeXN0ZW0uVGV4dC5Kc29uIiwiMTAuMC4wIiwibmV0MTAuMCIsbnVsbF1dLCJnIjpb"
        + "WzAsMV1dLCJhIjoxLCJ4IjowLCJ2IjoiYXBpIiwieSI6IlN5c3RlbS5UZXh0Lkpzb24u"
        + "SnNvblNlcmlhbGl6ZXIiLCJsIjpbIlN5c3RlbS5UZXh0Lkpzb24iXX0";

    private const string IndependentFocusVector =
        "eyJmIjoxLCJ0IjpbWyJQIixudWxsLCJuZXQxMC4wIixudWxsXSxbIlEiLG51bGwsIm5l"
        + "dDEwLjAiLG51bGxdXSwiZyI6W1swXSxbMV1dLCJhIjoxLCJ4IjowfQ";

    private const string RegistrationOnlyFormat3Vector =
        "eyJmIjozLCJ0IjpbXSwiZyI6W10sInIiOltbInAiLCJNaWNyb3NvZnQuRXh0ZW5zaW"
        + "9ucy4iXV0sImEiOm51bGwsIngiOm51bGwsInYiOlt7InQiOm51bGwsInUiOnsiayI6I"
        + "ndvcmtzcGFjZSJ9fV19";

    [Fact]
    public void ExactMemberBodyComparison_RoundTripsWithoutChangingLegacyFormat()
    {
        var state = new BrowserWorkspaceShareState(
            [new("t0", "package", "System.Text.Json", "10.0.12", "netstandard2.0", null)],
            [new("g0", ["t0"])], "t0", "g0",
            new("api", "System.Text.Json.JsonDocumentOptions", "0123456789", null, "compare",
                ["compile:lib/netstandard2.0/System.Text.Json.dll"], null,
                new("9.0.20", "member-body", "compile:lib/netstandard2.0/System.Text.Json.dll", "Il",
                    "AllowDuplicateProperties~0123456789:1", "System.Text.Json", "10.0.0.0", null, "cc7b13ffcd2ddd51")));
        BrowserWorkspaceShareEncodeResult encoded = BrowserWorkspaceShareOperations.Encode(state);
        Assert.True(encoded.Succeeded, encoded.Failure?.Message);
        Assert.Equal(4, WorkspaceSharePacketCodec.Decode(encoded.Packet!, TestContext.Current.CancellationToken).FormatVersion);
        BrowserWorkspaceShareDecodeResult decoded = BrowserWorkspaceShareOperations.Decode(encoded.Packet!);
        Assert.True(decoded.Succeeded, decoded.Failure?.Message);
        Assert.Equal(state.View.Comparison, decoded.State!.View.Comparison);
        Assert.Equal(state.View.MemberAnchor, decoded.State.View.MemberAnchor);
        Assert.Equal(encoded.Packet, BrowserWorkspaceShareOperations.Encode(decoded.State).Packet);
    }

    [Fact]
    public void OversizedExactDiffValue_ReturnsTypedRefusal()
    {
        string asset = "compile:" + new string('a', 300);
        var state = new BrowserWorkspaceShareState(
            [new("t0", "package", "System.Text.Json", "10.0.12", "netstandard2.0", null)],
            [new("g0", ["t0"])], "t0", "g0",
            new("library:compare", null, null, null, null, [asset], null,
                new("9.0.20", "api", asset, "CSharp", null,
                    "System.Text.Json", "10.0.0.0", null, "cc7b13ffcd2ddd51")));

        BrowserWorkspaceShareEncodeResult result = BrowserWorkspaceShareOperations.Encode(state);

        Assert.False(result.Succeeded);
        Assert.Null(result.Packet);
        Assert.Equal("InvalidBrowserState", result.Failure!.Kind);
        Assert.Contains("256", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalPacket_RoundTripsThroughLongFormBrowserTransport()
    {
        BrowserWorkspaceShareDecodeResult decoded =
            BrowserWorkspaceShareOperations.Decode(CanonicalVector);

        Assert.True(decoded.Succeeded);
        Assert.Null(decoded.Failure);
        BrowserWorkspaceShareState state =
            Assert.IsType<BrowserWorkspaceShareState>(decoded.State);
        Assert.Equal(["t0", "t1"], state.Tabs.Select(tab => tab.Id));
        Assert.Equal("group", state.Tabs[0].Kind);
        Assert.Equal(":Platform", state.Tabs[0].Source);
        Assert.Equal("10.0.10", state.Tabs[0].Version);
        Assert.Equal("package", state.Tabs[1].Kind);
        Assert.Equal("System.Text.Json", state.Tabs[1].Source);
        Assert.Equal(["t0", "t1"], Assert.Single(state.Contexts).TabIds);
        Assert.Equal("t1", state.ActiveTabId);
        Assert.Equal("g0", state.SelectedContextId);
        Assert.Equal("api", state.View.Lens);
        Assert.Equal("System.Text.Json.JsonSerializer", state.View.Type);
        Assert.Null(state.View.SourceView);
        Assert.Equal(["System.Text.Json"], state.View.Libraries);

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.True(encoded.Succeeded);
        Assert.Null(encoded.Failure);
        Assert.Equal(CanonicalVector, encoded.Packet);
    }

    [Fact]
    public void DecompiledMemberSourceView_RoundTripsThroughCanonicalPacket()
    {
        var state = new BrowserWorkspaceShareState(
            [
                new BrowserWorkspaceShareTab(
                    "t0",
                    "package",
                    "System.Text.Json",
                    "11.0.0-preview.7.26381.103",
                    "net10.0",
                    RuntimeIdentifier: null),
            ],
            [new BrowserWorkspaceShareContext("g0", ["t0"])],
            ActiveTabId: "t0",
            SelectedContextId: "g0",
            new BrowserWorkspaceShareView(
                Lens: "api",
                Type: "System.Text.Json.JsonDocument",
                MemberAnchor: "78b5dfa21b",
                MemberSignature: null,
                Section: "source",
                Libraries: [],
                SourceView: "decompiler-source"));

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.True(encoded.Succeeded);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            Assert.IsType<string>(encoded.Packet),
            TestContext.Current.CancellationToken);
        Assert.Equal("decompiler-source", packet.SourceView);
        Assert.Contains(
            "\"o\":\"decompiler-source\"",
            WorkspaceSharePacketCodec.SerializeJson(packet),
            StringComparison.Ordinal);

        BrowserWorkspaceShareState decoded =
            Assert.IsType<BrowserWorkspaceShareState>(
                BrowserWorkspaceShareOperations.Decode(encoded.Packet!).State);
        Assert.Equal("decompiler-source", decoded.View.SourceView);
    }

    [Fact]
    public void IndependentFocusAndSelectedContext_RoundTripIndependently()
    {
        BrowserWorkspaceShareState state =
            Assert.IsType<BrowserWorkspaceShareState>(
                BrowserWorkspaceShareOperations.Decode(
                    IndependentFocusVector).State);
        Assert.Equal("t1", state.ActiveTabId);
        Assert.Equal("g0", state.SelectedContextId);
        Assert.Equal(["t0"], state.Contexts[0].TabIds);

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.Equal(IndependentFocusVector, encoded.Packet);
    }

    [Fact]
    public void CompleteCapture_AuthorsFormat3AndPreservesIndependentFocus()
    {
        BrowserWorkspaceShareState state =
            Assert.IsType<BrowserWorkspaceShareState>(
                BrowserWorkspaceShareOperations.Decode(
                    IndependentFocusVector).State);
        state = state with
        {
            Tabs =
            [
                .. state.Tabs.Select(tab => tab with
                {
                    Version = "1.0.0",
                    Framework = "net10.0",
                }),
            ],
        };

        BrowserWorkspaceShareEncodeResult captured =
            BrowserWorkspaceShareOperations.CaptureComplete(state);

        Assert.True(captured.Succeeded);
        Assert.Null(captured.Failure);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            Assert.IsType<string>(captured.Packet),
            TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceSharePacketCodec.CurrentFormatVersion,
            packet.FormatVersion);
        Assert.Equal(1, packet.FocusedTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
    }

    [Fact]
    public void CompleteCapture_RefusesNonRootViewsWithoutPartialPacket()
    {
        BrowserWorkspaceShareState state =
            Assert.IsType<BrowserWorkspaceShareState>(
                BrowserWorkspaceShareOperations.Decode(CanonicalVector).State);

        BrowserWorkspaceShareEncodeResult captured =
            BrowserWorkspaceShareOperations.CaptureComplete(state);

        Assert.False(captured.Succeeded);
        Assert.Null(captured.Packet);
        Assert.Equal("NonProjectable", captured.Failure?.Kind);
        Assert.Equal("view", captured.Failure?.Path);
    }

    [Fact]
    public void LegacyPacket_ReturnsTypedCodecFailureWithoutState()
    {
        const string legacyPacket =
            "W1siU3lzdGVtLlRleHQuSnNvbiIsIjEwLjAuMCIsIm5ldDEwLjAiXV0";

        BrowserWorkspaceShareDecodeResult result =
            BrowserWorkspaceShareOperations.Decode(legacyPacket);

        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal("InvalidShape", result.Failure?.Kind);
        Assert.Equal("packet", result.Failure?.Path);
    }

    [Fact]
    public void Format2Packet_ReturnsTypedUnsupportedFormatFailure()
    {
        const string format2Packet =
            "eyJmIjoyLCJ0IjpbWyJQIiwiMS4wLjAiLCJuZXQxMS4wIixudWxsXV0sImciOltb"
            + "MF1dLCJhIjpudWxsLCJ4IjowLCJ2IjpbeyJ0IjpudWxsLCJ1Ijp7ImsiOiJ3b3Jr"
            + "c3BhY2UifX0seyJ0IjowLCJ1Ijp7ImsiOiJ3b3Jrc3BhY2UifX1dfQ";

        BrowserWorkspaceShareDecodeResult result =
            BrowserWorkspaceShareOperations.Decode(format2Packet);

        Assert.False(result.Succeeded);
        Assert.Null(result.State);
        Assert.Equal("UnsupportedFormat", result.Failure?.Kind);
        Assert.Equal("packet", result.Failure?.Path);
        Assert.Contains("format 2", result.Failure?.Message);
    }

    [Fact]
    public void Format3Packet_RoundTripsThroughManagedBrowserBoundary()
    {
        BrowserWorkspaceShareEncodeResult result =
            BrowserWorkspaceShareOperations.Canonicalize(
                RegistrationOnlyFormat3Vector);

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(
            RegistrationOnlyFormat3Vector,
            result.Packet);
    }

    [Fact]
    public void ComposedRegistrations_RoundTripThroughProductionBrowserCodec()
    {
        var prefix = new DotnetInspector.SourceSelection.PackagePrefixDeclaration("Microsoft.Extensions.");
        WorkspaceSharePacketProjectionResult authored = WorkspaceRegistrationEditor.AddPrefix(null, prefix);
        Assert.True(authored.Succeeded, authored.Failure?.Message);
        var selected = Assert.IsType<DotnetInspector.Ecosystems.EcosystemWorkspaceRegistrationSelectionResult.Known>(
            DotnetInspector.Ecosystems.EcosystemPackCatalog.SelectWorkspaceRegistration(
                DotnetInspector.Ecosystems.EcosystemPackIds.AI));
        var composed = WorkspaceRegistrationEditor.AddEcosystem(authored.Packet, selected.Declaration);
        Assert.True(composed.Succeeded, composed.Failure?.Message);
        string packet = WorkspaceSharePacketCodec.Encode(composed.Packet!);

        BrowserWorkspaceShareEncodeResult result = BrowserWorkspaceShareOperations.Canonicalize(packet);

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(packet, result.Packet);
        WorkspaceSharePacket restored = WorkspaceSharePacketCodec.Decode(result.Packet!, TestContext.Current.CancellationToken);
        Assert.Equal(2, restored.Registrations.Count);
        var registration = Assert.IsType<DotnetInspector.Queries.WorkspaceRegistration.PackagePrefix>(restored.Registrations[0]);
        Assert.Equal(prefix, registration.Prefix);
        var ecosystem = Assert.IsType<DotnetInspector.Queries.WorkspaceRegistration.Ecosystem>(restored.Registrations[1]);
        Assert.Equal(selected.Declaration.NamespaceRoots.ToArray(), ecosystem.Declaration.NamespaceRoots.ToArray());
        Assert.NotEmpty(ecosystem.Declaration.CorePackages);
        Assert.Equal(selected.Declaration.CorePackages.Select(package => package.PackageId),
            ecosystem.Declaration.CorePackages.Select(package => package.PackageId));
    }

    [Fact]
    public void Format5Packet_RoundTripsThroughManagedBrowserBoundary()
    {
        const string json =
            """{"f":5,"s":[["https://nuget.pkg.github.com/example/index.json","a"]],"t":[["Private.Package","1.2.3","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""";
        string packet = WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken));

        BrowserWorkspaceShareEncodeResult result =
            BrowserWorkspaceShareOperations.Canonicalize(packet);

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(packet, result.Packet);
    }

    [Fact]
    public void InvalidBrowserTopology_ReturnsTypedTransportFailure()
    {
        var state = new BrowserWorkspaceShareState(
            [
                new BrowserWorkspaceShareTab(
                    "t0",
                    "package",
                    "P",
                    "1.0.0",
                    "net10.0",
                    RuntimeIdentifier: null),
            ],
            [new BrowserWorkspaceShareContext("g0", ["missing"])],
            ActiveTabId: "t0",
            SelectedContextId: "g0",
            new BrowserWorkspaceShareView(
                Lens: null,
                Type: null,
                MemberAnchor: null,
                MemberSignature: null,
                Section: null,
                Libraries: [],
                SourceView: null));

        BrowserWorkspaceShareEncodeResult result =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.False(result.Succeeded);
        Assert.Null(result.Packet);
        Assert.Equal("InvalidBrowserState", result.Failure?.Kind);
        Assert.Equal("state", result.Failure?.Path);
        Assert.Contains("unknown tab", result.Failure?.Message);
    }
}
