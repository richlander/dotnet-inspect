using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageVersionCellMetadataInspectionTests
{
    const string PackageId = "Contoso.Metadata";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    static byte[] Image =>
        File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());

    [Fact]
    public async Task InspectionExecutesOnePreparedCellAndReturnsDetachedMetadata()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));
        var executor = new SettlementExecutor(
            execution => fixture.Realize(execution, content));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Available>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(),
                    executor,
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, executor.Calls);
        Assert.Same(
            executor.Execution!.Request,
            result.Evidence.HouseResult.Request);
        Assert.Same(
            result.Evidence.HouseResult.Evidence.Realization,
            result.Evidence.CompileRealization);
        Assert.NotNull(result.Evidence.RootCoordinate);
        Assert.Equal(PackageId, result.Evidence.PackageId);
        var available = Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                Assert.Single(result.Metadata.Assemblies));
        Assert.StartsWith(
            "v",
            available.Value.MetadataVersion.ToString());
        Assert.Null(result.Cleanup);
        Assert.Null(result.ApiInspection);
    }

    [Fact]
    public async Task InspectionRejectsSettlementForSubstitutedDemand()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));
        PackageVersionCellMetadataInspectionRequest request =
            fixture.Request();
        var executor = new SettlementExecutor(
            _ =>
            {
                PackageHouseRequest substituted =
                    request.Cell.CreateRequest(
                        request.HouseExecution.Request.Operation,
                        request.HouseExecution.Request.TargetContext,
                        request.HouseExecution.Request.AssetSelection,
                        request.HouseExecution.Request.LibraryHandoff);
                return fixture.Realize(substituted, content);
            });

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackageVersionCellMetadataInspector.ExecuteAsync(
                    request,
                    executor,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "does not belong",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, executor.Calls);
        Assert.Same(
            request.HouseExecution.Request.Association,
            executor.Settlement!.Result.Request.Association);
    }

    [Fact]
    public async Task InspectionPreservesNoContributionWithoutCreatingARoot()
    {
        CellFixture fixture = CellFixture.Create();
        PackageVersionCellMetadataInspectionRequest request =
            fixture.Request();
        var executor = new SettlementExecutor(
            execution => new PackageHouseSettlement.ResourceFree(
                new PackageHouseResult.Rejected(
                    new PackageHouseEvidence(execution.Request),
                    Reason("Fixture rejection."))));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.NoContribution>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    request,
                    executor,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageHouseRootNoContributionReason.ResourceFreeSettlement,
            result.Reason);
        Assert.Same(
            request.HouseExecution.Request,
            result.Evidence.HouseResult.Request);
        Assert.Null(result.Evidence.CompileRealization);
        Assert.Null(result.Evidence.RootCoordinate);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task
        InspectionPreservesOwnerDefaultRuntimeIdentifierAsNoContribution()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.NoContribution>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(
                        targetContext:
                            PackageHouseTargetContext.OwnerDefault(
                                "linux-x64")),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageHouseRootNoContributionReason.CoordinateNotRepresentable,
            result.Reason);
        Assert.NotNull(result.Evidence.CompileRealization);
        Assert.Null(result.Evidence.RootCoordinate);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task InspectionPreservesExplicitEmptyCompileSelection()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"ref/{Framework}/_._", []),
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Available>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            result.Evidence.CompileRealization!.Selection.Status);
        Assert.Empty(result.Metadata.Assemblies);
    }

    [Fact]
    public async Task InspectionExecutesPinnedMarkoutPackage()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "VersionCell",
            "Markout.dll");
        byte[] image = File.ReadAllBytes(path);
        CellFixture fixture = CellFixture.Create(
            packageId: "Markout",
            version: "0.35.2");
        IPackageContent content = fixture.Content(
            ("lib/net10.0/Markout.dll", image));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Available>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(framework: "net10.0"),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(233_472, image.Length);
        Assert.Equal(
            "lib/net10.0/Markout.dll",
            Assert.Single(
                result.Evidence.CompileRealization!
                    .Selection.Assets).Path);
        Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                Assert.Single(result.Metadata.Assemblies));
    }

    [Fact]
    public void EvidencePreservesCompileRealizationWithoutRootCoordinate()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));
        PackageVersionCellMetadataInspectionRequest request =
            fixture.Request();
        PackageHouseSettlement settlement = fixture.Realize(
            request.HouseExecution,
            content);
        var compile = Assert.IsType<
            PackageHouseRealizationReceipt.Compile>(
                settlement.Result.Evidence.Realization);

        var evidence =
            new PackageVersionCellExecutionEvidence(
                request.HouseExecution,
                settlement.Result,
                compile,
                rootCoordinate: null);

        Assert.Same(compile, evidence.CompileRealization);
        Assert.Null(evidence.RootCoordinate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionMalformedNeighborRejectsWholeRootWithoutMetadata(
        bool inspectApi)
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image),
            ($"lib/{Framework}/Malformed.dll", [1, 2, 3]));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(apiInspection: inspectApi ? ApiRequest() : null),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
            result.Failure.Stage);
        Assert.NotNull(result.Failure.ArtifactFailure);
        Assert.Null(result.Cleanup);
    }

    [Theory]
    [InlineData(RealizationLimit.Assemblies)]
    [InlineData(RealizationLimit.EntryBytes)]
    [InlineData(RealizationLimit.AggregateBytes)]
    public async Task InspectionEnforcesAssemblyEntryAndAggregateBounds(
        RealizationLimit limit)
    {
        CellFixture fixture = CellFixture.Create();
        byte[] image = Image;
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", image),
            ($"lib/{Framework}/Neighbor.dll", image));
        var limits = limit switch
        {
            RealizationLimit.Assemblies =>
                new PackageVersionCellWorkspaceLimits(
                    1,
                    image.Length,
                    image.Length * 2L),
            RealizationLimit.EntryBytes =>
                new PackageVersionCellWorkspaceLimits(
                    2,
                    image.Length - 1L,
                    image.Length * 2L),
            RealizationLimit.AggregateBytes =>
                new PackageVersionCellWorkspaceLimits(
                    2,
                    image.Length,
                    image.Length * 2L - 1L),
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(limits),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
            result.Failure.Stage);
        Assert.NotNull(result.Failure.ArtifactFailure);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task InspectionPreservesExpiredWorkspaceDeadline()
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));

        var result = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure>(
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                    fixture.Request(
                        deadline:
                            DateTimeOffset.UtcNow.AddSeconds(-1)),
                    new SettlementExecutor(
                        execution => fixture.Realize(
                            execution,
                            content)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
            result.Failure.Stage);
        Assert.Equal(
            WorkspaceScopeRejection.DeadlineExpired,
            result.Failure.Rejection);
        Assert.Null(result.Failure.ArtifactFailure);
    }

    [Fact]
    public void CleanupFailureSupersedesSuccessAndRemainsSecondaryToFailure()
    {
        CellFixture fixture = CellFixture.Create();
        PackageVersionCellExecutionEvidence evidence =
            fixture.ResourceFreeEvidence();
        var cleanup =
            new PackageVersionCellWorkspaceCleanupEvidence(
                [
                    new(
                        PackageVersionCellWorkspaceCleanupStage.GroupRelease,
                        1),
                ]);
        var available =
            new PackageVersionCellMetadataInspectionOutcome.Available(
                evidence,
                new([]));
        var workspaceFailure =
            new PackageVersionCellMetadataInspectionOutcome
                .WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceFailure.Failed(
                        PackageVersionCellMetadataWorkspaceStage.RootQuery,
                        ArtifactRootFailure.Absent));

        var cleanupFailure = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.CleanupFailure>(
                PackageVersionCellMetadataInspector.Complete(
                    available,
                    cleanup));
        var retainedFailure = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure>(
                PackageVersionCellMetadataInspector.Complete(
                    workspaceFailure,
                    cleanup));

        Assert.Same(cleanup, cleanupFailure.Cleanup);
        Assert.Same(workspaceFailure.Failure, retainedFailure.Failure);
        Assert.Same(cleanup, retainedFailure.Cleanup);
    }

    [Fact]
    public void CloseEvidencePreservesDistinctReleaseFailures()
    {
        var report = new InspectionWorkspaceCloseReport(
            [
                new InspectionWorkspaceDirectGroupCloseResult(
                    0,
                    new IOException("group fixture")),
            ],
            [],
            [new IOException("artifact fixture")]);

        ImmutableArray<PackageVersionCellWorkspaceCleanupFailure> failures =
            PackageVersionCellWorkspaceCleanup.Describe(
                    report,
                    scopeCommitted: true,
                    closeFaulted: true)
                .Failures;

        Assert.Equal(
            [
                PackageVersionCellWorkspaceCleanupStage.GroupRelease,
                PackageVersionCellWorkspaceCleanupStage.ArtifactRelease,
                PackageVersionCellWorkspaceCleanupStage.CloseOrchestration,
            ],
            failures.Select(failure => failure.Stage));
        Assert.All(failures, failure => Assert.Equal(1, failure.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionCancellationAfterQueryWaitsForCloseAndPublishesNoOutcome(
        bool inspectApi)
    {
        CellFixture fixture = CellFixture.Create();
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/Contoso.Metadata.dll", Image));
        using var cancellation = new CancellationTokenSource();
        PackageVersionCellMetadataInspectionOutcome? provisional = null;

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageVersionCellMetadataInspector
                    .ExecuteWithProvisionalOutcomeObserverAsync(
                        fixture.Request(apiInspection: inspectApi ? ApiRequest() : null),
                        new SettlementExecutor(
                            execution => fixture.Realize(
                                execution,
                                content)),
                        outcome =>
                        {
                            provisional = outcome;
                            cancellation.Cancel();
                        },
                        cancellation.Token));

        var available = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Available>(
                provisional);
        Assert.Equal(inspectApi, available.ApiInspection is not null);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.False(
            PackageVersionCellWorkspaceExceptionEvidence
                .TryGetCleanup(failure, out _));
    }

    [Fact]
    public void OutcomeClosureIsResourceFree()
    {
        var seen = new HashSet<Type>();
        foreach (Type root in new[]
        {
            typeof(PackageVersionCellMetadataInspectionOutcome),
            typeof(PackageVersionCellExecutionEvidence),
            typeof(PackageVersionCellMetadataWorkspaceFailure),
            typeof(PackageVersionCellWorkspaceCleanupEvidence),
        })
        {
            Visit(root);
        }
        Assert.DoesNotContain(
            typeof(PackageHouseVersionPopulationCell),
            seen);
        Assert.DoesNotContain(typeof(PackageRootBinding), seen);
        Assert.DoesNotContain(typeof(InspectionWorkspace), seen);
        Assert.DoesNotContain(typeof(IPackageContent), seen);
        Assert.DoesNotContain(typeof(Stream), seen);

        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(InertString))
            {
                return;
            }
            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                    Visit(argument);
                return;
            }

            Assert.False(type.IsByRefLike, type.FullName);
            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(Delegate).IsAssignableFrom(type),
                type.FullName);
            if (type.Assembly == typeof(object).Assembly
                || type.Namespace?.StartsWith(
                    "NuGet.",
                    StringComparison.Ordinal) is true)
            {
                return;
            }

            foreach (Type nested in type.GetNestedTypes(
                BindingFlags.Public))
            {
                Visit(nested);
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(field.FieldType);
            }
        }
    }

    static InertString Reason(string value) =>
        new(TextPolicy.Field, value);

    public enum RealizationLimit
    {
        Assemblies,
        EntryBytes,
        AggregateBytes,
    }

    sealed class SettlementExecutor(
        Func<
            PackageHouseVersionPopulationCellExecution,
            PackageHouseSettlement> execute)
        : IPackageHouseVersionPopulationCellExecutor
    {
        public int Calls { get; private set; }

        public List<int> Positions { get; } = [];

        public PackageHouseVersionPopulationCellExecution? Execution
        {
            get;
            private set;
        }

        public PackageHouseSettlement? Settlement { get; private set; }

        public Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseVersionPopulationCellExecution execution,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Positions.Add(execution.Cell.Address.Position);
            Execution = execution;
            Settlement = execute(execution);
            return Task.FromResult(Settlement);
        }
    }

    sealed class CellFixture(
        PackageHouseVersionPopulationCell cell,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source)
    {
        public static CellFixture Create(
            string packageId = PackageId,
            string version = Version) =>
            Assert.Single(CreatePopulation(packageId, version));

        public static ImmutableArray<CellFixture> CreatePopulation(
            string packageId,
            params string[] versions)
        {
            if (versions.Length == 0)
                throw new ArgumentException("At least one Version is required.", nameof(versions));
            var authority = new ConfiguredPackageAuthority(
                PackageSource.NuGetOrg);
            PackageSourceResultFactory results =
                CreateResultFactory(authority.Association);
            var discovery = new PackageVersionDiscoveryResult(
                packageId,
                PackageVersionDiscoveryState.Authoritative,
                [
                    .. versions.Select(version =>
                        new PackageVersionSourceInfo(
                            version,
                            "nuget.org",
                            Listed: true)),
                ],
                failures: [],
                hasAnyCandidate: true,
                candidates:
                [
                    .. versions.Select(version =>
                        new ConfiguredPackageCandidateObservation(
                            authority,
                            results.Candidate(
                                PackageSourceCoordinate.Create(
                                    packageId,
                                    version),
                                PackageDiscoveryContract
                                    .CompleteVersionEnumeration,
                                PackageListingState.Listed))),
                ],
                PackageVersionDiscoveryContract
                    .CompleteVersionEnumeration,
                candidateIssuer: new object());
            Assert.True(
                PackageVersionRange.TryParse(
                    $"{packageId}@{versions[0]}..{versions[^1]}",
                    out PackageVersionRange? range,
                    out string? error),
                error);
            var populationRequest =
                new PackageHouseVersionPopulationRequest(
                    range!,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Settle));
            var population =
                new PackageHouseVersionPopulationResult.Available(
                    new PackageHouseVersionPopulationEvidence(
                        populationRequest,
                        discovery));
            return
            [
                .. population.Vector.Addresses.Select(address =>
                    new CellFixture(
                        population.SelectCell(address),
                        authority,
                        results.Source)),
            ];
        }

        public PackageHouseVersionPopulationCell Cell => cell;

        public string CellVersion =>
            cell.Address.Version.ToNormalizedString();

        public PackageVersionCellAnalysisEndpoint AnalysisEndpoint(
            string framework = Framework,
            PackageHouseTargetContext? targetContext = null) =>
            new(
                cell,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize),
                targetContext
                    ?? PackageHouseTargetContext.Exact(framework));

        public PackageVersionCellMetadataInspectionRequest Request(
            PackageVersionCellWorkspaceLimits? limits = null,
            string framework = Framework,
            DateTimeOffset? deadline = null,
            PackageHouseTargetContext? targetContext = null,
            PackageVersionCellApiInspectionRequest? apiInspection = null) =>
            new(
                cell,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize),
                targetContext
                    ?? PackageHouseTargetContext.Exact(framework),
                limits
                    ?? new PackageVersionCellWorkspaceLimits(
                        16,
                        16_000_000,
                        32_000_000),
                deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
                apiInspection);

        public IPackageContent Content(
            params (string Path, byte[] Bytes)[] entries)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
                buffer,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                foreach ((string path, byte[] bytes) in entries)
                {
                    using Stream entry = archive.CreateEntry(path).Open();
                    entry.Write(bytes);
                }
            }

            return new InMemoryPackageContent(
                buffer.ToArray(),
                fromCache: true,
                source.Producer.Key);
        }

        public IPackageContent PackageContent(byte[] bytes) =>
            new InMemoryPackageContent(
                bytes,
                fromCache: true,
                source.Producer.Key);

        public PackageHouseSettlement Realize(
            PackageHouseVersionPopulationCellExecution execution,
            IPackageContent content) =>
            Realize(execution.Request, content);

        public PackageHouseSettlement Realize(
            PackageHouseRequest request,
            IPackageContent content)
        {
            var demand = Assert.IsType<PackageHouseDemand.Candidate>(
                request.Demand);
            PackageSourceCoordinate coordinate =
                demand.Value.Coordinate;
            PackageHouseDecisionReceipt decision =
                PackageHouseDecisionReceipt.RetainPackage(
                    request,
                    coordinate,
                    demand.Value);
            var initialPayload = new AcquiredPackageSourcePayload(
                coordinate,
                content,
                source.Producer.Key,
                source.Producer,
                PackagePayloadOrigin.Cache);
            var sourcePayload = new ConfiguredPackagePayloadResult(
                authority,
                source,
                initialPayload,
                failures: [],
                reportingAuthorities: [authority],
                selectionUsesOriginalSources: true,
                transfer: PackageTransferReceipt.Cache);
            AcquiredPackageSourcePayload payload =
                Assert.IsType<AcquiredPackageSourcePayload>(
                    sourcePayload.Payload);
            var acquisition = new PackageHouseAcquisitionReceipt(
                decision,
                authority,
                source,
                payload.Origin,
                payload.Content.GenerationIdentity,
                PackageTransferReceipt.Cache);
            PackageCompileAssetSelectionReceipt selection =
                PackageCompileAssetSelector.Evaluate(
                    payload.Content,
                    coordinate.PackageId,
                    request.TargetContext?.RequestedFramework is null
                        ? PackageCompileAssetSelectionPolicy.HighestAvailable
                        : PackageCompileAssetSelectionPolicy.ExplicitTarget,
                    request.TargetContext?.RequestedFramework,
                    request.TargetContext?.RuntimeIdentifier);
            var realization =
                new PackageHouseRealizationReceipt.Compile(
                    acquisition,
                    selection);
            var result = new PackageHouseResult.Settled(
                new PackageHouseEvidence(
                    request,
                    decision,
                    acquisition,
                    realization));
            return new PackageHouseSettlement.Acquired(
                result,
                payload,
                sourcePayload,
                selectionUsesOriginalSources: true);
        }

        public PackageVersionCellExecutionEvidence
            ResourceFreeEvidence()
        {
            PackageVersionCellMetadataInspectionRequest request = Request();
            var result = new PackageHouseResult.Rejected(
                new PackageHouseEvidence(
                    request.HouseExecution.Request),
                Reason("Fixture rejection."));
            return new(
                request.HouseExecution,
                result,
                compileRealization: null,
                rootCoordinate: null);
        }

        static PackageSourceResultFactory CreateResultFactory(
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
                        return new UnusedPackageSource(factory.Source);
                    });
            return Assert.IsType<PackageSourceResultFactory>(captured);
        }
    }

    sealed class UnusedPackageSource(
        PackageSourceResultIdentity source) : IPackageSourceClient
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
}
