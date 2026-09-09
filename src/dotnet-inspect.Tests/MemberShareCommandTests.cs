using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task MemberShare_PacketProjectsExactPackageMember()
    {
        var result = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            result.Output.Trim(),
            TestContext.Current.CancellationToken);
        WorkspaceShareTab tab = Assert.Single(packet.Tabs);
        Assert.Equal(WorkspaceShareSourceKind.Package, tab.SourceKind);
        Assert.Equal("Newtonsoft.Json", tab.Source);
        Assert.Equal("13.0.4", tab.Version);
        Assert.Equal("net6.0", tab.Framework);
        Assert.Equal(0, packet.ActiveTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal("api", packet.Lens);
        Assert.Equal("Newtonsoft.Json.JsonConvert", packet.Type);
        Assert.Matches("^[0-9a-f]{10}$", packet.MemberAnchor);
        Assert.Null(packet.MemberSignature);
        Assert.Null(packet.Section);
        Assert.Equal("Newtonsoft.Json", Assert.Single(packet.Libraries));
    }

    [Fact]
    public async Task MemberShare_UrlWrapsCanonicalPacket()
    {
        var result = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
            "--share",
            "url",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        var url = new Uri(result.Output.Trim());
        Assert.Equal("https", url.Scheme);
        Assert.Equal("dotnet-inspect.net", url.Host);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            url.Query[3..],
            TestContext.Current.CancellationToken);
        Assert.Equal("Newtonsoft.Json.JsonConvert", packet.Type);
        Assert.NotNull(packet.MemberAnchor);
    }

    [Fact]
    public async Task MemberShare_NeighboringOverloadsHaveDistinctAnchors()
    {
        var first = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
            "--share",
            "packet",
            "--tips",
            "q");
        var second = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:2",
            "--tfm",
            "net6.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(0, first.Exit);
        Assert.Equal(0, second.Exit);
        WorkspaceSharePacket firstPacket = WorkspaceSharePacketCodec.Decode(
            first.Output.Trim(),
            TestContext.Current.CancellationToken);
        WorkspaceSharePacket secondPacket = WorkspaceSharePacketCodec.Decode(
            second.Output.Trim(),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(firstPacket.MemberAnchor, secondPacket.MemberAnchor);
    }

    [Fact]
    public async Task MemberShare_RequiresExactMemberBeforeAcquisition()
    {
        var result = await RunAppAsync(
            "member",
            "Missing.Type",
            "--package",
            "Missing.Package",
            "Missing",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--share requires one exact member overload",
            result.Error);
        Assert.DoesNotContain(
            "Package 'Missing.Package'",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RejectsPlatformSource()
    {
        var result = await RunAppAsync(
            "member",
            "String",
            "--platform",
            "System.Private.CoreLib",
            "Equals:1",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "currently supports packages acquired from NuGet.org",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RejectsLocalPackage()
    {
        var (packagePath, tempDir) =
            CreateLocalRefPackage("System.Private.CoreLib");
        try
        {
            var result = await RunAppAsync(
                "member",
                "String",
                "--package",
                packagePath,
                "Equals:1",
                "--share",
                "packet",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "currently supports packages acquired from NuGet.org",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--raw")]
    [InlineData("--blob")]
    [InlineData("--count")]
    [InlineData("-S", "Signature")]
    [InlineData("--all")]
    public async Task MemberShare_RejectsConflictingModes(
        params string[] conflicting)
    {
        var result = await RunAppAsync(
            [
                "member",
                "Missing.Type",
                "--package",
                "Missing.Package",
                "Missing:1",
                "--share",
                "packet",
                "--tips",
                "q",
                .. conflicting,
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--share", result.Error);
        Assert.DoesNotContain(
            "Package 'Missing.Package'",
            result.Error);
    }
}
