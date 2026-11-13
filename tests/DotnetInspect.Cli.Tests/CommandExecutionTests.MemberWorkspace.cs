using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    // PR-fast: real production route over the existing pinned gallery asset.
    [Fact]
    public async Task MemberWorkspace_ParserToCompletedInspectionAndDerivedShare()
    {
        string[] args = ["member", "System.Text.Json.JsonSerializer", "-m", "Serialize:1",
            "--workspace", MemberWorkspaceRouteTests.Packet(), "--json"];
        var ordinary = await RunAppAsync(args);
        var shared = await RunAppAsync([.. args, "--share", "packet"]);
        Assert.True(ordinary.Exit == 0, ordinary.Error);
        Assert.True(shared.Exit == 0, shared.Error);
        Assert.Equal(ordinary.Output, shared.Output);
        Assert.Contains("Serialize", shared.Output, StringComparison.Ordinal);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            shared.Error.Trim(), TestContext.Current.CancellationToken);
        Assert.Equal(2, packet.Tabs.Count);
        Assert.Equal(0, packet.FocusedTabIndex);
        Assert.Equal(PortableRetainedSubjectContextKind.Member,
            packet.ViewStates[1].Context!.Kind);
        Assert.NotNull(Assert.IsType<PortableRetainedSubjectContext.EscapedMember>(
            packet.ViewStates[1].Context).MemberAnchor);
    }

    [Fact]
    public async Task MemberWorkspace_MatchConflictFailsBeforeAcquisition()
    {
        var result = await RunAppAsync("member", "System.Text.Json.JsonSerializer", "-m",
            "Serialize:1", "--workspace", "invalid", "--match");
        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--workspace cannot be combined with --match", result.Error);
    }
}
