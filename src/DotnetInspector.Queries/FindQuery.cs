using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public enum FindQueryRouteKind
{
    TypeResults,
    MemberResults
}

public sealed record FindInputRowSelection
{
    public FindInputRowSelection(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(start);
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "The input-row end must not precede its start.");
        }

        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }
}

public sealed record FindQueryPlan(
    FindQueryRouteKind RouteKind,
    PortableQueryIntent Intent,
    RowSelectionIntent<string> Rows,
    FindInputRowSelection? InputRows);

public abstract record FindQueryPlanResult
{
    private FindQueryPlanResult()
    {
    }

    public sealed record Accepted(FindQueryPlan Plan)
        : FindQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : FindQueryPlanResult;
}

/// <summary>
/// Executable query registration for the Type and Member Find result routes.
/// </summary>
public static class FindQuery
{
    public const string OperationIdentity = "find";
    public const string TypeRouteIdentity = "find/type-results";
    public const string MemberRouteIdentity = "find/member-results";
    public const string TypeSubjectRole = "authorized-type-search-population";
    public const string MemberSubjectRole =
        "authorized-member-search-population";
    public const string TypeResultGrain = "type-find-result";
    public const string MemberResultGrain = "member-find-result";
    public const string TypeRowSet = "type-results";
    public const string MemberRowSet = "member-results";
    public const string ResultRowsProfileIdentity = "result-rows";

    public static IQueryOperationRoute Route(
        FindQueryRouteKind kind) =>
        RouteCore(kind);

    public static FindQueryPlanResult ResolveIntent(
        FindQueryRouteKind kind,
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<FindQueryCorePlan> resolution =
            RouteCore(kind).Resolve(intent, cancellationToken);
        if (!resolution.IsResolved)
            return new FindQueryPlanResult.Rejected(resolution.Failure);

        return new FindQueryPlanResult.Accepted(
            new(
                kind,
                resolution.Plan.Intent,
                resolution.Plan.Rows,
                resolution.Plan.InputRows));
    }

    private static QueryOperationRoute<
        FindQueryPredicate,
        FindQueryCorePlan> RouteCore(
        FindQueryRouteKind kind) =>
        kind switch
        {
            FindQueryRouteKind.TypeResults =>
                OperationRegistration.TypeRoute,
            FindQueryRouteKind.MemberResults =>
                OperationRegistration.MemberRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private sealed record FindQueryPredicate;

    private sealed record FindQueryCorePlan(
        PortableQueryIntent Intent,
        RowSelectionIntent<string> Rows,
        FindInputRowSelection? InputRows);

    private sealed class FindQueryVocabulary
        : PortableQueryVocabulary<
            FindQueryPredicate,
            FindQueryCorePlan>
    {
        public override string Identity => "find/v1";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                FindQueryPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                FindQueryPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            kind is RowSelectionStageKind.Head
                or RowSelectionStageKind.Tail
                or RowSelectionStageKind.Window;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override FindQueryCorePlan CreatePlan(
            PortableQueryResolvedIntent<FindQueryPredicate> resolved)
        {
            PortableQueryIntent intent =
                PortableQueryIntent.Create(
                    [],
                    [],
                    [.. resolved.Stages],
                    []);
            RowSelectionIntent<string> rows =
                RowSelectionIntent<string>.Create(
                    [
                        .. resolved.Stages.Select(static stage =>
                            stage.Kind switch
                            {
                                RowSelectionStageKind.Head =>
                                    RowSelectionIntentOperation<string>.Head(
                                        stage.Count),
                                RowSelectionStageKind.Tail =>
                                    RowSelectionIntentOperation<string>.Tail(
                                        stage.Count),
                                RowSelectionStageKind.Window =>
                                    RowSelectionIntentOperation<string>.Window(
                                        stage.Start,
                                        stage.End),
                                _ => throw new InvalidOperationException(
                                    "Find Query resolved an unsupported row "
                                        + $"stage '{stage.Kind}'."),
                            }),
                    ]);
            FindInputRowSelection? inputRows =
                resolved.Stages.Count == 1
                    ? resolved.Stages[0].Kind switch
                    {
                        RowSelectionStageKind.Head =>
                            new(1, resolved.Stages[0].Count),
                        RowSelectionStageKind.Window =>
                            resolved.Stages[0].End is int end
                                ? new(
                                    resolved.Stages[0].Start ?? 1,
                                    end)
                                : null,
                        _ => null,
                    }
                    : null;
            return new(intent, rows, inputRows);
        }
    }

    private static class OperationRegistration
    {
        private static readonly FindQueryVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            FindQueryPredicate,
            FindQueryCorePlan> Definition =
            QueryOperationDefinition<
                FindQueryPredicate,
                FindQueryCorePlan>.Create(
                OperationIdentity,
                Vocabulary,
                [TypeSubjectRole, MemberSubjectRole],
                [TypeResultGrain, MemberResultGrain],
                [TypeRowSet, MemberRowSet],
                [],
                [],
                [
                    new(
                        ResultRowsProfileIdentity,
                        [],
                        []),
                ]);

        internal static readonly QueryOperationRoute<
            FindQueryPredicate,
            FindQueryCorePlan> TypeRoute =
            CreateRoute(
                TypeRouteIdentity,
                TypeSubjectRole,
                TypeResultGrain,
                TypeRowSet);

        internal static readonly QueryOperationRoute<
            FindQueryPredicate,
            FindQueryCorePlan> MemberRoute =
            CreateRoute(
                MemberRouteIdentity,
                MemberSubjectRole,
                MemberResultGrain,
                MemberRowSet);

        private static QueryOperationRoute<
            FindQueryPredicate,
            FindQueryCorePlan> CreateRoute(
            string identity,
            string subjectRole,
            string resultGrain,
            string rowSet) =>
            QueryOperationRoute<
                FindQueryPredicate,
                FindQueryCorePlan>.Create(
                identity,
                Definition,
                subjectRole,
                resultGrain,
                [rowSet],
                ResultRowsProfileIdentity,
                [],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]);
    }
}
