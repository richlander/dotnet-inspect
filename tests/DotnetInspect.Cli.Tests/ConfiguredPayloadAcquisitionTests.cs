using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Markout;
using NuGetFetch;
using NuGetFetch.Plugins;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;
using DesktopPackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspect.Cli.Tests;

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
                "--path", "@readme", "--content", "--raw"]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Equal(Readme, output.Trim());
        Assert.Empty(error);
        Assert.Equal(0, transports);
    }

    [Fact]
    public async Task PackageCommand_ExactHouseDocumentExportsPreserveReadmeAndSkillSemantics()
    {
        string id = $"Pinned.Documents.{Guid.NewGuid():N}";
        const string Readme = "House README payload.";
        const string Skill = """
            ---
            name: demo
            description: Demo package skill.
            ---

            # Demo
            """;
        byte[] archive = CreatePackage(
            id,
            Readme,
            extraEntries:
            [
                ("skills/demo/SKILL.md", Encoding.UTF8.GetBytes(Skill)),
            ]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));
        string readmePath = Path.Combine(_root, "README.md");
        string skillPath = Path.Combine(_root, "SKILL.md");
        string missingPath = Path.Combine(_root, "missing-SKILL.md");

        var readme = await RunCommandAsync(
            ["package", id, "--version", Version, "--source", FirstFeed,
                "--path", "readme.md", "--content", "--out", readmePath,
                "--tips", "q"]);
        var skill = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "skills\\demo\\SKILL.md", "--content", "--out",
                skillPath, "--tips", "q"]);
        var missing = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "skills/missing/SKILL.md", "--content", "--out",
                missingPath, "--tips", "q"]);

        Assert.Equal(0, readme.Exit);
        Assert.Empty(readme.Output);
        Assert.Empty(readme.Error);
        Assert.Equal(Readme, File.ReadAllText(readmePath));
        Assert.Equal(0, skill.Exit);
        Assert.Empty(skill.Output);
        Assert.Empty(skill.Error);
        Assert.Equal(Skill, File.ReadAllText(skillPath));
        Assert.Equal(1, missing.Exit);
        Assert.Empty(missing.Output);
        Assert.Contains("found 0", missing.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(missingPath));
        Assert.Equal(
            1,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PackageCommand_ExactDocumentExportPreservesTfmFiltering()
    {
        string id = $"Pinned.FilteredDocument.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "root README",
            library: new byte[17]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));
        string outputPath = Path.Combine(_root, "tfm-README.md");

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--path", "README.md", "--tfm", "net11.0",
                "--content", "--out", outputPath, "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("found 0", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(outputPath));
        Assert.Equal(
            1,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCommand_ExactDocumentExportPreservesToolWrapperRedirect(
        bool declaresPackageType)
    {
        string wrapperId = $"Pinned.DocumentWrapper.{Guid.NewGuid():N}";
        string payloadId = $"{wrapperId}.Payload";
        string source = Path.Combine(_root, "document-wrapper-feed");
        Directory.CreateDirectory(source);
        string packageTypes = declaresPackageType
            ? """
                      <packageTypes>
                        <packageType name="DotnetTool" />
                      </packageTypes>
              """
            : "";
        File.WriteAllBytes(
            Path.Combine(
                source,
                $"{wrapperId.ToLowerInvariant()}.{Version}.nupkg"),
            CreatePackage(
                wrapperId,
                "wrapper README",
                redirectId: payloadId,
                nuspecContent: $"""
                    <package><metadata>
                      <id>{wrapperId}</id><version>{Version}</version>
                      <authors>Payload tests</authors>
                      <description>Tool wrapper</description>
                      <readme>README.md</readme>
                      {packageTypes}
                    </metadata></package>
                    """));
        File.WriteAllBytes(
            Path.Combine(
                source,
                $"{payloadId.ToLowerInvariant()}.{Version}.nupkg"),
            CreatePackage(
                payloadId,
                "redirected README"));
        string readmePath = Path.Combine(_root, "redirected-README.md");

        var result = await RunCommandAsync(
            ["package", $"{wrapperId}@{Version}", "--source", source,
                "--path", "README.md", "--content", "--out", readmePath,
                "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.Empty(result.Output);
        Assert.Empty(result.Error);
        Assert.Equal("redirected README", File.ReadAllText(readmePath));
    }

    [Fact]
    public async Task PackageCommand_LayoutDoesNotValidatePackageInfoTarget()
    {
        string id = $"Pinned.Layout.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "layout package",
            library: new byte[17],
            libraryName: $"{id}.dll");
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "--layout", "--tfm", "bad tfm", "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "TFM 'bad tfm' not found. Use --tfms to list available frameworks.",
            error);
        Assert.DoesNotContain("ArgumentException", error);
        Assert.DoesNotContain("bounded ASCII target moniker", error);
        Assert.Equal(
            1,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PackageCommand_PackageInfoRejectsInvalidTargetBeforeAcquisition()
    {
        string id = $"Pinned.InvalidTarget.{Guid.NewGuid():N}";
        int transports = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ =>
        {
            transports++;
            throw new InvalidOperationException(
                "Invalid Package Info target reached acquisition.");
        });

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tfm", "bad tfm", "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Invalid --tfm value 'bad tfm': expected a bounded ASCII target moniker.",
            error);
        Assert.DoesNotContain("ArgumentException", error);
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

    [Fact]
    public async Task AcquirePinned_RequiredProducerConsultsOnlyItsConfiguredAuthority()
    {
        const string Id = "Pinned.RequiredProducer";
        string first = Path.Combine(_root, "required-first");
        string second = Path.Combine(_root, "required-second");
        WriteLocalPackage(first, Id, "first payload");
        WriteLocalPackage(second, Id, "second payload");

        await using var composition = LocalComposition();
        ConfiguredPackagePayloadResult selected =
            await composition.AcquirePinnedAsync(
                Id,
                Version,
                (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { Sources = [second] },
                cancellationToken: TestContext.Current.CancellationToken);
        AcquiredPackageSourcePayload selectedPayload =
            AssertPayload(selected, Id);
        PackageProducerIdentity producer =
            Assert.IsType<PackageProducerIdentity>(
                selectedPayload.Producer);
        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(selectedPayload);
        Assert.Equal(
            producer.PortableKey,
            binding.Coordinate.Producer);
        Assert.Equal(
            selectedPayload.ProducerKey,
            binding.Root.ProducerKey);

        ConfiguredPackagePayloadResult pinned =
            await composition.AcquirePinnedAsync(
                Id,
                Version,
                (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { Sources = [first, second] },
                cancellationToken: TestContext.Current.CancellationToken,
                requiredProducerKey: producer.PortableKey);

        Assert.Equal(
            second,
            pinned.Authority!.LocalIdentity!.CanonicalPath);
        AcquiredPackageSourcePayload pinnedPayload =
            AssertPayload(pinned, Id);
        Assert.Equal(
            "second payload",
            ReadReadme(pinnedPayload.Content));
        PackageRootBinding rebound =
            Assert.IsType<PackageRootRebindingOutcome.Bound>(
                PackageRootAcquisition.BindReacquired(
                    binding.CreateReacquisitionRequest(),
                    pinnedPayload))
                .Binding;
        Assert.Equal(
            binding.CreateReacquisitionRequest(),
            rebound.CreateReacquisitionRequest());
        Assert.True(
            RealizedMemberCoordinate.Package.TryCreate(
                binding.Coordinate.PackageId,
                binding.Coordinate.Version,
                NuGetCache.GetSourceKey(second),
                binding.Coordinate.Framework,
                binding.Coordinate.RuntimeIdentifier,
                out RealizedMemberCoordinate.Package? legacyCoordinate,
                out string? problem),
            problem);
        var legacyRequest = new PackageRootReacquisitionRequest(
            PackageArtifactRootRequest.Create(
                legacyCoordinate,
                binding.CompileTargetFramework,
                binding.ImplementationSelectionTargetFramework,
                binding.Root.RequestedRuntimeIdentifier,
                binding.HasSelectedImplementationUniverse,
                binding.UsesCompatibleImplementationSelection,
                binding.AllowsCompatibleTargetSelection));
        PackageRootBinding legacyRebound =
            Assert.IsType<PackageRootRebindingOutcome.Bound>(
                PackageRootAcquisition.BindReacquired(
                    legacyRequest,
                    pinnedPayload))
                .Binding;
        Assert.Equal(
            legacyRequest,
            legacyRebound.CreateReacquisitionRequest());

        ConfiguredPackagePayloadResult unauthorized =
            await composition.AcquirePinnedAsync(
                Id,
                Version,
                (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { Sources = [first, second] },
                cancellationToken: TestContext.Current.CancellationToken,
                requiredProducerKey: "nfs-local-1.bm90LWF1dGhvcml6ZWQ");

        Assert.Null(unauthorized.Payload);
        Assert.Null(unauthorized.Authority);
        Assert.Contains(
            unauthorized.Failures,
            failure => failure.IsRequiredProducerUnavailable);

        ConfiguredPackagePayloadResult denied =
            await composition.AcquirePinnedAsync(
                Id,
                Version,
                (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { ResolvedSources = [] },
                cancellationToken: TestContext.Current.CancellationToken,
                requiredProducerKey: producer.PortableKey);

        Assert.Null(denied.Payload);
        Assert.Null(denied.Authority);
        Assert.Contains(
            denied.Failures,
            failure => failure.IsRequiredProducerUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCommand_GroupedIntegrationsUseRetainedAuthorizedPayload(bool localSource)
    {
        string id = $"Pinned.Integrations.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id, "Retained package input",
            library: File.ReadAllBytes(typeof(Npgsql.NpgsqlConnection).Assembly.Location));
        var requests = new ConcurrentQueue<string>();
        int transports = 0;
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(source =>
        {
            transports++;
            return new PayloadFeedHandler(source, id,
                () => new ByteArrayContent(archive), requests);
        });
        string source = FirstFeed;
        if (localSource)
        {
            source = Path.Combine(_root, "integration-feed");
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, $"{id}.{Version}.nupkg"), archive);
        }

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", source,
                "--library", "--tfm", "net11.0",
                "-S", "Integration Opportunities", "--markdown", "--verbose", "--tips", "q"]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Contains("Using artifact-backed selected-entry package Integrations.", error);
        Assert.Contains("Aspire", output);
        Assert.Contains("Health Checks", output);
        Assert.Contains("Npgsql.NpgsqlConnection", output);
        if (localSource)
        {
            Assert.Equal(0, transports);
            Assert.Empty(requests);
        }
        else
        {
            Assert.Single(requests, request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal));
        }
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
    public async Task AuthorizationObservation_RetainsRegisteredAuthorityAndPartialFailure()
    {
        const string Id = "Pinned.AuthorizationObservation";
        const string Unsupported = "ftp://legacy.example/packages";
        string healthy = Path.Combine(_root, "authorization-healthy");
        Directory.CreateDirectory(healthy);
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [healthy, Unsupported],
        };
        await using var composition = LocalComposition();

        PackageSourceAuthorization observation =
            composition.AuthorizeSourcesFor(Id, sourceOptions);

        ConfiguredPackageAuthority authority =
            Assert.Single(observation.Authorities);
        Assert.Equal(healthy, authority.Source.Url);
        PackageAuthorityFailure failure =
            Assert.Single(observation.Failures);
        Assert.Equal(
            PackageAuthorityFailureKind.Configuration,
            failure.Kind);
        Assert.Null(observation.DenialReason);

        PackageAcquisitionCandidateResult compatibility =
            composition.ResolvePinnedCandidate(
                PackageSourceCoordinate.Create(Id, Version),
                sourceOptions,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.NotNull(compatibility.Candidate);
        Assert.Same(
            authority,
            Assert.Single(
                compatibility.Candidate.Authorities).Authority);
        Assert.Equal(
            failure.Message,
            Assert.Single(compatibility.Failures).Message);
        PackageAcquisitionCandidateResult houseCompatibility =
            await composition.ResolvePinnedCandidateAsync(
                PackageSourceCoordinate.Create(Id, Version),
                sourceOptions,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.NotNull(houseCompatibility.Candidate);
        Assert.Same(
            authority,
            Assert.Single(
                houseCompatibility.Candidate.Authorities).Authority);
        Assert.Equal(
            failure.Message,
            Assert.Single(houseCompatibility.Failures).Message);
        PackageSourceAuthorization repeated =
            composition.AuthorizeSourcesFor(Id, sourceOptions);
        Assert.Same(
            authority,
            Assert.Single(repeated.Authorities));

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageAcquisitionCandidateResult reusable =
            issuer.ResolvePinnedCandidate(
                observation,
                PackageSourceCoordinate.Create(Id, Version));
        Assert.NotNull(reusable.Candidate);
        Assert.Same(
            authority,
            Assert.Single(
                reusable.Candidate.Authorities).Authority);
        Assert.Same(failure, Assert.Single(reusable.Failures));

        PackageSourceAuthorization replacement =
            PackageSourceAuthorization.Authorize(observation.Sources);
        ConfiguredPackageAuthority replacementAuthority =
            Assert.Single(replacement.Authorities);
        Assert.NotSame(authority, replacementAuthority);
        Assert.False(
            observation.TryGetAuthority(
                replacementAuthority.Association,
                out _));
    }

    [Fact]
    public async Task CandidateManifest_UsesHouseOwnedDesktopOperation()
    {
        const string Id = "Pinned.HouseManifest";
        string source = Path.Combine(_root, "house-manifest");
        WriteLocalPackage(source, Id, "manifest payload");
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [source],
        };
        await using var composition = LocalComposition();
        PackageAcquisitionCandidateResult authorization =
            await composition.ResolvePinnedCandidateAsync(
                PackageSourceCoordinate.Create(Id, Version),
                sourceOptions,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        ConfiguredPackageManifestResult result =
            await composition.AcquireCandidateManifestAsync(
                authorization.Candidate!,
                TestContext.Current.CancellationToken);

        Assert.NotNull(result.Manifest);
        Assert.Equal(
            PackageSourceCoordinate.Create(Id, Version),
            result.Manifest.Coordinate);
        Assert.Same(
            Assert.Single(
                authorization.Candidate!.Authorities).Authority,
            result.Authority);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task AcquirePinned_NotFoundRetainsAttemptedAuthority()
    {
        const string Id = "Pinned.NotFound";
        var requests = new ConcurrentQueue<string>();
        await using var composition = CreateComposition(
            (source, _) => new NotFoundPayloadFeedHandler(
                source.Url,
                Id,
                requests));

        ConfiguredPackagePayloadResult result =
            await composition.AcquirePinnedAsync(
                Id,
                Version,
                (_, _) => new InMemoryPackageStore(),
                new NuGetSourceOptions { Sources = [FirstFeed] },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Authority);
        Assert.Null(result.Payload);
        Assert.Empty(result.Failures);
        ConfiguredPackageAuthority authority =
            Assert.Single(result.NotFoundAuthorities);
        Assert.Equal(FirstFeed, authority.Source.Url);
        Assert.Contains(
            requests,
            request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfiguredRootPayloadProvider_NotFoundNamesAuthority()
    {
        const string Id = "Pinned.ProviderNotFound";
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new NotFoundPayloadFeedHandler(
                source,
                Id,
                requests));
        await using var provider = new ConfiguredPackageRootPayloadProvider(
            TimeSpan.FromSeconds(5),
            new NuGetSourceOptions { Sources = [FirstFeed] });

        PackageRootPayloadResult.Unavailable unavailable =
            Assert.IsType<PackageRootPayloadResult.Unavailable>(
                await provider.GetPayloadAsync(
                    PackageSourceCoordinate.Create(Id, Version),
                    requiredProducerKey: null,
                    PackagePayloadLimits.Default,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageRootAcquisitionFailureKind.PackageUnavailable,
            unavailable.FailureKind);
        Assert.Equal(
            PackageSourceFailureKind.NotFound,
            unavailable.SourceFailureKind);
        Assert.Contains(
            FirstFeed,
            unavailable.Producer.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            FirstFeed,
            unavailable.Message,
            StringComparison.Ordinal);
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
            Assert.Equal(id, result.PackageName);
            Assert.Equal(ConfiguredPackageAuthorityKind.Http, result.Authority!.Kind);
            Assert.Null(result.CacheScopeKey);
            Assert.False(result.FromCache);
            Assert.NotNull(result.TempDir);
            Assert.True(Directory.Exists(result.TempDir));
            Assert.True(File.Exists(result.NupkgPath));
            AcquiredPackageSourcePayload payload =
                Assert.IsType<AcquiredPackageSourcePayload>(result.AcquiredPayload);
            Assert.Equal(result.ExtractPath, payload.Content.RootPath);
            Assert.Equal(result.ProducerKey, payload.ProducerKey);
            Assert.Equal(id, payload.Coordinate.PackageId, ignoreCase: true);
            Assert.Equal("HTTP package content", ReadReadme(payload.Content));
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
    public async Task ExtractPinnedPackage_CompileRealizationFeedsPackageInfoEnvelope()
    {
        const string Marker = "HOSTILE";
        const string UnsafeFolder = Marker + "\u202EMARKER";
        string id = $"Pinned.Measurements.{Guid.NewGuid():N}";
        byte[] library = new byte[17];
        byte[] archive = CreatePackage(
            id,
            "measurement package",
            library: library,
            libraryName: $"{id}.dll",
            extraEntries:
            [
                ($"{UnsafeFolder}/net11.0/data.bin", new byte[3]),
                ("lib/net8.0/Legacy.dll", new byte[5]),
            ]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));
        using var client = new HttpClient(
            new RejectNetworkHandler(new HttpClientHandler()));
        PackageExtractionOutcome outcome =
            await DesktopPackageExtractor.ExtractPinnedPackageAsync(
                client,
                id,
                Version,
                sourceOptions:
                    new NuGetSourceOptions { Sources = [FirstFeed] },
                compileTargetContext:
                    PackageHouseTargetContext.OwnerDefault());

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        PackageExtractionResult result = outcome.Result!;
        try
        {
            PackageHouseSettlement.Acquired settlement =
                Assert.IsType<PackageHouseSettlement.Acquired>(
                    result.HouseSettlement);
            Assert.Same(result.AcquiredPayload, settlement.Payload);
            InspectionEnvelope<PackageInfoMeasurements> envelope =
                PackageInfoMeasurementInspection.Project(settlement);
            PackageInfoMeasurements measurements = envelope.Content;

            Assert.Equal(
                PackageInfoMeasurementStatus.Measured,
                measurements.Status);
            Assert.Equal(archive.LongLength, measurements.CompressedPackageBytes);
            Assert.Equal(
                "net11.0",
                measurements.SelectedTargetFramework!.ToString());
            Assert.Equal(
                ["net11.0", "net8.0"],
                measurements.AvailableTargetFrameworks!
                    .Select(static framework => framework.ToString()));
            Assert.Equal(
                [@"HOSTILE\u202EMARKER", "lib"],
                measurements.SelectedTargetFrameworkFolders!
                    .Select(static folder => folder.ToString()));
            Assert.Equal(library.LongLength, measurements.SelectedLibraryPayloadBytes);
            Assert.Equal(1, measurements.SelectedLibraryCount);
            Assert.Same(
                settlement.Payload.Content.GenerationIdentity,
                measurements.Generation);
            Assert.Same(
                settlement.Result.Evidence.Realization,
                measurements.Evidence!.Realization);
            Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
            Assert.Empty(envelope.Diagnostics);
            Assert.Equal(
                1,
                requests.Count(request =>
                    request.EndsWith(".nupkg", StringComparison.Ordinal)));

            var inspection = new InspectionResult
            {
                PackageName = id,
                Version = Version,
                PackageInfoMeasurementInspection = envelope,
            };
            string output = MarkoutSerializer.Serialize(
                new InspectionResultView(inspection),
                InspectionContext.Default);
            Assert.Contains("| Package Size (compressed) |", output);
            Assert.Contains("| Selected TFM | net11.0 |", output);
            Assert.Contains(
                @"| Selected-TFM Folders | HOSTILE\u202EMARKER, lib |",
                output);
            HostileOutputAssert.MarkersRendered(
                output,
                "Package Info selected-TFM folders",
                Marker);
            HostileOutputAssert.NoRenderingHazard(
                output,
                "Package Info selected-TFM folders");
            Assert.Contains("| TFMs | net11.0, net8.0 |", output);
            Assert.Contains("| Selected-TFM Size | 17 B |", output);
            Assert.Contains("| Selected-TFM Library Count | 1 |", output);
            Assert.DoesNotContain("| Highest TFM |", output);
            Assert.DoesNotContain("| Size |", output);
        }
        finally
        {
            DesktopPackageExtractor.Cleanup(result.TempDir);
        }
    }

    [Fact]
    public async Task PackageCommand_PackageInfoAndDetailRenderEcosystemDependencies()
    {
        string id = $"Pinned.Ecosystems.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = CreatePackage(
            id,
            "ecosystem package",
            library: library,
            libraryName: $"{id}.dll",
            dependencies:
            [
                ("Microsoft.Extensions.AI.Abstractions", "10.0.0"),
                ("ThirdParty.Unrecognized", "1.0.0"),
            ]);
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));

        var info = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(info.Exit == 0, $"Exit {info.Exit}: {info.Error}");
        Assert.Contains(
            "| Ecosystem Dependencies | .NET Runtime, Microsoft.Extensions, AI |",
            info.Output);
        Assert.DoesNotContain(
            "| Ecosystem Dependency Status |",
            info.Output);

        var details = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "--columns", "Ecosystem,Kind,Dependency,Declared By",
                "--tips", "q"]);

        Assert.True(
            details.Exit == 0,
            $"Exit {details.Exit}: {details.Error}");
        Assert.Contains(
            "| Microsoft.Extensions | Package declaration | Microsoft.Extensions.AI.Abstractions 10.0.0 |",
            details.Output);
        Assert.Contains(
            "| AI | Package declaration | Microsoft.Extensions.AI.Abstractions 10.0.0 |",
            details.Output);
        Assert.DoesNotContain("ThirdParty.Unrecognized", details.Output);

        var selected = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "-n", "1",
                "--tips", "q"]);

        Assert.True(
            selected.Exit == 0,
            $"Exit {selected.Exit}: {selected.Error}");
        Assert.Equal(
            3,
            selected.Output.Split('\n').Count(
                static line => line.StartsWith("| ", StringComparison.Ordinal)));

        var windowed = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "--rows", "2..3",
                "--tips", "q"]);

        Assert.True(
            windowed.Exit == 0,
            $"Exit {windowed.Exit}: {windowed.Error}");
        Assert.Equal(
            4,
            windowed.Output.Split('\n').Count(
                static line => line.StartsWith("| ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PackageCommand_JsonRetainsRecognitionAndSelectedPairRows()
    {
        string id = $"Pinned.EcosystemJson.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "ecosystem JSON package",
            dependencies:
            [
                ("Microsoft.Extensions.AI.Abstractions", "10.0.0"),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "--rows", "2..2",
                "--json", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement recognition = document.RootElement.GetProperty(
            "ecosystem_dependencies");
        Assert.Equal(
            "complete",
            recognition.GetProperty("status").GetString());
        Assert.Equal(
            0,
            recognition.GetProperty("issue_count").GetInt32());
        Assert.Equal(
            ["Microsoft.Extensions", "AI"],
            recognition.GetProperty("ecosystems")
                .EnumerateArray()
                .Select(static value => value.GetString()!)
                .ToArray());
        JsonElement dependency = Assert.Single(
            recognition.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(
            "AI",
            dependency.GetProperty("ecosystem").GetString());
        Assert.Equal(
            "Package declaration",
            dependency.GetProperty("kind").GetString());
        Assert.Equal(
            "Microsoft.Extensions.AI.Abstractions 10.0.0",
            dependency.GetProperty("dependency").GetString());
        Assert.Equal(
            $"{id.ToLowerInvariant()}@{Version}",
            dependency.GetProperty("declared_by").GetString());
        Assert.Equal(
            "10.0.0",
            dependency.GetProperty("version_or_range").GetString());
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PackageCommand_LocalPackageDisclosesUnavailableRecognition()
    {
        string id = $"Local.Ecosystems.{Guid.NewGuid():N}";
        string packagePath = Path.Combine(
            _root,
            $"{id}.{Version}.nupkg");
        await File.WriteAllBytesAsync(
            packagePath,
            CreatePackage(
                id,
                "local ecosystem package",
                dependencies:
                [
                    ("Microsoft.Extensions.Logging.Abstractions", "10.0.0"),
                ]),
            TestContext.Current.CancellationToken);

        var result = await RunCommandAsync(
            ["package", packagePath,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.DoesNotContain(
            "| Ecosystem Dependencies |",
            result.Output);
        Assert.Contains(
            "| Ecosystem Dependency Status | Unavailable (3 issues) |",
            result.Output);

        var details = await RunCommandAsync(
            ["package", packagePath,
                "-S", PackageSections.EcosystemDependencies,
                "--tips", "q"]);

        Assert.True(
            details.Exit == 0,
            $"Exit {details.Exit}: {details.Error}");
        Assert.Contains(
            "ecosystem-dependency-recognition.package-manifest-unavailable",
            details.Error);
        Assert.Contains(
            "ecosystem-dependency-recognition.package-dependencies-not-attempted",
            details.Error);
        Assert.Contains(
            "ecosystem-dependency-recognition.package-compile-selection-unavailable",
            details.Error);

        var multiSectionDetails = await RunCommandAsync(
            ["package", packagePath,
                "-S",
                $"{PackageSections.EcosystemDependencies},{PackageSections.Dependencies}",
                "--tips", "q"]);

        Assert.True(
            multiSectionDetails.Exit == 0,
            $"Exit {multiSectionDetails.Exit}: {multiSectionDetails.Error}");
        Assert.Contains(
            "ecosystem-dependency-recognition.package-manifest-unavailable",
            multiSectionDetails.Error);
    }

    [Fact]
    public async Task PackageCommand_OfflineCachedPackageDisclosesUnavailableRecognition()
    {
        string id = $"Offline.Ecosystems.{Guid.NewGuid():N}";
        string nupkgPath = Path.Combine(
            _root,
            $"{id}.{Version}.nupkg");
        string stagedPath = Path.Combine(
            _root,
            $"offline-staged-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(
            nupkgPath,
            CreatePackage(
                id,
                "offline ecosystem package",
                dependencies:
                [
                    ("Microsoft.Extensions.Logging.Abstractions", "10.0.0"),
                ]),
            TestContext.Current.CancellationToken);
        ZipFile.ExtractToDirectory(nupkgPath, stagedPath);
        NuGetCache.CommitPackage(
            stagedPath,
            nupkgPath,
            id,
            Version,
            NuGetCache.GetSourceKey(FirstFeed));

        bool wasOffline = CoreHttpClientFactory.IsOffline;
        try
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = true });
            CoreHttpClientFactory.ResetSharedForTesting();

            var result = await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--tips", "q"]);

            Assert.True(
                result.Exit == 0,
                $"Exit {result.Exit}: {result.Error}");
            Assert.DoesNotContain(
                "| Ecosystem Dependencies |",
                result.Output);
            Assert.Contains(
                "| Ecosystem Dependency Status | Unavailable (3 issues) |",
                result.Output);
        }
        finally
        {
            CoreHttpClientFactory.Initialize(
                new HttpClientFactoryOptions { Offline = wasOffline });
            CoreHttpClientFactory.ResetSharedForTesting();
        }
    }

    [Fact]
    public async Task PackageCommand_IncompleteRecognitionOmitsRollupAndDisclosesStatus()
    {
        string id = $"Pinned.EcosystemFailure.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "incomplete ecosystem package",
            library: new byte[17],
            libraryName: $"{id}.dll",
            dependencies:
            [
                ("Microsoft.Extensions.AI.Abstractions", "10.0.0"),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.DoesNotContain(
            "| Ecosystem Dependencies |",
            result.Output);
        Assert.Contains(
            "| Ecosystem Dependency Status | Incomplete (1 issue) |",
            result.Output);
    }

    [Fact]
    public async Task PackageCommand_EmptyIncompleteDetailDisclosesRecognitionIssue()
    {
        string id = $"Pinned.EmptyIncompleteEcosystem.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "empty incomplete ecosystem package",
            library: new byte[17],
            libraryName: $"{id}.dll",
            dependencies:
            [
                ("ThirdParty.Unrecognized", "1.0.0"),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.Contains(
            "Warning: ecosystem-dependency-recognition.package-",
            result.Error);
        Assert.DoesNotContain(
            "| Ecosystem |",
            result.Output);

        var multiSectionResult = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S",
                $"{PackageSections.EcosystemDependencies},{PackageSections.Dependencies}",
                "--tips", "q"]);

        Assert.True(
            multiSectionResult.Exit == 0,
            $"Exit {multiSectionResult.Exit}: {multiSectionResult.Error}");
        Assert.Contains(
            "Warning: ecosystem-dependency-recognition.package-",
            multiSectionResult.Error);
    }

    [Fact]
    public async Task PackageCommand_MultiSectionJsonRejectsEcosystemRowWindow()
    {
        string id = $"Pinned.MultiSectionEcosystemJson.{Guid.NewGuid():N}";

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S",
                $"{PackageSections.PackageInfo},{PackageSections.EcosystemDependencies}",
                "--rows", "2..2",
                "--json", "--tips", "q"]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            result.Error);
    }

    [Fact]
    public async Task PackageCommand_MalformedSiblingRetainsValidEcosystemEvidence()
    {
        string id = $"Pinned.PartialEcosystem.{Guid.NewGuid():N}";
        byte[] validLibrary = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = CreatePackage(
            id,
            "partial ecosystem package",
            library: validLibrary,
            libraryName: "A.Valid.dll",
            extraEntries:
            [
                ("lib/net11.0/Z.Invalid.dll", new byte[17]),
            ],
            dependencies:
            [
                ("ThirdParty.Unrecognized", "1.0.0"),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.EcosystemDependencies,
                "--json", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement recognition = document.RootElement.GetProperty(
            "ecosystem_dependencies");
        Assert.Equal(
            "incomplete",
            recognition.GetProperty("status").GetString());
        Assert.Equal(
            1,
            recognition.GetProperty("issue_count").GetInt32());
        JsonElement[] dependencies = recognition.GetProperty("dependencies")
            .EnumerateArray()
            .ToArray();
        Assert.NotEmpty(dependencies);
        Assert.Contains(
            dependencies,
            static dependency =>
                dependency.GetProperty("kind").GetString()
                    == "Assembly reference"
                && dependency.GetProperty("coverage").GetString()
                    == "Incomplete");
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PackageCommand_DuplicateCompileIdentityDisclosesIncompleteRecognition()
    {
        string id = $"Pinned.DuplicateIdentity.{Guid.NewGuid():N}";
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] archive = CreatePackage(
            id,
            "duplicate identity ecosystem package",
            library: library,
            libraryName: "First.dll",
            extraEntries:
            [
                ("lib/net11.0/nested/Second.dll", library),
            ],
            dependencies:
            [
                ("Microsoft.Extensions.AI.Abstractions", "10.0.0"),
            ]);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.DoesNotContain(
            "| Ecosystem Dependencies |",
            result.Output);
        Assert.Contains(
            "| Ecosystem Dependency Status | Incomplete (1 issue) |",
            result.Output);
    }

    [Fact]
    public async Task PackageCommand_MalformedManifestDisclosesIncompleteRecognition()
    {
        string id = $"Pinned.MalformedManifest.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "malformed manifest ecosystem package",
            nuspecContent: $"""
                <package><metadata>
                  <id>{id}</id><version>{Version}</version>
                  <description>unterminated
                """);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.DoesNotContain(
            "| Ecosystem Dependencies |",
            result.Output);
        Assert.Contains(
            "| Ecosystem Dependency Status | Incomplete (2 issues) |",
            result.Output);

        var files = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", PackageSections.Files, "--tips", "q"]);

        Assert.Equal(1, files.Exit);
        Assert.Contains(
            "Package manifest is not well-formed XML",
            files.Error);
    }

    [Fact]
    public async Task PackageCommand_CompleteEmptyRecognitionOmitsEcosystemFields()
    {
        string id = $"Pinned.EmptyEcosystems.{Guid.NewGuid():N}";
        byte[] archive = CreatePackage(
            id,
            "empty ecosystem package");
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var result = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(result.Exit == 0, $"Exit {result.Exit}: {result.Error}");
        Assert.DoesNotContain(
            "| Ecosystem Dependencies |",
            result.Output);
        Assert.DoesNotContain(
            "| Ecosystem Dependency Status |",
            result.Output);
    }

    [Fact]
    public async Task PackageCommand_DeclaredToolUsesAggregateToolMeasurementsColdAndWarm()
    {
        string id = $"Pinned.ToolMeasurements.{Guid.NewGuid():N}";
        byte[] archive = CreateToolPackage(
            id,
            packageType: "DotnetToolRidPackage",
            ("tools/net10.0/any/Alpha.dll", new byte[11]),
            ("tools/net10.0/any/Beta.dll", new byte[17]),
            ("tools/net10.0/any/fr/Alpha.resources.dll", new byte[19]),
            (
                "tools/net10.0/any/runtimes/linux-x64/native/Native.dll",
                new byte[23]),
            ("tools/net8.0/any/Alpha.dll", new byte[29]));
        var requests = new ConcurrentQueue<string>();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                requests));

        for (int run = 0; run < 2; run++)
        {
            var (exit, output, error) = await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--tfm", "all", "--tips", "q"]);

            Assert.True(exit == 0, $"Exit {exit}: {error}");
            Assert.Contains("| Type | Tool |", output);
            Assert.Contains("| Selected TFM | net10.0 |", output);
            Assert.Contains("| Selected-TFM Folders | tools |", output);
            Assert.Contains("| TFMs | net10.0, net8.0 |", output);
            Assert.Contains("| Selected-TFM Size | 28 B |", output);
            Assert.Contains("| Selected-TFM Library Count | 2 |", output);
            Assert.DoesNotContain("| Selected-TFM Status |", output);
            Assert.Empty(error);
        }

        Assert.Equal(
            2,
            requests.Count(request =>
                request.EndsWith(".nupkg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PackageCommand_DeclaredToolJsonRetainsMeasuredAndNoApplicableOutcomes()
    {
        string id = $"Pinned.ToolMeasurementJson.{Guid.NewGuid():N}";
        byte[] archive = CreateToolPackage(
            id,
            packageType: "DotnetToolRidPackage",
            ("tools/net10.0/any/Alpha.dll", new byte[11]),
            ("tools/net10.0/any/Beta.dll", new byte[17]),
            ("tools/net8.0/any/Alpha.dll", new byte[29]));
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var (measuredExit, measuredOutput, measuredError) =
            await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--json", "--tips", "q"]);

        Assert.True(
            measuredExit == 0,
            $"Exit {measuredExit}: {measuredError}");
        using (JsonDocument document = JsonDocument.Parse(measuredOutput))
        {
            JsonElement measurements = document.RootElement.GetProperty(
                "package_info_measurements");
            Assert.Equal(
                "Measured",
                measurements.GetProperty("status").GetString());
            Assert.Equal(
                archive.LongLength,
                measurements.GetProperty(
                    "compressed_package_bytes").GetInt64());
            Assert.Equal(
                "net10.0",
                measurements.GetProperty(
                    "selected_target_framework").GetString());
            Assert.Equal(
                ["net10.0", "net8.0"],
                measurements.GetProperty(
                        "available_target_frameworks")
                    .EnumerateArray()
                    .Select(static value => value.GetString()!)
                    .ToArray());
            Assert.Equal(
                ["tools"],
                measurements.GetProperty(
                        "selected_target_framework_folders")
                    .EnumerateArray()
                    .Select(static value => value.GetString()!)
                    .ToArray());
            Assert.Equal(
                28,
                measurements.GetProperty(
                    "selected_library_payload_bytes").GetInt64());
            Assert.Equal(
                2,
                measurements.GetProperty(
                    "selected_library_count").GetInt32());
            Assert.False(measurements.TryGetProperty("detail", out _));
            Assert.False(
                measurements.TryGetProperty(
                    "unavailable_reason",
                    out _));
        }
        Assert.Empty(measuredError);

        var (noApplicableExit, noApplicableOutput, noApplicableError) =
            await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--tfm", "net6.0",
                    "--json", "--tips", "q"]);

        Assert.True(
            noApplicableExit == 0,
            $"Exit {noApplicableExit}: {noApplicableError}");
        using (JsonDocument document = JsonDocument.Parse(
            noApplicableOutput))
        {
            JsonElement measurements = document.RootElement.GetProperty(
                "package_info_measurements");
            Assert.Equal(
                "NoApplicableSlice",
                measurements.GetProperty("status").GetString());
            Assert.Equal(
                ["net10.0", "net8.0"],
                measurements.GetProperty(
                        "available_target_frameworks")
                    .EnumerateArray()
                    .Select(static value => value.GetString()!)
                    .ToArray());
            Assert.Contains(
                "no applicable tool slice",
                measurements.GetProperty("detail").GetString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(
                measurements.TryGetProperty(
                    "selected_target_framework",
                    out _));
            Assert.False(
                measurements.TryGetProperty(
                    "selected_library_payload_bytes",
                    out _));
            Assert.False(
                measurements.TryGetProperty(
                    "selected_library_count",
                    out _));
            Assert.False(
                measurements.TryGetProperty(
                    "unavailable_reason",
                    out _));
        }
        Assert.Empty(noApplicableError);
    }

    [Fact]
    public async Task PackageCommand_DeclaredToolSelectedFrameworkIsContainedAcrossOutputs()
    {
        const string UnsafeFramework = "net10.0\u202EHOSTILE";
        string id = $"Pinned.ToolFrameworkContainment.{Guid.NewGuid():N}";
        byte[] archive = CreateToolPackage(
            id,
            packageType: "DotnetToolRidPackage",
            ($"tools/{UnsafeFramework}/any/Tool.dll", new byte[11]),
            ("tools/net8.0/any/Legacy.dll", new byte[7]));
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var (markoutExit, markoutOutput, markoutError) =
            await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--tips", "q"]);

        Assert.True(
            markoutExit == 0,
            $"Exit {markoutExit}: {markoutError}");
        Assert.Contains(
            @"| Selected TFM | net10.0\u202EHOSTILE |",
            markoutOutput);
        Assert.DoesNotContain('\u202E', markoutOutput);
        Assert.Empty(markoutError);

        var (jsonExit, jsonOutput, jsonError) =
            await RunCommandAsync(
                ["package", $"{id}@{Version}", "--source", FirstFeed,
                    "-S", "Package Info", "--json", "--tips", "q"]);

        Assert.True(jsonExit == 0, $"Exit {jsonExit}: {jsonError}");
        using JsonDocument document = JsonDocument.Parse(jsonOutput);
        Assert.Equal(
            @"net10.0\u202EHOSTILE",
            document.RootElement
                .GetProperty("package_info_measurements")
                .GetProperty("selected_target_framework")
                .GetString());
        Assert.DoesNotContain('\u202E', jsonOutput);
        Assert.Empty(jsonError);
    }

    [Fact]
    public async Task PackageCommand_UndeclaredToolShapeDoesNotAuthorizeToolMeasurements()
    {
        string id = $"Pinned.ToolShape.{Guid.NewGuid():N}";
        byte[] archive = CreateToolPackage(
            id,
            packageType: null,
            ("tools/net10.0/any/Shape.dll", new byte[11]));
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            source => new PayloadFeedHandler(
                source,
                id,
                () => new ByteArrayContent(archive),
                new ConcurrentQueue<string>()));

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{id}@{Version}", "--source", FirstFeed,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Contains("| Type | Tool |", output);
        Assert.DoesNotContain("| Selected TFM |", output);
        Assert.DoesNotContain("| Selected-TFM Size |", output);
        Assert.DoesNotContain("| Selected-TFM Library Count |", output);
        Assert.Contains("| Selected-TFM Status |", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task PackageCommand_WrapperDeclarationDoesNotAuthorizePayloadMeasurements()
    {
        string wrapperId = $"Pinned.ToolWrapper.{Guid.NewGuid():N}";
        string payloadId = $"{wrapperId}.Payload";
        string source = Path.Combine(_root, "tool-wrapper-feed");
        Directory.CreateDirectory(source);
        byte[] settings = Encoding.UTF8.GetBytes($"""
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="wrapped" EntryPoint="Payload.dll" Runner="dotnet" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="{payloadId}" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);
        File.WriteAllBytes(
            Path.Combine(
                source,
                $"{wrapperId.ToLowerInvariant()}.{Version}.nupkg"),
            CreateToolPackage(
                wrapperId,
                packageType: "DotnetTool",
                ("tools/net10.0/any/DotnetToolSettings.xml", settings)));
        File.WriteAllBytes(
            Path.Combine(
                source,
                $"{payloadId.ToLowerInvariant()}.{Version}.nupkg"),
            CreateToolPackage(
                payloadId,
                packageType: null,
                ("tools/net10.0/any/Payload.dll", new byte[31])));

        var (exit, output, error) = await RunCommandAsync(
            ["package", $"{wrapperId}@{Version}", "--source", source,
                "-S", "Package Info", "--tips", "q"]);

        Assert.True(exit == 0, $"Exit {exit}: {error}");
        Assert.Contains("| Type | Tool v2 |", output);
        Assert.DoesNotContain("| Selected TFM |", output);
        Assert.DoesNotContain("| Selected-TFM Size |", output);
        Assert.DoesNotContain("| Selected-TFM Library Count |", output);
        Assert.Contains("| Selected-TFM Status |", output);
        Assert.Empty(error);
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
                    "--path", "@readme", "--content", "--raw", "--no-nuget-cache"]);
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
                        "--source", source, "--path", "@readme", "--content", "--raw",
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
        string? redirectId = null, string version = Version,
        byte[]? library = null,
        string libraryName = "Npgsql.dll",
        byte[]? documentation = null,
        string libraryDirectory = "lib/net11.0",
        byte[]? pdb = null)
    {
        string directory = hierarchical ? Path.Combine(root, id.ToLowerInvariant(), version) : root;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, $"{id.ToLowerInvariant()}.{version}.nupkg"),
            CreatePackage(
                id,
                readme,
                redirectId,
                version,
                library,
                libraryName,
                documentation,
                libraryDirectory,
                pdb: pdb));
    }

    private static HttpContent PackageContent(string id, string readme) =>
        new ByteArrayContent(CreatePackage(id, readme));

    private static byte[] CreatePackage(
        string id, string readme, string? redirectId = null,
        string version = Version, byte[]? library = null,
        string libraryName = "Npgsql.dll",
        byte[]? documentation = null,
        string libraryDirectory = "lib/net11.0",
        IReadOnlyList<(string Path, byte[] Content)>? extraEntries = null,
        IReadOnlyList<(string Id, string Version)>? dependencies = null,
        string? nuspecContent = null,
        byte[]? pdb = null)
    {
        string dependenciesXml = dependencies is { Count: > 0 }
            ? "<dependencies><group targetFramework=\"net11.0\">"
                + string.Concat(dependencies.Select(dependency =>
                    $"<dependency id=\"{dependency.Id}\" version=\"{dependency.Version}\" />"))
                + "</group></dependencies>"
            : "";
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                $"{id}.nuspec",
                nuspecContent ?? $"""
                    <package><metadata>
                      <id>{id}</id><version>{version}</version>
                      <authors>Payload tests</authors><description>Exact-pin fixture</description>
                      <readme>README.md</readme>
                      {dependenciesXml}
                    </metadata></package>
                    """);
            WriteEntry(archive, "README.md", readme);
            if (library is not null)
            {
                using Stream entry = archive.CreateEntry(
                    $"{libraryDirectory}/{libraryName}").Open();
                entry.Write(library);
            }
            if (documentation is not null)
            {
                using Stream entry = archive.CreateEntry(
                    $"{libraryDirectory}/"
                        + Path.ChangeExtension(libraryName, ".xml"))
                    .Open();
                entry.Write(documentation);
            }
            if (pdb is not null)
            {
                using Stream entry = archive.CreateEntry(
                    $"{libraryDirectory}/"
                        + Path.ChangeExtension(libraryName, ".pdb"))
                    .Open();
                entry.Write(pdb);
            }
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
            if (extraEntries is not null)
            {
                foreach (var (path, content) in extraEntries)
                {
                    using Stream entry = archive.CreateEntry(path).Open();
                    entry.Write(content);
                }
            }
        }
        return buffer.ToArray();
    }

    private static byte[] CreateToolPackage(
        string id,
        string? packageType,
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            string packageTypes = packageType is not null
                ? $"""
                      <packageTypes>
                        <packageType name="{packageType}" />
                      </packageTypes>
                  """
                : "";
            WriteEntry(archive, $"{id}.nuspec", $"""
                <package><metadata>
                  <id>{id}</id><version>{Version}</version>
                  <authors>Payload tests</authors>
                  <description>Tool measurement fixture</description>
                {packageTypes}
                </metadata></package>
                """);
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(path).Open();
                stream.Write(content);
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
        Func<CancellationToken, Task>? beforeResponse = null,
        string version = Version,
        Func<HttpContent>? vulnerabilityPage = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Enqueue(url);
            if (beforeResponse is not null)
                await beforeResponse(cancellationToken);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string vulnerabilityIndex =
                new Uri(new Uri(source), "vulnerability/index.json").AbsoluteUri;
            string vulnerabilityPageUrl =
                new Uri(new Uri(source), "vulnerability/page.json").AbsoluteUri;
            string packageUrl = $"{flat}{id.ToLowerInvariant()}/{version}/{id.ToLowerInvariant()}.{version}.nupkg";
            HttpContent content;
            if (url == source)
            {
                string vulnerabilityResource = vulnerabilityPage is null
                    ? ""
                    : $$"""
                      ,{"@id":"{{vulnerabilityIndex}}","@type":"VulnerabilityInfo/6.7.0"}
                      """;
                content = new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}{{vulnerabilityResource}}
                    ]}
                    """);
            }
            else if (url == packageUrl)
            {
                content = payload();
            }
            else if (vulnerabilityPage is not null
                && url == vulnerabilityIndex)
            {
                content = new StringContent($$"""
                    [{"@id":"{{vulnerabilityPageUrl}}"}]
                    """);
            }
            else if (vulnerabilityPage is not null
                && url == vulnerabilityPageUrl)
            {
                content = vulnerabilityPage();
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

    private sealed class NotFoundPayloadFeedHandler(
        string source,
        string id,
        ConcurrentQueue<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            requests.Enqueue(url);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string packageUrl =
                $"{flat}{id.ToLowerInvariant()}/{Version}/{id.ToLowerInvariant()}.{Version}.nupkg";
            HttpResponseMessage response;
            if (url == source)
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {"version":"3.0.0","resources":[
                          {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """),
                };
            }
            else if (url == packageUrl)
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected exact-pin request: {url}");
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
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
