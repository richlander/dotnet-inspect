using QuerySpace.Rows;

namespace QuerySpace;

public static class PortableQueryRowSelection
{
    public static IReadOnlyList<PortableQueryStage> ToStages(
        RowSelectionIntent<string>? rowSelection) =>
        rowSelection is null
            ? []
            :
            [
                .. rowSelection.Operations.Select(operation =>
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
                        RowSelectionStageKind.Top =>
                            PortableQueryStage.Top(operation.Count),
                        _ => throw new InvalidOperationException(
                            "Unknown row-selection stage."),
                    }),
            ];
}
