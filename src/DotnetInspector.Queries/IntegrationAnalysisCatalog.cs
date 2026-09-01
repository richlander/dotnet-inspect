using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Integration-owned concept identity retained by generic universe
/// requirements.
/// </summary>
public sealed class IntegrationConceptRequirementIdentity :
    IAnalysisRequirementAffectedIdentity
{
    internal IntegrationConceptRequirementIdentity(
        IntegrationConceptDescriptor concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        Concept = concept;
    }

    public IntegrationConceptDescriptor Concept { get; }
}

/// <summary>
/// One Integration producer policy bound to its query, relationship, and
/// universe-evidence declarations.
/// </summary>
public sealed class IntegrationProducerPolicyBinding
{
    internal IntegrationProducerPolicyBinding(
        IntegrationProducerPolicyDescriptor policy,
        InspectionQueryDefinition query,
        InspectionGraphRelationshipDescriptor relationship,
        IEnumerable<IntegrationConceptRequirementIdentity> affected)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(affected);
        if (!string.Equals(
                policy.RelationshipId,
                relationship.Id,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The producer policy and graph relationship identifiers must agree.",
                nameof(relationship));
        }

        Policy = policy;
        Query = query;
        Relationship = relationship;
        Affected = [.. affected];
        if (Affected.IsEmpty
            || !Affected.Select(identity => identity.Concept)
                .SequenceEqual(policy.Concepts))
        {
            throw new ArgumentException(
                "Affected identities must exactly match the producer policy concepts.",
                nameof(affected));
        }

        ProducerPrerequisite = new AnalysisProducerPrerequisiteDescriptor(
            Declaration($"{policy.Id.Value}.registration"));
        QueryPrerequisite = new AnalysisQueryPrerequisiteDescriptor(
            Declaration($"{policy.Id.Value}.query"),
            query);
        EvidenceCapability = new AnalysisUniverseCapabilityDescriptor(
            Declaration($"{policy.Id.Value}.evidence"),
            $"Structured evidence for producer policy {policy.Id.Value}.");
        UniverseRequirement = new AnalysisUniverseRequirementDescriptor(
            Declaration($"{policy.Id.Value}.requirement"),
            EvidenceCapability,
            [AnalysisQuestionMode.Census],
            Affected);
    }

    public IntegrationProducerPolicyDescriptor Policy { get; }
    public InspectionQueryDefinition Query { get; }
    public InspectionGraphRelationshipDescriptor Relationship { get; }
    public ImmutableArray<IntegrationConceptRequirementIdentity> Affected { get; }
    public AnalysisProducerPrerequisiteDescriptor ProducerPrerequisite { get; }
    public AnalysisQueryPrerequisiteDescriptor QueryPrerequisite { get; }
    public AnalysisUniverseCapabilityDescriptor EvidenceCapability { get; }
    public AnalysisUniverseRequirementDescriptor UniverseRequirement { get; }

    static AnalysisDeclarationId Declaration(string value) => new(value);
}

/// <summary>
/// Structural capability declarations for the Integration Workspace Census.
/// Accessing the catalog performs no producer execution or Section probing.
/// </summary>
public static class IntegrationAnalysisCatalog
{
    const int AnalysisRevision = 2;

    static readonly Dictionary<
        IntegrationConceptDescriptor,
        IntegrationConceptRequirementIdentity> AffectedByConcept;
    static readonly Dictionary<
        AnalysisUniverseRequirementDescriptor,
        IntegrationProducerPolicyBinding> ProducerPolicyByRequirement;

    static IntegrationAnalysisCatalog()
    {
        ConceptIdentities =
        [
            .. IntegrationConceptCatalog.Concepts.Select(
                concept => new IntegrationConceptRequirementIdentity(concept)),
        ];
        AffectedByConcept = new(
            ReferenceEqualityComparer.Instance);
        foreach (IntegrationConceptRequirementIdentity identity
            in ConceptIdentities)
        {
            AffectedByConcept.Add(identity.Concept, identity);
        }

        EcosystemObserved = Bind(
            IntegrationConceptCatalog.EcosystemObserved,
            AssemblyContextIntegrationsQuery.Definition,
            InspectionGraphIntegrationsCatalog.IntegrationObserved);
        OpenTelemetryObserved = Bind(
            IntegrationConceptCatalog.OpenTelemetryObserved,
            AssemblyContextIntegrationsQuery.Definition,
            InspectionGraphIntegrationsCatalog.IntegrationObserved);
        Opportunity = Bind(
            IntegrationConceptCatalog.Opportunity,
            AssemblyContextIntegrationOpportunitiesQuery.Definition,
            InspectionGraphIntegrationsCatalog.IntegrationOpportunity);
        ProducerPolicies =
        [
            EcosystemObserved,
            OpenTelemetryObserved,
            Opportunity,
        ];
        ProducerPolicyByRequirement = new(
            ReferenceEqualityComparer.Instance);
        foreach (IntegrationProducerPolicyBinding policy in ProducerPolicies)
            ProducerPolicyByRequirement.Add(policy.UniverseRequirement, policy);

        SelectedTypes = Capability(
            "universe.integration.selected-types",
            "Finite selected-Type population with owner-issued Type identity.");
        OrderedParticipants = Capability(
            "universe.integration.ordered-participants",
            "Ordered source participants with typed outcomes and authoritative provenance.");
        BindingContexts = Capability(
            "universe.integration.binding-contexts",
            "Stable comparable binding-context identity, deterministic context order, and authoritative source incidence.");
        PeerBinding = Capability(
            "universe.integration.peer-binding",
            "Structured peer-reference binding in each declared context.");
        ExactPeerResolution = Capability(
            "universe.integration.exact-peer-resolution",
            "Exact terminal peer resolution over a finite comparison domain.");
        Completeness = Capability(
            "universe.integration.completeness",
            "Retained completeness limits and rejected, unavailable, and failed members.");

        CommonUniverseRequirements =
        [
            Requirement(
                "requirement.integration.selected-types",
                SelectedTypes),
            Requirement(
                "requirement.integration.ordered-participants",
                OrderedParticipants),
            BindingContextsRequirement = Requirement(
                "requirement.integration.binding-contexts",
                BindingContexts),
            Requirement(
                "requirement.integration.peer-binding",
                PeerBinding),
            Requirement(
                "requirement.integration.exact-peer-resolution",
                ExactPeerResolution),
            Requirement(
                "requirement.integration.completeness",
                Completeness),
        ];
        UniverseRequirements =
        [
            .. CommonUniverseRequirements,
            .. ProducerPolicies.Select(binding =>
                binding.UniverseRequirement),
        ];

        WorkspaceDomain = new AnalysisTargetRoleDescriptor(
            Declaration("target.integration.workspace-domain"),
            AnalysisTargetFunction.ReportDomain,
            minimumCount: 1,
            maximumCount: 1);
        Rows = new AnalysisProjectionDescriptor(
            Declaration("projection.integration.rows"));
        Matrix = new AnalysisProjectionDescriptor(
            Declaration("projection.integration.matrix"));
        Graph = new AnalysisProjectionDescriptor(
            Declaration("projection.integration.graph"));

        Analysis = new AnalysisDescriptor(
            Declaration("analysis.integrations"),
            AnalysisRevision,
            InspectionCost.Unbounded,
            [AnalysisQuestionMode.Census],
            [
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    [WorkspaceDomain]),
            ],
            UniverseRequirements,
            [
                .. ProducerPolicies.SelectMany(binding =>
                    new AnalysisStructuralPrerequisiteDescriptor[]
                    {
                        binding.ProducerPrerequisite,
                        binding.QueryPrerequisite,
                    }),
            ],
            [
                new AnalysisHostRequirementDescriptor(
                    Declaration("host.integration.explicit-analysis")),
            ],
            [
                new AnalysisProjectionSupport(
                    Rows,
                    [AnalysisQuestionMode.Census]),
                new AnalysisProjectionSupport(
                    Matrix,
                    [AnalysisQuestionMode.Census]),
                new AnalysisProjectionSupport(
                    Graph,
                    [AnalysisQuestionMode.Census]),
            ]);
        Capabilities = new AnalysisCapabilityCatalog([Analysis]);
    }

    public static ImmutableArray<IntegrationConceptDescriptor> Concepts =>
        IntegrationConceptCatalog.Concepts;
    public static ImmutableArray<IntegrationConceptRequirementIdentity>
        ConceptIdentities { get; }
    public static ImmutableArray<IntegrationProducerPolicyBinding>
        ProducerPolicies { get; }
    public static ImmutableArray<AnalysisUniverseRequirementDescriptor>
        CommonUniverseRequirements { get; }
    public static ImmutableArray<AnalysisUniverseRequirementDescriptor>
        UniverseRequirements { get; }

    public static IntegrationProducerPolicyBinding EcosystemObserved { get; }
    public static IntegrationProducerPolicyBinding OpenTelemetryObserved { get; }
    public static IntegrationProducerPolicyBinding Opportunity { get; }

    public static AnalysisUniverseCapabilityDescriptor SelectedTypes { get; }
    public static AnalysisUniverseCapabilityDescriptor OrderedParticipants { get; }
    public static AnalysisUniverseCapabilityDescriptor BindingContexts { get; }
    public static AnalysisUniverseRequirementDescriptor
        BindingContextsRequirement { get; }
    public static AnalysisUniverseCapabilityDescriptor PeerBinding { get; }
    public static AnalysisUniverseCapabilityDescriptor ExactPeerResolution { get; }
    public static AnalysisUniverseCapabilityDescriptor Completeness { get; }

    public static AnalysisTargetRoleDescriptor WorkspaceDomain { get; }
    public static AnalysisProjectionDescriptor Rows { get; }
    public static AnalysisProjectionDescriptor Matrix { get; }
    public static AnalysisProjectionDescriptor Graph { get; }
    public static AnalysisDescriptor Analysis { get; }
    public static AnalysisCapabilityCatalog Capabilities { get; }

    public static IntegrationConceptRequirementIdentity IdentityOf(
        IntegrationConceptDescriptor concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return AffectedByConcept.TryGetValue(concept, out var identity)
            ? identity
            : throw new ArgumentException(
                "The concept is not configured in this Integration catalog.",
                nameof(concept));
    }

    public static bool TryGetProducerPolicy(
        AnalysisUniverseRequirementDescriptor requirement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out IntegrationProducerPolicyBinding? policy)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return ProducerPolicyByRequirement.TryGetValue(
            requirement,
            out policy);
    }

    public static IntegrationBindingContextAccess GetBindingContextAccess(
        AnalysisUniverseExecutionAccess executionAccess)
    {
        ArgumentNullException.ThrowIfNull(executionAccess);
        return executionAccess
            .GetBinding<IntegrationBindingContextAccess>(
                BindingContextsRequirement)
            .Access;
    }

    static IntegrationProducerPolicyBinding Bind(
        IntegrationProducerPolicyDescriptor policy,
        InspectionQueryDefinition query,
        InspectionGraphRelationshipDescriptor relationship) =>
        new(
            policy,
            query,
            relationship,
            policy.Concepts.Select(concept =>
                AffectedByConcept[concept]));

    static AnalysisUniverseCapabilityDescriptor Capability(
        string id,
        string summary) =>
        new(Declaration(id), summary);

    static AnalysisUniverseRequirementDescriptor Requirement(
        string id,
        AnalysisUniverseCapabilityDescriptor capability) =>
        new(
            Declaration(id),
            capability,
            [AnalysisQuestionMode.Census],
            ConceptIdentities);

    static AnalysisDeclarationId Declaration(string value) => new(value);
}
