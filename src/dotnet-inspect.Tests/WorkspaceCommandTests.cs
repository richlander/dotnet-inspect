using System.Net;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class WorkspaceCommandTests
{
    const string PackageId = "Workspace.Command.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net10.0";

    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    [Fact]
    public void WorkspaceCommand_IsReservedFromImplicitPackageRouting()
    {
        string[] arguments =
            CommandLineBuilder.PreprocessArgs(["workspace", "--help"]);

        Assert.Equal("workspace", arguments[0]);
        Assert.Contains("workspace", CommandLineBuilder.KnownCommands);
    }

    [Fact]
    public void WorkspaceCommand_RegistersSharedOutputOptions()
    {
        string[] arguments =
            ["workspace", "--json", "--rows", "1", "--verbose"];

        var result = CommandLineBuilder.CreateRootCommand().Parse(arguments);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task EmptyWorkspace_RendersTheTypedEmptyInventory()
    {
        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions(),
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = new InMemoryPackageStore(),
                }));

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal(
            """
            # Workspace

            No package occurrences.

            """,
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task PopulatedWorkspace_RendersProductOccurrenceOrder()
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{Framework}/dotnet-inspect.Tests.dll", assembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages =
                    [
                        $"{PackageId}@{Version}",
                        $"{PackageId}@{Version}",
                    ],
                    Tfm = Framework,
                },
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("# Workspace", captured.Output);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Version, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Equal(
            2,
            captured.Output.Split(
                PackageId,
                StringSplitOptions.None).Length - 1);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task EmptyCompileGroup_IsReportedAsUnsupported()
    {
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"ref/{Framework}/_._", []),
            ($"lib/{Framework}/dotnet-inspect.Tests.dll", assembly));
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                sourceKey,
                stream,
                TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new FailingHandler());
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                },
                new WorkspaceContextLoadOptions
                {
                    HttpClient = client,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization([Source]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "requires each package to select at least one managed compile assembly",
            captured.Error);
        Assert.Contains("EmptyCompileGroup", captured.Error);
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
    }
}
