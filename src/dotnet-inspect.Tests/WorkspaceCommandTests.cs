using System.Net;
using System.Text.Json;

using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
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

            """.ReplaceLineEndings(),
            captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task PopulatedWorkspace_CoalescesExactRoots()
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
            1,
            captured.Output.Split(
                PackageId,
                StringSplitOptions.None).Length - 1);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task EmptyCompileGroup_WithCompatibleLibrary_RemainsAWorkspaceRoot()
    {
        const string assetFramework = "net8.0";
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"ref/{Framework}/_._", []),
            ($"lib/{assetFramework}/dotnet-inspect.Tests.dll", assembly));
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

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task CompatibleCompileGroup_RendersTheRequestedFramework()
    {
        const string assetFramework = "net8.0";
        var store = new InMemoryPackageStore();
        string sourceKey = NuGetCache.GetSourceKey(Source.Url);
        byte[] assembly =
            await File.ReadAllBytesAsync(
                typeof(WorkspaceCommandTests).Assembly.Location,
                TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
            ($"lib/{assetFramework}/dotnet-inspect.Tests.dll", assembly));
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

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Theory]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Jsonl)]
    public async Task StructuredScope_PreservesOrderAndOnlyLowersPortableFacts(
        OutputFormat format)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.Z", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.A", ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.Z@1.0.0", "Workspace.A@1.0.0", "Workspace.Z@1.0.0"],
                    Tfm = Framework,
                    Format = format,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        string json = format == OutputFormat.Json
            ? captured.Output
            : $"[{string.Join(",", captured.Output.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))}]";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(["Workspace.Z", "Workspace.A"],
            rows.Select(row => row.GetProperty("package").GetString()));
        Assert.All(rows, row =>
        {
            Assert.Equal(["package", "version", "framework"],
                row.EnumerateObject().Select(property => property.Name));
            Assert.Equal(Version, row.GetProperty("version").GetString());
            Assert.Equal(Framework, row.GetProperty("framework").GetString());
        });
    }

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.Table)]
    [InlineData(OutputFormat.Tsv)]
    [InlineData(OutputFormat.PlainText)]
    public async Task RootOnlyPackage_RendersAcrossHumanFormats(OutputFormat format)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Format = format,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains(PackageId, captured.Output);
        Assert.Contains(Version, captured.Output);
        Assert.Contains(Framework, captured.Output);
        Assert.Empty(captured.Error);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net11.0")]
    public async Task FailedReplacement_DoesNotRenderASuccessfulPrefix(string assetFramework)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.Good", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.Bad",
            ($"lib/{assetFramework}/Bad.dll", [1, 2, 3]));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.Good@1.0.0", "Workspace.Bad@1.0.0"],
                    Tfm = Framework,
                    Format = OutputFormat.Json,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.NotEmpty(captured.Error);
    }

    [Fact]
    public async Task Count_UsesTheCoalescedCommittedRoots()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, PackageId, ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = [$"{PackageId}@{Version}", $"{PackageId}@{Version}"],
                    Tfm = Framework,
                    Count = true,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Equal("1", captured.Output.Trim());
        Assert.Empty(captured.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowWindow_AppliesToCommittedRootsBeforeRenderingOrCounting(bool count)
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(store, "Workspace.A", ("readme.txt", []));
        await AddPackageAsync(store, "Workspace.B", ("readme.txt", []));
        using var client = new HttpClient(new FailingHandler());

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = ["Workspace.A@1.0.0", "Workspace.A@1.0.0", "Workspace.B@1.0.0"],
                    Tfm = Framework,
                    Format = OutputFormat.Json,
                    Rows = RowWindow.Range(2, null),
                    Count = count,
                },
                LoadOptions(client, store),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        if (count)
        {
            Assert.Equal("1", captured.Output.Trim());
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(captured.Output);
            JsonElement row = Assert.Single(document.RootElement.EnumerateArray());
            Assert.Equal("Workspace.B", row.GetProperty("package").GetString());
        }
    }

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([Source]),
        PackageStore = store,
    };

    static async Task AddPackageAsync(
        InMemoryPackageStore store, string packageId,
        params (string Path, byte[] Content)[] entries)
    {
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            [(packageId + ".nuspec", "<package />"u8.ToArray()), .. entries]);
        using var stream = new MemoryStream(package);
        await store.CommitAsync(
            packageId, Version, NuGetCache.GetSourceKey(Source.Url),
            stream, TestContext.Current.CancellationToken);
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
