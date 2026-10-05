using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

public sealed record OptimizationOpportunityDeclaredMember(
    string Type,
    string Member,
    string StableSelector,
    string Accessibility,
    ImmutableArray<int> BodyTokens);

public sealed record AssemblyOptimizationOpportunityMember(
    OptimizationOpportunityMemberRanking Ranking,
    OptimizationOpportunityDeclaredMember? DeclaredMember);

public sealed record AssemblyOptimizationOpportunityRanking(
    ImmutableArray<AssemblyOptimizationOpportunityMember> Members,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure>
        ApiSurfaceInspectionFailures)
{
    public int TotalOpportunities =>
        Members.Sum(member => member.Ranking.Opportunities.Length);
}

public sealed record AssemblyContextOptimizationOpportunityMember(
    AssemblyContextSubject Subject,
    AssemblyOptimizationOpportunityMember Member);

/// <summary>
/// Ranked Analysis opportunities for one binding-consistent assembly context group.
/// </summary>
/// <remarks>
/// The query owns every whole-assembly Analysis execution, joins method bodies to the
/// all-accessibility declared-member surface through product body selectors, and retains
/// participant rejection or failure beside healthy rankings. Ordering, declared-member
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
        AssemblyContextResult<AssemblyOptimizationDeclaredMembers>
            declaredMembers = AssemblyContextQueryExecutor.Execute(
                group,
                ProjectDeclaredMembers);
        return Execute(group, declaredMembers);
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
        var declaredMembers =
            new AssemblyContextResult<AssemblyOptimizationDeclaredMembers>(
            [
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    ProjectDeclaredMembers),
            ]);
        return Execute(group, [participant], declaredMembers);
    }

    internal static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        AssemblyContextResult<AssemblyOptimizationDeclaredMembers>
            declaredMembers)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Execute(group, group.Participants, declaredMembers);
    }

    static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group,
        IReadOnlyList<AssemblyContextParticipant> participants,
        AssemblyContextResult<AssemblyOptimizationDeclaredMembers> declaredMembers)
    {
        ArgumentNullException.ThrowIfNull(declaredMembers);
        if (declaredMembers.Assemblies.Length
            != participants.Count)
        {
            throw new InspectionQueryException(
                "Assembly context declared members did not produce one result per participant.");
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
            AssemblyContextEntry<AssemblyOptimizationDeclaredMembers>
                projectedDeclaredMembers =
                    declaredMembers.Assemblies[index];
            EnsureSameParticipant(
                participant,
                projectedDeclaredMembers.Subject);
            entries.Add(
                projectedDeclaredMembers switch
                {
                    AssemblyContextEntry<
                        AssemblyOptimizationDeclaredMembers>.Rejected
                        rejected =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Rejected(
                                rejected.Subject,
                                rejected.Failure),
                    AssemblyContextEntry<
                        AssemblyOptimizationDeclaredMembers>.Failed failed =>
                        new AssemblyContextEntry<
                            AssemblyOptimizationOpportunityRanking>.Failed(
                                failed.Subject,
                                failed.Error),
                    AssemblyContextEntry<
                        AssemblyOptimizationDeclaredMembers>.Available
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
                        $"Unknown declared-member entry '{projectedDeclaredMembers.GetType().Name}'."),
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
        AssemblyOptimizationDeclaredMembers declaredMembers)
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
                AggregateDeclaredMembers(
                    rankings,
                    declaredMembers.Members),
                optimization.GeneratedFrameworkTypes,
                execution.Receipt.Diagnostics,
                declaredMembers.InspectionFailures);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            execution?.CallGraph.ReleaseCaches();
        }
    }

    static AssemblyOptimizationDeclaredMembers ProjectDeclaredMembers(
        AssemblyInspectionSession session)
    {
        ApiSurface surface =
            session.CompatibilityApiSurface(
                ApiSurfaceExtractionScope.IncludeAll);
        var members =
            new Dictionary<
                int,
                OptimizationOpportunityDeclaredMember>();
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

                OptimizationOpportunityDeclaredMember declaredMember =
                    DeclaredMember(
                        type,
                        member);
                foreach (CallGraphMemberBodySelector selector
                    in selectors)
                {
                    members.TryAdd(
                        selector.BodyToken,
                        declaredMember);
                }
            }
        }

        return new AssemblyOptimizationDeclaredMembers(
            members,
            [.. surface.InspectionFailures]);
    }

    static OptimizationOpportunityDeclaredMember DeclaredMember(
        ApiType type,
        ApiMember member)
    {
        MemberAnchor anchor =
            ApiMemberIdentity.GetMemberAnchor(type, member);
        return
            new OptimizationOpportunityDeclaredMember(
                AssemblyContextApiSurfaceQuery
                    .MetadataTypeIdentity(type),
                member.Name,
                anchor.StableSelector,
                ApiAccessibility.Classify(member.Accessibility).Id,
                []);
    }

    static ImmutableArray<AssemblyOptimizationOpportunityMember>
        AggregateDeclaredMembers(
            ImmutableArray<OptimizationOpportunityMemberRanking>
                rankings,
            IReadOnlyDictionary<
                int,
                OptimizationOpportunityDeclaredMember> declaredMembers)
    {
        AssemblyOptimizationOpportunityMember[] projected =
        [
            .. rankings.Select(ranking =>
                new AssemblyOptimizationOpportunityMember(
                    ranking,
                    declaredMembers.GetValueOrDefault(
                        ranking.Method.MetadataToken))),
        ];
        IEnumerable<AssemblyOptimizationOpportunityMember>
            unattributed = projected.Where(
                member => member.DeclaredMember is null);
        IEnumerable<AssemblyOptimizationOpportunityMember>
            declaredRankings = projected
                .Where(member => member.DeclaredMember is not null)
                .GroupBy(member => new DeclaredMemberKey(
                    member.DeclaredMember!.Type,
                    member.DeclaredMember.StableSelector))
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
                        leading.DeclaredMember! with
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
                .Concat(declaredRankings)
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

    internal sealed record AssemblyOptimizationDeclaredMembers(
        IReadOnlyDictionary<
            int,
            OptimizationOpportunityDeclaredMember> Members,
        ImmutableArray<ApiSurfaceInspectionFailure>
            InspectionFailures);

    readonly record struct DeclaredMemberKey(
        string Type,
        string StableSelector);
}
