using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using ILInspector.Metadata;

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
    RowSelectionIntent<string> Rows);

/// <summary>
/// One exact owner-issued Find row plan associated with execution, plus an
/// optional input range whose producer-order equivalence has been proven.
/// </summary>
public sealed record FindResultRowPlan
{
    internal FindResultRowPlan(
        FindQueryPlan query,
        FindInputRowSelection? delegatedInputRows)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        DelegatedInputRows = delegatedInputRows;
        Operations =
        [
            .. query.Rows.Operations.Select(
                static operation => operation.Kind switch
                {
                    RowSelectionStageKind.Head
                        or RowSelectionStageKind.Tail =>
                        new FindResultRowOperation(
                            operation.Kind,
                            operation.Count,
                            Start: null,
                            End: null),
                    RowSelectionStageKind.Window =>
                        new FindResultRowOperation(
                            operation.Kind,
                            Count: null,
                            operation.Start,
                            operation.End),
                    _ => throw new InvalidOperationException(
                        "Find result rows contain an unsupported operation."),
                }),
        ];
    }

    [JsonIgnore]
    public FindQueryPlan Query { get; }
    public FindQueryRouteKind RouteKind => Query.RouteKind;
    public ImmutableArray<FindResultRowOperation> Operations { get; }
    public FindInputRowSelection? DelegatedInputRows { get; }
}

public sealed record FindResultRowOperation(
    RowSelectionStageKind Kind,
    int? Count,
    int? Start,
    int? End);

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
                resolution.Plan.Rows));
    }

    public static FindResultRowPlan AssociateResultRows(
        FindQueryPlan plan,
        FindQuestion question)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(question);
        FindQueryRouteKind questionRoute = question switch
        {
            TypeFindQuestion => FindQueryRouteKind.TypeResults,
            MemberFindQuestion => FindQueryRouteKind.MemberResults,
            _ => throw new ArgumentOutOfRangeException(nameof(question)),
        };
        if (plan.RouteKind != questionRoute)
        {
            throw new ArgumentException(
                "The Find query plan route does not match its question.",
                nameof(question));
        }

        FindInputRowSelection? delegatedInputRows =
            FiniteInputRows(plan.Rows) is not { } inputRows
                ? null
                : question switch
                {
                    MemberFindQuestion => inputRows,
                    TypeFindQuestion typeQuestion
                        when CanDelegateTypeInputRows(plan, typeQuestion) =>
                        inputRows,
                    _ => null,
                };
        return new(plan, delegatedInputRows);
    }

    private static bool CanDelegateTypeInputRows(
        FindQueryPlan plan,
        TypeFindQuestion question)
    {
        RowSelectionIntentOperation<string> operation =
            plan.Rows.Operations.Single();
        return operation.Kind == RowSelectionStageKind.Head
            || operation.Kind == RowSelectionStageKind.Window
                && question.Patterns.All(static pattern =>
                    !MayRunImplicitMemberFallback(pattern));
    }

    private static bool MayRunImplicitMemberFallback(
        TypeFindPattern pattern) =>
        pattern is TypeFindPattern.Ordinary
        && !pattern.Text.Contains('.')
        && TypeNameMatchRanking.IsBroadenable(pattern.Text);

    private static FindInputRowSelection? FiniteInputRows(
        RowSelectionIntent<string> rows)
    {
        if (rows.Operations.Count != 1)
            return null;
        RowSelectionIntentOperation<string> operation =
            rows.Operations[0];
        return operation.Kind switch
        {
            RowSelectionStageKind.Head =>
                new(1, operation.Count),
            RowSelectionStageKind.Window
                when operation.End is int end =>
                new(operation.Start ?? 1, end),
            _ => null,
        };
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
        RowSelectionIntent<string> Rows);

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
            return new(intent, rows);
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
