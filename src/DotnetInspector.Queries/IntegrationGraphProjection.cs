using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Whether one admitted Census occurrence survived the requested graph
/// induction.
/// </summary>
public abstract record IntegrationGraphOccurrenceProjection
{
    private protected IntegrationGraphOccurrenceProjection()
    {
    }

    public sealed record FilteredByRequest :
        IntegrationGraphOccurrenceProjection;

    public sealed record Retained :
        IntegrationGraphOccurrenceProjection
    {
        public Retained(int occurrenceId, int edgeId)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(occurrenceId);
            ArgumentOutOfRangeException.ThrowIfNegative(edgeId);
            OccurrenceId = occurrenceId;
            EdgeId = edgeId;
        }

        public int OccurrenceId { get; }
        public int EdgeId { get; }
    }
}

/// <summary>
/// The pre-induction graph occurrence admitted by one <c>In</c> candidate.
/// </summary>
public sealed class IntegrationGraphCandidateOccurrence
{
    internal IntegrationGraphCandidateOccurrence(
        InspectionGraphSubject edgeSource,
        InspectionGraphSubject edgeTarget,
        InspectionGraphSubject occurrenceSource,
        InspectionGraphSubject occurrenceTarget,
        IntegrationGraphOccurrenceProjection projection)
    {
        EdgeSource = edgeSource;
        EdgeTarget = edgeTarget;
        OccurrenceSource = occurrenceSource;
        OccurrenceTarget = occurrenceTarget;
        Projection = projection;
    }

    public InspectionGraphSubject EdgeSource { get; }
    public InspectionGraphSubject EdgeTarget { get; }
    public InspectionGraphSubject OccurrenceSource { get; }
    public InspectionGraphSubject OccurrenceTarget { get; }
    public IntegrationGraphOccurrenceProjection Projection { get; }
}

/// <summary>
/// One classified candidate retained before explicit induced-set filtering.
/// </summary>
public sealed class IntegrationGraphCandidateProjection
{
    internal IntegrationGraphCandidateProjection(
        IntegrationCandidateAttempt.Classified attempt,
        IntegrationGraphCandidateOccurrence? occurrence)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if ((attempt.Disposition is IntegrationCandidateDisposition.In)
            != (occurrence is not null))
        {
            throw new ArgumentException(
                "Exactly the In candidate attempts require an admitted graph occurrence.",
                nameof(occurrence));
        }

        Attempt = attempt;
        Occurrence = occurrence;
    }

    public IntegrationCandidateAttempt.Classified Attempt { get; }
    public IntegrationCandidateAttemptAddress Address => Attempt.Address;
    public IntegrationCandidateIdentity Candidate => Address.Candidate;
    public IntegrationCandidateDisposition Disposition => Attempt.Disposition;
    public IntegrationGraphCandidateOccurrence? Occurrence { get; }
}

/// <summary>
/// The typed graph payload for one independently validated Integration
/// projection.
/// </summary>
public sealed class IntegrationGraphProjectionResult :
    IntegrationCensusProjectionResult
{
    internal IntegrationGraphProjectionResult(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot,
        InspectionGraphInducedSetRequest graphRequest,
        InspectionGraphDocument document,
        ImmutableArray<IntegrationGraphCandidateProjection>
            candidateInventory)
        : base(RequireGraphPlan(plan), snapshot)
    {
        ArgumentNullException.ThrowIfNull(graphRequest);
        ArgumentNullException.ThrowIfNull(document);
        GraphRequest = graphRequest;
        Document = document;
        CandidateInventory = candidateInventory;
    }

    public InspectionGraphInducedSetRequest GraphRequest { get; }
    public InspectionGraphDocument Document { get; }
    public ImmutableArray<IntegrationGraphCandidateProjection>
        CandidateInventory { get; }

    internal static AnalysisRequestPlan RequireGraphPlan(
        AnalysisRequestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(
                plan.Projection,
                IntegrationAnalysisCatalog.Graph))
        {
            throw new ArgumentException(
                "Integration graph projection requires the configured graph projection.",
                nameof(plan));
        }

        return plan;
    }
}

/// <summary>
/// Projects <c>In</c> Census candidates through the existing Integration
/// relationship and induced-set contracts.
/// </summary>
public static class IntegrationGraphProjection
{
    public static IntegrationGraphProjectionResult Project(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot,
        InspectionGraphInducedSetRequest graphRequest)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(graphRequest);
        IntegrationGraphProjectionResult.RequireGraphPlan(plan);
        if (!snapshot.IsCompatibleWith(plan))
        {
            throw new ArgumentException(
                "The Integration graph request is not compatible with the Census snapshot.",
                nameof(plan));
        }
        ValidateRelationships(graphRequest.Relationships);

        var builder = new SourceBuilder(snapshot);
        Dictionary<
            IntegrationCandidateAttemptAddress,
            CandidateOccurrenceShape> shapes =
            builder.AddCandidates();
        foreach (InspectionGraphSubject subject in graphRequest.Subjects)
            builder.AddRequestedSubject(subject);

        InspectionGraphDocument source = builder.Build();
        InspectionGraphDocument document =
            InspectionGraphInducedSetProjection.Project(
                source,
                graphRequest);
        Dictionary<IntegrationCandidateAttemptAddress, int> occurrenceIds =
            RetainedOccurrenceIds(document);
        Dictionary<int, int> edgeIds = RetainedEdgeIds(document);

        ImmutableArray<IntegrationGraphCandidateProjection> inventory =
        [
            .. snapshot.ClassifiedAttempts.Select(attempt =>
            {
                if (attempt.Disposition
                    is not IntegrationCandidateDisposition.In)
                {
                    return new IntegrationGraphCandidateProjection(
                        attempt,
                        occurrence: null);
                }

                CandidateOccurrenceShape shape =
                    shapes[attempt.Address];
                IntegrationGraphOccurrenceProjection projection =
                    occurrenceIds.TryGetValue(
                        attempt.Address,
                        out int occurrenceId)
                        ? new IntegrationGraphOccurrenceProjection.Retained(
                            occurrenceId,
                            edgeIds[occurrenceId])
                        : new IntegrationGraphOccurrenceProjection
                            .FilteredByRequest();
                return new IntegrationGraphCandidateProjection(
                    attempt,
                    new IntegrationGraphCandidateOccurrence(
                        shape.EdgeSource,
                        shape.EdgeTarget,
                        shape.OccurrenceSource,
                        shape.OccurrenceTarget,
                        projection));
            }),
        ];
        return new IntegrationGraphProjectionResult(
            plan,
            snapshot,
            graphRequest,
            document,
            inventory);
    }

    static void ValidateRelationships(
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships)
    {
        HashSet<InspectionGraphRelationshipDescriptor> configured =
        [
            .. IntegrationAnalysisCatalog.ProducerPolicies.Select(
                static policy => policy.Relationship),
        ];
        foreach (InspectionGraphRelationshipDescriptor relationship
            in relationships)
        {
            if (!configured.Contains(relationship))
            {
                throw new ArgumentException(
                    $"Relationship '{relationship.Id}' is not produced by the Integration Census.",
                    nameof(relationships));
            }
        }
    }

    static Dictionary<IntegrationCandidateAttemptAddress, int>
        RetainedOccurrenceIds(InspectionGraphDocument document)
    {
        var ids =
            new Dictionary<IntegrationCandidateAttemptAddress, int>();
        foreach (InspectionGraphOccurrence occurrence in document.Occurrences)
        {
            if (occurrence.Evidence
                is not InspectionGraphIntegrationCensusCandidateEvidence
                    evidence)
            {
                throw new InspectionQueryException(
                    "An Integration Census graph retained unsupported occurrence evidence.");
            }
            if (!ids.TryAdd(evidence.Attempt.Address, occurrence.Id))
            {
                throw new InspectionQueryException(
                    "An Integration candidate attempt contributed more than one graph occurrence.");
            }
        }

        return ids;
    }

    static Dictionary<int, int> RetainedEdgeIds(
        InspectionGraphDocument document)
    {
        var ids = new Dictionary<int, int>();
        foreach (InspectionGraphEdge edge in document.Edges)
        {
            foreach (int occurrenceId in edge.OccurrenceIds)
            {
                if (!ids.TryAdd(occurrenceId, edge.Id))
                {
                    throw new InspectionQueryException(
                        "An Integration occurrence contributed to more than one logical edge.");
                }
            }
        }

        return ids;
    }

    sealed class SourceBuilder
    {
        readonly IntegrationCensusSnapshot _snapshot;
        readonly HashSet<IntegrationSourceParticipantIdentity>
            _participants;
        readonly HashSet<IntegrationTypeIdentity> _selectedTypes;
        readonly HashSet<IntegrationCandidateSourceIdentity>
            _candidateSources;
        readonly HashSet<RealizedMemberCoordinate.Package> _packages;
        readonly List<InspectionGraphNode> _nodes = [];
        readonly Dictionary<InspectionGraphSubject, int> _nodeIds = [];
        readonly List<InspectionGraphGroup> _groups = [];
        readonly Dictionary<RealizedMemberCoordinate.Package, int>
            _packageGroupIds = [];
        readonly List<InspectionGraphOccurrence> _occurrences = [];
        readonly List<EdgeBuilder> _edges = [];
        readonly Dictionary<EdgeKey, EdgeBuilder> _edgeByKey = [];

        internal SourceBuilder(IntegrationCensusSnapshot snapshot)
        {
            _snapshot = snapshot;
            _participants =
            [
                .. snapshot.SourceParticipants.Concat(
                    snapshot.SelectedTypes.Select(
                        static type => type.Participant)),
            ];
            _selectedTypes = snapshot.SelectedTypes.ToHashSet();
            _candidateSources =
            [
                .. snapshot.Candidates.Select(
                    static candidate => candidate.Identity.Source),
            ];
            _packages =
            [
                .. _participants.Select(
                    static participant => participant.Coordinate)
                    .OfType<RealizedMemberCoordinate.Package>(),
            ];
        }

        internal Dictionary<
            IntegrationCandidateAttemptAddress,
            CandidateOccurrenceShape> AddCandidates()
        {
            var shapes =
                new Dictionary<
                    IntegrationCandidateAttemptAddress,
                    CandidateOccurrenceShape>();
            foreach (IntegrationCandidateAttempt.Classified attempt
                in _snapshot.ClassifiedAttempts)
            {
                if (attempt.Disposition
                    is not IntegrationCandidateDisposition.In inside)
                {
                    continue;
                }

                IntegrationCandidateIdentity candidate =
                    attempt.Address.Candidate;
                InspectionGraphSubject occurrenceSource =
                    SourceSubject(candidate.Source);
                InspectionGraphSubject occurrenceTarget =
                    InspectionGraphSubject.ForIntegrationType(
                        inside.Peer.Terminal);
                InspectionGraphSubject edgeSource = ReferenceEquals(
                    candidate.Relationship,
                    InspectionGraphIntegrationsCatalog
                        .IntegrationOpportunity)
                            ? InspectionGraphSubject.ForIntegrationAssembly(
                                candidate.Source.Participant)
                            : occurrenceSource;
                InspectionGraphSubject edgeTarget = occurrenceTarget;

                int sourceNodeId = AddNode(edgeSource);
                int targetNodeId = AddNode(edgeTarget);
                AddNode(occurrenceSource);
                AddNode(occurrenceTarget);
                int occurrenceId = _occurrences.Count;
                _occurrences.Add(
                    new InspectionGraphOccurrence(
                        occurrenceId,
                        candidate.Relationship,
                        occurrenceSource,
                        occurrenceTarget,
                        new InspectionGraphIntegrationCensusCandidateEvidence(
                            attempt),
                        []));

                var key = new EdgeKey(
                    sourceNodeId,
                    targetNodeId,
                    candidate.Relationship);
                if (!_edgeByKey.TryGetValue(key, out EdgeBuilder? edge))
                {
                    edge = new EdgeBuilder(
                        sourceNodeId,
                        targetNodeId,
                        candidate.Relationship);
                    _edgeByKey.Add(key, edge);
                    _edges.Add(edge);
                }
                edge.OccurrenceIds.Add(occurrenceId);
                shapes.Add(
                    attempt.Address,
                    new CandidateOccurrenceShape(
                        edgeSource,
                        edgeTarget,
                        occurrenceSource,
                        occurrenceTarget));
            }

            return shapes;
        }

        internal void AddRequestedSubject(InspectionGraphSubject subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            switch (subject)
            {
                case InspectionGraphSubject.PackageSubject
                {
                    Identity:
                        InspectionGraphPackageIdentity.Realized package,
                } when _packages.Contains(package.Package):
                    AddPackageGroup(package.Package);
                    return;
                case InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.CensusParticipant
                        assembly,
                } when _participants.Contains(assembly.Participant):
                    AddNode(subject);
                    return;
                case InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.CensusType type,
                } when _selectedTypes.Contains(type.Identity):
                    AddNode(subject);
                    return;
                case InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CensusMember
                        member,
                } when _candidateSources.Contains(member.Source):
                    AddNode(subject);
                    return;
                default:
                    throw new ArgumentException(
                        "An explicit Integration graph subject must be backed by the Census participant, selected-Type, or candidate-source roster.",
                        nameof(subject));
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
            InspectionGraphFailure[] failures = _snapshot.IsComplete
                ? []
                :
                [
                    new InspectionGraphFailure(
                        InspectionGraphIntegrationsCatalog.ProjectionFailure,
                        Evidence:
                            new InspectionGraphIntegrationCensusFailureEvidence(
                                _snapshot)),
                ];
            InspectionGraphDocumentScope scope =
                _occurrences.Count == 0
                && failures.Length == 0
                && _nodes.All(static node => node.Subject.IsPortable)
                && _groups.All(static group => group.Subject.IsPortable)
                    ? InspectionGraphDocumentScope.Portable
                    : InspectionGraphDocumentScope.SessionBound;
            return new InspectionGraphDocument(
                scope,
                InspectionGraphModeRequest.InducedSet(
                    InspectionGraphInducedSetRule.DocumentSubjects),
                _nodes,
                _groups,
                edges,
                _occurrences,
                [],
                [],
                [],
                failures);
        }

        int AddNode(InspectionGraphSubject subject)
        {
            if (_nodeIds.TryGetValue(subject, out int existing))
                return existing;

            int[] groupIds = ParticipantOf(subject) is { } participant
                && participant.Coordinate
                    is RealizedMemberCoordinate.Package package
                        ? [AddPackageGroup(package)]
                        : [];
            int id = _nodes.Count;
            _nodes.Add(
                new InspectionGraphNode(
                    id,
                    subject,
                    InspectionGraphNodeRole.Ordinary,
                    groupIds));
            _nodeIds.Add(subject, id);
            return id;
        }

        int AddPackageGroup(RealizedMemberCoordinate.Package package)
        {
            if (_packageGroupIds.TryGetValue(package, out int existing))
                return existing;

            int id = _groups.Count;
            _groups.Add(
                new InspectionGraphGroup(
                    id,
                    InspectionGraphSubject.ForRealizedPackage(package),
                    parentId: null));
            _packageGroupIds.Add(package, id);
            return id;
        }

        static InspectionGraphSubject SourceSubject(
            IntegrationCandidateSourceIdentity source) =>
            source.Element switch
            {
                IntegrationCandidateSourceElement.Member =>
                    InspectionGraphSubject.ForIntegrationMember(source),
                IntegrationCandidateSourceElement.Type =>
                    InspectionGraphSubject.ForIntegrationType(
                        new IntegrationTypeIdentity(
                            source.Participant,
                            source.SourceType)),
                _ => throw new InvalidOperationException(
                    "Unknown Integration candidate source element."),
            };

        static IntegrationSourceParticipantIdentity? ParticipantOf(
            InspectionGraphSubject subject) =>
            subject switch
            {
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CensusMember member,
                } => member.Source.Participant,
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.CensusType type,
                } => type.Identity.Participant,
                InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.CensusParticipant
                        assembly,
                } => assembly.Participant,
                _ => null,
            };
    }

    sealed record CandidateOccurrenceShape(
        InspectionGraphSubject EdgeSource,
        InspectionGraphSubject EdgeTarget,
        InspectionGraphSubject OccurrenceSource,
        InspectionGraphSubject OccurrenceTarget);

    readonly record struct EdgeKey(
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
}
