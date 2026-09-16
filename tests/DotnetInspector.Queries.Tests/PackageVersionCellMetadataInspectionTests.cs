using System.IO.Compression;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageVersionCellMetadataInspectionTests
{
    private const string PackageId = "Markout";
    private const string Version = "0.35.2";
    private const string Framework = "net11.0";

    [Fact]
    public async Task SuccessfulCell_InspectsExactCompileRootAndClosesWorkspace()
    {
        CellContext context = CreateCell();
        var content = new TrackingPackageContent(
            Archive(
                (
                    $"lib/{Framework}/{PackageId}.dll",
                    File.ReadAllBytes(
                        typeof(PackageVersionCellMetadataInspectionTests)
                            .Assembly.Location))),
            context.Source.Producer.Key);
        var executor = new SettlementExecutor(
            context,
            _ => content);

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                Request(context.Cell),
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                outcome);
        var inspected = Assert.IsType<
            PackageVersionCellMetadataInspectionResult.Inspected>(
                completed.Result);
        Assert.Same(context.Cell, inspected.Cell);
        Assert.Same(executor.Settlement!.Result, inspected.HouseResult);
        Assert.IsType<AssemblyContextEntry<MetadataImageOverview>.Available>(
            Assert.Single(inspected.Metadata.Assemblies));
        Assert.Equal(1, executor.ExecutionCount);
        Assert.Equal(0, content.ActiveStreams);
        Assert.True(content.OpenedStreams > 0);
    }

    [Fact]
    public async Task RootOnlyCell_ReturnsNoAssemblyContext()
    {
        CellContext context = CreateCell();
        var executor = new SettlementExecutor(
            context,
            _ => new InMemoryPackageContent(
                Archive(("tools/net11.0/any/markout.dll", [1])),
                fromCache: false,
                context.Source.Producer.Key));

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                Request(context.Cell),
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                outcome);
        var unavailable = Assert.IsType<
            PackageVersionCellMetadataInspectionResult.NoAssemblyContext>(
                completed.Result);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            unavailable.SelectionStatus);
    }

    [Fact]
    public async Task HouseFailure_RemainsTypedWithoutWorkspaceAdmission()
    {
        CellContext context = CreateCell();
        var executor = new SettlementExecutor(
            context,
            _ => throw new InvalidOperationException("Content is not expected."))
        {
            ReturnNoContribution = true,
        };

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                Request(context.Cell),
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                outcome);
        var unavailable = Assert.IsType<
            PackageVersionCellMetadataInspectionResult.NoContribution>(
                completed.Result);
        Assert.Equal(
            PackageHouseRootNoContributionReason.ResourceFreeSettlement,
            unavailable.Reason);
        Assert.IsType<PackageHouseResult.Rejected>(
            unavailable.HouseResult);
        Assert.Equal(1, executor.ExecutionCount);
    }

    [Fact]
    public async Task SettlementForAnotherCellDemand_IsRejected()
    {
        CellContext context = CreateCell();
        var executor = new SettlementExecutor(
            context,
            _ => throw new InvalidOperationException(
                "Content is not expected."))
        {
            ReturnNoContribution = true,
            CreateRequest = (cell, operation, targetContext) =>
                new PackageHouseRequest(
                    new PackageHouseDemand.Exact(
                        cell.Candidate.Coordinate),
                    operation,
                    targetContext,
                    PackageHouseAssetSelectionKind.Compile,
                    PackageHouseLibraryHandoffMode.PackageOnly,
                    cell.Association),
        };

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => PackageVersionCellMetadataInspection.ExecuteAsync(
                    Request(context.Cell),
                    executor,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            "The version-cell executor returned a settlement for another request.",
            failure.Message);
    }

    [Fact]
    public async Task ExpiredWorkspaceDeadline_RemainsTyped()
    {
        CellContext context = CreateCell();
        var executor = new SettlementExecutor(
            context,
            _ => new InMemoryPackageContent(
                Archive(
                    (
                        $"lib/{Framework}/{PackageId}.dll",
                        File.ReadAllBytes(
                            typeof(PackageVersionCellMetadataInspectionTests)
                                .Assembly.Location))),
                fromCache: false,
                context.Source.Producer.Key));
        var request = new PackageVersionCellMetadataInspectionRequest(
            context.Cell,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(Framework),
            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                request,
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                outcome);
        var notCommitted = Assert.IsType<
            PackageVersionCellMetadataInspectionResult.WorkspaceNotCommitted>(
                completed.Result);
        var rejected = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            notCommitted.Result);
        Assert.Equal(
            WorkspaceScopeRejection.DeadlineExpired,
            rejected.Reason);
    }

    [Fact]
    public async Task MalformedCompileImage_RemainsWorkspaceFailure()
    {
        CellContext context = CreateCell();
        var executor = new SettlementExecutor(
            context,
            _ => new InMemoryPackageContent(
                Archive(
                    ($"lib/{Framework}/Good.dll",
                        File.ReadAllBytes(
                            typeof(PackageVersionCellMetadataInspectionTests)
                                .Assembly.Location)),
                    ($"lib/{Framework}/Bad.dll", [1, 2, 3])),
                fromCache: false,
                context.Source.Producer.Key));

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                Request(context.Cell),
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                outcome);
        var notCommitted = Assert.IsType<
            PackageVersionCellMetadataInspectionResult.WorkspaceNotCommitted>(
                completed.Result);
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            notCommitted.Result);
        Assert.Equal(
            ArtifactRootFailure.PreparationFailed,
            failed.Failure);
    }

    [Fact]
    public async Task Cancellation_PropagatesAfterCleanup()
    {
        CellContext context = CreateCell();
        using var cancellation = new CancellationTokenSource();
        var content = new TrackingPackageContent(
            Archive(
                (
                    $"lib/{Framework}/{PackageId}.dll",
                    File.ReadAllBytes(
                        typeof(PackageVersionCellMetadataInspectionTests)
                            .Assembly.Location))),
            context.Source.Producer.Key,
            cancellation.Cancel);
        var executor = new SettlementExecutor(context, _ => content);

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageVersionCellMetadataInspection.ExecuteAsync(
                    Request(context.Cell),
                    executor,
                    cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(0, content.ActiveStreams);
    }

    [Fact]
    public async Task LinkedCancellation_IsNormalizedToCallerToken()
    {
        CellContext context = CreateCell();
        using var cancellation = new CancellationTokenSource();
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellation.Token);
        var executor = new ThrowingExecutor(
            () =>
            {
                cancellation.Cancel();
                linked.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "Linked cancellation was not observed.");
            });

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageVersionCellMetadataInspection.ExecuteAsync(
                    Request(context.Cell),
                    executor,
                    cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(
            linked.Token,
            Assert.IsType<OperationCanceledException>(
                failure.InnerException).CancellationToken);
    }

    [Fact]
    public async Task Outcome_WaitsForWorkspaceClose()
    {
        CellContext context = CreateCell();
        using var closeEntered = new ManualResetEventSlim();
        using var closeResume = new ManualResetEventSlim();
        var executor = SuccessfulExecutor(context);
        InspectionWorkspace workspace = CreateWorkspace(
            new BlockingResource(
                closeEntered,
                closeResume,
                TestContext.Current.CancellationToken));

        Task<PackageVersionCellMetadataInspectionOutcome> operation =
            Task.Factory.StartNew(
                () => PackageVersionCellMetadataInspection.ExecuteAsync(
                    Request(context.Cell),
                    executor,
                    () => workspace,
                    TestContext.Current.CancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        await Task.Run(
            () => closeEntered.Wait(
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.False(operation.IsCompleted);
        closeResume.Set();
        Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.Completed>(
                await operation);
    }

    [Fact]
    public async Task CleanupFailure_ReturnsTypedOutcome()
    {
        CellContext context = CreateCell();
        var executor = SuccessfulExecutor(context);
        InspectionWorkspace workspace =
            CreateWorkspace(new ThrowingResource());

        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspection.ExecuteAsync(
                Request(context.Cell),
                executor,
                () => workspace,
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            PackageVersionCellMetadataInspectionOutcome.CleanupFailed>(
                outcome);
        Assert.Equal(
            new PackageVersionCellMetadataCleanupFailure(
                PackageVersionCellMetadataCleanupStage.WorkspaceGroupRelease,
                1),
            Assert.Single(failed.Cleanup.Failures));
    }

    [Fact]
    public async Task Cancellation_PreservesCleanupEvidence()
    {
        CellContext context = CreateCell();
        using var cancellation = new CancellationTokenSource();
        var executor = SuccessfulExecutor(context);
        InspectionWorkspace workspace =
            CreateWorkspace(
                new CancelingThrowingResource(cancellation));

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageVersionCellMetadataInspection.ExecuteAsync(
                    Request(context.Cell),
                    executor,
                    () => workspace,
                    cancellation.Token));

        Assert.True(
            PackageVersionCellMetadataInspectionExceptionEvidence
                .TryGetCleanup(failure, out var cleanup));
        Assert.Equal(
            PackageVersionCellMetadataCleanupStage.WorkspaceGroupRelease,
            Assert.Single(cleanup.Failures).Stage);
    }

    [Fact]
    public void PublicResults_AreResourceFree()
    {
        Type[] prohibited =
        [
            typeof(IPackageContent),
            typeof(AcquiredPackageSourcePayload),
            typeof(PackageRootBinding),
            typeof(InspectionWorkspace),
            typeof(PackageAssemblyContextRealization),
            typeof(AssemblyContextGroup),
            typeof(AssemblyContextParticipant),
            typeof(ArtifactSetSession),
            typeof(ArtifactQueryLease),
            typeof(DesktopPackageSourceComposition),
            typeof(Stream),
            typeof(Delegate),
        ];
        Type[] publicResults =
        [
            typeof(PackageVersionCellMetadataInspectionOutcome),
            .. typeof(PackageVersionCellMetadataInspectionOutcome)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageVersionCellMetadataInspectionResult),
            .. typeof(PackageVersionCellMetadataInspectionResult)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageVersionCellMetadataCleanupEvidence),
            typeof(PackageVersionCellMetadataCleanupFailure),
        ];

        var visited = new HashSet<Type>();
        foreach (Type result in publicResults)
            AssertResourceFree(result);

        void AssertResourceFree(Type type)
        {
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Exception))
            {
                return;
            }

            Assert.DoesNotContain(
                prohibited,
                resource => resource.IsAssignableFrom(type));
            if (type.IsArray)
                AssertResourceFree(type.GetElementType()!);
            foreach (Type argument in type.GetGenericArguments())
                AssertResourceFree(argument);
            if (type.Assembly
                == typeof(PackageVersionCellMetadataInspectionOutcome)
                    .Assembly)
            {
                foreach (PropertyInfo property in type.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic))
                {
                    AssertResourceFree(property.PropertyType);
                }
                foreach (FieldInfo field in type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic))
                {
                    AssertResourceFree(field.FieldType);
                }
            }
        }
    }

    private static PackageVersionCellMetadataInspectionRequest Request(
        PackageHouseVersionPopulationCell cell) =>
        new(
            cell,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(Framework),
            DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30));

    private static SettlementExecutor SuccessfulExecutor(
        CellContext context) =>
        new(
            context,
            _ => new InMemoryPackageContent(
                Archive(
                    (
                        $"lib/{Framework}/{PackageId}.dll",
                        File.ReadAllBytes(
                            typeof(PackageVersionCellMetadataInspectionTests)
                                .Assembly.Location))),
                fromCache: false,
                context.Source.Producer.Key));

    private static InspectionWorkspace CreateWorkspace(
        IDisposable ownedResource)
    {
        var workspace = new InspectionWorkspace();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(PackageVersionCellMetadataInspectionTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "version-cell cleanup test"));
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        assembly,
                        MissingBindingPolicy.Instance),
                ]);
        group.RegisterOwnedResource(ownedResource);
        return workspace;
    }

    private static CellContext CreateCell()
    {
        var authority = new ConfiguredPackageAuthority(
            PackageSource.NuGetOrg);
        PackageSourceResultFactory factory =
            ResultFactory(authority.Association);
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(PackageId, Version);
        var observation = new ConfiguredPackageCandidateObservation(
            authority,
            factory.Candidate(
                coordinate,
                PackageDiscoveryContract.CompleteVersionEnumeration,
                PackageListingState.Listed));
        var discovery = new PackageVersionDiscoveryResult(
            PackageId,
            PackageVersionDiscoveryState.Authoritative,
            [
                new("0.33.0", "nuget.org", Listed: true),
                new("0.34.0", "nuget.org", Listed: true),
                new(Version, "nuget.org", Listed: true),
            ],
            failures: [],
            hasAnyCandidate: true,
            candidates: [observation],
            contract:
                PackageVersionDiscoveryContract
                    .CompleteVersionEnumeration,
            candidateIssuer: new object());
        Assert.True(
            PackageVersionRange.TryParse(
                $"{PackageId}@0.33.0..{Version}",
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
        PackageVersionAddress address = Assert.Single(
            population.Vector.Addresses.Where(candidate =>
                candidate.Version.ToNormalizedString() == Version));
        return new(
            population.SelectCell(address),
            authority,
            factory.Source);
    }

    private static PackageSourceResultFactory ResultFactory(
        PackageSourceAssociation association)
    {
        IdentityPackageSourceClient? tracking = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                association,
                factory =>
                {
                    tracking = new(factory);
                    return tracking;
                });
        return tracking!.Factory;
    }

    private static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive =
            new ZipArchive(
                buffer,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination =
                    archive.CreateEntry(path).Open();
                destination.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static InertString Reason(string value) =>
        new(TextPolicy.Field, value);

    private sealed record CellContext(
        PackageHouseVersionPopulationCell Cell,
        ConfiguredPackageAuthority Authority,
        PackageSourceResultIdentity Source);

    private sealed class SettlementExecutor(
        CellContext context,
        Func<PackageHouseRequest, IPackageContent> createContent)
        : IPackageVersionCellCompileExecutor
    {
        public int ExecutionCount { get; private set; }

        public bool ReturnNoContribution { get; init; }

        public Func<
            PackageHouseVersionPopulationCell,
            PackageHouseOperation,
            PackageHouseTargetContext?,
            PackageHouseRequest>? CreateRequest { get; init; }

        public PackageHouseSettlement? Settlement { get; private set; }

        public Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseVersionPopulationCell cell,
            PackageHouseOperation operation,
            PackageHouseTargetContext? targetContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            PackageHouseRequest request =
                CreateRequest?.Invoke(cell, operation, targetContext)
                ?? cell.CreateRequest(
                    operation,
                    targetContext,
                    PackageHouseAssetSelectionKind.Compile,
                    PackageHouseLibraryHandoffMode.PackageOnly);
            Settlement = ReturnNoContribution
                ? CreateNoContribution(request)
                : CreateAcquired(request);
            return Task.FromResult(Settlement);
        }

        private PackageHouseSettlement CreateNoContribution(
            PackageHouseRequest request)
        {
            PackageHouseDecisionReceipt decision =
                PackageHouseDecisionReceipt.Stop(
                    request,
                    context.Cell.Candidate.Coordinate);
            var evidence = new PackageHouseEvidence(
                request,
                decision,
                failures:
                [
                    new PackageHouseFailure.Stage(
                        PackageHouseFailureStage.Acquisition,
                        Reason("The current source policy rejected the cell.")),
                ]);
            return new PackageHouseSettlement.ResourceFree(
                new PackageHouseResult.Rejected(
                    evidence,
                    Reason("The current source policy rejected the cell.")));
        }

        private PackageHouseSettlement CreateAcquired(
            PackageHouseRequest request)
        {
            IPackageContent content = createContent(request);
            PackageAcquisitionCandidate candidate =
                context.Cell.Candidate;
            PackageHouseDecisionReceipt decision =
                PackageHouseDecisionReceipt.RetainPackage(
                    request,
                    candidate.Coordinate,
                    candidate);
            var payload = new AcquiredPackageSourcePayload(
                candidate.Coordinate,
                content,
                context.Source.Producer.Key,
                context.Source.Producer,
                PackagePayloadOrigin.Download);
            var sourceResult = new ConfiguredPackagePayloadResult(
                context.Authority,
                context.Source,
                payload,
                failures: [],
                reportingAuthorities:
                [
                    .. candidate.Authorities.Select(
                        evidence => evidence.Authority),
                ],
                selectionUsesOriginalSources: true);
            payload = sourceResult.Payload!;
            var acquisition = new PackageHouseAcquisitionReceipt(
                decision,
                context.Authority,
                context.Source,
                PackagePayloadOrigin.Download,
                content.GenerationIdentity);
            var realization = new PackageHouseRealizationReceipt.Compile(
                acquisition,
                PackageCompileAssetSelector.Evaluate(
                    content,
                    candidate.Coordinate.PackageId,
                    request.TargetContext?.RequestedFramework,
                    request.TargetContext?.RuntimeIdentifier));
            var evidence = new PackageHouseEvidence(
                request,
                decision,
                acquisition,
                realization);
            PackageHouseResult result = realization.Completion switch
            {
                PackageHouseRealizationCompletion.Settled =>
                    new PackageHouseResult.Settled(evidence),
                PackageHouseRealizationCompletion.NoMatch =>
                    new PackageHouseResult.NoMatch(
                        evidence,
                        Reason("The package has no matching compile surface.")),
                PackageHouseRealizationCompletion.Rejected =>
                    new PackageHouseResult.Rejected(
                        evidence,
                        Reason("The package compile surface was rejected.")),
                _ => throw new InvalidOperationException(
                    "Unexpected compile realization outcome."),
            };
            return new PackageHouseSettlement.Acquired(
                result,
                payload,
                sourceResult,
                selectionUsesOriginalSources: true);
        }
    }

    private sealed class ThrowingExecutor(Action throwFailure)
        : IPackageVersionCellCompileExecutor
    {
        public Task<PackageHouseSettlement> ExecuteAsync(
            PackageHouseVersionPopulationCell cell,
            PackageHouseOperation operation,
            PackageHouseTargetContext? targetContext,
            CancellationToken cancellationToken = default)
        {
            throwFailure();
            throw new InvalidOperationException(
                "The synthetic executor did not throw.");
        }
    }

    private sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingBindingPolicy Instance { get; } = new();

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    private sealed class BlockingResource(
        ManualResetEventSlim entered,
        ManualResetEventSlim resume,
        CancellationToken cancellationToken) : IDisposable
    {
        public void Dispose()
        {
            entered.Set();
            resume.Wait(cancellationToken);
        }
    }

    private sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(
                "Synthetic Workspace cleanup failure.");
    }

    private sealed class CancelingThrowingResource(
        CancellationTokenSource cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Cancel();
            throw new InvalidOperationException(
                "Synthetic Workspace cleanup failure.");
        }
    }

    private sealed class TrackingPackageContent(
        byte[] archive,
        string producerKey,
        Action? onOpen = null) : IPackageContent
    {
        private readonly InMemoryPackageContent _inner =
            new(archive, fromCache: false, producerKey);
        private Action? _onOpen = onOpen;
        private int _activeStreams;
        private int _openedStreams;

        public int ActiveStreams => Volatile.Read(ref _activeStreams);

        public int OpenedStreams => Volatile.Read(ref _openedStreams);

        public string? RootPath => _inner.RootPath;

        public string? NupkgPath => _inner.NupkgPath;

        public bool FromCache => _inner.FromCache;

        public string ProducerKey => _inner.ProducerKey;

        public PackageContentGenerationIdentity GenerationIdentity =>
            _inner.GenerationIdentity;

        public bool RequiresArchiveTreeMatch =>
            _inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            TryOpen(_inner.TryOpenArchive, out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!_inner.TryOpenEntry(relativePath, out Stream? inner))
            {
                stream = null;
                return false;
            }
            stream = Track(inner);
            return true;
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!_inner.TryOpenEntry(
                    relativePath,
                    maxExpandedBytes,
                    out Stream? inner))
            {
                stream = null;
                return false;
            }
            stream = Track(inner);
            return true;
        }

        public IEnumerable<string> EnumerateEntries() =>
            _inner.EnumerateEntries();

        private bool TryOpen(
            TryOpenStream open,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!open(out Stream? inner))
            {
                stream = null;
                return false;
            }
            stream = Track(inner);
            return true;
        }

        private Stream Track(Stream inner)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            Interlocked.Increment(ref _openedStreams);
            Interlocked.Increment(ref _activeStreams);
            return new TrackingStream(
                inner,
                () => Interlocked.Decrement(ref _activeStreams));
        }

        private delegate bool TryOpenStream(
            [NotNullWhen(true)] out Stream? stream);
    }

    private sealed class TrackingStream(
        Stream inner,
        Action onDispose) : Stream
    {
        private int _disposed;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                inner.Dispose();
                onDispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await inner.DisposeAsync();
                onDispose();
            }
            GC.SuppressFinalize(this);
        }
    }

    private sealed class IdentityPackageSourceClient(
        PackageSourceResultFactory factory) : IPackageSourceClient
    {
        public PackageSourceResultFactory Factory { get; } = factory;

        public PackageSourceResultIdentity Source => Factory.Source;

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
