using System.IO.Compression;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;
using BrowserMetadataJsonContext = DotnetInspect.Web.Interop.Metadata.BrowserMetadataJsonContext;
using BrowserAnalysisJsonContext = DotnetInspect.Web.Interop.Analysis.BrowserAnalysisJsonContext;
using BrowserSourceJsonContext = DotnetInspect.Web.Interop.Source.BrowserSourceJsonContext;
using BrowserCallGraphJsonContext = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphJsonContext;
using BrowserCatalogJsonContext = DotnetInspect.Web.Interop.Catalog.BrowserCatalogJsonContext;
using BrowserPackageMetadata = DotnetInspect.Web.Interop.Metadata.BrowserPackageMetadata;
using BrowserMetadataCompileLibraryStatus = DotnetInspect.Web.Interop.Metadata.BrowserCompileLibraryStatus;
using BrowserAnalysisCompileLibraryStatus = DotnetInspect.Web.Interop.Analysis.BrowserCompileLibraryStatus;
using BrowserPackageIntegrations = DotnetInspect.Web.Interop.Analysis.BrowserPackageIntegrations;
using BrowserPackageOpportunities = DotnetInspect.Web.Interop.Analysis.BrowserPackageOpportunities;
using BrowserPackagePerformance = DotnetInspect.Web.Interop.Analysis.BrowserPackagePerformance;
using BrowserPerformanceMember = DotnetInspect.Web.Interop.Analysis.BrowserPerformanceMember;
using BrowserOpportunityItem = DotnetInspect.Web.Interop.Analysis.BrowserOpportunityItem;
using BrowserSource = DotnetInspect.Web.Interop.Source.BrowserSource;
using BrowserCallGraph = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraph;
using BrowserCallGraphTarget = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphTarget;
using BrowserCallGraphWireProjection = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphWireProjection;
using BrowserCallGraphDiagnostics = DotnetInspect.Web.Interop.CallGraph.BrowserCallGraphDiagnostics;
using BrowserHomeDemoRunResult = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunResult;
using BrowserHomeDemoRunActivation = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunActivation;
using BrowserHomeDemoRunPlan = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunPlan;
using BrowserProductHomeDemos = DotnetInspect.Web.Interop.Catalog.BrowserProductHomeDemos;
using BrowserHomeDemoRunMember = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunMember;
using BrowserHomeDemoRunRequest = DotnetInspect.Web.Interop.Catalog.BrowserHomeDemoRunRequest;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{

    // A nested Outer+Inner and a type whose own metadata name is literally "Outer+Inner" share a
    // flattened spelling. The browser must carry an identity that tells them apart, and must not
    // publish the flattened one where it names both.
    [Fact]
    public void CallGraphTargets_DistinguishNestedFromLiteralPlusDeclaringTypes()
    {
        TypeRef nested = ResolvedDefinition("Example", ["Outer", "Inner"]);
        TypeRef literalPlus = ResolvedDefinition("Example", ["Outer+Inner"]);
        TypeRef returnType = TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void");
        var nestedMember = new MemberRef(
            nested,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        var literalPlusMember = new MemberRef(
            literalPlus,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            returnType,
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(nestedMember),
                nestedMember,
                "nested",
                CallGraphNodeKind.Normal),
            new(
                1,
                GraphNodeIdentity.FromMember(literalPlusMember),
                literalPlusMember,
                "literal",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTargetInfo[] targets = BrowserCallGraphProjection.Targets(nodes);

        // Both declaring types flatten to the same metadata spelling. That spelling genuinely
        // names the nested type, so it is still published for it; for the literal-plus type it
        // names the other one, so it is withheld rather than published as if it named this one.
        Assert.Equal("Outer+Inner", nested.Name);
        Assert.Equal("Outer+Inner", literalPlus.Name);
        Assert.Equal("Example.Outer+Inner", targets[0].TypeMetadataId);
        Assert.Null(targets[1].TypeMetadataId);

        // The escaped structured identity resolves each target uniquely, and is the same
        // projection a browsable type row carries as its id.
        Assert.Equal("Example.Outer+Inner", targets[0].TypeDefinitionId);
        Assert.Equal(@"Example.Outer\+Inner", targets[1].TypeDefinitionId);
        Assert.NotEqual(targets[0].TypeDefinitionId, targets[1].TypeDefinitionId);
        Assert.Equal(
            targets[0].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer.Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer", "Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
        Assert.Equal(
            targets[1].TypeDefinitionId,
            BrowserSurfaceProjection.Type(
                new ApiType
                {
                    Namespace = "Example",
                    Name = "Outer+Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = DefinitionName("Example", ["Outer+Inner"]),
                    Kind = "class",
                },
                "Example.dll",
                "asset:example",
                "Example").DefinitionId);
    }

    [Fact]
    public void CallGraphTargets_KeepTheLegacyIdentityWhereItIsUnambiguous()
    {
        TypeRef declaring = ResolvedDefinition("Example", ["Outer`1", "Widget`1"]);
        var member = new MemberRef(
            declaring,
            "Run",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.Definition(TypeRef.CoreLibrary, "System", "Void"),
            MemberKind.Method);
        CallGraphNode[] nodes =
        [
            new(
                0,
                GraphNodeIdentity.FromMember(member),
                member,
                "nested",
                CallGraphNodeKind.Normal),
        ];

        BrowserCallGraphTargetInfo[] targets = BrowserCallGraphProjection.Targets(nodes);

        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeMetadataId);
        Assert.Equal("Example.Outer`1+Widget`1", targets[0].TypeDefinitionId);
    }

    [Fact]
    public async Task BrowserWorkspace_SingleCoordinateScopeIsArtifactBacked()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Single.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Single.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.True(scope.ArtifactBacked);
        BrowserWorkspaceParticipant participant =
            Assert.Single(scope.SurfaceParticipants);
        Assert.Same(
            participant,
            scope.SurfaceParticipant(
                coordinate,
                coordinate.DefaultAsset
                    ?? throw new InvalidOperationException(
                        "The artifact coordinate selected no default asset.")));
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(surface.Assemblies.Assemblies));
    }

    [Fact]
    public async Task BrowserWorkspace_CompositeScopeKeepsBindingConsistentRoles()
    {
        byte[] surfaceImage =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        byte[] otherImage =
            File.ReadAllBytes(typeof(BrowserPackage).Assembly.Location);
        string firstId = $"Artifact.Composite.A.{Guid.NewGuid():N}";
        string secondId = $"Artifact.Composite.B.{Guid.NewGuid():N}";
        BrowserPackageCoordinate first = await ArtifactCoordinate(
            firstId,
            Package(surfaceImage, $"lib/net11.0/{firstId}.dll"),
            TestContext.Current.CancellationToken);
        BrowserPackageCoordinate second = await ArtifactCoordinate(
            secondId,
            Package(otherImage, $"lib/net11.0/{secondId}.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [first, second],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.False(scope.ArtifactBacked);
        Assert.Equal(2, scope.SurfaceParticipants.Length);
        Assert.Equal(firstId, scope.Coordinate(first).PackageId);
        Assert.Equal(secondId, scope.Coordinate(second).PackageId);
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.Equal(2, surface.Assemblies.Assemblies.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserWorkspace_ConcurrentScopeOpensShareOneRealization(bool unbound)
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.SingleFlight.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.SingleFlight.dll"),
            TestContext.Current.CancellationToken);

        // Fill the registry so neither open can be admitted immediately: three workspaces whose
        // queries are still running, and one whose retirement is suspended inside disposal.
        var held = new List<BrowserScopeLease<GatedScope>>();
        var settled = new TaskCompletionSource();
        settled.SetResult();
        for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes - 1; index++)
        {
            ScopeReservation reservation =
                await BrowserPackageWorkspace.ReserveScopeAsync(
                    TestContext.Current.CancellationToken);
            held.Add(await BrowserPackageWorkspace.RegisterScopeAsync(
                reservation,
                $"single-flight-holder-{index}-{Guid.NewGuid():N}",
                new GatedScope(settled),
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
        }

        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"single-flight-closing-{Guid.NewGuid():N}",
            closing);
        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;
        Assert.False(removal.IsCompleted);

        Task<BrowserScopeLease<BrowserInspectionScope>> firstOpen =
            OpenAsync();
        Task<BrowserScopeLease<BrowserInspectionScope>> secondOpen =
            OpenAsync();

        // Both callers are genuinely suspended: the single freed slot is not available until the
        // gated retirement settles.
        Assert.False(firstOpen.IsCompleted);
        Assert.False(secondOpen.IsCompleted);

        release.SetResult();
        await removal;
        BrowserScopeLease<BrowserInspectionScope> firstLease = await firstOpen;
        BrowserScopeLease<BrowserInspectionScope> secondLease = await secondOpen;
        BrowserInspectionScope first = firstLease.Scope;
        BrowserInspectionScope second = secondLease.Scope;

        // Only one slot was freed, and both callers hold the one realization it admitted: the
        // caller that resumed second joined the entry rather than demanding a second slot.
        Assert.Same(first, second);
        Assert.NotSame(firstLease, secondLease);
        Assert.True(first.ArtifactBacked);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(first));
        Assert.True(closing.Disposed);

        // Each caller's use is independent: releasing one leaves the other's workspace usable.
        await firstLease.DisposeAsync();
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(second));
        Assert.NotEmpty(
            second.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);
        await secondLease.DisposeAsync();

        foreach (BrowserScopeLease<GatedScope> lease in held)
            await lease.DisposeAsync();

        Task<BrowserScopeLease<BrowserInspectionScope>> OpenAsync() =>
            unbound
                ? BrowserPackageWorkspace.OpenScopeAsync(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    TestContext.Current.CancellationToken)
                : BrowserPackageWorkspace.OpenScopeAsync(
                    [coordinate],
                    TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BrowserWorkspace_ClosingScopeKeepsItsRegistrySlotUntilDisposalSettles()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Closing.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Closing.dll"),
            TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"closing-{Guid.NewGuid():N}",
            closing,
            [BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version)]);
        int occupied = BrowserPackageWorkspace.Stats().Workspaces;

        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;

        Assert.False(removal.IsCompleted);
        Assert.Equal(occupied, BrowserPackageWorkspace.Stats().Workspaces);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(closing));
        Assert.Throws<InvalidOperationException>(
            () => BrowserPackageWorkspace.LeaseScope(closing));

        release.SetResult();
        await removal;

        Assert.Equal(occupied - 1, BrowserPackageWorkspace.Stats().Workspaces);
        Assert.True(closing.Disposed);
    }

    [Fact]
    public async Task BrowserWorkspace_PackageEvictionAwaitsScopeClosedByAnotherPath()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Racing.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Racing.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        GatedScope[] closing =
        [
            .. Enumerable.Range(0, BrowserPackageWorkspace.MaxOpenScopes)
                .Select(_ => new GatedScope(release)),
        ];
        foreach (GatedScope scope in closing)
        {
            await BrowserPackageWorkspace.RegisterScopeAsync(
                $"racing-{Guid.NewGuid():N}",
                scope,
                [packageKey]);
        }

        Task[] removals =
        [
            .. closing.Select(scope =>
                BrowserPackageWorkspace.RemoveScopeAsync(scope).AsTask()),
        ];
        await Task.WhenAll(closing.Select(scope => scope.DisposeStarted.Task));
        Assert.All(removals, removal => Assert.False(removal.IsCompleted));

        Task<BrowserPackageWorkspace.PackageDownloadReservation> pressure =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.racing.pressure.{Guid.NewGuid():N}@1.0.0",
                120L * MiB).AsTask();

        Assert.False(pressure.IsCompleted);
        Assert.Contains(
            packageKey,
            BrowserPackageWorkspace.ResidentPackageKeys());

        release.SetResult();
        using (await pressure)
        {
            await Task.WhenAll(removals);
            Assert.All(closing, scope => Assert.True(scope.Disposed));
            Assert.DoesNotContain(
                packageKey,
                BrowserPackageWorkspace.ResidentPackageKeys());
        }
    }

    [Fact]
    public async Task WorkspaceOccurrences_LeaseAcquiredDuringRetirementKeepsArchiveResident()
    {
        await BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.occurrence.drain.{Guid.NewGuid():N}@1.0.0",
            128L * MiB)).Dispose();

        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.OccurrenceLease.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/DotnetInspect.Web.Tests.dll"),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(coordinate.PackageId, coordinate.Version);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"occurrence-retirement-{Guid.NewGuid():N}",
            closing,
            [packageKey]);
        Task<BrowserPackageWorkspace.PackageDownloadReservation> pressure =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.occurrence.pressure.{Guid.NewGuid():N}@1.0.0",
                128L * MiB).AsTask();
        try
        {
            await closing.DisposeStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            BrowserWorkspacePackageOccurrenceView view =
                await BrowserWorkspaceOccurrenceOperations.QueryAsync(
                    [new BrowserPackageRequest(
                        coordinate.PackageId,
                        coordinate.Version,
                        coordinate.Framework)]);

            release.TrySetResult();
            InvalidOperationException error =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    using var unexpected = await pressure;
                });
            Assert.Contains("package-cache limit", error.Message);
            Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());

            BrowserWorkspacePackageOccurrenceActivation activation =
                JsonSerializer.Deserialize(
                    await DotnetInspect.Web.Interop.Package.PackageExports.ActivateWorkspacePackageOccurrence(
                        Assert.Single(view.Occurrences).Action),
                    BrowserPackageJsonContext.Default
                        .BrowserWorkspacePackageOccurrenceActivation)!;
            Assert.True(activation.Activated);
            Assert.False(activation.Superseded);
            Assert.NotNull(activation.Package);
        }
        finally
        {
            release.TrySetResult();
            await BrowserWorkspaceOccurrenceOperations.ClearCurrent();
            await BrowserPackageWorkspace.RemoveScopeAsync(closing);
        }
    }

    [Fact]
    public async Task PackageCacheEntryBudget_CoversChargedRealizationEnvelope()
    {
        (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"entry.budget.drain.{Guid.NewGuid():N}@1.0.0",
            128L * MiB)).Dispose();

        BrowserPackageCacheSnapshot stats = BrowserPackageWorkspace.Stats();
        int expectedEntries = checked(
            WorkspaceScopeLimits.DefaultMaxPackages
            * BrowserWorkspaceRealizationHost.MaxChargedRealizations);
        Assert.Equal(expectedEntries, stats.MaxPackageEntries);

        var reservations =
            new List<BrowserPackageWorkspace.PackageDownloadReservation>(
                expectedEntries);
        try
        {
            for (int index = 0; index < expectedEntries; index++)
            {
                reservations.Add(
                    await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        $"entry.budget.{index}.{Guid.NewGuid():N}@1.0.0",
                        declaredLength: 0));
            }

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        $"entry.budget.rejected.{Guid.NewGuid():N}@1.0.0",
                        declaredLength: 0).AsTask());
            Assert.Contains(
                "package-cache limit",
                failure.Message,
                StringComparison.Ordinal);
            Assert.Equal(0, BrowserPackageWorkspace.Stats().ResidentBytes);
        }
        finally
        {
            foreach (
                BrowserPackageWorkspace.PackageDownloadReservation reservation
                in reservations)
            {
                reservation.Dispose();
            }
        }
    }

    [Fact]
    public async Task BrowserWorkspace_ConcurrentReservationsStayWithinTheByteBudget()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Budget.{Guid.NewGuid():N}";

        // Drain every unleased archive first: this gate measures the budget its own reservations
        // and its own dependent workspace occupy, not whatever an earlier test left resident.
        (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.budget.drain.{Guid.NewGuid():N}@1.0.0",
            128L * MiB)).Dispose();

        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Budget.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new GatedScope(release);
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"budget-{Guid.NewGuid():N}",
            closing,
            [BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version)]);
        Task removal = BrowserPackageWorkspace.RemoveScopeAsync(closing).AsTask();
        await closing.DisposeStarted.Task;

        Task<BrowserPackageWorkspace.PackageDownloadReservation> first =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.budget.a.{Guid.NewGuid():N}@1.0.0",
                70L * MiB).AsTask();
        Task<BrowserPackageWorkspace.PackageDownloadReservation> second =
            BrowserPackageWorkspace.ReservePackageDownloadAsync(
                $"artifact.budget.b.{Guid.NewGuid():N}@1.0.0",
                70L * MiB).AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        release.SetResult();
        await removal;

        BrowserPackageWorkspace.PackageDownloadReservation? admittedFirst =
            await TryReserve(first);
        BrowserPackageWorkspace.PackageDownloadReservation? admittedSecond =
            await TryReserve(second);
        try
        {
            Assert.True(
                BrowserPackageWorkspace.Stats().ResidentBytes <= 128L * MiB,
                "Concurrent reservations overshot the browser package-cache byte budget.");
            Assert.True(admittedFirst is not null || admittedSecond is not null);
            Assert.True(admittedFirst is null || admittedSecond is null);
        }
        finally
        {
            admittedFirst?.Dispose();
            admittedSecond?.Dispose();
        }

        static async Task<BrowserPackageWorkspace.PackageDownloadReservation?> TryReserve(
            Task<BrowserPackageWorkspace.PackageDownloadReservation> reservation)
        {
            try
            {
                return await reservation;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A terminal cleanup failure leaves the entry charged and unavailable with an observable
    /// failure record, and the archive it depended on stays charged with it: the registry never
    /// hands that capacity to a later workspace, and a runtime restart is the only recovery.
    /// </summary>
    [Fact]
    public async Task BrowserWorkspace_FailedScopeCloseStaysChargedAndUnavailable()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.CloseFailure.{Guid.NewGuid():N}";
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.CloseFailure.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        string packageKey =
            BrowserPackageWorkspace.PackageKey(packageId, coordinate.Version);
        var failing = new FailingScope();
        await BrowserPackageWorkspace.RegisterScopeAsync(
            $"close-failure-{Guid.NewGuid():N}",
            failing,
            [packageKey]);
        BrowserScopeLease<FailingScope> lease =
            BrowserPackageWorkspace.LeaseScope(failing);
        await BrowserPackageWorkspace.RemoveScopeAsync(failing);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(failing));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await lease.DisposeAsync());

        Assert.Contains(
            "The gated browser scope failed to close.",
            failure.Message,
            StringComparison.Ordinal);
        Assert.False(BrowserPackageWorkspace.IsScopeRetained(failing));
        Assert.Equal(1, BrowserPackageWorkspace.QuarantinedWorkspaces);

        // The entry stays charged: the archive it depended on is not handed back to the cache,
        // and an admission that needs its capacity is rejected with the recorded failure.
        Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());
        var pressure = new List<BrowserScopeLease<BrowserInspectionScope>>();
        InvalidOperationException? rejection = null;
        try
        {
            for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes; index++)
            {
                string pressureId = $"Artifact.CloseFailure.Pressure.{index}.{Guid.NewGuid():N}";
                pressure.Add(await BrowserPackageWorkspace.OpenScopeAsync(
                    [
                        await ArtifactCoordinate(
                            pressureId,
                            Package(image, $"lib/net11.0/{pressureId}.dll"),
                            TestContext.Current.CancellationToken),
                    ],
                    TestContext.Current.CancellationToken));
            }
        }
        catch (InvalidOperationException capacityFailure)
        {
            rejection = capacityFailure;
        }
        finally
        {
            foreach (BrowserScopeLease<BrowserInspectionScope> held in pressure)
                await held.DisposeAsync();
        }

        Assert.NotNull(rejection);
        Assert.Contains(
            "stay charged after a terminal cleanup failure",
            rejection!.Message,
            StringComparison.Ordinal);

        try
        {
            InvalidOperationException archiveRejection =
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                        $"artifact.close-failure.archive-pressure.{Guid.NewGuid():N}@1.0.0",
                        120L * MiB));
            Assert.Contains(
                "failed to release its retained content",
                archiveRejection.Message,
                StringComparison.Ordinal);
            Assert.Contains(packageKey, BrowserPackageWorkspace.ResidentPackageKeys());
        }
        finally
        {
            BrowserPackageWorkspace.SimulateRuntimeRestart();
        }
        Assert.Equal(0, BrowserPackageWorkspace.QuarantinedWorkspaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserWorkspace_DuplicateCandidateRetiresItsOwnReservation(bool cleanupFails)
    {
        var settled = new TaskCompletionSource();
        settled.SetResult();
        var retained = new GatedScope(settled);
        string key = $"duplicate-candidate-{Guid.NewGuid():N}";
        await using ScopeReservation firstReservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<GatedScope> first =
            await BrowserPackageWorkspace.RegisterScopeAsync(
                firstReservation,
                key,
                retained,
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));
        await using ScopeReservation duplicateReservation =
            await BrowserPackageWorkspace.ReserveScopeAsync(
                TestContext.Current.CancellationToken);
        int occupied = BrowserPackageWorkspace.Stats().Workspaces;
        var failing = new FailingScope();
        var healthy = new GatedScope(settled);
        IAsyncDisposable candidate = cleanupFails ? failing : healthy;

        try
        {
            if (cleanupFails)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await BrowserPackageWorkspace.RegisterScopeAsync(
                        duplicateReservation,
                        key,
                        candidate,
                        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)));
                Assert.Equal(1, failing.DisposalCount);
                Assert.Equal(occupied, BrowserPackageWorkspace.Stats().Workspaces);
                Assert.Equal(1, BrowserPackageWorkspace.QuarantinedWorkspaces);
            }
            else
            {
                await using BrowserScopeLease<IAsyncDisposable> joined =
                    await BrowserPackageWorkspace.RegisterScopeAsync(
                        duplicateReservation,
                        key,
                        candidate,
                        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));
                Assert.Same(retained, joined.Scope);
                Assert.True(healthy.Disposed);
                Assert.Equal(occupied - 1, BrowserPackageWorkspace.Stats().Workspaces);
            }

            await BrowserPackageWorkspace.RemoveScopeAsync(retained);
            await first.DisposeAsync();
            Assert.True(retained.Disposed);
        }
        finally
        {
            BrowserPackageWorkspace.SimulateRuntimeRestart();
        }
    }

    [Fact]
    public async Task BrowserWorkspace_CancelledScopeOpenYieldsNoScopeAndKeepsRegistryUsable()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Waiter.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Waiter.dll"),
            TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                cancelled.Token));

        await using BrowserScopeLease<BrowserInspectionScope> openedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope opened = openedLease.Scope;

        Assert.True(opened.ArtifactBacked);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(opened));
    }

    [Fact]
    public async Task BrowserWorkspace_ArtifactScopeDisposalClosesItsSession()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Disposal.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Disposal.dll"),
            TestContext.Current.CancellationToken);
        BrowserInspectionScope scope =
            await BrowserInspectionScope.CreateAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        Assert.True(scope.ArtifactBacked);
        Assert.Single(scope.SurfaceParticipants);

        await scope.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => scope.UseSurface(
                group => AssemblyContextApiSurfaceQuery.Execute(group)));
        await scope.DisposeAsync();
    }

    [Fact]
    public async Task BrowserWorkspace_ArtifactScopeKeepsRejectedParticipantVisible()
    {
        BrowserPackageCoordinate malformed = await ArtifactCoordinate(
            $"Artifact.Malformed.{Guid.NewGuid():N}",
            Package([0x01, 0x02, 0x03], "lib/net11.0/Artifact.Malformed.dll"),
            TestContext.Current.CancellationToken);

        await using BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [malformed],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;

        Assert.True(scope.ArtifactBacked);
        Assert.Single(scope.SurfaceParticipants);
        AssemblyContextApiSurfaceResult surface = scope.UseSurface(
            group => AssemblyContextApiSurfaceQuery.Execute(group));
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(surface.Assemblies.Assemblies));
    }

    [Fact]
    public async Task BrowserWorkspace_ReplacedArchiveRejectsStaleArtifactCoordinate()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Stale.{Guid.NewGuid():N}";
        BrowserPackageCoordinate stale = await ArtifactCoordinate(
            packageId,
            Package(image, "lib/net11.0/Artifact.Stale.dll"),
            TestContext.Current.CancellationToken);

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, "lib/net11.0/Artifact.Stale.Replacement.dll"),
                fromCache: false));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await BrowserPackageWorkspace.OpenScopeAsync(
                    [stale],
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "escaped aggregate cache accounting",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserWorkspace_CacheRoomAwaitsDependentScopeDisposal()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Evicted.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Evicted.dll", 60 * MiB),
            TestContext.Current.CancellationToken);
        BrowserScopeLease<BrowserInspectionScope> scopeLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope scope = scopeLease.Scope;
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));

        // The lease is the caller's protection: archive pressure that could only be satisfied by
        // dropping the archive this query is reading is rejected instead of silently evicting it.
        InvalidOperationException rejected =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await BrowserPackageWorkspace.ReservePackageDownloadAsync(
                    $"artifact.pressure.{Guid.NewGuid():N}@1.0.0",
                    120L * MiB));
        Assert.Contains(
            "cannot accommodate the requested workspace",
            rejected.Message,
            StringComparison.Ordinal);
        Assert.True(BrowserPackageWorkspace.IsScopeRetained(scope));
        Assert.NotEmpty(
            scope.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);

        // Once the protected use has been released the same pressure is satisfied by awaiting the
        // dependent workspace's disposal, and only then are the retained bytes counted as free.
        await scopeLease.DisposeAsync();
        using (await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"artifact.pressure.{Guid.NewGuid():N}@1.0.0",
            120L * MiB))
        {
            Assert.False(BrowserPackageWorkspace.IsScopeRetained(scope));
            Assert.Throws<ObjectDisposedException>(
                () => scope.UseSurface(
                    group => AssemblyContextApiSurfaceQuery.Execute(group)));
        }
    }

    [Fact]
    public async Task BrowserWorkspace_RepeatedUnboundRequestsJoinOneRetainedBinding()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Unbound.{Guid.NewGuid():N}";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);

        // The second unbound request joined the retained workspace before a second selection
        // token could be issued, so both callers read the one artifact-backed realization.
        Assert.Same(firstLease.Scope, secondLease.Scope);
        Assert.True(firstLease.Scope.ArtifactBacked);
        Assert.Single(firstLease.Scope.SurfaceParticipants);
    }

    [Fact]
    public async Task BrowserWorkspace_IndependentlyIssuedBindingsDoNotJoinOnLabelMatch()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Issued.{Guid.NewGuid():N}";
        BrowserPackageCoordinate first = await ArtifactCoordinate(
            packageId,
            Package(image, $"lib/net11.0/{packageId}.dll"),
            TestContext.Current.CancellationToken);
        BrowserPackageCoordinate second = await BrowserPackageWorkspace.ResolveAsync(
            packageId,
            "1.0.0",
            "net11.0",
            TestContext.Current.CancellationToken);
        Assert.NotNull(second.Binding);
        Assert.NotSame(first.Binding!.SelectionIdentity, second.Binding!.SelectionIdentity);

        await using BrowserScopeLease<BrowserInspectionScope> firstLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [first],
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> secondLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [second],
                TestContext.Current.CancellationToken);

        // Two independently issued selection tokens name the same labels but are not
        // interchangeable: each keeps its own workspace.
        Assert.NotSame(firstLease.Scope, secondLease.Scope);
        Assert.True(firstLease.Scope.ArtifactBacked);
        Assert.True(secondLease.Scope.ArtifactBacked);
    }

    [Fact]
    public async Task BrowserWorkspace_DefaultAndExplicitSelectionRequestsDoNotJoin()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        string packageId = $"Artifact.Selection.{Guid.NewGuid():N}";
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                "1.0.0",
                Package(image, $"lib/net11.0/{packageId}.dll"),
                fromCache: false));

        await using BrowserScopeLease<BrowserInspectionScope> defaultLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                targetFramework: null,
                TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> explicitLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                "1.0.0",
                "net11.0",
                TestContext.Current.CancellationToken);

        // A default selection is not an explicit one, even when it resolves to the same
        // framework: the two requests keep separate workspaces.
        Assert.NotSame(defaultLease.Scope, explicitLease.Scope);
    }

    [Fact]
    public async Task BrowserWorkspace_ProtectedUseSurvivesWorkspacePressureAcrossAnAsyncReturn()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate protectedCoordinate = await ArtifactCoordinate(
            $"Artifact.Protected.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Protected.dll"),
            TestContext.Current.CancellationToken);
        await using BrowserScopeLease<BrowserInspectionScope> protectedLease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                [protectedCoordinate],
                TestContext.Current.CancellationToken);
        BrowserInspectionScope protectedScope = protectedLease.Scope;

        // Every later workspace competes for the remaining slots and forces eviction. The
        // protected use is what keeps this caller's workspace out of every candidate set.
        for (int index = 0; index < BrowserPackageWorkspace.MaxOpenScopes + 1; index++)
        {
            BrowserPackageCoordinate pressure = await ArtifactCoordinate(
                $"Artifact.Pressure.{index}.{Guid.NewGuid():N}",
                Package(image, $"lib/net11.0/Artifact.Pressure.{index}.dll"),
                TestContext.Current.CancellationToken);
            await using BrowserScopeLease<BrowserInspectionScope> transient =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    [pressure],
                    TestContext.Current.CancellationToken);
            Assert.True(BrowserPackageWorkspace.IsScopeRetained(protectedScope));
        }

        Assert.True(BrowserPackageWorkspace.IsScopeRetained(protectedScope));
        Assert.NotEmpty(
            protectedScope.UseSurface(group => AssemblyContextApiSurfaceQuery.Execute(group))
                .Assemblies
                .Assemblies);
    }

    [Fact]
    public async Task BrowserWorkspace_CancelledWaiterLeavesTheOtherWaiterUnaffected()
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Waiters.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Waiters.dll"),
            TestContext.Current.CancellationToken);

        await using ScopeAdmissionGate admission =
            await ScopeAdmissionGate.CreateAsync();

        using var abandoning = new CancellationTokenSource();
        Task<BrowserScopeLease<BrowserInspectionScope>> abandoned =
            BrowserPackageWorkspace.OpenScopeAsync([coordinate], abandoning.Token);
        Task<BrowserScopeLease<BrowserInspectionScope>> waiting =
            BrowserPackageWorkspace.OpenScopeAsync(
                [coordinate],
                TestContext.Current.CancellationToken);
        Assert.False(abandoned.IsCompleted);
        Assert.False(waiting.IsCompleted);

        BrowserScopeLease<BrowserInspectionScope>? lease = null;
        try
        {
            await abandoning.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await abandoned.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
            Assert.False(waiting.IsCompleted);
            Assert.False(admission.Retirement.IsCompleted);

            admission.Release();
            lease = await waiting;
            Assert.True(lease.Scope.ArtifactBacked);
            Assert.True(BrowserPackageWorkspace.IsScopeRetained(lease.Scope));
        }
        finally
        {
            admission.Release();
            lease ??= await waiting;
            await lease.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkspaceOccurrences_ActivationCannotOutliveItsView(bool replace)
    {
        byte[] image =
            File.ReadAllBytes(typeof(BrowserEngineBoundaryTests).Assembly.Location);
        BrowserPackageCoordinate coordinate = await ArtifactCoordinate(
            $"Artifact.Activation.{Guid.NewGuid():N}",
            Package(image, "lib/net11.0/Artifact.Activation.dll"),
            TestContext.Current.CancellationToken);
        BrowserWorkspacePackageOccurrenceView view =
            await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([coordinate]);
        await using ScopeAdmissionGate admission =
            await ScopeAdmissionGate.CreateAsync();

        Task<string> activation = DotnetInspect.Web.Interop.Package.PackageExports.ActivateWorkspacePackageOccurrence(
            Assert.Single(view.Occurrences).Action);
        try
        {
            Assert.False(activation.IsCompleted);
            if (replace)
                await BrowserWorkspaceOccurrenceOperations.ReplaceCurrent([coordinate]);
            else
                await BrowserWorkspaceOccurrenceOperations.ClearCurrent();

            admission.Release();
            BrowserWorkspacePackageOccurrenceActivation result =
                JsonSerializer.Deserialize(
                    await activation,
                    BrowserPackageJsonContext.Default
                        .BrowserWorkspacePackageOccurrenceActivation)!;
            Assert.False(result.Activated);
            Assert.True(result.Superseded);
            Assert.Null(result.Package);
        }
        finally
        {
            admission.Release();
            await activation;
            await BrowserWorkspaceOccurrenceOperations.ClearCurrent();
        }
    }
}
