using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.Metadata;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Library API diff operations",
    DisableParallelization = true)]
public sealed class BrowserLibraryApiDiffOperationCollection;

[Collection("Library API diff operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryApiDiffOperationTests
{
    const string Framework = "net11.0";
    const string TargetVersion = "1.0.0";
    const string CurrentVersion = "2.0.0";
    const string AssemblyName = "LibraryApiDiffFixture.dll";

    [Fact]
    public async Task ExportProjectsCompleteProducerOrderedChangedTypeInventory()
    {
        await using Fixture fixture = await Fixture.Open();

        BrowserLibraryApiDiffResult result =
            await fixture.Query(fixture.Request());

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        Assert.Null(result.Unavailable);
        Assert.Null(result.Rejected);
        Assert.Null(result.FailureKind);
        BrowserLibraryApiDiffSucceeded value =
            Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);
        Assert.Equal("LibraryApiDiffFixture", value.LibraryDisplay);
        Assert.Equal(TargetVersion, value.Target.Version);
        Assert.Equal(CurrentVersion, value.Current.Version);
        Assert.Equal(
            BrowserLibraryApiDiffSurfaceScope.Public,
            value.Target.Scope);
        Assert.Equal(
            BrowserLibraryApiDiffSurfaceScope.Public,
            value.Current.Scope);
        Assert.Equal(fixture.CompileAssetId, value.Target.Asset.Id);
        Assert.Equal(fixture.CompileAssetId, value.Current.Asset.Id);
        Assert.Equal(
            "lib/net11.0/LibraryApiDiffFixture.dll",
            value.Target.Asset.Path);
        Assert.Empty(value.Target.Issues);
        Assert.Empty(value.Current.Issues);
        Assert.Equal(value.Aggregate.ChangedTypeCount, value.Types.Length);
        Assert.Equal(
            value.Types.Select(type => type.DocumentIdentifier),
            value.Types.Select(type =>
                type.After?.Identifier ?? type.Before!.Identifier));

        BrowserLibraryApiDiffType removed = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.RemovedType");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Deletion,
            removed.State);
        Assert.Equal(3, removed.ChangedMemberCount);
        Assert.Equal(1, removed.BreakingCount);
        Assert.Null(removed.TypeDefinitionChanged);
        Assert.Null(removed.After);
        Assert.Equal(
            "LibraryApiDiffFixture.RemovedType",
            removed.Before!.Identifier);
        Assert.Equal("LibraryApiDiffFixture", removed.Before.Namespace);
        Assert.Equal(["RemovedType"], removed.Before.Segments);

        BrowserLibraryApiDiffType added = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.AddedType");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Addition,
            added.State);
        Assert.Equal(3, added.ChangedMemberCount);
        Assert.Equal(1, added.AdditiveCount);
        Assert.Null(added.TypeDefinitionChanged);
        Assert.Null(added.Before);
        Assert.Equal(
            "LibraryApiDiffFixture.AddedType",
            added.After!.Identifier);

        BrowserLibraryApiDiffType definitionOnly = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.TypeDefinitionOnly");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Diff,
            definitionOnly.State);
        Assert.True(definitionOnly.TypeDefinitionChanged);
        Assert.Equal(0, definitionOnly.ChangedMemberCount);
        Assert.NotNull(definitionOnly.Before);
        Assert.NotNull(definitionOnly.After);

        BrowserLibraryApiDiffType receiver = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.ProjectionReceiver");
        BrowserLibraryApiDiffType extensions = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.ProjectionExtensions");
        Assert.Equal(2, receiver.ChangedMemberCount);
        Assert.Equal(1, extensions.ChangedMemberCount);
        Assert.Equal(
            new BrowserLibraryApiDiffAggregate(7, 1, 1, 10, 4, 2, 0),
            value.Aggregate);
    }

    [Fact]
    public async Task SameVersionIsSuccessfulAndEmpty()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request =
            fixture.Request(
                currentVersion: TargetVersion,
                targetVersion: TargetVersion);

        BrowserLibraryApiDiffResult result =
            await fixture.Query(request);

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        BrowserLibraryApiDiffSucceeded value =
            Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);
        Assert.Equal(TargetVersion, value.Target.Version);
        Assert.Equal(TargetVersion, value.Current.Version);
        Assert.Empty(value.Types);
        Assert.Equal(
            new BrowserLibraryApiDiffAggregate(0, 0, 0, 0, 0, 0, 0),
            value.Aggregate);
    }

    [Fact]
    public void IncompleteEndpointRemainsTypedUnavailable()
    {
        BrowserLibraryApiDiffRequest request = Request("Unavailable.Package");
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var truncation = new ApiSurfaceProjectionTruncation(
            ApiSurfaceProjectionLimit.Types,
            Bound: 1,
            ProjectedParticipants: 1,
            OmittedParticipants: 0,
            ProjectedTypes: 1,
            ProjectedMembers: 0,
            ProjectedInspectionFailures: 0,
            ProjectedTypeForwarders: 0,
            InspectedMetadataRows: 12,
            ProjectedRetainedTextCharacters: 24);
        var target = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: false,
            [new LibraryApiDiffEndpointIssue.Truncated(truncation)]);
        var current = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        var unavailable = new LibraryApiDiffPresentationResult.Unavailable(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            target,
            current);

        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                unavailable,
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Unavailable,
            result.Kind);
        Assert.Null(result.Value);
        Assert.Null(result.Rejected);
        BrowserLibraryApiDiffUnavailable wire =
            Assert.IsType<BrowserLibraryApiDiffUnavailable>(
                result.Unavailable);
        Assert.Equal(
            BrowserLibraryApiDiffUnavailableKind.TargetIncomplete,
            wire.Kind);
        Assert.False(wire.Target.IsComplete);
        Assert.True(wire.Current.IsComplete);
        BrowserLibraryApiDiffEndpointIssue issue =
            Assert.Single(wire.Target.Issues);
        Assert.Equal(
            BrowserLibraryApiDiffEndpointIssueKind.Truncated,
            issue.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffProjectionLimit.Types,
            issue.Truncation!.Limit);
        Assert.Equal(1, issue.Truncation.Bound);
        Assert.Empty(wire.Current.Issues);
    }

    [Fact]
    public async Task ExactCompileAssetMismatchFailsWithoutNameFallback()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request =
            fixture.Request() with
            {
                CompileAssetId =
                    "compile:lib/net11.0/NotTheSelectedAsset.dll",
            };

        BrowserLibraryApiDiffResult result =
            await fixture.Query(request);

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Failed,
            result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffFailureKind.Expected,
            result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains("exact compile asset", result.Error);
        Assert.Contains("target package endpoint", result.Error);
    }

    [Fact]
    public void ChangedTypeTransportBoundaryIsAtomic()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult admitted =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(BrowserLibraryApiDiffWireProjection.MaxChangedTypes),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            admitted.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxChangedTypes,
            admitted.Value!.Types.Length);

        BrowserLibraryApiDiffResult rejected =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(
                    BrowserLibraryApiDiffWireProjection.MaxChangedTypes + 1),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Rejected,
            rejected.Kind);
        Assert.Null(rejected.Value);
        Assert.Null(rejected.Unavailable);
        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(
                rejected.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind
                .ChangedTypeCountLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxChangedTypes,
            evidence.Bound);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxChangedTypes + 1,
            evidence.Observed);
    }

    [Fact]
    public void TypeTextTransportLimitRejectsTheWholeInventory()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(2_000, new string('x', 2_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Rejected,
            result.Kind);
        Assert.Null(result.Value);
        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(result.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.TypeTextLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxTypeTextCharacters,
            evidence.Bound);
        Assert.True(
            evidence.Observed
                > BrowserLibraryApiDiffWireProjection.MaxTypeTextCharacters);
    }

    [Fact]
    public async Task CancellationWinsOverLateCompletionAndOldIdCannotCancelSuccessor()
    {
        BrowserLibraryApiDiffRequest request = Request("Bridge.Package");
        var firstStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        string firstId = Guid.NewGuid().ToString();
        Task<BrowserLibraryApiDiffResult> first =
            MetadataExports.RunLibraryApiDiffOperationAsync(
                BrowserManagedOperationId.From(firstId),
                () => request,
                async _ =>
                {
                    firstStarted.SetResult();
                    await firstRelease.Task;
                    return new BrowserManagedOperationBodyResult<
                        BrowserLibraryApiDiffResult,
                        string,
                        string>.Succeeded(
                            ProjectedAvailable(request));
                });
        await firstStarted.Task;

        BrowserLibraryApiDiffCancellation cancellation =
            Cancel(firstId, "superseded");
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.Requested,
            cancellation.Kind);
        firstRelease.SetResult();
        BrowserLibraryApiDiffResult canceled = await first;
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Canceled,
            canceled.Kind);
        Assert.Equal("superseded", canceled.Reason);
        Assert.Null(canceled.Value);

        var secondStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        string secondId = Guid.NewGuid().ToString();
        Task<BrowserLibraryApiDiffResult> second =
            MetadataExports.RunLibraryApiDiffOperationAsync(
                BrowserManagedOperationId.From(secondId),
                () => request,
                async _ =>
                {
                    secondStarted.SetResult();
                    await secondRelease.Task;
                    return new BrowserManagedOperationBodyResult<
                        BrowserLibraryApiDiffResult,
                        string,
                        string>.Succeeded(
                            ProjectedAvailable(request));
                });
        await secondStarted.Task;

        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(firstId, "user").Kind);
        Assert.False(second.IsCompleted);
        secondRelease.SetResult();
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            (await second).Kind);
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(secondId, "user").Kind);
    }

    [Fact]
    public async Task SourceGeneratedJsonRoundTripsTheClosedInventoryShape()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request = fixture.Request();
        string requestJson = JsonSerializer.Serialize(
            request,
            BrowserMetadataJsonContext.Default
                .BrowserLibraryApiDiffRequest);
        Assert.Equal(
            request,
            JsonSerializer.Deserialize(
                requestJson,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest));

        string json = await MetadataExports.QueryLibraryApiDiff(
            Guid.NewGuid().ToString(),
            requestJson);
        BrowserLibraryApiDiffResult result =
            JsonSerializer.Deserialize(
                json,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)!;
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("Succeeded", root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        JsonElement row = root
            .GetProperty("value")
            .GetProperty("types")[0];
        Assert.Equal(
            [
                "additiveCount",
                "after",
                "before",
                "breakingCount",
                "changedMemberCount",
                "display",
                "documentIdentifier",
                "potentiallyBreakingCount",
                "state",
                "typeDefinitionChanged",
            ],
            row.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(row.TryGetProperty("members", out _));
        Assert.False(row.TryGetProperty("selectedType", out _));
    }

    static BrowserLibraryApiDiffCancellation Cancel(
        string operationId,
        string reason) =>
        JsonSerializer.Deserialize(
            MetadataExports.CancelLibraryApiDiff(operationId, reason),
            BrowserMetadataJsonContext.Default
                .BrowserLibraryApiDiffCancellation)!;

    static BrowserLibraryApiDiffResult ProjectedAvailable(
        BrowserLibraryApiDiffRequest request) =>
        BrowserLibraryApiDiffWireProjection.Project(
            request,
            Available(0),
            EndpointContext(request.TargetVersion),
            EndpointContext(request.CurrentVersion));

    static LibraryApiDiffPresentationResult.Available Available(
        int typeCount,
        string? display = null)
    {
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var endpoint = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        ImmutableArray<ComparisonSubject<LibraryApiTypeDiff>> subjects =
        [
            .. Enumerable.Range(0, typeCount)
                .Select(index =>
                {
                    MetadataTypeDefinitionName name = TypeName($"Type{index}");
                    var typeIdentity = new LibraryApiTypeIdentity(
                        name,
                        display ?? $"Transport.Type{index}");
                    var diff = new LibraryApiTypeDiff(
                        typeIdentity,
                        typeIdentity,
                        LibraryApiTypePairKind.Present,
                        TypeDefinitionChanged: false,
                        [],
                        []);
                    return new ComparisonSubject<LibraryApiTypeDiff>(
                        typeIdentity.Identifier,
                        display ?? typeIdentity.Display,
                        new ComparisonSubjectChange.Diff(),
                        diff);
                }),
        ];
        var document = new ComparisonDocument<LibraryApiTypeDiff>(
            ComparisonDocument<LibraryApiTypeDiff>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            "transport-library",
            "Transport",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<
                LibraryApiTypeDiff>.NotApplicable(),
            subjects,
            []);
        return new LibraryApiDiffPresentationResult.Available(
            endpoint,
            endpoint,
            new LibraryApiDiffSummary(
                typeCount,
                AddedTypeCount: 0,
                RemovedTypeCount: 0,
                ChangedMemberCount: 0,
                BreakingCount: 0,
                AdditiveCount: 0,
                PotentiallyBreakingCount: 0),
            document);
    }

    static MetadataTypeDefinitionName TypeName(string segment) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "Transport",
                [segment])).Name;

    static BrowserLibraryApiDiffRequest Request(string packageId) =>
        new(
            1,
            packageId,
            CurrentVersion,
            TargetVersion,
            Framework,
            "compile:lib/net11.0/LibraryApiDiffFixture.dll");

    static BrowserLibraryApiDiffEndpointContext EndpointContext(
        string version) =>
        new(
            "Projection.Package",
            version,
            Framework,
            "compile:lib/net11.0/LibraryApiDiffFixture.dll",
            "lib/net11.0/LibraryApiDiffFixture.dll",
            AssemblyName);

    static AssemblyReferenceIdentity AssemblyIdentity() =>
        new(
            "LibraryApiDiffFixture",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);

    sealed class Fixture : IAsyncDisposable
    {
        readonly BrowserInspectionScope _targetScope;
        readonly BrowserInspectionScope _currentScope;

        Fixture(
            string packageId,
            string compileAssetId,
            BrowserInspectionScope targetScope,
            BrowserInspectionScope currentScope)
        {
            PackageId = packageId;
            CompileAssetId = compileAssetId;
            _targetScope = targetScope;
            _currentScope = currentScope;
        }

        internal string PackageId { get; }
        internal string CompileAssetId { get; }

        internal static async Task<Fixture> Open()
        {
            string packageId =
                "Library.Api.Diff." + Guid.NewGuid().ToString("N");
            await Register(
                packageId,
                TargetVersion,
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath());
            await Register(
                packageId,
                CurrentVersion,
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath());

            BrowserInspectionScope targetScope;
            await using (BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId,
                    TargetVersion,
                    Framework,
                    TestContext.Current.CancellationToken))
            {
                targetScope = lease.Scope;
            }
            BrowserInspectionScope currentScope;
            await using (BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId,
                    CurrentVersion,
                    Framework,
                    TestContext.Current.CancellationToken))
            {
                currentScope = lease.Scope;
            }

            string targetAsset =
                Assert.IsType<DotnetInspector.Packages.PackageCompileAsset>(
                    targetScope.Coordinates[0].DefaultAsset).Id;
            string currentAsset =
                Assert.IsType<DotnetInspector.Packages.PackageCompileAsset>(
                    currentScope.Coordinates[0].DefaultAsset).Id;
            Assert.Equal(targetAsset, currentAsset);
            return new Fixture(
                packageId,
                currentAsset,
                targetScope,
                currentScope);
        }

        internal BrowserLibraryApiDiffRequest Request(
            string currentVersion = CurrentVersion,
            string targetVersion = TargetVersion) =>
            new(
                1,
                PackageId,
                currentVersion,
                targetVersion,
                Framework,
                CompileAssetId);

        internal async Task<BrowserLibraryApiDiffResult> Query(
            BrowserLibraryApiDiffRequest request)
        {
            string requestJson = JsonSerializer.Serialize(
                request,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest);
            string resultJson = await MetadataExports.QueryLibraryApiDiff(
                Guid.NewGuid().ToString(),
                requestJson);
            return JsonSerializer.Deserialize(
                resultJson,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)!;
        }

        public async ValueTask DisposeAsync()
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(_targetScope);
            if (!ReferenceEquals(_targetScope, _currentScope))
            {
                await BrowserPackageWorkspace.RemoveScopeAsync(
                    _currentScope);
            }
        }

        static async Task Register(
            string packageId,
            string version,
            string assemblyPath) =>
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
                new BrowserPackage(
                    packageId,
                    version,
                    Archive(
                        packageId,
                        version,
                        File.ReadAllBytes(assemblyPath)),
                    fromCache: false));

        static byte[] Archive(
            string packageId,
            string version,
            byte[] assembly)
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
                            $"<package><metadata><id>{packageId}</id>"
                                + $"<version>{version}</version>"
                                + "<authors>Tests</authors>"
                                + "<description>Library API diff fixture"
                                + "</description></metadata></package>"));
                }
                using Stream library = archive.CreateEntry(
                        "lib/net11.0/LibraryApiDiffFixture.dll")
                    .Open();
                library.Write(assembly);
            }
            return buffer.ToArray();
        }
    }
}
