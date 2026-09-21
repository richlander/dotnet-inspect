using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Packages;
using QuerySpace;
using DotnetInspector.QueryOperations;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    private static PackageQueryPlan Accepted(PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Accepted>(result).Plan;

    private static PackageQueryRequestFailure Rejected(
        PackageQueryPlanResult result) =>
        Assert.IsType<PackageQueryPlanResult.Rejected>(result).Failure;

    private static PortableQueryTerm Term(
        string key,
        string value,
        PortableQueryOperator @operator = PortableQueryOperator.Equal) =>
        new(key, @operator, value);

    private static PackageQueryEcosystemMembershipCatalog EcosystemCatalog(
        params PackageQueryEcosystemMembershipDeclaration[] declarations) =>
        new(declarations);

    private static PackageQueryEcosystemMembershipDeclaration Ecosystem(
        string id,
        string[]? exactPackages = null,
        string[]? packagePrefixes = null) =>
        new(
            WorkspaceEcosystemRegistrationId.Create(id),
            (exactPackages ?? []).Select(package => new PackageCoordinate(package)),
            (packagePrefixes ?? []).Select(prefix =>
                new PackagePrefixDeclaration(prefix)));

    private static string EvidenceProperty(
        PackageQueryMatch match,
        string evidenceId,
        string propertyName) =>
        EvidenceProperty(
            Assert.Single(
                match.Evidence,
                evidence => evidence.Id == evidenceId),
            propertyName);

    private static string EvidenceProperty(
        PackageQueryEvidence evidence,
        string propertyName) =>
        Assert.Single(
            evidence.Properties,
            property => property.Name == propertyName).Value;

    private static PortableQueryTerm[] InspectionTerms(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index =>
            Term(
                PackageQuery.DependsTermKey,
                $"Contoso.Dependency.{index:D2}")),
    ];

    private static SearchResult Match(
        string packageId,
        string version = "1.0.0",
        bool verified = false,
        long totalDownloads = 0)
        => new(
            packageId,
            version,
            TotalDownloads: totalDownloads,
            Verified: verified);

    private enum RequiredIdentityName
    {
        Module,
        Assembly,
        AssemblyReference,
    }

    private static byte[] ManagedAssemblyWithReferences(
        int referenceCount,
        RequiredIdentityName? emptyName = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: emptyName == RequiredIdentityName.Module
                ? default
                : metadata.GetOrAddString("Contoso.Package.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            emptyName == RequiredIdentityName.Assembly
                ? default
                : metadata.GetOrAddString("Contoso.Package"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < referenceCount; index++)
        {
            metadata.AddAssemblyReference(
                emptyName == RequiredIdentityName.AssemblyReference
                    && index == 0
                        ? default
                        : metadata.GetOrAddString(
                            $"Reference{index:D5}"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static async Task AssertAssemblyReferenceIdentityFailureAsync(
        byte[] image)
    {
        var content = new FakePackageQueryContentProvider(
            new Dictionary<string, IPackageContent>
            {
                ["Contoso.Package"] = FakePackageContent.FromBytes(
                    ("lib/net8.0/Contoso.Package.dll", image)),
            });
        var source = SourceFor(Manifest("Contoso.Package"));
        PackageQueryPlan plan = Accepted(PackageQuery.Plan(
            new PackageQueryRequest(
                "Contoso.*",
                [Term(PackageQuery.ReferencesTermKey, "Reference00000")],
                MaximumCandidates: 1,
                MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                content,
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        PackageQueryFailure failure =
            Assert.Single(events.OfType<PackageQueryEvent.Failure>()).Value;
        Assert.Equal(
            PackageQueryFailureKind.PackageContentEvaluation,
            failure.Kind);
    }

    private static byte[] WithMalformedModuleDefinitionName(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata => metadata.GetModuleDefinition().Name);

    private static byte[] WithMalformedAssemblyDefinitionName(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata => metadata.GetAssemblyDefinition().Name);

    private static byte[] WithMalformedAssemblyReferenceName(byte[] image) =>
        WithMalformedUtf8(
            image,
            metadata =>
                metadata.GetAssemblyReference(
                    Assert.Single(metadata.AssemblyReferences)).Name);

    private static byte[] WithMalformedUtf8(
        byte[] image,
        Func<MetadataReader, StringHandle> selectHandle)
    {
        byte[] malformed = image.ToArray();
        using var reader = new PEReader(
            new MemoryStream(malformed, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        StringHandle handle = selectHandle(metadata);
        Assert.False(handle.IsNil);
        int stringOffset = reader.PEHeaders.MetadataStartOffset
            + metadata.GetHeapMetadataOffset(HeapIndex.String)
            + MetadataTokens.GetHeapOffset(handle);
        malformed[stringOffset] = 0xff;
        return malformed;
    }

    private static byte[] PaddedImage(byte[] image, int length)
    {
        var padded = new byte[length];
        image.CopyTo(padded, 0);
        return padded;
    }

    private static FakePackageSource SourceFor(
        byte[] manifest,
        string packageId = "Contoso.Package",
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None) =>
        new(
            [Match(packageId)],
            new Dictionary<string, byte[]>
            {
                [$"{packageId.ToLowerInvariant()}@1.0.0"] = manifest,
            })
        {
            SearchTruncationReason = truncationReason,
        };

    private static byte[] Manifest(
        string packageId,
        string version = "1.0.0",
        string dependencies = "",
        string packageTypes = "",
        string readme = "",
        string license = "") =>
        Encoding.UTF8.GetBytes(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Manifest Author</authors>
                <description>Package query test.</description>
                {{license}}
                {{packageTypes}}
                {{readme}}
                <dependencies>{{dependencies}}</dependencies>
              </metadata>
            </package>
            """);

    private static async Task<List<PackageQueryEvent>> CollectAsync(
        IAsyncEnumerable<PackageQueryEvent> source)
    {
        List<PackageQueryEvent> events = [];
        await foreach (PackageQueryEvent item in source)
            events.Add(item);
        return events;
    }

    private sealed class RecordingPackageQueryNonterminalSink
        : IPackageQueryNonterminalSink
    {
        internal List<PackageQueryEvent.Nonterminal> Events { get; } = [];

        public ValueTask ReportAsync(
            PackageQueryEvent.Nonterminal queryEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(queryEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelOnMatchSink(CancellationTokenSource cancellation)
        : IPackageQueryNonterminalSink
    {
        internal bool SawMatch { get; private set; }

        public ValueTask ReportAsync(
            PackageQueryEvent.Nonterminal queryEvent,
            CancellationToken cancellationToken)
        {
            if (queryEvent is PackageQueryEvent.Match)
            {
                SawMatch = true;
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.CompletedTask;
        }
    }

    private static async Task AssertNoMatchesAsync(
        FakePackageSource source,
        PortableQueryTerm term)
    {
        PackageQueryPlan plan = Accepted(
            PackageQuery.Plan(
                new PackageQueryRequest(
                    "Contoso.*",
                    [term],
                    MaximumCandidates: 1,
                    MaximumMatches: 1)));

        List<PackageQueryEvent> events = await CollectAsync(
            PackageQuery.ExecuteAsync(
                source,
                plan,
                new FakePackageQueryContentProvider(
                    new Dictionary<string, IPackageContent>()),
                TestContext.Current.CancellationToken));

        Assert.Empty(events.OfType<PackageQueryEvent.Match>());
        Assert.Equal(
            PackageQueryCompletionKind.Exhausted,
            Assert.IsType<PackageQueryEvent.Completed>(events[^1])
                .Value.Completion);
    }

    private static PackageSourceResultFactory CreateResultFactory() =>
        CreateResultFactory(PackageSourceAssociation.Create());

    private static PackageSourceResultFactory CreateResultFactory(
        PackageSourceAssociation association)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                association,
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

    private sealed class FakePackageSource(
        IReadOnlyList<SearchResult> matches,
        IReadOnlyDictionary<string, byte[]> manifests)
        : IPackageSourceClient
    {
        private readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceFailureKind? SearchFailureKind { get; init; }
        public PackageSearchTruncationReason SearchTruncationReason
        {
            get;
            init;
        }
        public Action<int>? OnManifestRequest { get; set; }
        public List<string> ManifestRequests { get; } = [];
        public int LastSearchTake { get; private set; }
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
            LastSearchTake = take;
            PackageSourceOperationResult<PackageSearchResult> result =
                SearchFailureKind is null
                    ? _results.SucceededSearch(
                        _results.Search(
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
            PackageSourceCoordinate coordinate =
                PackageSourceCoordinate.Create(packageId, version);
            string key = $"{coordinate.PackageId}@{coordinate.Version}";
            ManifestRequests.Add(key);
            OnManifestRequest?.Invoke(ManifestRequests.Count);
            PackageSourceOperationResult<PackageSourceManifest> result =
                manifests.TryGetValue(key, out byte[]? content)
                    ? _results.SucceededManifest(
                        coordinate,
                        _results.Manifest(coordinate, content))
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

    private sealed class FakePackageQueryContentProvider(
        IReadOnlyDictionary<string, IPackageContent> content,
        string unavailableMessage = "package content unavailable")
        : IPackageQueryContentProvider
    {
        public List<string> Requests { get; } = [];

        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(package.PackageId);
            return ValueTask.FromResult<PackageQueryContentResult>(
                content.TryGetValue(
                    package.PackageId,
                    out IPackageContent? packageContent)
                    ? new PackageQueryContentResult.Available(packageContent)
                    : new PackageQueryContentResult.Unavailable(
                        unavailableMessage));
        }
    }

    private sealed class FakePackageQueryTraversal
    {
        private readonly Resolver _resolver;
        private readonly Acquirer _acquirer;

        internal FakePackageQueryTraversal(
            params (string PackageId, string Version, string Dependencies)[]
                packages)
        {
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize(
                    [
                        new PackageSource(
                            "package-query-test",
                            "https://package-query-test.example/v3/index.json"),
                    ]);
            var issuer = new PackageAcquisitionCandidateIssuer();
            var candidates =
                new Dictionary<string, PackageAcquisitionCandidate>(
                    StringComparer.OrdinalIgnoreCase);
            var manifests =
                new Dictionary<
                    PackageAcquisitionCandidateCorrespondence,
                    PackageSourceManifest>();
            foreach ((string packageId, string version, string dependencies)
                in packages)
            {
                PackageSourceCoordinate coordinate =
                    PackageSourceCoordinate.Create(packageId, version);
                PackageAcquisitionCandidate candidate =
                    issuer.ResolvePinnedCandidate(
                        authorization,
                        coordinate).Candidate
                    ?? throw new InvalidOperationException(
                        "The test package coordinate was not authorized.");
                candidates.Add(packageId, candidate);
                PackageSourceResultFactory factory = CreateResultFactory(
                    candidate.Authorities[0].Authority.Association);
                manifests.Add(
                    candidate.Correspondence,
                    factory.Manifest(
                        coordinate,
                        Manifest(packageId, version, dependencies)));
            }

            _resolver = new Resolver(candidates);
            _acquirer = new Acquirer(manifests);
            Services = new(_resolver, _acquirer);
        }

        internal PackageQueryDependencyTraversalServices Services { get; }

        internal int ResolverCallCount => _resolver.CallCount;

        internal int ManifestCallCount => _acquirer.CallCount;

        private sealed class Resolver(
            IReadOnlyDictionary<string, PackageAcquisitionCandidate> candidates)
            : IPackageDependencyTraversalCandidateResolver
        {
            internal int CallCount { get; private set; }

            public ValueTask<PackageDependencyTraversalCandidateResult>
                ResolveAsync(
                    PackageDependencyEvidenceDeclaration declaration,
                    CancellationToken cancellationToken = default,
                    NuGetOperationContext? operationContext = null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return ValueTask.FromResult<
                    PackageDependencyTraversalCandidateResult>(
                    candidates.TryGetValue(
                        declaration.CanonicalPackageId,
                        out PackageAcquisitionCandidate? candidate)
                            ? new PackageDependencyTraversalCandidateResult
                                .Resolved(candidate, [])
                            : new PackageDependencyTraversalCandidateResult
                                .Failed(
                                    new PackageDependencyTraversalCandidateFailure
                                        .NoMatchingVersion()));
            }
        }

        private sealed class Acquirer(
            IReadOnlyDictionary<
                PackageAcquisitionCandidateCorrespondence,
                PackageSourceManifest> manifests)
            : IPackageDependencyTraversalManifestAcquirer
        {
            internal int CallCount { get; private set; }

            public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
                PackageAcquisitionCandidate candidate,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return Task.FromResult<PackageDependencyTraversalManifestResult>(
                    manifests.TryGetValue(
                        candidate.Correspondence,
                        out PackageSourceManifest? manifest)
                            ? new PackageDependencyTraversalManifestResult
                                .Acquired(manifest, [])
                            : new PackageDependencyTraversalManifestResult
                                .Failed([]));
            }
        }
    }

    private sealed class FakePackageContent : IPackageContent
    {
        readonly IReadOnlyDictionary<string, byte[]> _entries;

        public FakePackageContent(
            params (string Path, string Content)[] entries) =>
            _entries = entries.ToDictionary(
                entry => entry.Path,
                entry => Encoding.UTF8.GetBytes(entry.Content),
                StringComparer.Ordinal);

        private FakePackageContent(
            IEnumerable<(string Path, byte[] Content)> entries) =>
            _entries = entries.ToDictionary(
                entry => entry.Path,
                entry => entry.Content,
                StringComparer.Ordinal);

        public static FakePackageContent FromBytes(
            params (string Path, byte[] Content)[] entries) =>
            new(entries);

        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "nuget.org";
        public bool RequiresArchiveTreeMatch => false;
        public List<string> EntryRequests { get; } = [];

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            EntryRequests.Add(relativePath);
            if (_entries.TryGetValue(relativePath, out byte[]? content))
            {
                stream = new MemoryStream(content, writable: false);
                return true;
            }

            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            EntryRequests.Add(relativePath);
            if (!_entries.TryGetValue(relativePath, out byte[]? content)
                || content.LongLength > maxExpandedBytes)
            {
                stream = null;
                return false;
            }

            stream = new MemoryStream(content, writable: false);
            return true;
        }

        public IEnumerable<string> EnumerateEntries() => _entries.Keys;
    }
}
