using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>The semantic leverage rank shared by every winner on one Type.</summary>
public sealed record TypeMethodLeverageRank(
    int DirectCallerCount,
    int RootReach,
    int Fanout,
    int LoopCallCount,
    int MaxDepth);

/// <summary>
/// One browsable member whose body selectors contain at least one winning
/// MethodDef.
/// </summary>
public sealed record TypeMethodLeverageWinner(
    string TypeDefinitionId,
    string StableSelector,
    ImmutableArray<int> MethodTokens);

/// <summary>
/// The complete Top Leverage designation for one Type, including winners that
/// have no browsable member anchor.
/// </summary>
public sealed record AssemblyTypeMethodLeverageInspection(
    string TypeDefinitionId,
    int MethodCount,
    int WinnerCount,
    TypeMethodLeverageRank? WinningRank,
    ImmutableArray<TypeMethodLeverageWinner> AnchoredWinners,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Ranks every method declared by one selected Type using whole-assembly call
/// evidence and attributes only exact winners to product-issued member anchors.
/// </summary>
public static class AssemblyContextTypeMethodLeverageQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>>
        Definition { get; } =
        new(
            "Assembly context type method leverage",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<
        AssemblyTypeMethodLeverageInspection> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string typeDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);

        AssemblyContextEntry<SelectedType> selected =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                participant,
                session => SelectType(session, typeDefinitionId));
        return selected switch
        {
            AssemblyContextEntry<SelectedType>.Rejected rejected =>
                new AssemblyContextEntry<
                    AssemblyTypeMethodLeverageInspection>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<SelectedType>.Failed failed =>
                new AssemblyContextEntry<
                    AssemblyTypeMethodLeverageInspection>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<SelectedType>.Available available =>
                AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
                    group,
                    participant,
                    (subject, snapshot) =>
                    {
                        if (!ReferenceEquals(
                                subject.Registration,
                                available.Subject.Registration))
                        {
                            throw new InspectionQueryException(
                                "Type method-leverage selection belongs to "
                                    + "another assembly-context participant.");
                        }
                        return Analyze(
                            group,
                            subject,
                            snapshot,
                            available.Value);
                    }),
            _ => throw new InvalidOperationException(
                $"Unknown Type method-leverage selection outcome "
                    + $"'{selected.GetType().Name}'."),
        };
    }

    static AssemblyTypeMethodLeverageInspection Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        SelectedType selected)
    {
        var resolver = AssemblyContextAnalysisSource.Resolver(group, subject);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence),
                resolver);
        try
        {
            ImmutableArray<MethodLeverage> ranked =
                analysis.Leverage.Top(
                    int.MaxValue,
                    method => AnalysisApiCorrespondence.IsSameType(
                        method.DeclaringType,
                        selected.Type));
            ImmutableArray<MethodLeverage> winners =
                Winners(ranked);
            TypeMethodLeverageRank? winningRank =
                winners.IsEmpty ? null : Rank(winners[0]);
            ImmutableHashSet<int> methodTokens =
            [
                .. ranked.Select(method => method.Method.MetadataToken),
            ];
            var result = new AssemblyTypeMethodLeverageInspection(
                selected.TypeDefinitionId,
                ranked.Length,
                winners.Length,
                winningRank,
                AttributeWinners(
                    selected.TypeDefinitionId,
                    winners,
                    selected.MembersByBodyToken),
                [
                    .. analysis.Leverage.Receipt.Diagnostics.Where(
                        diagnostic =>
                            methodTokens.Contains(diagnostic.MethodToken)
                            || diagnostic.SourceMethodToken is { } source
                                && methodTokens.Contains(source)),
                ],
                selected.InspectionFailures);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            analysis.CallGraph.ReleaseCaches();
        }
    }

    internal static ImmutableArray<MethodLeverage> Winners(
        ImmutableArray<MethodLeverage> ranked)
    {
        if (ranked.IsEmpty || ranked[0].DirectCallerCount == 0)
            return [];

        TypeMethodLeverageRank maximum = Rank(ranked[0]);
        return
        [
            .. ranked.TakeWhile(candidate =>
                SemanticRank(candidate) == SemanticRank(maximum)),
        ];
    }

    static TypeMethodLeverageRank Rank(MethodLeverage leverage) =>
        new(
            leverage.DirectCallerCount,
            leverage.RootReach,
            leverage.Fanout,
            leverage.LoopCallCount,
            leverage.MaxDepth);

    static (
        int DirectCallerCount,
        int RootReach,
        int Fanout,
        int LoopCallCount) SemanticRank(MethodLeverage leverage) =>
        (
            leverage.DirectCallerCount,
            leverage.RootReach,
            leverage.Fanout,
            leverage.LoopCallCount);

    static (
        int DirectCallerCount,
        int RootReach,
        int Fanout,
        int LoopCallCount) SemanticRank(TypeMethodLeverageRank leverage) =>
        (
            leverage.DirectCallerCount,
            leverage.RootReach,
            leverage.Fanout,
            leverage.LoopCallCount);

    static ImmutableArray<TypeMethodLeverageWinner> AttributeWinners(
        string typeDefinitionId,
        ImmutableArray<MethodLeverage> winners,
        IReadOnlyDictionary<int, ImmutableArray<SelectedMember>> byBodyToken)
    {
        return
        [
            .. winners
                .SelectMany(winner =>
                    byBodyToken
                        .GetValueOrDefault(
                            winner.Method.MetadataToken,
                            [])
                        .Select(member => new
                        {
                            Member = member,
                            Token = winner.Method.MetadataToken,
                        }))
                .GroupBy(
                    item => item.Member.StableSelector,
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new TypeMethodLeverageWinner(
                    typeDefinitionId,
                    group.Key,
                    [
                        .. group.Select(item => item.Token)
                            .Distinct()
                            .Order(),
                    ])),
        ];
    }

    static SelectedType SelectType(
        AssemblyInspectionSession session,
        string typeDefinitionId)
    {
        ApiSurface surface =
            session.ApiSurface(ApiSurfaceExtractionScope.IncludeAll);
        ApiType[] matchingTypes =
        [
            .. surface.Types.Where(type =>
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)
                    == typeDefinitionId),
        ];
        if (matchingTypes.Length != 1)
        {
            throw new InspectionQueryException(
                matchingTypes.Length == 0
                    ? $"API Type '{typeDefinitionId}' was not found."
                    : $"API Type '{typeDefinitionId}' is ambiguous.");
        }

        ApiType type = matchingTypes[0];
        var byBodyToken =
            new Dictionary<int, List<SelectedMember>>();
        var typeTokens = new HashSet<int>();
        if (type.MetadataToken is { } typeToken)
            typeTokens.Add(typeToken);
        foreach (ApiMember member in type.Members)
        {
            if (member.MetadataToken is { } memberToken)
                typeTokens.Add(memberToken);
            var selectedMember = new SelectedMember(
                ApiMemberIdentity.GetMemberAnchor(type, member)
                    .StableSelector);
            foreach (int bodyToken
                in CallGraphMemberResolver.CreateBodySelectors(type, member)
                    .Select(selector => selector.BodyToken)
                    .Distinct())
            {
                typeTokens.Add(bodyToken);
                if (!byBodyToken.TryGetValue(
                        bodyToken,
                        out List<SelectedMember>? owners))
                {
                    owners = [];
                    byBodyToken.Add(bodyToken, owners);
                }
                if (!owners.Contains(selectedMember))
                    owners.Add(selectedMember);
            }
        }

        return new SelectedType(
            typeDefinitionId,
            type,
            byBodyToken.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderBy(
                        member => member.StableSelector,
                        StringComparer.Ordinal)
                    .ToImmutableArray()),
            [
                .. surface.InspectionFailures.Where(failure =>
                    typeTokens.Contains(failure.SubjectToken)),
            ]);
    }

    sealed record SelectedType(
        string TypeDefinitionId,
        ApiType Type,
        IReadOnlyDictionary<int, ImmutableArray<SelectedMember>>
            MembersByBodyToken,
        ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

    sealed record SelectedMember(string StableSelector);
}
