using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

public sealed class SyncCallsInAsyncDemandTests
{
    [Fact]
    public void ClosingsMatchRowsCountAndApplicability()
    {
        Assert.Equal(
            [SyncCallsInAsyncClosing.Rows],
            SyncCallsInAsyncDemand.ClosingsFor(
                countOnly: false,
                applicabilityOnly: false));
        Assert.Equal(
            [SyncCallsInAsyncClosing.Count],
            SyncCallsInAsyncDemand.ClosingsFor(
                countOnly: true,
                applicabilityOnly: false));
        Assert.Equal(
            [SyncCallsInAsyncClosing.Exists],
            SyncCallsInAsyncDemand.ClosingsFor(
                countOnly: false,
                applicabilityOnly: true));
    }

    [Fact]
    public void CountAnswerProjectsSectionCountWithoutRows()
    {
        var inspection = new LibraryInspection();
        var result = new SyncCallsInAsyncResult(
            [
                (
                    SyncCallsInAsyncClosing.Count,
                    (SyncCallsInAsyncAnswer)
                        new SyncCallsInAsyncAnswer.Count(3)),
            ],
            [],
            []);

        LibraryMetadataService.ApplySyncCallsInAsyncResult(
            "sample.dll",
            inspection,
            new VerboseLogger(false),
            new SyncCallsInAsyncBindingResult.Available(result));

        Assert.Equal(3, inspection.SyncCallsInAsyncCount);
        Assert.Empty(inspection.SyncCallsInAsyncRows);
        Assert.True(
            LibrarySections.PerformanceSyncCallsInAsync.CanRender(
                inspection));

        var projection = new CountProjection();
        OutputFormatter.ApplySyncCallsInAsyncCount(
            projection,
            inspection,
            [SectionNames.PerformanceSyncCallsInAsync],
            rows: null);
        Assert.Equal(
            3,
            projection.SectionCounts[
                SectionNames.PerformanceSyncCallsInAsync]);
    }

    [Fact]
    public async Task EffectiveDiscoveryUsesExistsWithoutProjectingRows()
    {
        string path =
            FixtureCatalog.AnalysisAsyncSiblingRepository.AssemblyPath();
        using var httpClient = new HttpClient();

        LibraryInspection inspection =
            Assert.IsType<LibraryInspection>(
                await LibraryMetadataService.InspectAsync(
                    path,
                    new LibraryOptions
                    {
                        Discover =
                            [SectionNames.PerformanceSyncCallsInAsync],
                        Effective = true,
                    },
                    new VerboseLogger(false),
                    packageName: null,
                    packageVersion: null,
                    httpClient,
                    queries: [SyncCallsInAsyncDemand.Section],
                    queryCatalog: LibrarySections.QueryCatalog));

        Assert.True(
            inspection.SyncCallsInAsyncPresence,
            inspection.SyncCallsInAsyncFailure);
        Assert.Null(inspection.SyncCallsInAsyncCount);
        Assert.Empty(inspection.SyncCallsInAsyncRows);
        Assert.True(
            LibrarySections.PerformanceSyncCallsInAsync.CanRender(
                inspection));
    }

    [Fact]
    public void FailedAnswerProjectsInspectionFailure()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplySyncCallsInAsyncResult(
            "sample.dll",
            inspection,
            new VerboseLogger(false),
            new SyncCallsInAsyncBindingResult.Failed(
                new InvalidOperationException("binding unavailable")));

        List<LibraryInspectionFailureJson> failures =
            Assert.IsType<List<LibraryInspectionFailureJson>>(
                inspection.InspectionFailures);
        LibraryInspectionFailureJson failure = Assert.Single(
            failures,
            value => value.Section ==
                SectionNames.PerformanceSyncCallsInAsync);
        Assert.Equal("binding unavailable", failure.Reason);
        Assert.False(
            LibrarySections.PerformanceSyncCallsInAsync.CanRender(
                inspection));
    }
}
