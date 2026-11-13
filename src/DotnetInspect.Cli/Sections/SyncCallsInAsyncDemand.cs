using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Sections;

public abstract record SyncCallsInAsyncBindingResult
{
    private SyncCallsInAsyncBindingResult()
    {
    }

    public sealed record Available(SyncCallsInAsyncResult Result)
        : SyncCallsInAsyncBindingResult;

    public sealed record Failed(Exception Error)
        : SyncCallsInAsyncBindingResult;
}

public static class SyncCallsInAsyncDemand
{
    public static InspectionQuery<SyncCallsInAsyncBindingResult> Section
    { get; } =
        new(
            "Sync calls in async (section)",
            InspectionCost.NetworkFree);

    public static IReadOnlyList<SyncCallsInAsyncClosing> ClosingsFor(
        bool countOnly,
        bool applicabilityOnly) =>
        applicabilityOnly
            ? [SyncCallsInAsyncClosing.Exists]
            : countOnly
                ? [SyncCallsInAsyncClosing.Count]
                : [SyncCallsInAsyncClosing.Rows];
}
