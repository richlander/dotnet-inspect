using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Fixtures;
using InspectWeb.Engine.MetadataFacade;

namespace InspectWeb.Engine.Tests;

[CollectionDefinition(
    "Library API diff operations",
    DisableParallelization = true)]
public sealed class BrowserLibraryApiDiffOperationCollection;

/// <summary>
/// Managed outcome cases for the Library API Diff export (#6423): one page-keyed
/// <c>QueryLibraryApiDiff</c> operation and its matching keyed <c>CancelLibraryApiDiff</c>
/// cancellation. Before is the comparison target; After is the currently inspected Library.
/// </summary>
/// <remarks>
/// <para>
/// Every case that needs a package registers the real, compiler-produced
/// <c>fixtures/presentation/LibraryApiDiff.V1</c>/<c>V2</c> assemblies — the same fixture pair
/// <c>LibraryApiDiffPresentationTests</c> exercises directly — wrapped in a minimal in-memory
/// <c>.nupkg</c> archive so the browser package workspace can acquire them exactly as it would any
/// other registered package. Nothing here reconstructs a changed-Type count, a compatibility
/// change, or a member relation: the assertions read values the shared
/// <c>LibraryApiDiffPresentationAdapter</c> and its wire projection already produced.
/// </para>
/// <para>
/// Every case reuses one fixed package ID and four fixed version slots (bounded by the browser
/// package cache's 12-entry ceiling, shared process-wide across every test class) instead of
/// minting a fresh package per test.
/// </para>
/// </remarks>
[Collection("Library API diff operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryApiDiffOperationTests
{
    const string Framework = "net11.0";
    const string AssemblyFileName = "LibraryApiDiffFixture.dll";
    const string PackageId = "InspectWeb.LibraryApiDiffFixture";
    const string BeforeVersion = "1.0.0";
    const string AfterVersion = "2.0.0";
    const string LayoutChangedVersion = "2.0.0-ref-layout";
    const string CorruptVersion = "0.0.0-corrupt";

    [Fact]
    public async Task ChangedInventory_ReflectsTheRealFixturePairAndReleasesBothScopes()
    {
        await RegisterFixtureAsync(BeforeVersion, FixtureCatalog.LibraryApiDiffV1);
        await RegisterFixtureAsync(AfterVersion, FixtureCatalog.LibraryApiDiffV2);

        var request = new BrowserLibraryApiDiffRequest(
            PackageId, AfterVersion, Framework, BeforeVersion, AssemblyFileName);
        BrowserLibraryApiDiffResult result = await Query(request);

        Assert.Equal(1, result.Version);
        Assert.Equal(BrowserLibraryApiDiffResultKind.Succeeded, result.Kind);
        Assert.Null(result.FailureKind);
        Assert.Null(result.Reason);
        BrowserLibraryApiDiff value = Assert.IsType<BrowserLibraryApiDiff>(result.Value);
        Assert.Equal(request, value.Request);
        Assert.Equal(BrowserLibraryApiDiffPresentationKind.Available, value.Kind);
        Assert.Null(value.UnavailableKind);
        Assert.Null(value.RejectionKind);

        // Independent per-scope resolution: the same requested assembly name resolves to two
        // distinct assembly identities (distinct CLR assembly versions), never a cached or reused
        // Before/After token or asset.
        Assert.True(value.Before.IsComplete);
        Assert.True(value.After.IsComplete);
        Assert.NotEqual(value.Before.Identity, value.After.Identity);
        Assert.NotEqual(value.Before.Identity.Version, value.After.Identity.Version);
        Assert.Empty(value.Before.Issues);
        Assert.Empty(value.After.Issues);

        BrowserLibraryApiDiffSummary summary =
            Assert.IsType<BrowserLibraryApiDiffSummary>(value.Summary);
        BrowserLibraryApiDiffDocument document =
            Assert.IsType<BrowserLibraryApiDiffDocument>(value.Document);
        Assert.Equal(summary.ChangedTypeCount, document.Subjects.Length);
        Assert.True(summary.BreakingCount > 0);
        Assert.True(summary.AdditiveCount > 0);

        BrowserLibraryApiTypeSubject removed = Assert.Single(
            document.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.RemovedType");
        Assert.Equal(BrowserLibraryApiSubjectChangeKind.Deletion, removed.Change);
        Assert.Equal(BrowserLibraryApiTypePairKind.Removed, removed.TypeDiff.PairKind);
        Assert.Null(removed.TypeDiff.After);
        BrowserLibraryApiCompatibilityChange removedTypeChange =
            Assert.Single(removed.TypeDiff.CompatibilityChanges);
        Assert.Equal(BrowserLibraryApiChangeKind.TypeRemoved, removedTypeChange.Kind);
        Assert.Equal(
            BrowserLibraryApiChangeClassification.Breaking, removedTypeChange.Classification);

        BrowserLibraryApiTypeSubject added = Assert.Single(
            document.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.AddedType");
        Assert.Equal(BrowserLibraryApiSubjectChangeKind.Addition, added.Change);
        Assert.Equal(BrowserLibraryApiTypePairKind.Added, added.TypeDiff.PairKind);
        Assert.Null(added.TypeDiff.Before);
        BrowserLibraryApiCompatibilityChange addedTypeChange =
            Assert.Single(added.TypeDiff.CompatibilityChanges);
        Assert.Equal(BrowserLibraryApiChangeKind.TypeAdded, addedTypeChange.Kind);
        Assert.Equal(
            BrowserLibraryApiChangeClassification.Additive, addedTypeChange.Classification);

        await AssertScopesReleasedAsync(BeforeVersion, AfterVersion);
    }

    [Fact]
    public async Task SameVersionPair_ProducesASuccessfulEmptyDocument()
    {
        await RegisterFixtureAsync(BeforeVersion, FixtureCatalog.LibraryApiDiffV1);

        var request = new BrowserLibraryApiDiffRequest(
            PackageId, BeforeVersion, Framework, BeforeVersion, AssemblyFileName);
        BrowserLibraryApiDiffResult result = await Query(request);

        Assert.Equal(BrowserLibraryApiDiffResultKind.Succeeded, result.Kind);
        BrowserLibraryApiDiff value = Assert.IsType<BrowserLibraryApiDiff>(result.Value);
        Assert.Equal(BrowserLibraryApiDiffPresentationKind.Available, value.Kind);
        BrowserLibraryApiDiffDocument document =
            Assert.IsType<BrowserLibraryApiDiffDocument>(value.Document);
        Assert.Empty(document.Subjects);
        BrowserLibraryApiDiffSummary summary =
            Assert.IsType<BrowserLibraryApiDiffSummary>(value.Summary);
        Assert.Equal(0, summary.ChangedTypeCount);
        Assert.Equal(0, summary.ChangedMemberCount);
        // A successful empty document is a distinct outcome from Unavailable/Rejected.
        Assert.Null(value.UnavailableKind);
        Assert.Null(value.RejectionKind);

        await AssertScopesReleasedAsync(BeforeVersion);
    }

    [Fact]
    public async Task CurrentRefAsset_ResolvesTheEarlierLogicalLibraryAcrossLayoutChanges()
    {
        await RegisterFixtureAsync(BeforeVersion, FixtureCatalog.LibraryApiDiffV1);
        await RegisterFixtureAsync(
            LayoutChangedVersion, FixtureCatalog.LibraryApiDiffV2, includeReferenceAsset: true);

        var request = new BrowserLibraryApiDiffRequest(
            PackageId,
            LayoutChangedVersion,
            Framework,
            BeforeVersion,
            $"compile:ref/{Framework}/{AssemblyFileName}");
        BrowserLibraryApiDiffResult result = await Query(request);

        Assert.Equal(BrowserLibraryApiDiffResultKind.Succeeded, result.Kind);
        BrowserLibraryApiDiff value = Assert.IsType<BrowserLibraryApiDiff>(result.Value);
        Assert.Equal(BrowserLibraryApiDiffPresentationKind.Available, value.Kind);
        Assert.Equal("LibraryApiDiffFixture", value.Before.Identity.Name);
        Assert.Equal("LibraryApiDiffFixture", value.After.Identity.Name);
        Assert.NotEmpty(Assert.IsType<BrowserLibraryApiDiffDocument>(value.Document).Subjects);

        await AssertScopesReleasedAsync(BeforeVersion, LayoutChangedVersion);
    }

    [Fact]
    public async Task MalformedRequestIsAnExpectedFailureThatReleasesTheOperation()
    {
        string id = Guid.NewGuid().ToString();
        BrowserLibraryApiDiffResult result = Read(
            await MetadataExports.QueryLibraryApiDiff(id, "null"));

        Assert.Equal(BrowserLibraryApiDiffResultKind.Failed, result.Kind);
        Assert.Equal(BrowserLibraryApiDiffFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains("request is required", result.Diagnostic);
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(id, "user").Kind);
    }

    [Fact]
    public async Task UnresolvableLibraryIsAnExpectedFailure()
    {
        await RegisterFixtureAsync(BeforeVersion, FixtureCatalog.LibraryApiDiffV1);
        await RegisterFixtureAsync(AfterVersion, FixtureCatalog.LibraryApiDiffV2);

        var request = new BrowserLibraryApiDiffRequest(
            PackageId, AfterVersion, Framework, BeforeVersion, "NoSuchAssembly.dll");
        BrowserLibraryApiDiffResult result = await Query(request);

        Assert.Equal(BrowserLibraryApiDiffResultKind.Failed, result.Kind);
        Assert.Equal(BrowserLibraryApiDiffFailureKind.Expected, result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains("NoSuchAssembly.dll", result.Error);

        await AssertScopesReleasedAsync(BeforeVersion, AfterVersion);
    }

    [Fact]
    public async Task CancellationDuringResolutionIsVisibleAndPublishesNoPartialDocument()
    {
        // A syntactically valid but never-cataloged package ID forces a real network resolution
        // attempt for the comparison (Before) endpoint, which cannot complete synchronously. The
        // managed operation is registered before that attempt starts, so requesting cancellation
        // immediately after starting the query — with no intervening await — reliably observes it
        // as active.
        string id = Guid.NewGuid().ToString();
        var request = new BrowserLibraryApiDiffRequest(
            "DotnetInspect.NonexistentLibraryApiDiffFixture." + Guid.NewGuid().ToString("N"),
            BeforeVersion,
            Framework,
            BeforeVersion,
            AssemblyFileName);

        Task<string> pending = MetadataExports.QueryLibraryApiDiff(id, Serialize(request));
        Assert.False(pending.IsCompleted);
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.Requested,
            Cancel(id, "superseded").Kind);

        BrowserLibraryApiDiffResult result = Read(await pending);
        Assert.Equal(BrowserLibraryApiDiffResultKind.Canceled, result.Kind);
        Assert.Equal("superseded", result.Reason);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
        Assert.Null(result.FailureKind);
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(id, "user").Kind);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesTheClosedContractShape()
    {
        var request = new BrowserLibraryApiDiffRequest(
            "Sample.Package", "2.0.0", Framework, "1.0.0", AssemblyFileName);
        var beforeIdentity = new BrowserLibraryApiDiffAssemblyIdentity(
            "Sample.Package", "1.0.0.0", null, null);
        var afterIdentity = new BrowserLibraryApiDiffAssemblyIdentity(
            "Sample.Package", "2.0.0.0", null, null);
        var beforeType = new BrowserLibraryApiTypeIdentity("Sample.Type", "Sample.Type");
        var afterType = new BrowserLibraryApiTypeIdentity("Sample.Type", "Sample.Type");
        var beforeMember = new BrowserLibraryApiMemberIdentity(
            beforeType,
            new BrowserLibraryApiMemberAnchor("M()", "void M()", "abc123", "Sample.Type", "M"),
            "M()");
        var afterMember = new BrowserLibraryApiMemberIdentity(
            afterType,
            new BrowserLibraryApiMemberAnchor("M()", "void M()", "abc123", "Sample.Type", "M"),
            "M()");
        var relation = new BrowserLibraryApiMemberRelation(
            "library-api-member-relation.v1|Sample.Type.M()|0",
            BrowserLibraryApiMemberPairKind.Changed,
            beforeMember,
            afterMember,
            new BrowserLibraryApiMatchProvenance("extension-instance", 80, 80));
        var compatibilityChange = new BrowserLibraryApiCompatibilityChange(
            BrowserLibraryApiChangeKind.MemberSignatureChanged,
            BrowserLibraryApiChangeClassification.Breaking,
            BrowserLibraryApiChangeCategory.Signature,
            "Signature changed.",
            "void M()",
            "void M(int x)",
            new BrowserLibraryApiChangeSubject(
                BrowserLibraryApiChangeSubjectKind.Member,
                null,
                null,
                beforeMember,
                afterMember));
        var typeDiff = new BrowserLibraryApiTypeDiff(
            beforeType,
            afterType,
            BrowserLibraryApiTypePairKind.Changed,
            true,
            [compatibilityChange],
            [new BrowserLibraryApiMemberDiff(relation, BrowserLibraryApiMemberRelationRole.Both)],
            1,
            0,
            0,
            1);
        var document = new BrowserLibraryApiDiffDocument(
            "library-api.v1|Sample.Package",
            "Sample.Package",
            [
                new BrowserLibraryApiTypeSubject(
                    "Sample.Type", "Sample.Type", BrowserLibraryApiSubjectChangeKind.Diff, typeDiff),
            ]);
        var value = new BrowserLibraryApiDiff(
            request,
            BrowserLibraryApiDiffPresentationKind.Available,
            new BrowserLibraryApiDiffEndpointSummary(
                beforeIdentity, BrowserLibraryApiSurfaceScope.Public, true, []),
            new BrowserLibraryApiDiffEndpointSummary(
                afterIdentity, BrowserLibraryApiSurfaceScope.Public, true, []),
            new BrowserLibraryApiDiffSummary(1, 0, 0, 1, 1, 0, 0),
            document,
            null,
            null);
        var expected = new BrowserLibraryApiDiffResult(
            1, BrowserLibraryApiDiffResultKind.Succeeded, value, null, null, null, null);

        string json = JsonSerializer.Serialize(
            expected, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        BrowserLibraryApiDiffResult roundTripped = Read(json);

        // Record-generated equality compares nested arrays by reference, so a deserialized copy
        // is never `Equals` to the original; `Assert.Equivalent` compares the full object graph
        // structurally instead.
        Assert.Equivalent(expected, roundTripped, strict: true);
        Assert.Contains("\"comparisonVersion\"", json);
        Assert.Contains("\"changedTypeCount\"", json);
        Assert.Contains("\"compatibilityChanges\"", json);
    }

    [Fact]
    public async Task CorruptedEndpointProducesAVisibleUnavailableWithNoDocument()
    {
        await RegisterCorruptAsync(CorruptVersion);
        await RegisterFixtureAsync(AfterVersion, FixtureCatalog.LibraryApiDiffV2);

        var request = new BrowserLibraryApiDiffRequest(
            PackageId, AfterVersion, Framework, CorruptVersion, AssemblyFileName);
        BrowserLibraryApiDiffResult result = await Query(request);

        Assert.Equal(BrowserLibraryApiDiffResultKind.Succeeded, result.Kind);
        BrowserLibraryApiDiff value = Assert.IsType<BrowserLibraryApiDiff>(result.Value);
        Assert.Equal(BrowserLibraryApiDiffPresentationKind.Unavailable, value.Kind);
        Assert.Equal(BrowserLibraryApiDiffUnavailableKind.BeforeIncomplete, value.UnavailableKind);
        Assert.Null(value.RejectionKind);
        Assert.Null(value.Summary);
        Assert.Null(value.Document);

        // The malformed side is visibly incomplete; the well-formed side is untouched by it.
        Assert.False(value.Before.IsComplete);
        Assert.NotEmpty(value.Before.Issues);
        Assert.True(value.After.IsComplete);
        Assert.Empty(value.After.Issues);

        await AssertScopesReleasedAsync(CorruptVersion, AfterVersion);
    }

    static async Task RegisterCorruptAsync(string version)
    {
        byte[] garbage = new byte[4096];
        Random.Shared.NextBytes(garbage);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry($"lib/{Framework}/{AssemblyFileName}").Open();
            entry.Write(garbage);
        }
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(PackageId, version, buffer.ToArray(), fromCache: false));
    }

    static byte[] BuildFixturePackage(
        FixtureDefinition fixture,
        string version,
        bool includeReferenceAsset)
    {
        byte[] image = File.ReadAllBytes(fixture.AssemblyPath());
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream entry = archive
                .CreateEntry($"lib/{Framework}/{AssemblyFileName}")
                .Open())
            {
                entry.Write(image);
            }
            if (includeReferenceAsset)
            {
                using Stream reference = archive
                    .CreateEntry($"ref/{Framework}/{AssemblyFileName}")
                    .Open();
                reference.Write(image);
            }
            using var writer = new StreamWriter(
                archive.CreateEntry("LibraryApiDiff.Fixture.nuspec").Open());
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>LibraryApiDiff.Fixture</id>
                    <version>{version}</version>
                    <authors>dotnet-inspect</authors>
                    <description>Real Library API diff fixture, wrapped for the browser package workspace.</description>
                  </metadata>
                </package>
                """);
        }
        return buffer.ToArray();
    }

    static async Task RegisterFixtureAsync(
        string version,
        FixtureDefinition fixture,
        bool includeReferenceAsset = false) =>
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                PackageId,
                version,
                BuildFixturePackage(fixture, version, includeReferenceAsset),
                fromCache: false));

    static async Task AssertScopesReleasedAsync(params string[] versions)
    {
        foreach (string version in versions)
        {
            await using BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(PackageId, version, Framework);
            BrowserInspectionScope scope = lease.Scope;
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
            await lease.DisposeAsync();
            Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
        }
    }

    static async Task<BrowserLibraryApiDiffResult> Query(BrowserLibraryApiDiffRequest request) =>
        Read(await MetadataExports.QueryLibraryApiDiff(
            Guid.NewGuid().ToString(), Serialize(request)));

    static string Serialize(BrowserLibraryApiDiffRequest request) =>
        JsonSerializer.Serialize(
            request, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffRequest);

    static BrowserLibraryApiDiffResult Read(string json) =>
        JsonSerializer.Deserialize(
            json, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult)!;

    static BrowserLibraryApiDiffCancellation Cancel(string id, string reason) =>
        JsonSerializer.Deserialize(
            MetadataExports.CancelLibraryApiDiff(id, reason),
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffCancellation)!;
}
