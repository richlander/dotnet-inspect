using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Tests;

public partial class MatchDiscoveryTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Similar_RelativeLocalSourceReplayIsIndependentOfTheNextWorkingDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-relative-source-replay-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string sourceDirectory = Path.Combine(discoveryDirectory, "feed");
        string packageName = $"Match.Relative.Source.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string staged = Path.Combine(root, "staged");
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(replayDirectory);

        try
        {
            string nupkg = CreatePackageArchive(
                root,
                "relative-source",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            ZipFile.ExtractToDirectory(nupkg, staged);
            Directory.SetCurrentDirectory(discoveryDirectory);
            string source = NuGetFetch.LocalPackageSourceIdentity.Create(
                Path.Combine(".", "feed"),
                Directory.GetCurrentDirectory()).CanonicalPath;
            NuGetCache.Initialize(
                "dotnet-inspect-match-relative-source-replay",
                cacheDirectory,
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                staged,
                nupkg,
                packageName,
                version,
                NuGetCache.GetSourceKey(source));
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();

            string[] discoveryArguments =
            [
                "match",
                SampleSeed,
                "--similar",
                "--package",
                $"{packageName}@{version}",
                "--library",
                asset,
                "--source",
                Path.Combine(".", "feed"),
                "--all",
                "--format=json",
            ];
            var (discoveryExit, discoveryOutput, discoveryError) =
                await RunCliAsync(discoveryArguments);

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            string disclosure = Parse(discoveryOutput)
                .GetProperty("disclosure")
                .GetString()!;
            Assert.Contains(
                $"--source {ShellCommandText.Quote(source)}",
                disclosure);

            string[] replayArguments =
            [
                "match",
                SampleSeed,
                $"{typeof(MatchDiscoverySample).FullName}.ExactPeer",
                "--package",
                $"{packageName}@{version}",
                "--library",
                asset,
                "--tfm",
                "net10.0",
                "--all",
                "--format=json",
            ];
            Directory.SetCurrentDirectory(replayDirectory);
            var (replayExit, replayOutput, replayError) =
                await RunCliAsync(
                    [.. replayArguments, "--source", source]);
            var (staleExit, _, staleError) =
                await RunCliAsync(
                    [.. replayArguments, "--source", Path.Combine(".", "feed")]);

            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);
            Assert.Equal(
                "Exact",
                Parse(replayOutput).GetProperty("relation").GetString());
            Assert.Equal(1, staleExit);
            Assert.Contains("not available offline", staleError);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_RelativeLocalPackageReplayIsIndependentOfTheNextWorkingDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-relative-package-replay-{Guid.NewGuid():N}");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string packageName = $"Match.Relative.Package.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(discoveryDirectory);
        Directory.CreateDirectory(replayDirectory);

        try
        {
            string package = CreatePackageArchive(
                discoveryDirectory,
                "fixture",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            Directory.SetCurrentDirectory(discoveryDirectory);
            string discoveryWorkingDirectory = Directory.GetCurrentDirectory();
            string relativePackage = Path.GetFileName(package);

            var (discoveryExit, discoveryOutput, discoveryError) =
                await RunCliAsync(
                    "match",
                    SampleSeed,
                    "--similar",
                    "--package",
                    relativePackage,
                    "--library",
                    Path.GetFileName(TestAssembly),
                    "--all",
                    "--format=json");

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            JsonElement discovery = Parse(discoveryOutput);
            string absolutePackage =
                Path.GetFullPath(relativePackage, discoveryWorkingDirectory);
            Assert.Contains(
                $"--package {ShellCommandText.Quote(absolutePackage)}",
                discovery.GetProperty("disclosure").GetString());
            string candidateToken = discovery.GetProperty("candidates")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("member").GetString()!
                    .EndsWith(".ExactPeer", StringComparison.Ordinal))
                .GetProperty("token")
                .GetString()!;

            string[] replayArguments =
            [
                "match",
                SampleSeed,
                candidateToken,
                "--package",
                absolutePackage,
                "--library",
                asset,
                "--tfm",
                "net10.0",
                "--all",
                "--format=json",
            ];
            Directory.SetCurrentDirectory(replayDirectory);
            var (replayExit, replayOutput, replayError) =
                await RunCliAsync(replayArguments);
            var (staleExit, _, staleError) =
                await RunCliAsync(
                    [
                        .. replayArguments[..3],
                        "--package",
                        relativePackage,
                        .. replayArguments[5..],
                    ]);

            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);
            Assert.Equal(
                "Exact",
                Parse(replayOutput).GetProperty("relation").GetString());
            Assert.Equal(1, staleExit);
            Assert.Contains("File not found", staleError);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Similar_AmbientNuGetConfigReplayRetainsTheDiscoveryDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-ambient-config-replay-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(root, "cache");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string packageName = $"Match.Ambient.Config.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string source = "https://ambient-config.invalid/v3/index.json";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string staged = Path.Combine(root, "staged");
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;
        Directory.CreateDirectory(discoveryDirectory);
        Directory.CreateDirectory(replayDirectory);

        try
        {
            string package = CreatePackageArchive(
                root,
                "ambient-config",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            ZipFile.ExtractToDirectory(package, staged);
            File.WriteAllText(
                Path.Combine(discoveryDirectory, "NuGet.Config"),
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="ambient" value="{source}" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="ambient">
                      <package pattern="{packageName}" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            NuGetCache.Initialize(
                "dotnet-inspect-match-ambient-config-replay",
                cacheDirectory,
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                staged,
                package,
                packageName,
                version,
                NuGetCache.GetSourceKey(source));
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();

            Directory.SetCurrentDirectory(discoveryDirectory);
            string configDirectory = Directory.GetCurrentDirectory();
            var (discoveryExit, discoveryOutput, discoveryError) =
                await RunCliAsync(
                    "match",
                    SampleSeed,
                    "--similar",
                    "--package",
                    $"{packageName}@{version}",
                    "--library",
                    asset,
                    "--all",
                    "--format=json");

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            JsonElement discovery = Parse(discoveryOutput);
            Assert.Contains(
                $"--nugetconfig-directory {ShellCommandText.Quote(configDirectory)}",
                discovery.GetProperty("disclosure").GetString());
            string candidateToken = discovery.GetProperty("candidates")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("member").GetString()!
                    .EndsWith(".ExactPeer", StringComparison.Ordinal))
                .GetProperty("token")
                .GetString()!;

            string[] replayArguments =
            [
                "match",
                SampleSeed,
                candidateToken,
                "--package",
                $"{packageName}@{version}",
                "--library",
                asset,
                "--tfm",
                "net10.0",
                "--all",
                "--format=json",
            ];
            Directory.SetCurrentDirectory(replayDirectory);
            var (replayExit, replayOutput, replayError) =
                await RunCliAsync(
                    [
                        .. replayArguments,
                        "--nugetconfig-directory",
                        configDirectory,
                    ]);
            var (staleExit, _, staleError) =
                await RunCliAsync(replayArguments);

            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);
            Assert.Equal(
                "Exact",
                Parse(replayOutput).GetProperty("relation").GetString());
            Assert.Equal(1, staleExit);
            Assert.Contains("not available offline", staleError);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
