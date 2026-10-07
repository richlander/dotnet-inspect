using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Text;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    const string PrefixScopeFramework = "net10.0";

    static readonly PackageSource PrefixScopeSource =
        new("nuget.org", "https://api.nuget.org/v3/index.json");

    static readonly HttpClient PrefixScopeHttpClient =
        new(new PrefixScopeRejectingHandler());

    [Fact]
    public async Task WorkspacePrefixScope_AdmitsCandidatesInSourceOrder()
    {
        SearchResult[] matches =
        [
            Match("Contoso.Alpha"),
            Match("Contoso.Beta"),
        ];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync(matches);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceRegistrationRevision revision =
            CurrentRegistration(workspace);
        WorkspaceScopeSnapshot scope =
            await CurrentScopeAsync(workspace);

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        revision,
                        registration,
                        scope,
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));

        Assert.True(settled.IsComplete);
        Assert.Equal(
            ["Contoso.Alpha", "Contoso.Beta"],
            settled.Query.Results.Select(
                static result => result.Package.PackageId));
        Assert.All(
            settled.Candidates,
            candidate => Assert.Equal(
                PackagePrefixWorkspaceCandidateDisposition.Admitted,
                candidate.Disposition));
        WorkspaceScopeSnapshot committed =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                settled.ScopeOperation).Snapshot;
        Assert.Equal(
            ["contoso.alpha", "contoso.beta"],
            committed.Packages.Select(
                static package =>
                    package.Occurrence.Package.Coordinate.PackageId));
        Assert.Same(
            revision.Identity,
            CurrentRegistration(workspace).Identity);
    }

    [Fact]
    public async Task WorkspacePrefixScope_PreparationFailureLeavesUsableRoot()
    {
        SearchResult[] matches =
        [
            Match("Contoso.Available"),
            Match("Contoso.Missing"),
        ];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync(matches[..1]);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        await CurrentScopeAsync(workspace),
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));

        Assert.False(settled.IsComplete);
        Assert.Collection(
            settled.Candidates,
            candidate => Assert.Equal(
                PackagePrefixWorkspaceCandidateDisposition.Admitted,
                candidate.Disposition),
            candidate =>
            {
                Assert.Equal(
                    PackagePrefixWorkspaceCandidateDisposition
                        .PreparationFailed,
                    candidate.Disposition);
                Assert.Contains(
                    candidate.Failures,
                    failure =>
                        failure.Kind
                            == WorkspaceContextLoadFailureKind
                                .PackageProducerUnavailable);
            });
        WorkspaceScopeSnapshot committed =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                settled.ScopeOperation).Snapshot;
        Assert.Equal(
            "contoso.available",
            Assert.Single(committed.Packages)
                .Occurrence.Package.Coordinate.PackageId);
    }

    [Fact]
    public async Task WorkspacePrefixScope_CapacityDeclinesWithoutAcquisition()
    {
        SearchResult[] matches =
        [
            .. Enumerable.Range(
                    0,
                    WorkspaceScopeLimits.DefaultMaxPackages + 1)
                .Select(index => Match($"Contoso.Package{index:D2}")),
        ];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync(matches);
        var authorization = new RecordingAuthorization(PrefixScopeSource);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        await CurrentScopeAsync(workspace),
                        source,
                        store,
                        maximumPackages: matches.Length,
                        authorization),
                    TestContext.Current.CancellationToken));

        Assert.False(settled.IsComplete);
        Assert.Equal(
            WorkspaceScopeLimits.DefaultMaxPackages,
            settled.Candidates.Count(candidate =>
                candidate.Disposition
                    == PackagePrefixWorkspaceCandidateDisposition.Admitted));
        PackagePrefixWorkspaceCandidateResult declined =
            Assert.Single(
                settled.Candidates,
                candidate =>
                    candidate.Disposition
                        == PackagePrefixWorkspaceCandidateDisposition
                            .CapacityDeclined);
        Assert.Equal("Contoso.Package64", declined.Package.PackageId);
        Assert.Equal(
            WorkspaceScopeLimits.DefaultMaxPackages,
            authorization.Requests.Count);
        Assert.Equal(
            WorkspaceScopeLimits.DefaultMaxPackages,
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                    settled.ScopeOperation)
                .Snapshot.Packages.Length);
    }

    [Fact]
    public async Task WorkspacePrefixScope_ExistingPackageSkipsAcquisition()
    {
        SearchResult[] matches = [Match("Contoso.Alpha")];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync(matches);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceRegistrationRevision revision =
            CurrentRegistration(workspace);
        WorkspaceScopeSnapshot empty =
            await CurrentScopeAsync(workspace);
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled first =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        revision,
                        registration,
                        empty,
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot populated =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                first.ScopeOperation).Snapshot;
        var authorization = new RecordingAuthorization(PrefixScopeSource);

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled second =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        revision,
                        registration,
                        populated,
                        source,
                        store,
                        maximumPackages: 10,
                        authorization),
                    TestContext.Current.CancellationToken));

        Assert.True(second.IsComplete);
        PackagePrefixWorkspaceCandidateResult candidate =
            Assert.Single(second.Candidates);
        Assert.Equal(
            PackagePrefixWorkspaceCandidateDisposition.Existing,
            candidate.Disposition);
        Assert.Same(
            Assert.Single(populated.Packages),
            candidate.Occurrence);
        Assert.Empty(authorization.Requests);
        Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            second.ScopeOperation);
    }

    [Fact]
    public async Task WorkspacePrefixScope_ScopeMovementRejectsPreparedBatch()
    {
        SearchResult candidate = Match("Contoso.Alpha");
        SearchResult concurrent = Match("Contoso.Concurrent");
        var source = new FakePackageSource(
            [candidate],
            new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync([candidate, concurrent]);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceScopeSnapshot scope =
            await CurrentScopeAsync(workspace);
        PackageRootBinding concurrentRoot =
            await PrefixScopeRootAsync(concurrent, store);
        bool moved = false;
        var authorization = new RecordingAuthorization(
            PrefixScopeSource,
            _ =>
            {
                if (moved)
                    return;

                moved = true;
                Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                    workspace.AddPackagesAsync(
                        scope.Revision,
                        scope.PublicationBase,
                        [concurrentRoot],
                        DateTimeOffset.UtcNow.AddMinutes(1),
                        TestContext.Current.CancellationToken)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult());
            });

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        scope,
                        source,
                        store,
                        maximumPackages: 10,
                        authorization),
                    TestContext.Current.CancellationToken));

        Assert.False(settled.IsComplete);
        var rejection =
            Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                settled.ScopeOperation);
        Assert.Equal(
            WorkspaceScopeRejection.RevisionMismatch,
            rejection.Reason);
        Assert.Equal(
            PackagePrefixWorkspaceCandidateDisposition.NotCommitted,
            Assert.Single(settled.Candidates).Disposition);
        Assert.Equal(
            "contoso.concurrent",
            Assert.Single(rejection.Snapshot.Packages)
                .Occurrence.Package.Coordinate.PackageId);
    }

    [Fact]
    public async Task WorkspacePrefixScope_ProducerMismatchDoesNotFallback()
    {
        SearchResult[] matches = [Match("Contoso.Alpha")];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var store = await PrefixScopeStoreAsync(matches);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        var handler = new RecordingRejectingHandler();
        using var httpClient = new HttpClient(handler);
        var authorization = new RecordingAuthorization(
            new PackageSource(
                "other",
                "https://packages.example.test/v3/index.json"));

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        await CurrentScopeAsync(workspace),
                        source,
                        store,
                        maximumPackages: 10,
                        authorization,
                        httpClient),
                    TestContext.Current.CancellationToken));

        Assert.False(settled.IsComplete);
        PackagePrefixWorkspaceCandidateResult failed =
            Assert.Single(settled.Candidates);
        Assert.Equal(
            PackagePrefixWorkspaceCandidateDisposition.PreparationFailed,
            failed.Disposition);
        Assert.Contains(
            failed.Failures,
            failure =>
                failure.Kind
                    == WorkspaceContextLoadFailureKind
                        .PackageProducerUnavailable);
        Assert.Equal(0, handler.Requests);
        Assert.Empty(
            Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                    settled.ScopeOperation)
                .Snapshot.Packages);
    }

    [Fact]
    public async Task WorkspacePrefixScope_SearchTruncationIsIncomplete()
    {
        SearchResult[] matches = [Match("Contoso.Alpha")];
        var source = new FakePackageSource(
            matches,
            new Dictionary<string, byte[]>())
        {
            SearchTruncationReason =
                PackageSearchTruncationReason.RequestedLimit,
        };
        var store = await PrefixScopeStoreAsync(matches);
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);

        PackagePrefixWorkspaceScopeRealizationOutcome.Settled settled =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        await CurrentScopeAsync(workspace),
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));

        Assert.False(settled.IsComplete);
        Assert.Equal(
            PackageQueryCompletionKind.CandidateLimitReached,
            settled.Query.Summary.Completion);
        Assert.Equal(
            PackagePrefixWorkspaceCandidateDisposition.Admitted,
            Assert.Single(settled.Candidates).Disposition);
    }

    [Fact]
    public async Task WorkspacePrefixScope_StaleRegistrationStopsBeforeSearch()
    {
        SearchResult[] matches = [Match("Contoso.Alpha")];
        var source = new FakePackageSource(matches, new Dictionary<string, byte[]>());
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceRegistrationRevision stale =
            CurrentRegistration(workspace);
        WorkspaceScopeSnapshot scope =
            await CurrentScopeAsync(workspace);
        WorkspaceRegistrationOperationResult replacement =
            workspace.ReplaceRegistrations(
                stale,
                [
                    new WorkspaceRegistration.PackagePrefix(
                        new PackagePrefixDeclaration("Other.")),
                ]);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            replacement);

        PackagePrefixWorkspaceScopeRealizationOutcome.Rejected rejected =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Rejected>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        stale,
                        registration,
                        scope,
                        source,
                        new InMemoryPackageStore(),
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackagePrefixWorkspaceScopeRejection
                .StaleRegistrationRevision,
            rejected.Reason);
        Assert.Equal(0, source.LastSearchTake);
    }

    [Fact]
    public async Task WorkspacePrefixScope_RequiresCanonicalFramework()
    {
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Contoso."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceScopeSnapshot scope =
            await CurrentScopeAsync(workspace);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            Request(
                workspace,
                CurrentRegistration(workspace),
                registration,
                scope,
                new FakePackageSource(
                    [],
                    new Dictionary<string, byte[]>()),
                new InMemoryPackageStore(),
                maximumPackages: 10,
                targetFramework: "NET10.0"));

        Assert.Equal("targetFramework", exception.ParamName);
    }

    static PackagePrefixWorkspaceScopeRealizationRequest Request(
        InspectionWorkspace workspace,
        WorkspaceRegistrationRevision revision,
        WorkspaceRegistration.PackagePrefix registration,
        WorkspaceScopeSnapshot scope,
        IPackageSourceClient source,
        IPackageStore store,
        int maximumPackages,
        IPackageSourceAuthorization? authorization = null,
        HttpClient? httpClient = null,
        string targetFramework = PrefixScopeFramework) =>
        new(
            workspace,
            revision,
            registration,
            scope,
            source,
            PackagePrefixRequest.Create(
                registration.Prefix,
                maximumPackages),
            new WorkspaceContextLoadOptions
            {
                HttpClient = httpClient ?? PrefixScopeHttpClient,
                SourceAuthorization = authorization
                    ?? new UniformPackageSourceAuthorization(
                        [PrefixScopeSource]),
                PackageStore = store,
            },
            targetFramework,
            DateTimeOffset.UtcNow.AddMinutes(1));

    static WorkspaceRegistrationRevision CurrentRegistration(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    static async Task<WorkspaceScopeSnapshot> CurrentScopeAsync(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    static async Task<InMemoryPackageStore> PrefixScopeStoreAsync(
        IEnumerable<SearchResult> packages)
    {
        var store = new InMemoryPackageStore();
        foreach (SearchResult package in packages)
        {
            using var content = new MemoryStream(
                PrefixScopePackage(package.Id, package.Version));
            await store.CommitAsync(
                package.Id,
                package.Version,
                NuGetCache.GetSourceKey(PrefixScopeSource.Url),
                content,
                TestContext.Current.CancellationToken);
        }

        return store;
    }

    static async Task<PackageRootBinding> PrefixScopeRootAsync(
        SearchResult package,
        IPackageStore store)
    {
        var coordinate = new RealizedMemberCoordinate.Package(
            PackageSourceCoordinate.Create(
                package.Id,
                package.Version).PackageId,
            PackageSourceCoordinate.Create(
                package.Id,
                package.Version).Version,
            NuGetCache.GetSourceKey(PrefixScopeSource.Url),
            PrefixScopeFramework,
            runtimeIdentifier: null);
        WorkspacePackageRootAcquisitionOutcome outcome =
            await WorkspaceContextLoader.AcquireRealizedPackageRootAsync(
                coordinate,
                new WorkspaceContextLoadOptions
                {
                    HttpClient = PrefixScopeHttpClient,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            [PrefixScopeSource]),
                    PackageStore = store,
                },
                TestContext.Current.CancellationToken);
        return Assert.IsType<
            WorkspacePackageRootAcquisitionOutcome.Acquired>(
                outcome).Root;
    }

    static byte[] PrefixScopePackage(
        string packageId,
        string version)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream manifest =
                archive.CreateEntry($"{packageId}.nuspec").Open())
            {
                manifest.Write(
                    Encoding.UTF8.GetBytes(
                        $"""
                        <package>
                          <metadata>
                            <id>{packageId}</id>
                            <version>{version}</version>
                          </metadata>
                        </package>
                        """));
            }

            using (Stream readme =
                archive.CreateEntry("readme.txt").Open())
            {
                readme.Write("fixture"u8);
            }
        }

        return buffer.ToArray();
    }

    sealed class RecordingAuthorization(
        PackageSource source,
        Action<string>? onAuthorize = null)
        : IPackageSourceAuthorization
    {
        readonly UniformPackageSourceAuthorization _inner =
            new([source]);

        internal List<string> Requests { get; } = [];

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            Requests.Add(packageId);
            onAuthorize?.Invoke(packageId);
            return _inner.AuthorizeSourcesFor(packageId);
        }
    }

    sealed class RecordingRejectingHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
        }
    }

    sealed class PrefixScopeRejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
    }
}
