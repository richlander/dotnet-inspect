using System.Net;
using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task DependsTypeShareAppendsUrlWithoutChangingJsonContent()
    {
        var result = await RunAppAsync(
            "depends",
            "NpgsqlOptionsExtension",
            "--package",
            "Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4",
            "--tfm",
            "net8.0",
            "--json",
            "--share",
            "url",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension",
            document.RootElement
                .GetProperty("nodes")[0]
                .GetProperty("label")
                .GetString());
        string shareLine = result.Error
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Last();
        Assert.StartsWith(
            "https://dotnet-inspect.net/?w=",
            shareLine,
            StringComparison.Ordinal);
    }

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
    public async Task DependsShare_PacketPreservesCompatibleRequestedFramework()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "--tfm",
            "net8.0",
            "--source",
            "https://api.nuget.org/v3/index.json",
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
        Assert.Equal("Newtonsoft.Json", tab.Source);
        Assert.Equal("13.0.4", tab.Version);
        Assert.Equal("net8.0", tab.Framework);
        Assert.Equal("dependencies", packet.Lens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("url")]
    public async Task DependsShare_UrlWrapsCanonicalPacket(string? format)
    {
        var arguments = new List<string>
        {
            "depends",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "NET10.0",
            "--share",
        };
        if (format is not null)
            arguments.Add(format);
        arguments.AddRange(["--tips", "q"]);

        var result = await RunAppAsync([.. arguments]);

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
    [InlineData("")]
    [InlineData("@latest")]
    public async Task DependsShare_FloatingVersionResolvesWithoutPackageAcquisition(
        string versionSelector)
    {
        string packageId = $"Share.Floating.{Guid.NewGuid():N}";
        using var handler = new ShareVersionHandler(packageId, "2.0.0");
        using var httpClient = new HttpClient(handler);

        var result = await ConsoleCapture.RunAsync(
            () => DependsShareProjection.WriteAsync(
                new DependsOptions
                {
                    PackageName = packageId + versionSelector,
                    Tfm = "net8.0",
                    ShareFormat = WorkspaceShareFormat.Packet,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = ["https://api.nuget.org/v3/index.json"],
                    },
                },
                httpClient,
                new VerboseLogger(enabled: false),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            result.Output.Trim(),
            TestContext.Current.CancellationToken);
        WorkspaceShareTab tab = Assert.Single(packet.Tabs);
        Assert.Equal(packageId, tab.Source);
        Assert.Equal("2.0.0", tab.Version);
        Uri request = Assert.Single(handler.Requests);
        Assert.Equal("azuresearch-usnc.nuget.org", request.Host);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Newtonsoft.Json@13.*", "net6.0", "exact normalized NuGet version")]
    [InlineData("Newtonsoft.Json@13.0.4+build", "net6.0", "exact normalized NuGet version")]
    [InlineData("Newtonsoft.Json@13.0.4", null, "target framework")]
    [InlineData("Newtonsoft.Json@13.0.4", "not/a/tfm", "target framework")]
    public async Task DependsShare_RejectsNonProjectableCoordinate(
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
    public async Task DependsShare_RejectsNonNuGetOrgSource()
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
        Assert.Contains(
            "authorize exactly one NuGet.org source",
            result.Error);
    }

    [Fact]
    public async Task DependsShare_AcceptsExplicitNuGetOrgSource()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "Example.Package@1.0.0",
            "--tfm",
            "net8.0",
            "--source",
            "https://api.nuget.org/v3/index.json",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            result.Output.Trim(),
            TestContext.Current.CancellationToken);
        Assert.Equal("Example.Package", Assert.Single(packet.Tabs).Source);
    }

    [Fact]
    public async Task DependsShare_RejectsMappedPrivateEffectiveSource()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"depends-share-source-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "NuGet.Config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="private" value="https://feed.example.test/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="nuget.org">
                  <package pattern="Public.*" />
                </packageSource>
                <packageSource key="private">
                  <package pattern="Example.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """,
            TestContext.Current.CancellationToken);

        try
        {
            using var handler =
                new ShareVersionHandler("Example.Private", "1.0.0");
            using var client = new HttpClient(handler);
            var options = new DependsOptions
            {
                PackageName = "Example.Private@1.0.0",
                Tfm = "net8.0",
                ShareFormat = WorkspaceShareFormat.Packet,
                SourceOptions = new NuGetSourceOptions
                {
                    ConfigDirectory = root,
                },
            };

            var result = await ConsoleCapture.RunAsync(() =>
                DependsShareProjection.WriteAsync(
                    options,
                    client,
                    new VerboseLogger(enabled: false)));

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "authorize exactly one NuGet.org source",
                result.Error);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DependsShare_RejectsBrowserPlatformPackageId()
    {
        var result = await RunAppAsync(
            "depends",
            "--package",
            "Microsoft.NETCore.App@2.2.8",
            "--tfm",
            "netcoreapp2.2",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "published Browser reserves that id for the .NET Platform",
            result.Error);
        Assert.DoesNotContain("could not be resolved", result.Error);
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
    [InlineData("--plaintext")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--tree")]
    [InlineData("--no-headers")]
    [InlineData("--count")]
    [InlineData("--rows", "5")]
    [InlineData("-n", "1")]
    [InlineData("--head")]
    [InlineData("--tail")]
    [InlineData("--tail", "-n", "0")]
    [InlineData("--depth", "1")]
    [InlineData("--preview")]
    [InlineData("--max-packages", "1")]
    [InlineData("--discover")]
    [InlineData("-S")]
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

    private sealed class ShareVersionHandler(
        string packageId,
        string version) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            string? body = request.RequestUri!.Host.Equals(
                "azuresearch-usnc.nuget.org",
                StringComparison.OrdinalIgnoreCase)
                ? $$"""{"data":[{"id":"{{packageId}}","version":"{{version}}"}]}"""
                : null;
            return Task.FromResult(
                new HttpResponseMessage(
                    body is null
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.OK)
                {
                    Content = new StringContent(body ?? ""),
                    RequestMessage = request,
                });
        }
    }
}
