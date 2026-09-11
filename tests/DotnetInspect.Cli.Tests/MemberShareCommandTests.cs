using System.IO.Compression;
using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task MemberShare_PacketProjectsExactPackageMember()
    {
        var result = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
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
        Assert.Equal(0, packet.ActiveTabIndex);
        Assert.Equal(0, packet.SelectedContextIndex);
        Assert.Equal("api", packet.Lens);
        Assert.Equal("Newtonsoft.Json.JsonConvert", packet.Type);
        Assert.Matches("^[0-9a-f]{10}$", packet.MemberAnchor);
        Assert.Null(packet.MemberSignature);
        Assert.Null(packet.Section);
        Assert.Equal("Newtonsoft.Json", Assert.Single(packet.Libraries));
    }

    [Fact]
    public async Task MemberShare_VerbosityDoesNotChangeCanonicalPacket()
    {
        string[] common =
        [
            "member",
            "Utf8JsonWriter",
            "--package",
            "System.Text.Json@9.0.4",
            "WriteStringValue:7",
            "--tfm",
            "net9.0",
            "--share",
            "packet",
            "--tips",
            "q",
        ];

        var normal = await RunAppAsync(common);
        var detailed = await RunAppAsync(
            ["-v:d", .. common]);

        Assert.Equal(0, normal.Exit);
        Assert.Equal(0, detailed.Exit);
        Assert.Empty(normal.Error);
        Assert.Empty(detailed.Error);
        Assert.Equal(normal.Output, detailed.Output);

        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            normal.Output.Trim(),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "System.Text.Json.Utf8JsonWriter",
            packet.Type);
        Assert.Equal("7a7f0afab9", packet.MemberAnchor);
        Assert.Null(packet.Section);
        Assert.Equal(
            "System.Text.Json",
            Assert.Single(packet.Libraries));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("url")]
    public async Task MemberShare_UrlWrapsCanonicalPacket(string? format)
    {
        var arguments = new List<string>
        {
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
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
        Assert.Equal("Newtonsoft.Json.JsonConvert", packet.Type);
        Assert.NotNull(packet.MemberAnchor);
    }

    [Fact]
    public async Task MemberShare_NeighboringOverloadsHaveDistinctAnchors()
    {
        var first = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
            "--share",
            "packet",
            "--tips",
            "q");
        var second = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:2",
            "--tfm",
            "net6.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(0, first.Exit);
        Assert.Equal(0, second.Exit);
        WorkspaceSharePacket firstPacket = WorkspaceSharePacketCodec.Decode(
            first.Output.Trim(),
            TestContext.Current.CancellationToken);
        WorkspaceSharePacket secondPacket = WorkspaceSharePacketCodec.Decode(
            second.Output.Trim(),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(firstPacket.MemberAnchor, secondPacket.MemberAnchor);
    }

    [Fact]
    public async Task MemberShare_UsesBrowserCompileAssetsInsteadOfRuntimeCopies()
    {
        var result = await RunAppAsync(
            "member",
            "CodePagesEncodingProvider",
            "--package",
            "System.Text.Encoding.CodePages@10.0.0",
            "GetEncoding:1",
            "--tfm",
            "net10.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            result.Output.Trim(),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            "System.Text.CodePagesEncodingProvider",
            packet.Type);
        Assert.Equal(
            "System.Text.Encoding.CodePages",
            Assert.Single(packet.Libraries));
    }

    [Fact]
    public async Task MemberShare_PreservesPackageAcrossForwardedImplementation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"member-share-forwarder-{Guid.NewGuid():N}");
        string extracted = Path.Combine(root, "extracted");
        string cache = Path.Combine(root, "cache");
        string archive = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "fixtures",
            "cli",
            "package-archives",
            "avalonia.12.1.2.nupkg");
        bool wasOffline =
            DotnetInspector.Networking.HttpClientFactory.IsOffline;
        try
        {
            ZipFile.ExtractToDirectory(archive, extracted);
            NuGetCache.Initialize(
                "dotnet-inspect",
                basePath: cache,
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                extracted,
                archive,
                "Avalonia",
                "12.1.2",
                NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url));
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = true,
                });
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();

            var result = await RunAppAsync(
                "member",
                "MultiBinding",
                "--package",
                "Avalonia@12.1.2",
                "--library",
                "Avalonia.Markup",
                ".ctor:1",
                "--tfm",
                "net8.0",
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
            Assert.Equal("Avalonia", tab.Source);
            Assert.Equal("12.1.2", tab.Version);
            Assert.Equal("net8.0", tab.Framework);
            Assert.Equal("Avalonia.Data.MultiBinding", packet.Type);
            Assert.Equal(
                "Avalonia.Base",
                Assert.Single(packet.Libraries));
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = wasOffline,
                });
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MemberShare_RejectsToolsOnlyPackageSurface()
    {
        var result = await RunAppAsync(
            "member",
            "Mono.Cecil.AssemblyDefinition",
            "--package",
            "dotnet-ildasm@0.12.2",
            "ReadAssembly:1",
            "--tfm",
            "netcoreapp3.0",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Browser compile-asset set",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RequiresExactMemberBeforeAcquisition()
    {
        var result = await RunAppAsync(
            "member",
            "Missing.Type",
            "--package",
            "Missing.Package",
            "Missing",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--share requires one exact member overload",
            result.Error);
        Assert.DoesNotContain(
            "Package 'Missing.Package'",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RejectsPlatformSource()
    {
        var result = await RunAppAsync(
            "member",
            "String",
            "--platform",
            "System.Private.CoreLib",
            "Equals:1",
            "--share",
            "packet",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "currently supports packages acquired from NuGet.org",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RejectsLocalPackage()
    {
        var (packagePath, tempDir) =
            CreateLocalRefPackage("System.Private.CoreLib");
        try
        {
            var result = await RunAppAsync(
                "member",
                "String",
                "--package",
                packagePath,
                "Equals:1",
                "--share",
                "packet",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "currently supports packages acquired from NuGet.org",
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--raw")]
    [InlineData("--blob")]
    [InlineData("--count")]
    [InlineData("-S", "Signature")]
    [InlineData("--all")]
    public async Task MemberShare_RejectsConflictingModes(
        params string[] conflicting)
    {
        var result = await RunAppAsync(
            [
                "member",
                "Missing.Type",
                "--package",
                "Missing.Package",
                "Missing:1",
                "--share",
                "packet",
                "--tips",
                "q",
                .. conflicting,
            ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("--share", result.Error);
        Assert.DoesNotContain(
            "Package 'Missing.Package'",
            result.Error);
    }

    [Fact]
    public async Task MemberShare_RejectsLegacyLineWindowBeforeScalarOutput()
    {
        var result = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "SerializeObject:1",
            "--tfm",
            "net6.0",
            "--share",
            "--tail",
            "-n",
            "0",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot be combined with other output formatting or projection options",
            result.Error);
    }
}
