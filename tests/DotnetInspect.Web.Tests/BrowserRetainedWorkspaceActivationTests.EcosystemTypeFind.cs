using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspect.Web.Interop.Catalog;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    public async Task ManagedEcosystemTypeFind_IndexFailureStillCompletesSettlement()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "ecosystem-find-index-failure",
                EcosystemPackIds.Aspire);
        var events = new List<BrowserEcosystemTypeFindEvent>();

        BrowserEcosystemTypeFindInspection inspection =
            await BrowserEcosystemTypeFindOperation.ExecuteAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 1,
                new(
                    "JsonSerializer",
                    [EcosystemPackIds.Aspire.Value],
                    [],
                    RowStart: 1,
                    RowEnd: 25,
                    MaximumPrefixPackages: 25,
                    TargetFramework: "net11.0"),
                new BrowserEcosystemFindIndexAssetLoad.Invalid(
                    "fixture index failure"),
                events.Add,
                TestContext.Current.CancellationToken);

        Assert.All(
            inspection.Settlement.Findings,
            finding =>
            {
                Assert.NotEmpty(finding.Identity);
                Assert.NotEmpty(finding.Source.Library.Name);
                Assert.NotEmpty(finding.Source.Library.Version);
                Assert.NotEmpty(finding.Source.Provenance.Kind);
                Assert.NotEqual(Guid.Empty, finding.ModuleVersionId);
            });
        Assert.Equal("Partial", inspection.Settlement.Completion);
        Assert.True(inspection.Settlement.Incomplete);
        Assert.Contains(
            inspection.Settlement.Failures,
            failure => failure.Phase == "Indexed"
                && failure.Source == "checked-in indexes"
                && failure.Message == "fixture index failure");
        Assert.Contains(
            events,
            findEvent => findEvent.Kind
                    == BrowserEcosystemTypeFindEventKind.Progress
                && findEvent.Progress?.Phase
                    == BrowserEcosystemTypeFindPhase.Bounded);
        Assert.Contains(
            events,
            findEvent => findEvent.Kind
                    == BrowserEcosystemTypeFindEventKind.Progress
                && findEvent.Progress?.Phase
                    == BrowserEcosystemTypeFindPhase.Workspace);
        Assert.Contains(
            events,
            findEvent => findEvent.Kind
                    == BrowserEcosystemTypeFindEventKind.ItemFailure
                && findEvent.ItemFailure?.Message
                    == "fixture index failure");
        Assert.All(
            events.Where(findEvent =>
                findEvent.Kind
                    == BrowserEcosystemTypeFindEventKind.Finding),
            findEvent =>
            {
                BrowserEcosystemTypeFindItem item =
                    Assert.IsType<BrowserEcosystemTypeFindItem>(
                        findEvent.Finding);
                Assert.Equal(item.Identity, item.Finding.Identity);
                Assert.NotEqual(
                    BrowserEcosystemTypeFindActivationStatus.Failed,
                    item.Activation.Status);
            });
    }

    [Fact]
    public async Task ManagedEcosystemTypeFind_ZeroCapacitySkipsPrefixWork()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "ecosystem-find-zero-capacity",
                EcosystemPackIds.Aspire);
        var events = new List<BrowserEcosystemTypeFindEvent>();

        BrowserEcosystemTypeFindInspection inspection =
            await BrowserEcosystemTypeFindOperation.ExecuteAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 1,
                new(
                    "*",
                    [EcosystemPackIds.Aspire.Value],
                    [EcosystemPackIds.Aspire.Value],
                    RowStart: 1,
                    RowEnd: 1,
                    MaximumPrefixPackages: 25,
                    TargetFramework: "net11.0"),
                new BrowserEcosystemFindIndexAssetLoad.Available(
                    [LoadFindIndex("aspire.json")]),
                events.Add,
                TestContext.Current.CancellationToken);

        BrowserEcosystemTypeFindFinding finding =
            Assert.Single(inspection.Settlement.Findings);
        Assert.NotEmpty(finding.FullName);
        Assert.Equal(
            "RowLimitReached",
            inspection.Settlement.Completion);
        Assert.DoesNotContain(
            events,
            findEvent => findEvent.Progress?.Phase
                == BrowserEcosystemTypeFindPhase.Prefix);
        BrowserEcosystemTypeFindItem item = Assert.Single(
            events
                .Where(findEvent => findEvent.Kind
                    == BrowserEcosystemTypeFindEventKind.Finding)
                .Select(findEvent => findEvent.Finding!));
        Assert.Equal(
            BrowserEcosystemTypeFindActivationStatus.Available,
            item.Activation.Status);
        Assert.NotNull(item.Activation.Action);
    }

}
