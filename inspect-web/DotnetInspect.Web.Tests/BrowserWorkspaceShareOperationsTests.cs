using System.Runtime.Versioning;
using DotnetInspector.Queries.Definitions;

using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserWorkspaceShareOperationsTests
{
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
        Assert.Equal(["System.Text.Json"], state.View.Libraries);

        BrowserWorkspaceShareEncodeResult encoded =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.True(encoded.Succeeded);
        Assert.Null(encoded.Failure);
        Assert.Equal(CanonicalVector, encoded.Packet);
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
                Libraries: []));

        BrowserWorkspaceShareEncodeResult result =
            BrowserWorkspaceShareOperations.Encode(state);

        Assert.False(result.Succeeded);
        Assert.Null(result.Packet);
        Assert.Equal("InvalidBrowserState", result.Failure?.Kind);
        Assert.Equal("state", result.Failure?.Path);
        Assert.Contains("unknown tab", result.Failure?.Message);
    }
}
