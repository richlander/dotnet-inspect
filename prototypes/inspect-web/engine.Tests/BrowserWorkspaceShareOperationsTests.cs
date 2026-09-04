using System.Runtime.Versioning;

using InspectWeb.Engine.CatalogFacade;

namespace InspectWeb.Engine.Tests;

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
