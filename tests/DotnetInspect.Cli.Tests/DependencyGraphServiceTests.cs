using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class DependencyGraphServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"dependency-graph-tests-{Guid.NewGuid():N}");

    public DependencyGraphServiceTests()
    {
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            Path.Combine(_testRoot, "cache"),
            skipNuGetCache: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public async Task BuildTypeDependencyTreeAsync_LocalAssemblyRetainsQueryParticipant()
    {
        using var httpClient = new HttpClient();
        var result =
            await DependencyGraphService.BuildTypeDependencyTreeAsync(
                httpClient,
                new DependsOptions
                {
                    TargetType =
                        typeof(DependencyGraphServiceTests).FullName!,
                    Assemblies =
                    [
                        typeof(DependencyGraphServiceTests)
                            .Assembly.Location,
                    ],
                },
                new VerboseLogger(enabled: false),
                TestContext.Current.CancellationToken);

        Assert.True(result.IsAvailable);
        Assert.True(result.Dependency.Found);
        AssemblyContextTypeDependencyEntry participant =
            Assert.Single(
                result.Envelope!
                    .Content
                    .QueryResult
                    .Participants);
        Assert.IsType<
            AssemblyContextTypeDependencyEntry.Completed>(
                participant);
    }

    [Fact]
    public async Task BuildTypeDependencyTreeAsync_UnsupportedOnlyIsUnavailable()
    {
        Directory.CreateDirectory(_testRoot);
        string path = Path.Combine(
            _testRoot,
            "unsupported.dll");
        File.WriteAllBytes(
            path,
            MetadataTestImages.BuildWindowsMetadataImage());
        using var httpClient = new HttpClient();

        TypeDependencyExecutionResult result =
            await DependencyGraphService.BuildTypeDependencyTreeAsync(
                httpClient,
                new DependsOptions
                {
                    TargetType = "No.Such.Type",
                    Assemblies = [path],
                },
                new VerboseLogger(enabled: false),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsAvailable);
        TypeDependencyScanDiagnostic diagnostic =
            Assert.Single(result.Diagnostics);
        Assert.Equal(path, diagnostic.Subject);
        Assert.Equal(
            CandidateOpenFailureKind.UnsupportedMetadataFormat,
            diagnostic.Failure.Kind);
        Assert.Empty(
            result.Envelope!
                .Content
                .QueryResult
                .Participants);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_FileInput_ReturnsAssemblyReferenceGraph()
    {
        var assemblyPath = typeof(DependencyGraphServiceTests).Assembly.Location;
        using var httpClient = new HttpClient();
        var logger = new VerboseLogger(enabled: false);

        var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
            httpClient,
            assemblyPath,
            sourceOptions: null,
            logger,
            maxDepth: null,
            cancellationToken: TestContext.Current.CancellationToken);

        var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
        Assert.Equal("DotnetInspect.Cli.Tests", graph.AssemblyName);
        Assert.NotEmpty(graph.References);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_CancellationReachesPackageAcquisition()
    {
        string packageId = $"Depends.Cancel.{Guid.NewGuid():N}";
        string source =
            $"https://feed.example.test/{Guid.NewGuid():N}/v3/index.json";
        var handler = new CancellationObservingHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        Task<LibraryDependencyGraphResult> operation =
            DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                $"{packageId}@1.0.0",
                new NuGetSourceOptions { Sources = [source] },
                new VerboseLogger(enabled: false),
                maxDepth: null,
                cancellationToken: cancellation.Token);

        await handler.RequestStarted.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        await handler.CancellationObserved.WaitAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputUsesLibDescendingRootOrder()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-root-test").FullName;
        var packagePath = Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteAssemblyEntry(
                    archive,
                    "lib/net6.0/A.dll",
                    "Net6Root");
                WriteAssemblyEntry(
                    archive,
                    "lib/netstandard2.0/Z.dll",
                    "NetStandardRoot");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger,
                maxDepth: null,
                cancellationToken: TestContext.Current.CancellationToken);

            var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
            Assert.Equal("NetStandardRoot", graph.AssemblyName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputHonorsRequestedTfm()
    {
        string packageDir =
            Directory.CreateTempSubdirectory("depends-package-tfm-test")
                .FullName;
        string packagePath =
            Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");

        try
        {
            using (var archive =
                ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteAssemblyEntry(
                    archive,
                    "lib/net6.0/A.dll",
                    "Net6Root");
                WriteAssemblyEntry(
                    archive,
                    "lib/netstandard2.0/Z.dll",
                    "NetStandardRoot");
            }

            using var httpClient = new HttpClient();
            var result =
                await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                    httpClient,
                    packagePath,
                    sourceOptions: null,
                    new VerboseLogger(enabled: false),
                    maxDepth: null,
                    TestContext.Current.CancellationToken,
                    requestedTfm: "net6.0");

            Assert.Equal(
                "Net6Root",
                Assert.IsType<LibraryDependencyGraphResult.Graph>(result)
                    .AssemblyName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputRejectsMissingTfm()
    {
        string packageDir =
            Directory.CreateTempSubdirectory("depends-package-tfm-miss-test")
                .FullName;
        string packagePath =
            Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");

        try
        {
            using (var archive =
                ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteAssemblyEntry(
                    archive,
                    "lib/net6.0/A.dll",
                    "Net6Root");
            }

            using var httpClient = new HttpClient();
            var result =
                await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                    httpClient,
                    packagePath,
                    sourceOptions: null,
                    new VerboseLogger(enabled: false),
                    maxDepth: null,
                    TestContext.Current.CancellationToken,
                    requestedTfm: "net20.0");

            LibraryDependencyGraphResult.Error error =
                Assert.IsType<LibraryDependencyGraphResult.Error>(result);
            Assert.Contains(
                "target framework 'net20.0'",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PlatformInputHonorsRequestedTfm()
    {
        string? originalDotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string temporaryRoot = Directory.CreateTempSubdirectory(
            "depends-platform-tfm-").FullName;
        try
        {
            WriteInstalledPlatformAssembly(
                temporaryRoot,
                "42.0.1",
                "net42.0",
                "Runtime42");
            WriteInstalledPlatformAssembly(
                temporaryRoot,
                "43.0.1",
                "net43.0",
                "Runtime43");
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                temporaryRoot);

            using var httpClient = new HttpClient();
            LibraryDependencyGraphResult selected42 =
                await DependencyGraphService
                    .BuildLibraryDependencyTreeAsync(
                        httpClient,
                        "System.Runtime",
                        sourceOptions: null,
                        new VerboseLogger(enabled: false),
                        maxDepth: null,
                        TestContext.Current.CancellationToken,
                        traverseReferences: false,
                        requestedTfm: "net42.0");
            LibraryDependencyGraphResult selected43 =
                await DependencyGraphService
                    .BuildLibraryDependencyTreeAsync(
                        httpClient,
                        "System.Runtime",
                        sourceOptions: null,
                        new VerboseLogger(enabled: false),
                        maxDepth: null,
                        TestContext.Current.CancellationToken,
                        traverseReferences: false,
                        requestedTfm: "net43.0");

            Assert.Equal(
                "Runtime42",
                Assert.IsType<LibraryDependencyGraphResult.Empty>(
                    selected42).AssemblyName);
            Assert.Equal(
                "Runtime43",
                Assert.IsType<LibraryDependencyGraphResult.Empty>(
                    selected43).AssemblyName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                originalDotnetRoot);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void WriteAssemblyEntry(
        ZipArchive archive,
        string entryName,
        string assemblyName)
    {
        byte[] image = BuildAssemblyImage(
            Path.GetFileName(entryName),
            assemblyName,
            includeSystemRuntimeReference: true);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream stream = entry.Open();
        stream.Write(image);
    }

    private static void WriteInstalledPlatformAssembly(
        string dotnetRoot,
        string version,
        string targetFramework,
        string assemblyName)
    {
        string refDirectory = Directory.CreateDirectory(
            Path.Combine(
                dotnetRoot,
                "packs",
                "Microsoft.NETCore.App.Ref",
                version,
                "ref",
                targetFramework)).FullName;
        File.WriteAllBytes(
            Path.Combine(refDirectory, "System.Runtime.dll"),
            BuildAssemblyImage(
                "System.Runtime.dll",
                assemblyName,
                includeSystemRuntimeReference: false));
    }

    private static byte[] BuildAssemblyImage(
        string moduleName,
        string assemblyName,
        bool includeSystemRuntimeReference)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString(
                moduleName),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (includeSystemRuntimeReference)
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputWithNoLibAssembliesReportsSpecificError()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-no-lib-test").FullName;
        var packagePath = Path.Combine(packageDir, "NoLib.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "tools/NoLib.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger,
                maxDepth: null,
                cancellationToken: TestContext.Current.CancellationToken);

            var error = Assert.IsType<LibraryDependencyGraphResult.Error>(result);
            Assert.Contains("No libraries found in package", error.Message);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public void ResolvedSourceRestriction_ReleasesAmbientSources()
    {
        const string FeedA = "https://feed-a.example/v3/index.json";
        const string FeedB = "https://feed-b.example/v3/index.json";
        var original = new NuGetSourceOptions
        {
            Sources = [FeedA, FeedB],
        };
        NuGetSourceOptions restricted =
            NuGetSourceResolver.RestrictToResolvedSources(
                original,
                [new NuGetFetch.PackageSource(FeedA, FeedA)]);

        Assert.Equal(
            [FeedA],
            NuGetSourceResolver.ResolveSources(restricted)
                .Select(source => source.Url));

        NuGetSourceOptions? unrestricted =
            NuGetSourceResolver.WithoutSourceRestriction(restricted);
        Assert.Equal(
            [FeedA, FeedB],
            NuGetSourceResolver.ResolveSources(unrestricted)
                .Select(source => source.Url));
    }

    private sealed class CancellationObservingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => _requestStarted.Task;

        public Task CancellationObserved => _cancellationObserved.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requestStarted.TrySetResult(true);
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _cancellationObserved.TrySetResult(true);
                throw;
            }

            throw new InvalidOperationException("Unreachable.");
        }
    }

}
