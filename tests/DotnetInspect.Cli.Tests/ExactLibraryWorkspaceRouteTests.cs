using System.IO.Compression;
using System.Text.Json;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactLibraryWorkspaceRouteTests
{
    const string PackageId = "exact-library-cli.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string Library = "ILInspector.Metadata.dll";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task EligibleListingUsesInjectedWorkspaceCapabilities()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = Library,
            Tfm = Framework,
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
            "# exact-library-cli.test",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnvelopePreservesExplicitTipsOnStderr()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = Library,
            Tfm = Framework,
            EnvelopeOutput = true,
            CompactJson = true,
            TipLevel = TipLevel.Detailed,
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
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "exact-library-api",
            document.RootElement.GetProperty("result_kind").GetString());
        Assert.Contains("Tips:", error, StringComparison.Ordinal);
        Assert.Contains(
            "inspect type members",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RicherAndNonExactRequestsRemainOnCompatibilityPath()
    {
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = Library,
            Tfm = Framework,
        };

        Assert.True(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options,
                out ExactLibraryApiInspectionRequest? exactRequest));
        Assert.Equal(Version, exactRequest.PackageVersion);
        Assert.True(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    PackagePath = $"{PackageId}@1.0",
                },
                out ExactLibraryApiInspectionRequest? normalizedRequest));
        Assert.Equal("1.0.0", normalizedRequest.PackageVersion);
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    PackagePath = PackageId,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    PackagePath = $"{PackageId}@1.0.0..2.0.0",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    PlatformFramework = "net9.0",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    WorkspacePacket = "packet",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    JsonOutput = true,
                    Verbosity = Verbosity.Normal,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    JsonOutput = true,
                    Verbosity = Verbosity.Detailed,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    Tfm = "all",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    TypeName = typeof(ApiType).FullName,
                },
                out _));
        Assert.True(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    JsonOutput = true,
                    FormatExplicitlySet = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    DocsExplicitlySet = true,
                    ShowDocs = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    JsonOutput = true,
                    Tree = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactLibraryApiRequest(
                options with
                {
                    JsonOutput = true,
                    PlainText = true,
                },
                out _));
    }

    [Theory]
    [InlineData("n")]
    [InlineData("d")]
    [Trait("Speed", "Slow")]
    public async Task RicherJsonVerbosityUsesCompatibilityPath(
        string verbosity)
    {
        string[] arguments =
        [
            "type",
            "--package",
            "System.Text.Json@10.0.0",
            "--library",
            "System.Text.Json.dll",
            "--tfm",
            "net10.0",
            "--json",
            "--compact",
            $"-v:{verbosity}",
            "-T",
            "q",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeAsync(
                    root.Parse(processed),
                    processed));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement json = document.RootElement;
        Assert.True(json.TryGetProperty("types", out JsonElement types));
        Assert.True(types.GetArrayLength() > 0);
        Assert.False(json.TryGetProperty("outcome", out _));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RowSelectedJsonUsesCompatibilityPath()
    {
        string[] arguments =
        [
            "type",
            "--package",
            "System.Text.Json@10.0.0",
            "--library",
            "System.Text.Json.dll",
            "--tfm",
            "net10.0",
            "--json",
            "--compact",
            "-n",
            "1",
            "-T",
            "q",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeAsync(
                    root.Parse(processed),
                    processed));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement json = document.RootElement;
        Assert.True(json.TryGetProperty("types", out JsonElement types));
        Assert.Equal(1, types.GetArrayLength());
        Assert.False(json.TryGetProperty("outcome", out _));
    }

    [Fact]
    public async Task EnvelopeContentMatchesUnprojectedJson()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var baseline = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = Library,
            Tfm = Framework,
            TipLevel = TipLevel.Quiet,
            CompactJson = true,
        };
        WorkspaceContextLoadOptions capabilities = new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

        (int jsonExit, string jsonOutput, string jsonError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    baseline with
                    {
                        JsonOutput = true,
                        Format = OutputFormat.Json,
                        FormatExplicitlySet = true,
                        FormatFlagExplicitlySet = true,
                    },
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(baseline),
                    capabilities));
        (int envelopeExit, string envelopeOutput, string envelopeError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    baseline with { EnvelopeOutput = true },
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(baseline),
                    capabilities));

        Assert.Equal(0, jsonExit);
        Assert.Equal(0, envelopeExit);
        Assert.Empty(jsonError);
        Assert.Empty(envelopeError);
        using JsonDocument contentDocument =
            JsonDocument.Parse(jsonOutput);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelopeOutput);
        JsonElement root = envelopeDocument.RootElement;
        Assert.Equal(
            "exact-library-api",
            root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(
            contentDocument.RootElement,
            root.GetProperty("content")));
        Assert.Equal(
            "available",
            root.GetProperty("share").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task MissingLibraryPreservesCompatibilityError()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = "Missing.dll",
            Tfm = Framework,
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

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Library 'Missing.dll' not found in package.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingLibraryContentJsonRemainsVisible()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            AssemblyPath = "Missing.dll",
            Tfm = Framework,
            TipLevel = TipLevel.Quiet,
            JsonOutput = true,
            Format = OutputFormat.Json,
            FormatExplicitlySet = true,
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

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("outcome", out _));
        Assert.Contains(
            "Library 'Missing.dll' not found in package.",
            error,
            StringComparison.Ordinal);
    }

    static async Task<IPackageStore> CachedStoreAsync()
    {
        var store = new InMemoryPackageStore();
        byte[] package = Archive(
            ($"ref/{Framework}/{Library}",
                await File.ReadAllBytesAsync(
                    typeof(ApiSurface).Assembly.Location,
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
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
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
                $"Unexpected request to {request.RequestUri}.");
    }
}
