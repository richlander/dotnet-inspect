using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class IntegrationAnalysisCatalogTests
{
    [Fact]
    public void IntegrationCapability_ListsConfiguredUnobservedConcepts()
    {
        AnalysisDescriptor analysis = Assert.Single(
            IntegrationAnalysisCatalog.Capabilities.Analyses);

        Assert.Same(IntegrationAnalysisCatalog.Analysis, analysis);
        Assert.Equal(
            IntegrationConceptCatalog.Concepts,
            IntegrationAnalysisCatalog.Concepts);
        Assert.Equal(
            IntegrationAnalysisCatalog.Concepts,
            IntegrationAnalysisCatalog.ConceptIdentities.Select(
                identity => identity.Concept));
        Assert.All(
            IntegrationAnalysisCatalog.Concepts,
            concept => Assert.NotEmpty(concept.ProducerPolicies));
    }

    [Fact]
    public void IntegrationCapability_DoesNotExecuteProducersOrProbeSections()
    {
        int executions = 0;
        InspectionQueryCatalog<object> queryCatalog =
            QueryCatalog(() => executions++);

        AnalysisRequestPlanResult result =
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(Universe()),
                Environment(queryCatalog));

        Assert.IsType<AnalysisRequestPlanResult.Accepted>(result);
        Assert.Equal(0, executions);
        Assert.All(
            IntegrationAnalysisCatalog.Analysis.StructuralPrerequisites,
            prerequisite => Assert.True(
                prerequisite is AnalysisProducerPrerequisiteDescriptor
                    or AnalysisQueryPrerequisiteDescriptor));
    }

    [Fact]
    public void IntegrationCapability_RejectsUnsupportedCensusRequestBeforeExecution()
    {
        int executions = 0;
        AnalysisRequest request = Request(
            Universe(),
            new AnalysisProjectionDescriptor(
                new AnalysisDeclarationId("projection.integration.lookalike")));

        var rejected = Assert.IsType<AnalysisRequestPlanResult.Rejected>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                request,
                Environment(QueryCatalog(() => executions++))));

        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedProjection,
            rejected.Rejection.Reason);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void IntegrationCapability_DeclaresTypedUniverseRequirementsByConcept()
    {
        Assert.Equal(
            [
                IntegrationAnalysisCatalog.EcosystemObserved,
                IntegrationAnalysisCatalog.OpenTelemetryObserved,
                IntegrationAnalysisCatalog.Opportunity,
            ],
            IntegrationAnalysisCatalog.ProducerPolicies);
        AssertPolicy(
            IntegrationAnalysisCatalog.EcosystemObserved,
            IntegrationConceptCatalog.Concepts.Where(concept =>
                !ReferenceEquals(
                    concept,
                    IntegrationConceptCatalog.OpenTelemetry)));
        AssertPolicy(
            IntegrationAnalysisCatalog.OpenTelemetryObserved,
            [IntegrationConceptCatalog.OpenTelemetry]);
        AssertPolicy(
            IntegrationAnalysisCatalog.Opportunity,
            [
                IntegrationConceptCatalog.AI,
                IntegrationConceptCatalog.Aspire,
                IntegrationConceptCatalog.Authentication,
                IntegrationConceptCatalog.Configuration,
                IntegrationConceptCatalog.DependencyInjection,
                IntegrationConceptCatalog.HealthChecks,
            ]);

        Assert.All(
            IntegrationAnalysisCatalog.CommonUniverseRequirements,
            requirement => Assert.Equal(
                IntegrationAnalysisCatalog.Concepts,
                ConceptsAffectedBy(requirement)));
        Assert.Equal(
            IntegrationAnalysisCatalog.UniverseRequirements,
            IntegrationAnalysisCatalog.Analysis.UniverseRequirements);
    }

    [Fact]
    public void IntegrationCapability_UnsatisfiedUniverseNamesRequirementsAndConcepts()
    {
        IntegrationProducerPolicyBinding policy =
            IntegrationAnalysisCatalog.Opportunity;
        AnalysisUniverseDescription universe = Universe(
            IntegrationAnalysisCatalog.UniverseRequirements
                .Where(requirement =>
                    !ReferenceEquals(
                        requirement,
                        policy.UniverseRequirement))
                .Select(requirement => requirement.Capability));

        var rejected = Assert.IsType<AnalysisRequestPlanResult.Rejected>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(universe),
                Environment(QueryCatalog())));

        AnalysisUniverseRequirementDescriptor requirement = Assert.Single(
            rejected.Rejection.UniverseRequirements);
        Assert.Same(policy.UniverseRequirement, requirement);
        Assert.Equal(policy.Policy.Concepts, ConceptsAffectedBy(requirement));
    }

    [Fact]
    public void IntegrationCapability_ValidatedUniverseRetainsExactRequirementIdentities()
    {
        var accepted = Assert.IsType<AnalysisRequestPlanResult.Accepted>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(Universe()),
                Environment(QueryCatalog())));

        Assert.Equal(
            IntegrationAnalysisCatalog.UniverseRequirements,
            accepted.Plan.UniverseRequirements);
        for (int i = 0; i < accepted.Plan.UniverseRequirements.Length; i++)
        {
            Assert.Same(
                IntegrationAnalysisCatalog.UniverseRequirements[i],
                accepted.Plan.UniverseRequirements[i]);
        }
        Assert.Equal(InspectionCost.Unbounded, accepted.Plan.Cost);
    }

    [Fact]
    public void IntegrationCapability_RequiresStableOrderedBindingContextIdentityAndIncidence()
    {
        AnalysisUniverseRequirementDescriptor bindingContexts =
            Assert.Single(
                IntegrationAnalysisCatalog.UniverseRequirements,
                requirement => ReferenceEquals(
                    requirement.Capability,
                    IntegrationAnalysisCatalog.BindingContexts));
        Assert.Same(
            IntegrationAnalysisCatalog.BindingContextsRequirement,
            bindingContexts);
        Assert.Equal(2, IntegrationAnalysisCatalog.Analysis.Revision);
        Assert.Contains(
            "incidence",
            IntegrationAnalysisCatalog.BindingContexts.Summary,
            StringComparison.Ordinal);
        AnalysisUniverseDescription universe = Universe(
            IntegrationAnalysisCatalog.UniverseRequirements
                .Where(requirement =>
                    !ReferenceEquals(requirement, bindingContexts))
                .Select(requirement => requirement.Capability));

        var rejected = Assert.IsType<AnalysisRequestPlanResult.Rejected>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(universe),
                Environment(QueryCatalog())));

        Assert.Equal(
            AnalysisRequestRejectionReason.UnsatisfiedUniverse,
            rejected.Rejection.Reason);
        Assert.Same(
            bindingContexts,
            Assert.Single(rejected.Rejection.UniverseRequirements));
    }

    [Fact]
    public void IntegrationCapability_PartialProducerPolicyEvidenceNamesAffectedConcepts()
    {
        IntegrationProducerPolicyBinding omitted =
            IntegrationAnalysisCatalog.OpenTelemetryObserved;
        AnalysisUniverseDescription universe = Universe(
            IntegrationAnalysisCatalog.UniverseRequirements
                .Where(requirement =>
                    !ReferenceEquals(
                        requirement,
                        omitted.UniverseRequirement))
                .Select(requirement => requirement.Capability));

        var rejected = Assert.IsType<AnalysisRequestPlanResult.Rejected>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(universe),
                Environment(QueryCatalog())));

        AnalysisUniverseRequirementDescriptor requirement = Assert.Single(
            rejected.Rejection.UniverseRequirements);
        Assert.Same(omitted.UniverseRequirement, requirement);
        Assert.Equal(
            [IntegrationConceptCatalog.OpenTelemetry],
            ConceptsAffectedBy(requirement));
    }

    [Fact]
    public void IntegrationCapability_EveryDeclaredUniverseRequirementHasPositiveAndNegativeCoverage()
    {
        InspectionQueryCatalog<object> queryCatalog = QueryCatalog();
        AnalysisPlanningEnvironment environment = Environment(queryCatalog);
        Assert.IsType<AnalysisRequestPlanResult.Accepted>(
            IntegrationAnalysisCatalog.Capabilities.Plan(
                Request(Universe()),
                environment));

        foreach (AnalysisUniverseRequirementDescriptor omitted
            in IntegrationAnalysisCatalog.UniverseRequirements)
        {
            AnalysisUniverseDescription universe = Universe(
                IntegrationAnalysisCatalog.UniverseRequirements
                    .Where(requirement =>
                        !ReferenceEquals(requirement, omitted))
                    .Select(requirement => requirement.Capability));
            var rejected = Assert.IsType<AnalysisRequestPlanResult.Rejected>(
                IntegrationAnalysisCatalog.Capabilities.Plan(
                    Request(universe),
                    environment));

            Assert.Equal(
                AnalysisRequestRejectionReason.UnsatisfiedUniverse,
                rejected.Rejection.Reason);
            Assert.Same(
                omitted,
                Assert.Single(rejected.Rejection.UniverseRequirements));
        }
    }

    [Fact]
    public void IntegrationCatalog_RevisionMirrorsDeclarationShapeAndPolicyMapping()
    {
        IntegrationConceptCatalogRevision revision =
            IntegrationConceptCatalog.Revision;

        Assert.Equal(1, revision.Number);
        Assert.Equal(
            IntegrationConceptCatalog.Concepts.Select(concept => concept.Id),
            revision.ConceptIds);
        Assert.Equal(
            IntegrationConceptCatalog.ProducerPolicies.Length,
            revision.ProducerPolicies.Length);
        for (int index = 0; index < revision.ProducerPolicies.Length; index++)
        {
            IntegrationProducerPolicyDescriptor descriptor =
                IntegrationConceptCatalog.ProducerPolicies[index];
            IntegrationProducerPolicyRevision policy =
                revision.ProducerPolicies[index];
            Assert.Equal(descriptor.Id, policy.Id);
            Assert.Equal(descriptor.RelationshipId, policy.RelationshipId);
            Assert.Equal(
                descriptor.Concepts.Select(concept => concept.Id),
                policy.ConceptIds);
        }
    }

    static void AssertPolicy(
        IntegrationProducerPolicyBinding binding,
        IEnumerable<IntegrationConceptDescriptor> expectedConcepts)
    {
        IntegrationConceptDescriptor[] concepts = [.. expectedConcepts];
        Assert.Equal(concepts, binding.Policy.Concepts);
        Assert.Equal(concepts, binding.Affected.Select(
            identity => identity.Concept));
        Assert.All(
            concepts,
            concept => Assert.Contains(
                binding.Policy,
                concept.ProducerPolicies));
        Assert.Equal(
            binding.Policy.RelationshipId,
            binding.Relationship.Id);
        Assert.Same(
            binding.Query,
            binding.QueryPrerequisite.Query);
    }

    static ImmutableArray<IntegrationConceptDescriptor> ConceptsAffectedBy(
        AnalysisUniverseRequirementDescriptor requirement) =>
        [
            .. requirement.Affected
                .Cast<IntegrationConceptRequirementIdentity>()
                .Select(identity => identity.Concept),
        ];

    static AnalysisRequest Request(
        AnalysisUniverseDescription universe,
        AnalysisProjectionDescriptor? projection = null) =>
        new(
            IntegrationAnalysisCatalog.Analysis,
            new AnalysisReportSurface(
                AnalysisReportSurfaceKind.Workspace,
                new WorkspaceIdentity(),
                [
                    new AnalysisTargetBinding(
                        IntegrationAnalysisCatalog.WorkspaceDomain,
                        new WorkspaceTarget()),
                ]),
            universe,
            AnalysisQuestionMode.Census,
            projection ?? IntegrationAnalysisCatalog.Rows);

    static AnalysisUniverseDescription Universe(
        IEnumerable<AnalysisUniverseCapabilityDescriptor>? capabilities = null)
        => new(
            new UniverseIdentity(),
            new UniverseBoundary(),
            new UniverseBoundary(),
            isFinite: true,
            capabilities
                ?? IntegrationAnalysisCatalog.UniverseRequirements.Select(
                    requirement => requirement.Capability),
            new UniverseCompleteness());

    static AnalysisPlanningEnvironment Environment(
        IInspectionQueryCatalog queryCatalog) =>
        new(
            queryCatalog,
            IntegrationAnalysisCatalog.ProducerPolicies.Select(
                policy => policy.ProducerPrerequisite));

    static InspectionQueryCatalog<object> QueryCatalog(
        Action? executed = null) =>
        new InspectionQueryRegistry<object>()
            .Add(
                AssemblyContextIntegrationsQuery.Definition,
                _ =>
                {
                    executed?.Invoke();
                    return new AssemblyContextIntegrationsResult([]);
                })
            .Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                (_, _) =>
                {
                    executed?.Invoke();
                    return new AssemblyContextIntegrationOpportunitiesResult(
                        []);
                },
                AssemblyContextIntegrationsQuery.Definition)
            .Compile();

    sealed class WorkspaceIdentity : IAnalysisReportSurfaceIdentity;
    sealed class WorkspaceTarget : IAnalysisTargetIdentity;
    sealed class UniverseIdentity : IAnalysisUniverseIdentity;
    sealed class UniverseBoundary : IAnalysisUniverseBoundary;
    sealed class UniverseCompleteness : IAnalysisUniverseCompleteness;
}
