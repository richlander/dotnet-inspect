using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

// PR-fast: bounded declaration/codec operations with no runtime Workspace.
public sealed class WorkspaceRegistrationEditingTests
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
        var added = WorkspaceRegistrationEditor.AddPrefix(input, new PackagePrefixDeclaration("System.Text.Json"));
        Assert.True(added.Succeeded, added.Failure?.Message);
        Assert.Equal(format, added.Packet!.FormatVersion);
        Assert.Equal("Microsoft.Extensions.", Assert.IsType<WorkspaceRegistration.PackagePrefix>(added.Packet.Registrations[0]).Prefix.Prefix);
        var prefix = Assert.IsType<WorkspaceRegistration.PackagePrefix>(added.Packet.Registrations[1]);
        var removed = WorkspaceRegistrationEditor.RemovePrefix(added.Packet, prefix.Prefix);
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
            var added = WorkspaceRegistrationEditor.AddPrefix(packet, new PackagePrefixDeclaration($"System.Text.Json{index}"));
            Assert.True(added.Succeeded, added.Failure?.Message);
            packet = added.Packet;
        }
        string original = WorkspaceSharePacketCodec.Encode(packet!);
        var refused = WorkspaceRegistrationEditor.AddPrefix(packet, new PackagePrefixDeclaration("System.Text.Json.Extra"));
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
        var result = WorkspaceRegistrationEditor.AddPrefix(packet, new PackagePrefixDeclaration("System.Text.Json"));
        Assert.False(result.Succeeded);
        Assert.Null(result.Packet);
        Assert.Contains("format 3, 4, or 5", result.Failure!.Message);
    }

}
