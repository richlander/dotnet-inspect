using System.IO.Compression;
using System.Reflection;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyContextCompletionTests
{
    const string Framework = "net11.0";

    [Fact]
    public async Task PackageRealizationProjection_PreservesDemandPackageIdentityAndOrder()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextCompletionTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageRootBinding first = Binding(
            "First.Projection",
            ("ref/net11.0/First.Projection.dll", firstImage),
            ("lib/net11.0/First.Projection.dll", firstImage));
        PackageRootBinding second = Binding(
            "Second.Projection",
            ("lib/net11.0/Second.Projection.dll", secondImage));
        PackageRootBinding[] bindings = [first, second];
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, bindings);
        PackageRootIdentity[] firstDemand =
        [
            CloneRoot(first.Root.Identity),
            CloneRoot(second.Root.Identity),
        ];
        PackageRootIdentity[] secondDemand =
        [
            CloneRoot(first.Root.Identity),
            CloneRoot(second.Root.Identity),
        ];
        PackageAssemblyContextProjection firstProjection =
            completion.CreateProjection(bindings, firstDemand);
        PackageAssemblyContextProjection secondProjection =
            completion.CreateProjection(bindings, secondDemand);

        Assert.Equal(
            firstDemand,
            firstProjection.SurfaceParticipants.Select(entry => entry.Package));
        Assert.Equal(
            secondDemand,
            secondProjection.SurfaceParticipants.Select(entry => entry.Package));
        Assert.All(
            firstProjection.SurfaceParticipants.Zip(
                secondProjection.SurfaceParticipants),
            pair => Assert.Same(
                pair.First.Participant,
                pair.Second.Participant));
        Assert.All(
            firstProjection.SurfaceParticipants,
            surface =>
            {
                PackageAssemblyRoleParticipant implementation =
                    Assert.IsType<PackageAssemblyRoleParticipant>(
                        firstProjection.ImplementationParticipant(surface));
                Assert.Same(surface.Package, implementation.Package);
            });
        PackageWorkspaceIntegrationsResult result =
            PackageWorkspaceIntegrationsQuery.Execute(firstProjection);
        Assert.Equal(
            firstDemand,
            result.Libraries.Select(entry => entry.Subject.Package));
        Assert.Throws<ArgumentException>(
            () => completion.CreateProjection(
                [second, first],
                firstDemand));
        Assert.Throws<ArgumentException>(
            () => completion.CreateProjection(
                bindings,
                [
                    new PackageRootIdentity(
                        "Wrong.Projection",
                        "1.0.0",
                        Framework,
                        null),
                    secondDemand[1],
                ]));

        await firstProjection.ReturnAsync();
        await secondProjection.ReturnAsync();
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationProjection_OneReturnDoesNotInvalidateAnotherDemand()
    {
        PackageRootBinding binding = SharedBinding("Independent.Demand");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection first =
            completion.CreateProjection([binding]);
        PackageAssemblyContextProjection second =
            completion.CreateProjection([binding]);

        await first.ReturnAsync();

        Assert.Single(
            AssemblyContextIntegrationsQuery.Execute(
                second.SurfaceRole)
            .Assemblies);
        await second.ReturnAsync();
        await completion.CloseAsync();
    }

    [Fact]
    public void PackageRealizationProjection_CannotTerminallyReleaseSharedParticipant()
    {
        Assert.DoesNotContain(
            typeof(AssemblyContextIntegrationsQuery).GetMethods(
                BindingFlags.Public | BindingFlags.Static),
            method =>
                method.Name == nameof(
                    AssemblyContextIntegrationsQuery.ExecuteParticipantAsync)
                && method.GetParameters().FirstOrDefault()?.ParameterType
                    == typeof(PackageAssemblyContextRoleProjection));
    }

    [Fact]
    public void PackageRealizationProjection_RetainedSnapshotPolicyIsExplicit()
    {
        Assert.DoesNotContain(
            typeof(PackageAssemblyContextRoleProjection).GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly),
            method =>
                method.Name.Contains(
                    "RetainAssemblyReference",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void PackageRealizationLeaseHolder_CannotReleaseSharedGroup()
    {
        Type role = typeof(PackageAssemblyContextRoleProjection);
        Assert.DoesNotContain(
            role.GetProperties(
                BindingFlags.Public | BindingFlags.Instance),
            property =>
                typeof(AssemblyContextGroup).IsAssignableFrom(
                    property.PropertyType));
        Assert.DoesNotContain(
            role.GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly),
            method =>
                method.Name.Contains(
                    "Release",
                    StringComparison.Ordinal)
                || method.Name.Contains(
                    "Dispose",
                    StringComparison.Ordinal)
                || method.Name.Contains(
                    "RegisterOwnedResource",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task PackageRealizationReturnedLease_RejectsProjectionAccess()
    {
        PackageRootBinding binding = SharedBinding("Returned.Demand");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([binding]);
        PackageAssemblyContextRoleProjection role =
            projection.SurfaceRole;

        await projection.ReturnAsync();

        Assert.Throws<ObjectDisposedException>(
            () => projection.SurfaceRole);
        Assert.Throws<ObjectDisposedException>(
            () => role.Participants);
        Assert.Throws<ObjectDisposedException>(
            () => AssemblyContextIntegrationsQuery.Execute(role));
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationConcurrentUseAndReturn_LinearizesBeforeCleanup()
    {
        PackageRootBinding binding = SharedBinding("Concurrent.Return");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([binding]);
        var resource = new CountingResource();
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(resource);
        var entered =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var resume =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> use = Task.Run(() =>
            projection.SurfaceRole.Use(group =>
            {
                entered.SetResult();
                resume.Task.GetAwaiter().GetResult();
                return projection.SurfaceParticipants.Length;
            }));
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Task returned = projection.ReturnAsync();
        Task<PackageRoleCleanupReport> close = completion.CloseAsync();

        Assert.False(returned.IsCompleted);
        Assert.False(close.IsCompleted);
        Assert.Equal(0, resource.DisposeCount);
        resume.SetResult();
        Assert.Equal(1, await use);
        await returned;
        await close;
        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task PackageRealizationProjection_ReentrantReturnRejectsBeforeMutation()
    {
        PackageRootBinding binding = SharedBinding("Reentrant.Return");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([binding]);
        PackageAssemblyContextRoleProjection role =
            projection.SurfaceRole;

        role.Use(group =>
        {
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    _ = projection.ReturnAsync();
                });
            Assert.Throws<InvalidOperationException>(
                () =>
                {
                    _ = completion.CloseAsync();
                });
            return group.RetainedImageBytes;
        });

        Assert.Single(role.Participants);
        await projection.ReturnAsync();
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationCompletion_LastReturnAndCloseStartCleanupOnce()
    {
        PackageRootBinding binding = SharedBinding("Last.Return");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection first =
            completion.CreateProjection([binding]);
        PackageAssemblyContextProjection second =
            completion.CreateProjection([binding]);
        var resource = new CountingResource();
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(resource);
        Task<PackageRoleCleanupReport> close = completion.CloseAsync();

        await first.ReturnAsync();
        Assert.False(close.IsCompleted);
        await second.ReturnAsync();
        await close;

        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async Task PackageRealizationCompletion_CloseReturnsExactKeyedCleanupDomain()
    {
        PackageRootBinding binding = SeparateBinding("Keyed.Cleanup");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);

        PackageRoleCleanupReport report =
            await completion.CloseAsync();

        Assert.Same(completion.Operation, report.Operation);
        Assert.Equal(2, report.Groups.Length);
        Assert.Contains(
            report.Groups,
            record => ReferenceEquals(
                record.Group,
                completion.SurfaceGroup));
        Assert.Contains(
            report.Groups,
            record => ReferenceEquals(
                record.Group,
                completion.ImplementationGroup));
        Assert.All(
            report.Groups,
            record => Assert.IsType<
                PackageRoleGroupCleanupRecord.Released>(record));
    }

    [Fact]
    public async Task PackageRealizationCompletion_RepeatedCloseSharesReport()
    {
        PackageRootBinding binding = SharedBinding("Repeated.Close");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);

        Task<PackageRoleCleanupReport> first = completion.CloseAsync();
        Task<PackageRoleCleanupReport> second = completion.CloseAsync();
        PackageRoleCleanupReport firstReport = await first;
        PackageRoleCleanupReport secondReport = await second;

        Assert.Same(first, second);
        Assert.Same(firstReport, secondReport);
        Assert.Same(firstReport, completion.CloseReport);
    }

    [Fact]
    public async Task PackageRealizationLease_ReturnIsIdempotent()
    {
        PackageRootBinding binding = SharedBinding("Repeated.Return");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([binding]);

        Task first = projection.ReturnAsync();
        Task second = projection.ReturnAsync();

        Assert.Same(first, second);
        await first;
        Assert.Same(first, projection.ReturnAsync());
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationRelease_WaitsForEveryLease()
    {
        PackageRootBinding binding = SharedBinding("Every.Lease");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        PackageAssemblyContextProjection first =
            completion.CreateProjection([binding]);
        PackageAssemblyContextProjection second =
            completion.CreateProjection([binding]);

        Task<PackageRoleCleanupReport> close = completion.CloseAsync();
        await first.ReturnAsync();

        Assert.False(close.IsCompleted);
        await second.ReturnAsync();
        await close;
    }

    [Fact]
    public async Task PackageRealizationRelease_UsesPackageRoleCompletionExactlyOnce()
    {
        PackageRootBinding binding = SeparateBinding("Exact.Release");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        var surface = new CountingResource();
        var implementation = new CountingResource();
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(surface);
        completion.ImplementationAssemblyContextGroup!.RegisterOwnedResource(
            implementation);

        await Task.WhenAll(
            completion.CloseAsync(),
            completion.CloseAsync());

        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(1, implementation.DisposeCount);
    }

    [Fact]
    public async Task PackageRealizationCleanupFailure_RemainsVisible()
    {
        PackageRootBinding binding = SharedBinding("Failed.Cleanup");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletion completion =
            await ExecuteAsync(workspace, [binding]);
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
            new ThrowingResource());

        PackageRoleCleanupReport report =
            await completion.CloseAsync();

        var failed = Assert.IsType<
            PackageRoleGroupCleanupRecord.Failed>(
                Assert.Single(report.Groups));
        Assert.Same(completion.SurfaceGroup, failed.Group);
        Assert.Equal(
            "package-role-group-release-failed",
            failed.Diagnostic.Code);
        Assert.DoesNotContain(
            "test cleanup failure",
            failed.Diagnostic.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageRealizationOperation_IsWorkspaceOwnedAndCallerIndependent()
    {
        PackageRootBinding binding = SharedBinding("Caller.Independent");
        using var workspace = new InspectionWorkspace();
        var resume =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding],
                options: null,
                () => new ValueTask(resume.Task));
        Task<PackageAssemblyContextCompletion> physical =
            operation.ExecuteAsync(operation.Identity);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => physical.WaitAsync(callerCancellation.Token));
        Assert.False(physical.IsCompleted);
        resume.SetResult();
        PackageAssemblyContextCompletion completion = await physical;
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = operation.ExecuteAsync(operation.Identity);
            });
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationOperation_CannotRunBeforeInFlightPublication()
    {
        PackageRootBinding binding = SharedBinding("Published.Operation");
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion([binding]);

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = operation.ExecuteAsync(
                    new PackageRoleRealizationOperationId());
            });
        Assert.Equal(0, GroupCount(workspace));

        PackageAssemblyContextCompletion completion =
            await operation.ExecuteAsync(operation.Identity);
        await completion.CloseAsync();
    }

    [Fact]
    public async Task PackageRealizationOperation_HasBoundedCooperativeProgress()
    {
        PackageRootBinding first = SharedBinding("Yield.First");
        PackageRootBinding second = Binding(
            "Yield.Second",
            (
                "lib/net11.0/Yield.Second.dll",
                File.ReadAllBytes(
                    typeof(AssemblyReferenceIdentity).Assembly.Location)));
        using var workspace = new InspectionWorkspace();
        int yields = 0;
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(
                [first, second],
                options: null,
                () =>
                {
                    Interlocked.Increment(ref yields);
                    return ValueTask.CompletedTask;
                });

        PackageAssemblyContextCompletion completion =
            await operation.ExecuteAsync(operation.Identity);

        Assert.Equal(2, yields);
        await completion.CloseAsync();
    }

    static async Task<PackageAssemblyContextCompletion> ExecuteAsync(
        InspectionWorkspace workspace,
        IEnumerable<PackageRootBinding> bindings)
    {
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(bindings);
        return await operation.ExecuteAsync(operation.Identity);
    }

    static PackageRootBinding SharedBinding(string packageId) =>
        Binding(
            packageId,
            (
                $"lib/net11.0/{packageId}.dll",
                File.ReadAllBytes(
                    typeof(PackageAssemblyContextCompletionTests)
                        .Assembly.Location)));

    static PackageRootBinding SeparateBinding(string packageId)
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(PackageAssemblyContextCompletionTests)
                    .Assembly.Location);
        return Binding(
            packageId,
            ($"ref/net11.0/{packageId}.dll", image),
            ($"lib/net11.0/{packageId}.dll", image));
    }

    static PackageRootBinding Binding(
        string packageId,
        params (string Path, byte[] Content)[] entries)
    {
        var content = new InMemoryPackageContent(
            Archive(entries),
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            content,
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(
                payload,
                Framework);
        Assert.True(binding.Root.AssetSelection.IsSelected);
        return binding;
    }

    static PackageRootIdentity CloneRoot(
        PackageRootIdentity root) =>
        new(
            root.PackageId,
            root.PackageVersion,
            root.RequestedTargetFramework,
            root.RequestedRuntimeIdentifier);

    static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination =
                    archive.CreateEntry(
                        path,
                        CompressionLevel.NoCompression)
                    .Open();
                destination.Write(content);
            }
        }
        return buffer.ToArray();
    }

    static int GroupCount(InspectionWorkspace workspace)
    {
        FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    sealed class CountingResource : IDisposable
    {
        int _disposeCount;

        internal int DisposeCount =>
            Volatile.Read(ref _disposeCount);

        public void Dispose() =>
            Interlocked.Increment(ref _disposeCount);
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(
                "test cleanup failure");
    }
}
