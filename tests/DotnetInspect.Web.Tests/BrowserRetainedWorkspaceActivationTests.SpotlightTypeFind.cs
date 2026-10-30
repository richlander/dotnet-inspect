using DotnetInspector.Ecosystems;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Web.Interop.Catalog;
using ILInspector.Metadata;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    public void ManagedSpotlightTypeFind_IdentityPreservesExactSourceArm()
    {
        var assembly = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "Contoso.Json",
                new Version(1, 2, 3, 0),
                null,
                null));
        MetadataTypeDefinitionName type = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Contoso",
                    ["JsonSerializer"]))
            .Name;
        Guid moduleVersionId = Guid.Parse(
            "109727aa-bbd2-4ff8-a0ca-7e5ef0591989");
        ExactLibrarySourceCoordinate[] sources =
        [
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    "Contoso.Json",
                    "1.2.3"),
                assembly),
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                assembly),
            new ExactLibrarySourceCoordinate.Project(assembly),
            new ExactLibrarySourceCoordinate.Local(assembly),
        ];

        string[] identities =
        [
            .. sources.Select(source =>
                BrowserSpotlightTypeIdentity.TryCreate(
                    source,
                    type,
                    moduleVersionId)!),
        ];

        Assert.Equal(identities.Length, identities.Distinct().Count());
        Assert.StartsWith("package-type:", identities[0]);
        Assert.StartsWith("platform-type:", identities[1]);
        Assert.StartsWith("project-type:", identities[2]);
        Assert.StartsWith("local-type:", identities[3]);
    }

    [Fact]
    public async Task ManagedSpotlightTypeFind_UsesOneOrderedSessionAcrossProducers()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "spotlight-ordered-find",
                EcosystemPackIds.Aspire);
        EcosystemFindIndexArtifact aspire = LoadFindIndex("aspire.json");
        BrowserIndexedTypeFindExecutionResult.Completed indexed =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                        owner,
                        posting.RetainedDefinitionId,
                        posting.RealizationId,
                        [aspire],
                        "Aspire.Hosting.ApplicationModel.RedisResource",
                        maximumRows: 25,
                        resultGeneration: 1,
                        TestContext.Current.CancellationToken));
        TypeFindSemanticMatch redis = Assert.Single(
            indexed.Result.Find.Content.Matches,
            match => match.FullName
                == "Aspire.Hosting.ApplicationModel.RedisResource");
        string action = Assert.Single(
            indexed.Result.Activations,
            activation => activation.MatchOrdinal
                == indexed.Result.Find.Content.Matches.IndexOf(redis) + 1)
            .Action!;
        BrowserTypeFindActivationResult? activated =
            await Catalog.BrowserRetainedWorkspaceActivationService
                .TryActivateIndexedTypeFindActionAsync(
                    owner,
                    action,
                    TestContext.Current.CancellationToken);
        Assert.IsType<BrowserTypeFindActivationResult.Navigation>(activated);

        var streamed = new RecordingFindingSink();
        var session = new FindSemanticResultSession(100, streamed);
        BrowserSpotlightTypeFindExecution execution =
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindSpotlightTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    new BrowserEcosystemFindIndexAssetLoad.Available([aspire]),
                    "RedisResource",
                    maximumRows: 100,
                    resultGeneration: 2,
                    session,
                    TestContext.Current.CancellationToken);
        FindSemanticResult completed = session.Complete(
            execution.Completion,
            execution.Failures,
            execution.Unsearched);
        BrowserSpotlightTypeFindInspection inspection =
            Catalog.CatalogExports.ProjectSpotlightTypeFindInspection(
                completed,
                execution);

        Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
            execution.Indexed);
        Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
            execution.Resident);
        Assert.Equal(streamed.Findings, completed.Findings);
        Assert.Equal(
            streamed.Findings.Select(finding =>
                Catalog.CatalogExports
                    .ProjectSpotlightTypeFindFinding(finding).Identity),
            inspection.Candidates.Select(candidate => candidate.Identity));
        Assert.Single(
            completed.Findings,
            finding => Assert.IsType<FindSemanticFinding.Type>(finding)
                .Match.FullName
                    == "Aspire.Hosting.ApplicationModel.RedisResource");
        Assert.All(
            inspection.Candidates,
            candidate => Assert.NotNull(candidate.Action));
    }

    [Fact]
    public async Task ManagedSpotlightTypeFind_IndexFailureStillSearchesWorkspace()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "spotlight-index-failure", Packet());
        var streamed = new RecordingFindingSink();
        var session = new FindSemanticResultSession(25, streamed);

        BrowserSpotlightTypeFindExecution execution =
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindSpotlightTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    new BrowserEcosystemFindIndexAssetLoad.Invalid(
                        "fixture index failure"),
                    "JsonSerializer",
                    maximumRows: 25,
                    resultGeneration: 1,
                    session,
                    TestContext.Current.CancellationToken);
        FindSemanticResult completed = session.Complete(
            execution.Completion,
            execution.Failures,
            execution.Unsearched);

        Assert.Null(execution.Indexed);
        Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
            execution.Resident);
        Assert.NotEmpty(completed.Findings);
        Assert.Contains(
            completed.Failures,
            failure => failure.Source == "checked-in index"
                && failure.Message == "fixture index failure");
        Assert.Equal(FindSemanticResultCompletion.Partial, completed.Completion);
    }

    [Fact]
    public async Task ManagedSpotlightTypeFind_IndexLimitSkipsWorkspace()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "spotlight-index-limit",
                EcosystemPackIds.Aspire);
        var session = new FindSemanticResultSession(1);

        BrowserSpotlightTypeFindExecution execution =
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindSpotlightTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    new BrowserEcosystemFindIndexAssetLoad.Available(
                        [LoadFindIndex("aspire.json")]),
                    "*",
                    maximumRows: 1,
                    resultGeneration: 1,
                    session,
                    TestContext.Current.CancellationToken);
        FindSemanticResult completed = session.Complete(
            execution.Completion,
            execution.Failures,
            execution.Unsearched);

        Assert.Null(execution.Resident);
        Assert.Single(completed.Findings);
        Assert.Equal(
            FindSemanticResultCompletion.RowLimitReached,
            completed.Completion);
        Assert.Equal(["retained Workspace"], completed.Unsearched);
    }

    [Fact]
    public async Task ManagedSpotlightTypeFind_CancellationPublishesNoLaterFinding()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "spotlight-find-cancellation",
                EcosystemPackIds.Aspire);
        using var canceled = new CancellationTokenSource();
        var streamed = new CancelingFindingSink(canceled);
        var session = new FindSemanticResultSession(25, streamed);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindSpotlightTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    new BrowserEcosystemFindIndexAssetLoad.Available(
                        [LoadFindIndex("aspire.json")]),
                    "*",
                    maximumRows: 25,
                    resultGeneration: 1,
                    session,
                    canceled.Token));

        Assert.Single(streamed.Findings);
    }

    class RecordingFindingSink : IFindSemanticFindingSink
    {
        public List<FindSemanticFinding> Findings { get; } = [];

        public virtual void Publish(FindSemanticFinding finding) =>
            Findings.Add(finding);
    }

    sealed class CancelingFindingSink(CancellationTokenSource cancellation) :
        RecordingFindingSink
    {
        public override void Publish(FindSemanticFinding finding)
        {
            base.Publish(finding);
            cancellation.Cancel();
        }
    }
}
