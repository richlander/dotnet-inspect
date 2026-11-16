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

    [Fact]
    public async Task ManagedEcosystemTypeFind_IndexedPrefixPackageNeedsNoLiveDemand()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "ecosystem-find-indexed-prefix-without-demand",
                EcosystemPackIds.Aspire);
        var events = new List<BrowserEcosystemTypeFindEvent>();

        BrowserEcosystemTypeFindInspection inspection =
            await BrowserEcosystemTypeFindOperation.ExecuteAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 1,
                new(
                    "Aspire.Hosting.ApplicationModel.RedisResource",
                    [EcosystemPackIds.Aspire.Value],
                    [],
                    RowStart: 1,
                    RowEnd: 25,
                    MaximumPrefixPackages: 25,
                    TargetFramework: "net11.0"),
                new BrowserEcosystemFindIndexAssetLoad.Available(
                    [LoadFindIndex("aspire.json")]),
                events.Add,
                TestContext.Current.CancellationToken);

        BrowserEcosystemTypeFindFinding finding = Assert.Single(
            inspection.Settlement.Findings,
            candidate => candidate.FullName
                == "Aspire.Hosting.ApplicationModel.RedisResource");
        BrowserEcosystemTypeFindMembership membership = Assert.Single(
            finding.Memberships,
            candidate => candidate.Ecosystem
                == EcosystemPackIds.Aspire.Value);
        Assert.Equal("PackagePrefix", membership.Basis);
        Assert.DoesNotContain(
            events,
            findEvent => findEvent.Progress?.Phase
                == BrowserEcosystemTypeFindPhase.Prefix);
    }

    [Fact]
    public async Task ManagedEcosystemTypeFind_HasIndependentPublicationGeneration()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "ecosystem-find-independent-generation",
                EcosystemPackIds.Aspire);
        const string text =
            "Aspire.Hosting.ApplicationModel.ContainerResource";
        EcosystemFindIndexArtifact index = LoadFindIndex("aspire.json");
        var events = new List<BrowserEcosystemTypeFindEvent>();
        Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
            await owner.FindTypesAsync(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                "JsonSerializer",
                resultGeneration: 3,
                TestContext.Current.CancellationToken));
        Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    [index],
                    text,
                    maximumRows: 25,
                    resultGeneration: 3,
                    TestContext.Current.CancellationToken));

        BrowserEcosystemTypeFindInspection inspection =
            await BrowserEcosystemTypeFindOperation.ExecuteAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 1,
                new(
                    text,
                    [EcosystemPackIds.Aspire.Value],
                    [],
                    RowStart: 1,
                    RowEnd: 25,
                    MaximumPrefixPackages: 3,
                    TargetFramework: "net11.0"),
                new BrowserEcosystemFindIndexAssetLoad.Available([index]),
                events.Add,
                TestContext.Current.CancellationToken);

        Assert.Contains(
            inspection.Settlement.Findings,
            finding => finding.FullName == text);
        Assert.DoesNotContain(
            inspection.Settlement.Failures,
            failure => failure.Message.Contains(
                "newer",
                StringComparison.OrdinalIgnoreCase));
        string action = Assert.Single(
            events,
            findEvent => findEvent.Finding?.Finding.FullName == text
                && findEvent.Finding.Activation.Action is not null)
            .Finding!.Activation.Action!;
        Assert.NotNull(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .TryActivateIndexedTypeFindActionAsync(
                    owner,
                    action,
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManagedEcosystemTypeFind_ResidentActionsUseIndependentAuthority()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(
                owner,
                "ecosystem-find-independent-resident-action",
                Packet());

        Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
            await owner.FindTypesAsync(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                "JsonSerializer",
                resultGeneration: 3,
                TestContext.Current.CancellationToken));
        BrowserTypeFindExecutionResult.Completed ecosystem =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindEcosystemSemanticTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    TypeFindQuestion.Create(
                        ["JsonSerializer"],
                        FindVisibility.Public),
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        BrowserTypeFindCandidateActivation? activation =
            ecosystem.Result.Activations.FirstOrDefault(
                candidate => candidate.Action is not null);
        Assert.NotNull(activation);
        string action = activation.Action!;
        Assert.NotNull(owner.ResolveTypeFindAction(action));
    }

}
