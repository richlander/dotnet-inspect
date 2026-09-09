using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task DependsShare_PacketProjectsExactPackageDependencyView()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "Newtonsoft.Json@13.0.4",
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
        Assert.Equal("dependencies", packet.Lens);
        Assert.Null(packet.Type);
        Assert.Null(packet.MemberAnchor);
        Assert.Null(packet.MemberSignature);
        Assert.Null(packet.Section);
        Assert.Empty(packet.Libraries);
    }

    [Fact]
    public async Task DependsShare_UrlWrapsCanonicalPacket()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "NET10.0",
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
        Assert.Equal("10.0.0", Assert.Single(packet.Tabs).Version);
        Assert.Equal("net10.0", Assert.Single(packet.Tabs).Framework);
        Assert.Equal("dependencies", packet.Lens);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", "net6.0", "exact NuGet package version")]
    [InlineData("Newtonsoft.Json@latest", "net6.0", "exact NuGet package version")]
    [InlineData("Newtonsoft.Json@13.*", "net6.0", "exact NuGet package version")]
    [InlineData("Newtonsoft.Json@13.0.4+build", "net6.0", "exact NuGet package version")]
    [InlineData("Newtonsoft.Json@13.0.4", null, "target framework")]
    [InlineData("Newtonsoft.Json@13.0.4", "not/a/tfm", "target framework")]
    public async Task DependsShare_RequiresExactCoordinateBeforeAcquisition(
        string package,
        string? framework,
        string expectedError)
    {
        var arguments = new List<string>
        {
            "depends",
            "--package",
            package,
            "--share",
            "packet",
            "--tips",
            "q",
        };
        if (framework is not null)
        {
            arguments.Add("--tfm");
            arguments.Add(framework);
        }

        var result = await RunAppAsync([.. arguments]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error);
        Assert.DoesNotContain("could not be resolved", result.Error);
    }

    [Fact]
    public async Task DependsShare_RejectsLocalPackage()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "local.1.0.0.nupkg",
            "--tfm",
            "net8.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("local package archives", result.Error);
    }

    [Fact]
    public async Task DependsShare_RejectsConfiguredSource()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "Example.Private@1.0.0",
            "--tfm",
            "net8.0",
            "--source",
            "https://feed.example.test/v3/index.json",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("cannot be combined with source configuration", result.Error);
    }

    [Theory]
    [InlineData("String", "--platform")]
    [InlineData(null, "--library", "System.Runtime")]
    public async Task DependsShare_RejectsNonPackageModes(
        string? type,
        params string[] mode)
    {
        var arguments = new List<string> { "depends" };
        if (type is not null)
            arguments.Add(type);
        arguments.AddRange(mode);
        arguments.AddRange(
        [
            "--share",
            "packet",
            "--tips",
            "q",
        ]);

        var result = await RunAppAsync([.. arguments]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("requires exactly one --package", result.Error);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--markdown")]
    [InlineData("--mermaid")]
    [InlineData("--count")]
    [InlineData("--rows", "5")]
    public async Task DependsShare_RejectsConflictingOutput(
        params string[] conflicting)
    {
        var result = await RunAppAsync(
            [
                "depends",
                "--package",
                "Example.Package@1.0.0",
                "--tfm",
                "net8.0",
                "--share",
                "packet",
                "--tips",
                "q",
                .. conflicting,
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--share", result.Error);
    }
}
