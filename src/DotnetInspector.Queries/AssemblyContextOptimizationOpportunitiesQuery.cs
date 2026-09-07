using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public sealed record OptimizationOpportunityPublicMember(
    string Type,
    string Member,
    string StableSelector,
    ImmutableArray<int> BodyTokens);

public sealed record AssemblyOptimizationOpportunityMember(
    OptimizationOpportunityMemberRanking Ranking,
    OptimizationOpportunityPublicMember? PublicMember);

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
            .Where(member => member.PublicMember is null)
            .Sum(member => member.Ranking.Opportunities.Length);
}

public sealed record AssemblyContextOptimizationOpportunityMember(
    AssemblyContextSubject Subject,
    AssemblyOptimizationOpportunityMember Member);

/// <summary>
/// Ranked Analysis opportunities for one binding-consistent assembly context group.
/// </summary>
/// <remarks>
/// The query owns every whole-assembly Analysis index, joins method bodies to the
/// public API surface through product body selectors, and retains participant
/// rejection or failure beside healthy rankings. Ordering, public-member
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
        AssemblyContextResult<AssemblyOptimizationPublicMembers>
            publicMembers = AssemblyContextQueryExecutor.Execute(
                group,
                ProjectPublicMembers);
        return Execute(group, publicMembers);
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
        var publicMembers =
            new AssemblyContextResult<AssemblyOptimizationPublicMembers>(
            [
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    ProjectPublicMembers),
            ]);
        return Execute(group, [participant], publicMembers);
    }

    internal static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        AssemblyContextResult<AssemblyOptimizationPublicMembers>
            publicMembers)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Execute(group, group.Participants, publicMembers);
    }

    static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        IReadOnlyList<AssemblyContextParticipant> participants,
        AssemblyContextResult<AssemblyOptimizationPublicMembers> publicMembers)
    {
        ArgumentNullException.ThrowIfNull(publicMembers);
        if (publicMembers.Assemblies.Length
            != participants.Count)
        {
            throw new InspectionQueryException(
                "Assembly context public members did not produce one result per participant.");
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
            AssemblyContextEntry<AssemblyOptimizationPublicMembers>
                projectedPublicMembers =
                    publicMembers.Assemblies[index];
            EnsureSameParticipant(
                participant,
                projectedPublicMembers.Subject);
            entries.Add(
                projectedPublicMembers switch
                {
                    AssemblyContextEntry<
                        AssemblyOptimizationPublicMembers>.Rejected
                        rejected =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Rejected(
                                rejected.Subject,
                                rejected.Failure),
                    AssemblyContextEntry<
                        AssemblyOptimizationPublicMembers>.Failed failed =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Failed(
                                failed.Subject,
                                failed.Error),
                    AssemblyContextEntry<
                        AssemblyOptimizationPublicMembers>.Available
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
                        $"Unknown public-member entry '{projectedPublicMembers.GetType().Name}'."),
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
        AssemblyOptimizationPublicMembers publicMembers)
    {
        LibraryBodyIndex? index = null;
        try
        {
            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            index = LibraryBodyIndex.OpenFromPrefetchedImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                resolver);

            ImmutableArray<
                OptimizationOpportunityMemberRanking> rankings =
                OptimizationOpportunityRanking.RankMembers(
                    index.OptimizationOpportunities.Where(
                        opportunity =>
                            OptimizationOpportunityRanking
                                .IncludePerformanceOpportunity(
                                    opportunity,
                                    index.GeneratedFrameworkTypes)));
            var result = new AssemblyOptimizationOpportunityRanking(
                AggregatePublicMembers(
                    rankings,
                    publicMembers.Members),
                index.GeneratedFrameworkTypes.ToImmutableHashSet(),
                index.Diagnostics,
                publicMembers.InspectionFailures);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            index?.ReleaseCallGraphCaches();
        }
    }

    static AssemblyOptimizationPublicMembers ProjectPublicMembers(
        AssemblyInspectionSession session)
    {
        ApiSurface surface =
            session.ApiSurface(ApiSurfaceExtractionScope.Public);
        var members =
            new Dictionary<
                int,
                OptimizationOpportunityPublicMember>();
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

                OptimizationOpportunityPublicMember publicMember =
                    PublicMember(
                        type,
                        member);
                foreach (CallGraphMemberBodySelector selector
                    in selectors)
                {
                    members.TryAdd(
                        selector.BodyToken,
                        publicMember);
                }
            }
        }

        return new AssemblyOptimizationPublicMembers(
            members,
            [.. surface.InspectionFailures]);
    }

    static OptimizationOpportunityPublicMember PublicMember(
        ApiType type,
        ApiMember member)
    {
        MemberAnchor anchor =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        return
            new OptimizationOpportunityPublicMember(
                AssemblyContextApiSurfaceQuery
                    .MetadataTypeIdentity(type),
                member.Name,
                anchor.StableSelector,
                []);
    }

    static ImmutableArray<AssemblyOptimizationOpportunityMember>
        AggregatePublicMembers(
            ImmutableArray<OptimizationOpportunityMemberRanking>
                rankings,
            IReadOnlyDictionary<
                int,
                OptimizationOpportunityPublicMember> publicMembers)
    {
        AssemblyOptimizationOpportunityMember[] projected =
        [
            .. rankings.Select(ranking =>
                new AssemblyOptimizationOpportunityMember(
                    ranking,
                    publicMembers.GetValueOrDefault(
                        ranking.Method.MetadataToken))),
        ];
        IEnumerable<AssemblyOptimizationOpportunityMember>
            nonPublic = projected.Where(
                member => member.PublicMember is null);
        IEnumerable<AssemblyOptimizationOpportunityMember>
            publicRankings = projected
                .Where(member => member.PublicMember is not null)
                .GroupBy(member => new PublicMemberKey(
                    member.PublicMember!.Type,
                    member.PublicMember.StableSelector))
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
                        leading.PublicMember! with
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
            .. nonPublic
                .Concat(publicRankings)
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

    internal sealed record AssemblyOptimizationPublicMembers(
        IReadOnlyDictionary<
            int,
            OptimizationOpportunityPublicMember> Members,
        ImmutableArray<ApiSurfaceInspectionFailure>
            InspectionFailures);

    readonly record struct PublicMemberKey(
        string Type,
        string StableSelector);
}
