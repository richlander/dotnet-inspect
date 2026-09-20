using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformRealPackageTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        GalleryReferencePopulationMaterializesAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "gallery-reference-population");
        PlatformHouseRequest request =
            ReferencePopulationRequest(adapter, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: reference.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: reference.Value.Libraries.Sum(
                static library => library.ContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        consumed));

        Assert.Equal(
            reference.Value.Libraries.Select(
                static library => library.Identity),
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.Equal(
            reference.Value.Libraries.Length,
            completed.Population.Owners.Count);
        Assert.Same(
            reference.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        Assert.All(
            completed.Population.Value.Libraries,
            static library =>
                Assert.Null(library.ImplementationAssembly));

        LibraryReference json = Assert.Single(
            completed.Population.Value.Libraries,
            static library =>
                library.ApiAssembly.AssemblyIdentity!.Identity.Name
                == "System.Text.Json");
        var provenance =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        json.ApiAssembly.ArtifactReference.Provenance)
                    .SourceProvenance);
        Assert.Equal(
            "ref/net11.0/System.Text.Json.dll",
            provenance.Path);
        Assert.Same(reference.Value.Candidate, provenance.Candidate);
        Assert.Same(reference.Value.Authority, provenance.Authority);
        Assert.Same(
            reference.Value.ContentGeneration,
            provenance.ContentGeneration);

        ValueTask sourceRetirement = root.DisposeAsync();
        Assert.True(sourceRetirement.IsCompletedSuccessfully);
        await sourceRetirement;
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        int jsonIndex =
            completed.Population.Value.Libraries.ToList().IndexOf(json);
        using LibraryOperationLease operation = Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    completed.Population.Owners[jsonIndex]
                        .IssueOperationLease(json))
            .Lease;
        Assert.Equal(
            ((byte)'M', (byte)'Z'),
            operation.Snapshot(
                json.ApiAssembly,
                static (view, _) =>
                    (view.Content[0], view.Content[1]),
                cancellationToken));
        operation.Dispose();
        foreach (
            LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        GalleryImplementationPopulationMaterializesAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "gallery-implementation-population");
        PlatformHouseRequest request =
            ImplementationPopulationRequest(adapter, cancellationToken);

        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: implementation.Value.Frameworks.Length,
            targetCandidates: 0,
            assemblies: implementation.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: implementation.Value.Libraries.Sum(
                static library => library.ContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        consumed));

        Assert.Equal(
            implementation.Value.Libraries.Select(
                static library => library.Identity),
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.Equal(
            implementation.Value.Libraries.Length,
            completed.Population.Owners.Count);
        Assert.Same(
            implementation.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        Assert.All(
            completed.Population.Value.Libraries,
            static library =>
            {
                Assert.Same(
                    library.ApiAssembly,
                    library.ImplementationAssembly);
                Assert.Equal(2, library.ApiAssembly.Roles.Count);
            });

        LibraryReference json = Assert.Single(
            completed.Population.Value.Libraries,
            static library =>
                library.ApiAssembly.AssemblyIdentity!.Identity.Name
                == "System.Text.Json");
        PackageImplementationLibrary sourceJson = Assert.Single(
            implementation.Value.Libraries,
            static library =>
                library.Identity.Name == "System.Text.Json");
        var provenance =
            Assert.IsType<PackageImplementationArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        json.ApiAssembly.ArtifactReference.Provenance)
                    .SourceProvenance);
        Assert.Equal(
            PackagePlatformTestEnvironment
                .RuntimeImplementationPackageId,
            provenance.PackageId);
        Assert.Same(sourceJson.Framework.Candidate, provenance.Candidate);
        Assert.Same(sourceJson.Framework.Authority, provenance.Authority);
        Assert.Same(
            sourceJson.Framework.ContentGeneration,
            provenance.ContentGeneration);
        Assert.Equal(sourceJson.ContentDigest, provenance.ContentDigest);

        ValueTask sourceRetirement = root.DisposeAsync();
        Assert.True(sourceRetirement.IsCompletedSuccessfully);
        await sourceRetirement;
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        int jsonIndex =
            completed.Population.Value.Libraries.ToList().IndexOf(json);
        using LibraryOperationLease operation = Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    completed.Population.Owners[jsonIndex]
                        .IssueOperationLease(json))
            .Lease;
        Assert.Equal(
            ((byte)'M', (byte)'Z'),
            operation.Snapshot(
                json.ApiAssembly,
                static (view, _) =>
                    (view.Content[0], view.Content[1]),
                cancellationToken));
        operation.Dispose();
        foreach (
            LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        GalleryRuntimePopulationMaterializesPairedAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "gallery-population");
        PlatformHouseRequest request =
            PairedPopulationRequest(adapter, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1 + implementation.Value.Frameworks.Length,
            targetCandidates: 0,
            assemblies:
                reference.Value.Libraries.Length
                + implementation.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes:
                reference.Value.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Value.Libraries.Sum(
                    static library => library.ContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        consumed));

        var referenceIdentities =
            new HashSet<AssemblyReferenceIdentity>(
                reference.Value.Libraries.Select(
                    static library => library.Identity),
                AssemblyReferenceIdentity.EquivalentComparer);
        AssemblyReferenceIdentity[] expected =
        [
            .. reference.Value.Libraries.Select(
                static library => library.Identity),
            .. implementation.Value.Libraries
                .Where(
                    library => !referenceIdentities.Contains(
                        library.Identity))
                .Select(static library => library.Identity),
        ];
        Assert.Equal(
            expected,
            completed.Population.Value.Libraries.Select(
                    static library =>
                        library.ApiAssembly.AssemblyIdentity!.Identity)
                .ToArray(),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.Equal(expected.Length, completed.Population.Owners.Count);
        Assert.Equal(
            [reference.Contribution, implementation.Contribution],
            completed.Population.Receipt.HouseReceipt.SourceSettlements
                .Select(static settlement => settlement.Contribution));

        LibraryReference json = Assert.Single(
            completed.Population.Value.Libraries,
            static library =>
                library.ApiAssembly.AssemblyIdentity!.Identity.Name
                == "System.Text.Json");
        Assert.NotNull(json.ImplementationAssembly);
        Assert.Equal(
            "ref/net11.0/System.Text.Json.dll",
            Assert.IsType<PackageReferenceArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            json.ApiAssembly.ArtifactReference.Provenance)
                        .SourceProvenance)
                .Path);
        Assert.Equal(
            PackagePlatformTestEnvironment
                .RuntimeImplementationPackageId,
            Assert.IsType<PackageImplementationArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            json.ImplementationAssembly!.ArtifactReference
                                .Provenance)
                        .SourceProvenance)
                .PackageId);

        ValueTask sourceRetirement = root.DisposeAsync();
        Assert.True(sourceRetirement.IsCompletedSuccessfully);
        await sourceRetirement;
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        using LibraryOperationLease operation = Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    completed.Population.Owners[
                            completed.Population.Value.Libraries
                                .ToList()
                                .IndexOf(json)]
                        .IssueOperationLease(json))
            .Lease;
        Assert.Equal(
            ((byte)'M', (byte)'M'),
            operation.SnapshotPair(
                json.ApiAssembly,
                json.ImplementationAssembly,
                static (view, _) =>
                    (view.First.Content[0],
                        view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        foreach (
            LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        GallerySystemTextJsonMaterializesPairedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "gallery-library");
        PlatformHouseRequest request = PairedLibraryRequest(
            adapter,
            ReadIdentity(
                typeof(JsonSerializer).Assembly.Location),
            cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1 + implementation.Value.Frameworks.Length,
            targetCandidates: 0,
            assemblies:
                reference.Value.Libraries.Length
                + implementation.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes:
                reference.Value.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Value.Libraries.Sum(
                    static library => library.ContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        consumed));
        LibraryReference library = completed.Library.Value.Reference;
        Assert.Equal(
            "ref/net11.0/System.Text.Json.dll",
            Assert.IsType<PackageReferenceArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance)
                .Path);
        Assert.Equal(
            PackagePlatformTestEnvironment
                .RuntimeImplementationPackageId,
            Assert.IsType<PackageImplementationArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ImplementationAssembly!
                                .ArtifactReference.Provenance)
                        .SourceProvenance)
                .PackageId);

        ValueTask sourceRetirement = root.DisposeAsync();
        Assert.True(sourceRetirement.IsCompletedSuccessfully);
        await sourceRetirement;
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        using LibraryOperationLease operation = Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    completed.Library.Owner.IssueOperationLease(
                        library))
            .Lease;
        Assert.Equal(
            ((byte)'M', (byte)'M'),
            operation.SnapshotPair(
                library.ApiAssembly,
                library.ImplementationAssembly,
                static (view, _) =>
                    (view.First.Content[0],
                        view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task GalleryReferencePackDiscoveryRealizationAndDetachedLifetime()
    {
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(source, "gallery-reference");
        PlatformFamilyTarget target = Target();
        PlatformHouseRequest selecting = SelectingRequest(
            adapter,
            TestContext.Current.CancellationToken);

        var discovery = Assert.IsType<
            PackagePlatformHouseResult<PackagePlatformTargetInventory>.Succeeded>(
                await adapter.DiscoverTargetsAsync(
                    selecting,
                    root.IssueOperationLease(
                        selecting.CancellationToken,
                        operationTimeout: selecting.Work.MaxDuration)));
        PackagePlatformTargetSelection selection =
            discovery.Value.SelectTarget(target);
        var realized = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.Succeeded>(
                await adapter.RealizeReferenceAsync(
                    selecting,
                    discovery,
                    selection,
                    root.IssueOperationLease(
                        selecting.CancellationToken,
                        operationTimeout: selecting.Work.MaxDuration)));

        Assert.Same(selection.Candidate, realized.Value.Candidate);
        Assert.Equal(PackageAcquisitionCandidateKind.Discovered, realized.Value.Candidate.Kind);
        Assert.Equal(176, realized.Value.Libraries.Length);
        PackageReferenceLibrary json = Assert.Single(
            realized.Value.Libraries,
            library => library.Path == "ref/net11.0/System.Text.Json.dll");
        Assert.Equal(87_848, json.ContentLength);
        var contribution = Assert.IsType<PlatformSourceContribution.Realization>(
            realized.Contribution);
        Assert.Same(selecting.Snapshot, contribution.Request);
        Assert.Equal(target, contribution.Target);

        PlatformHouseRequest zeroBytes = ExactRequest(
            adapter,
            target,
            maxBytes: 0,
            TestContext.Current.CancellationToken);
        var incomplete = Assert.IsType<
            PackagePlatformHouseResult<PackageReferenceRealization>.NotSucceeded>(
                await adapter.RealizeReferenceAsync(
                    zeroBytes,
                    root.IssueOperationLease(
                        zeroBytes.CancellationToken,
                        operationTimeout: zeroBytes.Work.MaxDuration)));
        Assert.Equal(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, incomplete.Diagnostic.Kind);
        Assert.Equal(PlatformSourceContributionKind.Incomplete, incomplete.Contribution.Kind);

        ValueTask close = root.DisposeAsync();
        Assert.True(close.IsCompletedSuccessfully);
        await close;
        byte[] bytes = await PackagePlatformTestData.ReadAllAsync(json);
        Assert.Equal(87_848, bytes.Length);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'Z', bytes[1]);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task GalleryAspNetRuntimeClosureAndDetachedLifetime()
    {
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize([PackageSource.NuGetOrg]);
        var authorization = new TestAuthorization(sources);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            authorization,
            new PackagePayloadAcquisitionPlan((_, _) => store));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "gallery-runtime");
        PlatformFamilyTarget target = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));
        PlatformHouseRequest request = ImplementationRequest(
            adapter,
            target,
            TestContext.Current.CancellationToken);

        var realized = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        root.IssueOperationLease(
                            request.CancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        Assert.Equal(
            [
                "Microsoft.NETCore.App",
                "Microsoft.AspNetCore.App",
            ],
            realized.Value.Frameworks.Select(
                static framework => framework.Name.Value));
        Assert.Equal(
            [
                PackagePlatformTestEnvironment
                    .RuntimeImplementationPackageId,
                PackagePlatformTestEnvironment
                    .AspNetImplementationPackageId,
            ],
            realized.Value.Frameworks.Select(
                static framework => framework.PackageId));
        PackageImplementationLibrary json = Assert.Single(
            realized.Value.Libraries,
            library => library.Identity.Name == "System.Text.Json");
        Assert.Contains(
            realized.Value.Libraries,
            library =>
                library.Identity.Name
                == "Microsoft.AspNetCore.Hosting");
        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                realized.Contribution);
        Assert.Equal(
            PlatformSourceFacet.Implementation,
            contribution.Facet);
        Assert.Equal(target, contribution.Target);
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: realized.Value.Frameworks.Length,
            targetCandidates: 0,
            assemblies: realized.Value.Libraries.Length,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: realized.Value.Libraries.Sum(
                static library => library.ContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        realized,
                        consumed));
        PlatformPopulationMember runtime = Assert.Single(
            completed.Population.Value.Members,
            member =>
                member.Library.ApiAssembly.AssemblyIdentity!.Identity.Name
                    == "System.Text.Json");
        Assert.Equal(
            PlatformPopulationMemberRole.BindingSupport,
            runtime.Role);
        Assert.Equal(
            PlatformFamily.DotNetRuntime,
            runtime.Target.Family);
        PlatformPopulationMember aspNet = Assert.Single(
            completed.Population.Value.Members,
            member =>
                member.Library.ApiAssembly.AssemblyIdentity!.Identity.Name
                    == "Microsoft.AspNetCore.Hosting");
        Assert.Equal(PlatformPopulationMemberRole.Focus, aspNet.Role);
        Assert.Equal(PlatformFamily.AspNetCore, aspNet.Target.Family);

        ValueTask close = root.DisposeAsync();
        Assert.True(close.IsCompletedSuccessfully);
        await close;
        byte[] bytes =
            await PackagePlatformTestData.ReadAllAsync(json);
        Assert.True(bytes.Length > 0);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'Z', bytes[1]);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(
            TestContext.Current.CancellationToken);
    }

    private static PlatformHouseRequest SelectingRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("gallery-reference"),
            new PlatformTargetDemand.Selecting(
                PlatformFamily.DotNetRuntime,
                PlatformTargetFramework.Parse("net11.0"),
                new PlatformVersionSelectionDemand.Requirement(
                    PlatformVersionRequirementIdentity.Create(
                        PackagePlatformTestEnvironment.Version)),
                [adapter.TargetDiscovery],
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 1024,
                    maxComparisons: 2048)),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("gallery-reference"),
                PlatformSourcePolicyGeneration.Create("gallery-reference"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                ]),
            Work(maxBytes: 512L * 1024 * 1024),
            cancellationToken);

    private static PlatformHouseRequest PairedLibraryRequest(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("gallery-library"),
            new PlatformTargetDemand.Exact(Target()),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.ReferenceAndImplementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("gallery-library"),
                PlatformSourcePolicyGeneration.Create(
                    "gallery-library"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ImplementationRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 8,
                maxTargetCandidates: 0,
                maxAssemblies: 4096,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 512L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromMinutes(2)),
            cancellationToken);

    private static PlatformHouseRequest PairedPopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("gallery-population"),
            new PlatformTargetDemand.Exact(Target()),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.ReferenceAndImplementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("gallery-population"),
                PlatformSourcePolicyGeneration.Create(
                    "gallery-population"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ImplementationRealization]),
                ]),
            Work(maxBytes: 512L * 1024 * 1024),
            cancellationToken);

    private static PlatformHouseRequest ReferencePopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "gallery-reference-population"),
            new PlatformTargetDemand.Exact(Target()),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "gallery-reference-population"),
                PlatformSourcePolicyGeneration.Create(
                    "gallery-reference-population"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                ]),
            Work(maxBytes: 512L * 1024 * 1024),
            cancellationToken);

    private static PlatformHouseRequest ImplementationPopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "gallery-implementation-population"),
            new PlatformTargetDemand.Exact(Target()),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "gallery-implementation-population"),
                PlatformSourcePolicyGeneration.Create(
                    "gallery-implementation-population"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ImplementationRealization]),
                ]),
            Work(maxBytes: 512L * 1024 * 1024),
            cancellationToken);

    private static PlatformHouseRequest ExactRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        long maxBytes,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("gallery-reference-zero-bytes"),
            new PlatformTargetDemand.Exact(target),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("gallery-reference-exact"),
                PlatformSourcePolicyGeneration.Create("gallery-reference"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                ]),
            Work(maxBytes),
            cancellationToken);

    private static PlatformHouseRequest ImplementationRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformFamilyTarget target,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create("gallery-runtime"),
            new PlatformTargetDemand.Exact(target),
            Origin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("gallery-runtime"),
                PlatformSourcePolicyGeneration.Create("gallery-runtime"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ImplementationRealization]),
                ]),
            Work(maxBytes: 512L * 1024 * 1024),
            cancellationToken);

    private static PlatformHouseRequestOrigin Origin() =>
        new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create("gallery-reference"));

    private static PlatformHouseWorkBudget Work(long maxBytes) =>
        new(
            maxSourceOperations: 2,
            maxTargetCandidates: 1024,
            maxAssemblies: 512,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromMinutes(2));

    private static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(PackagePlatformTestEnvironment.Version));

    private static AssemblyReferenceIdentity ReadIdentity(
        string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }
}
