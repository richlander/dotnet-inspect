using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Web.Interop.Catalog;
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
    public async Task ManagedIndexedTypeFind_UsesRetainedEcosystemAndExactIdentity()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateEcosystemAsync(
                owner,
                "aspire-indexed-type-find",
                EcosystemPackIds.Aspire);
        EcosystemFindIndexArtifact aspire = LoadFindIndex("aspire.json");
        EcosystemFindIndexArtifact extensions =
            LoadFindIndex("microsoft-extensions.json");

        BrowserIndexedTypeFindExecutionResult.Completed completed =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    [aspire, extensions],
                    "Aspire.Hosting.ApplicationModel.RedisResource",
                    maximumRows: 5000,
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        EcosystemFindIndexedIntegrationResult.Type content =
            completed.Result.Find.Content;
        TypeFindSemanticMatch match = Assert.Single(
            content.Matches,
            candidate => candidate.FullName
                == "Aspire.Hosting.ApplicationModel.RedisResource");
        Assert.Equal(
            "Aspire.Hosting.ApplicationModel.RedisResource",
            match.FullName);
        var source = Assert.IsType<ExactLibrarySourceCoordinate.Package>(
            match.Declaration.Source.Coordinate);
        Assert.Equal(
            "aspire.hosting.redis",
            source.PackageCoordinate.PackageId);
        Assert.Equal(
            "13.6.3",
            source.PackageCoordinate.Version);
        Assert.Equal(
            "Aspire.Hosting.Redis",
            source.LibraryIdentity.Identity.Name);
        Assert.Equal(
            ["RedisResource"],
            match.Declaration.Name.Segments);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Exhausted,
            content.Completion);
        Assert.Equal(
            EcosystemPackIds.Aspire,
            Assert.Single(content.Coverage).Source.Ecosystem);
        InspectionShare.NonProjectable share =
            Assert.IsType<InspectionShare.NonProjectable>(
                completed.Result.Find.Share);
        Assert.Empty(completed.Result.Find.Diagnostics);

        Catalog.BrowserIndexedTypeFindOperationResult projected =
            Catalog.CatalogExports.ProjectIndexedTypeFindOperation(
                completed.Result);
        Catalog.BrowserVocabularyNonProjectableShare projectedShare =
            Assert.IsType<Catalog.BrowserVocabularyNonProjectableShare>(
                projected.Find.Share);
        Assert.Equal(share.Path, projectedShare.Path);
        Assert.Equal(share.Reason.ToString(), projectedShare.Reason);
        Assert.Empty(projected.Find.Diagnostics);
        JsonElement wireMatch = projected.Find.Content
            .GetProperty("matches")
            .EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("fullName").GetString()
                    == "Aspire.Hosting.ApplicationModel.RedisResource");
        Assert.Equal(
            "Aspire.Hosting.ApplicationModel",
            wireMatch.GetProperty("declaration")
                .GetProperty("name")
                .GetProperty("namespace")
                .GetString());
        Assert.Equal(
            "aspire.hosting.redis",
            wireMatch.GetProperty("declaration")
                .GetProperty("source")
                .GetProperty("coordinate")
                .GetProperty("packageCoordinate")
                .GetProperty("packageId")
                .GetString());
        BrowserIndexedTypeFindCandidateActivation activation =
            Assert.Single(
                projected.Activations,
                candidate => candidate.MatchOrdinal
                    == completed.Result.Find.Content.Matches
                        .IndexOf(match) + 1);
        Assert.Equal(
            BrowserIndexedTypeFindActivationStatus.Available,
            activation.Status);
        Assert.NotNull(activation.Identity);
        Assert.NotNull(activation.Action);

        BrowserTypeFindActivationResult? activated =
            await Catalog.BrowserRetainedWorkspaceActivationService
                .TryActivateIndexedTypeFindActionAsync(
                    owner,
                    activation.Action!,
                    TestContext.Current.CancellationToken);
        if (activated is BrowserTypeFindActivationResult.Blocked blocked)
            Assert.Fail($"{blocked.Status}: {blocked.Reason}");
        BrowserTypeFindActivationResult.Navigation navigation =
            Assert.IsType<BrowserTypeFindActivationResult.Navigation>(
                activated);
        Assert.Equal(
            "Aspire.Hosting.ApplicationModel.RedisResource",
            navigation.Selection.DefinitionId);
        Assert.Equal(
            "Aspire.Hosting.Redis",
            navigation.Selection.AssemblyName);
        Assert.True(
            navigation.Result.Consumer.Outcome.Kind
                == NavigationOutcomeKind.Applied,
            $"{navigation.Result.Consumer.Outcome.Kind}: "
                + navigation.Result.Consumer.Outcome.Message);
        BrowserTypeFindExecutionResult.Completed resident =
            Assert.IsType<BrowserTypeFindExecutionResult.Completed>(
                await owner.FindTypesAsync(
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    match.FullName,
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        Assert.Contains(
            resident.Result.Activations,
            candidate => candidate.Identity == activation.Identity);

        BrowserIndexedTypeFindExecutionResult.Completed nonAdmitted =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    [aspire, extensions],
                    "Microsoft.Extensions.Logging.ILogger",
                    maximumRows: 5000,
                    resultGeneration: 2,
                    TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            nonAdmitted.Result.Find.Content.Matches,
            match => match.FullName == "Microsoft.Extensions.Logging.ILogger");
        Assert.All(
            nonAdmitted.Result.Find.Content.Matches,
            match => Assert.StartsWith(
                "aspire.",
                Assert.IsType<ExactLibrarySourceCoordinate.Package>(
                    match.Declaration.Source.Coordinate)
                    .PackageCoordinate.PackageId,
                StringComparison.Ordinal));
        Assert.All(
            nonAdmitted.Result.Find.Content.Coverage,
            coverage => Assert.Equal(
                EcosystemPackIds.Aspire,
                coverage.Source.Ecosystem));

        BrowserIndexedTypeFindExecutionResult.Completed limited =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                    owner,
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    [aspire, extensions],
                    "*",
                    maximumRows: 1,
                    resultGeneration: 3,
                    TestContext.Current.CancellationToken));
        Assert.Single(limited.Result.Find.Content.Matches);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.RowLimitReached,
            limited.Result.Find.Content.Completion);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, posting.RetainedDefinitionId);
        var indexedTypeFind = new BrowserIndexedTypeFindOperation();
        BrowserIndexedTypeFindExecutionResult.Completed canceledResult =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                indexedTypeFind.Execute(
                    operation,
                    [aspire, extensions],
                    posting.RetainedDefinitionId,
                    posting.RealizationId,
                    "*",
                    maximumRows: 25,
                    resultGeneration: 0,
                    canceled.Token));
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Canceled,
            canceledResult.Result.Find.Content.Completion);

        Assert.IsType<BrowserIndexedTypeFindExecutionResult.Rejected>(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                [aspire, extensions],
                "RedisResource",
                maximumRows: 0,
                resultGeneration: 4,
                TestContext.Current.CancellationToken));
        Assert.IsType<BrowserIndexedTypeFindExecutionResult.Stale>(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                [aspire, extensions],
                "RedisResource",
                maximumRows: 25,
                resultGeneration: 3,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManagedIndexedTypeFind_ReplacementResetsAuthority()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        EcosystemFindIndexArtifact aspire = LoadFindIndex("aspire.json");
        BrowserRetainedWorkspacePosting first =
            await ActivateEcosystemAsync(
                owner,
                "first-indexed-type-find",
                EcosystemPackIds.Aspire);
        BrowserIndexedTypeFindExecutionResult.Completed firstResult =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                owner,
                first.RetainedDefinitionId,
                first.RealizationId,
                [aspire],
                "RedisResource",
                maximumRows: 25,
                resultGeneration: 1,
                TestContext.Current.CancellationToken));
        string firstAction = firstResult.Result.Activations.First(
            activation => activation.Status
                == BrowserTypeFindActivationStatus.Available).Action!;

        BrowserRetainedWorkspacePosting replacement =
            await ActivateEcosystemAsync(
                owner,
                "replacement-indexed-type-find",
                EcosystemPackIds.Aspire);
        BrowserTypeFindActivationResult.Blocked stale =
            Assert.IsType<BrowserTypeFindActivationResult.Blocked>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .TryActivateIndexedTypeFindActionAsync(
                        owner,
                        firstAction,
                        TestContext.Current.CancellationToken));
        Assert.Equal(BrowserTypeFindActivationStatus.Stale, stale.Status);

        Assert.IsType<BrowserIndexedTypeFindExecutionResult.Unavailable>(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .FindIndexedTypesAsync(
                owner,
                first.RetainedDefinitionId,
                first.RealizationId,
                [aspire],
                "RedisResource",
                maximumRows: 25,
                resultGeneration: 2,
                TestContext.Current.CancellationToken));
        BrowserIndexedTypeFindExecutionResult.Completed replacementResult =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                    owner,
                    replacement.RetainedDefinitionId,
                    replacement.RealizationId,
                    [aspire],
                    "RedisResource",
                    maximumRows: 25,
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        string replacementAction = replacementResult.Result.Activations.First(
            activation => activation.Status
                == BrowserTypeFindActivationStatus.Available).Action!;
        Assert.NotEqual(firstAction, replacementAction);
        stale = Assert.IsType<BrowserTypeFindActivationResult.Blocked>(
            await Catalog.BrowserRetainedWorkspaceActivationService
                .TryActivateIndexedTypeFindActionAsync(
                    owner,
                    firstAction,
                    TestContext.Current.CancellationToken));
        Assert.Equal(BrowserTypeFindActivationStatus.Stale, stale.Status);

        BrowserRetainedWorkspacePosting narrowed =
            await ActivateEcosystemAsync(
                owner,
                "narrowed-indexed-type-find",
                EcosystemPackIds.Aspire,
                includePackagePrefixes: false);
        BrowserIndexedTypeFindExecutionResult.Completed noPrefix =
            Assert.IsType<BrowserIndexedTypeFindExecutionResult.Completed>(
                await Catalog.BrowserRetainedWorkspaceActivationService
                    .FindIndexedTypesAsync(
                    owner,
                    narrowed.RetainedDefinitionId,
                    narrowed.RealizationId,
                    [aspire],
                    "RedisResource",
                    maximumRows: 25,
                    resultGeneration: 1,
                    TestContext.Current.CancellationToken));
        Assert.Empty(noPrefix.Result.Find.Content.Matches);
        Assert.Empty(noPrefix.Result.Find.Content.Coverage);
    }

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
            JsonObject wireCandidate =
                Assert.IsType<JsonObject>(
                    findEnvelope["content"]!["answers"]![0]!["candidates"]![0]);
            Assert.NotNull(
                wireCandidate["coordinate"]!["library_identity"]);
            Assert.NotNull(wireCandidate["module_version_id"]);
            Assert.NotNull(
                wireCandidate["observation"]!["context_order"]);
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

            string selectionJson =
                await Catalog.CatalogExports.ActivateSpotlightDestination(
                    actions[0]);
            Catalog.BrowserSpotlightActionResult selection =
                Assert.IsType<Catalog.BrowserSpotlightActionResult>(
                    JsonSerializer.Deserialize(
                        selectionJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserSpotlightActionResult));
            Assert.Equal("navigation", selection.Status);
            Assert.NotNull(selection.Navigation);
            Assert.True(
                selection.Navigation.Outcome.Kind == "Applied",
                $"{selection.Navigation.Outcome.Kind}: "
                    + selection.Navigation.Outcome.Message);
            Assert.Equal(
                "System.Text.Json.JsonSerializer",
                selection.Selection?.DefinitionId);
            Assert.Equal(
                "System.Text.Json",
                selection.Selection?.AssemblyName);
            Assert.Null(selection.Surface);
            Assert.Null(selection.SelectedType);
            Assert.Null(selection.Reason);

            string repeatedJson =
                await Catalog.CatalogExports.ActivateSpotlightDestination(
                    actions[0]);
            Catalog.BrowserSpotlightActionResult repeated =
                Assert.IsType<Catalog.BrowserSpotlightActionResult>(
                    JsonSerializer.Deserialize(
                        repeatedJson,
                        Catalog.BrowserCatalogJsonContext.Default
                            .BrowserSpotlightActionResult));
            Assert.Equal("blocked", repeated.Status);
            Assert.Equal("Stale", repeated.ActivationStatus);

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

        BrowserTypeFindCandidateActivation package =
            completed.Result.Activations.First(
                activation => activation.Source
                    == BrowserTypeFindActivationSource.Package);
        TypeDeclarationLocatorSectionResult.Evaluated projected =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                completed.Result.Find.Content);
        TypeDeclarationLocatorSectionCandidate packageCandidate =
            projected.Answers[package.Candidate.AnswerOrdinal - 1]
                .Candidates[package.Candidate.CandidateOrdinal - 1];
        BrowserTypeFindActivationResult.Navigation navigation =
            Assert.IsType<BrowserTypeFindActivationResult.Navigation>(
                await owner.ActivateTypeFindActionAsync(
                    package.Action!,
                    TestContext.Current.CancellationToken));
        Assert.True(
            navigation.Result.Consumer.Outcome.Kind
                == NavigationOutcomeKind.Applied,
            $"{navigation.Result.Consumer.Outcome.Kind}: "
                + navigation.Result.Consumer.Outcome.Message);
        Assert.Equal(
            packageCandidate.Name.ToMetadataFullName(),
            navigation.Selection.DefinitionId);

        BrowserTypeFindCandidateActivation framework =
            completed.Result.Activations.First(
                activation => activation.Source
                    == BrowserTypeFindActivationSource.Framework);
        TypeDeclarationLocatorSectionCandidate frameworkCandidate =
            projected.Answers[framework.Candidate.AnswerOrdinal - 1]
                .Candidates[framework.Candidate.CandidateOrdinal - 1];
        BrowserTypeFindActivationResult.Framework frameworkResult =
            Assert.IsType<BrowserTypeFindActivationResult.Framework>(
                await owner.ActivateTypeFindActionAsync(
                    framework.Action!,
                    TestContext.Current.CancellationToken));
        BrowserFrameworkDeclarationEffect.Type frameworkType =
            Assert.IsType<BrowserFrameworkDeclarationEffect.Type>(
                frameworkResult.Effect);
        Assert.Equal(
            frameworkCandidate.Name.ToMetadataFullName(),
            frameworkType.SelectedType.DefinitionId);
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

    static async Task<BrowserRetainedWorkspacePosting>
        ActivateEcosystemAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            EcosystemPackId ecosystem,
            bool includePackagePrefixes = true)
    {
        WorkspacePlan catalogPlan =
            EcosystemPackCatalog.CreateWorkspacePlan([ecosystem]);
        WorkspaceRegistration.Ecosystem registration =
            Assert.Single(
                catalogPlan.Registrations
                    .OfType<WorkspaceRegistration.Ecosystem>(),
                candidate => candidate.Declaration.Id.Value
                    == ecosystem.Value);
        if (!includePackagePrefixes)
        {
            WorkspaceEcosystemRegistrationDeclaration declaration =
                registration.Declaration;
            registration = new WorkspaceRegistration.Ecosystem(
                new WorkspaceEcosystemRegistrationDeclaration(
                    declaration.Id,
                    declaration.NamespaceRoots,
                    declaration.CorePackages,
                    declaration.Populations.Where(population =>
                        population is not
                            WorkspaceEcosystemPopulationDeclaration
                                .PackagePrefix),
                    declaration.IntegrationScanner));
        }
        var plan = new WorkspacePlan([registration]);
        var request = new BrowserRetainedWorkspaceActivationRequest(
            retainedDefinitionId,
            retainedDefinitionId,
            $"/ecosystems/{ecosystem.Value}",
            new CompleteRestorationRequestBasis
                .RegistrationOnlyEcosystemInput(
                    plan,
                    registration.Declaration.Id,
                    new ViewFacetId("ecosystem.overview")));
        return Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    request,
                    TestContext.Current.CancellationToken)).Posting;
    }

    static EcosystemFindIndexArtifact LoadFindIndex(string fileName)
    {
        byte[] bytes = File.ReadAllBytes(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "DotnetInspector.Ecosystems",
                "FindIndexes",
                fileName));
        return Assert.IsType<EcosystemFindIndexLoadResult.Available>(
            EcosystemFindIndexJson.Load(bytes)).Artifact;
    }
}
