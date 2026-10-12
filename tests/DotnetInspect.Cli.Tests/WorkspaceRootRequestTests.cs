using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Covers the exact package Root reopening route: <c>workspace --root-request</c>
/// opens the Root an owner-issued token names, through this host's ordinary
/// source authorization and package store, and reports every typed failure
/// rather than falling back to a neighbouring Root.
/// </summary>
[Collection("Console")]
public sealed class WorkspaceRootRequestTests
{
    const string PackageId = "Workspace.RootRequest.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net10.0";

    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    static readonly PackageSource OtherSource =
        new("other", "https://other.invalid/v3/index.json");

    [Fact]
    public void RootRequestOption_IsAcceptedAndRejectsPackageSelection()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        Assert.Empty(root
            .Parse(["workspace", "--root-request", "pkgroot1.a.b.c.d.e.f.g"])
            .Errors);
        var workspace = root.Subcommands.Single(
            command => command.Name == "workspace");
        var option = Assert.Single(
            workspace.Options,
            option => option.Name == "--root-request");
        Assert.Contains(
            "library-literal=TEXT",
            option.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootRequest_CannotBeCombinedWithAPackageSelection()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = "pkgroot1.a.b.c.d.e.f.g",
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                },
                new InMemoryPackageStore(),
                [Source],
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("--root-request", captured.Error, StringComparison.Ordinal);
        Assert.Contains("cannot be combined", captured.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("pkgroot1.a.b.c")]
    [InlineData("pkgroot2.a.b.c.d.e.f.g")]
    [InlineData("pkgroot1.!!!.b.c.d.e.f.g")]
    public async Task RootRequest_RefusesATokenThisToolDidNotIssue(string token)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                new InMemoryPackageStore(),
                [Source],
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "must be a package Root reopening token",
            captured.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "library-literal=TEXT",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootRequest_ReopensTheExactRootTheTokenNames()
    {
        (InMemoryPackageStore store, string token, PackageRootReacquisitionRequest issued) =
            await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                store,
                [Source],
                TestContext.Current.CancellationToken));

        Assert.True(captured.ExitCode == 0, captured.Error);
        Assert.Contains("# Workspace", captured.Output, StringComparison.Ordinal);
        Assert.Contains(
            issued.Coordinate.PackageId,
            captured.Output,
            StringComparison.Ordinal);
        Assert.Contains(Version, captured.Output, StringComparison.Ordinal);
        Assert.Contains("Ready", captured.Output, StringComparison.Ordinal);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task RootRequest_HonoursTheRequestedOutputFormat()
    {
        (InMemoryPackageStore store, string token, _) = await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = token,
                    Format = OutputFormat.Json,
                },
                store,
                [Source],
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("\"package_id\"", captured.Output, StringComparison.Ordinal);
        Assert.Contains(
            "\"requested_target_framework\"",
            captured.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PackageCompileAssetSelectionStatus.NoCompileAssets)]
    [InlineData(PackageCompileAssetSelectionStatus.EmptyCompileGroup)]
    public async Task RootRequest_ReopensAnAssemblyFreeRoot(
        PackageCompileAssetSelectionStatus selectionStatus)
    {
        (InMemoryPackageStore store, string token, PackageRootReacquisitionRequest issued) =
            await IssueTokenAsync(selectionStatus);

        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = token,
                    Format = OutputFormat.Json,
                },
                store,
                [Source],
                TestContext.Current.CancellationToken));

        Assert.True(captured.ExitCode == 0, captured.Error);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement root = Assert.Single(
            document.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(
            issued.Coordinate.PackageId,
            root.GetProperty("package_id").GetString());
        Assert.Equal(
            Version,
            root.GetProperty("package_version").GetString());
        Assert.Equal(
            Framework,
            root.GetProperty("requested_target_framework").GetString());
        Assert.Equal(
            (int)selectionStatus,
            root.GetProperty("asset_selection_status").GetInt32());
    }

    [Fact]
    public async Task RootRequest_ReportsAnUnauthorizedPinnedProducerRatherThanSubstituting()
    {
        (InMemoryPackageStore store, string token, _) = await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                store,
                [OtherSource],
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("could not be reopened", captured.Error, StringComparison.Ordinal);
        Assert.Contains(
            PackageRootAcquisitionFailureKind.ProducerNotAuthorized.ToString(),
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootRequest_ReportsUnavailableContentRatherThanAnEmptyWorkspace()
    {
        (_, string token, _) = await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                new InMemoryPackageStore(),
                [Source],
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains("could not be reopened", captured.Error, StringComparison.Ordinal);
        Assert.Contains(
            PackageRootAcquisitionFailureKind.PackageUnavailable.ToString(),
            captured.Error,
            StringComparison.Ordinal);
    }

    static async Task<(InMemoryPackageStore Store, string Token, PackageRootReacquisitionRequest Issued)>
        IssueTokenAsync(
            PackageCompileAssetSelectionStatus selectionStatus =
                PackageCompileAssetSelectionStatus.Selected)
    {
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(WorkspaceRootRequestTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        (string Path, byte[] Content)[] entries = selectionStatus switch
        {
            PackageCompileAssetSelectionStatus.Selected =>
                [($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly)],
            PackageCompileAssetSelectionStatus.NoCompileAssets =>
                [("readme.txt", [])],
            PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                [
                    ($"ref/{Framework}/_._", []),
                    ("lib/net8.0/DotnetInspect.Cli.Tests.dll", assembly),
                ],
            _ => throw new ArgumentOutOfRangeException(nameof(selectionStatus)),
        };
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
            [
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()),
                .. entries,
            ]);
        using (var stream = new MemoryStream(package))
        {
            await store.CommitAsync(
                PackageId,
                Version,
                PackageSourceClientFactory
                    .GetProducerIdentity(Source).Key,
                stream,
                TestContext.Current.CancellationToken);
        }

        WorkspaceContextLoadOptions loadOptions =
            LoadOptions(store, [Source]);
        DesktopPackageSourceComposition composition =
            CreateComposition();
        await using var executor =
            new ConfiguredPackageRootHouseExecutor(
                composition,
                new NuGetSourceOptions
                {
                    Sources = [Source.Url],
                },
                loadOptions,
                (_, _) => store);
        PackageRootAcquisitionOutcome outcome =
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(PackageId, Version, Framework),
                executor.Execution,
                TestContext.Current.CancellationToken);
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(outcome);
        Assert.Equal(selectionStatus, acquired.Binding.Root.AssetSelection.Status);
        if (selectionStatus
            == PackageCompileAssetSelectionStatus.EmptyCompileGroup)
        {
            Assert.Equal(
                Framework,
                acquired.Request.CompileTargetFramework);
            Assert.Equal(
                "net8.0",
                acquired.Request.SelectionTargetFramework);
            Assert.False(
                acquired.Request.AllowsCompatibleTargetSelection);
            Assert.False(
                acquired.Request.UsesCompatibleImplementationSelection);
        }
        return (store, acquired.Request.Encode(), acquired.Request);
    }

    static WorkspaceContextLoadOptions LoadOptions(
        IPackageStore store,
        PackageSource[] sources) =>
        new()
        {
            HttpClient = new HttpClient(new FailingHandler()),
            SourceAuthorization = new UniformPackageSourceAuthorization(sources),
            PackageStore = store,
        };

    static async Task<int> ExecuteAsync(
        WorkspaceOptions options,
        IPackageStore store,
        PackageSource[] sources,
        CancellationToken cancellationToken)
    {
        WorkspaceContextLoadOptions loadOptions =
            LoadOptions(store, sources);
        DesktopPackageSourceComposition composition =
            CreateComposition();
        await using var executor =
            new ConfiguredPackageRootHouseExecutor(
                composition,
                new NuGetSourceOptions
                {
                    Sources =
                    [
                        .. sources.Select(
                            static source => source.Url),
                    ],
                },
                loadOptions,
                (_, _) => store);
        return await WorkspaceCommand.ExecuteAsync(
            options,
            loadOptions,
            executor.Execution,
            cancellationToken);
    }

    static DesktopPackageSourceComposition CreateComposition() =>
        new(
            TimeSpan.FromSeconds(5),
            new UnavailableCredentials(),
            (_, _) => new FailingHandler());

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

    sealed class UnavailableCredentials : ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri source,
            bool isRetry,
            CancellationToken cancellationToken) =>
            Task.FromResult<PackageSourceCredential?>(null);
    }
}
