using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspector.Queries.Tests;

public sealed class FindQueryTests
{
    [Theory]
    [InlineData(
        FindQueryRouteKind.TypeResults,
        FindQuery.TypeRouteIdentity,
        FindQuery.TypeSubjectRole,
        FindQuery.TypeResultGrain,
        FindQuery.TypeRowSet)]
    [InlineData(
        FindQueryRouteKind.MemberResults,
        FindQuery.MemberRouteIdentity,
        FindQuery.MemberSubjectRole,
        FindQuery.MemberResultGrain,
        FindQuery.MemberRowSet)]
    public void Routes_ExposeDistinctResultsAndSharedRowCapabilities(
        FindQueryRouteKind kind,
        string identity,
        string subjectRole,
        string resultGrain,
        string rowSet)
    {
        IQueryOperationRoute route = FindQuery.Route(kind);

        Assert.Equal(identity, route.Identity);
        Assert.Equal(FindQuery.OperationIdentity, route.OperationIdentity);
        Assert.Equal(subjectRole, route.SubjectRole);
        Assert.Equal(resultGrain, route.ResultGrain);
        Assert.Equal([rowSet], route.RowSets);
        Assert.Equal(
            FindQuery.ResultRowsProfileIdentity,
            route.ProfileIdentity);
        Assert.Empty(route.Capabilities.Terms);
        Assert.Empty(route.Capabilities.Orders);
        Assert.Empty(route.Capabilities.Dimensions);
        Assert.Equal(
            [
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
                RowSelectionStageKind.Window,
            ],
            route.Capabilities.Stages);
    }

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void Routes_PreserveOrderedResultSelection(
        FindQueryRouteKind kind)
    {
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [
                        PortableQueryStage.Window(2, 5),
                        PortableQueryStage.Head(2),
                        PortableQueryStage.Tail(1),
                    ],
                    []),
                TestContext.Current.CancellationToken));

        Assert.Equal(kind, plan.RouteKind);
        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Tail,
            ],
            plan.Rows.Operations.Select(operation =>
                operation.Kind));
        Assert.Equal(
            [
                (RowSelectionStageKind.Window, 2, 5, 0),
                (RowSelectionStageKind.Head, null, null, 2),
                (RowSelectionStageKind.Tail, null, null, 1),
            ],
            plan.Rows.Operations.Select(Operation));
        Assert.Null(
            FindQuery.AssociateResultRows(
                plan,
                Question(kind)).DelegatedInputRows);

        static (
            RowSelectionStageKind Kind,
            int? Start,
            int? End,
            int Count) Operation(
            RowSelectionIntentOperation<string> operation) =>
            operation.Kind switch
            {
                RowSelectionStageKind.Window =>
                    (
                        operation.Kind,
                        operation.Start,
                        operation.End,
                        0),
                _ =>
                    (
                        operation.Kind,
                        null,
                        null,
                        operation.Count),
            };
    }

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void PureHead_AssociatesItsDelegatedInputRange(
        FindQueryRouteKind kind)
    {
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Head(3)],
                    []),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new FindInputRowSelection(1, 3),
            FindQuery.AssociateResultRows(
                plan,
                Question(kind)).DelegatedInputRows);
    }

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void FiniteWindow_AssociatesItsDelegatedInputRange(
        FindQueryRouteKind kind)
    {
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Window(2, 5)],
                    []),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new FindInputRowSelection(2, 5),
            FindQuery.AssociateResultRows(
                plan,
                Question(kind)).DelegatedInputRows);
    }

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void OpenEndedWindowAndTail_RemainExhaustive(
        FindQueryRouteKind kind)
    {
        FindQueryPlan openWindow = Accepted(
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Window(2, null)],
                    []),
                TestContext.Current.CancellationToken));
        FindQueryPlan tail = Accepted(
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Tail(2)],
                    []),
                TestContext.Current.CancellationToken));

        FindQuestion question = Question(kind);
        Assert.Null(
            FindQuery.AssociateResultRows(
                openWindow,
                question).DelegatedInputRows);
        Assert.Null(
            FindQuery.AssociateResultRows(
                tail,
                question).DelegatedInputRows);
    }

    [Fact]
    public void MemberFiniteWindow_AssociatesExactPlanAndDelegatedRange()
    {
        MemberFindQuestion question =
            MemberFindQuestion.Create(["Add*"], FindVisibility.Public);
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                FindQueryRouteKind.MemberResults,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Window(2, 5)],
                    []),
                TestContext.Current.CancellationToken));

        FindResultRowPlan associated =
            FindQuery.AssociateResultRows(plan, question);

        Assert.Same(plan, associated.Query);
        Assert.Equal(
            new FindInputRowSelection(2, 5),
            associated.DelegatedInputRows);
    }

    [Theory]
    [InlineData("Thing", false)]
    [InlineData("Thing*", true)]
    [InlineData("System.Thing", true)]
    public void TypeFiniteWindow_DelegatesOnlyWithoutImplicitMemberFallback(
        string pattern,
        bool delegates)
    {
        TypeFindQuestion question =
            TypeFindQuestion.Create([pattern], FindVisibility.Public);
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                FindQueryRouteKind.TypeResults,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Window(2, 5)],
                    []),
                TestContext.Current.CancellationToken));

        FindResultRowPlan associated =
            FindQuery.AssociateResultRows(plan, question);

        Assert.Same(plan, associated.Query);
        Assert.Equal(
            delegates ? new FindInputRowSelection(2, 5) : null,
            associated.DelegatedInputRows);
    }

    [Fact]
    public void TypeOnlyFiniteWindow_DelegatesWithoutImplicitMemberFallback()
    {
        TypeFindQuestion question =
            TypeFindQuestion.Create(["Thing"], FindVisibility.Public);
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                FindQueryRouteKind.TypeResults,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Window(2, 5)],
                    []),
                TestContext.Current.CancellationToken));

        FindResultRowPlan associated =
            FindQuery.AssociateResultRows(
                plan,
                question,
                TypeFindResultScope.TypeOnly);

        Assert.Same(plan, associated.Query);
        Assert.Equal(
            new FindInputRowSelection(2, 5),
            associated.DelegatedInputRows);
    }

    [Theory]
    [MemberData(nameof(ExhaustiveSelections))]
    public void ExhaustiveSelection_RetainsExactPlanWithoutDelegation(
        PortableQueryStage[] stages)
    {
        MemberFindQuestion question =
            MemberFindQuestion.Create(["Add*"], FindVisibility.Public);
        FindQueryPlan plan = Accepted(
            FindQuery.ResolveIntent(
                FindQueryRouteKind.MemberResults,
                PortableQueryIntent.Create([], [], stages, []),
                TestContext.Current.CancellationToken));

        FindResultRowPlan associated =
            FindQuery.AssociateResultRows(plan, question);

        Assert.Same(plan, associated.Query);
        Assert.Null(associated.DelegatedInputRows);
    }

    public static TheoryData<PortableQueryStage[]> ExhaustiveSelections =>
        new()
        {
            {
                new[] { PortableQueryStage.Tail(2) }
            },
            {
                new[] { PortableQueryStage.Window(2, null) }
            },
            {
                new[]
                {
                    PortableQueryStage.Head(5),
                    PortableQueryStage.Window(2, 3),
                }
            },
        };

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void Routes_DoNotWidenIntoPredicatesOrRanking(
        FindQueryRouteKind kind)
    {
        FindQueryPlanResult termResult =
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [
                        new(
                            "Visibility",
                            PortableQueryOperator.Equal,
                            "Public"),
                    ],
                    [],
                    [],
                    []),
                TestContext.Current.CancellationToken);
        FindQueryPlanResult topResult =
            FindQuery.ResolveIntent(
                kind,
                PortableQueryIntent.Create(
                    [],
                    [],
                    [PortableQueryStage.Top(1)],
                    []),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PortableQueryFailureReason.UnknownKey,
            Assert.IsType<FindQueryPlanResult.Rejected>(
                termResult).Failure.Reason);
        Assert.Equal(
            PortableQueryFailureReason.StageNotAdmitted,
            Assert.IsType<FindQueryPlanResult.Rejected>(
                topResult).Failure.Reason);
    }

    private static FindQueryPlan Accepted(
        FindQueryPlanResult result) =>
        Assert.IsType<FindQueryPlanResult.Accepted>(
            result).Plan;

    private static FindQuestion Question(
        FindQueryRouteKind kind) =>
        kind is FindQueryRouteKind.MemberResults
            ? MemberFindQuestion.Create(
                ["Thing*"],
                FindVisibility.Public)
            : TypeFindQuestion.Create(
                ["Thing*"],
                FindVisibility.Public);
}
