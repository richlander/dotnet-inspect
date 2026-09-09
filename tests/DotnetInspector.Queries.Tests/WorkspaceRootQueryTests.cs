using System.IO.Compression;

using DotnetInspector.Artifacts;
using DotnetInspector.Packages;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.QueriesConsumer;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceRootQueryTests
{
    static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);
    static DateTimeOffset Deadline => DateTimeOffset.UtcNow.AddMinutes(5);
    static CancellationToken TestCancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NonFriendConsumer_QueriesTwoCommittedRootsWithDistinctPackageRoles()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding shared = Binding("Shared.Package", "lib/net11.0/Fixture.dll");
        PackageRootBinding separate = Binding("Separate.Package",
            "ref/net11.0/Fixture.dll", "lib/net11.0/Fixture.dll");
        WorkspaceScopeSnapshot scope = await Replace(workspace, shared, separate);

        PackageRootInventory sharedResult = Available(await PackageRootQueryConsumer.QueryAsync(
            workspace, Correspondence(scope.Roots[0]), Ready(scope.Roots[0]),
            cancellationToken: TestCancellation));
        PackageRootInventory separateResult = Available(await PackageRootQueryConsumer.QueryAsync(
            workspace, Correspondence(scope.Roots[1]), Ready(scope.Roots[1]),
            cancellationToken: TestCancellation));

        Assert.True(sharedResult.HasAssemblyContexts);
        Assert.True(sharedResult.HasImplementationGroup);
        Assert.True(sharedResult.SharesGroup);
        Assert.Equal(1, sharedResult.SurfaceParticipantCount);
        Assert.Equal(1, sharedResult.ImplementationParticipantCount);
        var sharedSurface = Inventory(sharedResult.Surface, "Shared.Package");
        var sharedImplementation = Inventory(sharedResult.Implementation, "Shared.Package");
        Assert.Same(sharedSurface.Subject.Registration, sharedImplementation.Subject.Registration);
        AssertPackageArtifact(sharedSurface.Subject, shared,
            "lib/net11.0/Fixture.dll", PackageCompileAssetKind.Library);

        Assert.True(separateResult.HasAssemblyContexts);
        Assert.True(separateResult.HasImplementationGroup);
        Assert.False(separateResult.SharesGroup);
        Assert.Equal(1, separateResult.SurfaceParticipantCount);
        Assert.Equal(1, separateResult.ImplementationParticipantCount);
        var separateSurface = Inventory(separateResult.Surface, "Separate.Package");
        var separateImplementation = Inventory(separateResult.Implementation, "Separate.Package");
        Assert.NotSame(separateSurface.Subject.Registration, separateImplementation.Subject.Registration);
        Assert.NotSame(separateSurface.Subject.Registration.ArtifactRegistration,
            separateImplementation.Subject.Registration.ArtifactRegistration);
        Assert.Equal(separateSurface.Subject.Identity, separateImplementation.Subject.Identity);
        Assert.Equal(separateSurface.Subject.Registration.ModuleVersionId,
            separateImplementation.Subject.Registration.ModuleVersionId);
        AssertPackageArtifact(separateSurface.Subject, separate,
            "ref/net11.0/Fixture.dll", PackageCompileAssetKind.Reference);
        AssertPackageArtifact(separateImplementation.Subject, separate,
            "lib/net11.0/Fixture.dll", PackageCompileAssetKind.Library);
        Assert.NotSame(sharedSurface.Subject.Registration, separateSurface.Subject.Registration);

        Assert.NotNull(sharedResult.SurfacePolicy);
        Assert.NotNull(separateResult.SurfacePolicy);
        Assert.NotSame(sharedResult.SurfacePolicy, separateResult.SurfacePolicy);
        PackageRootInventory repeated = Available(await PackageRootQueryConsumer.QueryAsync(
            workspace, Correspondence(scope.Roots[0]), Ready(scope.Roots[0]),
            sharedResult.SurfacePolicy, TestCancellation));
        Assert.Same(sharedSurface.Subject.Registration,
            Inventory(repeated.Surface, "Shared.Package").Subject.Registration);
    }

    [Theory]
    [InlineData("tools/net11.0/Fixture.dll", PackageCompileAssetSelectionStatus.NoCompileAssets)]
    [InlineData("ref/net11.0/_._", PackageCompileAssetSelectionStatus.EmptyCompileGroup)]
    public async Task NonFriendConsumer_RootOnlyAndExplicitEmptyReachCallbackWithoutAssemblies(
        string entry, PackageCompileAssetSelectionStatus expected)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding = entry.EndsWith("/_._", StringComparison.Ordinal)
            ? Binding("Empty.Package", entry, "lib/net11.0/Fixture.dll")
            : Binding("Empty.Package", entry);
        WorkspaceScopeSnapshot scope = await Replace(workspace, binding);
        WorkspaceRootOccurrenceDescriptor root = Assert.Single(scope.Roots);
        Assert.Equal(expected,
            Assert.IsType<WorkspaceRootDescriptor.Package>(root.Occurrence.Root).SelectionStatus);

        PackageRootInventory result = Available(await PackageRootQueryConsumer.QueryAsync(
            workspace, Correspondence(root), Ready(root), cancellationToken: TestCancellation));

        Assert.False(result.HasAssemblyContexts);
        Assert.False(result.HasImplementationGroup);
        Assert.False(result.SharesGroup);
        Assert.Equal(0, result.SurfaceParticipantCount);
        Assert.Equal(0, result.ImplementationParticipantCount);
        Assert.Null(result.SurfacePolicy);
        Assert.Null(result.Surface);
        Assert.Null(result.Implementation);
    }

    [Fact]
    public async Task Admission_RejectsStaleForeignAndWrongPolicyBeforeCallback()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot first = await Replace(workspace, Binding("Same.Package"));
        WorkspaceScopeSnapshot other = await Replace(foreign, Binding("Same.Package"));
        WorkspaceRootOccurrenceDescriptor oldRoot = first.Roots[0];
        WorkspaceRootOccurrenceDescriptor foreignRoot = other.Roots[0];
        AssemblyBindingPolicyVersion foreignPolicy = Available(await foreign.ExecutePackageRootQueryAsync(
            Correspondence(foreignRoot), Ready(foreignRoot),
            static (realization, _) => ValueTask.FromResult(realization.SurfaceGroup.BindingPolicyVersion),
            cancellationToken: TestCancellation));

        WorkspaceScopeSnapshot cleared = await Clear(workspace, first);
        Assert.Empty(cleared.Roots);
        await Reject(Correspondence(oldRoot), Ready(oldRoot), null,
            ArtifactRootFailure.ArtifactGenerationMismatch);
        WorkspaceScopeSnapshot readded = await Replace(workspace, Binding("Same.Package"));
        WorkspaceRootOccurrenceDescriptor current = readded.Roots[0];
        Assert.Equal(Correspondence(oldRoot), Correspondence(current));
        Assert.NotSame(Ready(oldRoot), Ready(current));

        await Reject(Correspondence(current), Ready(oldRoot), foreignPolicy,
            ArtifactRootFailure.ArtifactGenerationMismatch);
        await Reject(Correspondence(current), Ready(foreignRoot), foreignPolicy,
            ArtifactRootFailure.ArtifactGenerationMismatch);
        await Reject(Correspondence(foreignRoot), Ready(current), foreignPolicy,
            ArtifactRootFailure.ArtifactGenerationMismatch);
        await Reject(Correspondence(foreignRoot), Ready(foreignRoot), foreignPolicy,
            ArtifactRootFailure.ArtifactGenerationMismatch);
        await Reject(Correspondence(current), Ready(current), foreignPolicy,
            ArtifactRootFailure.BindingPolicyMismatch);

        Inventory(Available(await PackageRootQueryConsumer.QueryAsync(
            workspace, Correspondence(current), Ready(current),
            cancellationToken: TestCancellation)).Surface, "Same.Package");

        async Task Reject(
            PackageArtifactRootCorrespondence correspondence,
            ArtifactRootGenerationReference generation,
            AssemblyBindingPolicyVersion? policy,
            ArtifactRootFailure expected)
        {
            bool invoked = false;
            ArtifactRootResult<int> result = await workspace.ExecutePackageRootQueryAsync(
                correspondence, generation, (_, _) =>
                {
                    invoked = true;
                    return ValueTask.FromResult(1);
                }, policy, TestCancellation).AsTask().WaitAsync(WaitLimit, TestCancellation);
            Assert.Equal(expected, Rejected(result));
            Assert.False(invoked);
        }
    }

    [Fact]
    public async Task Admission_PendingAndFailedRootsRejectBeforeCallback()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Unavailable.Package"));
        WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
        ArtifactRootCompositionGenerationIdentity pending = Available(
            await workspace.RetireArtifactRootAsync(Correspondence(root), Ready(root)));
        Assert.IsType<ArtifactRootRealizationStatus.Pending>((await Current(workspace)).Roots[0].Realization.Status);
        await Reject();

        Available(await workspace.FailArtifactRootReplacementAsync(
            Correspondence(root), pending, ArtifactRootFailure.PreparationFailed));
        Assert.Equal(ArtifactRootFailure.PreparationFailed,
            Assert.IsType<ArtifactRootRealizationStatus.Failed>(
                (await Current(workspace)).Roots[0].Realization.Status).Failure);
        await Reject();

        async Task Reject()
        {
            bool invoked = false;
            ArtifactRootResult<int> result = await workspace.ExecutePackageRootQueryAsync(
                Correspondence(root), Ready(root), (_, _) =>
                {
                    invoked = true;
                    return ValueTask.FromResult(1);
                }, cancellationToken: TestCancellation);
            Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(result));
            Assert.False(invoked);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackExceptions_PreserveExactExceptionAndReleaseLease(bool afterAwait)
    {
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var entered = Signal();
        var resume = Signal();
        try
        {
            WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Throwing.Package"));
            WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
            var expected = new InvalidOperationException("The query callback failed.");
            Task<ArtifactRootResult<int>> operation = workspace.ExecutePackageRootQueryAsync(
                Correspondence(root), Ready(root), Query, cancellationToken: TestCancellation).AsTask();
            try
            {
                await entered.Task.WaitAsync(WaitLimit, TestCancellation);
                if (afterAwait)
                    Assert.False(operation.IsCompleted);
            }
            finally { resume.TrySetResult(); }

            InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
                () => operation.WaitAsync(WaitLimit, TestCancellation));
            Assert.Same(expected, actual);
            Assert.Empty((await Clear(workspace, scope)).Roots);

            ValueTask<int> Query(PackageAssemblyContextRealization _, CancellationToken token)
            {
                Assert.Equal(TestCancellation, token);
                entered.TrySetResult();
                if (!afterAwait)
                    throw expected;
                return ThrowAfterAwait();
            }

            async ValueTask<int> ThrowAfterAwait()
            {
                await resume.Task.WaitAsync(WaitLimit, TestCancellation);
                throw expected;
            }
        }
        finally
        {
            resume.TrySetResult();
            await Close(workspace);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_BeforeEntryOrWhileCompositionGateIsHeldDoesNotInvokeCallback(bool whileWaiting)
    {
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using var cancellation = new CancellationTokenSource();
        InspectionWorkspace.ArtifactRootCompositionReadLease? read = null;
        try
        {
            WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Cancelled.Package"));
            WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
            if (whileWaiting)
                read = Available(await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
            else
                cancellation.Cancel();

            bool invoked = false;
            Task<ArtifactRootResult<int>> operation = workspace.ExecutePackageRootQueryAsync(
                Correspondence(root), Ready(root), (_, _) =>
                {
                    invoked = true;
                    return ValueTask.FromResult(1);
                }, cancellationToken: cancellation.Token).AsTask();
            try
            {
                if (whileWaiting)
                {
                    Assert.False(operation.IsCompleted);
                    cancellation.Cancel();
                }
                OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => operation.WaitAsync(WaitLimit, TestCancellation));
                Assert.Equal(cancellation.Token, error.CancellationToken);
                Assert.False(invoked);
            }
            finally
            {
                read?.Dispose();
                read = null;
            }

            Inventory(Available(await PackageRootQueryConsumer.QueryAsync(
                workspace, Correspondence(root), Ready(root),
                cancellationToken: TestCancellation)).Surface, "Cancelled.Package");
            Assert.Empty((await Clear(workspace, scope)).Roots);
        }
        finally
        {
            read?.Dispose();
            await Close(workspace);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackCancellation_IsCooperativeAndReleasesLease(bool observeCancellation)
    {
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var resume = Signal();
        try
        {
            WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Cooperative.Package"));
            WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
            Task<ArtifactRootResult<int>> operation = workspace.ExecutePackageRootQueryAsync<int>(
                Correspondence(root), Ready(root), async (_, token) =>
                {
                    Assert.Equal(cancellation.Token, token);
                    entered.TrySetResult();
                    await resume.Task.WaitAsync(WaitLimit,
                        observeCancellation ? token : TestCancellation);
                    return 42;
                }, cancellationToken: cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(WaitLimit, TestCancellation);
                cancellation.Cancel();
                if (observeCancellation)
                {
                    OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => operation.WaitAsync(WaitLimit, TestCancellation));
                    Assert.Equal(cancellation.Token, error.CancellationToken);
                }
                else
                {
                    Assert.False(operation.IsCompleted);
                }
            }
            finally { resume.TrySetResult(); }

            if (!observeCancellation)
                Assert.Equal(42, Available(await operation.WaitAsync(WaitLimit, TestCancellation)));
            Assert.Empty((await Clear(workspace, scope)).Roots);
        }
        finally
        {
            resume.TrySetResult();
            await Close(workspace);
        }
    }

    [Fact]
    public async Task Clear_StopsNewAdmissionButAdmittedCallbackCanStillQuery()
    {
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var entered = Signal();
        var resume = Signal();
        try
        {
            WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Cleared.Package"));
            WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
            Task<ArtifactRootResult<AssemblyContextResult<AssemblyTypeInventory>>> operation =
                workspace.ExecutePackageRootQueryAsync<AssemblyContextResult<AssemblyTypeInventory>>(
                    Correspondence(root), Ready(root), async (realization, token) =>
                    {
                        entered.TrySetResult();
                        await resume.Task.WaitAsync(WaitLimit, token);
                        return AssemblyContextTypeInventoryQuery.Execute(realization.SurfaceGroup);
                    }, cancellationToken: TestCancellation).AsTask();
            try
            {
                await entered.Task.WaitAsync(WaitLimit, TestCancellation);
                Assert.Empty((await Clear(workspace, scope)).Roots);
                Assert.False(operation.IsCompleted);
                bool invoked = false;
                Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, Rejected(
                    await workspace.ExecutePackageRootQueryAsync(Correspondence(root), Ready(root), (_, _) =>
                    {
                        invoked = true;
                        return ValueTask.FromResult(1);
                    }, cancellationToken: TestCancellation).AsTask().WaitAsync(WaitLimit, TestCancellation)));
                Assert.False(invoked);
            }
            finally { resume.TrySetResult(); }

            Inventory(Available(await operation.WaitAsync(WaitLimit, TestCancellation)), "Cleared.Package");
        }
        finally
        {
            resume.TrySetResult();
            await Close(workspace);
        }
    }

    [Fact]
    public async Task Close_WaitsForAdmittedCallbackAndRejectsNewAccess()
    {
        InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var entered = Signal();
        var resume = Signal();
        try
        {
            WorkspaceScopeSnapshot scope = await Replace(workspace, Binding("Closing.Package"));
            WorkspaceRootOccurrenceDescriptor root = scope.Roots[0];
            Task<ArtifactRootResult<AssemblyContextResult<AssemblyTypeInventory>>> operation =
                workspace.ExecutePackageRootQueryAsync<AssemblyContextResult<AssemblyTypeInventory>>(
                    Correspondence(root), Ready(root), async (realization, token) =>
                    {
                        AssemblyContextResult<AssemblyTypeInventory> inventory =
                            AssemblyContextTypeInventoryQuery.Execute(realization.SurfaceGroup);
                        entered.TrySetResult();
                        await resume.Task.WaitAsync(WaitLimit, token);
                        return inventory;
                    }, cancellationToken: TestCancellation).AsTask();
            Task<InspectionWorkspaceCloseReport> closing;
            try
            {
                await entered.Task.WaitAsync(WaitLimit, TestCancellation);
                closing = workspace.CloseAsync();
                Assert.False(closing.IsCompleted);
                await Reject(ArtifactRootFailure.WorkspaceClosing);
                Assert.False(operation.IsCompleted);
            }
            finally { resume.TrySetResult(); }

            Inventory(Available(await operation.WaitAsync(WaitLimit, TestCancellation)), "Closing.Package");
            Assert.Empty((await closing.WaitAsync(WaitLimit, TestCancellation)).ArtifactSessionCleanupFailures);
            await Reject(ArtifactRootFailure.WorkspaceClosed);

            async Task Reject(ArtifactRootFailure expected)
            {
                bool invoked = false;
                ArtifactRootResult<int> result = await workspace.ExecutePackageRootQueryAsync(
                    Correspondence(root), Ready(root), (_, _) =>
                    {
                        invoked = true;
                        return ValueTask.FromResult(1);
                    }, cancellationToken: TestCancellation).AsTask().WaitAsync(WaitLimit, TestCancellation);
                Assert.Equal(expected, Rejected(result));
                Assert.False(invoked);
            }
        }
        finally
        {
            resume.TrySetResult();
            await Close(workspace);
        }
    }

    static AssemblyContextEntry<AssemblyTypeInventory>.Available Inventory(
        AssemblyContextResult<AssemblyTypeInventory>? result, string packageId)
    {
        Assert.NotNull(result);
        Assert.True(result.IsComplete);
        var entry = Assert.IsType<AssemblyContextEntry<AssemblyTypeInventory>.Available>(
            Assert.Single(result.Assemblies));
        Assert.Contains(entry.Value.Types,
            type => type.FullName == typeof(EmbeddedSourceFixture).FullName);
        Assert.Empty(entry.Value.InspectionFailures);
        var provenance = Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(entry.Subject.Provenance);
        Assert.Equal(packageId, provenance.PackageId);
        Assert.Equal("1.0.0", provenance.PackageVersion);
        Assert.Equal("net11.0", provenance.Tfm);
        Assert.Null(provenance.Rid);
        return entry;
    }

    static void AssertPackageArtifact(
        AssemblyContextSubject subject, PackageRootBinding binding,
        string path, PackageCompileAssetKind kind)
    {
        var registration = Assert.IsType<ArtifactAcquisitionRegistration>(subject.Registration.ArtifactRegistration);
        var provenance = Assert.IsType<PackageAssemblyArtifactProvenance>(registration.Provenance);
        Assert.Equal(binding.Coordinate, provenance.Coordinate);
        Assert.Same(binding.ContentGenerationIdentity, provenance.ContentGenerationIdentity);
        Assert.Same(binding.SelectionIdentity, provenance.SelectionIdentity);
        Assert.Equal(path, provenance.Asset.Path);
        Assert.Equal(kind, provenance.Asset.Kind);
    }

    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static T Available<T>(ArtifactRootResult<T> result) =>
        Assert.IsType<ArtifactRootResult<T>.Available>(result).Value;

    static ArtifactRootFailure Rejected<T>(ArtifactRootResult<T> result) =>
        Assert.IsType<ArtifactRootResult<T>.Rejected>(result).Failure;

    static PackageArtifactRootCorrespondence Correspondence(WorkspaceRootOccurrenceDescriptor root) =>
        Assert.IsType<PackageArtifactRootCorrespondence>(root.Occurrence.Correspondence);

    static ArtifactRootGenerationReference Ready(WorkspaceRootOccurrenceDescriptor root) =>
        Assert.IsType<ArtifactRootRealizationStatus.Ready>(root.Realization.Status).Generation;

    static async Task<WorkspaceScopeSnapshot> Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(await workspace.GetScopeSnapshotAsync()).Snapshot;

    static async Task<WorkspaceScopeSnapshot> Replace(
        InspectionWorkspace workspace, params PackageRootBinding[] bindings) =>
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync((await Current(workspace)).Revision,
                [.. bindings], Deadline, TestCancellation).AsTask().WaitAsync(WaitLimit, TestCancellation)).Snapshot;

    static async Task<WorkspaceScopeSnapshot> Clear(
        InspectionWorkspace workspace, WorkspaceScopeSnapshot scope) =>
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ClearScopeAsync(scope.Revision, Deadline, TestCancellation)
                .AsTask().WaitAsync(WaitLimit, TestCancellation)).Snapshot;

    static async Task Close(InspectionWorkspace workspace) =>
        Assert.Empty((await workspace.CloseAsync().WaitAsync(WaitLimit, TestCancellation)).ArtifactSessionCleanupFailures);

    static PackageRootBinding Binding(string packageId, params string[] entries)
    {
        byte[] image = File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location);
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in entries.Length == 0 ? ["lib/net11.0/Fixture.dll"] : entries)
            {
                using Stream destination = archive.CreateEntry(path).Open();
                if (!path.EndsWith("/_._", StringComparison.Ordinal))
                    destination.Write(image);
            }
        }
        IPackageContent content = new InMemoryPackageContent(
            bytes.ToArray(), fromCache: false, producerKey: "tests");
        return PackageRootBinding.CreateFromSource(new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"), content, "tests", PackagePayloadOrigin.Download),
            "net11.0", displayPackageId: packageId);
    }
}
