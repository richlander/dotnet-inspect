using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

// PR-fast: bounded declaration/codec operations with no runtime Workspace.
public sealed class WorkspaceEcosystemEditingTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Edit_PreservesFormatAndUnrelatedState(int format)
    {
        string sources = format == 5 ? "\"s\":[[\"https://api.nuget.org/v3/index.json\"]]," : "";
        string json = $$$"""
            {"f":{{{format}}},{{{sources}}}"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[["p","Microsoft.Extensions."]],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """;
        WorkspaceSharePacket input = WorkspaceSharePacketCodec.ParseJson(json, TestContext.Current.CancellationToken);
        string original = WorkspaceSharePacketCodec.Encode(input);
        var added = WorkspaceEcosystemEditor.Add(input, Declaration("ecosystem.json", "System.Text.Json"));
        Assert.True(added.Succeeded, added.Failure?.Message);
        var updated = WorkspaceEcosystemEditor.Update(added.Packet!, Declaration("ecosystem.json", "System.Text.Json.Serialization"));
        Assert.True(updated.Succeeded, updated.Failure?.Message);
        Assert.Equal(format, updated.Packet!.FormatVersion);
        Assert.Equal("Microsoft.Extensions.", Assert.IsType<WorkspaceRegistration.PackagePrefix>(updated.Packet.Registrations[0]).Prefix.Prefix);
        var ecosystem = Assert.IsType<WorkspaceRegistration.Ecosystem>(updated.Packet.Registrations[1]);
        Assert.Equal("System.Text.Json.Serialization", Assert.Single(ecosystem.Declaration.NamespaceRoots));
        var removed = WorkspaceEcosystemEditor.Remove(updated.Packet, ecosystem.Declaration.Id);
        Assert.True(removed.Succeeded, removed.Failure?.Message);
        Assert.Equal(original, WorkspaceSharePacketCodec.Encode(removed.Packet!));
        Assert.Equal(original, WorkspaceSharePacketCodec.Encode(input));
    }

    [Fact]
    public void Add_EnforcesPacketCapacityWithoutPartialOutput()
    {
        WorkspaceSharePacket? packet = null;
        for (int index = 0; index < WorkspaceSharePacketCodec.MaxRegistrations; index++)
        {
            var added = WorkspaceEcosystemEditor.Add(packet, Declaration($"ecosystem.json{index}", "System.Text.Json"));
            Assert.True(added.Succeeded, added.Failure?.Message);
            packet = added.Packet;
        }
        string original = WorkspaceSharePacketCodec.Encode(packet!);
        var refused = WorkspaceEcosystemEditor.Add(packet, Declaration("ecosystem.extra", "System.Text.Json"));
        Assert.False(refused.Succeeded);
        Assert.Null(refused.Packet);
        Assert.NotNull(refused.Failure);
        Assert.Equal(original, WorkspaceSharePacketCodec.Encode(packet!));
    }

    [Fact]
    public void Add_DoesNotSilentlyUpgradeFormat2()
    {
        var packet = WorkspaceSharePacketCodec.ParseJson(
            """{"f":2,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}""",
            TestContext.Current.CancellationToken);
        var result = WorkspaceEcosystemEditor.Add(packet, Declaration("ecosystem.json", "System.Text.Json"));
        Assert.False(result.Succeeded);
        Assert.Null(result.Packet);
        Assert.Contains("format 3, 4, or 5", result.Failure!.Message);
    }

    private static WorkspaceEcosystemRegistrationDeclaration Declaration(string id, string root) =>
        new(WorkspaceEcosystemRegistrationId.Create(id), [root], [new PackageCoordinate("System.Text.Json")], []);
}
