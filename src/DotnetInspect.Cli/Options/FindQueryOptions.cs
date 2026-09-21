using QuerySpace;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

internal static class FindQueryOptions
{
    internal static string Section(
        FindQueryRouteKind kind)
    {
        string rowSet = FindQuery.Route(kind).RowSets.Single();
        return rowSet switch
        {
            FindQuery.TypeRowSet => "Results",
            FindQuery.MemberRowSet => "Members",
            _ => throw new InvalidOperationException(
                $"Find Query route '{kind}' exposes unknown row set "
                    + $"'{rowSet}'."),
        };
    }

    internal static bool TryResolve(
        FindQueryRouteKind kind,
        RowSelectionIntent<string>? rowSelection,
        out FindQueryPlan plan,
        out string? error)
    {
        PortableQueryIntent intent =
            PortableQueryIntent.Create(
                [],
                [],
                rowSelection is null
                    ? []
                    : [
                        .. rowSelection.Operations.Select(
                            ToPortableStage),
                    ],
                []);
        FindQueryPlanResult result =
            FindQuery.ResolveIntent(kind, intent);
        if (result is FindQueryPlanResult.Rejected rejected)
        {
            plan = null!;
            error =
                "Find Query could not resolve its registered result-row "
                + $"capabilities ({rejected.Failure.Reason}).";
            return false;
        }

        plan = ((FindQueryPlanResult.Accepted)result).Plan;
        error = null;
        return true;
    }

    private static PortableQueryStage ToPortableStage(
        RowSelectionIntentOperation<string> operation) =>
        operation.Kind switch
        {
            RowSelectionStageKind.Head =>
                PortableQueryStage.Head(operation.Count),
            RowSelectionStageKind.Tail =>
                PortableQueryStage.Tail(operation.Count),
            RowSelectionStageKind.Window =>
                PortableQueryStage.Window(
                    operation.Start,
                    operation.End),
            _ => throw new InvalidOperationException(
                "Find row selection contains an unsupported "
                    + $"'{operation.Kind}' stage."),
        };
}
