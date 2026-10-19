using System.CommandLine;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageRootRequestTests
{
    const string PackageId = "Exact.Root.Fixture";
    const string Version = "1.0.0";
    const string Framework = "net10.0";
    static readonly PackageSource Source = new("fixture", "https://fixture.invalid/v3/index.json");
    static readonly PackageSource OtherSource = new("other", "https://other.invalid/v3/index.json");

    [Fact]
    public async Task DefaultPackageChildrenPreserveTheFrozenCompileSelection()
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, ["--json"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Selected.dll", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Neighbor.dll", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("--files", "--json")]
    [InlineData("--files", "--count")]
    [InlineData("--files", "--paths")]
    [InlineData("--tfms", "--json")]
    [InlineData("--tfms", "--count")]
    public async Task ParserToInspectionUsesAdmittedContent(string section, string terminal)
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, [section, terminal]);
        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("--files")]
    [InlineData("--tfms")]
    public async Task RootSourceRetainsSemanticRowSelection(string section)
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, [section, "--count", "-n", "1"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1", result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("Other.Package")]
    [InlineData("--tfm", "net11.0")]
    [InlineData("--version", "2.0.0")]
    [InlineData("--versions")]
    [InlineData("--versions-with-feed")]
    [InlineData("--workspace", "packet")]
    [InlineData("--preview")]
    [InlineData("--share", "packet")]
    public async Task CompetingSourceIsRejectedBeforeAcquisition(params string[] conflict)
    {
        var result = await ExecuteAsync(new InMemoryPackageStore(), "not-a-token", conflict);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--root-request cannot be combined", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("pkgroot6.a.b.c")]
    public async Task InvalidTokenIsVisibleAndDoesNotAcquire(string token)
    {
        var result = await ExecuteAsync(new InMemoryPackageStore(), token, ["--files"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must be a package Root reopening token", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedProducerDoesNotUseSameNamedNeighbor()
    {
        var (store, token) = await IssueAsync();
        await CommitAsync(store, OtherSource, "neighbor-only.txt");
        var result = await ExecuteAsync(store, token, ["--files"], [OtherSource]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("ProducerNotAuthorized", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingExactContentDoesNotUseOtherProducerCache()
    {
        var (_, token) = await IssueAsync();
        var neighbor = new InMemoryPackageStore();
        await CommitAsync(neighbor, OtherSource, "neighbor-only.txt");
        var result = await ExecuteAsync(neighbor, token, ["--files"], allowMissing: true);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("PackageUnavailable", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplacementTargetDoesNotBecomeAReopenedRoot()
    {
        var (_, token) = await IssueAsync();
        var replacement = new InMemoryPackageStore();
        await CommitAsync(replacement, Source, "lib/net11.0/Neighbor.dll");
        var result = await ExecuteAsync(replacement, token, ["--files"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("SelectionRequestNotReproduced", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryNarrowingDoesNotPickHigherTargetNeighbor()
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, ["--library", "Neighbor.dll", "-S", "Library Info"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Neighbor.dll", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCompanionUsesAdmittedIdentity()
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, ["--files", "--count", "-E", ".tips"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(PackageId, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadmeContentDoesNotReacquireFromDisplayLabel()
    {
        var (store, token) = await IssueAsync();
        var result = await ExecuteAsync(store, token, ["--path", "lib/net10.0/retained.txt", "--content"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("retained Root content", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssemblyFreeRootStillSupportsPackageFiles(bool emptyCompileGroup)
    {
        var (store, token) = await IssueAsync(emptyCompileGroup ? "ref/net10.0/_._" : "lib/net10.0/no-assemblies.txt", neighbor: false);
        var result = await ExecuteAsync(store, token, ["--files", "--json"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(emptyCompileGroup ? "ref/net10.0/_._" : "lib/net10.0/no-assemblies.txt", result.Output, StringComparison.Ordinal);
    }

    static async Task<(InMemoryPackageStore Store, string Token)> IssueAsync(string? entry = null, bool neighbor = true)
    {
        var store = new InMemoryPackageStore();
        await CommitAsync(store, Source, entry ?? "lib/net10.0/Selected.dll", neighbor);
        using var client = new HttpClient(new ForbiddenNetwork());
        var load = LoadOptions(client, store, [Source]);
        await using var executor = Executor(store, load, [Source]);
        var outcome = await PackageRootAcquisition.AcquireAsync(
            PackageRootAcquisitionRequest.Create(PackageId, Version, Framework),
            executor.Execution, TestContext.Current.CancellationToken);
        var acquired = Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(outcome);
        return (store, acquired.Request.Encode());
    }

    static async Task CommitAsync(InMemoryPackageStore store, PackageSource producer, string entry, bool neighbor = true)
    {
        byte[] assembly = await File.ReadAllBytesAsync(typeof(PackageRootRequestTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = SnupkgPdbReaderTests.MakeSnupkg(
        [
            ($"{PackageId}.nuspec", System.Text.Encoding.UTF8.GetBytes(
                $"<package><metadata><id>{PackageId}</id><version>{Version}</version><authors>test</authors><description>fixture</description></metadata></package>")),
            (entry, entry.EndsWith(".dll", StringComparison.Ordinal) ? assembly : []),
            .. !neighbor || entry == "lib/net11.0/Neighbor.dll"
                ? Array.Empty<(string, byte[])>()
                : [("lib/net11.0/Neighbor.dll", assembly)],
            ("lib/net10.0/retained.txt", "retained Root content"u8.ToArray()),
        ]);
        using var stream = new MemoryStream(package);
        await store.CommitAsync(PackageId, Version,
            PackageSourceClientFactory.GetProducerIdentity(producer).Key,
            stream, TestContext.Current.CancellationToken);
    }

    static async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        InMemoryPackageStore store, string token, string[] arguments, PackageSource[]? sources = null, bool allowMissing = false)
    {
        sources ??= [Source];
        var shared = new SharedOptions();
        var package = PackageCommandDefinitions.CreatePackageCommand(shared, out var args);
        var root = new RootCommand { package };
        root.Options.Add(shared.Companion);
        string[] argv = ["package", "--root-request", token, .. arguments];
        string[] processed = CommandLineBuilder.PreprocessArgs(argv, root);
        using var client = new HttpClient(new ForbiddenNetwork(allowMissing));
        var load = LoadOptions(client, store, sources);
        await using var executor = Executor(store, load, sources, allowMissing);
        package.SetAction(async (parsed, ct) =>
        {
            var result = PackageOptionsParser.Parse(parsed, shared, args);
            Assert.True(result is PackageOptionsParser.Success, result.ToString());
            var success = (PackageOptionsParser.Success)result;
            return await PackageCommand.ExecuteRootRequestAsync(
                success.Options, new CommandContext(false, client, () => Composition(allowMissing)),
                load, executor.Execution, ct);
        });
        return await ConsoleCapture.RunAsync(() => CommandLineBuilder.InvokeAsync(root.Parse(processed), processed));
    }

    static WorkspaceContextLoadOptions LoadOptions(HttpClient client, IPackageStore store, PackageSource[] sources) => new()
    {
        HttpClient = client,
        PackageStore = store,
        SourceAuthorization = new UniformPackageSourceAuthorization(sources),
    };

    static ConfiguredPackageRootHouseExecutor Executor(IPackageStore store, WorkspaceContextLoadOptions load, PackageSource[] sources, bool allowMissing = false) =>
        new(Composition(allowMissing), new NuGetSourceOptions { Sources = [.. sources.Select(source => source.Url)] },
            load, (_, _) => store);

    static DesktopPackageSourceComposition Composition(bool allowMissing = false) => new(
        TimeSpan.FromSeconds(5), new UnavailableCredentials(), (_, _) => new ForbiddenNetwork(allowMissing));

    sealed class ForbiddenNetwork(bool allowMissing = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            allowMissing && request.RequestUri?.Host == "fixture.invalid"
                ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request })
                : throw new InvalidOperationException($"Unexpected source fallback: {request.RequestUri}");
    }

    sealed class UnavailableCredentials : ICredentialSource
    {
        public bool HasCredentialSources => false;
        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri source, bool isRetry, CancellationToken cancellationToken) =>
            Task.FromResult<PackageSourceCredential?>(null);
    }
}
