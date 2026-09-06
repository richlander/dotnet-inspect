using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

using DotnetInspector.CommandLine;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;
using NuGetFetch.Plugins;

using CoreHttpClientFactory = DotnetInspector.Core.HttpClientFactory;
using DesktopPackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed partial class ConfiguredPayloadAcquisitionTests : IDisposable
{
    private const string Version = "1.0.0";
    private const string FirstFeed = "https://first-payload.invalid/v3/index.json";
    private const string SecondFeed = "https://second-payload.invalid/v3/index.json";
    private readonly string _root = Path.GetFullPath(Path.Combine(
        "artifacts", "tests", $"configured-payload-{Guid.NewGuid():N}"));

    public ConfiguredPayloadAcquisitionTests()
    {
        Directory.CreateDirectory(_root);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            inner => new RejectNetworkHandler(inner));
        NuGetCache.Initialize(
            "dotnet-inspect-test", Path.Combine(_root, "cache"), skipNuGetCache: true);
    }

    public void Dispose()
    {
        CoreHttpClientFactory.SetAuthenticationDecorator(null);
        CoreHttpClientFactory.Initialize(new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect");
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCommand_ExactLocalPinPrintsPayloadWithoutHttp(bool hierarchicalFileUri)
    {
        string id = $"Pinned.Local.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "local-feed");
        const string Readme = "The exact local package's README.";
        WriteLocalPackage(source, id, Readme, hierarchical: hierarchicalFileUri);
        int transports = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
        {
            transports++;
            throw new InvalidOperationException("Local payload acquisition created HTTP transport.");
        });

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source",
                hierarchicalFileUri ? new Uri(source).AbsoluteUri : source,
                "--path", "@readme", "--content", "--bare"]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Equal(Readme, output.Trim());
        Assert.Empty(error);
        Assert.Equal(0, transports);
    }

    [Fact]
    public async Task AcquirePinned_LocalPrecedesHttpAndDeclarationOrderDoesNotChoosePayload()
    {
        const string Id = "Pinned.LocalPrecedence";
        string firstLocal = Path.Combine(_root, "first-local");
        string secondLocal = Path.Combine(_root, "second-local");
        WriteLocalPackage(firstLocal, Id, "first local bytes");
        WriteLocalPackage(secondLocal, Id, "second local bytes", hierarchical: true);
        var requests = new ConcurrentQueue<string>();
        string? selectedReadme = null;
        string? selectedRoot = null;

        foreach (string[] sources in new[]
                 {
                     new[] { FirstFeed, firstLocal, secondLocal },
                     new[] { secondLocal, firstLocal, FirstFeed },
                 })
        {
            await using var composition = CreateComposition(
                (source, _) => new PayloadFeedHandler(
                    source.Url, Id, () => PackageContent(Id, "different HTTP bytes"), requests));
            ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
                Id, Version, (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { Sources = sources },
                cancellationToken: TestContext.Current.CancellationToken);

            AcquiredPackageSourcePayload payload = AssertPayload(result, Id);
            Assert.Equal(ConfiguredPackageAuthorityKind.LocalFolder, result.Authority!.Kind);
            Assert.Empty(result.Failures);
            string readme = ReadReadme(payload.Content);
            Assert.Contains(readme, new[] { "first local bytes", "second local bytes" });
            if (selectedReadme is not null)
            {
                Assert.Equal(selectedReadme, readme);
                Assert.Equal(selectedRoot, result.Authority.LocalIdentity!.CanonicalPath);
            }
            selectedReadme = readme;
            selectedRoot = result.Authority.LocalIdentity!.CanonicalPath;
        }

        Assert.Empty(requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcquirePinned_UnavailableLocalPeersRetainAttributedFailures(bool exactPinExists)
    {
        const string Id = "Pinned.LocalPeer";
        string unreadable = Path.Combine(_root, "a-not-a-directory");
        string missing = Path.Combine(_root, "b-missing");
        string healthy = Path.Combine(_root, "z-healthy");
        File.WriteAllText(unreadable, "This source root is a file, not a directory.");
        WriteLocalPackage(healthy, Id, "healthy local payload");
        await using var composition = LocalComposition();
        ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
            Id, exactPinExists ? Version : "2.0.0",
            (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [healthy, missing, unreadable] },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Failures.Count);
        Assert.All(result.Failures, failure =>
        {
            Assert.Equal(PackageAuthorityFailureKind.Transport, failure.Kind);
            Assert.NotNull(failure.ResultSource);
            Assert.NotNull(failure.SourceFailure);
            Assert.DoesNotContain("not found", failure.Message, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(result.Failures, failure => failure.Authority.ToString().Contains(
            unreadable, StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Authority.ToString().Contains(
            missing, StringComparison.Ordinal));
        if (exactPinExists)
        {
            Assert.Equal("healthy local payload", ReadReadme(AssertPayload(result, Id).Content));
            Assert.Equal(healthy, result.Authority!.Source.Url);
        }
        else
        {
            Assert.Null(result.Payload);
            Assert.Null(result.Authority);
            using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
            PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
                client, Id, "2.0.0",
                sourceOptions: new NuGetSourceOptions { Sources = [healthy, missing, unreadable] });
            Assert.False(outcome.IsSuccess);
            Assert.NotNull(outcome.ErrorMessage);
            Assert.Contains(unreadable, outcome.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("not found", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AcquirePinned_MappingFiltersAliasesBeforeCollapsingAuthorities()
    {
        const string Id = "Pinned.Mapped";
        string allowed = Path.Combine(_root, "z-allowed");
        string excluded = Path.Combine(_root, "a-excluded");
        WriteLocalPackage(allowed, Id, "mapped payload");
        WriteLocalPackage(excluded, Id, "excluded payload");
        string config = WriteConfig(
            [
                ("excluded-alias", "z-allowed", "Other.*"),
                ("allowed-path", "z-allowed", Id),
                ("allowed-uri", new Uri(allowed).AbsoluteUri, Id),
                ("excluded-root", "a-excluded", "Other.*"),
            ]);
        var authorities = new HashSet<ConfiguredPackageAuthority>();
        var stores = new Dictionary<ConfiguredPackageAuthority, IPackageStore>();
        await using var composition = LocalComposition();
        ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
            Id, Version,
            (authority, _) =>
            {
                authorities.Add(authority);
                if (!stores.TryGetValue(authority, out IPackageStore? store))
                    stores.Add(authority, store = new InMemoryPackageStore());
                return store;
            },
            new NuGetSourceOptions { ConfigFile = config },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mapped payload", ReadReadme(AssertPayload(result, Id).Content));
        Assert.Empty(result.Failures);
        Assert.Same(Assert.Single(authorities), result.Authority);
        Assert.Contains(result.Authority!.Source.Name, new[] { "allowed-path", "allowed-uri" });
    }

    [Fact]
    public async Task AcquirePinned_RequestTimeoutCanFailOverWithinExternalOperation()
    {
        const string Id = "Pinned.RequestTimeout";
        var requests = new ConcurrentQueue<string>();
        string? stalledSource = null;
        await using var composition = CreateComposition(
            (source, _) => new PayloadFeedHandler(
                source.Url, Id, () => PackageContent(Id, "later healthy authority"), requests,
                async token =>
                {
                    stalledSource ??= source.Url;
                    if (source.Url == stalledSource)
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }),
            TimeSpan.FromMilliseconds(50));
        using var operation = new NuGetOperationContext(
            TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
            Id, Version, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [SecondFeed, FirstFeed] },
            cancellationToken: TestContext.Current.CancellationToken,
            operationContext: operation);

        Assert.Equal("later healthy authority", ReadReadme(AssertPayload(result, Id).Content));
        PackageAuthorityFailure failure = Assert.Single(result.Failures);
        Assert.Equal(PackageAuthorityFailureKind.Timeout, failure.Kind);
        Assert.NotNull(stalledSource);
        Assert.Contains(stalledSource, failure.Authority.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(stalledSource, result.Authority!.Source.Url);
        Assert.Contains(requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
        operation.ThrowIfExpired();
    }

    [Fact]
    public async Task AcquirePinned_OperationTimeoutIsTerminalBeforeHealthyPeer()
    {
        const string Id = "Pinned.OperationTimeout";
        var requests = new ConcurrentQueue<string>();
        string? stalledSource = null;
        await using var composition = CreateComposition(
            (source, _) => new PayloadFeedHandler(
                source.Url, Id, () => PackageContent(Id, "must not be consulted"), requests,
                async token =>
                {
                    stalledSource ??= source.Url;
                    if (source.Url == stalledSource)
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }));
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);

        ConfiguredPackagePayloadResult result = await composition.AcquirePinnedAsync(
            Id, Version, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [FirstFeed, SecondFeed] },
            cancellationToken: TestContext.Current.CancellationToken,
            operationContext: operation);

        Assert.Null(result.Payload);
        Assert.Null(result.Authority);
        Assert.Contains(result.Failures, failure =>
            failure.Kind == PackageAuthorityFailureKind.Timeout
            && failure.Timeout?.Kind == PackageSourceTimeoutKind.Operation);
        Assert.NotNull(stalledSource);
        Assert.All(requests, request => Assert.Equal(stalledSource, request));
    }

    [Fact]
    public async Task AcquirePinned_CallerCancellationRetainsOriginalToken()
    {
        const string Id = "Pinned.Cancellation";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), cancellation.Token);
        await using var composition = CreateComposition(
            (source, _) => new PayloadFeedHandler(
                source.Url, Id, () => PackageContent(Id, "unused"), new(),
                async token =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }));

        Task<ConfiguredPackagePayloadResult> acquisition = composition.AcquirePinnedAsync(
            Id, Version, (_, _) => new InMemoryPackageStore(),
            new NuGetSourceOptions { Sources = [FirstFeed] },
            cancellationToken: cancellation.Token,
            operationContext: operation);
        await Task.WhenAny(entered.Task, acquisition).WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(entered.Task.IsCompleted, "Acquisition completed before entering transport.");
        cancellation.Cancel();

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => acquisition);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task AcquirePinned_ContextLivesThroughBodyAndCommitAndRemainsCallerOwned()
    {
        const string Id = "Pinned.OperationLifetime";
        byte[] archive = CreatePackage(Id, "committed with a live context");
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        int reads = 0;
        var store = new GatedCommitStore(operation);
        await using (var composition = CreateComposition(
            (source, _) => new PayloadFeedHandler(source.Url, Id, () =>
            {
                var content = new StreamContent(new ReadTrackingStream(archive, () =>
                {
                    operation.ThrowIfExpired();
                    reads++;
                }));
                content.Headers.ContentLength = archive.Length;
                return content;
            }, new())))
        {
            Task<ConfiguredPackagePayloadResult> acquisition = composition.AcquirePinnedAsync(
                Id, Version, (_, _) => store,
                new NuGetSourceOptions { Sources = [FirstFeed] },
                cancellationToken: TestContext.Current.CancellationToken,
                operationContext: operation);
            try
            {
                await Task.WhenAny(store.Entered.Task, acquisition)
                    .WaitAsync(TestContext.Current.CancellationToken);
                Assert.True(store.Entered.Task.IsCompleted, "Acquisition completed before entering commit.");
                Assert.True(reads > 0);
                Assert.False(acquisition.IsCompleted);
                Assert.False(store.Committed);
                operation.ThrowIfExpired();
            }
            finally
            {
                store.Release.TrySetResult();
            }

            ConfiguredPackagePayloadResult result = await acquisition;
            Assert.True(store.Committed);
            Assert.Equal("committed with a live context", ReadReadme(AssertPayload(result, Id).Content));
            operation.ThrowIfExpired();
        }

        operation.ThrowIfExpired();
    }

    [Fact]
    public async Task ExtractPinnedPackage_ExactHttpPinKeepsPayloadUntilCallerCleanup()
    {
        string id = $"Pinned.Http.{Guid.NewGuid():N}";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source, id, () => PackageContent(id, "HTTP package content"), requests));
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));
        PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
            client, id, Version,
            sourceOptions: new NuGetSourceOptions { Sources = [FirstFeed] });

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        PackageExtractionResult result = outcome.Result!;
        try
        {
            Assert.Equal(ConfiguredPackageAuthorityKind.Http, result.Authority!.Kind);
            Assert.Null(result.CacheScopeKey);
            Assert.False(result.FromCache);
            Assert.NotNull(result.TempDir);
            Assert.True(Directory.Exists(result.TempDir));
            Assert.True(File.Exists(result.NupkgPath));
            Assert.Equal("HTTP package content", File.ReadAllText(
                Path.Combine(result.ExtractPath, "README.md")));
            Assert.Contains(requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(result.TempDir);
        }

        Assert.False(Directory.Exists(result.TempDir));
        Assert.False(Directory.Exists(result.ExtractPath));
    }

    [Fact]
    public async Task ExtractPinnedPackage_LocalWrapperReauthorizesRedirectedId()
    {
        const string WrapperId = "Pinned.Wrapper";
        const string PayloadId = "Pinned.Wrapper.Payload";
        string wrapperRoot = Path.Combine(_root, "wrapper");
        string payloadRoot = Path.Combine(_root, "payload");
        WriteLocalPackage(wrapperRoot, WrapperId, "wrapper README", redirectId: PayloadId);
        WriteLocalPackage(wrapperRoot, PayloadId, "wrong root's payload");
        WriteLocalPackage(payloadRoot, PayloadId, "redirected mapped payload", hierarchical: true);
        string config = WriteConfig(
            [("wrapper", wrapperRoot, WrapperId), ("payload", payloadRoot, PayloadId)]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => throw new InvalidOperationException("Local wrapper created HTTP transport."));
        using var client = new HttpClient(new RejectNetworkHandler(new HttpClientHandler()));

        PackageExtractionOutcome outcome = await DesktopPackageExtractor.ExtractPinnedPackageAsync(
            client, WrapperId, Version,
            sourceOptions: new NuGetSourceOptions { ConfigFile = config });

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        PackageExtractionResult result = outcome.Result!;
        try
        {
            Assert.Equal(PayloadId, result.PackageName, ignoreCase: true);
            Assert.Equal("redirected mapped payload", File.ReadAllText(
                Path.Combine(result.ExtractPath, "README.md")));
            Assert.Equal(payloadRoot, result.Authority!.Source.Url);
            ToolWrapperPackage wrapper = Assert.Single(result.ToolWrapperChain);
            Assert.Equal(WrapperId, wrapper.PackageName, ignoreCase: true);
            Assert.Equal(wrapperRoot, wrapper.Authority!.Source.Url);
            Assert.NotSame(wrapper.Authority, result.Authority);
            Assert.True(Directory.Exists(wrapper.ExtractPath));
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(result.TempDir);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCommand_HttpWrapperCleansTemporaryStorageAndKeepsLocalTarget(bool warmTarget)
    {
        const string WrapperId = "Pinned.Cleanup.Wrapper";
        const string PayloadId = "Pinned.Cleanup.Payload";
        const string Readme = "The retained local target.";
        string localFeed = Path.Combine(_root, "local-feed");
        string temporaryRoot = Directory.CreateDirectory(Path.Combine(_root, "command-temp")).FullName;
        WriteLocalPackage(localFeed, PayloadId, Readme);
        if (warmTarget)
        {
            var (exit, output, error) = await RunIsolatedCommandAsync(temporaryRoot,
                ["package", $"{PayloadId}@{Version}", "--source", localFeed,
                    "--path", "@readme", "--content", "--bare", "--no-nuget-cache"]);
            Assert.True(exit == 0, $"Exit {exit}: {error}");
            Assert.Equal(Readme, output.Trim());
            Assert.Empty(Directory.EnumerateDirectories(temporaryRoot, "inspect-pkg*"));
            Directory.Delete(localFeed, recursive: true);
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        string source = $"{origin}v3/index.json";
        var requests = new ConcurrentQueue<string>();
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Task server = ServeFeedAsync(listener,
            new PayloadFeedHandler(source, WrapperId,
                () => new ByteArrayContent(CreatePackage(WrapperId, "wrapper README", PayloadId)),
                requests),
            origin, shutdown.Token);
        try
        {
            for (int iteration = 1; iteration <= 2; iteration++)
            {
                // With the source gone, success proves the isolated CLI retained the target.
                if (warmTarget || iteration > 1)
                    Assert.False(Directory.Exists(localFeed));

                var (exit, output, error) = await RunIsolatedCommandAsync(temporaryRoot,
                    ["package", $"{WrapperId}@{Version}", "--source", localFeed,
                        "--source", source, "--path", "@readme", "--content", "--bare",
                        "--no-nuget-cache"]);
                Assert.True(exit == 0, $"Exit {exit}: {error}");
                Assert.Equal(Readme, output.Trim());
                Assert.Equal(iteration, requests.Count(request =>
                    request.EndsWith(".nupkg", StringComparison.Ordinal)));
                Assert.Empty(Directory.EnumerateDirectories(temporaryRoot, "inspect-pkg*"));

                if (!warmTarget && iteration == 1)
                    Directory.Delete(localFeed, recursive: true);
            }
        }
        finally
        {
            shutdown.Cancel();
            try
            {
                await server;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
        }
    }

    private static DesktopPackageSourceComposition CreateComposition(
        DesktopPackageSourceComposition.SourceTransportFactory transport,
        TimeSpan? requestTimeout = null) =>
        new(requestTimeout ?? TimeSpan.FromSeconds(5), new UnavailableCredentials(), transport);

    private static DesktopPackageSourceComposition LocalComposition() =>
        CreateComposition((_, _) =>
            throw new InvalidOperationException("Local composition created HTTP transport."));

    private static AcquiredPackageSourcePayload AssertPayload(
        ConfiguredPackagePayloadResult result, string packageId)
    {
        Assert.NotNull(result.Authority);
        AcquiredPackageSourcePayload payload = Assert.IsType<AcquiredPackageSourcePayload>(result.Payload);
        Assert.Equal(packageId, payload.Coordinate.PackageId, ignoreCase: true);
        Assert.Equal(Version, payload.Coordinate.Version);
        Assert.Equal(payload.ProducerKey, payload.Content.ProducerKey);
        Assert.Equal(PackagePayloadOrigin.Download, payload.Origin);
        return payload;
    }

    private static string ReadReadme(IPackageContent content)
    {
        Assert.True(content.TryOpenEntry("README.md", out Stream? stream));
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private string WriteConfig((string Name, string Source, string Pattern)[] sources)
    {
        string path = Path.Combine(_root, $"sources-{Guid.NewGuid():N}.config");
        new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear"),
                sources.Select(source => new XElement("add",
                    new XAttribute("key", source.Name), new XAttribute("value", source.Source)))),
            new XElement("packageSourceMapping",
                sources.Select(source => new XElement("packageSource",
                    new XAttribute("key", source.Name),
                    new XElement("package", new XAttribute("pattern", source.Pattern)))))))
            .Save(path);
        return path;
    }

    private static void WriteLocalPackage(
        string root, string id, string readme, bool hierarchical = false,
        string? redirectId = null, string version = Version)
    {
        string directory = hierarchical ? Path.Combine(root, id.ToLowerInvariant(), version) : root;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, $"{id.ToLowerInvariant()}.{version}.nupkg"),
            CreatePackage(id, readme, redirectId, version));
    }

    private static HttpContent PackageContent(string id, string readme) =>
        new ByteArrayContent(CreatePackage(id, readme));

    private static byte[] CreatePackage(
        string id, string readme, string? redirectId = null, string version = Version)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, $"{id}.nuspec", $"""
                <package><metadata>
                  <id>{id}</id><version>{version}</version>
                  <authors>Payload tests</authors><description>Exact-pin fixture</description>
                  <readme>README.md</readme>
                </metadata></package>
                """);
            WriteEntry(archive, "README.md", readme);
            if (redirectId is not null)
            {
                WriteEntry(archive, "tools/net10.0/any/DotnetToolSettings.xml", $"""
                    <DotNetCliTool Version="2">
                      <Commands><Command Name="{id}" /></Commands>
                      <RuntimeIdentifierPackages>
                        <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="{redirectId}" />
                      </RuntimeIdentifierPackages>
                    </DotNetCliTool>
                    """);
            }
        }
        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }

    private static Task<(int Exit, string Output, string Error)> RunCommandAsync(string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var parsed = CommandLineBuilder.CreateRootCommand().Parse(
                CommandLineBuilder.PreprocessArgs(args));
            Assert.Empty(parsed.Errors);
            return await CommandLineBuilder.InvokeAsync(parsed);
        });

    private async Task<(int Exit, string Output, string Error)> RunIsolatedCommandAsync(
        string temporaryRoot, string[] args)
    {
        string executable = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in args)
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_INSPECT_CACHE_DIR"] = Path.Combine(_root, "cache");
        start.Environment["DOTNET_INSPECT_OFFLINE"] = "";
        foreach (string variable in new[] { "TMPDIR", "TMP", "TEMP" })
            start.Environment[variable] = temporaryRoot;

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
                OutOfProcessCliProcess.KillAndWaitForExit(process, TimeSpan.FromSeconds(10));
        }
    }

    private static async Task ServeFeedAsync(
        TcpListener listener, HttpMessageHandler handler, string origin, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(handler);
        while (true)
        {
            using TcpClient connection = await listener.AcceptTcpClientAsync(cancellationToken);
            using NetworkStream stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            string request = await reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("The fixture received no HTTP request line.");
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
            {
            }
            using HttpResponseMessage response = await client.GetAsync(
                new Uri(new Uri(origin), request.Split(' ')[1]), cancellationToken);
            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            byte[] headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n"
                + $"Content-Length: {content.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken);
            await stream.WriteAsync(content, cancellationToken);
        }
    }

    private sealed class UnavailableCredentials : ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<PackageSourceCredential?> GetCredentialsAsync(
            Uri uri, bool isRetry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These public fixtures do not require credentials.");
    }

    private sealed class RejectNetworkHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected legacy HTTP request: {request.RequestUri}");
    }

    private sealed class PayloadFeedHandler(
        string source,
        string id,
        Func<HttpContent> payload,
        ConcurrentQueue<string> requests,
        Func<CancellationToken, Task>? beforeResponse = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Enqueue(url);
            if (beforeResponse is not null)
                await beforeResponse(cancellationToken);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string packageUrl = $"{flat}{id.ToLowerInvariant()}/{Version}/{id.ToLowerInvariant()}.{Version}.nupkg";
            HttpContent content;
            if (url == source)
            {
                content = new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                    ]}
                    """);
            }
            else if (url == packageUrl)
            {
                content = payload();
            }
            else
            {
                throw new InvalidOperationException($"Unexpected exact-pin request: {url}");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            };
        }
    }

    private sealed class ReadTrackingStream(byte[] archive, Action onRead)
        : MemoryStream(archive, writable: false)
    {
        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class GatedCommitStore(NuGetOperationContext operation) : IPackageStore
    {
        private readonly InMemoryPackageStore _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Committed { get; private set; }

        public IPackageContent? TryGetCached(
            string packageName, string version, IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            _inner.TryGetCached(packageName, version, allowedSourceKeys, log);

        public async ValueTask<IPackageContent> CommitAsync(
            string packageName, string version, string sourceKey, Stream nupkg,
            CancellationToken cancellationToken = default)
        {
            operation.ThrowIfExpired();
            Assert.True(cancellationToken.CanBeCanceled);
            cancellationToken.ThrowIfCancellationRequested();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            operation.ThrowIfExpired();
            IPackageContent content = await _inner.CommitAsync(
                packageName, version, sourceKey, nupkg, cancellationToken);
            Committed = true;
            return content;
        }
    }
}
