using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Analysis;

internal static partial class BrowserImplementationProfileWireProjection
{
    const int TypeMethodLeverageSchemaVersion = 1;

    internal static BrowserTypeMethodLeverage ProjectTypeMethodLeverage(
        InspectionEnvelope<
            AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>>
                inspection,
        BrowserCompileLibraryAvailability compileLibrary)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(compileLibrary);

        BrowserAnalysisInspectionShare share =
            BrowserAnalysisInspectionProjection.Project(inspection.Share);
        BrowserAnalysisInspectionDiagnostic[] diagnostics =
        [
            .. inspection.Diagnostics.Select(
                BrowserAnalysisInspectionProjection.Project),
        ];
        return inspection.Content switch
        {
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Available available =>
                new(
                    TypeMethodLeverageSchemaVersion,
                    "available",
                    Project(available.Subject),
                    ProjectTypeMethodLeverage(available.Value),
                    Failure: null,
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Rejected rejected =>
                new(
                    TypeMethodLeverageSchemaVersion,
                    "rejected",
                    Project(rejected.Subject),
                    Content: null,
                    new(
                        rejected.Failure.Kind.ToString(),
                        rejected.Failure.Detail,
                        rejected.Failure.MetadataRootReason?.ToString()),
                    share,
                    diagnostics,
                    compileLibrary),
            AssemblyContextEntry<
                AssemblyTypeMethodLeverageInspection>.Failed failed =>
                new(
                    TypeMethodLeverageSchemaVersion,
                    "failed",
                    Project(failed.Subject),
                    Content: null,
                    new(
                        failed.Error.GetType().Name,
                        failed.Error.Message,
                        MetadataRootReason: null),
                    share,
                    diagnostics,
                    compileLibrary),
            _ => throw new InvalidOperationException(
                "Unknown Type method-leverage outcome."),
        };
    }

    internal static BrowserTypeMethodLeverage TypeMethodLeverageUnavailable(
        string kind,
        string detail,
        BrowserCompileLibraryAvailability compileLibrary) =>
        new(
            TypeMethodLeverageSchemaVersion,
            "unavailable",
            Subject: null,
            Content: null,
            new(kind, detail, MetadataRootReason: null),
            Share: null,
            Diagnostics: [],
            compileLibrary);

    static BrowserTypeMethodLeverageContent ProjectTypeMethodLeverage(
        AssemblyTypeMethodLeverageInspection inspection) =>
        new(
            inspection.TypeDefinitionId,
            inspection.MethodCount,
            inspection.WinnerCount,
            inspection.WinningRank is { } rank
                ? new BrowserTypeMethodLeverageRank(
                    rank.DirectCallerCount,
                    rank.RootReach,
                    rank.Fanout,
                    rank.LoopCallCount,
                    rank.MaxDepth)
                : null,
            [
                .. inspection.AnchoredWinners.Select(
                    static winner =>
                        new BrowserTypeMethodLeverageWinner(
                            winner.TypeDefinitionId,
                            winner.StableSelector,
                            [.. winner.MethodTokens])),
            ],
            [.. inspection.Diagnostics.Select(Project)],
            [.. inspection.ApiSurfaceInspectionFailures.Select(Project)]);
}
