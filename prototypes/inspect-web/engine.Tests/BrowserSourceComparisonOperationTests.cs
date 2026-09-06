using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InspectWeb.Engine.SourceFacade;
using InspectWeb.MethodBodyFixtures;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

/// <summary>
/// Managed outcome cases for the authored Source comparison export (#6076).
/// </summary>
/// <remarks>
/// <para>
/// Every case starts from the compiler-produced version pair in
/// <c>fixtures/inspect-web/InspectWeb.SourceComparisonFixtures.V1</c> and
/// <c>fixtures/inspect-web/InspectWeb.SourceComparisonFixtures.V2</c>, registered as two versions
/// of one browser package. That pair compiles the same <c>Counter.cs</c> inputs as the queries
/// source-diff fixtures, but in the shape a browser participant can actually reach: an embedded
/// PDB, because the browser reads no adjacent file and an unpublished package has no symbol
/// transport, and a SourceLink map on a host the production
/// <see cref="BrowserSourceFetchPolicy"/> admits. Nothing here builds a Source endpoint, a line
/// pair, or a comparison result: the product query resolves each image independently, extracts and
/// checksum-verifies the PDB source, and compares the authored declarations, and the browser
/// projection is asserted on the result of that work.
/// </para>
/// <para>
/// Through the public export the Source context is the production browser one, whose HTTP client
/// reaches the real internet, so the source fetch for this unpublished fixture pair ends
/// non-success. Those export cases gate the envelope, independent endpoint resolution, and visible
/// non-success. The compared-declaration cases run the same browser scopes and the same paired
/// query with only the HTTP transport substituted — the production fetch policy still governs
/// every source request — which is the only offline route to real extraction, checksum
/// verification, and comparison; published-Wasm acceptance covers the production transport end to
/// end.
/// </para>
/// </remarks>
[Collection("Type source operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserSourceComparisonOperationTests(ITestOutputHelper output)
{
    const string Framework = "net11.0";
    const string SourcePackageId = "InspectWeb.SourceComparisonFixture";
    const string AssemblyName = "InspectWebSourceComparisonFixture.dll";
    const string BeforeVersion = "1.0.0";
    const string AfterVersion = "2.0.0";

    [Theory]
    [InlineData("Counter", true)]
    [InlineData("MovedCounter", false)]
    public async Task ExportResolvesEachEndpointIndependentlyAndKeepsMissingSourceVisible(
        string typeName,
        bool sameToken)
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request = await pair.Request(typeName, "Value");

        BrowserSourceComparisonResult result = await Compare(request);

        Assert.Equal(1, result.Version);
        Assert.Equal(BrowserSourceComparisonResultKind.Succeeded, result.Kind);
        Assert.Null(result.FailureKind);
        Assert.Null(result.Reason);
        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(result.Value);
        Assert.Equal(request, value.Request);
        Assert.Equal(BeforeVersion, value.Before.Version);
        Assert.Equal(AfterVersion, value.After.Version);
        Assert.Equal(value.Before.Assembly, value.After.Assembly);
        Assert.Equal(value.Before.Framework, value.After.Framework);

        // Independent resolution: the same logical member, two images, two module identities.
        Assert.Equal(value.Before.MemberIdentity, value.After.MemberIdentity);
        Assert.Contains(typeName, value.Before.MemberIdentity!);
        Assert.NotEqual(value.Before.AssemblyIdentity, value.After.AssemblyIdentity);
        Assert.NotEqual(value.Before.ModuleVersionId, value.After.ModuleVersionId);
        Assert.NotNull(value.Before.ModuleVersionId);
        Assert.NotNull(value.After.ModuleVersionId);
        Assert.NotNull(value.Before.MetadataToken);
        Assert.NotNull(value.After.MetadataToken);
        Assert.Equal(request.MetadataToken, value.Before.MetadataToken);
        Assert.Equal(sameToken, value.Before.MetadataToken == value.After.MetadataToken);

        // Refused Source acquisition is visible non-success, never an empty successful diff.
        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Empty(value.Lines);
        output.WriteLine($"Before: {value.Before.State}: {value.Before.Detail}");
        output.WriteLine($"After: {value.After.State}: {value.After.Detail}");
        AssertUnresolvedSource(value.Before);
        AssertUnresolvedSource(value.After);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task MemberMissingFromAfterIsNotFoundWithoutHidingTheBeforeEndpoint()
    {
        await using Pair pair = await Pair.OpenAsync();

        BrowserSourceComparisonResult result = await Compare(
            await pair.Request("Counter", "BeforeOnly"));

        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(result.Value);
        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Empty(value.Lines);
        Assert.Equal("NotFound", value.After.State);
        Assert.Contains("TargetNotFound", value.After.Detail);
        Assert.Null(value.After.MemberIdentity);
        Assert.Null(value.After.MetadataToken);
        Assert.Equal(AfterVersion, value.After.Version);
        Assert.NotNull(value.After.AssemblyIdentity);

        // The launching endpoint keeps its own resolved identity: a member that is absent in the
        // other version is not a comparison failure and does not retract Before.
        Assert.NotNull(value.Before.MemberIdentity);
        Assert.Equal(value.Request.MetadataToken, value.Before.MetadataToken);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task SameVersionPairStaysTwoIndependentlyResolvedEndpoints()
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request =
            await pair.Request("Counter", "Value", afterVersion: BeforeVersion);

        BrowserSourceComparison value = Assert.IsType<BrowserSourceComparison>(
            (await Compare(request)).Value);

        Assert.Equal(BeforeVersion, value.Before.Version);
        Assert.Equal(BeforeVersion, value.After.Version);
        Assert.Equal(value.Before.ModuleVersionId, value.After.ModuleVersionId);
        Assert.Equal(value.Before.MetadataToken, value.After.MetadataToken);
        Assert.Equal(value.Before.MemberIdentity, value.After.MemberIdentity);
        await pair.AssertScopesReleased(BeforeVersion);
    }

    [Theory]
    [InlineData("{", "JsonException")]
    [InlineData("null", "request is required")]
    public async Task MalformedRequestIsAnExpectedFailureThatReleasesTheOperation(
        string requestJson,
        string diagnostic)
    {
        string id = Guid.NewGuid().ToString();
        BrowserSourceComparisonResult result = Read(
            await SourceExports.QueryMemberSourceComparison(id, requestJson));

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains(diagnostic, result.Diagnostic);
        Assert.Equal(
            BrowserTypeSourceCancellationKind.NotActive,
            Cancel(id, "user").Kind);
    }

    [Theory]
    [InlineData("", BeforeVersion, AfterVersion, 0x06000001, "PackageId")]
    [InlineData("Source.Comparison.Unused", "not a version", AfterVersion, 0x06000001,
        "two exact package versions")]
    [InlineData("Source.Comparison.Unused", BeforeVersion, "", 0x06000001,
        "two exact package versions")]
    [InlineData("Source.Comparison.Unused", BeforeVersion, AfterVersion, 0x04000001,
        "selected MethodDef")]
    [InlineData("Source.Comparison.Unused", BeforeVersion, AfterVersion, 0x06000000,
        "selected MethodDef")]
    public async Task InvalidPairOrSelectionIsRejectedBeforeAnyAcquisition(
        string packageId,
        string beforeVersion,
        string afterVersion,
        int metadataToken,
        string message)
    {
        var request = new BrowserSourceComparisonRequest(
            packageId,
            beforeVersion,
            afterVersion,
            Framework,
            AssemblyName,
            "SourceDiffFixture.Counter",
            "Value",
            "Value()",
            metadataToken);

        BrowserSourceComparisonResult result = await Compare(request);

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task AccessorSelectionDoesNotSilentlyBecomeItsEnclosingDeclaration()
    {
        string packageId = "Source.Comparison.Accessor." + Guid.NewGuid().ToString("N");
        await RegisterAsync(FixtureCatalog.InspectWebMethodBodies, BeforeVersion, packageId);
        await using BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId, BeforeVersion, Framework, TestContext.Current.CancellationToken);
        ApiType type = Assert.Single(
            Surface(lease.Scope).Types,
            candidate => candidate.FullName == typeof(Left).FullName);
        ApiMember property = Assert.Single(
            type.Members,
            member => member.Name == nameof(Left.Value));
        CallGraphMemberBodySelector getter = Assert.Single(
            CallGraphMemberResolver.CreateBodySelectors(type, property),
            selector => selector.MemberName == "get_Value");

        BrowserSourceComparisonResult result = await Compare(new(
            packageId,
            BeforeVersion,
            BeforeVersion,
            Framework,
            "InspectWeb.MethodBodyFixtures.dll",
            type.DefinitionName!.ToEscapedFullName(),
            getter.MemberName,
            getter.SelectorKey,
            getter.BodyToken));

        Assert.Equal(BrowserSourceComparisonResultKind.Failed, result.Kind);
        Assert.Equal(BrowserTypeSourceFailureKind.Expected, result.FailureKind);
        Assert.Contains("whole method declaration", result.Error);
        Assert.Null(result.Value);
        await BrowserPackageWorkspace.RemoveScopeAsync(lease.Scope);
    }

    [Fact]
    public async Task CancellationPublishesNoPartialPairAndSettlesEveryProtectedScope()
    {
        await using Pair pair = await Pair.OpenAsync();
        BrowserSourceComparisonRequest request = await pair.Request("Counter", "Value");
        using BrowserSourceOperationLease holder =
            await BrowserSourceOperationCoordinator.BeginAsync();
        string id = Guid.NewGuid().ToString();

        Task<string> pending = SourceExports.QueryMemberSourceComparison(id, Serialize(request));
        Assert.False(pending.IsCompleted);
        Assert.Equal(
            BrowserTypeSourceCancellationKind.Requested,
            Cancel(id, "superseded").Kind);

        BrowserSourceComparisonResult result = Read(await pending);
        Assert.Equal(BrowserSourceComparisonResultKind.Canceled, result.Kind);
        Assert.Equal("superseded", result.Reason);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
        Assert.Null(result.FailureKind);
        Assert.Equal(BrowserTypeSourceCancellationKind.NotActive, Cancel(id, "user").Kind);

        Task<BrowserSourceOperationLease> successor =
            BrowserSourceOperationCoordinator.BeginAsync().AsTask();
        Assert.False(successor.IsCompleted);
        holder.Dispose();
        using BrowserSourceOperationLease next = await successor;
        Assert.False(next.CancellationToken.IsCancellationRequested);
        await pair.AssertScopesReleased();
    }

    [Fact]
    public async Task AuthoredSourceOnlyChangeComparesVerifiedDeclarations()
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", "Value");

        Assert.Equal("Compared", value.Status);
        Assert.False(value.IsExact);
        Assert.Equal("Available", value.Before.State);
        Assert.Equal("Available", value.After.State);
        Assert.Contains("1 + 2", value.Before.Text);
        Assert.Contains("=> 3", value.After.Text);
        Assert.DoesNotContain("=> 3", value.Before.Text);
        Assert.StartsWith(SourcePairHost.BeforeSourcePrefix, value.Before.SourceUrl);
        Assert.StartsWith(SourcePairHost.AfterSourcePrefix, value.After.SourceUrl);
        Assert.NotEqual(value.Before.ModuleVersionId, value.After.ModuleVersionId);

        // The debug information travelled inside each image, so no symbol server was consulted,
        // and both texts arrived over a fetch the production browser policy admitted rather than
        // from the compiler inputs still sitting on this machine.
        Assert.Empty(host.SymbolRequests);
        Assert.Equal(2, host.SourceRequests.Count);
        Assert.All(host.SourceRequests, uri =>
            Assert.Equal("raw.githubusercontent.com", uri.IdnHost));

        // The authored declaration is one line in each version, and the native comparison keeps
        // that polarity: the removed Before row and the added After row each carry a one-based
        // declaration-relative coordinate on their own side only.
        Assert.Equal(2, value.Lines.Length);
        BrowserSourceComparisonLine removed =
            Assert.Single(value.Lines, line => line.Kind == "Removed");
        Assert.Equal("None", removed.Difference);
        Assert.Equal(1, removed.BeforeLine);
        Assert.Contains("1 + 2", removed.BeforeText);
        Assert.Null(removed.AfterLine);
        Assert.Null(removed.AfterText);
        BrowserSourceComparisonLine added =
            Assert.Single(value.Lines, line => line.Kind == "Added");
        Assert.Equal(1, added.AfterLine);
        Assert.Contains("=> 3", added.AfterText);
        Assert.Null(added.BeforeLine);
        Assert.Null(added.BeforeText);
    }

    [Theory]
    [InlineData("Unchanged", true, false)]
    [InlineData("SameSource", true, false)]
    [InlineData("Reordered", false, false)]
    [InlineData("MovedBlock", false, true)]
    [InlineData("MovedBlockAndEdit", false, true)]
    public async Task NativeLinePolarityAndMovementSurviveTheBrowserProjection(
        string memberName,
        bool exact,
        bool moved)
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host();

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", memberName);

        Assert.Equal("Compared", value.Status);
        Assert.Equal(exact, value.IsExact);
        Assert.NotEmpty(value.Lines);
        Assert.Equal(moved, value.Lines.Any(line => line.Difference == "Moved"));
        Assert.All(value.Lines, line =>
        {
            Assert.Contains(line.Kind, (string[])["Present", "Added", "Removed", "Changed"]);
            Assert.Equal(line.BeforeLine is null, line.BeforeText is null);
            Assert.Equal(line.AfterLine is null, line.AfterText is null);
            Assert.True(line.BeforeLine is null or > 0);
            Assert.True(line.AfterLine is null or > 0);
        });
        if (exact)
        {
            Assert.All(value.Lines, line =>
            {
                Assert.Equal("Present", line.Kind);
                Assert.Equal("None", line.Difference);
                Assert.Equal(line.BeforeLine, line.AfterLine);
            });
        }

        if (moved)
        {
            // A moved comment block keeps both of its declaration-relative coordinates, so the
            // browser can render the movement instead of inferring it from an empty row.
            BrowserSourceComparisonLine movedLine =
                value.Lines.First(line => line.Difference == "Moved");
            Assert.NotNull(movedLine.BeforeLine);
            Assert.NotNull(movedLine.AfterLine);
            Assert.NotEqual(movedLine.BeforeLine, movedLine.AfterLine);
            Assert.Equal(movedLine.BeforeText, movedLine.AfterText);
        }

        if (memberName == "MovedBlockAndEdit")
        {
            Assert.Contains(
                value.Lines,
                line => line.Kind != "Present" && line.AfterText?.Contains("+ 1") == true);
        }
    }

    [Fact]
    public async Task UnavailableAfterSourceLeavesTheAvailableDeclarationInspectable()
    {
        await using Pair pair = await Pair.OpenAsync();
        using var host = Host(afterSource: false);

        BrowserSourceComparison value = await pair.CompareThrough(host, "Counter", "Value");

        Assert.Equal("Unavailable", value.Status);
        Assert.False(value.IsExact);
        Assert.Empty(value.Lines);
        Assert.Equal("Available", value.Before.State);
        Assert.Contains("1 + 2", value.Before.Text);
        Assert.StartsWith(SourcePairHost.BeforeSourcePrefix, value.Before.SourceUrl);
        AssertUnresolvedSource(value.After);
        Assert.Contains(
            host.SourceRequests,
            uri => uri.AbsolutePath.Contains("/source-comparison/v2/", StringComparison.Ordinal));
        Assert.NotNull(value.After.MemberIdentity);
        Assert.NotNull(value.After.MetadataToken);
    }

    static void AssertUnresolvedSource(BrowserSourceComparisonEndpoint endpoint)
    {
        Assert.Contains(endpoint.State, (string[])["Unavailable", "Failed"]);
        Assert.NotEmpty(endpoint.Detail!);
        Assert.Null(endpoint.Text);
    }

    static async Task<BrowserSourceComparisonResult> Compare(
        BrowserSourceComparisonRequest request) =>
        Read(await SourceExports.QueryMemberSourceComparison(
            Guid.NewGuid().ToString(), Serialize(request)));

    static string Serialize(BrowserSourceComparisonRequest request) =>
        JsonSerializer.Serialize(
            request, BrowserSourceJsonContext.Default.BrowserSourceComparisonRequest);

    static BrowserSourceComparisonResult Read(string json) =>
        JsonSerializer.Deserialize(
            json, BrowserSourceJsonContext.Default.BrowserSourceComparisonResult)!;

    static BrowserTypeSourceCancellation Cancel(string id, string reason) =>
        JsonSerializer.Deserialize(
            SourceExports.CancelMemberSourceComparison(id, reason),
            BrowserSourceJsonContext.Default.BrowserTypeSourceCancellation)!;

    static ApiSurface Surface(BrowserInspectionScope scope) =>
        scope.UseSurface(group =>
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                AssemblyContextApiSurfaceQuery.ExecuteBounded(
                    group,
                    ApiSurfaceScope.IncludeAll,
                    BrowserApiSurfacePolicy.Limits).Assemblies.Assemblies.Single())
                .Value.Surface);

    /// <summary>
    /// Registers the fixture's own built package, so the browser workspace reads the same
    /// <c>.nupkg</c> a browser acceptance run acquires instead of a test-shaped archive.
    /// </summary>
    static async Task RegisterAsync(
        FixtureDefinition fixture, string version, string? packageId = null) =>
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId ?? SourcePackageId,
                version,
                File.ReadAllBytes(fixture.AssetPath("package")),
                fromCache: false));

    static SourcePairHost Host(bool afterSource = true) =>
        new(
            FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.Old),
            afterSource ? FixtureSource(FixtureCatalog.InspectWebSourceComparisonPair.New) : null);

    static byte[] FixtureSource(FixtureDefinition fixture) =>
        File.ReadAllBytes(Assert.Single(
            fixture.SourcePaths(), path => Path.GetFileName(path) == "Counter.cs"));

    sealed class Pair(string packageId) : IAsyncDisposable
    {
        internal string PackageId => packageId;

        internal static async Task<Pair> OpenAsync()
        {
            await RegisterAsync(
                FixtureCatalog.InspectWebSourceComparisonPair.Old, BeforeVersion);
            await RegisterAsync(
                FixtureCatalog.InspectWebSourceComparisonPair.New, AfterVersion);
            return new(SourcePackageId);
        }

        internal async Task<BrowserSourceComparisonRequest> Request(
            string typeName,
            string memberName,
            string afterVersion = AfterVersion)
        {
            await using BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId, BeforeVersion, Framework);
            ApiType type = Assert.Single(
                Surface(lease.Scope).Types,
                candidate => candidate.FullName == $"SourceDiffFixture.{typeName}");
            ApiMember member = Assert.Single(
                type.Members, candidate => candidate.Name == memberName);
            CallGraphMemberBodySelector body = Assert.Single(
                CallGraphMemberResolver.CreateBodySelectors(type, member));
            return new(
                packageId,
                BeforeVersion,
                afterVersion,
                Framework,
                AssemblyName,
                type.DefinitionName!.ToEscapedFullName(),
                body.MemberName,
                body.SelectorKey,
                body.BodyToken);
        }

        /// <summary>
        /// Runs the export's own resolution, leasing, and paired query over both registered
        /// versions, substituting only the Source transport so the fixtures' controlled SourceLink
        /// map is reachable offline.
        /// </summary>
        internal async Task<BrowserSourceComparison> CompareThrough(
            SourcePairHost host,
            string typeName,
            string memberName)
        {
            BrowserSourceComparisonRequest request = await Request(typeName, memberName);
            await using BrowserMemberResolution.ScopedResolution before =
                await BrowserMemberResolution.ImplementationMemberAsync(
                    request.PackageId, request.BeforeVersion, request.Framework,
                    request.Assembly, request.TypeIdentity, request.MemberName,
                    request.SelectorKey, request.MetadataToken,
                    TestContext.Current.CancellationToken);
            AssemblyMemberSourceRequest selected = AssemblyMemberSourceRequest.From(
                before.Member.Type, before.Member.Member);
            await using BrowserScopeLease<BrowserInspectionScope> afterLease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    request.PackageId, request.AfterVersion, request.Framework,
                    TestContext.Current.CancellationToken);
            BrowserInspectionScope afterScope = afterLease.Scope;
            BrowserPackageCoordinate afterCoordinate = afterScope.Coordinates[0];
            BrowserWorkspaceParticipant after = afterScope.ImplementationParticipant(
                afterScope.SurfaceParticipant(
                    afterCoordinate, afterCoordinate.CompileAsset(request.Assembly)));
            AssemblyMemberSourcePairResult pair = await before.Scope.UseImplementationParticipant(
                before.ImplementationParticipant,
                (beforeGroup, beforeParticipant) => afterScope.UseImplementationParticipant(
                    after,
                    (afterGroup, afterParticipant) =>
                        AssemblyContextMemberSourcePairQuery.ExecuteAsync(
                            beforeGroup, beforeParticipant, afterGroup, afterParticipant,
                            new(selected.Type, selected.Member), host.Context,
                            TestContext.Current.CancellationToken)));
            return BrowserSourceComparisonProjection.Project(
                request, pair, before.ImplementationParticipant, after);
        }

        internal async Task AssertScopesReleased(params string[] versions)
        {
            foreach (string version in versions.Length == 0
                ? [BeforeVersion, AfterVersion]
                : versions)
            {
                await using BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        packageId, version, Framework);
                BrowserInspectionScope scope = lease.Scope;
                await BrowserPackageWorkspace.RemoveScopeAsync(scope);
                await lease.DisposeAsync();
                Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string version in (string[])[BeforeVersion, AfterVersion])
            {
                await using BrowserScopeLease<BrowserInspectionScope> lease =
                    await BrowserPackageWorkspace.OpenScopeAsync(
                        packageId, version, Framework);
                await BrowserPackageWorkspace.RemoveScopeAsync(lease.Scope);
            }
        }
    }

    /// <summary>
    /// Substitutes only the HTTP transport. The production browser fetch policy still decides
    /// which source requests may leave, and the PDB still comes from the inspected image.
    /// </summary>
    sealed class SourcePairHost : IDisposable
    {
        internal const string BeforeSourcePrefix =
            "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/v1/";

        internal const string AfterSourcePrefix =
            "https://raw.githubusercontent.com/dotnet-inspect-fixtures/source-comparison/v2/";

        readonly HttpClient _symbolClient;
        readonly HttpClient _sourceClient;
        readonly List<Uri> _symbolRequests = [];
        readonly List<Uri> _sourceRequests = [];

        internal SourcePairHost(byte[] beforeSource, byte[]? afterSource)
        {
            _symbolClient = new HttpClient(new ContentHandler(uri =>
            {
                lock (_symbolRequests)
                    _symbolRequests.Add(uri);
                return null;
            }));
            _sourceClient = new HttpClient(new ContentHandler(uri =>
            {
                lock (_sourceRequests)
                    _sourceRequests.Add(uri);
                return uri.AbsolutePath.Contains("/source-comparison/v2/", StringComparison.Ordinal)
                    ? afterSource
                    : beforeSource;
            }));
            Context = new AssemblyContextSourceQueryContext(
                _symbolClient,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                new SourceFetcher(
                    _sourceClient,
                    new InMemorySourceContentStore(),
                    BrowserSourceFetchPolicy.Instance));
        }

        internal AssemblyContextSourceQueryContext Context { get; }

        /// <summary>Every symbol-server request, which an embedded PDB should never need.</summary>
        internal IReadOnlyList<Uri> SymbolRequests
        {
            get
            {
                lock (_symbolRequests)
                    return [.. _symbolRequests];
            }
        }

        /// <summary>Every source request the production fetch policy allowed to leave.</summary>
        internal IReadOnlyList<Uri> SourceRequests
        {
            get
            {
                lock (_sourceRequests)
                    return [.. _sourceRequests];
            }
        }

        public void Dispose()
        {
            _symbolClient.Dispose();
            _sourceClient.Dispose();
        }

        sealed class ContentHandler(Func<Uri, byte[]?> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? content = response(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(
                    content is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                {
                    Content = content is null ? null : new ByteArrayContent(content),
                    RequestMessage = request,
                });
            }
        }
    }
}
