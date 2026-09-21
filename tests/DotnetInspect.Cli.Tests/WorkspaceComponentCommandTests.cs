using System.Text.Json;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class WorkspaceComponentCommandTests
{
    [Fact]
    public async Task ComponentList_EmitsCanonicalPackageAndContextPaths()
    {
        string packet = Packet(
            """
            {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);

        var result = await RunCliAsync(
            "workspace",
            "component",
            "list",
            "--packet",
            packet);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "contexts/g0",
            document.RootElement
                .GetProperty("contexts")[0]
                .GetProperty("path")
                .GetString());
        Assert.Equal(
            "packages/system.text.json@10.0.0/net10.0/~",
            document.RootElement
                .GetProperty("packages")[0]
                .GetProperty("path")
                .GetString());
    }

    [Fact]
    public async Task PackageAddThenRemove_RoundTripsTheInputPacket()
    {
        string input = Packet(
            """
            {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);

        var added = await RunCliAsync(
            "workspace",
            "package",
            "add",
            "Microsoft.Extensions.Logging.Abstractions@10.0.0",
            "--packet",
            input);
        Assert.Equal(0, added.ExitCode);
        Assert.Empty(added.Error);

        var removed = await RunCliAsync(
            "workspace",
            "package",
            "remove",
            "packages/microsoft.extensions.logging.abstractions@10.0.0/net10.0/~",
            "--packet",
            added.Output.Trim());
        Assert.Equal(0, removed.ExitCode);
        Assert.Empty(removed.Error);
        Assert.Equal(input, removed.Output.Trim());
    }

    [Fact]
    public void RemovedWorkspaceState_PointsToNestedPacketCommands()
    {
        Assert.True(
            DotnetInspect.Cli.CommandLineBuilder.TryGetRemovedCommandError(
                ["workspace-state", "decode", "packet"],
                out string? error));
        Assert.Contains("workspace packet decode", error);
    }

    [Fact]
    public async Task PackageEdit_RejectsUnknownAndWrongKindPaths()
    {
        string packet = Packet(
            """
            {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);

        var unknown = await RunCliAsync(
            "workspace",
            "package",
            "remove",
            "packages/system.text.json@9.0.0/net10.0/~",
            "--packet",
            packet);
        var wrongKind = await RunCliAsync(
            "workspace",
            "package",
            "remove",
            "contexts/g0",
            "--packet",
            packet);

        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("UnknownComponent", unknown.Error);
        Assert.Equal(1, wrongKind.ExitCode);
        Assert.Contains("packages/ID@VERSION/TFM/RID", wrongKind.Error);
    }

    private static string Packet(string json) =>
        WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken));

    private static async Task<(
        int ExitCode,
        string Output,
        string Error)> RunCliAsync(params string[] args) =>
        await ConsoleCapture.RunAsync(
            () => DotnetInspect.Cli.CommandLineBuilder
                .CreateRootCommand()
                .Parse(args)
                .InvokeAsync());
}
