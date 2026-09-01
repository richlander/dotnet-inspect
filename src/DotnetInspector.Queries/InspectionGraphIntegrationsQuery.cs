using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>Metadata-owned relationships used by Integration graph projection.</summary>
public static class InspectionGraphIntegrationsCatalog
{
    static InspectionGraphOccurrenceIdentityProjection
        OccurrenceIdentity { get; } =
        new IntegrationOccurrenceIdentityProjection();

    static InspectionGraphEndpointProjection
        OpportunityEndpointProjection { get; } =
        new OpportunityEndpointProjectionImpl();

    public static InspectionGraphEvidenceDescriptor ExtensionEvidence { get; } =
        new("metadata.extension-api", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor IntegrationEvidence
        { get; } =
        new("metadata.integration-api", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor ReferenceEvidence { get; } =
        new("metadata.assembly-reference", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor OpportunityEvidence
        { get; } =
        new("metadata.integration-opportunity", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor FailureEvidence { get; } =
        new("queries.integration-graph-failure", InspectionGraphOwner.Queries);

    public static InspectionGraphRelationshipDescriptor Extension { get; } =
        new(
            "api.extension",
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                new(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [ExtensionEvidence]);

    public static InspectionGraphRelationshipDescriptor IntegrationObserved
        { get; } =
        new(
            "integration.observed",
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                new(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [IntegrationEvidence]);

    public static InspectionGraphRelationshipDescriptor MetadataReference
        { get; } =
        new(
            "metadata.reference",
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Assembly],
            [InspectionGraphSubjectKind.Assembly],
            [InspectionGraphSubjectKind.Assembly],
            [InspectionGraphSubjectKind.Assembly],
            [
                new(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [ReferenceEvidence]);

    public static InspectionGraphRelationshipDescriptor
        IntegrationOpportunity { get; } =
        new(
            "integration.opportunity",
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Assembly],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Assembly,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.OccurrenceEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
                new(
                    InspectionGraphSubjectKind.Package,
                    InspectionGraphSeedAdmissionKind.OwnedSubjects,
                    InspectionGraphEndpointRole.Source),
            ],
            OpportunityEndpointProjection,
            OccurrenceIdentity,
            [OpportunityEvidence]);

    public static InspectionGraphFailureDescriptor ProjectionFailure { get; } =
        new(
            "queries.integration-graph-incomplete",
            InspectionGraphOwner.Queries,
            [FailureEvidence]);

    public static ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships { get; } =
        [
            Extension,
            IntegrationObserved,
            MetadataReference,
            IntegrationOpportunity,
        ];

    sealed class IntegrationOccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence switch
            {
                InspectionGraphExtensionEvidence extension =>
                    new NamedTypeOccurrenceIdentity(
                        extension.Registration,
                        extension.Member,
                        concept: null,
                        extension.ExtendedType),
                InspectionGraphIntegrationEvidence integration =>
                    new NamedTypeOccurrenceIdentity(
                        integration.Registration,
                        integration.Member,
                        RequireConcept(
                            integration.Integration,
                            integration.GetConcept()),
                        integration.TargetType),
                InspectionGraphReferenceEvidence reference =>
                    new ReferenceOccurrenceIdentity(
                        reference.SourceRegistration,
                        reference.Reference),
                InspectionGraphOpportunityEvidence opportunity =>
                    (
                        opportunity.SourceRegistration,
                        opportunity.SourceType,
                        RequireConcept(
                            opportunity.Integration,
                            opportunity.GetConcept()),
                        opportunity.Target),
                _ => throw new ArgumentException(
                    "Unsupported Integration graph occurrence evidence.",
                    nameof(occurrence)),
            };

        static IntegrationConceptDescriptor RequireConcept(
            string integration,
            IntegrationConceptDescriptor? concept) =>
            concept
            ?? throw new InspectionQueryException(
                $"Integration evidence '{integration}' is not configured.");

        sealed class NamedTypeOccurrenceIdentity :
            IEquatable<NamedTypeOccurrenceIdentity>
        {
            readonly AssemblyAcquisitionRegistration _registration;
            readonly MemberAnchor _member;
            readonly IntegrationConceptDescriptor? _concept;
            readonly MetadataNamedTypeReference _reference;

            internal NamedTypeOccurrenceIdentity(
                AssemblyAcquisitionRegistration registration,
                MemberAnchor member,
                IntegrationConceptDescriptor? concept,
                MetadataNamedTypeReference reference)
            {
                _registration = registration;
                _member = member;
                _concept = concept;
                _reference = reference;
            }

            public bool Equals(NamedTypeOccurrenceIdentity? other) =>
                other is not null
                && ReferenceEquals(
                    _registration,
                    other._registration)
                && _member == other._member
                && ReferenceEquals(_concept, other._concept)
                && MetadataNamedTypeReference.EquivalentComparer.Equals(
                    _reference,
                    other._reference);

            public override bool Equals(object? obj) =>
                obj is NamedTypeOccurrenceIdentity other
                && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(
                    _registration,
                    _member,
                    _concept,
                    MetadataNamedTypeReference.EquivalentComparer
                        .GetHashCode(_reference));
        }

        sealed class ReferenceOccurrenceIdentity :
            IEquatable<ReferenceOccurrenceIdentity>
        {
            readonly AssemblyAcquisitionRegistration _registration;
            readonly AssemblyReferenceIdentity _reference;

            internal ReferenceOccurrenceIdentity(
                AssemblyAcquisitionRegistration registration,
                AssemblyReferenceIdentity reference)
            {
                _registration = registration;
                _reference = reference;
            }

            public bool Equals(ReferenceOccurrenceIdentity? other) =>
                other is not null
                && ReferenceEquals(
                    _registration,
                    other._registration)
                && _reference.IsEquivalentTo(other._reference);

            public override bool Equals(object? obj) =>
                obj is ReferenceOccurrenceIdentity other
                && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(
                    _registration,
                    AssemblyReferenceIdentity.EquivalentComparer
                        .GetHashCode(_reference));
        }

    }

    sealed class OpportunityEndpointProjectionImpl :
        InspectionGraphEndpointProjection
    {
        public override bool Supports(
            InspectionGraphOccurrence occurrence,
            InspectionGraphEndpointRole role,
            InspectionGraphSubject endpoint)
        {
            if (role == InspectionGraphEndpointRole.Target)
                return occurrence.TargetSubject == endpoint;
            if (role != InspectionGraphEndpointRole.Source)
                throw new ArgumentOutOfRangeException(nameof(role));

            if (occurrence.SourceSubject == endpoint)
                return true;
            return occurrence.SourceSubject
                    is InspectionGraphSubject.TypeSubject
                    {
                        Identity:
                            InspectionGraphTypeIdentity.AcquiredDefinition
                            source,
                    }
                && endpoint
                    is InspectionGraphSubject.AssemblySubject
                    {
                        Identity:
                            InspectionGraphAssemblyIdentity.Acquired assembly,
                    }
                && ReferenceEquals(
                    source.Registration,
                    assembly.Registration);
        }
    }
}

/// <summary>Typed evidence for one extension-member relationship.</summary>
public sealed record InspectionGraphExtensionEvidence(
    AssemblyAcquisitionRegistration Registration,
    MemberAnchor Member,
    MetadataNamedTypeReference ExtendedType)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.ExtensionEvidence;
}

/// <summary>Typed evidence for one observed Integration API relationship.</summary>
public sealed record InspectionGraphIntegrationEvidence(
    AssemblyAcquisitionRegistration Registration,
    MemberAnchor Member,
    string Integration,
    MetadataNamedTypeReference TargetType)
    : IInspectionGraphOccurrenceEvidence
{
    string _integration = Integration;
    IntegrationConceptDescriptor? _concept = ResolveConcept(Integration);

    public AssemblyAcquisitionRegistration Registration { get; init; } =
        Registration;
    public MemberAnchor Member { get; init; } = Member;
    public string Integration
    {
        get => _integration;
        init
        {
            _integration = value;
            _concept = ResolveConcept(value);
        }
    }
    public MetadataNamedTypeReference TargetType { get; init; } = TargetType;

    internal InspectionGraphIntegrationEvidence(
        AssemblyAcquisitionRegistration registration,
        MemberAnchor member,
        IntegrationConceptDescriptor concept,
        MetadataNamedTypeReference targetType)
        : this(
            registration,
            member,
            concept.DisplayLabel,
            targetType)
    {
        _concept = concept;
    }

    public IntegrationConceptDescriptor? GetConcept() => _concept;

    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.IntegrationEvidence;

    public bool Equals(InspectionGraphIntegrationEvidence? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && EqualityComparer<AssemblyAcquisitionRegistration>.Default.Equals(
            Registration,
            other.Registration)
        && EqualityComparer<MemberAnchor>.Default.Equals(
            Member,
            other.Member)
        && string.Equals(
            Integration,
            other.Integration,
            StringComparison.Ordinal)
        && EqualityComparer<MetadataNamedTypeReference>.Default.Equals(
            TargetType,
            other.TargetType);

    public override int GetHashCode() =>
        HashCode.Combine(
            EqualityContract,
            Registration,
            Member,
            Integration,
            TargetType);

    static IntegrationConceptDescriptor? ResolveConcept(string? integration) =>
        integration is not null
        && IntegrationConceptCatalog.TryGetByDisplayLabel(
            integration,
            out IntegrationConceptDescriptor? concept)
                ? concept
                : null;
}

/// <summary>Typed evidence for one direct metadata assembly reference.</summary>
public sealed record InspectionGraphReferenceEvidence(
    AssemblyAcquisitionRegistration SourceRegistration,
    AssemblyReferenceIdentity Reference)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.ReferenceEvidence;
}

/// <summary>Typed evidence for one integration opportunity.</summary>
public sealed record InspectionGraphOpportunityEvidence(
    AssemblyAcquisitionRegistration SourceRegistration,
    MetadataTypeDefinitionName SourceType,
    string Integration,
    IntegrationOpportunityTarget Target)
    : IInspectionGraphOccurrenceEvidence
{
    string _integration = Integration;
    IntegrationConceptDescriptor? _concept = ResolveConcept(Integration);

    public AssemblyAcquisitionRegistration SourceRegistration { get; init; } =
        SourceRegistration;
    public MetadataTypeDefinitionName SourceType { get; init; } = SourceType;
    public string Integration
    {
        get => _integration;
        init
        {
            _integration = value;
            _concept = ResolveConcept(value);
        }
    }
    public IntegrationOpportunityTarget Target { get; init; } = Target;

    internal InspectionGraphOpportunityEvidence(
        AssemblyAcquisitionRegistration sourceRegistration,
        MetadataTypeDefinitionName sourceType,
        IntegrationConceptDescriptor concept,
        IntegrationOpportunityTarget target)
        : this(
            sourceRegistration,
            sourceType,
            concept.DisplayLabel,
            target)
    {
        _concept = concept;
    }

    public IntegrationConceptDescriptor? GetConcept() => _concept;

    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.OpportunityEvidence;

    public bool Equals(InspectionGraphOpportunityEvidence? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && EqualityComparer<AssemblyAcquisitionRegistration>.Default.Equals(
            SourceRegistration,
            other.SourceRegistration)
        && EqualityComparer<MetadataTypeDefinitionName>.Default.Equals(
            SourceType,
            other.SourceType)
        && string.Equals(
            Integration,
            other.Integration,
            StringComparison.Ordinal)
        && EqualityComparer<IntegrationOpportunityTarget>.Default.Equals(
            Target,
            other.Target);

    public override int GetHashCode() =>
        HashCode.Combine(
            EqualityContract,
            SourceRegistration,
            SourceType,
            Integration,
            Target);

    static IntegrationConceptDescriptor? ResolveConcept(string? integration) =>
        integration is not null
        && IntegrationConceptCatalog.TryGetByDisplayLabel(
            integration,
            out IntegrationConceptDescriptor? concept)
                ? concept
                : null;
}

/// <summary>Why available workspace evidence could not enter the graph.</summary>
public enum InspectionGraphIntegrationFailureKind
{
    ParticipantRejected,
    ParticipantFailed,
    StructuredEvidenceUnavailable,
    BindingMissing,
    BindingUnavailable,
    BindingAmbiguous,
    BindingRejected,
    TargetOutsideContext,
    TargetTypeMissing,
    TargetTypeAmbiguous,
    TargetTypeForwarded,
    TargetTypeRejected,
    OpportunityTargetMissing,
    OpportunityTargetAmbiguous,
}

/// <summary>One incomplete Integration graph contribution.</summary>
public sealed record InspectionGraphIntegrationFailureDetail(
    string Producer,
    AssemblyAcquisitionRegistration Registration,
    InspectionGraphIntegrationFailureKind Kind,
    CandidateOpenFailure? AcquisitionFailure = null,
    Exception? Error = null,
    AssemblyReferenceIdentity? Reference = null);

/// <summary>Typed evidence for incomplete contributions to one graph target.</summary>
public sealed record InspectionGraphIntegrationFailureEvidence :
    IInspectionGraphDiagnosticEvidence
{
    public InspectionGraphIntegrationFailureEvidence(
        IEnumerable<InspectionGraphIntegrationFailureDetail> details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Details = [.. details];
        if (Details.IsEmpty)
            throw new ArgumentException(
                "Integration graph failure evidence requires at least one detail.",
                nameof(details));
    }

    public ImmutableArray<InspectionGraphIntegrationFailureDetail> Details
        { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.FailureEvidence;
}

/// <summary>
/// Projects extension, Integration, opportunity, and in-context reference
/// evidence from one realized workspace context.
/// </summary>
public static class InspectionGraphIntegrationsQuery
{
    public static InspectionQuery<InspectionGraphDocument> Definition { get; } =
        new("Inspection graph integrations", InspectionCost.Unbounded);

    public static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context) =>
        Execute(
            context,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.WorkspaceParticipants));

    public static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphModeRequest modeRequest) =>
        Execute(
            context,
            modeRequest,
            recordExecution: null);

    internal static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphModeRequest modeRequest,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modeRequest);
        if (modeRequest.InducedSetRule
            == InspectionGraphInducedSetRule.ExplicitSubjects)
        {
            throw new InspectionQueryException(
                "Explicit-subject induced mode requires an "
                + $"{nameof(InspectionGraphInducedSetRequest)}. Use the "
                + "typed request overload.");
        }

        InspectionQueryResults results = CreateRegistry().Run(
            [
                AssemblyContextExtensionMethodsQuery.Definition,
                AssemblyContextReferencesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
            ],
            context.Group,
            recordExecution);

        return Create(
            context,
            modeRequest,
            results.Get(
                AssemblyContextExtensionMethodsQuery.Definition),
            results.Get(AssemblyContextIntegrationsQuery.Definition),
            results.Get(
                AssemblyContextIntegrationOpportunitiesQuery.Definition),
            results.Get(AssemblyContextReferencesQuery.Definition));
    }

    public static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphNeighborhoodRequest request) =>
        Execute(
            context,
            request,
            recordExecution: null);

    internal static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphNeighborhoodRequest request,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRelationships(request.Relationships);
        InspectionQueryResults results = CreateRegistry().Run(
            Plan(request),
            context.Group,
            recordExecution);
        return Create(context, request, results);
    }

    static void ValidateSubjects(
        WorkspaceContextLoadOutcome.Loaded context,
        ImmutableArray<InspectionGraphSubject> subjects)
    {
        var builder = new Builder(
            context,
            InspectionGraphPackageBoundary.Create(context));
        foreach (InspectionGraphSubject subject in subjects)
            builder.EnsureSubject(subject);
    }

    public static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphInducedSetRequest request) =>
        Execute(
            context,
            request,
            recordExecution: null);

    internal static InspectionGraphDocument Execute(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphInducedSetRequest request,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRelationships(request.Relationships);
        ValidateSubjects(context, request.Subjects);
        InspectionQueryResults results = CreateRegistry().Run(
            Plan(request),
            context.Group,
            recordExecution);
        return Create(context, request, results);
    }

    internal static ImmutableArray<InspectionQueryDefinition> Plan(
        InspectionGraphNeighborhoodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Plan(request.Relationships);
    }

    internal static ImmutableArray<InspectionQueryDefinition> Plan(
        InspectionGraphInducedSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Plan(request.Relationships);
    }

    static ImmutableArray<InspectionQueryDefinition> Plan(
        ImmutableArray<InspectionGraphRelationshipDescriptor>
            relationships)
    {
        ValidateRelationships(relationships);
        var queries =
            ImmutableArray.CreateBuilder<InspectionQueryDefinition>();
        bool opportunitiesSelected = relationships.Contains(
            InspectionGraphIntegrationsCatalog.IntegrationOpportunity);
        if (opportunitiesSelected
            || relationships.Contains(
                InspectionGraphIntegrationsCatalog.Extension))
        {
            queries.Add(
                AssemblyContextExtensionMethodsQuery.Definition);
        }
        if (relationships.Contains(
            InspectionGraphIntegrationsCatalog.MetadataReference))
        {
            queries.Add(AssemblyContextReferencesQuery.Definition);
        }
        if (opportunitiesSelected
            || relationships.Contains(
                InspectionGraphIntegrationsCatalog.IntegrationObserved))
        {
            queries.Add(AssemblyContextIntegrationsQuery.Definition);
        }
        if (opportunitiesSelected)
        {
            queries.Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition);
        }
        return queries.ToImmutable();
    }

    static InspectionQueryRegistry<AssemblyContextGroup> CreateRegistry() =>
        new InspectionQueryRegistry<AssemblyContextGroup>()
            .Add(
                AssemblyContextExtensionMethodsQuery.Definition,
                static group =>
                    AssemblyContextExtensionMethodsQuery.Execute(group))
            .Add(
                AssemblyContextReferencesQuery.Definition,
                AssemblyContextReferencesQuery.Execute)
            .Add(
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyContextIntegrationsQuery.Execute)
            .Add(
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Execute,
                AssemblyContextIntegrationsQuery.Definition);

    static void ValidateRelationships(
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships)
    {
        foreach (InspectionGraphRelationshipDescriptor relationship
            in relationships)
        {
            if (!InspectionGraphIntegrationsCatalog.Relationships.Contains(
                relationship))
            {
                throw new InspectionQueryException(
                    $"Relationship '{relationship.Id}' is not supported by "
                    + "the Integration graph.");
            }
        }
    }

    internal static InspectionGraphDocument Create(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphModeRequest modeRequest,
        AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>> extensions,
        AssemblyContextIntegrationsResult integrations,
        AssemblyContextIntegrationOpportunitiesResult opportunities,
        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>>
            references)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modeRequest);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(references);

        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create(context);
        var builder = new Builder(context, boundary);
        builder.ValidateResults(
            extensions.Assemblies,
            static entry => entry.Subject);
        builder.ValidateResults(
            integrations.Assemblies,
            static entry => entry.Subject);
        builder.ValidateResults(
            opportunities.Assemblies,
            static entry => entry.Subject);
        builder.ValidateResults(
            references.Assemblies,
            static entry => entry.Subject);

        builder.AddTypeCurrency(integrations);
        builder.AddExtensions(extensions);
        builder.AddIntegrations(integrations);
        builder.AddReferences(references);
        builder.AddOpportunities(opportunities);
        return builder.Build(modeRequest);
    }

    internal static InspectionGraphDocument Create(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphNeighborhoodRequest request,
        InspectionQueryResults results)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);
        ValidateRelationships(request.Relationships);

        InspectionGraphDocument source = CreateSelectedSource(
            context,
            request.ModeRequest,
            request.Seeds,
            request.Relationships,
            results);
        return InspectionGraphNeighborhoodProjection.Project(
            source,
            request);
    }

    internal static InspectionGraphDocument Create(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphInducedSetRequest request,
        InspectionQueryResults results)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(results);
        ValidateRelationships(request.Relationships);

        InspectionGraphDocument source = CreateSelectedSource(
            context,
            InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.WorkspaceParticipants),
            request.Subjects,
            request.Relationships,
            results);
        return InspectionGraphInducedSetProjection.Project(
            source,
            request);
    }

    static InspectionGraphDocument CreateSelectedSource(
        WorkspaceContextLoadOutcome.Loaded context,
        InspectionGraphModeRequest modeRequest,
        ImmutableArray<InspectionGraphSubject> requestedSubjects,
        ImmutableArray<InspectionGraphRelationshipDescriptor>
            relationships,
        InspectionQueryResults results)
    {
        InspectionGraphPackageBoundary boundary =
            InspectionGraphPackageBoundary.Create(context);
        var builder = new Builder(context, boundary);
        bool extensionsSelected = relationships.Contains(
            InspectionGraphIntegrationsCatalog.Extension);
        bool integrationsSelected = relationships.Contains(
            InspectionGraphIntegrationsCatalog.IntegrationObserved);
        bool referencesSelected = relationships.Contains(
            InspectionGraphIntegrationsCatalog.MetadataReference);
        bool opportunitiesSelected = relationships.Contains(
            InspectionGraphIntegrationsCatalog.IntegrationOpportunity);
        bool extensionsNeeded =
            extensionsSelected || opportunitiesSelected;
        bool integrationsNeeded =
            integrationsSelected || opportunitiesSelected;

        AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>>?
            extensions = extensionsNeeded
                ? results.Get(
                    AssemblyContextExtensionMethodsQuery.Definition)
                : null;
        AssemblyContextIntegrationsResult? integrations =
            integrationsNeeded
                ? results.Get(
                    AssemblyContextIntegrationsQuery.Definition)
                : null;
        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>>?
            references = referencesSelected
                ? results.Get(AssemblyContextReferencesQuery.Definition)
                : null;
        AssemblyContextIntegrationOpportunitiesResult? opportunities =
            opportunitiesSelected
                ? results.Get(
                    AssemblyContextIntegrationOpportunitiesQuery.Definition)
                : null;

        if (extensions is not null)
        {
            builder.ValidateResults(
                extensions.Assemblies,
                static entry => entry.Subject);
        }
        if (integrations is not null)
        {
            builder.ValidateResults(
                integrations.Assemblies,
                static entry => entry.Subject);
        }
        if (references is not null)
        {
            builder.ValidateResults(
                references.Assemblies,
                static entry => entry.Subject);
        }
        if (opportunities is not null)
        {
            builder.ValidateResults(
                opportunities.Assemblies,
                static entry => entry.Subject);
        }

        if (integrations is not null)
            builder.AddTypeCurrency(integrations);
        if (extensions is not null)
            builder.AddExtensions(extensions);
        if (integrations is not null)
            builder.AddIntegrations(integrations);
        if (references is not null)
            builder.AddReferences(references);
        if (opportunities is not null)
            builder.AddOpportunities(opportunities);
        foreach (InspectionGraphSubject subject in requestedSubjects)
            builder.EnsureSubject(subject);

        return builder.Build(modeRequest);
    }

    sealed class Builder
    {
        readonly WorkspaceContextLoadOutcome.Loaded _context;
        readonly InspectionGraphPackageBoundary _boundary;
        readonly List<InspectionGraphNode> _nodes;
        readonly ImmutableArray<InspectionGraphGroup> _groups;
        readonly Dictionary<InspectionGraphSubject, int> _nodeIds;
        readonly Dictionary<
            InspectionGraphSubject.PackageSubject,
            int> _packageGroupIds;
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyContextParticipant> _participants;
        readonly List<InspectionGraphOccurrence> _occurrences = [];
        readonly Dictionary<
            InspectionGraphRelationshipDescriptor,
            HashSet<object>> _occurrenceIdentities = [];
        readonly List<EdgeBuilder> _edges = [];
        readonly Dictionary<EdgeKey, EdgeBuilder> _edgeByKey = [];
        readonly List<FailureBuilder> _failures = [];
        readonly Dictionary<int, FailureBuilder> _failureByTarget = [];
        readonly HashSet<FailureKey> _failureKeys = [];
        readonly Dictionary<
            TypeResolutionKey,
            InspectionGraphSubject.TypeSubject> _resolvedTypes = [];
        readonly Dictionary<
            InspectionGraphSubject.MemberSubject,
            InspectionGraphSubject.TypeSubject> _extensionReceivers = [];
        readonly HashSet<OpportunityFulfillmentKey>
            _fulfilledOpportunities = [];

        internal Builder(
            WorkspaceContextLoadOutcome.Loaded context,
            InspectionGraphPackageBoundary boundary)
        {
            _context = context;
            _boundary = boundary;
            InspectionGraphDocument packageDocument = boundary.Project(
                InspectionGraphPackageBoundaryLens.PackageGroups);
            _nodes = [.. packageDocument.Nodes];
            _groups = packageDocument.Groups;
            _nodeIds = _nodes.ToDictionary(
                static node => node.Subject,
                static node => node.Id);
            _packageGroupIds = _groups.ToDictionary(
                group =>
                    (InspectionGraphSubject.PackageSubject)
                        group.Subject,
                static group => group.Id);
            _participants = new(ReferenceEqualityComparer.Instance);
            foreach (AssemblyContextParticipant participant
                in context.Group.Participants)
            {
                _participants.Add(
                    participant.Assembly.Registration,
                    participant);
            }
        }

        internal void ValidateResults<TEntry>(
            ImmutableArray<TEntry> entries,
            Func<TEntry, AssemblyContextSubject> subject)
        {
            if (entries.Length != _context.Group.Participants.Length)
            {
                throw new InspectionQueryException(
                    "An Integration graph prerequisite did not produce one result per workspace participant.");
            }

            for (int index = 0; index < entries.Length; index++)
            {
                if (!ReferenceEquals(
                        _context.Group.Participants[index]
                            .Assembly.Registration,
                        subject(entries[index]).Registration))
                {
                    throw new InspectionQueryException(
                        "An Integration graph prerequisite result does not match workspace participant order.");
                }
            }
        }

        internal void EnsureSubject(InspectionGraphSubject subject)
        {
            if (subject is InspectionGraphSubject.PackageSubject package)
            {
                if (!_packageGroupIds.ContainsKey(package))
                    throw SubjectNotPresent(subject);
                return;
            }

            if (!TryGetRegistration(subject, out var registration)
                || !_participants.TryGetValue(
                    registration,
                    out var participant))
            {
                throw SubjectNotPresent(subject);
            }

            switch (subject)
            {
                case InspectionGraphSubject.AssemblySubject:
                    if (!_boundary.TryGetAssemblySubject(
                            registration,
                            out var exactAssembly)
                        || exactAssembly != subject)
                    {
                        throw SubjectNotPresent(subject);
                    }
                    break;
                case InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.AcquiredDefinition type
                }:
                    EnsureDeclared(
                        participant,
                        subject,
                        session =>
                            DeclarationValidation.From(
                                session.ProbeDeclaration(type.Type)));
                    break;
                case InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.AcquiredApi member
                }:
                    EnsureDeclared(
                        participant,
                        subject,
                        session =>
                            ValidateMemberDeclaration(
                                session,
                                member));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown explicit-subject kind.");
            }

            AddNode(subject, registration);
        }

        void EnsureDeclared(
            AssemblyContextParticipant participant,
            InspectionGraphSubject subject,
            Func<
                AssemblyInspectionSession,
                DeclarationValidation> predicate)
        {
            AssemblyImageAccessResult<DeclarationValidation> access;
            try
            {
                access = _context.Group.UseAssemblySession(
                    participant.Assembly,
                    predicate);
            }
            catch (Exception ex) when (
                AssemblyContextQueryExecutor.IsArtifactFailure(ex))
            {
                throw new InspectionQueryException(
                    $"Explicit induced-set subject '{subject}' could not "
                    + "be validated because decoding its workspace "
                    + "participant image failed "
                    + $"({ex.GetType().Name}: {ex.Message}).",
                    ex);
            }

            if (access
                is AssemblyImageAccessResult<
                    DeclarationValidation>.Rejected rejected)
            {
                throw new InspectionQueryException(
                    $"Explicit induced-set subject '{subject}' could not "
                    + "be validated because its workspace participant "
                    + $"image is unavailable ({rejected.Failure.Kind}). "
                    + "Update the request to use a subject declared by "
                    + "this workspace.");
            }

            DeclarationValidation validation =
                ((AssemblyImageAccessResult<
                    DeclarationValidation>.Available)access).Value;
            if (validation
                is DeclarationValidation.Rejected declarationRejected)
            {
                MetadataTypeNameFailure failure =
                    declarationRejected.Failure;
                throw new InspectionQueryException(
                    $"Explicit induced-set subject '{subject}' could not "
                    + "be validated because its metadata declaration "
                    + $"was rejected ({failure.Mechanism}/"
                    + $"{failure.Kind}: {failure.Detail}).");
            }

            if (validation is not DeclarationValidation.Declared)
                throw SubjectNotPresent(subject);
        }

        static DeclarationValidation ValidateMemberDeclaration(
            AssemblyInspectionSession session,
            InspectionGraphMemberIdentity.AcquiredApi member)
        {
            DeclarationValidation declaration = DeclarationValidation.From(
                session.ProbeDeclaration(member.DeclaringType));
            if (declaration is not DeclarationValidation.Declared)
                return declaration;
            return session.DeclaresExtensionMember(
                member.DeclaringType,
                member.Member)
                ? new DeclarationValidation.Declared()
                : new DeclarationValidation.Absent();
        }

        abstract record DeclarationValidation
        {
            private protected DeclarationValidation()
            {
            }

            internal static DeclarationValidation From(
                TypeDeclarationResult declaration) =>
                declaration switch
                {
                    TypeDeclarationResult.Defined =>
                        new Declared(),
                    TypeDeclarationResult.Rejected rejected =>
                        new Rejected(rejected.Rejection),
                    _ => new Absent(),
                };

            internal sealed record Declared :
                DeclarationValidation;
            internal sealed record Absent :
                DeclarationValidation;
            internal sealed record Rejected(
                MetadataTypeNameFailure Failure) :
                DeclarationValidation;
        }

        internal void AddTypeCurrency(
            AssemblyContextIntegrationsResult result)
        {
            foreach (AssemblyIntegrationsEntry entry in result.Assemblies)
            {
                if (entry is not AssemblyIntegrationsEntry.Available available)
                    continue;
                foreach (EcosystemIntegrationSignalInfo signal
                    in available.EcosystemSignals)
                {
                    if (signal.GetTypeDefinition() is { } type)
                    {
                        AddNode(
                            TypeSubject(
                                available.Subject.Registration,
                                type),
                            available.Subject.Registration);
                    }
                    else if (signal.Shape == IntegrationSignalShape.Type)
                    {
                        AddFailure(
                            "integrations",
                            available.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .StructuredEvidenceUnavailable);
                    }
                }
            }
        }

        internal void AddExtensions(
            AssemblyContextResult<
                ImmutableArray<ExtensionMethodInfo>> result)
        {
            foreach (AssemblyContextEntry<
                ImmutableArray<ExtensionMethodInfo>> entry
                in result.Assemblies)
            {
                switch (entry)
                {
                    case AssemblyContextEntry<
                        ImmutableArray<ExtensionMethodInfo>>.Available
                        available:
                        foreach (ExtensionMethodInfo extension
                            in available.Value)
                        {
                            if (extension.Anchor is not { } member
                                || extension.GetDeclaringTypeDefinition()
                                    is not { } declaringType
                                || extension.GetExtendedTypeReference()
                                    is not { } extendedType)
                            {
                                AddFailure(
                                    "extensions",
                                    available.Subject.Registration,
                                    InspectionGraphIntegrationFailureKind
                                        .StructuredEvidenceUnavailable);
                                continue;
                            }

                            InspectionGraphSubject.MemberSubject source =
                                MemberSubject(
                                    available.Subject.Registration,
                                    declaringType,
                                    member);
                            AddNode(
                                source,
                                available.Subject.Registration);
                            if (!TryResolveType(
                                    available.Subject.Registration,
                                    extendedType,
                                    "extensions",
                                    source,
                                    out InspectionGraphSubject.TypeSubject?
                                        target))
                            {
                                continue;
                            }

                            AddNode(
                                target,
                                Registration(target));
                            _extensionReceivers[source] = target;
                            AddOccurrence(
                                source,
                                target,
                                source,
                                target,
                                InspectionGraphIntegrationsCatalog.Extension,
                                new InspectionGraphExtensionEvidence(
                                    available.Subject.Registration,
                                    member,
                                    extendedType));
                        }
                        break;
                    case AssemblyContextEntry<
                        ImmutableArray<ExtensionMethodInfo>>.Rejected rejected:
                        AddFailure(
                            "extensions",
                            rejected.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantRejected,
                            rejected.Failure);
                        break;
                    case AssemblyContextEntry<
                        ImmutableArray<ExtensionMethodInfo>>.Failed failed:
                        AddFailure(
                            "extensions",
                            failed.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantFailed,
                            error: failed.Error);
                        break;
                }
            }
        }

        internal void AddIntegrations(
            AssemblyContextIntegrationsResult result)
        {
            foreach (AssemblyIntegrationsEntry entry in result.Assemblies)
            {
                switch (entry)
                {
                    case AssemblyIntegrationsEntry.Available available:
                        foreach (EcosystemIntegrationSignalInfo signal
                            in available.EcosystemSignals)
                        {
                            if (signal.Shape
                                != IntegrationSignalShape.Api)
                            {
                                continue;
                            }
                            if (signal.IsApiEvidenceIncomplete())
                            {
                                AddFailure(
                                    "integrations",
                                    available.Subject.Registration,
                                    InspectionGraphIntegrationFailureKind
                                        .StructuredEvidenceUnavailable);
                            }
                            foreach (EcosystemIntegrationApiEvidence api
                                in signal.GetApiEvidenceSet())
                            {
                                AddIntegration(
                                    available.Subject.Registration,
                                    signal,
                                    api);
                            }
                        }
                        break;
                    case AssemblyIntegrationsEntry.Rejected rejected:
                        AddFailure(
                            "integrations",
                            rejected.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantRejected,
                            rejected.Failure);
                        break;
                    case AssemblyIntegrationsEntry.Failed failed:
                        AddFailure(
                            "integrations",
                            failed.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantFailed,
                            error: failed.Error);
                        break;
                }
            }

            void AddIntegration(
                AssemblyAcquisitionRegistration registration,
                EcosystemIntegrationSignalInfo signal,
                EcosystemIntegrationApiEvidence api)
            {
                IntegrationConceptDescriptor concept =
                    signal.GetConcept()
                    ?? throw new InspectionQueryException(
                        $"Integration signal '{signal.Integration}' is not configured.");
                if (api.ReturnType is not { } targetType)
                {
                    AddFailure(
                        "integrations",
                        registration,
                        InspectionGraphIntegrationFailureKind
                            .StructuredEvidenceUnavailable);
                    return;
                }

                InspectionGraphSubject.MemberSubject source =
                    MemberSubject(
                        registration,
                        api.DeclaringType,
                        api.Member);
                AddNode(source, registration);
                if (!TryResolveType(
                        registration,
                        targetType,
                        "integrations",
                        source,
                        out InspectionGraphSubject.TypeSubject? target))
                {
                    return;
                }

                AddNode(target, Registration(target));
                if (_extensionReceivers.TryGetValue(
                        source,
                        out InspectionGraphSubject.TypeSubject? receiver))
                {
                    _fulfilledOpportunities.Add(
                        new OpportunityFulfillmentKey(
                            receiver,
                            concept,
                            target));
                }
                AddOccurrence(
                    source,
                    target,
                    source,
                    target,
                    InspectionGraphIntegrationsCatalog.IntegrationObserved,
                    new InspectionGraphIntegrationEvidence(
                        registration,
                        api.Member,
                        concept,
                        targetType));
            }
        }

        internal void AddReferences(
            AssemblyContextResult<
                ImmutableArray<AssemblyReferenceIdentity>> result)
        {
            foreach (AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>> entry
                in result.Assemblies)
            {
                switch (entry)
                {
                    case AssemblyContextEntry<
                        ImmutableArray<AssemblyReferenceIdentity>>.Available
                        available:
                        InspectionGraphSubject.AssemblySubject source =
                            AssemblySubject(
                                available.Subject.Registration);
                        foreach (AssemblyReferenceIdentity reference
                            in available.Value)
                        {
                            if (!TryBindInContext(
                                    available.Subject.Registration,
                                    AssemblyBindingTarget.Reference(reference),
                                    out AssemblyContextParticipant? target,
                                    out InspectionGraphIntegrationFailureKind
                                        bindingFailure))
                            {
                                if (bindingFailure
                                    != InspectionGraphIntegrationFailureKind
                                        .BindingMissing)
                                {
                                    AddFailure(
                                        "references",
                                        available.Subject.Registration,
                                        bindingFailure,
                                        reference: reference);
                                }
                                continue;
                            }

                            InspectionGraphSubject.AssemblySubject
                                targetSubject = AssemblySubject(
                                    target.Assembly.Registration);
                            AddOccurrence(
                                source,
                                targetSubject,
                                source,
                                targetSubject,
                                InspectionGraphIntegrationsCatalog
                                    .MetadataReference,
                                new InspectionGraphReferenceEvidence(
                                    available.Subject.Registration,
                                    reference));
                        }
                        break;
                    case AssemblyContextEntry<
                        ImmutableArray<AssemblyReferenceIdentity>>.Rejected
                        rejected:
                        AddFailure(
                            "references",
                            rejected.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantRejected,
                            rejected.Failure);
                        break;
                    case AssemblyContextEntry<
                        ImmutableArray<AssemblyReferenceIdentity>>.Failed
                        failed:
                        AddFailure(
                            "references",
                            failed.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantFailed,
                            error: failed.Error);
                        break;
                }
            }
        }

        internal void AddOpportunities(
            AssemblyContextIntegrationOpportunitiesResult result)
        {
            foreach (AssemblyIntegrationOpportunitiesEntry entry
                in result.Assemblies)
            {
                switch (entry)
                {
                    case AssemblyIntegrationOpportunitiesEntry.Available
                        available:
                        foreach (IntegrationOpportunityInfo opportunity
                            in available.Opportunities)
                        {
                            IntegrationConceptDescriptor concept =
                                opportunity.GetConcept()
                                ?? throw new InspectionQueryException(
                                    $"Integration opportunity '{opportunity.Integration}' is not configured.");
                            if (opportunity.GetTarget() is not { } targetSpec)
                                continue;
                            if (opportunity.GetSourceTypeDefinition()
                                    is not { } sourceType)
                            {
                                AddFailure(
                                    "opportunities",
                                    available.Subject.Registration,
                                    InspectionGraphIntegrationFailureKind
                                        .StructuredEvidenceUnavailable);
                                continue;
                            }

                            InspectionGraphSubject.TypeSubject
                                occurrenceSource = TypeSubject(
                                    available.Subject.Registration,
                                    sourceType);
                            AddNode(
                                occurrenceSource,
                                available.Subject.Registration);
                            if (!TryResolveOpportunityTarget(
                                    available.Subject.Registration,
                                    targetSpec,
                                    occurrenceSource,
                                    out InspectionGraphSubject.TypeSubject?
                                        target))
                            {
                                continue;
                            }
                            if (_fulfilledOpportunities.Contains(
                                    new OpportunityFulfillmentKey(
                                        occurrenceSource,
                                        concept,
                                        target)))
                            {
                                continue;
                            }

                            AddNode(
                                target,
                                Registration(target));
                            AddOccurrence(
                                AssemblySubject(
                                    available.Subject.Registration),
                                target,
                                occurrenceSource,
                                target,
                                InspectionGraphIntegrationsCatalog
                                    .IntegrationOpportunity,
                                new InspectionGraphOpportunityEvidence(
                                    available.Subject.Registration,
                                    sourceType,
                                    concept,
                                    targetSpec));
                        }
                        break;
                    case AssemblyIntegrationOpportunitiesEntry.Rejected
                        rejected:
                        AddFailure(
                            "opportunities",
                            rejected.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantRejected,
                            rejected.Failure);
                        break;
                    case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                        AddFailure(
                            "opportunities",
                            failed.Subject.Registration,
                            InspectionGraphIntegrationFailureKind
                                .ParticipantFailed,
                            error: failed.Error);
                        break;
                }
            }
        }

        internal InspectionGraphDocument Build(
            InspectionGraphModeRequest modeRequest)
        {
            InspectionGraphEdge[] edges =
            [
                .. _edges.Select((edge, id) =>
                    new InspectionGraphEdge(
                        id,
                        edge.FromNodeId,
                        edge.ToNodeId,
                        edge.Relationship,
                        edge.OccurrenceIds)),
            ];
            InspectionGraphFailure[] failures =
            [
                .. _failures.Select(failure =>
                    new InspectionGraphFailure(
                        InspectionGraphIntegrationsCatalog
                            .ProjectionFailure,
                        InspectionGraphTarget.Node(
                            failure.TargetId),
                        new InspectionGraphIntegrationFailureEvidence(
                            failure.Details))),
            ];
            ImmutableArray<InspectionGraphSeed> seeds =
                InspectionGraphSeedBinder.Bind(
                    modeRequest,
                    _nodes,
                    _groups,
                    InspectionGraphSeedTargetPreference.Group);
            return new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                modeRequest,
                _nodes,
                _groups,
                edges,
                _occurrences,
                [],
                seeds,
                [],
                failures);
        }

        void AddOccurrence(
            InspectionGraphSubject edgeSource,
            InspectionGraphSubject edgeTarget,
            InspectionGraphSubject occurrenceSource,
            InspectionGraphSubject occurrenceTarget,
            InspectionGraphRelationshipDescriptor relationship,
            IInspectionGraphOccurrenceEvidence evidence)
        {
            var occurrence = new InspectionGraphOccurrence(
                _occurrences.Count,
                relationship,
                occurrenceSource,
                occurrenceTarget,
                evidence,
                []);
            object identity =
                relationship.OccurrenceIdentity.Project(occurrence)
                ?? throw new InspectionQueryException(
                    "An Integration occurrence identity cannot be null.");
            if (!_occurrenceIdentities.TryGetValue(
                    relationship,
                    out HashSet<object>? identities))
            {
                identities = [];
                _occurrenceIdentities.Add(
                    relationship,
                    identities);
            }
            if (!identities.Add(identity))
                return;

            int sourceNodeId = AddNode(
                edgeSource,
                Registration(edgeSource));
            int targetNodeId = AddNode(
                edgeTarget,
                Registration(edgeTarget));
            int occurrenceId = occurrence.Id;
            _occurrences.Add(occurrence);
            var key = new EdgeKey(
                sourceNodeId,
                targetNodeId,
                relationship);
            if (!_edgeByKey.TryGetValue(
                    key,
                    out EdgeBuilder? edge))
            {
                edge = new EdgeBuilder(
                    sourceNodeId,
                    targetNodeId,
                    relationship);
                _edgeByKey.Add(key, edge);
                _edges.Add(edge);
            }
            edge.OccurrenceIds.Add(occurrenceId);
        }

        bool TryResolveOpportunityTarget(
            AssemblyAcquisitionRegistration sourceRegistration,
            IntegrationOpportunityTarget target,
            InspectionGraphSubject source,
            [NotNullWhen(true)]
            out InspectionGraphSubject.TypeSubject? subject)
        {
            AssemblyContextParticipant[] candidates =
            [
                .. _context.Group.Participants.Where(participant =>
                    string.Equals(
                        participant.Assembly.Identity.Name,
                        target.AssemblyName,
                        StringComparison.OrdinalIgnoreCase)),
            ];
            if (candidates.Length == 0)
            {
                AddFailure(
                    "opportunities",
                    sourceRegistration,
                    InspectionGraphIntegrationFailureKind
                        .OpportunityTargetMissing,
                    target: source);
                subject = null;
                return false;
            }
            if (candidates.Length > 1)
            {
                AddFailure(
                    "opportunities",
                    sourceRegistration,
                    InspectionGraphIntegrationFailureKind
                        .OpportunityTargetAmbiguous,
                    target: source);
                subject = null;
                return false;
            }

            return TryValidateDefinition(
                sourceRegistration,
                candidates[0],
                target.Type,
                "opportunities",
                source,
                out subject);
        }

        bool TryResolveType(
            AssemblyAcquisitionRegistration sourceRegistration,
            MetadataNamedTypeReference reference,
            string producer,
            InspectionGraphSubject source,
            [NotNullWhen(true)]
            out InspectionGraphSubject.TypeSubject? subject)
        {
            var key = new TypeResolutionKey(
                sourceRegistration,
                reference);
            if (_resolvedTypes.TryGetValue(key, out subject))
                return true;

            if (!_participants.TryGetValue(
                    sourceRegistration,
                    out AssemblyContextParticipant? sourceParticipant))
            {
                throw new InspectionQueryException(
                    "Integration evidence names a participant outside the workspace context.");
            }

            AssemblyContextParticipant? targetParticipant;
            switch (reference.Scope)
            {
                case MetadataTypeReferenceScope.CurrentAssembly:
                    targetParticipant = sourceParticipant;
                    break;
                case MetadataTypeReferenceScope.AssemblyReference assembly:
                    if (!TryBindInContext(
                            sourceRegistration,
                            AssemblyBindingTarget.Reference(
                                assembly.Assembly),
                            out targetParticipant,
                            out InspectionGraphIntegrationFailureKind
                                bindingFailure))
                    {
                        AddFailure(
                            producer,
                            sourceRegistration,
                            bindingFailure,
                            reference: assembly.Assembly,
                            target: source);
                        subject = null;
                        return false;
                    }
                    break;
                case MetadataTypeReferenceScope.IntrinsicCoreLibrary:
                    if (!TryBindInContext(
                            sourceRegistration,
                            AssemblyBindingTarget.CoreLibrary(),
                            out targetParticipant,
                            out InspectionGraphIntegrationFailureKind
                                coreBindingFailure))
                    {
                        AddFailure(
                            producer,
                            sourceRegistration,
                            coreBindingFailure,
                            target: source);
                        subject = null;
                        return false;
                    }
                    break;
                case MetadataTypeReferenceScope.ModuleReference:
                    AddFailure(
                        producer,
                        sourceRegistration,
                        InspectionGraphIntegrationFailureKind
                            .BindingUnavailable,
                        target: source);
                    subject = null;
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown metadata type-reference scope.");
            }

            if (!TryValidateDefinition(
                    sourceRegistration,
                    targetParticipant,
                    reference.Type,
                    producer,
                    source,
                    out subject))
            {
                return false;
            }

            _resolvedTypes.Add(key, subject);
            return true;
        }

        bool TryValidateDefinition(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyContextParticipant targetParticipant,
            MetadataTypeDefinitionName type,
            string producer,
            InspectionGraphSubject source,
            [NotNullWhen(true)]
            out InspectionGraphSubject.TypeSubject? subject)
        {
            AssemblyImageAccessResult<TypeDeclarationResult> access =
                _context.Group.UseAssemblySession(
                    targetParticipant.Assembly,
                    session => session.ProbeDeclaration(type));
            if (access
                is AssemblyImageAccessResult<TypeDeclarationResult>.Rejected
                    rejected)
            {
                AddFailure(
                    producer,
                    sourceRegistration,
                    InspectionGraphIntegrationFailureKind
                        .ParticipantRejected,
                    rejected.Failure,
                    target: source);
                subject = null;
                return false;
            }

            TypeDeclarationResult declaration =
                ((AssemblyImageAccessResult<TypeDeclarationResult>.Available)
                    access).Value;
            InspectionGraphIntegrationFailureKind? failure =
                declaration switch
                {
                    TypeDeclarationResult.Defined => null,
                    TypeDeclarationResult.Missing =>
                        InspectionGraphIntegrationFailureKind
                            .TargetTypeMissing,
                    TypeDeclarationResult.Ambiguous =>
                        InspectionGraphIntegrationFailureKind
                            .TargetTypeAmbiguous,
                    TypeDeclarationResult.Forwarded
                        or TypeDeclarationResult.ExportedFromModule =>
                        InspectionGraphIntegrationFailureKind
                            .TargetTypeForwarded,
                    TypeDeclarationResult.Rejected =>
                        InspectionGraphIntegrationFailureKind
                            .TargetTypeRejected,
                    _ => throw new InvalidOperationException(
                        "Unknown type declaration result."),
                };
            if (failure is { } kind)
            {
                AddFailure(
                    producer,
                    sourceRegistration,
                    kind,
                    target: source);
                subject = null;
                return false;
            }

            subject = TypeSubject(
                targetParticipant.Assembly.Registration,
                type);
            return true;
        }

        bool TryBindInContext(
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyBindingTarget target,
            [NotNullWhen(true)]
            out AssemblyContextParticipant? participant,
            out InspectionGraphIntegrationFailureKind failure)
        {
            AssemblyContextParticipant source =
                _participants[sourceRegistration];
            var request = new AssemblyBindingRequest(
                target,
                AssemblyBindingOrigin.FromAssembly(
                    source.Assembly),
                AssemblyResolutionScope.Any);
            AssemblyBindingSelection selection =
                AssemblyBindingSelection.ValidateForRequest(
                    request,
                    source.BindingPolicy.Select(request));
            if (selection
                is AssemblyBindingSelection.Selected selected)
            {
                if (_participants.TryGetValue(
                        selected.Assembly.Registration,
                        out participant))
                {
                    failure = default;
                    return true;
                }

                participant = null;
                failure = InspectionGraphIntegrationFailureKind
                    .TargetOutsideContext;
                return false;
            }

            participant = null;
            failure = selection switch
            {
                AssemblyBindingSelection.Missing =>
                    InspectionGraphIntegrationFailureKind
                        .BindingMissing,
                AssemblyBindingSelection.Unavailable =>
                    InspectionGraphIntegrationFailureKind
                        .BindingUnavailable,
                AssemblyBindingSelection.Ambiguous =>
                    InspectionGraphIntegrationFailureKind
                        .BindingAmbiguous,
                AssemblyBindingSelection.Rejected =>
                    InspectionGraphIntegrationFailureKind
                        .BindingRejected,
                _ => InspectionGraphIntegrationFailureKind
                    .BindingUnavailable,
            };
            return false;
        }

        int AddNode(
            InspectionGraphSubject subject,
            AssemblyAcquisitionRegistration registration)
        {
            if (_nodeIds.TryGetValue(subject, out int id))
                return id;

            int[] groupIds =
                _boundary.TryGetPackageSubject(
                    registration,
                    out InspectionGraphSubject.PackageSubject? package)
                && _packageGroupIds.TryGetValue(
                    package,
                    out int groupId)
                    ? [groupId]
                    : [];
            id = _nodes.Count;
            _nodes.Add(
                new InspectionGraphNode(
                    id,
                    subject,
                    InspectionGraphNodeRole.Ordinary,
                    groupIds));
            _nodeIds.Add(subject, id);
            return id;
        }

        void AddFailure(
            string producer,
            AssemblyAcquisitionRegistration registration,
            InspectionGraphIntegrationFailureKind kind,
            CandidateOpenFailure? acquisitionFailure = null,
            Exception? error = null,
            AssemblyReferenceIdentity? reference = null,
            InspectionGraphSubject? target = null)
        {
            int targetId = AddNode(
                target ?? AssemblySubject(registration),
                Registration(target ?? AssemblySubject(registration)));
            var key = new FailureKey(
                producer,
                registration,
                kind,
                targetId,
                reference);
            if (!_failureKeys.Add(key))
                return;

            if (!_failureByTarget.TryGetValue(
                    targetId,
                    out FailureBuilder? failure))
            {
                failure = new FailureBuilder(targetId);
                _failureByTarget.Add(targetId, failure);
                _failures.Add(failure);
            }
            failure.Details.Add(
                new InspectionGraphIntegrationFailureDetail(
                    producer,
                    registration,
                    kind,
                    acquisitionFailure,
                    error,
                    reference));
        }

        InspectionGraphSubject.AssemblySubject AssemblySubject(
            AssemblyAcquisitionRegistration registration)
        {
            if (_boundary.TryGetAssemblySubject(
                    registration,
                    out InspectionGraphSubject.AssemblySubject? subject))
            {
                return subject;
            }

            throw new InspectionQueryException(
                "Integration evidence names an assembly outside the workspace package boundary.");
        }

        static InspectionGraphSubject.MemberSubject MemberSubject(
            AssemblyAcquisitionRegistration registration,
            MetadataTypeDefinitionName declaringType,
            MemberAnchor member) =>
            (InspectionGraphSubject.MemberSubject)
                InspectionGraphSubject.ForAcquiredApiMember(
                    registration,
                    declaringType,
                    member);

        static InspectionGraphSubject.TypeSubject TypeSubject(
            AssemblyAcquisitionRegistration registration,
            MetadataTypeDefinitionName type) =>
            (InspectionGraphSubject.TypeSubject)
                InspectionGraphSubject.ForAcquiredType(
                    registration,
                    type);

        static AssemblyAcquisitionRegistration Registration(
            InspectionGraphSubject subject) =>
            subject switch
            {
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.AcquiredApi acquired,
                } => acquired.Registration,
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.AcquiredDefinition
                        acquired,
                } => acquired.Registration,
                InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.Acquired acquired,
                } => acquired.Registration,
                _ => throw new ArgumentException(
                    "The Integration graph requires acquisition-bound subjects.",
                    nameof(subject)),
            };

        static bool TryGetRegistration(
            InspectionGraphSubject subject,
            out AssemblyAcquisitionRegistration registration)
        {
            registration = subject switch
            {
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.AcquiredApi acquired,
                } => acquired.Registration,
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.AcquiredDefinition
                        acquired,
                } => acquired.Registration,
                InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.Acquired acquired,
                } => acquired.Registration,
                _ => null!,
            };
            return registration is not null;
        }

        static InspectionQueryException SubjectNotPresent(
            InspectionGraphSubject subject) =>
            new(
                $"The requested {subject.Kind.ToString().ToLowerInvariant()} "
                + "subject is not present in this graph. Add the subject "
                + "to workspace scope or select a relationship and lens "
                + "that admit it.");

        sealed record EdgeKey(
            int FromNodeId,
            int ToNodeId,
            InspectionGraphRelationshipDescriptor Relationship);

        sealed class EdgeBuilder(
            int fromNodeId,
            int toNodeId,
            InspectionGraphRelationshipDescriptor relationship)
        {
            internal int FromNodeId { get; } = fromNodeId;
            internal int ToNodeId { get; } = toNodeId;
            internal InspectionGraphRelationshipDescriptor Relationship
                { get; } = relationship;
            internal List<int> OccurrenceIds { get; } = [];
        }

        sealed class FailureBuilder(int targetId)
        {
            internal int TargetId { get; } = targetId;
            internal List<InspectionGraphIntegrationFailureDetail> Details
                { get; } = [];
        }

        readonly record struct FailureKey(
            string Producer,
            AssemblyAcquisitionRegistration Registration,
            InspectionGraphIntegrationFailureKind Kind,
            int TargetId,
            AssemblyReferenceIdentity? Reference);

        readonly record struct TypeResolutionKey(
            AssemblyAcquisitionRegistration Source,
            MetadataNamedTypeReference Reference);

        readonly record struct OpportunityFulfillmentKey(
            InspectionGraphSubject.TypeSubject Source,
            IntegrationConceptDescriptor Concept,
            InspectionGraphSubject.TypeSubject Target);
    }
}
