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
    // ---- Round 9 review findings: the disclosed address must outlive the command ----

    /// <summary>
    /// A package is extracted to a temporary directory that <c>match</c> deletes as it exits, so
    /// disclosing that extraction path handed the caller an address that no longer existed by the
    /// time they could type it: discovery completed at exit 0 and the command it printed failed
    /// with "File not found". A package-sourced run must disclose the package and the library
    /// inside it, which is what actually replays.
    /// </summary>
    [Fact]
    public void Disclosure_ForAPackageSourcedRun_NamesThePackageRatherThanTheExtractionPath()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            CandidatePackage: "Fixture@1.0.0",
            CandidateTfm: "net10.0");

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "`--package 'Fixture@1.0.0' --library 'lib/net10.0/Target.dll' --tfm 'net10.0'`",
            disclosure);
    }

    [Fact]
    public async Task Similar_PackageForwardedPopulation_DisclosesTheExactReplayAddress()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-replay-{Guid.NewGuid():N}");
        string package = Path.Combine(fixtureDirectory, "Forwarding.Fixture.1.0.0.nupkg");
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                ZipArchiveEntry facade = archive.CreateEntry("lib/net10.0/Facade.dll");
                await using (Stream stream = facade.Open())
                {
                    await stream.WriteAsync(BuildForwarderFacade(
                        "Facade",
                        TestAssembly,
                        typeof(MatchDiscoverySample)),
                        TestContext.Current.CancellationToken);
                }
                archive.CreateEntryFromFile(
                    TestAssembly,
                    $"lib/net10.0/{Path.GetFileName(TestAssembly)}");
            }

            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                RightSelector = typeof(MatchDiscoverySample).FullName,
                PackagePath = package,
                AssemblyPath = "lib/net10.0/Facade.dll",
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            JsonElement document = Parse(output);
            Assert.Equal(
                $"lib/net10.0/{Path.GetFileName(TestAssembly)}",
                document.GetProperty("candidate_assembly").GetString());
            Assert.Contains(
                $"--package '{package}' "
                    + $"--library 'lib/net10.0/{Path.GetFileName(TestAssembly)}' "
                    + "--tfm 'net10.0'",
                document.GetProperty("disclosure").GetString());
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_PackageForwarderUsesOnlyAnAuthorizedDependencyPayload()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-forwarded-dependency-{Guid.NewGuid():N}");
        string appCache = Path.Combine(fixtureDirectory, "app-cache");
        string globalRoot = Path.Combine(fixtureDirectory, "global");
        string rootPackage = Path.Combine(
            fixtureDirectory,
            "Forwarding.Root.1.0.0.nupkg");
        string dependencyId = $"Forwarding.Target.{Guid.NewGuid():N}";
        const string dependencyVersion = "1.0.0";
        const string authorizedSource =
            "https://authorized.invalid/v3/index.json";
        const string unauthorizedSource =
            "https://unauthorized.invalid/v3/index.json";
        const string targetNamespace = "Forwarding.Target";
        const string targetTypeName = "Sample";
        string targetSelector = $"{targetNamespace}.{targetTypeName}.Seed";
        string targetType = $"{targetNamespace}.{targetTypeName}";
        string dependencyAsset =
            $"lib/net10.0/{dependencyId}.dll";
        byte[] targetImage = BuildMatchTargetAssembly(
            dependencyId,
            targetNamespace,
            targetTypeName);
        string? previousNuGetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       rootPackage,
                       ZipArchiveMode.Create))
            {
                ZipArchiveEntry facade =
                    archive.CreateEntry("lib/net10.0/Facade.dll");
                await using (Stream stream = facade.Open())
                {
                    await stream.WriteAsync(
                        BuildForwarderFacade(
                            "Facade",
                            new AssemblyReferenceIdentity(
                                dependencyId,
                                new Version(1, 0, 0, 0),
                                Culture: null,
                                PublicKeyToken: null),
                            targetNamespace,
                            targetTypeName),
                        TestContext.Current.CancellationToken);
                }

                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Forwarding.Root.nuspec");
                await using Stream nuspecStream = nuspec.Open();
                await using var writer = new StreamWriter(nuspecStream);
                await writer.WriteAsync(
                    $"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>Forwarding.Root</id>
                        <version>1.0.0</version>
                        <authors>dotnet-inspect tests</authors>
                        <description>forwarded dependency fixture</description>
                        <dependencies>
                          <group targetFramework="net10.0">
                            <dependency id="{dependencyId}" version="{dependencyVersion}" />
                          </group>
                        </dependencies>
                      </metadata>
                    </package>
                    """);
            }

            string dependencyDirectory = Path.Combine(
                globalRoot,
                dependencyId.ToLowerInvariant(),
                dependencyVersion);
            string dependencyAssetPath = Path.Combine(
                dependencyDirectory,
                dependencyAsset.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(
                Path.GetDirectoryName(dependencyAssetPath)!);
            File.WriteAllBytes(dependencyAssetPath, targetImage);
            File.WriteAllText(
                Path.Combine(
                    dependencyDirectory,
                    $"{dependencyId}.nuspec"),
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{dependencyId}</id>
                    <version>{dependencyVersion}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>forwarded target fixture</description>
                  </metadata>
                </package>
                """);
            string metadataPath = Path.Combine(
                dependencyDirectory,
                ".nupkg.metadata");
            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{unauthorizedSource}}"}""");

            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                globalRoot);
            NuGetCache.Initialize(
                "dotnet-inspect-match-forwarded-dependency",
                appCache,
                skipNuGetCache: false);
            var options = new MatchOptions
            {
                LeftSelector = targetSelector,
                RightSelector = targetType,
                PackagePath = rootPackage,
                AssemblyPath = "lib/net10.0/Facade.dll",
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [authorizedSource],
                },
            };

            var (unauthorizedExit, unauthorizedOutput, unauthorizedError) =
                await RunAsync(options);

            Assert.Null(
                PackageExtractor.TryGetAdmittedCachedPackagePath(
                    dependencyId,
                    dependencyVersion,
                    options.SourceOptions,
                    [globalRoot]));
            Assert.True(
                unauthorizedExit == 1,
                $"Expected unavailable forwarding.\nOutput:\n{unauthorizedOutput}\nError:\n{unauthorizedError}");
            Assert.Empty(unauthorizedOutput);
            Assert.Contains(
                $"Forwarded type '{targetType}' "
                    + "could not be resolved: UnboundBinding.",
                unauthorizedError);

            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{authorizedSource}}"}""");
            var (authorizedExit, authorizedOutput, authorizedError) =
                await RunAsync(options);

            Assert.Equal(0, authorizedExit);
            Assert.Empty(authorizedError);
            JsonElement document = Parse(authorizedOutput);
            Assert.Equal(
                dependencyAsset,
                document.GetProperty("candidate_assembly").GetString());
            string disclosure =
                document.GetProperty("disclosure").GetString()!;
            string expectedDisclosure =
                $"--package '{dependencyId.ToLowerInvariant()}@{dependencyVersion}' "
                    + $"--library '{dependencyAsset}' --tfm 'net10.0' "
                    + $"--source '{authorizedSource}'";
            Assert.True(
                disclosure.Contains(
                    expectedDisclosure,
                    StringComparison.Ordinal),
                $"Expected disclosure fragment:\n{expectedDisclosure}\nActual:\n{disclosure}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                previousNuGetPackages);
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_DirectCacheAndLocalPackageForwardersRetainAmbientSourcePolicy()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-ambient-forwarding-replay-{Guid.NewGuid():N}");
        string appCache = Path.Combine(root, "app-cache");
        string globalRoot = Path.Combine(root, "global");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string rootPackageDirectory = Path.Combine(
            globalRoot,
            "forwarding.root",
            "1.0.0");
        string directFacade = Path.Combine(
            rootPackageDirectory,
            "lib",
            "net10.0",
            "Facade.dll");
        string localPackage = Path.Combine(
            discoveryDirectory,
            "Forwarding.Root.1.0.0.nupkg");
        string dependencyId = $"Forwarding.Ambient.{Guid.NewGuid():N}";
        const string dependencyVersion = "1.0.0";
        const string authorizedSource =
            "https://ambient-authorized.invalid/v3/index.json";
        const string unauthorizedSource =
            "https://ambient-unauthorized.invalid/v3/index.json";
        const string targetNamespace = "Forwarding.Ambient";
        const string targetTypeName = "Sample";
        string targetSelector = $"{targetNamespace}.{targetTypeName}.Seed";
        string targetType = $"{targetNamespace}.{targetTypeName}";
        string dependencyAsset = $"lib/net10.0/{dependencyId}.dll";
        byte[] facadeImage = BuildForwarderFacade(
            "Facade",
            new AssemblyReferenceIdentity(
                dependencyId,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            targetNamespace,
            targetTypeName);
        string rootNuspec =
            $"""
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Forwarding.Root</id>
                <version>1.0.0</version>
                <authors>dotnet-inspect tests</authors>
                <description>ambient forwarding fixture</description>
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="{dependencyId}" version="{dependencyVersion}" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;
        string dependencyDirectory = Path.Combine(
            globalRoot,
            dependencyId.ToLowerInvariant(),
            dependencyVersion);
        string dependencyAssetPath = Path.Combine(
            dependencyDirectory,
            dependencyAsset.Replace('/', Path.DirectorySeparatorChar));
        string metadataPath = Path.Combine(
            dependencyDirectory,
            ".nupkg.metadata");
        string? previousNuGetPackages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;
        Directory.CreateDirectory(Path.GetDirectoryName(directFacade)!);
        Directory.CreateDirectory(discoveryDirectory);
        Directory.CreateDirectory(replayDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(dependencyAssetPath)!);

        try
        {
            File.WriteAllBytes(directFacade, facadeImage);
            File.WriteAllText(
                Path.Combine(rootPackageDirectory, "Forwarding.Root.nuspec"),
                rootNuspec);
            using (ZipArchive archive = ZipFile.Open(
                       localPackage,
                       ZipArchiveMode.Create))
            {
                ZipArchiveEntry facade =
                    archive.CreateEntry("lib/net10.0/Facade.dll");
                await using (Stream stream = facade.Open())
                {
                    await stream.WriteAsync(
                        facadeImage,
                        TestContext.Current.CancellationToken);
                }

                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Forwarding.Root.nuspec");
                await using Stream stream2 = nuspec.Open();
                await using var writer = new StreamWriter(stream2);
                await writer.WriteAsync(rootNuspec);
            }

            File.WriteAllBytes(
                dependencyAssetPath,
                BuildMatchTargetAssembly(
                    dependencyId,
                    targetNamespace,
                    targetTypeName));
            File.WriteAllText(
                Path.Combine(
                    dependencyDirectory,
                    $"{dependencyId}.nuspec"),
                $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{dependencyId}</id>
                    <version>{dependencyVersion}</version>
                    <authors>dotnet-inspect tests</authors>
                    <description>ambient forwarding target</description>
                  </metadata>
                </package>
                """);
            File.WriteAllText(
                Path.Combine(discoveryDirectory, "NuGet.Config"),
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="ambient" value="{authorizedSource}" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="ambient">
                      <package pattern="{dependencyId}" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{unauthorizedSource}}"}""");

            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                globalRoot);
            NuGetCache.Initialize(
                "dotnet-inspect-match-ambient-forwarding-replay",
                appCache,
                skipNuGetCache: false);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            Directory.SetCurrentDirectory(discoveryDirectory);
            string configDirectory = Directory.GetCurrentDirectory();
            string[] directArguments =
            [
                "match",
                targetSelector,
                targetType,
                "--similar",
                "--library",
                directFacade,
                "--all",
                "--format=json",
            ];

            var (unauthorizedExit, unauthorizedOutput, unauthorizedError) =
                await RunCliAsync(directArguments);

            Assert.True(
                unauthorizedExit == 1,
                $"Expected unavailable forwarding.\nOutput:\n{unauthorizedOutput}\nError:\n{unauthorizedError}");
            Assert.Empty(unauthorizedOutput);
            Assert.Contains("UnboundBinding", unauthorizedError);

            File.WriteAllText(
                metadataPath,
                $$"""{"source":"{{authorizedSource}}"}""");
            string relativeGlobalRoot = Path.GetRelativePath(
                configDirectory,
                globalRoot);
            Assert.False(Path.IsPathRooted(relativeGlobalRoot));
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                relativeGlobalRoot);
            var (
                relativeRootExit,
                relativeRootOutput,
                relativeRootError) = await RunCliAsync(
                    "match",
                    targetSelector,
                    targetType,
                    "--similar",
                    "--package",
                    localPackage,
                    "--library",
                    "lib/net10.0/Facade.dll",
                    "--all",
                    "--format=json");

            Assert.Equal(1, relativeRootExit);
            Assert.Empty(relativeRootOutput);
            Assert.Contains(
                "the selected image came from a relative NUGET_PACKAGES root",
                relativeRootError);

            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                globalRoot);
            var (directExit, directOutput, directError) =
                await RunCliAsync(directArguments);
            var (localExit, localOutput, localError) =
                await RunCliAsync(
                    "match",
                    targetSelector,
                    targetType,
                    "--similar",
                    "--package",
                    localPackage,
                    "--library",
                    "lib/net10.0/Facade.dll",
                    "--all",
                    "--format=json");

            Assert.Equal(0, directExit);
            Assert.Empty(directError);
            Assert.Equal(0, localExit);
            Assert.Empty(localError);
            foreach (string output in new[] { directOutput, localOutput })
            {
                string disclosure = Parse(output)
                    .GetProperty("disclosure")
                    .GetString()!;
                Assert.Contains(
                    $"--package '{dependencyId.ToLowerInvariant()}@{dependencyVersion}'",
                    disclosure);
                Assert.Contains(
                    $"--nugetconfig-directory {ShellCommandText.Quote(configDirectory)}",
                    disclosure);
            }

            string candidateToken = Parse(localOutput)
                .GetProperty("candidates")
                .EnumerateArray()
                .First()
                .GetProperty("token")
                .GetString()!;
            string[] replayArguments =
            [
                "match",
                targetSelector,
                candidateToken,
                "--package",
                $"{dependencyId}@{dependencyVersion}",
                "--library",
                dependencyAsset,
                "--tfm",
                "net10.0",
                "--all",
                "--format=json",
            ];
            Directory.SetCurrentDirectory(replayDirectory);
            var (replayExit, _, replayError) =
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
            Assert.Equal(1, staleExit);
            Assert.Contains("not available offline", staleError);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            Environment.SetEnvironmentVariable(
                "NUGET_PACKAGES",
                previousNuGetPackages);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplayAddress_AppCachePathRequiresTypedPackageProvenance()
    {
        string cacheRoot = Path.Combine(
            Path.GetTempPath(),
            $"match-app-cache-address-{Guid.NewGuid():N}");
        string packageId = $"app.cache.dependency.{Guid.NewGuid():N}";
        NuGetCache.Initialize(
            "dotnet-inspect-match-app-cache-address",
            cacheRoot,
            skipNuGetCache: true);
        try
        {
            string candidate = Path.Combine(
                NuGetCache.GetPackageContentCachePath(),
                packageId,
                "1.0.0",
                "producer",
                "lib",
                "net10.0",
                "Target.dll");

            var directAddress =
                MatchDiscovery.GetReplayableCandidateAddress(
                    packagePath: null,
                    packageExtractPath: null,
                    candidate);
            var packageAddress =
                MatchDiscovery.GetReplayableCandidateAddress(
                    packagePath: null,
                    packageExtractPath: null,
                    candidate,
                    AssemblyResolutionProvenance.Package(
                        packageId,
                        "1.0.0",
                        tfm: null,
                        rid: null));

            Assert.Null(directAddress.Package);
            Assert.Equal(candidate, directAddress.Library);
            Assert.Equal($"{packageId}@1.0.0", packageAddress.Package);
            Assert.Equal("lib/net10.0/Target.dll", packageAddress.Library);
            Assert.Equal("net10.0", packageAddress.Tfm);
        }
        finally
        {
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_PackageSameImage_DisclosesTheExactReplayAddress()
    {
        string fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-same-image-replay-{Guid.NewGuid():N}");
        string package = Path.Combine(fixtureDirectory, "Same.Image.Fixture.1.0.0.nupkg");
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        Directory.CreateDirectory(fixtureDirectory);

        try
        {
            using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(TestAssembly, asset);

            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = package,
                AssemblyPath = Path.GetFileName(TestAssembly),
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            JsonElement document = Parse(output);
            Assert.False(document.TryGetProperty("candidate_assembly", out _));
            Assert.NotEmpty(document.GetProperty("candidates").EnumerateArray());
            Assert.Contains(
                $"--package '{package}' --library '{asset}' --tfm 'net10.0'",
                document.GetProperty("disclosure").GetString());
            Assert.Contains(
                "against that same image",
                document.GetProperty("disclosure").GetString());
        }
        finally
        {
            Directory.Delete(fixtureDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_ExactPackageReplayRetainsExplicitSourceAuthorityOffline()
    {
        string cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            $"match-source-replay-{Guid.NewGuid():N}");
        string packageName = $"Match.Source.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string source = "https://private.invalid/v3/index.json";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string nupkg = Path.Combine(cacheDirectory, $"{packageName}.{version}.nupkg");
        string staged = Path.Combine(cacheDirectory, "staged");
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;

        Directory.CreateDirectory(cacheDirectory);
        try
        {
            using (ZipArchive archive = ZipFile.Open(nupkg, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(TestAssembly, asset);
                ZipArchiveEntry nuspec = archive.CreateEntry($"{packageName}.nuspec");
                await using Stream stream = nuspec.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(
                    $"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{packageName}</id>
                        <version>{version}</version>
                        <authors>dotnet-inspect tests</authors>
                        <description>match replay fixture</description>
                      </metadata>
                    </package>
                    """);
            }

            ZipFile.ExtractToDirectory(nupkg, staged);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = true });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-source-replay",
                cacheDirectory,
                skipNuGetCache: true);
            NuGetCache.CommitPackage(
                staged,
                nupkg,
                packageName,
                version,
                NuGetCache.GetSourceKey(source));

            var sourceOptions = new NuGetSourceOptions { Sources = [source] };
            var discovery = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = $"{packageName}@{version}",
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = sourceOptions,
            };

            var (discoveryExit, output, discoveryError) = await RunAsync(discovery);

            Assert.Equal(0, discoveryExit);
            Assert.Empty(discoveryError);
            Assert.Contains(
                $"--package '{packageName}@{version}' --library '{asset}' --tfm 'net10.0' "
                    + $"--source '{source}'",
                Parse(output).GetProperty("disclosure").GetString());

            MatchOptions replay = discovery with
            {
                LeftSelector = SampleSeed,
                RightSelector = $"{typeof(MatchDiscoverySample).FullName}.ExactPeer",
                Similar = false,
                JsonOutput = false,
            };
            string[] replayArguments =
            [
                "match",
                replay.LeftSelector!,
                replay.RightSelector!,
                "--package",
                replay.PackagePath!,
                "--library",
                replay.AssemblyPath!,
                "--tfm",
                "net10.0",
                "--all",
            ];
            var (withoutSourceExit, _, withoutSourceError) =
                await RunCliAsync(replayArguments);
            var (withSourceExit, _, withSourceError) =
                await RunCliAsync([.. replayArguments, "--source", source]);

            Assert.Equal(1, withoutSourceExit);
            Assert.Contains("not available offline", withoutSourceError);
            Assert.Equal(0, withSourceExit);
            Assert.Empty(withSourceError);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("", null, false, false)]
    [InlineData("@1.*", null, false, false)]
    [InlineData("@1.0.0..1.0.0", "last", false, false)]
    [InlineData("@1.0.0..1.0.0", "last", true, false)]
    [InlineData("@1.0.0..1.0.0", "last", true, true)]
    public async Task Similar_SelectedVersionProducer_ReplayReopensTheSamePayload(
        string packageVersionSelector,
        string? rangeAddress,
        bool useConfig,
        bool useMapping)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-range-source-replay-{Guid.NewGuid():N}");
        string cacheRoot = Path.Combine(root, "cache");
        string packageName = $"Match.Range.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        Directory.CreateDirectory(root);
        using var feed = new RangeReplayFeed(packageName, version);
        string sourceA = feed.SourceA;
        string sourceB = feed.SourceB;
        string configPath = Path.Combine(root, "nuget.config");
        if (useConfig)
        {
            File.WriteAllText(
                configPath,
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="feed-a" value="{sourceA}" />
                    <add key="feed-b" value="{sourceB}" />
                  </packageSources>
                  {(useMapping
                    ? $"""
                      <packageSourceMapping>
                        <packageSource key="feed-b">
                          <package pattern="{packageName}" />
                        </packageSource>
                      </packageSourceMapping>
                      """
                    : "")}
                </configuration>
                """);
        }
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;

        try
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = false });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-range-source-replay",
                cacheRoot,
                skipNuGetCache: true);
            string packageA = CreatePackageArchive(
                root,
                "package-a",
                packageName,
                version,
                asset,
                [1, 2, 3, 4]);
            string packageB = CreatePackageArchive(
                root,
                "package-b",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            feed.AddPackage(sourceA, packageName, version, packageA);
            feed.AddPackage(sourceB, packageName, version, packageB);
            if (rangeAddress is null)
            {
                CommitCachedPackage(
                    root, "staged-a", packageA, packageName, version, sourceA);
                CommitCachedPackage(
                    root, "staged-b", packageB, packageName, version, sourceB);
            }

            var sourceOptions = new NuGetSourceOptions
            {
                Sources = useConfig ? [] : [sourceA, sourceB],
                ConfigFile = useConfig ? configPath : null,
            };
            var discovery = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = $"{packageName}{packageVersionSelector}",
                PackageRangeAddress = rangeAddress,
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = sourceOptions,
            };

            var (discoveryExit, output, discoveryError) =
                await RunAsync(discovery);

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            string disclosure =
                Parse(output).GetProperty("disclosure").GetString()!;
            if (useMapping)
            {
                Assert.DoesNotContain(
                    $"--source '{sourceB}'",
                    disclosure);
            }
            else
            {
                Assert.Contains(
                    $"--source '{sourceB}'",
                    disclosure);
            }
            Assert.DoesNotContain(
                $"--source '{sourceA}'",
                disclosure);
            if (useConfig)
            {
                Assert.Contains(
                    $"--nugetconfig '{Path.GetFullPath(configPath)}'",
                    disclosure);
            }

            string[] exactArguments =
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
            ];
            var (widenedExit, _, widenedError) =
                await RunCliAsync(
                    [
                        .. exactArguments,
                        "--source",
                        sourceA,
                        "--source",
                        sourceB,
                    ]);
            string[] replaySourceArguments = useMapping
                ? ["--nugetconfig", configPath]
                : ["--source", sourceB, .. ConfigArguments()];
            var (replayExit, _, replayError) =
                await RunCliAsync(
                    [.. exactArguments, .. replaySourceArguments]);

            Assert.Equal(1, widenedExit);
            Assert.Contains(
                "The assembly metadata root is malformed "
                    + "(UnmappableMetadataDirectory).",
                widenedError);
            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);
            Assert.Equal(1, feed.PayloadRequests(sourceA, packageName, version));
            // Selected discovery acquires once; its emitted exact replay acquires once more.
            Assert.Equal(2, feed.PayloadRequests(sourceB, packageName, version));

            string[] ConfigArguments() =>
                useConfig ? ["--nugetconfig", configPath] : [];
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Similar_SelectedVersionReplayRetainsAmbientConfigDirectory(bool retainedRange)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-selected-version-ambient-config-{Guid.NewGuid():N}");
        string cacheRoot = Path.Combine(root, "cache");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string packageName = $"Match.Selected.Ambient.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;
        Directory.CreateDirectory(discoveryDirectory);
        Directory.CreateDirectory(replayDirectory);
        using var feed = new RangeReplayFeed(packageName, version);

        try
        {
            File.WriteAllText(
                Path.Combine(discoveryDirectory, "NuGet.Config"),
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="feed-a" value="{feed.SourceA}" />
                    <add key="feed-b" value="{feed.SourceB}" />
                  </packageSources>
                </configuration>
                """);
            File.WriteAllText(
                Path.Combine(replayDirectory, "NuGet.Config"),
                $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="blocked" value="https://blocked.invalid/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="blocked">
                      <package pattern="{packageName}" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = false });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-selected-version-ambient-config",
                cacheRoot,
                skipNuGetCache: true);
            string packageA = CreatePackageArchive(
                root,
                "package-a",
                packageName,
                version,
                asset,
                [1, 2, 3, 4]);
            string packageB = CreatePackageArchive(
                root,
                "package-b",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            feed.AddPackage(feed.SourceA, packageName, version, packageA);
            feed.AddPackage(feed.SourceB, packageName, version, packageB);
            if (!retainedRange)
            {
                CommitCachedPackage(
                    root, "staged-a", packageA, packageName, version, feed.SourceA);
                CommitCachedPackage(
                    root, "staged-b", packageB, packageName, version, feed.SourceB);
            }

            Directory.SetCurrentDirectory(discoveryDirectory);
            string configDirectory = Directory.GetCurrentDirectory();
            string[] packageArguments = retainedRange
                ? [$"{packageName}@{version}..{version}", "--at", "last"]
                : [packageName];
            var (discoveryExit, discoveryOutput, discoveryError) =
                await RunCliAsync(
                [
                    "match",
                    SampleSeed,
                    "--similar",
                    "--package",
                    .. packageArguments,
                    "--library",
                    asset,
                    "--all",
                    "--format=json",
                ]);

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            JsonElement discovery = Parse(discoveryOutput);
            string disclosure = discovery
                .GetProperty("disclosure")
                .GetString()!;
            Assert.Contains(
                $"--source {ShellCommandText.Quote(feed.SourceB)}",
                disclosure);
            Assert.Contains(
                $"--nugetconfig-directory {ShellCommandText.Quote(configDirectory)}",
                disclosure);
            string candidateToken = discovery.GetProperty("candidates")
                .EnumerateArray()
                .Single(candidate => candidate.GetProperty("member").GetString()!
                    .EndsWith(".ExactPeer", StringComparison.Ordinal))
                .GetProperty("token")
                .GetString()!;

            if (!retainedRange)
            {
                DotnetInspector.Networking.HttpClientFactory.Initialize(
                    new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = true });
                DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            }
            Directory.SetCurrentDirectory(replayDirectory);
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
                "--source",
                feed.SourceB,
            ];
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
            Assert.Contains("maps to source", staleError);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Similar_LocalFolderRangeReplayReopensThePackageFromAnotherDirectory(bool fileUri)
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"match-local-range-replay-{Guid.NewGuid():N}");
        string discoveryDirectory = Path.Combine(root, "discovery");
        string replayDirectory = Path.Combine(root, "replay");
        string feedDirectory = Path.Combine(discoveryDirectory, "feed");
        string otherFeedDirectory = Path.Combine(discoveryDirectory, "a-nonreporter");
        string packageName = $"Match.Local.Range.{Guid.NewGuid():N}";
        const string asset = "lib/net10.0/Replay.Target.dll";
        const string seed = "Replay.Target.Seed";
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;
        Directory.CreateDirectory(feedDirectory);
        Directory.CreateDirectory(otherFeedDirectory);
        Directory.CreateDirectory(replayDirectory);

        try
        {
            byte[] assembly = BuildMatchTargetAssembly("Replay.Target", "Replay", "Target");
            foreach (string version in new[] { "1.0.0", "2.0.0" })
            {
                CreatePackageArchive(
                    feedDirectory, $"{packageName}.{version}", packageName, version, asset, assembly);
            }
            byte[] otherAssembly = BuildMatchTargetAssembly("Replay.Target", "Replay", "Other");
            CreatePackageArchive(
                otherFeedDirectory, $"{packageName}.1.0.0", packageName, "1.0.0", asset, otherAssembly);
            string otherSelectedPackage = CreatePackageArchive(
                otherFeedDirectory, $"{packageName}.2.0.0", packageName, "2.0.0", asset, otherAssembly);

            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = false });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-local-range-replay",
                Path.Combine(root, "discovery-cache"),
                skipNuGetCache: true);
            var (warmExit, _, warmError) = await RunCliAsync(
                "match", "Replay.Other.Seed", "--similar",
                "--package", $"{packageName}@2.0.0", "--library", asset,
                "--source", otherFeedDirectory, "--all", "--format=json");
            Assert.True(warmExit == 0, warmError);
            // The authority cache stays warm, but this folder no longer reports the selected version.
            File.Delete(otherSelectedPackage);

            Directory.SetCurrentDirectory(discoveryDirectory);
            string effectiveDiscoveryDirectory =
                Directory.GetCurrentDirectory();
            string source = fileUri
                ? new Uri(feedDirectory + Path.DirectorySeparatorChar).AbsoluteUri
                : Path.Combine(".", "feed");
            string expectedSource = fileUri
                ? feedDirectory
                : Path.Combine(effectiveDiscoveryDirectory, "feed");
            var (discoveryExit, discoveryOutput, discoveryError) = await RunCliAsync(
                "match", seed, "--similar",
                "--package", $"{packageName}@1.0.0..2.0.0", "--at", "last",
                "--library", asset, "--source", otherFeedDirectory, "--source", source, "--all", "--format=json");

            Assert.True(discoveryExit == 0, discoveryError);
            Assert.Empty(discoveryError);
            JsonElement discovery = Parse(discoveryOutput);
            string disclosure = discovery.GetProperty("disclosure").GetString()!;
            Assert.Contains(
                $"--package {ShellCommandText.Quote($"{packageName}@2.0.0")}", disclosure,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"--source {ShellCommandText.Quote(expectedSource)}", disclosure);
            Assert.DoesNotContain($"--source {ShellCommandText.Quote(otherFeedDirectory)}", disclosure);
            Assert.Contains(
                $"--nugetconfig-directory {ShellCommandText.Quote(effectiveDiscoveryDirectory)}",
                disclosure);
            string token = discovery.GetProperty("candidates").EnumerateArray()
                .Single(candidate => candidate.GetProperty("member").GetString() == "Replay.Target.ExactPeer")
                .GetProperty("token").GetString()!;

            var (widenedExit, _, widenedError) = await RunCliAsync(
                "match", seed, token, "--package", $"{packageName}@2.0.0",
                "--library", asset, "--tfm", "net10.0",
                "--source", otherFeedDirectory, "--source", feedDirectory, "--all", "--format=json");
            Assert.Equal(1, widenedExit);
            Assert.Contains("must name a Type.Member", widenedError);

            Directory.SetCurrentDirectory(replayDirectory);
            NuGetCache.Initialize(
                "dotnet-inspect-match-local-range-replay",
                Path.Combine(root, "replay-cache"),
                skipNuGetCache: true);
            var (replayExit, replayOutput, replayError) = await RunCliAsync(
                "match", seed, token, "--package", $"{packageName}@2.0.0",
                "--library", asset, "--tfm", "net10.0", "--source", feedDirectory,
                "--nugetconfig-directory", discoveryDirectory, "--all", "--format=json");

            Assert.True(replayExit == 0, replayError);
            Assert.Empty(replayError);
            Assert.Equal("Exact", Parse(replayOutput).GetProperty("relation").GetString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Similar_QueryDistinctReporterReplayRequiresExactConfigPolicy(bool useMapping)
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"match-query-replay-{Guid.NewGuid():N}");
        string packageName = $"Match.Query.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string asset = "lib/net10.0/Replay.Target.dll";
        Directory.CreateDirectory(root);
        using var feed = new RangeReplayFeed(packageName, version, queryDistinctSources: true);
        string configFile = Path.Combine(root, "NuGet.Config");
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;

        try
        {
            File.WriteAllText(configFile, $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="feed-a" value="{feed.SourceA}" />
                    <add key="feed-b" value="{feed.SourceB}" />
                  </packageSources>
                  {(useMapping ? $"""
                    <packageSourceMapping>
                      <packageSource key="feed-b">
                        <package pattern="{packageName}" />
                      </packageSource>
                    </packageSourceMapping>
                    """ : "")}
                </configuration>
                """);
            string package = CreatePackageArchive(
                root, "package", packageName, version, asset,
                BuildMatchTargetAssembly("Replay.Target", "Replay", "Target"));
            feed.AddPackage(feed.SourceB, packageName, version, package);
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = false });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-query-replay", Path.Combine(root, "cache"), skipNuGetCache: true);

            var (discoveryExit, discoveryOutput, discoveryError) = await RunCliAsync(
                "match", "Replay.Target.Seed", "--similar",
                "--package", $"{packageName}@{version}..{version}", "--at", "last",
                "--library", asset, "--nugetconfig", configFile, "--all", "--format=json");

            Assert.Equal(1, feed.PayloadRequests(feed.SourceB, packageName, version));
            Assert.Equal(0, feed.PayloadRequests(feed.SourceA, packageName, version));
            Assert.DoesNotContain("channel=", discoveryOutput + discoveryError);
            if (!useMapping)
            {
                Assert.Equal(1, discoveryExit);
                Assert.Empty(discoveryOutput);
                Assert.Contains("package source mapping", discoveryError);
                return;
            }

            Assert.True(discoveryExit == 0, discoveryError);
            Assert.Empty(discoveryError);
            string disclosure = Parse(discoveryOutput).GetProperty("disclosure").GetString()!;
            Assert.Contains($"--nugetconfig {ShellCommandText.Quote(configFile)}", disclosure);
            Assert.DoesNotContain("--source", disclosure);
            var (replayExit, replayOutput, replayError) = await RunCliAsync(
                "match", "Replay.Target.Seed", "Replay.Target.ExactPeer",
                "--package", $"{packageName}@{version}", "--library", asset,
                "--tfm", "net10.0", "--nugetconfig", configFile, "--all", "--format=json");

            Assert.True(replayExit == 0, replayError);
            Assert.Empty(replayError);
            Assert.Equal("Exact", Parse(replayOutput).GetProperty("relation").GetString());
            Assert.Equal(2, feed.PayloadRequests(feed.SourceB, packageName, version));
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_RangeToolWrapperReplayUsesFinalPackageSourcePolicy()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-wrapper-source-replay-{Guid.NewGuid():N}");
        string cacheRoot = Path.Combine(root, "cache");
        string wrapperName = $"Match.Wrapper.Replay.{Guid.NewGuid():N}";
        string targetName = $"Match.Target.Replay.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        string asset = $"lib/net10.0/{Path.GetFileName(TestAssembly)}";
        Directory.CreateDirectory(root);
        using var feed = new RangeReplayFeed(wrapperName, version);
        string sourceA = feed.SourceA;
        string sourceB = feed.SourceB;
        bool wasOffline = DotnetInspector.Networking.HttpClientFactory.IsOffline;

        try
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = false });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-match-wrapper-source-replay",
                cacheRoot,
                skipNuGetCache: true);
            string wrapperPackage = CreateToolWrapperArchive(
                root,
                wrapperName,
                version,
                targetName);
            string targetPackage = CreatePackageArchive(
                root,
                "target",
                targetName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            feed.AddPackage(sourceB, wrapperName, version, wrapperPackage);
            feed.AddPackage(sourceA, targetName, version, targetPackage);

            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = $"{wrapperName}@{version}..{version}",
                PackageRangeAddress = "last",
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = new NuGetSourceOptions
                {
                    Sources = [sourceA, sourceB],
                },
            };

            var (discoveryExit, output, discoveryError) =
                await RunAsync(options);

            Assert.True(
                discoveryExit == 0,
                $"Expected discovery success, got {discoveryExit}: {discoveryError}");
            Assert.Empty(discoveryError);
            string disclosure =
                Parse(output).GetProperty("disclosure").GetString()!;
            Assert.Contains(
                $"--package '{targetName}@{version}'",
                disclosure,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"--source '{sourceA}'", disclosure);
            Assert.Contains($"--source '{sourceB}'", disclosure);

            string[] exactArguments =
            [
                "match",
                SampleSeed,
                $"{typeof(MatchDiscoverySample).FullName}.ExactPeer",
                "--package",
                $"{targetName}@{version}",
                "--library",
                asset,
                "--tfm",
                "net10.0",
                "--all",
                "--format=json",
            ];
            var (replayExit, replayOutput, replayError) =
                await RunCliAsync(
                    [.. exactArguments, "--source", sourceA, "--source", sourceB]);
            var (staleExit, _, staleError) =
                await RunCliAsync(
                    [.. exactArguments, "--source", sourceB]);

            Assert.Equal(0, replayExit);
            Assert.Empty(replayError);
            Assert.Equal(
                "Exact",
                Parse(replayOutput).GetProperty("relation").GetString());
            Assert.Equal(1, staleExit);
            Assert.Contains("No eligible source supplied this exact coordinate.", staleError);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions { Offline = wasOffline });
            DotnetInspector.Networking.HttpClientFactory.ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("lib/net10.0/Target\u202E.dll")]
    [InlineData("lib/net10.0/a`b/Target.dll")]
    public async Task Similar_PackageAssetThatCannotBeDisclosedLosslessly_IsRefused(
        string asset)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-replay-containment-{Guid.NewGuid():N}");
        const string packageName = "Match.Replay.Containment";
        const string version = "1.0.0";
        Directory.CreateDirectory(root);

        try
        {
            string package = CreatePackageArchive(
                root,
                "package",
                packageName,
                version,
                asset,
                File.ReadAllBytes(TestAssembly));
            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = package,
                AssemblyPath = asset,
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
            };

            var (exit, output, error) = await RunAsync(options);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("library selector", error);
            Assert.Contains("cannot be emitted losslessly", error);
            Assert.DoesNotContain(asset, error, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Similar_NonPublicSeedDisclosureRetainsAll()
    {
        string selector = $"{typeof(MatchDiscoveryTests).FullName}.{nameof(Parse)}";
        var (exit, output, error) = await RunCliAsync(
            "match",
            selector,
            "--similar",
            "--library",
            TestAssembly,
            "--all",
            "--format=json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        JsonElement document = Parse(output);
        Assert.Contains(
            "--all",
            document.GetProperty("disclosure").GetString());
        string candidateToken = document
            .GetProperty("candidates")
            .EnumerateArray()
            .First()
            .GetProperty("token")
            .GetString()!;

        var (replayExit, _, replayError) = await RunCliAsync(
            "match",
            selector,
            candidateToken,
            "--library",
            TestAssembly,
            "--all",
            "--format=json");

        Assert.Equal(0, replayExit);
        Assert.Empty(replayError);
    }

    [Fact]
    public async Task Similar_UnavailableForwardedSeed_ReportsTheTypedFailureAndTarget()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"match-missing-forwarder-{Guid.NewGuid():N}");
        string facade = Path.Combine(directory, "Facade.dll");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(
                facade,
                BuildForwarderFacade(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        "Missing.Target",
                        new Version(1, 0, 0, 0),
                        "fr-FR",
                        "0011223344556677"),
                    "Missing",
                    "Forwarded"));
            var options = new MatchOptions
            {
                LeftSelector = "Missing.Forwarded.Member",
                AssemblyPath = facade,
                IncludeAll = true,
                Similar = true,
            };

            var (exitCode, output, error) = await RunAsync(options);

            Assert.Equal(1, exitCode);
            Assert.Empty(output);
            Assert.Contains(
                "Forwarded type 'Missing.Forwarded' could not be resolved: UnboundBinding.",
                error);
            Assert.Contains(
                "Target: Missing.Target, Version=1.0.0.0, Culture=fr-FR, "
                    + "PublicKeyToken=0011223344556677.",
                error);
            Assert.DoesNotContain("must name a Type.Member selector", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

}
