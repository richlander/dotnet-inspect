using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

// PR-fast: bounded canonical packet edits, without acquisition.
public sealed class WorkspacePackageScenarioDerivationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2.0.0")]
    public void ExistingMemberIsReusedWithoutReplacingVersionPolicy(string? requestedVersion)
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["System.Text.Json",null,"net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        WorkspaceSharePacketProjectionResult result =
            WorkspacePackageScenarioDerivation.Prepare(input, "system.text.json", requestedVersion);

        Assert.Same(input, result.Packet);
        Assert.Null(result.Failure);
        Assert.Null(input.Tabs[0].Version);
    }

    [Fact]
    public void AdditionUsesSelectedContextAndPreservesOtherContextsAndViews()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0","linux-x64"],["Example.Other","2.0.0","net9.0",null]],"g":[[0],[1]],"r":[["p","Polly."]],"a":1,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        string original = WorkspaceSharePacketCodec.Encode(input);
        var result = WorkspacePackageScenarioDerivation.Prepare(input, "System.Text.Json", "10.0.0");

        Assert.True(result.Succeeded, result.Failure?.Message);
        WorkspaceSharePacket derived = result.Packet!;
        Assert.Equal(original, WorkspaceSharePacketCodec.Encode(input));
        Assert.Equal([0, 2], derived.Contexts[0].TabIndexes);
        Assert.Equal([1], derived.Contexts[1].TabIndexes);
        Assert.Equal("net10.0", derived.Tabs[2].Framework);
        Assert.Equal("linux-x64", derived.Tabs[2].RuntimeIdentifier);
        Assert.Equal(1, derived.FocusedTabIndex); // Inspection owns subsequent activation.
        Assert.Equal(0, derived.SelectedContextIndex);
        Assert.Equal(input.Tabs, derived.Tabs.Take(2));
        for (int index = 0; index < input.ViewStates.Count; index++)
        {
            Assert.Equal(input.ViewStates[index].Subject, derived.ViewStates[index].Subject);
            Assert.Equal(input.ViewStates[index].Context, derived.ViewStates[index].Context);
            Assert.Equal(input.ViewStates[index].Facet, derived.ViewStates[index].Facet);
            Assert.Equal(input.ViewStates[index].Libraries, derived.ViewStates[index].Libraries);
            Assert.Equal(input.ViewStates[index].QueryIndexes, derived.ViewStates[index].QueryIndexes);
        }
        Assert.Equal(input.Registrations, derived.Registrations);
    }

    static WorkspaceSharePacket Parse(string json) =>
        WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken);
}
