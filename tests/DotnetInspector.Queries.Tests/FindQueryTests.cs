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
        Assert.Null(plan.InputRows);

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
    public void PureHead_ExposesItsAcceptedInputRows(
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
            plan.InputRows);
    }

    [Theory]
    [InlineData(FindQueryRouteKind.TypeResults)]
    [InlineData(FindQueryRouteKind.MemberResults)]
    public void FiniteWindow_ExposesItsAcceptedInputRows(
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
            plan.InputRows);
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

        Assert.Null(openWindow.InputRows);
        Assert.Null(tail.InputRows);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void InputRows_RejectInvalidBounds(int start, int end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FindInputRowSelection(start, end));
    }

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
}
