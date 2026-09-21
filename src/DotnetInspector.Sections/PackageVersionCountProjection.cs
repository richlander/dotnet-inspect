using QuerySpace.Rows;

namespace DotnetInspector.Sections;

internal static class PackageVersionCountProjection
{
    public static PackageVersionPopulationCountOutcome Count<TVersion, TSource>(
        IReadOnlyList<TVersion> versions,
        IReadOnlyList<TSource> sourceListings,
        PackageVersionPopulationCountRequest request) =>
        request.Cohort switch
        {
            PackageVersionPopulationCountCohort.Versions =>
                CountRows(versions, request),
            PackageVersionPopulationCountCohort.SourceListings =>
                CountRows(sourceListings, request),
            _ => throw new InvalidOperationException(
                $"Unknown package version population Count cohort '{request.Cohort}'."),
        };

    private static PackageVersionPopulationCountOutcome CountRows<T>(
        IReadOnlyList<T> rows,
        PackageVersionPopulationCountRequest request)
    {
        if (request.RowSelection is not { Operations.Count: > 0 })
        {
            return new PackageVersionPopulationCountOutcome.Completed(
                new(request.Cohort, rows.Count));
        }

        RowsCohortResult<string, T> selected =
            RowsCohortExecutor.ApplyUnordered(
                [
                    RowsCohortSequence<string, T>.Create(
                        "Package versions",
                        rows),
                ],
                request.RowSelection);
        if (selected.IsSuccess)
        {
            return new PackageVersionPopulationCountOutcome.Completed(
                new(request.Cohort, selected.RowSets[0].Values.Count));
        }

        RowsCohortSemanticFailure<string> failure = selected.Failure!;
        return new PackageVersionPopulationCountOutcome.Rejected(
            new(
                request.Cohort,
                failure.Failure.StageNumber,
                failure.Failure.RequiredPosition,
                failure.Failure.AvailableCount));
    }

    public static InspectionEnvelope<int> ProjectEnvelope<TContent>(
        InspectionEnvelope<TContent> source,
        PackageVersionPopulationCountOutcome.Completed count) =>
        new(
            InspectionContentKind.Result,
            count.Result.Value,
            new InspectionPortableProjection.NonProjectable(
                "package-version-count/share",
                InspectionPortableProjectionFailureReason.NotSupported),
            source.Diagnostics);
}
