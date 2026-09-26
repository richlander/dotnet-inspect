using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using Catalog = DotnetInspect.Web.Interop.Catalog;
using Metadata = DotnetInspect.Web.Interop.Metadata;
using TypeFindPlan =
    DotnetInspect.Web.BrowserSpotlightDestinationActivationPlan<
        DotnetInspect.Web.BrowserTypeFindPackageRequest,
        DotnetInspector.Queries.NavigationAction,
        DotnetInspect.Web.BrowserFrameworkDeclarationAction,
        DotnetInspect.Web.BrowserTypeFindLibraryIntent>;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    public async Task MetadataFacade_TypeFindReturnsSharedEnvelopeAndExactActions()
    {
        _ = await OptionsAsync();
        await Catalog.BrowserRetainedWorkspaceActivationService
            .ResetForTestsAsync();
        try
        {
            string activationJson =
                await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                    "type-find",
                    "Type Find",
                    "/workspace",
                    Packet());
            Catalog.BrowserRetainedWorkspaceActivationResult activation =
                Assert.IsType<
                    Catalog.BrowserRetainedWorkspaceActivationResult>(
                        JsonSerializer.Deserialize(
                            activationJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspaceActivationResult));
            Catalog.BrowserRetainedWorkspacePosting posting =
                Assert.IsType<Catalog.BrowserRetainedWorkspacePosting>(
                    activation.Posting);

            BrowserTypeFindExecutionResult.Completed managed =
                Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                    await Catalog.BrowserRetainedWorkspaceActivationService
                        .Owner.FindTypesAsync(
                            posting.RetainedDefinitionId,
                            posting.RealizationId,
                            "JsonSerializer",
                            resultGeneration: 1,
                            TestContext.Current.CancellationToken));
            TypeDeclarationLocatorSectionResult.Evaluated content =
                Assert.IsType<
                    TypeDeclarationLocatorSectionResult.Evaluated>(
                        managed.Result.Find.Content);
            TypeDeclarationLocatorSectionAnswer answer =
                Assert.Single(content.Answers);
            Assert.NotEmpty(answer.Candidates);
            Assert.Equal(
                answer.Candidates.Length,
                managed.Result.Activations.Length);
            Assert.Equal(
                2,
                answer.Candidates
                    .Select(candidate => candidate.Observation.ContextOrder)
                    .Distinct()
                    .Count());
            Assert.All(
                answer.Candidates,
                candidate => Assert.Equal(
                    answer.Candidates[0].Coordinate,
                    candidate.Coordinate));
            Assert.All(
                managed.Result.Activations,
                candidate =>
                {
                    Assert.Equal(answer.Identity.Ordinal,
                        candidate.Candidate.AnswerOrdinal);
                    Assert.Equal(
                        BrowserTypeFindActivationSource.Package,
                        candidate.Source);
                    Assert.Equal(
                        BrowserTypeFindActivationStatus.Available,
                        candidate.Status);
                    Assert.False(string.IsNullOrWhiteSpace(candidate.Action));
                    Assert.Null(candidate.Reason);
                });
            StructuralSubjectIdentity.TypeSubject[] subjects =
            [
                .. managed.Result.Activations.Select(activation =>
                {
                    BrowserTypeFindCapturedDestination.Package captured =
                        Assert.IsType<
                            BrowserTypeFindCapturedDestination.Package>(
                                Catalog
                                    .BrowserRetainedWorkspaceActivationService
                                    .Owner.ResolveTypeFindAction(
                                        activation.Action!));
                    TypeFindPlan.NavigateCurrent plan =
                        Assert.IsType<TypeFindPlan.NavigateCurrent>(
                            captured.Descriptor.Plan);
                    return Assert.IsType<
                        StructuralSubjectIdentity.TypeSubject>(
                            plan.Subject);
                }),
            ];
            Assert.Equal(subjects[0], subjects[1]);

            string findJson = await Metadata.MetadataExports.FindTypes(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 2,
                "JsonSerializer");
            JsonObject find = Assert.IsType<JsonObject>(
                JsonNode.Parse(findJson));
            Assert.Equal("Completed", find["status"]?.GetValue<string>());
            JsonObject operation = Assert.IsType<JsonObject>(find["operation"]);
            JsonObject findEnvelope =
                Assert.IsType<JsonObject>(operation["find"]);
            JsonNode? sharedContent = JsonNode.Parse(
                TypeDeclarationLocatorSectionJson.Serialize(
                    managed.Result.Find.Content));
            Assert.NotNull(sharedContent);
            Assert.True(
                JsonNode.DeepEquals(
                    sharedContent,
                    findEnvelope["content"]),
                $"Managed: {sharedContent}{Environment.NewLine}"
                    + $"Facade: {findEnvelope["content"]}");
            JsonArray activations =
                Assert.IsType<JsonArray>(operation["activations"]);
            Assert.Equal(
                answer.Candidates.Length,
                activations.Count);
            string[] actions =
            [
                .. activations.Select(candidate =>
                    Assert.IsType<JsonObject>(candidate)["action"]!
                        .GetValue<string>()),
            ];
            Assert.Equal(
                actions.Length,
                actions
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                managed.Result.Activations,
                activation => Assert.Null(
                    Catalog.BrowserRetainedWorkspaceActivationService
                        .Owner.ResolveTypeFindAction(
                            activation.Action!)));
            Assert.All(
                actions,
                action => Assert.IsType<
                    BrowserTypeFindCapturedDestination.Package>(
                        Catalog.BrowserRetainedWorkspaceActivationService
                            .Owner.ResolveTypeFindAction(action)));

            string staleJson = await Metadata.MetadataExports.FindTypes(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 2,
                "JsonDocument");
            JsonObject stale = Assert.IsType<JsonObject>(
                JsonNode.Parse(staleJson));
            Assert.Equal("Stale", stale["status"]?.GetValue<string>());
            Assert.Null(stale["operation"]);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService
                .ResetForTestsAsync();
        }
    }

    [Fact]
    public async Task ManagedTypeFind_BindsPackageAndFrameworkCandidates()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync(includePlatform: true);
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(
                owner,
                "mixed-type-find",
                MixedInventoryPacket(reverseContexts: false));

        BrowserTypeFindExecutionResult.Completed completed =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "Json*",
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            completed.Result.Activations,
            activation =>
                activation.Source == BrowserTypeFindActivationSource.Package
                && activation.Status
                    == BrowserTypeFindActivationStatus.Available);
        Assert.Contains(
            completed.Result.Activations,
            activation =>
                activation.Source == BrowserTypeFindActivationSource.Framework
                && activation.Status
                    == BrowserTypeFindActivationStatus.Available);
        Assert.All(
            completed.Result.Activations,
            activation =>
            {
                Assert.Equal(
                    BrowserTypeFindActivationStatus.Available,
                    activation.Status);
                Assert.False(string.IsNullOrWhiteSpace(activation.Action));
                BrowserTypeFindCapturedDestination captured =
                    Assert.IsAssignableFrom<
                        BrowserTypeFindCapturedDestination>(
                            owner.ResolveTypeFindAction(
                                activation.Action!));
                if (activation.Source
                    == BrowserTypeFindActivationSource.Package)
                {
                    Assert.IsType<
                        BrowserTypeFindCapturedDestination.Package>(
                            captured);
                }
                else
                {
                    Assert.Equal(
                        BrowserTypeFindActivationSource.Framework,
                        activation.Source);
                    Assert.IsType<
                        BrowserTypeFindCapturedDestination.Framework>(
                            captured);
                }
            });
    }

    [Fact]
    public async Task ManagedTypeFind_ReplacementInvalidatesCapturedActions()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting first =
            await ActivateAsync(owner, "first-type-find", Packet());
        BrowserTypeFindExecutionResult.Completed firstFind =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    first.RetainedDefinitionId,
                    first.RealizationId,
                    "JsonSerializer",
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        string action = firstFind.Result.Activations
            .First(activation => activation.Action is not null)
            .Action!;
        Assert.NotNull(owner.ResolveTypeFindAction(action));

        BrowserRetainedWorkspacePosting replacement =
            await ActivateAsync(owner, "replacement-type-find", Packet());

        Assert.Null(owner.ResolveTypeFindAction(action));
        Assert.IsType<BrowserTypeFindExecutionResult.Unavailable>(
            await owner.FindTypesAsync(
                first.RetainedDefinitionId,
                first.RealizationId,
                "JsonSerializer",
                resultGeneration: 2,
                TestContext.Current.CancellationToken));
        BrowserTypeFindExecutionResult.Completed replacementFind =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
            await owner.FindTypesAsync(
                replacement.RetainedDefinitionId,
                replacement.RealizationId,
                "JsonSerializer",
                resultGeneration: 1,
                TestContext.Current.CancellationToken));
        string replacementAction = replacementFind.Result.Activations
            .First(activation => activation.Action is not null)
            .Action!;
        Assert.NotEqual(action, replacementAction);
        Assert.Null(owner.ResolveTypeFindAction(action));
        Assert.NotNull(owner.ResolveTypeFindAction(replacementAction));
    }

    [Fact]
    public async Task ManagedTypeFind_EmptyTextSupersedesCapturedActions()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "cleared-type-find", Packet());
        BrowserTypeFindExecutionResult.Completed completed =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "JsonSerializer",
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        string action = completed.Result.Activations
            .First(activation => activation.Action is not null)
            .Action!;
        Assert.NotNull(owner.ResolveTypeFindAction(action));

        Assert.IsType<BrowserTypeFindExecutionResult.Rejected>(
            await owner.FindTypesAsync(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                " ",
                resultGeneration: 2,
                TestContext.Current.CancellationToken));

        Assert.Null(owner.ResolveTypeFindAction(action));
        Assert.IsType<BrowserTypeFindExecutionResult.Stale>(
            await owner.FindTypesAsync(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                "JsonDocument",
                resultGeneration: 1,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManagedTypeFind_ReusesAdmittedDeclarationPopulation()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "reused-type-find", Packet());

        BrowserTypeFindExecutionResult.Completed first =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "JsonSerializer",
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        Assert.NotEmpty(first.Result.Activations);
        ImmutableArray<WorkspaceDeclarationContext> admitted;
        using (WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, posting.RetainedDefinitionId))
        {
            admitted = operation.Workspace.GetDeclarationContextsSnapshot();
        }
        Assert.Equal(2, admitted.Length);

        BrowserTypeFindExecutionResult.Completed second =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "JsonDocument",
                    resultGeneration: 2,
                    TestContext.Current.CancellationToken));
        Assert.NotEmpty(second.Result.Activations);
        using WorkspaceRealizationOperationLease later =
            await EnterAsync(owner, posting.RetainedDefinitionId);
        ImmutableArray<WorkspaceDeclarationContext> reused =
            later.Workspace.GetDeclarationContextsSnapshot();
        Assert.Equal(admitted.Length, reused.Length);
        for (int index = 0; index < admitted.Length; index++)
            Assert.Same(admitted[index], reused[index]);
    }

    [Fact]
    public async Task ManagedTypeFind_NoMatchReturnsEmptyCompletedVector()
    {
        CompleteRestorationExecutionOptions options =
            await DetachedInventoryOptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateAsync(owner, "empty-vector-type-find", Packet());

        BrowserTypeFindExecutionResult.Completed completed =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "ThisTypeDoesNotExistAnywhere",
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        TypeDeclarationLocatorSectionResult.Evaluated content =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                completed.Result.Find.Content);
        Assert.Empty(Assert.Single(content.Answers).Candidates);
        Assert.Empty(completed.Result.Activations);
    }

    [Fact]
    public async Task MetadataFacade_EmptyTypeFindDoesNotActivateLocator()
    {
        _ = await OptionsAsync();
        await Catalog.BrowserRetainedWorkspaceActivationService
            .ResetForTestsAsync();
        try
        {
            string activationJson =
                await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                    "empty-type-find",
                    "Empty Type Find",
                    "/workspace",
                    Packet());
            Catalog.BrowserRetainedWorkspaceActivationResult activation =
                Assert.IsType<
                    Catalog.BrowserRetainedWorkspaceActivationResult>(
                        JsonSerializer.Deserialize(
                            activationJson,
                            Catalog.BrowserCatalogJsonContext.Default
                                .BrowserRetainedWorkspaceActivationResult));
            Catalog.BrowserRetainedWorkspacePosting posting =
                Assert.IsType<Catalog.BrowserRetainedWorkspacePosting>(
                    activation.Posting);

            string findJson = await Metadata.MetadataExports.FindTypes(
                posting.RetainedDefinitionId,
                posting.RealizationId,
                resultGeneration: 1,
                " ");
            JsonObject find = Assert.IsType<JsonObject>(
                JsonNode.Parse(findJson));
            Assert.Equal("Rejected", find["status"]?.GetValue<string>());
            Assert.Null(find["operation"]);
            Assert.False(string.IsNullOrWhiteSpace(
                find["reason"]?.GetValue<string>()));
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService
                .ResetForTestsAsync();
        }
    }
}
