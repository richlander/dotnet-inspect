using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// One independently validated projection over a compatible Integration
/// Census snapshot.
/// </summary>
public abstract class IntegrationCensusProjectionResult
{
    private protected IntegrationCensusProjectionResult(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsCompatibleWith(plan))
        {
            throw new ArgumentException(
                "The Integration projection request is not compatible with the Census snapshot.",
                nameof(plan));
        }

        Plan = plan;
        Snapshot = snapshot;
    }

    public AnalysisRequestPlan Plan { get; }
    public IntegrationCensusSnapshot Snapshot { get; }
    public AnalysisDescriptor Analysis => Plan.Analysis;
    public AnalysisReportSurface ReportSurface => Plan.ReportSurface;
    public AnalysisUniverseDescription Universe => Plan.Universe;
    public AnalysisQuestionMode Mode => Plan.Mode;
    public AnalysisProjectionDescriptor Projection => Plan.Projection;
    public ImmutableArray<AnalysisUniverseRequirementDescriptor>
        UniverseRequirements => Plan.UniverseRequirements;
    public IntegrationConceptCatalogRevision CatalogRevision =>
        Snapshot.CatalogRevision;
    public bool IsComplete => Snapshot.IsComplete;
}

/// <summary>
/// The exact structured peer handoff retained by an Integration Inventory row.
/// </summary>
public sealed class IntegrationPeerLookup
{
    public IntegrationPeerLookup(IntegrationCandidatePeerIdentity identity)
        : this(identity, authoritativeProvenance: null)
    {
    }

    internal IntegrationPeerLookup(
        IntegrationCandidatePeerIdentity identity,
        RealizedMemberCoordinate? authoritativeProvenance)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
        Type = identity.Type;
        FindPattern = Type.ToMetadataFullName();
        Scope = identity is IntegrationCandidatePeerIdentity.NamedType named
            ? named.Reference.Scope
            : null;
        PolicyTarget =
            (identity as IntegrationCandidatePeerIdentity.PolicyTarget)?.Target;
        AuthoritativeProvenance = authoritativeProvenance;
        AuthoritativeParent = IntegrationInventoryProjection.ParentOf(
            authoritativeProvenance);
    }

    public IntegrationCandidatePeerIdentity Identity { get; }
    public MetadataTypeDefinitionName Type { get; }
    public MetadataTypeReferenceScope? Scope { get; }
    public IntegrationOpportunityTarget? PolicyTarget { get; }
    public RealizedMemberCoordinate? AuthoritativeProvenance { get; }
    public RealizedMemberCoordinate? AuthoritativeParent { get; }
    public string FindPattern { get; }
}

/// <summary>
/// One classified candidate attempt in the Integration Inventory.
/// </summary>
public sealed class IntegrationInventoryRow
{
    internal IntegrationInventoryRow(
        IntegrationCandidateAttempt.Classified attempt,
        ImmutableArray<IntegrationProducerPolicyAttemptAddress>
            producerPolicyAttempts)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Attempt = attempt.Address;
        ProducerPolicyAttempts = producerPolicyAttempts;
        Candidate = Attempt.Candidate;
        BindingContext = Attempt.BindingContext;
        Relationship = Candidate.Relationship;
        Concept = Candidate.Concept;
        Source = Candidate.Source;
        SourceAssembly = Source.Participant.Assembly;
        SourceProvenance = Source.Participant.Coordinate;
        SourceParent = IntegrationInventoryProjection.ParentOf(
            SourceProvenance);
        ResolvedPeer = attempt.Disposition.Peer;
        ResolutionPath = ResolvedPeer.ResolutionPath;
        ForwardingHops =
        [
            .. ResolutionPath.Take(ResolutionPath.Length - 1),
        ];
        TerminalDefinition = ResolvedPeer.Terminal;
        TerminalAssembly = TerminalDefinition.Participant.Assembly;
        TerminalProvenance = TerminalDefinition.Participant.Coordinate;
        TerminalParent = IntegrationInventoryProjection.ParentOf(
            TerminalProvenance);
        PeerLookup = new IntegrationPeerLookup(
            Candidate.Peer,
            TerminalProvenance);
        Disposition = attempt.Disposition;
        OutReason = attempt.Disposition
            is IntegrationCandidateDisposition.Out outside
                ? outside.Reason
                : null;
        AdmittedRelationshipIdentity = attempt.Disposition
            is IntegrationCandidateDisposition.In
                ? Attempt
                : null;
    }

    public IntegrationCandidateAttemptAddress Attempt { get; }
    public IntegrationCandidateIdentity Candidate { get; }
    public IIntegrationBindingContextIdentity BindingContext { get; }
    public ImmutableArray<IntegrationProducerPolicyAttemptAddress>
        ProducerPolicyAttempts { get; }
    public InspectionGraphRelationshipDescriptor Relationship { get; }
    public IntegrationConceptDescriptor Concept { get; }
    public IntegrationCandidateSourceIdentity Source { get; }
    public AssemblyReferenceIdentity SourceAssembly { get; }
    public RealizedMemberCoordinate? SourceProvenance { get; }
    public RealizedMemberCoordinate? SourceParent { get; }
    public IntegrationPeerLookup PeerLookup { get; }
    public IntegrationResolvedPeer ResolvedPeer { get; }
    public ImmutableArray<IntegrationTypeIdentity> ResolutionPath { get; }
    public ImmutableArray<IntegrationTypeIdentity> ForwardingHops { get; }
    public IntegrationTypeIdentity TerminalDefinition { get; }
    public AssemblyReferenceIdentity TerminalAssembly { get; }
    public RealizedMemberCoordinate? TerminalProvenance { get; }
    public RealizedMemberCoordinate? TerminalParent { get; }
    public IntegrationCandidateDisposition Disposition { get; }
    public IntegrationCandidateOutReason? OutReason { get; }

    /// <summary>
    /// The context-addressed candidate identity admitted as a relationship, or
    /// <c>null</c> when the terminal peer is outside the selected universe.
    /// </summary>
    public IntegrationCandidateAttemptAddress? AdmittedRelationshipIdentity
        { get; }

}

/// <summary>The typed row payload for the Integration Inventory section.</summary>
public sealed class IntegrationInventoryProjectionResult
    : IntegrationCensusProjectionResult
{
    internal IntegrationInventoryProjectionResult(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot,
        ImmutableArray<IntegrationInventoryRow> rows)
        : base(RequireRowsPlan(plan), snapshot)
    {
        Rows = rows;
    }

    public ImmutableArray<IntegrationInventoryRow> Rows { get; }

    static AnalysisRequestPlan RequireRowsPlan(AnalysisRequestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(
                plan.Projection,
                IntegrationAnalysisCatalog.Rows))
        {
            throw new ArgumentException(
                "Integration Inventory requires the configured rows projection.",
                nameof(plan));
        }

        return plan;
    }
}

/// <summary>Projects classified Census attempts into canonical inventory rows.</summary>
public static class IntegrationInventoryProjection
{
    public static IntegrationInventoryProjectionResult Project(
        AnalysisRequestPlan plan,
        IntegrationCensusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        Dictionary<IntegrationCandidateIdentity, IntegrationCensusCandidate>
            candidates = snapshot.Candidates.ToDictionary(
                static candidate => candidate.Identity);
        ImmutableArray<IntegrationInventoryRow> rows =
        [
            .. snapshot.ClassifiedAttempts.Select(attempt =>
                new IntegrationInventoryRow(
                    attempt,
                    candidates[attempt.Address.Candidate]
                        .ProducerAttempts)),
        ];
        return new IntegrationInventoryProjectionResult(
            plan,
            snapshot,
            rows);
    }

    internal static RealizedMemberCoordinate? ParentOf(
        RealizedMemberCoordinate? provenance) =>
        provenance is RealizedMemberCoordinate.Package
            or RealizedMemberCoordinate.Platform
                ? provenance
                : null;
}
