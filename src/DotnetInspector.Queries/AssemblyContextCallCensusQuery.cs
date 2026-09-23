using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// The admitted group contains two registrations for one physical assembly
/// artifact.
/// </summary>
public sealed class AssemblyContextCallCensusRequestException
    : ArgumentException
{
    internal AssemblyContextCallCensusRequestException(string message)
        : base(message, "group")
    {
    }
}

/// <summary>One participant that supplied focused call evidence.</summary>
public sealed record AssemblyContextCallCensusParticipant(
    AssemblyContextSubject Subject,
    Analysis.LibraryBodyAnalysisReceipt AnalysisReceipt);

/// <summary>One participant that could not supply focused call evidence.</summary>
public abstract record AssemblyContextCallCensusFailure(
    AssemblyContextSubject Subject)
{
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyContextCallCensusFailure(Subject);

    public sealed record InvalidImage(
        AssemblyContextSubject Subject,
        Exception Error)
        : AssemblyContextCallCensusFailure(Subject);
}

/// <summary>One exact declared method in the admitted census population.</summary>
public sealed record AssemblyContextCallCensusMember(
    AssemblyContextSubject Subject,
    Analysis.MethodIdentity Method,
    Analysis.GraphNodeEvidence Evidence,
    bool HasBody,
    Analysis.AnalysisDiagnostic? Diagnostic,
    Analysis.CatalogCallCensusMethodOrderingKey OrderingKey);

/// <summary>
/// One admitted physical call instruction whose static operand resolved to an
/// exact declared method in the census population.
/// </summary>
public sealed record AssemblyContextCallCensusOccurrence(
    AssemblyContextSubject Source,
    Analysis.MethodIdentity SourceMethod,
    Analysis.CatalogCallCensusMethodOrderingKey SourceOrderingKey,
    AssemblyContextSubject Target,
    Analysis.MethodIdentity TargetMethod,
    Analysis.CatalogCallCensusMethodOrderingKey TargetOrderingKey,
    Analysis.DirectCall Call,
    Analysis.GraphNodeEvidence CallSiteEvidence,
    Analysis.CatalogCallCensusOccurrenceOrderingKey OrderingKey);

/// <summary>
/// One admitted physical call instruction whose exact static operand
/// definition was unavailable in the census population.
/// </summary>
public sealed record AssemblyContextCallCensusUnresolvedOccurrence(
    AssemblyContextSubject Source,
    Analysis.MethodIdentity SourceMethod,
    Analysis.CatalogCallCensusMethodOrderingKey SourceOrderingKey,
    Analysis.DirectCall Call,
    Analysis.GraphNodeEvidence CallSiteEvidence,
    Analysis.CatalogCallCensusOccurrenceOrderingKey OrderingKey);

/// <summary>
/// Exact group-wide call evidence and every qualification that limits a
/// complete static-call claim.
/// </summary>
public sealed record AssemblyContextCallCensusResult(
    ImmutableArray<AssemblyContextSubject> Subjects,
    ImmutableArray<AssemblyContextCallCensusParticipant> Participants,
    ImmutableArray<AssemblyContextCallCensusFailure> Failures,
    Analysis.CatalogCallCensusReceipt? Receipt,
    ImmutableArray<AssemblyContextCallCensusMember> Members,
    ImmutableArray<AssemblyContextCallCensusOccurrence> Occurrences,
    ImmutableArray<AssemblyContextCallCensusUnresolvedOccurrence>
        UnresolvedOccurrences,
    ImmutableArray<Analysis.CatalogCallCensusVersionSkewEvidence>
        VersionSkewedBindings,
    Analysis.CatalogCallCensusDiagnostics Diagnostics,
    ImmutableArray<Analysis.GraphNodeEvidence> IncompleteNodes,
    ImmutableArray<Analysis.GraphEdgeEvidence> IncompleteEdges)
{
    public bool IsComplete =>
        Receipt is not null
        && Failures.IsEmpty
        && Subjects.Length == Participants.Length
        && Participants.All(participant =>
            participant.AnalysisReceipt.HasFullMethodEvidenceScope
            && participant.AnalysisReceipt.Diagnostics.IsEmpty)
        && !Diagnostics.IsIncomplete;
}

/// <summary>
/// Publishes the exact resolved <c>call</c>, <c>callvirt</c>, and
/// <c>newobj</c> census for every available participant in one admitted,
/// binding-consistent assembly context group.
/// </summary>
public static class AssemblyContextCallCensusQuery
{
    public static InspectionQuery<AssemblyContextCallCensusResult> Definition
    {
        get;
    } = new("Assembly context call census", InspectionCost.Unbounded);

    public static AssemblyContextCallCensusResult Execute(
        AssemblyContextGroup group,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var available = ImmutableArray.CreateBuilder<
            AssemblyContextCallGraphAnalysisParticipant>();
        var failures =
            ImmutableArray.CreateBuilder<AssemblyContextCallCensusFailure>();
        foreach (AssemblyContextParticipant participant
            in group.Participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (AssemblyContextCallGraphAnalysis.Execute(
                group,
                participant,
                cancellationToken))
            {
                case AssemblyContextCallGraphAnalysisResult
                    .Available result:
                    available.Add(result.Participant);
                    break;
                case AssemblyContextCallGraphAnalysisResult
                    .Rejected rejected:
                    failures.Add(
                        new AssemblyContextCallCensusFailure.Rejected(
                            rejected.Subject,
                            rejected.Failure));
                    break;
                case AssemblyContextCallGraphAnalysisResult
                    .InvalidImage invalid:
                    failures.Add(
                        new AssemblyContextCallCensusFailure.InvalidImage(
                            invalid.Subject,
                            invalid.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown call-graph analysis result.");
            }
        }

        ImmutableArray<AssemblyContextSubject> subjects =
        [
            .. group.Participants
                .Select(participant =>
                    new AssemblyContextSubject(participant.Assembly))
                .OrderBy(SubjectOrderingKey, StringComparer.Ordinal),
        ];
        ImmutableArray<AssemblyContextCallGraphAnalysisParticipant>
            orderedAvailable =
        [
            .. available
                .OrderBy(participant =>
                    new Analysis.CatalogCallCensusMethodOrderingKey(
                        participant.Assembly.Identity,
                        participant.CallGraph.ModuleIdentity.ModuleVersionId,
                        MetadataToken: 0)),
        ];
        RejectDuplicateArtifacts(orderedAvailable);
        ImmutableArray<AssemblyContextCallCensusFailure> orderedFailures =
        [
            .. failures.OrderBy(
                failure => SubjectOrderingKey(failure.Subject),
                StringComparer.Ordinal),
        ];
        if (orderedAvailable.IsEmpty)
        {
            return new(
                subjects,
                [],
                orderedFailures,
                Receipt: null,
                Members: [],
                Occurrences: [],
                UnresolvedOccurrences: [],
                VersionSkewedBindings: [],
                new(
                    Analysis.CatalogCallGraphDiagnostics.Empty,
                    UnresolvedOccurrenceCount: 0,
                    VersionSkewedBindingCount: 0),
                IncompleteNodes: [],
                IncompleteEdges: []);
        }

        SourceRelativeAssemblyGroupBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                orderedAvailable.Select(participant => (
                    participant.Assembly,
                    participant.ContextParticipant.BindingPolicy)));
        using var scope = new Analysis.CatalogCallGraphScope(
            policy,
            orderedAvailable.Select(participant =>
                new Analysis.CatalogCallGraphParticipant(
                    participant.CallGraph,
                    participant.Assembly)));
        Analysis.CatalogCallCensus census = scope.Census();
        Dictionary<
            Analysis.LibraryCallGraphAnalysisResult,
            AssemblyContextCallGraphAnalysisParticipant> analyzedByCallGraph =
                new(ReferenceEqualityComparer.Instance);
        foreach (AssemblyContextCallGraphAnalysisParticipant participant
            in orderedAvailable)
        {
            analyzedByCallGraph.Add(
                participant.CallGraph,
                participant);
        }
        Dictionary<
            Analysis.LibraryCallGraphAnalysisResult,
            AssemblyContextCallCensusParticipant> participantsByCallGraph =
                new(ReferenceEqualityComparer.Instance);
        foreach (Analysis.CatalogCallGraphParticipant participant
            in census.Population)
        {
            AssemblyContextCallGraphAnalysisParticipant analyzed =
                analyzedByCallGraph[participant.CallGraph];
            participantsByCallGraph.Add(
                participant.CallGraph,
                new AssemblyContextCallCensusParticipant(
                    analyzed.Subject,
                    analyzed.CallGraph.Receipt));
        }

        return new(
            subjects,
            [
                .. census.Population.Select(participant =>
                    participantsByCallGraph[
                        participant.CallGraph]),
            ],
            orderedFailures,
            census.Receipt,
            [
                .. census.Members.Select(member =>
                {
                    AssemblyContextCallCensusParticipant participant =
                        participantsByCallGraph[
                            member.Participant.CallGraph];
                    return new AssemblyContextCallCensusMember(
                        participant.Subject,
                        member.Method,
                        member.Evidence,
                        member.HasBody,
                        member.Diagnostic,
                        member.OrderingKey);
                }),
            ],
            [
                .. census.Occurrences.Select(occurrence =>
                    new AssemblyContextCallCensusOccurrence(
                        participantsByCallGraph[
                            occurrence.Source.CallGraph].Subject,
                        occurrence.SourceMethod,
                        occurrence.SourceOrderingKey,
                        participantsByCallGraph[
                            occurrence.Target.CallGraph].Subject,
                        occurrence.TargetMethod,
                        occurrence.TargetOrderingKey,
                        occurrence.Call,
                        occurrence.CallSiteEvidence,
                        occurrence.OrderingKey)),
            ],
            [
                .. census.UnresolvedOccurrences.Select(occurrence =>
                    new AssemblyContextCallCensusUnresolvedOccurrence(
                        participantsByCallGraph[
                            occurrence.Source.CallGraph].Subject,
                        occurrence.SourceMethod,
                        occurrence.SourceOrderingKey,
                        occurrence.Call,
                        occurrence.CallSiteEvidence,
                        occurrence.OrderingKey)),
            ],
            census.VersionSkewedBindings,
            census.Diagnostics,
            census.IncompleteNodes,
            census.IncompleteEdges);
    }

    static void RejectDuplicateArtifacts(
        ImmutableArray<AssemblyContextCallGraphAnalysisParticipant>
            participants)
    {
        for (int index = 0; index < participants.Length; index++)
        {
            AssemblyContextCallGraphAnalysisParticipant current =
                participants[index];
            for (int earlier = 0; earlier < index; earlier++)
            {
                AssemblyContextCallGraphAnalysisParticipant candidate =
                    participants[earlier];
                if (candidate.CallGraph.ModuleIdentity.ModuleVersionId
                        == current.CallGraph.ModuleIdentity.ModuleVersionId
                    && candidate.Assembly.Identity.IsEquivalentTo(
                        current.Assembly.Identity))
                {
                    throw new AssemblyContextCallCensusRequestException(
                        "A graph-wide call census requires distinct physical "
                        + "assembly artifacts.");
                }
            }
        }
    }

    static string SubjectOrderingKey(AssemblyContextSubject subject) =>
        string.Join(
            "\0",
            subject.Identity.Name,
            subject.Identity.Version?.ToString() ?? "",
            subject.Identity.Culture ?? "",
            subject.Identity.PublicKeyToken ?? "",
            MetadataReceiptEvidence.For(subject.Registration));
}
