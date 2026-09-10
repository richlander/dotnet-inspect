using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

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
        Assert.Empty(
            CommandLineBuilder.CreateRootCommand()
                .Parse(["workspace", "--root-request", "pkgroot1.a.b.c.d.e.f.g"])
                .Errors);
    }

    [Fact]
    public async Task RootRequest_CannotBeCombinedWithAPackageSelection()
    {
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = "pkgroot1.a.b.c.d.e.f.g",
                    Packages = [$"{PackageId}@{Version}"],
                    Tfm = Framework,
                },
                LoadOptions(new InMemoryPackageStore(), [Source]),
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
    [InlineData("pkgroot3.ZXhhbXBsZQ.MS4wLjA.dGVzdHM......Y29tcGF0aWJsZQ")]
    [InlineData("pkgroot3.ZXhhbXBsZQ.MS4wLjA.dGVzdHM...IA.bmV0MTEuMA..ZXhhY3Q")]
    [InlineData("pkgroot3.ZXhhbXBsZQ.MS4wLjA.dGVzdHM...bm90IHZhbGlk.bm90IHZhbGlk..ZXhhY3Q")]
    public async Task RootRequest_RefusesATokenThisToolDidNotIssue(string token)
    {
        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                LoadOptions(new InMemoryPackageStore(), [Source]),
                TestContext.Current.CancellationToken));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.Output);
        Assert.Contains(
            "must be a package Root reopening token",
            captured.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootRequest_ReopensTheExactRootTheTokenNames()
    {
        (InMemoryPackageStore store, string token, PackageRootReacquisitionRequest issued) =
            await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                LoadOptions(store, [Source]),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("# Workspace", captured.Output, StringComparison.Ordinal);
        Assert.Contains(
            issued.Coordinate.PackageId,
            captured.Output,
            StringComparison.Ordinal);
        Assert.Contains(Version, captured.Output, StringComparison.Ordinal);
        Assert.Contains(Framework, captured.Output, StringComparison.Ordinal);
        Assert.Empty(captured.Error);
    }

    [Fact]
    public async Task RootRequest_HonoursTheRequestedOutputFormat()
    {
        (InMemoryPackageStore store, string token, _) = await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = token,
                    Format = OutputFormat.Json,
                },
                LoadOptions(store, [Source]),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Contains("\"package\"", captured.Output, StringComparison.Ordinal);
        Assert.Contains("\"framework\"", captured.Output, StringComparison.Ordinal);
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
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    RootRequest = token,
                    Format = OutputFormat.Json,
                },
                LoadOptions(store, [Source]),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.Error);
        using JsonDocument document = JsonDocument.Parse(captured.Output);
        JsonElement root = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(issued.Coordinate.PackageId, root.GetProperty("package").GetString());
        Assert.Equal(Version, root.GetProperty("version").GetString());
        Assert.Equal(Framework, root.GetProperty("framework").GetString());
    }

    [Fact]
    public async Task RootRequest_ReportsAnUnauthorizedPinnedProducerRatherThanSubstituting()
    {
        (InMemoryPackageStore store, string token, _) = await IssueTokenAsync();

        var captured = await ConsoleCapture.RunAsync(
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                LoadOptions(store, [OtherSource]),
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
            () => WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions { RootRequest = token },
                LoadOptions(new InMemoryPackageStore(), [Source]),
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
                NuGetCache.GetSourceKey(Source.Url),
                stream,
                TestContext.Current.CancellationToken);
        }

        PackageRootAcquisitionOutcome outcome =
            await PackageRootAcquisition.AcquireAsync(
                PackageRootAcquisitionRequest.Create(PackageId, Version, Framework),
                LoadOptions(store, [Source]),
                TestContext.Current.CancellationToken);
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(outcome);
        Assert.Equal(selectionStatus, acquired.Binding.Root.AssetSelection.Status);
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
