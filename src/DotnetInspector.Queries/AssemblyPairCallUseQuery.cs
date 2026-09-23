using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>The reason a pairwise call-use request was rejected.</summary>
public enum AssemblyPairCallUseRequestFailureKind
{
    SameParticipant,
    ParticipantOutsideGroup,
    SamePhysicalAssemblyArtifact,
}

/// <summary>The requested pair cannot form one pairwise call-use query.</summary>
public sealed class AssemblyPairCallUseRequestException
    : ArgumentException
{
    internal AssemblyPairCallUseRequestException(
        AssemblyPairCallUseRequestFailureKind kind,
        string message,
        string parameterName)
        : base(message, parameterName)
    {
        Kind = kind;
    }

    public AssemblyPairCallUseRequestFailureKind Kind { get; }
}

/// <summary>
/// One participant that supplied focused call-graph evidence for a pairwise
/// call-use query.
/// </summary>
public sealed record AssemblyPairCallUseParticipant(
    AssemblyContextSubject Subject,
    Guid ModuleVersionId,
    ImmutableArray<Analysis.AnalysisDiagnostic> Diagnostics);

/// <summary>One participant that could not supply body evidence.</summary>
public abstract record AssemblyPairCallUseFailure(
    AssemblyContextSubject Subject)
{
    public sealed record Rejected(
        AssemblyContextSubject Subject,
        CandidateOpenFailure Failure)
        : AssemblyPairCallUseFailure(Subject);

    public sealed record InvalidImage(
        AssemblyContextSubject Subject,
        Exception Error)
        : AssemblyPairCallUseFailure(Subject);
}

/// <summary>
/// One exact direct IL call or construction whose endpoints belong to the
/// requested library pair.
/// </summary>
public sealed record AssemblyPairCallUseOccurrence(
    AssemblyContextSubject Source,
    Guid SourceModuleVersionId,
    Analysis.MethodIdentity SourceMethod,
    AssemblyContextSubject Target,
    Guid TargetModuleVersionId,
    Analysis.MethodIdentity TargetMethod,
    Analysis.DirectCall Call);

/// <summary>
/// Pair-specific correspondence evidence that prevented a complete absence
/// claim.
/// </summary>
public sealed record AssemblyPairCallUseDiagnostics(
    int UnresolvedCandidateCallCount)
{
    public static AssemblyPairCallUseDiagnostics Empty { get; } =
        new(0);

    public bool IsIncomplete => UnresolvedCandidateCallCount > 0;
}

/// <summary>
/// Exact call-use evidence and completeness state for two admitted assembly
/// participants.
/// </summary>
public sealed record AssemblyPairCallUseResult(
    ImmutableArray<AssemblyContextSubject> Subjects,
    ImmutableArray<AssemblyPairCallUseParticipant> Participants,
    ImmutableArray<AssemblyPairCallUseFailure> Failures,
    ImmutableArray<AssemblyPairCallUseOccurrence> Occurrences,
    AssemblyPairCallUseDiagnostics Diagnostics)
{
    public bool IsComplete =>
        Failures.IsEmpty
        && Subjects.Length == 2
        && Participants.Length == 2
        && Participants.All(
            participant => participant.Diagnostics.IsEmpty)
        && !Diagnostics.IsIncomplete;
}

/// <summary>
/// Produces every exact resolved <c>call</c>, <c>callvirt</c>, and
/// <c>newobj</c> occurrence crossing between two participants in one
/// binding-consistent assembly context group.
/// </summary>
public static class AssemblyPairCallUseQuery
{
    public static InspectionQuery<AssemblyPairCallUseResult> Definition
    {
        get;
    } = new("Assembly pair call use", InspectionCost.Unbounded);

    public static AssemblyPairCallUseResult Execute(
        AssemblyContextGroup group,
        ResolvedAssemblyReference first,
        ResolvedAssemblyReference second)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (ReferenceEquals(first.Registration, second.Registration))
        {
            throw new AssemblyPairCallUseRequestException(
                AssemblyPairCallUseRequestFailureKind.SameParticipant,
                "Pairwise call use requires two distinct participants.",
                nameof(second));
        }

        AssemblyContextParticipant firstParticipant =
            RequireParticipant(group, first);
        AssemblyContextParticipant secondParticipant =
            RequireParticipant(group, second);
        AssemblyContextParticipant[] requested =
        [
            firstParticipant,
            secondParticipant,
        ];
        ImmutableArray<AssemblyContextSubject> subjects =
        [
            new(firstParticipant.Assembly),
            new(secondParticipant.Assembly),
        ];
        var available =
            ImmutableArray.CreateBuilder<AnalyzedParticipant>();
        var failures =
            ImmutableArray.CreateBuilder<AssemblyPairCallUseFailure>();
        foreach (AssemblyContextParticipant participant in requested)
        {
            switch (AssemblyContextCallGraphAnalysis.Execute(
                group,
                participant))
            {
                case AssemblyContextCallGraphAnalysisResult
                    .Available result:
                    available.Add(
                        new AnalyzedParticipant(
                            result.Participant.ContextParticipant,
                            result.Participant.Assembly,
                            result.Participant.CallGraph,
                            new AssemblyPairCallUseParticipant(
                                result.Participant.Subject,
                                result.Participant.CallGraph
                                    .ModuleIdentity.ModuleVersionId,
                                result.Participant.CallGraph.Diagnostics)));
                    break;
                case AssemblyContextCallGraphAnalysisResult
                    .Rejected rejected:
                    failures.Add(
                        new AssemblyPairCallUseFailure.Rejected(
                            rejected.Subject,
                            rejected.Failure));
                    break;
                case AssemblyContextCallGraphAnalysisResult
                    .InvalidImage invalid:
                    failures.Add(
                        new AssemblyPairCallUseFailure.InvalidImage(
                            invalid.Subject,
                            invalid.Error));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown call-graph analysis result.");
            }
        }

        if (available.Count != 2)
        {
            return new AssemblyPairCallUseResult(
                subjects,
                [.. available.Select(item => item.ResultParticipant)],
                failures.ToImmutable(),
                [],
                AssemblyPairCallUseDiagnostics.Empty);
        }

        AnalyzedParticipant firstAnalyzed = available[0];
        AnalyzedParticipant secondAnalyzed = available[1];
        if (firstAnalyzed.ResultParticipant.ModuleVersionId
                == secondAnalyzed.ResultParticipant.ModuleVersionId
            && firstAnalyzed.ResultParticipant.Subject.Identity
                .IsEquivalentTo(
                    secondAnalyzed.ResultParticipant.Subject.Identity))
        {
            throw new AssemblyPairCallUseRequestException(
                AssemblyPairCallUseRequestFailureKind
                    .SamePhysicalAssemblyArtifact,
                "Pairwise call use requires two distinct physical assembly artifacts.",
                nameof(second));
        }

        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            available.Select(item => (
                item.Assembly,
                item.Participant.BindingPolicy)));
        using var scope = new Analysis.CatalogCallGraphScope(
            policy,
            available.Select(item =>
                new Analysis.CatalogCallGraphParticipant(
                    item.CallGraph,
                    item.Assembly)));

        ImmutableArray<AssemblyPairCallUseOccurrence> occurrences =
        [
            .. scope
                .ResolvedCalls(
                    firstAnalyzed.CallGraph,
                    secondAnalyzed.CallGraph)
                .Concat(
                    scope.ResolvedCalls(
                        secondAnalyzed.CallGraph,
                        firstAnalyzed.CallGraph))
                .Where(call =>
                    call.Call.Kind is
                        Analysis.CallKind.Call
                        or Analysis.CallKind.CallVirtual
                        or Analysis.CallKind.NewObject)
                .Select(call =>
                {
                    AnalyzedParticipant source =
                        ReferenceEquals(
                            call.Source.CallGraph,
                            firstAnalyzed.CallGraph)
                            ? firstAnalyzed
                            : secondAnalyzed;
                    AnalyzedParticipant target =
                        ReferenceEquals(
                            call.Target.CallGraph,
                            firstAnalyzed.CallGraph)
                            ? firstAnalyzed
                            : secondAnalyzed;
                    return new AssemblyPairCallUseOccurrence(
                        source.ResultParticipant.Subject,
                        source.ResultParticipant.ModuleVersionId,
                        call.SourceMethod,
                        target.ResultParticipant.Subject,
                        target.ResultParticipant.ModuleVersionId,
                        call.TargetMethod,
                        call.Call);
                })
                .OrderBy(
                    occurrence => occurrence.Source.Identity.Name,
                    StringComparer.Ordinal)
                .ThenBy(occurrence =>
                    occurrence.SourceModuleVersionId)
                .ThenBy(occurrence =>
                    occurrence.SourceMethod.MetadataToken)
                .ThenBy(
                    occurrence => occurrence.Target.Identity.Name,
                    StringComparer.Ordinal)
                .ThenBy(occurrence =>
                    occurrence.TargetModuleVersionId)
                .ThenBy(occurrence =>
                    occurrence.TargetMethod.MetadataToken)
                .ThenBy(occurrence => occurrence.Call.ILOffset)
                .ThenBy(occurrence => occurrence.Call.OperandToken),
        ];
        var resolvedSites = occurrences
            .Select(PhysicalSite)
            .ToHashSet();
        int unresolvedCandidateCallCount =
            CountUnresolvedPairCandidates(
                firstAnalyzed,
                secondAnalyzed,
                resolvedSites)
            + CountUnresolvedPairCandidates(
                secondAnalyzed,
                firstAnalyzed,
                resolvedSites);

        return new AssemblyPairCallUseResult(
            subjects,
            [firstAnalyzed.ResultParticipant, secondAnalyzed.ResultParticipant],
            [],
            occurrences,
            new AssemblyPairCallUseDiagnostics(
                unresolvedCandidateCallCount));
    }

    static int CountUnresolvedPairCandidates(
        AnalyzedParticipant source,
        AnalyzedParticipant target,
        HashSet<PhysicalCallSite> resolvedSites) =>
        source.CallGraph.DirectCalls.Count(call =>
            IsAdmittedKind(call.Kind)
            && NamesTargetAssembly(
                call,
                target.ResultParticipant.Subject.Identity)
            && !resolvedSites.Contains(
                PhysicalSite(
                    source.ResultParticipant,
                    call)));

    static bool NamesTargetAssembly(
        Analysis.DirectCall call,
        AssemblyReferenceIdentity targetAssembly)
    {
        Analysis.TypeRef declaringType =
            Analysis.GenericMemberIdentity.OpenDeclaringType(
                call.Callee.DeclaringType);
        return declaringType.Resolution?.Origin
                is Analysis.TypeReferenceOrigin.AssemblyReference reference
            && IsPairRelevantAssemblyReference(
                reference.Assembly,
                targetAssembly);
    }

    internal static bool IsPairRelevantAssemblyReference(
        AssemblyReferenceIdentity reference,
        AssemblyReferenceIdentity target) =>
        (reference with { Version = target.Version })
            .IsEquivalentTo(target);

    static bool IsAdmittedKind(Analysis.CallKind kind) =>
        kind is
            Analysis.CallKind.Call
            or Analysis.CallKind.CallVirtual
            or Analysis.CallKind.NewObject;

    static PhysicalCallSite PhysicalSite(
        AssemblyPairCallUseOccurrence occurrence) =>
        PhysicalSite(occurrence.Source, occurrence.Call);

    static PhysicalCallSite PhysicalSite(
        AssemblyPairCallUseParticipant source,
        Analysis.DirectCall call) =>
        PhysicalSite(source.Subject, call);

    static PhysicalCallSite PhysicalSite(
        AssemblyContextSubject source,
        Analysis.DirectCall call) =>
        new(
            source.Registration,
            call.EvidenceMethod.ModuleVersionId,
            call.EvidenceMethod.MetadataToken,
            call.ILOffset,
            call.OperandToken);

    static AssemblyContextParticipant RequireParticipant(
        AssemblyContextGroup group,
        ResolvedAssemblyReference assembly) =>
        group.Participants.SingleOrDefault(
            participant => ReferenceEquals(
                participant.Assembly.Registration,
                assembly.Registration))
        ?? throw new AssemblyPairCallUseRequestException(
            AssemblyPairCallUseRequestFailureKind
                .ParticipantOutsideGroup,
            "The assembly does not belong to the context group.",
            nameof(assembly));

    sealed record AnalyzedParticipant(
        AssemblyContextParticipant Participant,
        ResolvedAssemblyReference Assembly,
        Analysis.LibraryCallGraphAnalysisResult CallGraph,
        AssemblyPairCallUseParticipant ResultParticipant);

    readonly record struct PhysicalCallSite(
        AssemblyAcquisitionRegistration Source,
        Guid ModuleVersionId,
        int EvidenceMethodToken,
        int ILOffset,
        int OperandToken);
}
