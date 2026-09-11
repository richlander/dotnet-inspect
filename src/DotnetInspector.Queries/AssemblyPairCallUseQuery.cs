using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>The requested pair cannot form one pairwise call-use query.</summary>
public sealed class AssemblyPairCallUseRequestException
    : ArgumentException
{
    internal AssemblyPairCallUseRequestException(
        string message,
        string parameterName)
        : base(message, parameterName)
    {
    }
}

/// <summary>
/// One participant that supplied a complete body index for a pairwise call-use
/// query.
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
            ImmutableArray.CreateBuilder<IndexedParticipant>();
        var failures =
            ImmutableArray.CreateBuilder<AssemblyPairCallUseFailure>();
        foreach (AssemblyContextParticipant participant in requested)
        {
            BuildResult result = BuildIndex(group, participant);
            if (result is BuildResult.Available indexed)
                available.Add(indexed.Participant);
            else if (result is BuildResult.Unavailable unavailable)
                failures.Add(unavailable.Failure);
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

        IndexedParticipant firstIndexed = available[0];
        IndexedParticipant secondIndexed = available[1];
        if (firstIndexed.ResultParticipant.ModuleVersionId
                == secondIndexed.ResultParticipant.ModuleVersionId
            && firstIndexed.ResultParticipant.Subject.Identity
                .IsEquivalentTo(
                    secondIndexed.ResultParticipant.Subject.Identity))
        {
            throw new AssemblyPairCallUseRequestException(
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
                    item.Index,
                    item.Assembly)));

        ImmutableArray<AssemblyPairCallUseOccurrence> occurrences =
        [
            .. scope
                .ResolvedCalls(
                    firstIndexed.Index,
                    secondIndexed.Index)
                .Concat(
                    scope.ResolvedCalls(
                        secondIndexed.Index,
                        firstIndexed.Index))
                .Where(call =>
                    call.Call.Kind is
                        Analysis.CallKind.Call
                        or Analysis.CallKind.CallVirtual
                        or Analysis.CallKind.NewObject)
                .Select(call =>
                {
                    IndexedParticipant source =
                        ReferenceEquals(
                            call.Source.Index,
                            firstIndexed.Index)
                            ? firstIndexed
                            : secondIndexed;
                    IndexedParticipant target =
                        ReferenceEquals(
                            call.Target.Index,
                            firstIndexed.Index)
                            ? firstIndexed
                            : secondIndexed;
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
                firstIndexed,
                secondIndexed,
                resolvedSites)
            + CountUnresolvedPairCandidates(
                secondIndexed,
                firstIndexed,
                resolvedSites);

        return new AssemblyPairCallUseResult(
            subjects,
            [firstIndexed.ResultParticipant, secondIndexed.ResultParticipant],
            [],
            occurrences,
            new AssemblyPairCallUseDiagnostics(
                unresolvedCandidateCallCount));
    }

    static int CountUnresolvedPairCandidates(
        IndexedParticipant source,
        IndexedParticipant target,
        HashSet<PhysicalCallSite> resolvedSites) =>
        source.Index.DirectCalls.Count(call =>
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
            "The assembly does not belong to the context group.",
            nameof(assembly));

    static BuildResult BuildIndex(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        AssemblyContextSubject subject =
            new(participant.Assembly);
        AssemblyImageAccessResult<BuildResult> access =
            group.UseSnapshot<BuildResult>(
                participant.Assembly,
                snapshot =>
                {
                    try
                    {
                        Analysis.LibraryBodyIndex index =
                            Analysis.LibraryBodyIndex
                                .OpenFromPrefetchedImage(
                                    participant.Assembly.Path
                                        ?? participant.Assembly
                                            .Identity.Name,
                                    snapshot.Content,
                                    Analysis.LibraryBodyAnalysisFeatures
                                        .MethodEvidence);
                        ResolvedAssemblyReference assembly =
                            snapshot.RetainAssemblyReference(
                                participant.Assembly);
                        return new BuildResult.Available(
                            new IndexedParticipant(
                                participant,
                                assembly,
                                index,
                                new AssemblyPairCallUseParticipant(
                                    subject,
                                    index.ModuleIdentity.ModuleVersionId,
                                    index.Diagnostics)));
                    }
                    catch (Exception exception)
                        when (MemberCallGraphSession
                            .IsInvalidImageException(exception))
                    {
                        return new BuildResult.Unavailable(
                            new AssemblyPairCallUseFailure.InvalidImage(
                                subject,
                                exception));
                    }
                });

        return access switch
        {
            AssemblyImageAccessResult<BuildResult>.Available available =>
                available.Value,
            AssemblyImageAccessResult<BuildResult>.Rejected rejected =>
                new BuildResult.Unavailable(
                    new AssemblyPairCallUseFailure.Rejected(
                        subject,
                        rejected.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown assembly image access result."),
        };
    }

    sealed record IndexedParticipant(
        AssemblyContextParticipant Participant,
        ResolvedAssemblyReference Assembly,
        Analysis.LibraryBodyIndex Index,
        AssemblyPairCallUseParticipant ResultParticipant);

    abstract record BuildResult
    {
        internal sealed record Available(
            IndexedParticipant Participant)
            : BuildResult;

        internal sealed record Unavailable(
            AssemblyPairCallUseFailure Failure)
            : BuildResult;
    }

    readonly record struct PhysicalCallSite(
        AssemblyAcquisitionRegistration Source,
        Guid ModuleVersionId,
        int EvidenceMethodToken,
        int ILOffset,
        int OperandToken);
}
