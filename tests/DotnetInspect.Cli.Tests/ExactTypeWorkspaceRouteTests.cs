using System.IO.Compression;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactTypeWorkspaceRouteTests
{
    const string PackageId = "ilinspector.metadata.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task EligiblePinnedPackageRouteUsesInjectedWorkspaceCapabilities()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            "ILInspector.Metadata.ApiType",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "string? Accessibility { get; set; }",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RicherViewsRemainOnCompatibilityPath()
    {
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
        };

        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options,
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    Verbosity = Verbosity.Normal,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    IncludeSections = ["Summary"],
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    FormatExplicitlySet = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { IncludeAll = true },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { DocsExplicitlySet = true },
                out _));
    }

    static async Task<IPackageStore> CachedStoreAsync()
    {
        var store = new InMemoryPackageStore();
        byte[] package = Archive(
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
        return store;
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"The eligible route bypassed injected Workspace capabilities: "
                + request.RequestUri);
    }
}
