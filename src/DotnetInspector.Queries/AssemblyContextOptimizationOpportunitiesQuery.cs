using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public sealed record OptimizationOpportunityMemberSurface(
    string Type,
    string Member,
    string StableSelector,
    bool IsPublic,
    ApiType DeclaringType,
    ImmutableArray<int> BodyTokens);

public sealed record AssemblyOptimizationOpportunityMember(
    OptimizationOpportunityMemberRanking Ranking,
    OptimizationOpportunityMemberSurface? Member);

public sealed record AssemblyOptimizationOpportunityRanking(
    ImmutableArray<AssemblyOptimizationOpportunityMember> Members,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure>
        ApiSurfaceInspectionFailures)
{
    public int TotalOpportunities =>
        Members.Sum(member => member.Ranking.Opportunities.Length);

    public int NonPublicOpportunities =>
        Members
            .Where(member => member.Member?.IsPublic != true)
            .Sum(member => member.Ranking.Opportunities.Length);
}

public sealed record AssemblyContextOptimizationOpportunityMember(
    AssemblyContextSubject Subject,
    AssemblyOptimizationOpportunityMember Member);

/// <summary>
/// Ranked Analysis opportunities for one binding-consistent assembly context group.
/// </summary>
/// <remarks>
/// The query owns every whole-assembly Analysis execution, joins method bodies to the
/// all-accessibility API surface through product body selectors, and retains participant
/// rejection or failure beside healthy rankings. Ordering, member
/// attribution, and sequential execution are gated by
/// <c>AssemblyContextOptimizationOpportunitiesQueryTests</c>.
/// </remarks>
public sealed record AssemblyContextOptimizationOpportunitiesResult(
    AssemblyContextResult<AssemblyOptimizationOpportunityRanking> Assemblies,
    ImmutableArray<AssemblyContextOptimizationOpportunityMember>
        RankedMembers)
{
    public bool IsComplete => Assemblies.IsComplete;

    public int TotalOpportunities =>
        Assemblies.Assemblies
            .OfType<
                AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Available>()
            .Sum(entry => entry.Value.TotalOpportunities);

    public int NonPublicOpportunities =>
        Assemblies.Assemblies
            .OfType<
                AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Available>()
            .Sum(entry => entry.Value.NonPublicOpportunities);
}

public static class AssemblyContextOptimizationOpportunitiesQuery
{
    public static InspectionQuery<
        AssemblyContextOptimizationOpportunitiesResult> Definition { get; } =
        new(
            "Assembly context optimization opportunities",
            InspectionCost.Unbounded);

    public static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        AssemblyContextResult<AssemblyOptimizationMembers>
            members = AssemblyContextQueryExecutor.Execute(
                group,
                ProjectMembers);
        return Execute(group, members);
    }

    /// <summary>
    /// Ranks one participant without inspecting unrelated group participants. The group's
    /// binding policy remains available for resolving the selected participant's dependencies.
    /// </summary>
    public static AssemblyContextOptimizationOpportunitiesResult ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        var members =
            new AssemblyContextResult<AssemblyOptimizationMembers>(
            [
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    ProjectMembers),
            ]);
        return Execute(group, [participant], members);
    }

    internal static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        AssemblyContextResult<AssemblyOptimizationMembers> members)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Execute(group, group.Participants, members);
    }

    static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        IReadOnlyList<AssemblyContextParticipant> participants,
        AssemblyContextResult<AssemblyOptimizationMembers> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Assemblies.Length
            != participants.Count)
        {
            throw new InspectionQueryException(
                "Assembly context members did not produce one result per participant.");
        }

        var entries =
            ImmutableArray.CreateBuilder<
                AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>>(
                        participants.Count);
        for (int index = 0;
            index < participants.Count;
            index++)
        {
            AssemblyContextParticipant participant =
                participants[index];
            AssemblyContextEntry<AssemblyOptimizationMembers>
                projectedMembers =
                    members.Assemblies[index];
            EnsureSameParticipant(
                participant,
                projectedMembers.Subject);
            entries.Add(
                projectedMembers switch
                {
                    AssemblyContextEntry<
                        AssemblyOptimizationMembers>.Rejected
                        rejected =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Rejected(
                                rejected.Subject,
                                rejected.Failure),
                    AssemblyContextEntry<
                        AssemblyOptimizationMembers>.Failed failed =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Failed(
                                failed.Subject,
                                failed.Error),
                    AssemblyContextEntry<
                        AssemblyOptimizationMembers>.Available
                        available =>
                        AssemblyContextQueryExecutor
                            .ExecuteParticipantOverSnapshot(
                                group,
                                participant,
                                (subject, snapshot) => Analyze(
                                    group,
                                    subject,
                                    snapshot,
                                    available.Value)),
                    _ => throw new InvalidOperationException(
                        $"Unknown member entry '{projectedMembers.GetType().Name}'."),
                });
        }

        var assemblies =
            new AssemblyContextResult<
                AssemblyOptimizationOpportunityRanking>(
                    entries.MoveToImmutable());
        return new AssemblyContextOptimizationOpportunitiesResult(
            assemblies,
            RankAcrossGroup(assemblies));
    }

    static AssemblyOptimizationOpportunityRanking Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyOptimizationMembers members)
    {
        LibraryBodyAnalysisExecution? execution = null;
        try
        {
            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            LibraryBodyAnalysisRequest request =
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities);
            execution = LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                request,
                resolver);

            LibraryOptimizationAnalysisResult optimization =
                execution.Optimization;
            ImmutableArray<
                OptimizationOpportunityMemberRanking> rankings =
                OptimizationOpportunityRanking.RankMembers(
                    optimization.Opportunities.Where(
                        opportunity =>
                            OptimizationOpportunityRanking
                                .IncludePerformanceOpportunity(
                                    opportunity,
                                    optimization.GeneratedFrameworkTypes)
                            && OptimizationOpportunityRanking
                                .IncludeInMemberTriage(
                                    opportunity)));
            var result = new AssemblyOptimizationOpportunityRanking(
                AggregateMembers(
                    rankings,
                    members.ByBodyToken),
                optimization.GeneratedFrameworkTypes,
                execution.Receipt.Diagnostics,
                members.InspectionFailures);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            execution?.CallGraph.ReleaseCaches();
        }
    }

    static AssemblyOptimizationMembers ProjectMembers(
        AssemblyInspectionSession session)
    {
        ApiSurface surface =
            session.CompatibilityApiSurface(
                ApiSurfaceExtractionScope.IncludeAll);
        var members =
            new Dictionary<
                int,
                OptimizationOpportunityMemberSurface>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                ImmutableArray<CallGraphMemberBodySelector> selectors =
                [
                    .. CallGraphMemberResolver.CreateBodySelectors(
                        type,
                        member),
                ];
                if (selectors.Length == 0)
                    continue;

                OptimizationOpportunityMemberSurface surfaceMember =
                    SurfaceMember(
                        type,
                        member);
                foreach (CallGraphMemberBodySelector selector
                    in selectors)
                {
                    members.TryAdd(
                        selector.BodyToken,
                        surfaceMember);
                }
            }
        }

        return new AssemblyOptimizationMembers(
            members,
            [.. surface.InspectionFailures]);
    }

    static OptimizationOpportunityMemberSurface SurfaceMember(
        ApiType type,
        ApiMember member)
    {
        MemberAnchor anchor =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        return
            new OptimizationOpportunityMemberSurface(
                AssemblyContextApiSurfaceQuery
                    .MetadataTypeIdentity(type),
                member.Name,
                anchor.StableSelector,
                type.Accessibility is null
                    && member.Accessibility is null,
                type,
                []);
    }

    static ImmutableArray<AssemblyOptimizationOpportunityMember>
        AggregateMembers(
            ImmutableArray<OptimizationOpportunityMemberRanking>
                rankings,
            IReadOnlyDictionary<
                int,
                OptimizationOpportunityMemberSurface> members)
    {
        AssemblyOptimizationOpportunityMember[] projected =
        [
            .. rankings.Select(ranking =>
                new AssemblyOptimizationOpportunityMember(
                    ranking,
                    members.GetValueOrDefault(
                        ranking.Method.MetadataToken))),
        ];
        IEnumerable<AssemblyOptimizationOpportunityMember>
            unattributed = projected.Where(
                member => member.Member is null);
        IEnumerable<AssemblyOptimizationOpportunityMember>
            attributed = projected
                .Where(member => member.Member is not null)
                .GroupBy(member => new MemberKey(
                    member.Member!.Type,
                    member.Member.StableSelector))
                .Select(group =>
                {
                    AssemblyOptimizationOpportunityMember leading =
                        group.OrderBy(
                                member => member.Ranking,
                                OptimizationOpportunityRanking
                                    .MemberComparer)
                            .First();
                    return new AssemblyOptimizationOpportunityMember(
                        OptimizationOpportunityRanking.RankMember(
                            leading.Ranking.Method,
                            group.SelectMany(
                                member =>
                                    member.Ranking.Opportunities)),
                        leading.Member! with
                        {
                            BodyTokens =
                            [
                                .. group
                                    .SelectMany(member =>
                                        member.Ranking.Opportunities)
                                    .Select(opportunity =>
                                        opportunity.EvidenceMethodToken
                                        ?? opportunity.Method.MetadataToken)
                                    .Distinct()
                                    .Order(),
                            ],
                        });
                });
        return
        [
            .. unattributed
                .Concat(attributed)
                .OrderBy(
                    member => member.Ranking,
                    OptimizationOpportunityRanking.MemberComparer),
        ];
    }

    static ImmutableArray<
        AssemblyContextOptimizationOpportunityMember>
        RankAcrossGroup(
            AssemblyContextResult<
                AssemblyOptimizationOpportunityRanking> assemblies)
        =>
        [
            .. assemblies.Assemblies
                .OfType<
                    AssemblyContextEntry<
                        AssemblyOptimizationOpportunityRanking>.Available>()
                .SelectMany(
                    entry => entry.Value.Members.Select(
                        member =>
                            new AssemblyContextOptimizationOpportunityMember(
                                entry.Subject,
                                member)))
                .OrderBy(
                    member => member.Member.Ranking,
                    OptimizationOpportunityRanking.MemberComparer),
        ];

    static void EnsureSameParticipant(
        AssemblyContextParticipant participant,
        AssemblyContextSubject subject)
    {
        if (!ReferenceEquals(
                participant.Assembly.Registration,
                subject.Registration))
        {
            throw new InspectionQueryException(
                "Assembly context API surface result order does not match the group participants.");
        }
    }

    internal sealed record AssemblyOptimizationMembers(
        IReadOnlyDictionary<
            int,
            OptimizationOpportunityMemberSurface> ByBodyToken,
        ImmutableArray<ApiSurfaceInspectionFailure>
            InspectionFailures);

    readonly record struct MemberKey(
        string Type,
        string StableSelector);
}
