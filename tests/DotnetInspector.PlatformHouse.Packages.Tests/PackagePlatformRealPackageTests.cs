using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformRealPackageTests
{
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

        ValueTask close = root.DisposeAsync();
        Assert.True(close.IsCompletedSuccessfully);
        await close;
        byte[] bytes =
            await PackagePlatformTestData.ReadAllAsync(json);
        Assert.True(bytes.Length > 0);
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'Z', bytes[1]);
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
}
