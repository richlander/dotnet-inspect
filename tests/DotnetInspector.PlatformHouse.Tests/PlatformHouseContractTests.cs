using System.Collections.Immutable;
using DotnetInspector.Platforms;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Tests;

public class PlatformHouseContractTests
{
    [Fact]
    public void TargetDemands_RetainExactCurrencyOrSelectionIdentity()
    {
        PlatformFamilyTarget target = Target();
        var exact = new PlatformTargetDemand.Exact(target);
        PlatformSourceCapabilityIdentity discovery =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformVersionRequirementIdentity requirement =
            PlatformVersionRequirementIdentity.Create("net11-stable");
        var selecting = new PlatformTargetDemand.Selecting(
            PlatformFamily.DotNetRuntime,
            Framework(),
            new PlatformVersionSelectionDemand.Requirement(requirement),
            [discovery],
            new PlatformTargetDiscoveryBudget(4, 8));

        Assert.Same(target, exact.Target);
        Assert.Same(
            requirement,
            Assert.IsType<PlatformVersionSelectionDemand.Requirement>(
                selecting.Version).Identity);
        Assert.Same(discovery, Assert.Single(selecting.DiscoveryCapabilities));
    }

    [Fact]
    public void FamilyDefaultPolicy_RetainsDesktopAndBrowserStages()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetSelectionPolicyIdentity identity =
            PlatformTargetSelectionPolicyIdentity.Create(
                "versionless-runtime-default");
        PlatformTargetSelectionPolicyGeneration generation =
            PlatformTargetSelectionPolicyGeneration.Create("generation-1");
        PlatformTargetDiscoveryStage preferred = new(
            new PlatformTargetDiscoveryScope.AllFrameworks(),
            [installed]);
        PlatformTargetDiscoveryStage fallback = new(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [package]);
        var policy = new PlatformVersionlessRuntimeTargetPolicy(
            identity,
            generation,
            PlatformVersion.Parse("10.0.1"),
            preferred,
            fallback);
        var demand = new PlatformTargetDemand.FamilyDefault(
            PlatformFamily.DotNetRuntime,
            policy,
            new PlatformTargetDiscoveryBudget(32, 64));
        var browserPolicy = new PlatformVersionlessRuntimeTargetPolicy(
            identity,
            generation,
            PlatformVersion.Parse("10.0.1"),
            preferred: null,
            fallback);
        var browserDemand = new PlatformTargetDemand.FamilyDefault(
            PlatformFamily.DotNetRuntime,
            browserPolicy,
            new PlatformTargetDiscoveryBudget(32, 64));

        Assert.Same(identity, demand.Policy.Identity);
        Assert.Same(generation, demand.Policy.Generation);
        Assert.Equal(
            PlatformVersion.Parse("10.0.1"),
            demand.Policy.MinimumPreferredVersion);
        Assert.Same(preferred, demand.Policy.Preferred);
        Assert.Same(fallback, demand.Policy.Fallback);
        Assert.Equal([installed, package], demand.Policy.DiscoveryCapabilities);
        Assert.Null(browserPolicy.Preferred);
        Assert.Same(
            package,
            Assert.Single(browserPolicy.DiscoveryCapabilities));
        Assert.IsType<PlatformHouseRequestValidation.Accepted>(
            PlatformHouseRequestValidation.Validate(
                Request(browserDemand, TargetDiscoveryPlan(package))));
        Assert.Equal(32, demand.Work.MaxCandidates);
        Assert.Equal(64, demand.Work.MaxComparisons);
    }

    [Fact]
    public void FamilyDefaultPolicy_RejectsInvalidStageMembership()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformTargetDiscoveryStage preferred = new(
            new PlatformTargetDiscoveryScope.AllFrameworks(),
            [installed]);
        PlatformTargetDiscoveryStage duplicateFallback = new(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [installed]);

        Assert.Throws<ArgumentException>(
            () => new PlatformTargetDiscoveryStage(
                new PlatformTargetDiscoveryScope.AllFrameworks(),
                [installed, installed]));
        Assert.Throws<ArgumentException>(
            () => new PlatformVersionlessRuntimeTargetPolicy(
                PlatformTargetSelectionPolicyIdentity.Create("default"),
                PlatformTargetSelectionPolicyGeneration.Create("generation"),
                PlatformVersion.Parse("10.0.1"),
                preferred,
                duplicateFallback));
        Assert.Throws<ArgumentException>(
            () => new PlatformVersionlessRuntimeTargetPolicy(
                PlatformTargetSelectionPolicyIdentity.Create("default"),
                PlatformTargetSelectionPolicyGeneration.Create("generation"),
                PlatformVersion.Parse("10.0.1-rc.2"),
                preferred: null,
                new PlatformTargetDiscoveryStage(
                    new PlatformTargetDiscoveryScope.ExactFramework(
                        PlatformTargetFramework.Parse("net10.0")),
                    [PlatformSourceCapabilityIdentity.Create("package")])));
        Assert.Throws<ArgumentException>(
            () => new PlatformVersionlessRuntimeTargetPolicy(
                PlatformTargetSelectionPolicyIdentity.Create("default"),
                PlatformTargetSelectionPolicyGeneration.Create("generation"),
                PlatformVersion.Parse("10.0.1"),
                preferred: null,
                new PlatformTargetDiscoveryStage(
                    new PlatformTargetDiscoveryScope.ExactFramework(
                        Framework()),
                    [PlatformSourceCapabilityIdentity.Create("package")])));
    }

    [Fact]
    public void FamilyDefaultRequest_RequiresExactlyStagedCapabilities()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformSourceCapabilityIdentity extra =
            PlatformSourceCapabilityIdentity.Create("extra-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        PlatformHouseRequest missing = Request(
            demand,
            TargetDiscoveryPlan(installed));
        PlatformHouseRequest complete = Request(
            demand,
            TargetDiscoveryPlan(installed, package));
        PlatformHouseRequest withExtra = Request(
            demand,
            TargetDiscoveryPlan(installed, package, extra));

        var missingRejection = Assert.IsType<
            PlatformHouseRequestValidation.Rejected>(
                PlatformHouseRequestValidation.Validate(missing));
        Assert.Same(
            package,
            Assert.IsType<
                PlatformHouseRejection
                    .TargetDiscoveryCapabilityNotAuthorized>(
                        missingRejection.Rejection).Capability);
        Assert.IsType<PlatformHouseRequestValidation.Accepted>(
            PlatformHouseRequestValidation.Validate(complete));
        var extraRejection = Assert.IsType<
            PlatformHouseRequestValidation.Rejected>(
                PlatformHouseRequestValidation.Validate(withExtra));
        Assert.Same(
            extra,
            Assert.IsType<
                PlatformHouseRejection
                    .TargetDiscoveryCapabilityNotStaged>(
                        extraRejection.Rejection).Capability);
    }

    [Fact]
    public void FamilyDefaultCandidates_CorrespondToTheirTypedStage()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        PlatformHouseRequest request = Request(
            demand,
            TargetDiscoveryPlan(installed, package));
        var installedTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            Framework(),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));
        var packageTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12"));

        var preferred = new PlatformSourceContribution.TargetDiscovery(
            installed,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-generation"),
            [installedTarget]);
        var fallback = new PlatformSourceContribution.TargetDiscovery(
            package,
            request.Snapshot,
            PlatformSourceGeneration.Create("package-generation"),
            [packageTarget]);

        Assert.Same(installedTarget, Assert.Single(preferred.Candidates));
        Assert.Same(packageTarget, Assert.Single(fallback.Candidates));
        Assert.Throws<ArgumentException>(
            () => new PlatformSourceContribution.TargetDiscovery(
                package,
                request.Snapshot,
                PlatformSourceGeneration.Create("wrong-band"),
                [installedTarget]));
    }

    [Fact]
    public void FamilyDefaultSelection_AppliesFloorAndStableFallback()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        PlatformHouseRequest request = Request(
            demand,
            TargetDiscoveryPlan(installed, package));
        var belowFloor = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.0-rc.2"));
        var fallbackPreview = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12-rc.1"));
        var fallbackStable = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12"));
        var preferredDiscovery =
            new PlatformSourceContribution.TargetDiscovery(
                installed,
                request.Snapshot,
                PlatformSourceGeneration.Create("installed-generation"),
                [belowFloor]);
        var fallbackDiscovery =
            new PlatformSourceContribution.TargetDiscovery(
                package,
                request.Snapshot,
                PlatformSourceGeneration.Create("package-generation"),
                [fallbackPreview, fallbackStable]);

        Assert.Throws<ArgumentException>(
            () => new PlatformTargetSettlement.Selected(
                demand,
                belowFloor,
                [preferredDiscovery]));
        Assert.Throws<ArgumentException>(
            () => new PlatformTargetSettlement.Selected(
                demand,
                fallbackPreview,
                [fallbackDiscovery]));
        var selected = new PlatformTargetSettlement.Selected(
            demand,
            fallbackStable,
            [fallbackDiscovery]);

        Assert.Same(fallbackStable, selected.SettledTarget);
        Assert.Same(fallbackDiscovery, Assert.Single(selected.Discoveries));
    }

    [Fact]
    public void FamilyDefaultReceipt_RetainsPolicyAndFiniteWork()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        PlatformHouseRequest request = Request(
            demand,
            TargetDiscoveryPlan(installed, package));
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            Framework(),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            installed,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-generation"),
            [target]);
        var targetSettlement = new PlatformTargetSettlement.Selected(
            demand,
            target,
            [discovery]);
        var sourceSettlement = new PlatformSourceSettlement(
            discovery,
            PlatformSourceSettlementDisposition.Selected);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement,
            [sourceSettlement],
            Consumed(targetCandidates: 4, targetComparisons: 8),
            termination: new PlatformHouseTermination.Unavailable(
                PlatformHouseTerminalEvidenceIdentity.Create("unavailable")));

        Assert.Same(demand.Policy, Assert.IsType<
            PlatformTargetDemand.FamilyDefault>(
                receipt.Request.Target).Policy);
        Assert.Same(target, receipt.TargetSettlement.SettledTarget);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                request.Snapshot,
                targetSettlement,
                [sourceSettlement],
                Consumed(targetCandidates: 5, targetComparisons: 8),
                termination: new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "candidate-budget"))));
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                request.Snapshot,
                targetSettlement,
                [sourceSettlement],
                Consumed(targetCandidates: 4, targetComparisons: 9),
                termination: new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "comparison-budget"))));
    }

    [Fact]
    public void FamilyDefaultReceipt_UsesTypedStagesInsteadOfPlanOrder()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        var plan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("reversed-plan"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Aggregation,
                    [package, installed]),
            ]);
        PlatformHouseRequest request = Request(demand, plan);
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            Framework(),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            installed,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-generation"),
            [target]);
        var selected = new PlatformSourceSettlement(
            discovery,
            PlatformSourceSettlementDisposition.Selected);

        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Selected(
                demand,
                target,
                [discovery]),
            [selected],
            Consumed(),
            termination: new PlatformHouseTermination.Unavailable(
                PlatformHouseTerminalEvidenceIdentity.Create("unavailable")));

        Assert.Same(target, receipt.TargetSettlement.SettledTarget);
        Assert.Same(selected, Assert.Single(receipt.SourceSettlements));
    }

    [Fact]
    public void FamilyDefaultFallback_RequiresTypedPreferredAbsence()
    {
        PlatformSourceCapabilityIdentity installed =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformSourceCapabilityIdentity package =
            PlatformSourceCapabilityIdentity.Create("package-targets");
        PlatformTargetDemand.FamilyDefault demand =
            FamilyDefaultDemand(installed, package);
        PlatformHouseRequest request = Request(
            demand,
            TargetDiscoveryPlan(package, installed));
        var belowFloor = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.0"));
        var eligibleInstalled = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            Framework(),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));
        var fallbackTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12"));
        var preferredAbsence =
            new PlatformSourceContribution.TargetDiscovery(
                installed,
                request.Snapshot,
                PlatformSourceGeneration.Create("installed-generation"),
                [belowFloor]);
        var preferredMatch =
            new PlatformSourceContribution.TargetDiscovery(
                installed,
                request.Snapshot,
                PlatformSourceGeneration.Create("installed-match"),
                [eligibleInstalled]);
        var fallback = new PlatformSourceContribution.TargetDiscovery(
            package,
            request.Snapshot,
            PlatformSourceGeneration.Create("package-generation"),
            [fallbackTarget]);
        var selectedTarget = new PlatformTargetSettlement.Selected(
            demand,
            fallbackTarget,
            [fallback]);
        var selectedFallback = new PlatformSourceSettlement(
            fallback,
            PlatformSourceSettlementDisposition.Selected);

        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            selectedTarget,
            [
                new PlatformSourceSettlement(
                    preferredAbsence,
                    PlatformSourceSettlementDisposition.OutcomeRelevant),
                selectedFallback,
            ],
            Consumed(),
            termination: new PlatformHouseTermination.Unavailable(
                PlatformHouseTerminalEvidenceIdentity.Create("unavailable")));

        Assert.Same(fallbackTarget, receipt.TargetSettlement.SettledTarget);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                request.Snapshot,
                selectedTarget,
                [
                    new PlatformSourceSettlement(
                        preferredMatch,
                        PlatformSourceSettlementDisposition.OutcomeRelevant),
                    selectedFallback,
                ],
                Consumed(),
                termination: new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "preferred-match"))));
    }

    [Fact]
    public void TargetDiscoveryRejection_PreservesDemandMismatchEvidence()
    {
        PlatformSourceCapabilityIdentity invoked =
            PlatformSourceCapabilityIdentity.Create("invoked-source");
        PlatformSourceCapabilityIdentity demanded =
            PlatformSourceCapabilityIdentity.Create("demanded-source");
        var demand = new PlatformTargetDemand.Selecting(
            PlatformFamily.DotNetRuntime,
            Framework(),
            new PlatformVersionSelectionDemand.Requirement(
                PlatformVersionRequirementIdentity.Create("net11-stable")),
            [demanded],
            new PlatformTargetDiscoveryBudget(4, 8));
        PlatformHouseRequest request = Request(
            demand,
            TargetDiscoveryPlan(invoked, demanded));

        var rejection = new PlatformSourceContribution.Rejected(
            PlatformSourceFacet.TargetDiscovery,
            invoked,
            request.Snapshot,
            PlatformSourceGeneration.Create("invoked-generation"),
            exactTarget: null);
        var rejectionSettlement = new PlatformSourceSettlement(
            rejection,
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var rejectedReceipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Unsettled(demand),
            [rejectionSettlement],
            Consumed(),
            termination: new PlatformHouseTermination.Rejected(
                new PlatformHouseRejection.OwnerEvidence(
                    PlatformHouseRejectionKind.InvalidSourcePlan,
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "demand-mismatch"))));

        Assert.Same(
            rejection,
            Assert.Single(rejectedReceipt.SourceSettlements).Contribution);
        Assert.Throws<ArgumentException>(
            () => new PlatformSourceContribution.TargetDiscovery(
                invoked,
                request.Snapshot,
                PlatformSourceGeneration.Create("invalid-success"),
                []));
    }

    [Fact]
    public void SourcePlan_SnapshotsExplicitFacetPolicy()
    {
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformSourceCapabilityIdentity[] capabilities = [first, second];
        var selection = new PlatformSourceSelection(
            PlatformSourceFacet.Reference,
            PlatformSourceSelectionMode.Fallback,
            capabilities);
        PlatformSourceSelection[] selections = [selection];

        var plan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("desktop-reference"),
            PlatformSourcePolicyGeneration.Create("sources-3"),
            selections);
        capabilities[0] =
            PlatformSourceCapabilityIdentity.Create("mutated-capability");
        selections[0] = new PlatformSourceSelection(
            PlatformSourceFacet.Implementation,
            PlatformSourceSelectionMode.Aggregation,
            [second]);

        PlatformSourceSelection retained = Assert.Single(plan.Selections);
        Assert.Equal(PlatformSourceFacet.Reference, retained.Facet);
        Assert.Equal(PlatformSourceSelectionMode.Fallback, retained.Mode);
        Assert.Same(first, retained.Capabilities[0]);
        Assert.True(plan.Authorizes(PlatformSourceFacet.Reference, first));
        Assert.False(
            plan.Authorizes(PlatformSourceFacet.Implementation, first));
    }

    [Fact]
    public void SourcePlan_RejectsDuplicateCapabilityAndFacetIdentities()
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");

        Assert.Throws<ArgumentException>(
            () => new PlatformSourceSelection(
                PlatformSourceFacet.Reference,
                PlatformSourceSelectionMode.Precedence,
                [capability, capability]));

        PlatformSourceSelection first = new(
            PlatformSourceFacet.Reference,
            PlatformSourceSelectionMode.Precedence,
            [capability]);
        PlatformSourceSelection second = new(
            PlatformSourceFacet.Reference,
            PlatformSourceSelectionMode.Aggregation,
            [PlatformSourceCapabilityIdentity.Create("package-backed")]);
        Assert.Throws<ArgumentException>(
            () => new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("duplicate-facet"),
                PlatformSourcePolicyGeneration.Create("generation"),
                [first, second]));
    }

    [Fact]
    public void ClosedOperations_SeparateLiveInputsFromReceiptIdentities()
    {
        PlatformLibraryIdentity library =
            PlatformLibraryIdentityAuthority.Create("catalog")
                .Issue("System.Runtime");
        var realize = new PlatformHouseOperation.Realize(
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.PlatformLibrary(library)),
            PlatformViewDemand.ReferenceAndImplementation);

        var bindingRequest = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);
        var route = new TestRoutePrerequisites();
        var bindingEvidence =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                bindingRequest,
                "binding-1");
        var routeEvidence =
            new PlatformRoutePrerequisitesEvidence<TestRoutePrerequisites>(
                route,
                "route-1");
        var resolveAssembly =
            new PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<TestRoutePrerequisites>(
                    bindingEvidence,
                    routeEvidence,
                    PlatformViewDemand.Reference);

        MetadataTypeDefinitionName typeName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System",
                    ImmutableArray.Create("Object"))).Name;
        var typeRequest = TypeResolutionRequest.FromReference(
            new AssemblyReferenceIdentity(
                "System.Runtime",
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform,
            typeName);
        var reference = new TestReferenceCandidate();
        var resolveType = new PlatformHouseOperation.ResolveTypeDefinition
            .FromReference<TestReferenceCandidate>(
                new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                    typeRequest,
                    "type-request-1"),
                new PlatformTypeResolutionCandidateEvidence<
                    TestReferenceCandidate>(
                    reference,
                    "reference-1"),
                PlatformViewDemand.Implementation);
        var implementation = new TestReferenceCandidate();
        var resolveImplementation =
            new PlatformHouseOperation.ResolveTypeDefinition
                .FromImplementation<TestReferenceCandidate>(
                    new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                        typeRequest,
                        "type-request-2"),
                    new PlatformTypeResolutionCandidateEvidence<
                        TestReferenceCandidate>(
                            implementation,
                            "implementation-1"));

        Assert.Same(
            library,
            Assert.IsType<PlatformLibraryDemand.PlatformLibrary>(
                Assert.IsType<PlatformPopulationDemand.Library>(
                    realize.Population).Value).Identity);
        Assert.Same(route, resolveAssembly.Prerequisites);
        Assert.Same(
            bindingEvidence.Identity,
            Assert.IsType<
                PlatformHouseOperationSnapshot.ResolveAssemblyReference>(
                    resolveAssembly.Snapshot).Request);
        Assert.Same(reference, resolveType.StartingReference);
        Assert.Equal(
            PlatformViewDemand.Reference,
            ((PlatformHouseOperationSnapshot.ResolveTypeDefinition)
                resolveType.Snapshot).StartingView);
        Assert.Same(
            implementation,
            resolveImplementation.StartingImplementation);
        Assert.Equal(
            PlatformViewDemand.Implementation,
            ((PlatformHouseOperationSnapshot.ResolveTypeDefinition)
                resolveImplementation.Snapshot).StartingView);
    }

    [Fact]
    public void
        CompiledXmlContentDemandRequiresOneLibraryReferenceView()
    {
        PlatformLibraryContentDemand demand =
            PlatformLibraryContentDemand
                .CompiledXmlDocumentation;
        AssemblyReferenceIdentity identity =
            new(
                "System.Text.Json",
                new Version(11, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null);

        var operation = new PlatformHouseOperation.Realize(
            new PlatformPopulationDemand.Library(
                new PlatformLibraryDemand.Assembly(identity)),
            PlatformViewDemand.ReferenceAndImplementation,
            demand);

        Assert.Equal(demand, operation.ContentDemand);
        Assert.Equal(
            demand,
            Assert.IsType<
                    PlatformHouseOperationSnapshot.Realize>(
                        operation.Snapshot)
                .ContentDemand);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand
                    .CompletePopulation(),
                PlatformViewDemand.Reference,
                demand));
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(
                        identity)),
                PlatformViewDemand.Implementation,
                demand));
    }

    [Fact]
    public void RequestSnapshot_OmitsCancellationAndLiveOwnerInputs()
    {
        var live = new TestRoutePrerequisites();
        var operation = new PlatformHouseOperation.ResolveAssemblyReference
            .WithPrerequisites<TestRoutePrerequisites>(
                new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.CoreLibrary(),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Platform),
                    "binding-1"),
                new PlatformRoutePrerequisitesEvidence<
                    TestRoutePrerequisites>(
                        live,
                        "route-1"),
                PlatformViewDemand.Reference);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("request-1"),
            new PlatformTargetDemand.Exact(Target()),
            StandaloneOrigin(),
            operation,
            EmptyPlan(),
            Work(),
            new CancellationToken(canceled: true));

        Assert.Same(operation.Snapshot, request.Snapshot.Operation);
        Assert.DoesNotContain(
            typeof(PlatformHouseRequestSnapshot).GetProperties(),
            property => property.PropertyType == typeof(CancellationToken));
        Assert.DoesNotContain(
            request.Snapshot.Operation.GetType().GetProperties(),
            property => property.PropertyType == typeof(TestRoutePrerequisites)
                || property.PropertyType == typeof(AssemblyBindingRequest));
    }

    [Fact]
    public void DelegatedOrigin_RetainsOnlyOrchestrationAssociation()
    {
        PlatformDelegationAssociationIdentity association =
            PlatformDelegationAssociationIdentity.Create(
                "package-decision-to-platform-request");
        var origin = new PlatformHouseRequestOrigin.Delegated(association);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("delegated-request"),
            new PlatformTargetDemand.Exact(Target()),
            origin,
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            EmptyPlan(),
            Work());

        Assert.Same(origin, request.Snapshot.Origin);
        Assert.Same(
            association,
            Assert.IsType<PlatformHouseRequestOrigin.Delegated>(
                request.Snapshot.Origin).Association);
        Assert.DoesNotContain(
            typeof(PlatformHouseRequestOrigin.Delegated).GetProperties(),
            property => property.PropertyType.Namespace
                == "DotnetInspector.Packages");
    }

    [Fact]
    public void SelectingDemand_RejectsUnauthorizedDiscoveryCapability()
    {
        PlatformSourceCapabilityIdentity discovery =
            PlatformSourceCapabilityIdentity.Create("installed-targets");
        PlatformTargetDemand.Selecting demand = SelectingDemand(discovery);
        PlatformHouseRequest request = Request(demand, EmptyPlan());

        var rejected = Assert.IsType<PlatformHouseRequestValidation.Rejected>(
            PlatformHouseRequestValidation.Validate(request));
        var unauthorized = Assert.IsType<
            PlatformHouseRejection.TargetDiscoveryCapabilityNotAuthorized>(
                rejected.Rejection);

        Assert.Same(discovery, unauthorized.Capability);
    }

    [Fact]
    public void Contributions_RetainExactRequestAndOwnerFacts()
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformTargetDemand.Selecting demand = SelectingDemand(capability);
        PlatformHouseRequest request = Request(
            demand,
            Plan(
                (PlatformSourceFacet.TargetDiscovery, capability),
                (PlatformSourceFacet.Reference, capability)));
        PlatformFamilyTarget target = Target();
        PlatformSourceGeneration generation =
            PlatformSourceGeneration.Create("installed-9");
        PlatformSourceCoordinateIdentity coordinate =
            PlatformSourceCoordinateIdentity.Create("pack-11.0.0");
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            capability,
            request.Snapshot,
            generation,
            [target]);
        var population =
            ((PlatformHouseOperation.Realize)request.Operation).Population;
        var realization = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Reference,
            capability,
            request.Snapshot,
            generation,
            target,
            coordinate,
            population,
            PlatformSourceContributionCompleteness.Authoritative);
        var failed = new PlatformSourceContribution.Failed(
            PlatformSourceFacet.Reference,
            capability,
            request.Snapshot,
            generation,
            target);

        Assert.Same(request.Snapshot, discovery.Request);
        Assert.Same(generation, realization.Generation);
        Assert.Same(coordinate, realization.Coordinate);
        Assert.Equal(PlatformViewDemand.Reference, realization.View);
        Assert.Same(population, realization.Population);
        Assert.Same(target, failed.ExactTarget);
        Assert.Throws<ArgumentNullException>(
            () => new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create("installed-9"),
                exactTarget: null,
                PlatformSourceUnavailabilityKind.Unavailable));
    }

    [Fact]
    public void SelectedTarget_IsBoundToRetainedDiscoveryContribution()
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformTargetDemand.Selecting demand = SelectingDemand(capability);
        PlatformHouseRequest request = Request(
            demand,
            Plan((PlatformSourceFacet.TargetDiscovery, capability)));
        PlatformFamilyTarget offered = Target();
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            [offered]);

        var selected = new PlatformTargetSettlement.Selected(
            demand,
            offered,
            [discovery]);

        Assert.Same(discovery, Assert.Single(selected.Discoveries));
        Assert.Throws<ArgumentException>(
            () => new PlatformTargetSettlement.Selected(
                demand,
                new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    Framework(),
                    PlatformVersion.Parse("11.0.1")),
                [discovery]));
    }

    [Fact]
    public void Receipt_RejectsUnauthorizedWrongRequestAndOverBudgetEvidence()
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformTargetDemand.Selecting demand = SelectingDemand(capability);
        PlatformHouseRequest unauthorized = Request(demand, EmptyPlan());
        PlatformFamilyTarget target = Target();
        var unauthorizedDiscovery = new PlatformSourceContribution.TargetDiscovery(
            capability,
            unauthorized.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            [target]);
        var unauthorizedSettlement = new PlatformTargetSettlement.Selected(
            demand,
            target,
            [unauthorizedDiscovery]);

        Assert.Throws<ArgumentException>(
            () => TerminalReceipt(
                unauthorized,
                unauthorizedSettlement,
                new PlatformSourceSettlement(
                    unauthorizedDiscovery,
                    PlatformSourceSettlementDisposition.Selected)));

        PlatformHouseRequest authorized = Request(
            demand,
            Plan((PlatformSourceFacet.TargetDiscovery, capability)));
        var authorizedDiscovery = new PlatformSourceContribution.TargetDiscovery(
            capability,
            authorized.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            [target]);
        var authorizedSettlement = new PlatformTargetSettlement.Selected(
            demand,
            target,
            [authorizedDiscovery]);
        Assert.Throws<ArgumentException>(
            () => TerminalReceipt(
                authorized,
                authorizedSettlement,
                new PlatformSourceSettlement(
                    unauthorizedDiscovery,
                    PlatformSourceSettlementDisposition.Selected)));
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                authorized.Snapshot,
                authorizedSettlement,
                [
                    new PlatformSourceSettlement(
                        authorizedDiscovery,
                        PlatformSourceSettlementDisposition.Selected),
                ],
                Consumed(sourceOperations: 9),
                termination: new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "unavailable-1"))));
    }

    [Fact]
    public void RealizationCompletion_RequiresDemandedViewsAndCompleteness()
    {
        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference");
        PlatformSourceCapabilityIdentity implementation =
            PlatformSourceCapabilityIdentity.Create("implementation");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        PlatformHouseRequest request = Request(
            demand,
            Plan(
                (PlatformSourceFacet.Reference, reference),
                (PlatformSourceFacet.Implementation, implementation)),
            PlatformViewDemand.ReferenceAndImplementation);
        var population =
            ((PlatformHouseOperation.Realize)request.Operation).Population;
        PlatformSourceSettlement referenceSettlement = Settlement(
            PlatformSourceFacet.Reference,
            reference);
        PlatformSourceSettlement implementationSettlement = Settlement(
            PlatformSourceFacet.Implementation,
            implementation);
        var operationSnapshot =
            (PlatformHouseOperationSnapshot.Realize)request.Snapshot.Operation;

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseCompletion.Realization(
                operationSnapshot,
                [referenceSettlement]));

        var completion = new PlatformHouseCompletion.Realization(
            operationSnapshot,
            [referenceSettlement, implementationSettlement],
            new PlatformViewCorrespondenceEvidence<TestViewCorrespondence>(
                new TestViewCorrespondence(),
                "views-1"));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [referenceSettlement, implementationSettlement],
            Consumed(),
            completion);

        Assert.Same(completion, receipt.Completion);

        PlatformSourceSettlement Settlement(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability) =>
            new(
                new PlatformSourceContribution.Realization(
                    facet,
                    capability,
                    request.Snapshot,
                    PlatformSourceGeneration.Create($"{facet}-1"),
                    target,
                    PlatformSourceCoordinateIdentity.Create($"{facet}-coordinate"),
                    population,
                    PlatformSourceContributionCompleteness.Authoritative),
                PlatformSourceSettlementDisposition.Selected);
    }

    [Fact]
    public void AssemblyNoNameOwnerCompletion_RequiresSettledAbsence()
    {
        var operation = new PlatformHouseOperation.ResolveAssemblyReference
            .WithPrerequisites<TestRoutePrerequisites>(
                new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.CoreLibrary(),
                        AssemblyBindingOrigin.Global(),
                        AssemblyResolutionScope.Platform),
                    "binding-1"),
                new PlatformRoutePrerequisitesEvidence<
                    TestRoutePrerequisites>(
                        new TestRoutePrerequisites(),
                        "route-1"),
                PlatformViewDemand.Reference);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("request-1"),
            new PlatformTargetDemand.Exact(Target()),
            StandaloneOrigin(),
            operation,
            EmptyPlan(),
            Work());
        var metadataOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "no-name-owner");
        var completion = new PlatformHouseCompletion.AssemblyReference(
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                request.Snapshot.Operation,
            PlatformAssemblyReferenceCompletionKind.NoNameOwner,
            metadataOutcome,
            []);

        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)request.Target),
            [],
            Consumed(),
            completion);

        Assert.Equal(
            PlatformAssemblyReferenceCompletionKind.NoNameOwner,
            completion.Kind);
        Assert.Empty(completion.SourceSettlements);
        Assert.Same(completion, receipt.Completion);

        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("first");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("second");
        var aggregatingRequest = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("aggregating-request"),
            new PlatformTargetDemand.Exact(Target()),
            StandaloneOrigin(),
            operation,
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("aggregation"),
                PlatformSourcePolicyGeneration.Create("generation"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Aggregation,
                        [first, second]),
                ]),
            Work());
        var firstAbsent = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.Reference,
                first,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("first"),
                Target(),
                PlatformSourceUnavailabilityKind.Absent),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var secondIncomplete = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second"),
                Target()),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var invalidCompletion = new PlatformHouseCompletion.AssemblyReference(
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                aggregatingRequest.Snapshot.Operation,
            PlatformAssemblyReferenceCompletionKind.NoNameOwner,
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "aggregating-no-name-owner"),
            [firstAbsent, secondIncomplete]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregatingRequest.Snapshot,
                new PlatformTargetSettlement.Exact(
                    (PlatformTargetDemand.Exact)
                        aggregatingRequest.Target),
                [firstAbsent, secondIncomplete],
                Consumed(),
                invalidCompletion));

        var selectedIncomplete = new PlatformSourceSettlement(
            secondIncomplete.Contribution,
            PlatformSourceSettlementDisposition.Selected);
        var selectedIncompleteCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.NoNameOwner,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "selected-incomplete-no-name-owner"),
                [firstAbsent, selectedIncomplete]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregatingRequest.Snapshot,
                new PlatformTargetSettlement.Exact(
                    (PlatformTargetDemand.Exact)
                        aggregatingRequest.Target),
                [firstAbsent, selectedIncomplete],
                Consumed(),
                selectedIncompleteCompletion));

        var realized = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                first,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("realized"),
                Target(),
                PlatformSourceCoordinateIdentity.Create("coordinate"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.PlatformLibrary(
                        PlatformLibraryIdentityAuthority.Create("catalog")
                            .Issue("System.Runtime"))),
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);
        var secondAbsent = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second-absent"),
                Target(),
                PlatformSourceUnavailabilityKind.Absent),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var mixedCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.NoNameOwner,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "mixed-no-name-owner"),
                [realized, secondAbsent]);
        var mixedReceipt = new PlatformHouseReceipt(
            aggregatingRequest.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)
                    aggregatingRequest.Target),
            [realized, secondAbsent],
            Consumed(),
            mixedCompletion);

        Assert.Same(mixedCompletion, mixedReceipt.Completion);

        var resolvedOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "mixed-resolved",
                realized.Contribution);
        var resolvedCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.Resolved,
                resolvedOutcome,
                [realized]);
        var resolvedReceipt = new PlatformHouseReceipt(
            aggregatingRequest.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)
                    aggregatingRequest.Target),
            [realized, secondAbsent],
            Consumed(),
            resolvedCompletion);

        Assert.Same(resolvedCompletion, resolvedReceipt.Completion);

        var secondRealized = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second-realized"),
                Target(),
                PlatformSourceCoordinateIdentity.Create(
                    "second-coordinate"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.PlatformLibrary(
                        PlatformLibraryIdentityAuthority.Create("catalog")
                            .Issue("System.Private.CoreLib"))),
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);
        var partialRealized = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("partial-realized"),
                Target(),
                PlatformSourceCoordinateIdentity.Create(
                    "partial-coordinate"),
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformSourceContributionCompleteness.Partial),
            PlatformSourceSettlementDisposition.Selected);
        var partialCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.NoNameOwner,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "partial-no-name-owner"),
                [realized, partialRealized]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregatingRequest.Snapshot,
                new PlatformTargetSettlement.Exact(
                    (PlatformTargetDemand.Exact)
                        aggregatingRequest.Target),
                [realized, partialRealized],
                Consumed(),
                partialCompletion));

        var partialResolvedOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "partial-resolved",
                realized.Contribution);
        var partialResolvedCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.Resolved,
                partialResolvedOutcome,
                [realized, partialRealized]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregatingRequest.Snapshot,
                new PlatformTargetSettlement.Exact(
                    (PlatformTargetDemand.Exact)
                        aggregatingRequest.Target),
                [realized, partialRealized],
                Consumed(),
                partialResolvedCompletion));

        var searchedCompletion =
            new PlatformHouseCompletion.AssemblyReference(
                (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                    aggregatingRequest.Snapshot.Operation,
                PlatformAssemblyReferenceCompletionKind.NoNameOwner,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "searched-no-name-owner"),
                [realized, secondRealized]);

        var searchedReceipt = new PlatformHouseReceipt(
            aggregatingRequest.Snapshot,
            new PlatformTargetSettlement.Exact(
                (PlatformTargetDemand.Exact)
                    aggregatingRequest.Target),
            [realized, secondRealized],
            Consumed(),
            searchedCompletion);

        Assert.Same(searchedCompletion, searchedReceipt.Completion);
    }

    [Fact]
    public void TypeCompletion_BindsMetadataOutcomeAndImplementationSupplier()
    {
        PlatformSourceCapabilityIdentity implementation =
            PlatformSourceCapabilityIdentity.Create("implementation");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        var operation = new PlatformHouseOperation.ResolveTypeDefinition
            .FromReference<TestReferenceCandidate>(
                new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                    TypeRequest(),
                    "type-request"),
                new PlatformTypeResolutionCandidateEvidence<
                    TestReferenceCandidate>(
                    new TestReferenceCandidate(),
                    "starting-reference"),
                PlatformViewDemand.Implementation);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("request"),
            demand,
            StandaloneOrigin(),
            operation,
            Plan((PlatformSourceFacet.Implementation, implementation)),
            Work());
        var population = new PlatformPopulationDemand.Library(
            new PlatformLibraryDemand.PlatformLibrary(
                PlatformLibraryIdentityAuthority.Create("catalog")
                    .Issue("System.Runtime")));
        var source = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Implementation,
                implementation,
                request.Snapshot,
                PlatformSourceGeneration.Create("implementation-1"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "implementation-coordinate"),
                population,
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);
        var referenceOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "reference-outcome");
        var correspondence =
            new PlatformViewCorrespondenceEvidence<TestViewCorrespondence>(
                new TestViewCorrespondence(),
                "views");
        var implementationOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "implementation-outcome",
                source.Contribution,
                correspondence);
        var snapshot =
            (PlatformHouseOperationSnapshot.ResolveTypeDefinition)
                request.Snapshot.Operation;

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseCompletion.TypeDefinition(
                snapshot,
                referenceOutcome,
                referenceOutcome,
                [source],
                correspondence));
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseCompletion.TypeDefinition(
                snapshot,
                referenceOutcome,
                implementationOutcome,
                [],
                correspondence));

        var referenceSupplier = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                implementation,
                request.Snapshot,
                PlatformSourceGeneration.Create("reference-1"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "reference-coordinate"),
                population,
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseCompletion.TypeDefinition(
                snapshot,
                referenceOutcome,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "wrong-supplier",
                    referenceSupplier.Contribution,
                    correspondence),
                [referenceSupplier, source],
                correspondence));

        var completion = new PlatformHouseCompletion.TypeDefinition(
            snapshot,
            referenceOutcome,
            implementationOutcome,
            [source],
            correspondence);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [source],
            Consumed(),
            completion);

        Assert.Same(source, Assert.Single(completion.SelectedContributions));
        PlatformTypeDefinitionValue.ReferenceAndImplementation<
            TestMetadataOutcome,
            TestMetadataOutcome> value =
                completion.BindReferenceAndImplementation(
                    referenceOutcome,
                    implementationOutcome).Value;
        Assert.Same(referenceOutcome.Value, value.ReferenceOutcome);
        Assert.Same(
            implementationOutcome.Value,
            value.ImplementationOutcome);
        Assert.Throws<ArgumentException>(
            () => completion.BindReferenceAndImplementation(
                referenceOutcome,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "other-outcome")));
        Assert.Same(completion, receipt.Completion);
    }

    [Fact]
    public void
        DirectImplementationTypeCompletionBindsOneMetadataOutcome()
    {
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        var operation = new PlatformHouseOperation.ResolveTypeDefinition
            .FromImplementation<TestReferenceCandidate>(
                new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                    TypeRequest(),
                    "implementation-type-request"),
                new PlatformTypeResolutionCandidateEvidence<
                    TestReferenceCandidate>(
                        new TestReferenceCandidate(),
                        "starting-implementation"));
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "implementation-type-resolution"),
            demand,
            StandaloneOrigin(),
            operation,
            Plan(),
            Work());
        var metadataOutcome =
            new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                new TestMetadataOutcome(),
                "implementation-outcome");
        var completion = new PlatformHouseCompletion.TypeDefinition(
            (PlatformHouseOperationSnapshot.ResolveTypeDefinition)
                request.Snapshot.Operation,
            metadataOutcome,
            implementationOutcome: null,
            selectedContributions: [],
            correspondence: null);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [],
            Consumed(),
            completion);

        Assert.Equal(
            PlatformTypeDefinitionCompletionKind.Implementation,
            completion.Kind);
        Assert.Null(completion.ReferenceOutcome);
        Assert.Same(
            metadataOutcome.Identity,
            completion.ImplementationOutcome);
        Assert.Empty(completion.SelectedContributions);
        Assert.Null(completion.Correspondence);
        PlatformTypeDefinitionValue.Implementation<TestMetadataOutcome>
            value = completion.BindImplementation(metadataOutcome).Value;
        Assert.Same(metadataOutcome.Value, value.Outcome);
        Assert.Same(completion, receipt.Completion);
    }

    [Fact]
    public void CompletedReceipt_EnforcesFallbackAndAggregationPolicy()
    {
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("first");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("second");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        var fallbackPlan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("fallback"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Fallback,
                    [first, second]),
            ]);
        PlatformHouseRequest fallbackRequest = Request(demand, fallbackPlan);
        var earlierUnavailable = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.Reference,
                first,
                fallbackRequest.Snapshot,
                PlatformSourceGeneration.Create("first-1"),
                target,
                PlatformSourceUnavailabilityKind.Unavailable),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        PlatformSourceSettlement laterSelected = RealizationSettlement(
            fallbackRequest,
            second,
            target,
            "second");
        var fallbackCompletion = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)
                fallbackRequest.Snapshot.Operation,
            [laterSelected]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                fallbackRequest.Snapshot,
                new PlatformTargetSettlement.Exact(demand),
                [laterSelected],
                Consumed(),
                fallbackCompletion));
        _ = new PlatformHouseReceipt(
            fallbackRequest.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [earlierUnavailable, laterSelected],
            Consumed(),
            fallbackCompletion);
        var earlierFailed = new PlatformSourceSettlement(
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.Reference,
                first,
                fallbackRequest.Snapshot,
                PlatformSourceGeneration.Create("first-2"),
                target),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        _ = new PlatformHouseReceipt(
            fallbackRequest.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [earlierFailed, laterSelected],
            Consumed(),
            fallbackCompletion);

        var precedencePlan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("precedence"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [first, second]),
            ]);
        PlatformHouseRequest precedenceRequest = Request(
            demand,
            precedencePlan);
        var precedenceFailed = new PlatformSourceSettlement(
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.Reference,
                first,
                precedenceRequest.Snapshot,
                PlatformSourceGeneration.Create("first-1"),
                target),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        PlatformSourceSettlement precedenceSelected =
            RealizationSettlement(
                precedenceRequest,
                second,
                target,
                "precedence-second");
        var precedenceCompletion = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)
                precedenceRequest.Snapshot.Operation,
            [precedenceSelected]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                precedenceRequest.Snapshot,
                new PlatformTargetSettlement.Exact(demand),
                [precedenceFailed, precedenceSelected],
                Consumed(),
                precedenceCompletion));

        var aggregationPlan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("aggregation"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Aggregation,
                    [first, second]),
            ]);
        PlatformHouseRequest aggregationRequest = Request(
            demand,
            aggregationPlan);
        PlatformSourceSettlement aggregationSelected =
            RealizationSettlement(
                aggregationRequest,
                first,
                target,
                "aggregation-first");
        var aggregationIncomplete = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.Reference,
                second,
                aggregationRequest.Snapshot,
                PlatformSourceGeneration.Create("second-1"),
                target),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var aggregationCompletion = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)
                aggregationRequest.Snapshot.Operation,
            [aggregationSelected]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregationRequest.Snapshot,
                new PlatformTargetSettlement.Exact(demand),
                [aggregationSelected, aggregationIncomplete],
                Consumed(),
                aggregationCompletion));
    }

    [Fact]
    public void CompletedReceipt_EnforcesDiscoveryPolicy()
    {
        PlatformSourceCapabilityIdentity firstDiscovery =
            PlatformSourceCapabilityIdentity.Create("first-discovery");
        PlatformSourceCapabilityIdentity secondDiscovery =
            PlatformSourceCapabilityIdentity.Create("second-discovery");
        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference");
        var selecting = new PlatformTargetDemand.Selecting(
            PlatformFamily.DotNetRuntime,
            Framework(),
            new PlatformVersionSelectionDemand.Requirement(
                PlatformVersionRequirementIdentity.Create("net11-stable")),
            [firstDiscovery, secondDiscovery],
            new PlatformTargetDiscoveryBudget(4, 8));
        var discoveryPlan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("discovery-plan"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Aggregation,
                    [firstDiscovery, secondDiscovery]),
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [reference]),
            ]);
        PlatformHouseRequest discoveryRequest = Request(
            selecting,
            discoveryPlan);
        PlatformFamilyTarget target = Target();
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            firstDiscovery,
            discoveryRequest.Snapshot,
            PlatformSourceGeneration.Create("discovery-1"),
            [target]);
        var selectedDiscovery = new PlatformSourceSettlement(
            discovery,
            PlatformSourceSettlementDisposition.Selected);
        var incompleteDiscovery = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.TargetDiscovery,
                secondDiscovery,
                discoveryRequest.Snapshot,
                PlatformSourceGeneration.Create("discovery-2"),
                exactTarget: null),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        PlatformSourceSettlement selectedReference = RealizationSettlement(
            discoveryRequest,
            reference,
            target,
            "reference");
        var realization = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)
                discoveryRequest.Snapshot.Operation,
            [selectedReference]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                discoveryRequest.Snapshot,
                new PlatformTargetSettlement.Selected(
                    selecting,
                    target,
                    [discovery]),
                [
                    selectedDiscovery,
                    incompleteDiscovery,
                    selectedReference,
                ],
                Consumed(),
                realization));

        var fallbackDiscoveryPlan = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("fallback-discovery-plan"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Fallback,
                    [firstDiscovery, secondDiscovery]),
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [reference]),
            ]);
        PlatformHouseRequest fallbackDiscoveryRequest = Request(
            selecting,
            fallbackDiscoveryPlan);
        var emptyDiscovery = new PlatformSourceContribution.TargetDiscovery(
            firstDiscovery,
            fallbackDiscoveryRequest.Snapshot,
            PlatformSourceGeneration.Create("empty-discovery"),
            []);
        var offeredDiscovery = new PlatformSourceContribution.TargetDiscovery(
            secondDiscovery,
            fallbackDiscoveryRequest.Snapshot,
            PlatformSourceGeneration.Create("offered-discovery"),
            [target]);
        var retainedEmptyDiscovery = new PlatformSourceSettlement(
            emptyDiscovery,
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var retainedOfferedDiscovery = new PlatformSourceSettlement(
            offeredDiscovery,
            PlatformSourceSettlementDisposition.Selected);
        PlatformSourceSettlement fallbackReference =
            RealizationSettlement(
                fallbackDiscoveryRequest,
                reference,
                target,
                "fallback-reference");
        var fallbackRealization = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)
                fallbackDiscoveryRequest.Snapshot.Operation,
            [fallbackReference]);
        var fallbackDiscoveryReceipt = new PlatformHouseReceipt(
            fallbackDiscoveryRequest.Snapshot,
            new PlatformTargetSettlement.Selected(
                selecting,
                target,
                [offeredDiscovery]),
            [
                retainedEmptyDiscovery,
                retainedOfferedDiscovery,
                fallbackReference,
            ],
            Consumed(),
            fallbackRealization);

        Assert.Same(fallbackRealization, fallbackDiscoveryReceipt.Completion);
    }

    [Fact]
    public void IncompleteReceipt_RetainsActualOverBudgetWork()
    {
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        PlatformHouseRequest request = Request(demand, EmptyPlan());
        var incomplete = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(
                "deadline-exhausted"));

        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [],
            Consumed(elapsed: TimeSpan.FromSeconds(31)),
            termination: incomplete);

        Assert.Equal(TimeSpan.FromSeconds(31), receipt.ConsumedWork.Elapsed);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                request.Snapshot,
                new PlatformTargetSettlement.Exact(demand),
                [],
                Consumed(elapsed: TimeSpan.FromSeconds(31)),
                termination: new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "over-budget-unavailable"))));
    }

    [Fact]
    public void OutcomeKinds_BindToExactReceiptEvidence()
    {
        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        PlatformHouseRequest request = Request(
            demand,
            Plan((PlatformSourceFacet.Reference, reference)));
        var population =
            ((PlatformHouseOperation.Realize)request.Operation).Population;
        var source = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                reference,
                request.Snapshot,
                PlatformSourceGeneration.Create("reference-1"),
                target,
                PlatformSourceCoordinateIdentity.Create("reference-coordinate"),
                population,
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);
        var completion = new PlatformHouseCompletion.Realization(
            (PlatformHouseOperationSnapshot.Realize)request.Snapshot.Operation,
            [source]);
        var targetSettlement = new PlatformTargetSettlement.Exact(demand);
        var completedReceipt = new PlatformHouseReceipt(
            request.Snapshot,
            targetSettlement,
            [source],
            Consumed(),
            completion);
        var unavailableEvidence = new PlatformHouseTermination.Unavailable(
            PlatformHouseTerminalEvidenceIdentity.Create("unavailable"));
        var ambiguousEvidence = new PlatformHouseTermination.Ambiguous(
            [
                PlatformHouseCandidateIdentity.Create("candidate-1"),
                PlatformHouseCandidateIdentity.Create("candidate-2"),
            ]);
        var rejectedEvidence = new PlatformHouseTermination.Rejected(
            new PlatformHouseRejection.OwnerEvidence(
                PlatformHouseRejectionKind.InvalidOwnerResult,
                PlatformHouseTerminalEvidenceIdentity.Create("rejected")));
        var incompleteEvidence = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create("incomplete"));
        PlatformHouseReceipt unavailableReceipt = TerminalReceipt(
            unavailableEvidence);
        PlatformHouseReceipt ambiguousReceipt = TerminalReceipt(
            ambiguousEvidence);
        PlatformHouseReceipt rejectedReceipt = TerminalReceipt(
            rejectedEvidence);
        PlatformHouseReceipt incompleteReceipt = TerminalReceipt(
            incompleteEvidence);

        var completed = new PlatformHouseOutcome<string>.Completed(
            completion.Bind("value"),
            completedReceipt);
        var unavailable = new PlatformHouseOutcome<string>.Unavailable(
            unavailableEvidence,
            unavailableReceipt);
        var ambiguous = new PlatformHouseOutcome<string>.Ambiguous(
            ambiguousEvidence,
            ambiguousReceipt);
        var rejected = new PlatformHouseOutcome<string>.Rejected(
            rejectedEvidence,
            rejectedReceipt);
        var incomplete = new PlatformHouseOutcome<string>.Incomplete(
            incompleteEvidence,
            incompleteReceipt);

        Assert.Equal("value", completed.Value);
        Assert.Same(unavailableEvidence, unavailable.Evidence);
        Assert.Equal(2, ambiguous.Evidence.Candidates.Count);
        Assert.Same(rejectedEvidence, rejected.Evidence);
        Assert.Same(incompleteEvidence, incomplete.Evidence);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseOutcome<string>.Unavailable(
                new PlatformHouseTermination.Unavailable(
                    PlatformHouseTerminalEvidenceIdentity.Create("other")),
                unavailableReceipt));

        PlatformHouseReceipt TerminalReceipt(
            PlatformHouseTermination termination) =>
            new(
                request.Snapshot,
                targetSettlement,
                [],
                Consumed(),
                termination: termination);
    }

    [Fact]
    public void ReceiptEvidence_RejectsDuplicateCandidatesAndContributions()
    {
        PlatformHouseCandidateIdentity candidate =
            PlatformHouseCandidateIdentity.Create("candidate");
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseTermination.Ambiguous(
                [candidate, candidate]));

        PlatformSourceCapabilityIdentity reference =
            PlatformSourceCapabilityIdentity.Create("reference");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        PlatformHouseRequest request = Request(
            demand,
            Plan((PlatformSourceFacet.Reference, reference)));
        var contribution = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Reference,
            reference,
            request.Snapshot,
            PlatformSourceGeneration.Create("reference-1"),
            target,
            PlatformSourceCoordinateIdentity.Create("reference-coordinate"),
            ((PlatformHouseOperation.Realize)request.Operation).Population,
            PlatformSourceContributionCompleteness.Authoritative);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                request.Snapshot,
                new PlatformTargetSettlement.Exact(demand),
                [
                    new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition.Selected),
                    new PlatformSourceSettlement(
                        contribution,
                        PlatformSourceSettlementDisposition.Shadowed),
                ],
                Consumed(),
                termination: new PlatformHouseTermination.Incomplete(
                    PlatformHouseTerminalEvidenceIdentity.Create(
                        "incomplete"))));
    }

    [Fact]
    public void OpaqueIdentities_UseOwnerIssuedTokenIdentity()
    {
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("installed");

        Assert.NotSame(first, second);
        Assert.NotEqual(first, second);
        Assert.Equal(first.Name, second.Name);
    }

    static PlatformTargetDemand.Selecting SelectingDemand(
        PlatformSourceCapabilityIdentity capability) =>
        new(
            PlatformFamily.DotNetRuntime,
            Framework(),
            new PlatformVersionSelectionDemand.Requirement(
                PlatformVersionRequirementIdentity.Create("net11-stable")),
            [capability],
            new PlatformTargetDiscoveryBudget(4, 8));

    static PlatformTargetDemand.FamilyDefault FamilyDefaultDemand(
        PlatformSourceCapabilityIdentity installed,
        PlatformSourceCapabilityIdentity package)
    {
        var preferred = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.AllFrameworks(),
            [installed]);
        var fallback = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                PlatformTargetFramework.Parse("net10.0")),
            [package]);
        var policy = new PlatformVersionlessRuntimeTargetPolicy(
            PlatformTargetSelectionPolicyIdentity.Create(
                "versionless-runtime-default"),
            PlatformTargetSelectionPolicyGeneration.Create("generation-1"),
            PlatformVersion.Parse("10.0.1"),
            preferred,
            fallback);
        return new PlatformTargetDemand.FamilyDefault(
            PlatformFamily.DotNetRuntime,
            policy,
            new PlatformTargetDiscoveryBudget(4, 8));
    }

    static PlatformHouseRequest Request(
        PlatformTargetDemand demand,
        PlatformSourcePlan sources,
        PlatformViewDemand view = PlatformViewDemand.Reference) =>
        new(
            PlatformHouseRequestIdentity.Create("request"),
            demand,
            StandaloneOrigin(),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                view),
            sources,
            Work());

    static PlatformHouseRequestOrigin StandaloneOrigin() =>
        new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create("standalone"));

    static PlatformSourcePlan EmptyPlan() =>
        new(
            PlatformSourcePlanIdentity.Create("none"),
            PlatformSourcePolicyGeneration.Create("generation"),
            []);

    static PlatformSourcePlan Plan(
        params (PlatformSourceFacet Facet,
            PlatformSourceCapabilityIdentity Capability)[] values) =>
        new(
            PlatformSourcePlanIdentity.Create("plan"),
            PlatformSourcePolicyGeneration.Create("generation"),
            values.Select(
                value => new PlatformSourceSelection(
                    value.Facet,
                    PlatformSourceSelectionMode.Precedence,
                    [value.Capability])));

    static PlatformSourcePlan TargetDiscoveryPlan(
        params PlatformSourceCapabilityIdentity[] capabilities) =>
        new(
            PlatformSourcePlanIdentity.Create("target-discovery-plan"),
            PlatformSourcePolicyGeneration.Create("generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Precedence,
                    capabilities),
            ]);

    static PlatformHouseReceipt TerminalReceipt(
        PlatformHouseRequest request,
        PlatformTargetSettlement targetSettlement,
        PlatformSourceSettlement source) =>
        new(
            request.Snapshot,
            targetSettlement,
            [source],
            Consumed(),
            termination: new PlatformHouseTermination.Unavailable(
                PlatformHouseTerminalEvidenceIdentity.Create("unavailable")));

    static PlatformHouseWorkBudget Work() =>
        new(
            maxSourceOperations: 8,
            maxTargetCandidates: 8,
            maxAssemblies: 64,
            maxXmlDocuments: 8,
            maxPortablePdbs: 8,
            maxSourceDocuments: 16,
            maxBytes: 1024 * 1024,
            maxForwardingHops: 16,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations = 0,
        int targetCandidates = 0,
        int targetComparisons = 0,
        TimeSpan? elapsed = null) =>
        new(
            sourceOperations,
            targetCandidates,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons,
            elapsed: elapsed ?? TimeSpan.Zero);

    static TypeResolutionRequest TypeRequest()
    {
        MetadataTypeDefinitionName typeName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System",
                    ImmutableArray.Create("Object"))).Name;
        return TypeResolutionRequest.FromReference(
            new AssemblyReferenceIdentity(
                "System.Runtime",
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform,
            typeName);
    }

    static PlatformSourceSettlement RealizationSettlement(
        PlatformHouseRequest request,
        PlatformSourceCapabilityIdentity capability,
        PlatformFamilyTarget target,
        string name) =>
        new(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create($"{name}-generation"),
                target,
                PlatformSourceCoordinateIdentity.Create($"{name}-coordinate"),
                ((PlatformHouseOperation.Realize)request.Operation).Population,
                PlatformSourceContributionCompleteness.Authoritative),
            PlatformSourceSettlementDisposition.Selected);

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            Framework(),
            PlatformVersion.Parse("11.0.0"));

    static PlatformTargetFramework Framework() =>
        PlatformTargetFramework.Parse("net11.0");

    sealed class TestRoutePrerequisites;
    sealed class TestReferenceCandidate;
    sealed class TestViewCorrespondence;
    sealed class TestMetadataOutcome;
}
