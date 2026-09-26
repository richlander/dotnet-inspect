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
                    "JsonSerializer",
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
                Assert.NotNull(
                    owner.ResolveTypeFindAction(activation.Action!));
            });
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
