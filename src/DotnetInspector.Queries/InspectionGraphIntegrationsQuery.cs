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
            OpportunityEndpointProjection,
            OccurrenceIdentity,
            [OpportunityEvidence]);

    public static InspectionGraphFailureDescriptor ProjectionFailure { get; } =
        new(
            "queries.integration-graph-incomplete",
            InspectionGraphOwner.Queries,
            [FailureEvidence]);

    sealed class IntegrationOccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence switch
            {
                InspectionGraphExtensionEvidence extension =>
                    (
                        extension.Registration,
                        extension.Member,
                        extension.ExtendedType),
                InspectionGraphIntegrationEvidence integration =>
                    (
                        integration.Registration,
                        integration.Member,
                        integration.Integration,
                        integration.TargetType),
                InspectionGraphReferenceEvidence reference =>
                    (
                        reference.SourceRegistration,
                        reference.Reference),
                InspectionGraphOpportunityEvidence opportunity =>
                    (
                        opportunity.SourceRegistration,
                        opportunity.SourceType,
                        opportunity.Integration,
                        opportunity.Target),
                _ => throw new ArgumentException(
                    "Unsupported Integration graph occurrence evidence.",
                    nameof(occurrence)),
            };
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
    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.IntegrationEvidence;
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
    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.OpportunityEvidence;
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
    Exception? Error = null);

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
        WorkspaceContextLoadOutcome.Loaded context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registry =
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
        InspectionQueryResults results = registry.Run(
            [
                AssemblyContextExtensionMethodsQuery.Definition,
                AssemblyContextReferencesQuery.Definition,
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
            ],
            context.Group);

        return Create(
            context,
            results.Get(
                AssemblyContextExtensionMethodsQuery.Definition),
            results.Get(AssemblyContextIntegrationsQuery.Definition),
            results.Get(
                AssemblyContextIntegrationOpportunitiesQuery.Definition),
            results.Get(AssemblyContextReferencesQuery.Definition));
    }

    internal static InspectionGraphDocument Create(
        WorkspaceContextLoadOutcome.Loaded context,
        AssemblyContextResult<ImmutableArray<ExtensionMethodInfo>> extensions,
        AssemblyContextIntegrationsResult integrations,
        AssemblyContextIntegrationOpportunitiesResult opportunities,
        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>>
            references)
    {
        ArgumentNullException.ThrowIfNull(context);
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
        return builder.Build();
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
                            if (signal.GetApiEvidence() is not { } api)
                            {
                                if (signal.Shape
                                    == IntegrationSignalShape.Api)
                                {
                                    AddFailure(
                                        "integrations",
                                        available.Subject.Registration,
                                        InspectionGraphIntegrationFailureKind
                                            .StructuredEvidenceUnavailable);
                                }
                                continue;
                            }
                            if (api.ReturnType is not { } targetType)
                            {
                                AddFailure(
                                    "integrations",
                                    available.Subject.Registration,
                                    InspectionGraphIntegrationFailureKind
                                        .StructuredEvidenceUnavailable);
                                continue;
                            }

                            InspectionGraphSubject.MemberSubject source =
                                MemberSubject(
                                    available.Subject.Registration,
                                    api.Member);
                            AddNode(
                                source,
                                available.Subject.Registration);
                            if (!TryResolveType(
                                    available.Subject.Registration,
                                    targetType,
                                    "integrations",
                                    source,
                                    out InspectionGraphSubject.TypeSubject?
                                        target))
                            {
                                continue;
                            }

                            AddNode(
                                target,
                                Registration(target));
                            if (_extensionReceivers.TryGetValue(
                                    source,
                                    out InspectionGraphSubject.TypeSubject?
                                        receiver))
                            {
                                _fulfilledOpportunities.Add(
                                    new OpportunityFulfillmentKey(
                                        receiver,
                                        signal.Integration,
                                        target));
                            }
                            AddOccurrence(
                                source,
                                target,
                                source,
                                target,
                                InspectionGraphIntegrationsCatalog
                                    .IntegrationObserved,
                                new InspectionGraphIntegrationEvidence(
                                    available.Subject.Registration,
                                    api.Member,
                                    signal.Integration,
                                    targetType));
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
                            in available.Value.Distinct(
                                AssemblyReferenceIdentity
                                    .EquivalentComparer))
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
                                        bindingFailure);
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
                                        opportunity.Integration,
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
                                    opportunity.Integration,
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

        internal InspectionGraphDocument Build()
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
            return new InspectionGraphDocument(
                InspectionGraphDocumentScope.SessionBound,
                _nodes,
                _groups,
                edges,
                _occurrences,
                [],
                [],
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
            AssemblyBindingSelection selection =
                source.BindingPolicy.Select(
                    new AssemblyBindingRequest(
                        target,
                        AssemblyBindingOrigin.FromAssembly(
                            source.Assembly),
                        AssemblyResolutionScope.Any));
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
            InspectionGraphSubject? target = null)
        {
            int targetId = AddNode(
                target ?? AssemblySubject(registration),
                Registration(target ?? AssemblySubject(registration)));
            var key = new FailureKey(
                producer,
                registration,
                kind,
                targetId);
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
                    error));
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
            MemberAnchor member) =>
            (InspectionGraphSubject.MemberSubject)
                InspectionGraphSubject.ForAcquiredApiMember(
                    registration,
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
            int TargetId);

        readonly record struct TypeResolutionKey(
            AssemblyAcquisitionRegistration Source,
            MetadataNamedTypeReference Reference);

        readonly record struct OpportunityFulfillmentKey(
            InspectionGraphSubject.TypeSubject Source,
            string Integration,
            InspectionGraphSubject.TypeSubject Target);
    }
}
