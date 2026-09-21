using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageChangesOperationsTests
{
    [Fact]
    public void PackageSets_ProjectProductOrderWithoutMembership()
    {
        BrowserPackageChangesPackageSetCatalog catalog =
            BrowserPackageChangesOperations.PackageSets();

        Assert.Equal(
            BrowserPackageChangesOperations.PackageSetCatalogVersion,
            catalog.Version);
        Assert.NotEmpty(catalog.PackageSets);
        Assert.Equal(
            catalog.PackageSets.OrderBy(packageSet => packageSet.Order),
            catalog.PackageSets);
        Assert.All(
            catalog.PackageSets,
            packageSet =>
            {
                Assert.StartsWith("package-set.", packageSet.Id);
                Assert.False(string.IsNullOrWhiteSpace(packageSet.Title));
                Assert.False(string.IsNullOrWhiteSpace(packageSet.Summary));
            });

        string json = JsonSerializer.Serialize(
            catalog,
            BrowserPackageJsonContext.Default
                .BrowserPackageChangesPackageSetCatalog);
        Assert.DoesNotContain(
            "\"members\"",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvePlan_UsesOnlyRegisteredPackageSetsAndPairedIntervals()
    {
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        EcosystemChangeReportPlan defaults =
            BrowserPackageChangesOperations.ResolvePlan(
                new(
                    "package-set.microsoft-extensions",
                    null,
                    null,
                    SecurityOnly: true,
                    MaximumRows: 25),
                clock);

        var selection =
            Assert.IsType<EcosystemChangePackageSelection.PackageSet>(
                defaults.Request.PackageSelection);
        Assert.Equal(
            "package-set.microsoft-extensions",
            selection.SelectionId);
        Assert.NotEmpty(selection.PackageIds);
        Assert.True(defaults.UsedDefaultInterval);
        Assert.Equal(25, defaults.Request.MaximumRows);
        Assert.Equal(
            EcosystemChangeSecuritySelection.SecurityRelevant,
            defaults.Request.SecuritySelection);

        EcosystemChangeReportPlan explicitInterval =
            BrowserPackageChangesOperations.ResolvePlan(
                new(
                    "package-set.aspnetcore",
                    "2026-03-01T01:00:00.0000000+01:00",
                    "2026-03-02T01:00:00.0000000+01:00",
                    SecurityOnly: false,
                    MaximumRows: 100),
                clock);
        Assert.Equal(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            explicitInterval.Interval.FromExclusive);
        Assert.Equal(
            new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero),
            explicitInterval.Interval.ThroughInclusive);

        Assert.Throws<ArgumentException>(() =>
            BrowserPackageChangesOperations.ResolvePlan(
                new(
                    "package-set.unknown",
                    null,
                    null,
                    false,
                    100),
                clock));
        Assert.Throws<ArgumentException>(() =>
            BrowserPackageChangesOperations.ResolvePlan(
                new(
                    "package-set.aspnetcore",
                    "2026-03-01T00:00:00.0000000Z",
                    null,
                    false,
                    100),
                clock));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserPackageChangesOperations.ResolvePlan(
                new(
                    "package-set.aspnetcore",
                    null,
                    null,
                    false,
                    1_001),
                clock));
    }

    [Fact]
    public void WireProjection_PreservesNonterminalsInTerminalDocument()
    {
        EcosystemChangeReportProgressPresentation progress = new(
            EcosystemChangeReportProgressPhase.Catalog,
            Completed: 7,
            Total: null,
            CapturedHorizon: new DateTimeOffset(
                2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            CatalogPagesAcquired: 2,
            CatalogHttpAttempts: 2,
            CatalogDecodedBytes: 4_096);
        var advisoryEvidence =
            new EcosystemChangeAdvisoryEvidencePresentation(
                DotnetInspector.Services.GitHubNuGetAdvisoryAvailability.Complete,
                []);
        var row = new EcosystemChangeReportRowPresentation(
            new(
                "Example.Package",
                "1.0.0",
                "example.package",
                "1.0.0",
                "https://api.nuget.org/v3/catalog0/page/leaf.json",
                "0123456789abcdef",
                new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero),
                NuGetFetch.NuGetCatalogEventKind.Details,
                EcosystemChangeActivityKind.SnapshotObserved),
            advisoryEvidence,
            advisoryEvidence,
            null,
            EcosystemSecurityReleaseStatus.CheckedNoFixedVersionAssociation,
            null,
            IsSecurityRelevant: false);
        var failure = new EcosystemChangeReportFailurePresentation(
            EcosystemChangeFailureProvider.Advisory,
            null,
            DotnetInspector.Services.GitHubNuGetAdvisoryFailureKind
                .SourceUnavailable,
            null);
        EcosystemChangeReportDocument document =
            CreateDocument(progress, row, failure);
        var envelope =
            new InspectionEnvelope<EcosystemChangeReportDocument>(
                new ResourcePath("package-changes"),
                InspectionContentKind.Document,
                document,
                new InspectionPortableProjection.NonProjectable(
                    InspectionPortableProjectionFailureReason.NotSupported));

        BrowserPackageChangesInspection inspection =
            BrowserPackageChangesWireProjection.Project(envelope);
        BrowserPackageChangesEvent progressEvent =
            BrowserPackageChangesWireProjection.Project(
                new EcosystemChangeReportNonterminalEvent.Progress(progress));
        BrowserPackageChangesEvent rowEvent =
            BrowserPackageChangesWireProjection.Project(
                new EcosystemChangeReportNonterminalEvent.Row(row));
        BrowserPackageChangesEvent failureEvent =
            BrowserPackageChangesWireProjection.Project(
                new EcosystemChangeReportNonterminalEvent.Failure(failure));

        Assert.Equal(
            ["Progress", "Row", "Failure"],
            Enum.GetNames<BrowserPackageChangesEventKind>());
        Assert.Equal(
            Serialize(progressEvent.Progress),
            Serialize(Assert.Single(inspection.Content.Progress)));
        Assert.Equal(
            Serialize(rowEvent.Row),
            Serialize(Assert.Single(inspection.Content.Rows)));
        Assert.Equal(
            Serialize(failureEvent.Failure),
            Serialize(Assert.Single(inspection.Content.Failures)));
        Assert.Equal(
            BrowserInspectionPortableProjectionKind.NonProjectable,
            inspection.PortableProjection.Kind);
        Assert.Equal("package-changes", inspection.ResourcePath);
        Assert.Null(inspection.PortableProjection.Location);
        Assert.Empty(inspection.Diagnostics);

        string serialized = JsonSerializer.Serialize(
            new BrowserPackageChangesResult(
                1,
                BrowserPackageChangesResultKind.Succeeded,
                inspection,
                null,
                null,
                null,
                null),
            BrowserPackageJsonContext.Default.BrowserPackageChangesResult);
        Assert.Contains(
            "\"kind\":\"Succeeded\"",
            serialized,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"content\":{\"schemaVersion\":1",
            serialized,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"commitTimestamp\":\"2026-03-31T00:00:00+00:00\"",
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagedCancellation_UsesPackageChangesSettlement()
    {
        var bridge = new BrowserManagedOperationBridge();
        BrowserManagedOperationId operationId =
            BrowserManagedOperationId.From("package-changes-1");
        var started =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<
            BrowserManagedOperationResult<
                BrowserPackageChangesInspection,
                string,
                string>> pending =
            bridge.RunAsync<
                BrowserPackageChangesInspection,
                string,
                string,
                BrowserPackageChangesEvent>(
                operationId,
                eventCallback: null,
                async (token, _) =>
                {
                    started.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    throw new InvalidOperationException("unreachable");
                },
                exception => new(exception.Message, exception.ToString()));
        await started.Task;

        BrowserManagedCancellationRequestResult requested =
            bridge.RequestCancellation(
                operationId,
                BrowserManagedOperationCancelReason.Superseded);
        Assert.IsType<
            BrowserManagedCancellationRequestResult.Requested>(requested);
        BrowserPackageChangesResult result =
            BrowserPackageChangesResult.From(await pending);

        Assert.Equal(BrowserPackageChangesResultKind.Canceled, result.Kind);
        Assert.Equal("superseded", result.Reason);
        Assert.Null(result.Inspection);
    }

    static EcosystemChangeReportDocument CreateDocument(
        EcosystemChangeReportProgressPresentation progress,
        EcosystemChangeReportRowPresentation row,
        EcosystemChangeReportFailurePresentation failure)
    {
        var advisoryEvidence =
            new EcosystemChangeAdvisoryAcquisitionPresentation(
                "nuget.org",
                "GitHub Advisory Database",
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                ApiRequests: 1,
                ResponseBytes: 2,
                Complete: false,
                [
                    DotnetInspector.Services.GitHubNuGetAdvisoryFailureKind
                        .SourceUnavailable,
                ],
                []);
        return new(
            EcosystemChangeReportDocument.CurrentSchemaVersion,
            new(
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                false,
                new(
                    EcosystemChangePackageScopeKind.PackageSet,
                    "package-set.microsoft-extensions",
                    null,
                    ["Example.Package"]),
                EcosystemChangeSecuritySelection.AllActivity,
                MaximumRows: 100,
                MaximumCandidateEvents: 1_000,
                MaximumReceiptRequests: 100),
            new(
                "nuget.org",
                "NuGet.org",
                NuGetFetch.PackageSourceKind.NuGetV3),
            [progress],
            [row],
            [failure],
            new(
                progress.CapturedHorizon,
                NuGetFetch.NuGetCatalogCompletion.WindowExhausted,
                null,
                CatalogPagesAcquired: 2,
                CatalogHttpAttempts: 2,
                CatalogDecodedBytes: 4_096,
                CatalogInWindowEventCount: 7,
                MatchingEventCount: 1,
                RetainedEventCount: 1,
                CandidateLimitReached: false,
                AdvisoryEvidence: advisoryEvidence,
                ReceiptCandidates: 0,
                ReceiptRequests: 0,
                ReceiptSuccesses: 0,
                ReceiptFailures:
                    ImmutableArray<
                        EcosystemChangeReceiptFailurePresentation>.Empty,
                ReceiptLimitReached: false,
                CurrentContextUnevaluableRows: 1,
                SecurityReleaseUnevaluableRows: 0,
                EligibleRowCount: 1,
                ReturnedRowCount: 1,
                ResultLimitReached: false,
                EcosystemChangeReportCompletionKind.Partial));
    }

    static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value);

    sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
