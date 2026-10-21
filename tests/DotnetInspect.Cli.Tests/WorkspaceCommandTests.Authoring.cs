using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public sealed partial class WorkspaceCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultAuthoring_ProducesPortablePacketWithoutAcquisition(bool json)
    {
        using var client = new HttpClient(new RejectRequestsHandler());
        var captured = await ConsoleCapture.RunAsync(() => WorkspaceCommand.ExecuteAsync(
            new WorkspaceOptions
            {
                Packages = ["System.Text.Json@10.0.0"],
                Tfm = "net10.0",
                RegisteredEcosystems = ["runtime"],
                Format = json ? OutputFormat.Json : OutputFormat.Markdown,
            },
            LoadOptions(client, new FailOnAccessPackageStore()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        WorkspaceSharePacket packet = json
            ? WorkspaceSharePacketCodec.ParseJson(captured.Output.TrimEnd(), TestContext.Current.CancellationToken)
            : WorkspaceSharePacketCodec.Decode(captured.Output.TrimEnd(), TestContext.Current.CancellationToken);
        Assert.Equal("System.Text.Json", Assert.Single(packet.Tabs).Source);
        Assert.Single(packet.Contexts);
        Assert.IsType<DotnetInspector.Queries.WorkspaceRegistration.Ecosystem>(
            Assert.Single(packet.Registrations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultPacketInput_RetainsAllContextsAndViewWithoutRealization(bool json)
    {
        WorkspaceSharePacket packet = CreatePacket(
            ("System.Text.Json", "10.0.0", "net10.0"),
            ("Markout", "0.35.2", "net10.0"));
        using var client = new HttpClient(new RejectRequestsHandler());
        var captured = await ConsoleCapture.RunAsync(() => WorkspaceCommand.ExecuteAsync(
            new WorkspaceOptions
            {
                Packet = WorkspaceSharePacketCodec.Encode(packet),
                Format = json ? OutputFormat.Json : OutputFormat.Markdown,
            },
            LoadOptions(client, new FailOnAccessPackageStore()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        string encoded = json
            ? WorkspaceSharePacketCodec.Encode(
                WorkspaceSharePacketCodec.ParseJson(captured.Output.TrimEnd(), TestContext.Current.CancellationToken))
            : captured.Output.TrimEnd();
        Assert.Equal(WorkspaceSharePacketCodec.Encode(packet), encoded);
    }

    [Fact]
    public async Task DefaultPacketInput_PreservesSavedQueryWithoutExecutingIt()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            """
            {"f":3,"t":[],"g":[],"r":[],"a":null,"x":null,"q":[["package-query/v1",{"t":[["prefix","eq","Microsoft.Extensions."],["prerelease","eq","stable"]],"b":[["candidates",200]]}]],"v":[{"t":null,"u":{"k":"workspace"},"q":[0]}]}
            """,
            TestContext.Current.CancellationToken);
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        using var client = new HttpClient(new RejectRequestsHandler());
        var captured = await ConsoleCapture.RunAsync(() => WorkspaceCommand.ExecuteAsync(
            new WorkspaceOptions { Packet = encoded },
            LoadOptions(client, new FailOnAccessPackageStore()),
            TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        Assert.Equal(encoded, captured.Output.TrimEnd());
    }

    [Fact]
    public async Task DefaultAuthoring_DoesNotReadCredentialProvider()
    {
        string nonexistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pat");
        var captured = await RunCliAsync(
            "workspace", "--package", "System.Text.Json@10.0.0", "--tfm", "net10.0",
            "--nuget-source-auth-required", "https://example.test/v3/index.json",
            "--pat-for", "https://example.test/v3/index.json", "user", "file:" + nonexistentPath);

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(captured.Output.TrimEnd(), TestContext.Current.CancellationToken);
        Assert.Single(packet.PackageSources);
        Assert.DoesNotContain(nonexistentPath, captured.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareWorkspace_ShowsAuthoringAndInventoryHelp()
    {
        var captured = await RunCliAsync("workspace");
        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        Assert.Contains("--inventory", captured.Output, StringComparison.Ordinal);
        Assert.Contains("ecosystem", captured.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--active-package", "1")]
    [InlineData("--active-ecosystem", "runtime")]
    [InlineData("--library", "compile:lib/net10.0/System.Text.Json.dll")]
    [InlineData("--all-libraries", null)]
    [InlineData("--type", "System.Text.Json.JsonSerializer")]
    [InlineData("--member", "Serialize")]
    [InlineData("--lens", ".compare")]
    [InlineData("--root-request", "invalid")]
    public void RetiredWorkspaceSelectors_AreNotInTheGrammar(string option, string? value)
    {
        string[] args = value is null ? ["workspace", option] : ["workspace", option, value];
        Assert.NotEmpty(CommandLineBuilder.CreateRootCommand().Parse(args).Errors);
    }

    [Theory]
    [InlineData("--kind", "package")]
    [InlineData("--count", null)]
    [InlineData("--table", null)]
    [InlineData("--preview", null)]
    public async Task Authoring_RejectsLiveObservationOptions(string option, string? value)
    {
        var args = new List<string> { "workspace", "--register-ecosystem", "runtime", option };
        if (value is not null)
            args.Add(value);
        var captured = await RunCliAsync([.. args]);
        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("--inventory", captured.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--inventory")]
    [InlineData("--json")]
    public async Task Share_RejectsConflictingOutput(string other)
    {
        var captured = await RunCliAsync(
            "workspace", "--register-ecosystem", "runtime", "--share", "packet", other);
        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.NotEmpty(captured.Error);
    }

    sealed class RejectRequestsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Authoring must not send an HTTP request.");
    }
}
