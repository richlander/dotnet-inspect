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
                new PlatformReferenceCandidateEvidence<TestReferenceCandidate>(
                    reference,
                    "reference-1"),
                PlatformViewDemand.Implementation);

        var documentation = new PlatformHouseOperation
            .ResolveDocumentationEvidence
            .CompiledXmlAndSourceDerived<
                TestDocumentationSubject,
                TestReferenceEvidence,
                TestViewCorrespondence>(
                    new PlatformDocumentationSubjectEvidence<
                        TestDocumentationSubject>(
                            new TestDocumentationSubject(),
                            "member-1"),
                    new PlatformReferenceEvidence<TestReferenceEvidence>(
                        new TestReferenceEvidence(),
                        "reference-def-1"),
                    new PlatformViewCorrespondenceEvidence<
                        TestViewCorrespondence>(
                            new TestViewCorrespondence(),
                            "views-1"));

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
            PlatformDocumentationDemand.CompiledXmlAndSourceDerived,
            documentation.Demand);
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
    public void Contributions_RetainOnlyResourceFreeRequestAndEvidence()
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
        PlatformSourceEvidenceIdentity discoveryEvidence =
            PlatformSourceEvidenceIdentity.Create("discovery-1");
        var discovery = new PlatformSourceContribution.TargetDiscovery(
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            [target],
            discoveryEvidence);
        var population =
            ((PlatformHouseOperation.Realize)request.Operation).Population;
        var realization = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Reference,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            target,
            PlatformSourceCoordinateIdentity.Create("pack-11.0.0"),
            PlatformTargetCorrespondenceIdentity.Create("target-1"),
            population,
            PlatformSourceContributionCompleteness.Authoritative,
            PlatformSourceEvidenceIdentity.Create("realization-1"));
        var failed = new PlatformSourceContribution.Failed(
            PlatformSourceFacet.Reference,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create("installed-9"),
            target,
            PlatformSourceEvidenceIdentity.Create("failure-1"));

        Assert.Same(request.Snapshot, discovery.Request);
        Assert.Same(discoveryEvidence, discovery.Evidence);
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
                PlatformSourceUnavailabilityKind.Unavailable,
                PlatformSourceEvidenceIdentity.Create("missing-target")));
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
            [offered],
            PlatformSourceEvidenceIdentity.Create("discovery-1"));

        var selected = new PlatformTargetSettlement.Selected(
            demand,
            offered,
            [discovery],
            PlatformSourceEvidenceIdentity.Create("selection-1"));

        Assert.Same(discovery, Assert.Single(selected.Discoveries));
        Assert.Throws<ArgumentException>(
            () => new PlatformTargetSettlement.Selected(
                demand,
                new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    Framework(),
                    PlatformVersion.Parse("11.0.1")),
                [discovery],
                PlatformSourceEvidenceIdentity.Create("selection-2")));
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
            [target],
            PlatformSourceEvidenceIdentity.Create("discovery-1"));
        var unauthorizedSettlement = new PlatformTargetSettlement.Selected(
            demand,
            target,
            [unauthorizedDiscovery],
            PlatformSourceEvidenceIdentity.Create("selection-1"));

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
            [target],
            PlatformSourceEvidenceIdentity.Create("discovery-2"));
        var authorizedSettlement = new PlatformTargetSettlement.Selected(
            demand,
            target,
            [authorizedDiscovery],
            PlatformSourceEvidenceIdentity.Create("selection-2"));
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
                    PlatformTargetCorrespondenceIdentity.Create($"{facet}-target"),
                    population,
                    PlatformSourceContributionCompleteness.Authoritative,
                    PlatformSourceEvidenceIdentity.Create($"{facet}-evidence")),
                PlatformSourceSettlementDisposition.Selected);
    }

    [Fact]
    public void DocumentationCompletion_RequiresEveryRequestedChannel()
    {
        var operation = new PlatformHouseOperation
            .ResolveDocumentationEvidence
            .CompiledXmlAndSourceDerived<
                TestDocumentationSubject,
                TestReferenceEvidence,
                TestViewCorrespondence>(
                    new PlatformDocumentationSubjectEvidence<
                        TestDocumentationSubject>(
                            new TestDocumentationSubject(),
                            "member-1"),
                    new PlatformReferenceEvidence<TestReferenceEvidence>(
                        new TestReferenceEvidence(),
                        "reference-1"),
                    new PlatformViewCorrespondenceEvidence<
                        TestViewCorrespondence>(
                            new TestViewCorrespondence(),
                            "views-1"));
        var snapshot =
            (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                operation.Snapshot;
        PlatformSourceCapabilityIdentity xmlCapability =
            PlatformSourceCapabilityIdentity.Create("xml");
        PlatformSourceCapabilityIdentity sourceCapability =
            PlatformSourceCapabilityIdentity.Create("source");
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("documentation-request"),
            demand,
            StandaloneOrigin(),
            operation,
            Plan(
                (PlatformSourceFacet.CompiledXml, xmlCapability),
                (PlatformSourceFacet.SourceDerivedDocumentation,
                    sourceCapability)),
            Work());
        var xmlSettlement = new PlatformSourceSettlement(
            new PlatformSourceContribution.Documentation(
                PlatformSourceFacet.CompiledXml,
                xmlCapability,
                request.Snapshot,
                PlatformSourceGeneration.Create("xml-generation"),
                target,
                PlatformSourceEvidenceIdentity.Create("xml-contribution")),
            PlatformSourceSettlementDisposition.Selected);
        var sourceSettlement = new PlatformSourceSettlement(
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.SourceDerivedDocumentation,
                sourceCapability,
                request.Snapshot,
                PlatformSourceGeneration.Create("source-generation"),
                target,
                PlatformSourceEvidenceIdentity.Create("source-contribution")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var xml = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Available,
            [xmlSettlement],
            PlatformSourceEvidenceIdentity.Create("xml-1"));
        var source = new PlatformDocumentationAttempt(
            PlatformSourceFacet.SourceDerivedDocumentation,
            PlatformDocumentationAttemptKind.Failed,
            [sourceSettlement],
            PlatformSourceEvidenceIdentity.Create("source-1"));

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseCompletion.Documentation(
                snapshot,
                [xml]));
        var completion = new PlatformHouseCompletion.Documentation(
            snapshot,
            [xml, source]);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [xmlSettlement, sourceSettlement],
            Consumed(),
            completion);

        Assert.Equal(2, completion.Attempts.Count);
        Assert.Same(completion, receipt.Completion);

        var xmlOnlyRequest = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("xml-only-request"),
            demand,
            StandaloneOrigin(),
            operation,
            Plan((PlatformSourceFacet.CompiledXml, xmlCapability)),
            Work());
        var xmlOnlySettlement = new PlatformSourceSettlement(
            new PlatformSourceContribution.Documentation(
                PlatformSourceFacet.CompiledXml,
                xmlCapability,
                xmlOnlyRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-only-generation"),
                target,
                PlatformSourceEvidenceIdentity.Create(
                    "xml-only-contribution")),
            PlatformSourceSettlementDisposition.Selected);
        var xmlOnlyCompletion = new PlatformHouseCompletion.Documentation(
            (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                xmlOnlyRequest.Snapshot.Operation,
            [
                new PlatformDocumentationAttempt(
                    PlatformSourceFacet.CompiledXml,
                    PlatformDocumentationAttemptKind.Available,
                    [xmlOnlySettlement],
                    PlatformSourceEvidenceIdentity.Create(
                        "xml-only-attempt")),
                new PlatformDocumentationAttempt(
                    PlatformSourceFacet.SourceDerivedDocumentation,
                    PlatformDocumentationAttemptKind.Unavailable,
                    [],
                    PlatformSourceEvidenceIdentity.Create(
                        "source-not-authorized")),
            ]);
        var xmlOnlyReceipt = new PlatformHouseReceipt(
            xmlOnlyRequest.Snapshot,
            new PlatformTargetSettlement.Exact(demand),
            [xmlOnlySettlement],
            Consumed(),
            xmlOnlyCompletion);

        Assert.Same(xmlOnlyCompletion, xmlOnlyReceipt.Completion);
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
                PlatformSourceUnavailabilityKind.Absent,
                PlatformSourceEvidenceIdentity.Create("first-absent")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var secondIncomplete = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second"),
                Target(),
                PlatformSourceEvidenceIdentity.Create("second-incomplete")),
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
                PlatformTargetCorrespondenceIdentity.Create(
                    "target-correspondence"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.PlatformLibrary(
                        PlatformLibraryIdentityAuthority.Create("catalog")
                            .Issue("System.Runtime"))),
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create("realized-evidence")),
            PlatformSourceSettlementDisposition.Selected);
        var secondAbsent = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second-absent"),
                Target(),
                PlatformSourceUnavailabilityKind.Absent,
                PlatformSourceEvidenceIdentity.Create("second-absent")),
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

        var secondRealized = new PlatformSourceSettlement(
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                second,
                aggregatingRequest.Snapshot,
                PlatformSourceGeneration.Create("second-realized"),
                Target(),
                PlatformSourceCoordinateIdentity.Create(
                    "second-coordinate"),
                PlatformTargetCorrespondenceIdentity.Create(
                    "second-target-correspondence"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.PlatformLibrary(
                        PlatformLibraryIdentityAuthority.Create("catalog")
                            .Issue("System.Private.CoreLib"))),
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create(
                    "second-realized-evidence")),
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
                PlatformTargetCorrespondenceIdentity.Create(
                    "partial-target-correspondence"),
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformSourceContributionCompleteness.Partial,
                PlatformSourceEvidenceIdentity.Create(
                    "partial-realized-evidence")),
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
                new PlatformReferenceCandidateEvidence<TestReferenceCandidate>(
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
                PlatformTargetCorrespondenceIdentity.Create(
                    "implementation-target"),
                population,
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create(
                    "implementation-evidence")),
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
                PlatformTargetCorrespondenceIdentity.Create(
                    "reference-target"),
                population,
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create(
                    "reference-evidence")),
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
            TestMetadataOutcome> value = completion.BindImplementation(
                referenceOutcome,
                implementationOutcome).Value;
        Assert.Same(referenceOutcome.Value, value.ReferenceOutcome);
        Assert.Same(
            implementationOutcome.Value,
            value.ImplementationOutcome);
        Assert.Throws<ArgumentException>(
            () => completion.BindImplementation(
                referenceOutcome,
                new PlatformMetadataOutcomeEvidence<TestMetadataOutcome>(
                    new TestMetadataOutcome(),
                    "other-outcome")));
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
                PlatformSourceUnavailabilityKind.Unavailable,
                PlatformSourceEvidenceIdentity.Create("first-unavailable")),
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
                target,
                PlatformSourceEvidenceIdentity.Create("second-incomplete")),
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
    public void CompletedReceipt_EnforcesDiscoveryAndDocumentationPolicy()
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
            [target],
            PlatformSourceEvidenceIdentity.Create("discovery-evidence"));
        var selectedDiscovery = new PlatformSourceSettlement(
            discovery,
            PlatformSourceSettlementDisposition.Selected);
        var incompleteDiscovery = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.TargetDiscovery,
                secondDiscovery,
                discoveryRequest.Snapshot,
                PlatformSourceGeneration.Create("discovery-2"),
                exactTarget: null,
                PlatformSourceEvidenceIdentity.Create(
                    "incomplete-discovery")),
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
                    [discovery],
                    PlatformSourceEvidenceIdentity.Create("selection")),
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
            [],
            PlatformSourceEvidenceIdentity.Create("empty-discovery"));
        var offeredDiscovery = new PlatformSourceContribution.TargetDiscovery(
            secondDiscovery,
            fallbackDiscoveryRequest.Snapshot,
            PlatformSourceGeneration.Create("offered-discovery"),
            [target],
            PlatformSourceEvidenceIdentity.Create("offered-discovery"));
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
                [offeredDiscovery],
                PlatformSourceEvidenceIdentity.Create("fallback-selection")),
            [
                retainedEmptyDiscovery,
                retainedOfferedDiscovery,
                fallbackReference,
            ],
            Consumed(),
            fallbackRealization);

        Assert.Same(fallbackRealization, fallbackDiscoveryReceipt.Completion);

        PlatformSourceCapabilityIdentity firstXml =
            PlatformSourceCapabilityIdentity.Create("first-xml");
        PlatformSourceCapabilityIdentity secondXml =
            PlatformSourceCapabilityIdentity.Create("second-xml");
        var documentationOperation = new PlatformHouseOperation
            .ResolveDocumentationEvidence
            .CompiledXml<TestDocumentationSubject, TestReferenceEvidence>(
                new PlatformDocumentationSubjectEvidence<
                    TestDocumentationSubject>(
                        new TestDocumentationSubject(),
                        "member"),
                new PlatformReferenceEvidence<TestReferenceEvidence>(
                    new TestReferenceEvidence(),
                    "reference"));
        var exact = new PlatformTargetDemand.Exact(target);
        var documentationRequest = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("documentation"),
            exact,
            StandaloneOrigin(),
            documentationOperation,
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("documentation-plan"),
                PlatformSourcePolicyGeneration.Create("generation"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.CompiledXml,
                        PlatformSourceSelectionMode.Fallback,
                        [firstXml, secondXml]),
                ]),
            Work());
        var laterXml = new PlatformSourceSettlement(
            new PlatformSourceContribution.Documentation(
                PlatformSourceFacet.CompiledXml,
                secondXml,
                documentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-2"),
                target,
                PlatformSourceEvidenceIdentity.Create("xml-contribution")),
            PlatformSourceSettlementDisposition.Selected);
        var xmlAttempt = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Available,
            [laterXml],
            PlatformSourceEvidenceIdentity.Create("xml-attempt"));
        var documentationCompletion =
            new PlatformHouseCompletion.Documentation(
                (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                    documentationRequest.Snapshot.Operation,
                [xmlAttempt]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                documentationRequest.Snapshot,
                new PlatformTargetSettlement.Exact(exact),
                [laterXml],
                Consumed(),
                documentationCompletion));

        var earlierXmlFailure = new PlatformSourceSettlement(
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.CompiledXml,
                firstXml,
                documentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-1"),
                target,
                PlatformSourceEvidenceIdentity.Create("xml-failure")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var mislabeledAttempt = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Failed,
            [earlierXmlFailure, laterXml],
            PlatformSourceEvidenceIdentity.Create("mislabeled-attempt"));
        var mislabeledCompletion =
            new PlatformHouseCompletion.Documentation(
                (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                    documentationRequest.Snapshot.Operation,
                [mislabeledAttempt]);

        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                documentationRequest.Snapshot,
                new PlatformTargetSettlement.Exact(exact),
                [earlierXmlFailure, laterXml],
                Consumed(),
                mislabeledCompletion));

        var fallbackIncompleteXml = new PlatformSourceSettlement(
            new PlatformSourceContribution.Incomplete(
                PlatformSourceFacet.CompiledXml,
                firstXml,
                documentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-incomplete"),
                target,
                PlatformSourceEvidenceIdentity.Create("xml-incomplete")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var fallbackAbsentXml = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.CompiledXml,
                secondXml,
                documentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-absent"),
                target,
                PlatformSourceUnavailabilityKind.Absent,
                PlatformSourceEvidenceIdentity.Create("xml-absent")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var incompleteAttempt = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Incomplete,
            [fallbackIncompleteXml, fallbackAbsentXml],
            PlatformSourceEvidenceIdentity.Create("incomplete-attempt"));
        var incompleteCompletion =
            new PlatformHouseCompletion.Documentation(
                (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                    documentationRequest.Snapshot.Operation,
                [incompleteAttempt]);
        var incompleteReceipt = new PlatformHouseReceipt(
            documentationRequest.Snapshot,
            new PlatformTargetSettlement.Exact(exact),
            [fallbackIncompleteXml, fallbackAbsentXml],
            Consumed(),
            incompleteCompletion);

        Assert.Same(incompleteCompletion, incompleteReceipt.Completion);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                documentationRequest.Snapshot,
                new PlatformTargetSettlement.Exact(exact),
                [fallbackIncompleteXml, fallbackAbsentXml],
                Consumed(),
                new PlatformHouseCompletion.Documentation(
                    (PlatformHouseOperationSnapshot
                        .ResolveDocumentationEvidence)
                            documentationRequest.Snapshot.Operation,
                    [
                        new PlatformDocumentationAttempt(
                            PlatformSourceFacet.CompiledXml,
                            PlatformDocumentationAttemptKind.Absent,
                            [fallbackIncompleteXml, fallbackAbsentXml],
                            PlatformSourceEvidenceIdentity.Create(
                                "incorrect-fallback-absence")),
                    ])));

        var aggregationDocumentationRequest = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("aggregation-documentation"),
            exact,
            StandaloneOrigin(),
            documentationOperation,
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "aggregation-documentation-plan"),
                PlatformSourcePolicyGeneration.Create("generation"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.CompiledXml,
                        PlatformSourceSelectionMode.Aggregation,
                        [firstXml, secondXml]),
                ]),
            Work());
        var unavailableXml = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.CompiledXml,
                firstXml,
                aggregationDocumentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-unavailable"),
                target,
                PlatformSourceUnavailabilityKind.Unavailable,
                PlatformSourceEvidenceIdentity.Create("xml-unavailable")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var failedXml = new PlatformSourceSettlement(
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.CompiledXml,
                secondXml,
                aggregationDocumentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-failed"),
                target,
                PlatformSourceEvidenceIdentity.Create("xml-failed")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var aggregateAttempt = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Failed,
            [unavailableXml, failedXml],
            PlatformSourceEvidenceIdentity.Create("aggregate-attempt"));
        var aggregateCompletion =
            new PlatformHouseCompletion.Documentation(
                (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                    aggregationDocumentationRequest.Snapshot.Operation,
                [aggregateAttempt]);
        var aggregateReceipt = new PlatformHouseReceipt(
            aggregationDocumentationRequest.Snapshot,
            new PlatformTargetSettlement.Exact(exact),
            [unavailableXml, failedXml],
            Consumed(),
            aggregateCompletion);

        Assert.Same(aggregateCompletion, aggregateReceipt.Completion);

        var firstAbsent = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.CompiledXml,
                firstXml,
                aggregationDocumentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-absent-1"),
                target,
                PlatformSourceUnavailabilityKind.Absent,
                PlatformSourceEvidenceIdentity.Create("xml-absent-1")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var secondAbsent = new PlatformSourceSettlement(
            new PlatformSourceContribution.Unavailable(
                PlatformSourceFacet.CompiledXml,
                secondXml,
                aggregationDocumentationRequest.Snapshot,
                PlatformSourceGeneration.Create("xml-absent-2"),
                target,
                PlatformSourceUnavailabilityKind.Absent,
                PlatformSourceEvidenceIdentity.Create("xml-absent-2")),
            PlatformSourceSettlementDisposition.OutcomeRelevant);
        var absentAttempt = new PlatformDocumentationAttempt(
            PlatformSourceFacet.CompiledXml,
            PlatformDocumentationAttemptKind.Absent,
            [firstAbsent, secondAbsent],
            PlatformSourceEvidenceIdentity.Create("absent-attempt"));
        var absentCompletion = new PlatformHouseCompletion.Documentation(
            (PlatformHouseOperationSnapshot.ResolveDocumentationEvidence)
                aggregationDocumentationRequest.Snapshot.Operation,
            [absentAttempt]);
        var absentReceipt = new PlatformHouseReceipt(
            aggregationDocumentationRequest.Snapshot,
            new PlatformTargetSettlement.Exact(exact),
            [firstAbsent, secondAbsent],
            Consumed(),
            absentCompletion);

        Assert.Same(absentCompletion, absentReceipt.Completion);
        Assert.Throws<ArgumentException>(
            () => new PlatformHouseReceipt(
                aggregationDocumentationRequest.Snapshot,
                new PlatformTargetSettlement.Exact(exact),
                [firstAbsent, secondAbsent],
                Consumed(),
                new PlatformHouseCompletion.Documentation(
                    (PlatformHouseOperationSnapshot
                        .ResolveDocumentationEvidence)
                            aggregationDocumentationRequest
                                .Snapshot.Operation,
                    [
                        new PlatformDocumentationAttempt(
                            PlatformSourceFacet.CompiledXml,
                            PlatformDocumentationAttemptKind.Unavailable,
                            [firstAbsent, secondAbsent],
                            PlatformSourceEvidenceIdentity.Create(
                                "wrong-absence")),
                    ])));
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
                PlatformTargetCorrespondenceIdentity.Create("reference-target"),
                population,
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create("reference-evidence")),
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
            PlatformTargetCorrespondenceIdentity.Create("reference-target"),
            ((PlatformHouseOperation.Realize)request.Operation).Population,
            PlatformSourceContributionCompleteness.Authoritative,
            PlatformSourceEvidenceIdentity.Create("reference-evidence"));

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
        TimeSpan? elapsed = null) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
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
                PlatformTargetCorrespondenceIdentity.Create($"{name}-target"),
                ((PlatformHouseOperation.Realize)request.Operation).Population,
                PlatformSourceContributionCompleteness.Authoritative,
                PlatformSourceEvidenceIdentity.Create($"{name}-evidence")),
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
    sealed class TestDocumentationSubject;
    sealed class TestReferenceEvidence;
    sealed class TestViewCorrespondence;
    sealed class TestMetadataOutcome;
}
