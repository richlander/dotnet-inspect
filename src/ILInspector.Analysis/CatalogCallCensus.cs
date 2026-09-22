using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Total ordering key for one exact method in a catalog call census.
/// </summary>
public sealed record CatalogCallCensusMethodOrderingKey(
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId,
    int MetadataToken)
    : IComparable<CatalogCallCensusMethodOrderingKey>
{
    public int CompareTo(CatalogCallCensusMethodOrderingKey? other)
    {
        if (other is null)
            return 1;

        int comparison = CompareAssembly(Assembly, other.Assembly);
        if (comparison != 0)
            return comparison;
        comparison = ModuleVersionId.CompareTo(other.ModuleVersionId);
        return comparison != 0
            ? comparison
            : MetadataToken.CompareTo(other.MetadataToken);
    }

    internal static int CompareAssembly(
        AssemblyReferenceIdentity left,
        AssemblyReferenceIdentity right)
    {
        int comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Name,
            right.Name);
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.Ordinal.Compare(
            left.Name,
            right.Name);
        if (comparison != 0)
            return comparison;
        comparison = Comparer<Version?>.Default.Compare(
            left.Version,
            right.Version);
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Culture ?? "",
            right.Culture ?? "");
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.Ordinal.Compare(
            left.Culture ?? "",
            right.Culture ?? "");
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.PublicKeyToken ?? "",
            right.PublicKeyToken ?? "");
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                left.PublicKeyToken ?? "",
                right.PublicKeyToken ?? "");
    }
}

/// <summary>
/// Total ordering key for one physical call instruction in a catalog call
/// census.
/// </summary>
public sealed record CatalogCallCensusOccurrenceOrderingKey(
    CatalogCallCensusMethodOrderingKey EvidenceMethod,
    int ILOffset,
    int OperandToken,
    CallKind Kind)
    : IComparable<CatalogCallCensusOccurrenceOrderingKey>
{
    public int CompareTo(CatalogCallCensusOccurrenceOrderingKey? other)
    {
        if (other is null)
            return 1;

        int comparison = EvidenceMethod.CompareTo(other.EvidenceMethod);
        if (comparison != 0)
            return comparison;
        comparison = ILOffset.CompareTo(other.ILOffset);
        if (comparison != 0)
            return comparison;
        comparison = OperandToken.CompareTo(other.OperandToken);
        return comparison != 0
            ? comparison
            : Kind.CompareTo(other.Kind);
    }
}

/// <summary>One exact declared method in an admitted census participant.</summary>
public sealed record CatalogCallCensusMember(
    CatalogCallGraphParticipant Participant,
    MethodIdentity Method,
    GraphNodeEvidence Evidence,
    bool HasBody,
    AnalysisDiagnostic? Diagnostic,
    CatalogCallCensusMethodOrderingKey OrderingKey);

/// <summary>
/// One admitted physical call instruction whose static operand resolved to one
/// exact declared method in the census population.
/// </summary>
public sealed record CatalogCallCensusOccurrence(
    CatalogCallGraphParticipant Source,
    MethodIdentity SourceMethod,
    CatalogCallCensusMethodOrderingKey SourceOrderingKey,
    CatalogCallGraphParticipant Target,
    MethodIdentity TargetMethod,
    CatalogCallCensusMethodOrderingKey TargetOrderingKey,
    DirectCall Call,
    GraphNodeEvidence CallSiteEvidence,
    CatalogCallCensusOccurrenceOrderingKey OrderingKey);

/// <summary>
/// One admitted physical call instruction for which the census population did
/// not establish one exact static operand definition.
/// </summary>
public sealed record CatalogCallCensusUnresolvedOccurrence(
    CatalogCallGraphParticipant Source,
    MethodIdentity SourceMethod,
    CatalogCallCensusMethodOrderingKey SourceOrderingKey,
    DirectCall Call,
    GraphNodeEvidence CallSiteEvidence,
    CatalogCallCensusOccurrenceOrderingKey OrderingKey);

/// <summary>
/// One exact call binding in a population that admits another identity of the
/// selected target assembly name.
/// </summary>
public sealed class CatalogCallCensusVersionSkewEvidence
{
    internal CatalogCallCensusVersionSkewEvidence(
        GraphNodeEvidence callSite,
        AssemblyReferenceIdentity requested,
        AssemblyReferenceIdentity selected,
        ImmutableArray<AssemblyReferenceIdentity> admittedAlternatives)
    {
        CallSite = callSite;
        Requested = requested;
        Selected = selected;
        AdmittedAlternatives = admittedAlternatives;
    }

    public GraphNodeEvidence CallSite { get; }
    public AssemblyReferenceIdentity Requested { get; }
    public AssemblyReferenceIdentity Selected { get; }
    public ImmutableArray<AssemblyReferenceIdentity>
        AdmittedAlternatives { get; }
}

/// <summary>
/// Graph correspondence diagnostics plus exact physical calls that did not
/// resolve to one admitted definition.
/// </summary>
public sealed record CatalogCallCensusDiagnostics(
    CatalogCallGraphDiagnostics Graph,
    int UnresolvedOccurrenceCount,
    int VersionSkewedBindingCount)
{
    public bool IsIncomplete =>
        Graph.IsIncomplete
        || UnresolvedOccurrenceCount > 0;
}

/// <summary>
/// Generation-bound receipt for one exact catalog call census.
/// </summary>
public sealed record CatalogCallCensusReceipt(
    AssemblyCatalogId Catalog,
    AssemblyCatalogGenerationId Generation,
    int ParticipantCount,
    int MemberCount,
    int OccurrenceCount,
    int UnresolvedOccurrenceCount,
    int VersionSkewedBindingCount);

/// <summary>
/// Exact members, resolved physical call occurrences, and correspondence
/// diagnostics for one fixed catalog call-graph population.
/// </summary>
public sealed record CatalogCallCensus(
    CatalogCallCensusReceipt Receipt,
    ImmutableArray<CatalogCallGraphParticipant> Population,
    ImmutableArray<CatalogCallCensusMember> Members,
    ImmutableArray<CatalogCallCensusOccurrence> Occurrences,
    ImmutableArray<CatalogCallCensusUnresolvedOccurrence>
        UnresolvedOccurrences,
    ImmutableArray<CatalogCallCensusVersionSkewEvidence>
        VersionSkewedBindings,
    CatalogCallCensusDiagnostics Diagnostics,
    ImmutableArray<GraphNodeEvidence> IncompleteNodes,
    ImmutableArray<GraphEdgeEvidence> IncompleteEdges)
{
    public bool IsComplete =>
        Population.All(participant =>
            participant.CallGraph.HasFullMethodEvidenceScope
            && participant.CallGraph.Diagnostics.IsEmpty)
        && !Diagnostics.IsIncomplete;
}
