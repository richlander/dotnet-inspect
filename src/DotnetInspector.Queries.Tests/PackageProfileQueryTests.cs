using System.Text;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageProfileQueryTests
{
    [Fact]
    public async Task ExecuteAsync_StreamsManifestMatchesWithoutPackagePayloads()
    {
        var source = new FakePackageSource(
            [
                Match(
                    "Contoso.First",
                    "1.0.0",
                    owners: ["Contoso", "Partner"]),
                Match("Contoso.Second", "2.0.0"),
            ],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.first@1.0.0"] = Manifest(
                    "Contoso.First",
                    "1.0.0",
                    """
                    <group targetFramework="net8.0">
                      <dependency id="Third.Party" version="[3.0.0]" />
                    </group>
                    """),
                ["contoso.second@2.0.0"] = Manifest(
                    "Contoso.Second",
                    "2.0.0"),
            });

        await using IAsyncEnumerator<PackageProfileEvent> events =
            PackageProfileQuery.ExecuteAsync(
                    source,
                    new PackagePrefixProfileRequest("Contoso.", 10),
                    TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await events.MoveNextAsync());
        PackageProfileMatch first = Assert.IsType<PackageProfileEvent.Match>(
            events.Current).Value;
        Assert.Equal("Contoso.First", first.PackageId);
        Assert.Equal(["Contoso", "Partner"], first.Owners);
        DeclaredPackageDependency dependency = Assert.Single(
            Assert.Single(first.Manifest.DependencyGroups).Dependencies);
        Assert.Equal("Third.Party", dependency.Id);
        Assert.Equal("[3.0.0]", dependency.VersionRange);
        Assert.Equal(["contoso.first@1.0.0"], source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);

        Assert.True(await events.MoveNextAsync());
        PackageProfileMatch second = Assert.IsType<PackageProfileEvent.Match>(
            events.Current).Value;
        Assert.Equal("Contoso.Second", second.PackageId);
        Assert.Empty(second.Manifest.DependencyGroups);
        Assert.Equal(
            ["contoso.first@1.0.0", "contoso.second@2.0.0"],
            source.ManifestRequests);
        Assert.Equal(0, source.PackageRequests);

        Assert.True(await events.MoveNextAsync());
        PackageProfileSummary summary =
            Assert.IsType<PackageProfileEvent.Completed>(events.Current).Value;
        Assert.Equal(2, summary.Candidates);
        Assert.Equal(2, summary.Matches);
        Assert.Equal(0, summary.Failures);
        Assert.False(summary.Truncated);
        Assert.False(await events.MoveNextAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsSharedOperationContext()
    {
        var source = new FakePackageSource(
            [Match("Contoso.Package", "1.0.0")],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.package@1.0.0"] = Manifest(
                    "Contoso.Package",
                    "1.0.0"),
            });
        using var operationContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(4),
            TestContext.Current.CancellationToken);

        _ = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken,
                operationContext));

        Assert.Same(operationContext, source.SearchOperationContext);
        Assert.Same(
            operationContext,
            Assert.Single(source.ManifestOperationContexts));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInvalidManifestAndContinues()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.Broken", "1.0.0"),
                Match("Contoso.Valid", "1.0.0"),
            ],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.broken@1.0.0"] = Manifest(
                    "Other.Package",
                    "1.0.0"),
                ["contoso.valid@1.0.0"] = Manifest(
                    "Contoso.Valid",
                    "1.0.0"),
            });

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(
            PackageProfileFailureKind.InvalidManifest,
            failure.Kind);
        Assert.Equal(
            PackageManifestFailureReason.IdentityMismatch,
            failure.ManifestFailureReason);
        Assert.Equal(
            "The package manifest identity does not match the requested package.",
            failure.Message);
        Assert.DoesNotContain(
            "Other.Package",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal("Contoso.Broken", failure.PackageId);
        Assert.IsType<PackageProfileEvent.Match>(events[1]);
        PackageProfileSummary summary =
            Assert.IsType<PackageProfileEvent.Completed>(events[2]).Value;
        Assert.Equal(1, summary.Matches);
        Assert.Equal(1, summary.Failures);
        Assert.Equal(0, source.PackageRequests);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsSearchFailureAsIncompleteStream()
    {
        var source = new FakePackageSource(
            [],
            new Dictionary<string, byte[]>())
        {
            SearchFailureKind = PackageSourceFailureKind.Timeout,
        };

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(PackageProfileFailureKind.Search, failure.Kind);
        PackageProfileSummary summary =
            Assert.IsType<PackageProfileEvent.Completed>(events[1]).Value;
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsTruncation()
    {
        var source = new FakePackageSource(
            [Match("Contoso.One", "1.0.0")],
            new Dictionary<string, byte[]>
            {
                ["contoso.one@1.0.0"] = Manifest(
                    "Contoso.One",
                    "1.0.0"),
            })
        {
            SearchTruncated = true,
        };

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso.", 1),
                TestContext.Current.CancellationToken));

        Assert.True(
            Assert.IsType<PackageProfileEvent.Completed>(events[^1])
                .Value.Truncated);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesSourcePaginationTruncation()
    {
        var source = new FakePackageSource(
            [],
            new Dictionary<string, byte[]>())
        {
            SearchTruncationReason =
                PackageSearchTruncationReason.SourcePageLimit,
        };

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileSummary summary =
            Assert.IsType<PackageProfileEvent.Completed>(events[^1]).Value;
        Assert.Equal(
            PackageSearchTruncationReason.SourcePageLimit,
            summary.TruncationReason);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOutOfPrefixSourceItemBeforeManifestFetch()
    {
        var source = new FakePackageSource(
            [Match("Other.Package", "1.0.0")],
            new Dictionary<string, byte[]>());

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(
            PackageProfileFailureKind.SearchContract,
            failure.Kind);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsSearchOverReturnBeforeManifestFetch()
    {
        var source = new FakePackageSource(
            [
                Match("Contoso.First", "1.0.0"),
                Match("Contoso.Second", "1.0.0"),
            ],
            new Dictionary<string, byte[]>());

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest(
                    "Contoso.",
                    MaximumPackages: 1),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(PackageProfileFailureKind.SearchContract, failure.Kind);
        PackageProfileSummary summary =
            Assert.IsType<PackageProfileEvent.Completed>(events[1]).Value;
        Assert.Equal(0, summary.Candidates);
        Assert.Equal(1, summary.Failures);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_ResultSnapshotDoesNotExposeEnumeratorOverReturn()
    {
        var matches = new MisreportingReadOnlyList<SearchResult>(
            [
                Match("Contoso.First", "1.0.0"),
                Match("Contoso.Second", "2.0.0"),
            ],
            reportedCount: 1);
        var source = new FakePackageSource(
            matches,
            new Dictionary<string, byte[]>
            {
                ["contoso.first@1.0.0"] = Manifest(
                    "Contoso.First",
                    "1.0.0"),
            });

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest(
                    "Contoso.",
                    MaximumPackages: 1),
                TestContext.Current.CancellationToken));

        Assert.IsType<PackageProfileEvent.Match>(events[0]);
        Assert.IsType<PackageProfileEvent.Completed>(events[1]);
        Assert.Equal(
            ["contoso.first@1.0.0"],
            source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMismatchedSearchSourceBeforeManifestFetch()
    {
        var source = new FakePackageSource(
            [
                new SearchResult("Contoso.Package", "1.0.0"),
            ],
            new Dictionary<string, byte[]>())
        {
            SearchResultFactory = CreateResultFactory(),
        };

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(
            PackageProfileFailureKind.SearchContract,
            failure.Kind);
        Assert.Empty(source.ManifestRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsManifestWithMismatchedProvenance()
    {
        var source = new FakePackageSource(
            [Match("Contoso.Package", "1.0.0")],
            new Dictionary<string, byte[]>
            {
                ["contoso.package@1.0.0"] = Manifest(
                    "Contoso.Package",
                    "1.0.0"),
            })
        {
            ManifestResultFactory = CreateResultFactory(
                PackageSourceDescriptor.NuGetV3(
                    "packages",
                    "Packages",
                    new Uri("https://packages.example/v3/index.json"))),
        };

        List<PackageProfileEvent> events = await CollectAsync(
            PackageProfileQuery.ExecuteAsync(
                source,
                new PackagePrefixProfileRequest("Contoso."),
                TestContext.Current.CancellationToken));

        PackageProfileFailure failure =
            Assert.IsType<PackageProfileEvent.Failure>(events[0]).Value;
        Assert.Equal(
            PackageProfileFailureKind.ManifestContract,
            failure.Kind);
    }

    private static SearchResult Match(
        string packageId,
        string version,
        IReadOnlyList<string>? owners = null) =>
        new(
            packageId,
            version,
            Owners: owners);

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Package profile test.</description>
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static async Task<List<PackageProfileEvent>> CollectAsync(
        IAsyncEnumerable<PackageProfileEvent> source)
    {
        List<PackageProfileEvent> events = [];
        await foreach (PackageProfileEvent item in source)
            events.Add(item);
        return events;
    }

    private static PackageSourceResultFactory CreateResultFactory(
        PackageSourceDescriptor? descriptor = null)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                descriptor ?? PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    private sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class MisreportingReadOnlyList<T>(
        T[] items,
        int reportedCount)
        : IReadOnlyList<T>
    {
        public int Count => reportedCount;
        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator() =>
            ((IEnumerable<T>)items).GetEnumerator();

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            items.GetEnumerator();
    }

    private sealed class FakePackageSource(
        IReadOnlyList<SearchResult> matches,
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceFailureKind? SearchFailureKind { get; init; }
        public PackageSourceResultFactory? SearchResultFactory { get; init; }
        public bool SearchTruncated
        {
            init
            {
                if (value)
                {
                    SearchTruncationReason =
                        PackageSearchTruncationReason.RequestedLimit;
                }
            }
        }
        public PackageSearchTruncationReason SearchTruncationReason
        {
            get;
            init;
        }
        public PackageSourceResultFactory? ManifestResultFactory { get; init; }
        public NuGetOperationContext? SearchOperationContext
        {
            get;
            private set;
        }
        public List<NuGetOperationContext?> ManifestOperationContexts
        {
            get;
        } = [];
        public List<string> ManifestRequests { get; } = [];
        public int PackageRequests { get; private set; }
        public PackageSourceResultIdentity Source => _results.Source;
        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchOperationContext = operationContext;
            PackageSourceResultFactory results =
                SearchResultFactory ?? _results;
            PackageSourceOperationResult<PackageSearchResult> result =
                SearchFailureKind is null
                    ? results.SucceededSearch(
                        results.Search(
                            matches,
                            SearchTruncationReason))
                    : _results.FailedSearch(SearchFailureKind.Value);
            return Task.FromResult(result);
        }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManifestOperationContexts.Add(operationContext);
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            ManifestRequests.Add(key);
            PackageSourceResultFactory results =
                ManifestResultFactory ?? _results;
            PackageSourceOperationResult<PackageSourceManifest> result =
                manifests.TryGetValue(key, out byte[]? content)
                    ? results.SucceededManifest(
                        coordinate,
                        results.Manifest(coordinate, content))
                    : _results.FailedManifest(
                        coordinate,
                        PackageSourceFailureKind.NotFound);
            return Task.FromResult(result);
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            PackageRequests++;
            throw new NotSupportedException();
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
