using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition("Package query operations", DisableParallelization = true)]
public sealed class PackageQueryOperationCollection;

[Collection("Package query operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserPackageQueryOperationsTests
{
    [Theory]
    [InlineData("Newtonsoft.Json", false, "Newtonsoft.Json", 1)]
    [InlineData("Newtonsoft.*", true, "Newtonsoft.", 200)]
    [InlineData("Newtonsoft*", true, "Newtonsoft", 200)]
    public void PackagePlan_DispatchesExactAndLiteralPrefixInput(
        string text,
        bool prefix,
        string expected,
        int expectedCandidates)
    {
        var accepted = Assert.IsType<PackageQueryPlanResult.Accepted>(
            BrowserPackageQueryOperations.Plan(
                text,
                [],
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: true));

        Assert.Equal(expectedCandidates, accepted.Plan.MaximumCandidates);
        Assert.True(accepted.Plan.IncludePrerelease);
        if (prefix)
        {
            var input = Assert.IsType<SourceSelector.PackagePrefix>(
                accepted.Plan.PackageInput);
            Assert.Equal(expected, input.Request.Prefix);
        }
        else
        {
            var input = Assert.IsType<SourceSelector.Package>(
                accepted.Plan.PackageInput);
            Assert.Equal(expected, input.Coordinate.PackageId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Newton*soft")]
    public void PackagePlan_RejectsBlankAndMalformedInputWithoutDiscovery(
        string text)
    {
        var rejected = Assert.IsType<PackageQueryPlanResult.Rejected>(
            BrowserPackageQueryOperations.Plan(
                text,
                [],
                maximumCandidates: 200,
                maximumMatches: 10,
                includePrerelease: false));

        Assert.Equal(
            PackageQueryRequestFailureReason.InvalidPackageInput,
            rejected.Failure.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Project_ExactCompletionRetainsAuthoritativeSourceSelection(
        int sourceCandidates)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create());
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Missing.Package"),
            source.Source,
            CandidateLimit: 1,
            MatchLimit: 100,
            Candidates: sourceCandidates,
            Matches: sourceCandidates,
            Failures: 0,
            PackageQueryCompletionKind.ExactPackageComplete)
        {
            SourceCandidates = sourceCandidates,
        };

        BrowserPackageQueryCompletion completion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary)).Completion!;

        Assert.Equal(
            BrowserPackageQueryCompletionKind.ExactPackageComplete,
            completion.Kind);
        Assert.Equal(sourceCandidates, completion.Candidates);
        Assert.Equal(sourceCandidates, completion.Matches);
        Assert.Equal(sourceCandidates, completion.SourceCandidates);
    }

    [Fact]
    public void Facets_MatchProductCatalogOrderAndMetadata()
    {
        BrowserPackageQueryFacetCatalog catalog =
            BrowserPackageQueryOperations.Facets();

        Assert.Equal(PackageQuery.Facets.Length, catalog.Facets.Length);
        for (int index = 0; index < catalog.Facets.Length; index++)
        {
            PackageQueryFacetDescriptor expected = PackageQuery.Facets[index];
            BrowserPackageQueryFacetDescriptor actual = catalog.Facets[index];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Label, actual.Label);
            Assert.Equal(expected.Summary, actual.Summary);
            Assert.Equal(expected.Weight, actual.Weight);
            Assert.Equal(expected.SelectionGroupId, actual.SelectionGroupId);
            Assert.Equal(
                expected.CombinesWithinSelectionGroup,
                actual.CombinesWithinSelectionGroup);
            Assert.Equal(expected.DisplayGroupId, actual.DisplayGroupId);
            Assert.Equal(expected.DisplayGroupLabel, actual.DisplayGroupLabel);
            Assert.Equal(
                expected.Tier == PackageQueryFacetTier.Nuspec
                    ? BrowserPackageQueryFacetTier.Nuspec
                    : BrowserPackageQueryFacetTier.PackageContent,
                actual.Tier);
        }
    }

    [Fact]
    public void Project_PreservesFailureAndCompletionEvidence()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageQueryFailureKind.ManifestAcquisition,
            "manifest unavailable",
            PackageManifestFailureReason.InvalidDependencyContract);
        var summary = new PackageQuerySummary(
            new InertString(TextPolicy.Field, "Contoso."),
            source.Source,
            CandidateLimit: 200,
            MatchLimit: 100,
            Candidates: 5,
            Matches: 2,
            Failures: 1,
            PackageQueryCompletionKind.CandidateLimitReached);

        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));
        BrowserPackageQueryEvent projectedCompletion =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Completed(summary));
        BrowserPackageQueryEvent projectedProgress =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Progress(
                    new PackageQueryProgress(
                        PackageQueryProgressPhase.Manifest,
                        Completed: 3,
                        Limit: 20)));
        var profile = new PackageProfileMatch(
            "Contoso.Package",
            "1.0.0",
            ["Contoso", "Fabrikam"],
            42,
            Verified: true,
            source.Source,
            new PackageManifestFacts(
                PackageSourceCoordinate.Create(
                    "Contoso.Package",
                    "1.0.0"),
                ManifestVersion: "nuspec",
                Description: new InertString(
                    TextPolicy.Field,
                    "Package description."),
                Authors: "Contoso; Fabrikam",
                Repository: "https://example.test/contoso/package",
                RepositoryType: "git",
                RepositoryCommit: "0123456789abcdef",
                License: "MIT",
                LicenseUrl: "https://example.test/licenses/mit",
                PackageTypes: ["DotnetTool"],
                IsToolPackage: true,
                ReadmeFile: "README.md",
                DependencyGroups:
                [
                    new DeclaredPackageDependencyGroup(
                        "net10.0",
                        [
                            new DeclaredPackageDependency(
                                "Contoso.Dependency",
                                "[2.0.0,3.0.0)"),
                        ]),
                ])
            {
                IconFile = "icon.png",
                IconUrl = "https://example.test/icon.png",
                IdentityProvenance =
                    PackageManifestIdentityProvenance.SelfAttested,
            });
        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(
                    new PackageQueryMatch(
                        profile,
                        PackageQueryFacetTier.Nuspec,
                        [])));
        string expectedProducer =
            source.Source.Producer.Display.ToString();

        Assert.Equal(
            "https://api.nuget.org:443/v3/index.json",
            expectedProducer);
        Assert.Equal(BrowserPackageQueryEventKind.Failure, projectedFailure.Kind);
        Assert.Equal(
            BrowserPackageQueryFailureKind.ManifestAcquisition,
            projectedFailure.Failure!.Kind);
        Assert.Equal("manifest unavailable", projectedFailure.Failure.Message);
        Assert.Equal(
            BrowserPackageQueryManifestFailureReason.InvalidDependencyContract,
            projectedFailure.Failure.ManifestFailureReason);
        Assert.Equal(
            expectedProducer,
            projectedFailure.Failure.Producer);
        Assert.Equal(
            BrowserPackageQueryEventKind.Completed,
            projectedCompletion.Kind);
        Assert.Equal(
            BrowserPackageQueryCompletionKind.CandidateLimitReached,
            projectedCompletion.Completion!.Kind);
        Assert.Equal(
            expectedProducer,
            projectedCompletion.Completion.Producer);
        Assert.Equal(200, projectedCompletion.Completion.CandidateLimit);
        Assert.Equal(100, projectedCompletion.Completion.MatchLimit);
        Assert.Equal(
            expectedProducer,
            projectedMatch.Row!.Producer);
        Assert.Equal(
            ["Contoso", "Fabrikam"],
            projectedMatch.Row.Owners);
        BrowserPackageQueryManifest projectedManifest =
            Assert.IsType<BrowserPackageQueryManifest>(
                projectedMatch.Row.Manifest);
        Assert.Equal("contoso.package", projectedManifest.PackageId);
        Assert.Equal("1.0.0", projectedManifest.Version);
        Assert.Equal("nuspec", projectedManifest.ManifestVersion);
        Assert.Equal("Package description.", projectedManifest.Description);
        Assert.Equal("Contoso; Fabrikam", projectedManifest.Authors);
        Assert.Equal(
            "https://example.test/contoso/package",
            projectedManifest.Repository);
        Assert.Equal("git", projectedManifest.RepositoryType);
        Assert.Equal(
            "0123456789abcdef",
            projectedManifest.RepositoryCommit);
        Assert.Equal("MIT", projectedManifest.License);
        Assert.Equal(
            "https://example.test/licenses/mit",
            projectedManifest.LicenseUrl);
        Assert.Equal(["DotnetTool"], projectedManifest.PackageTypes);
        Assert.True(projectedManifest.IsToolPackage);
        Assert.Equal("README.md", projectedManifest.ReadmeFile);
        BrowserPackageQueryDeclaredDependencyGroup projectedGroup =
            Assert.Single(projectedManifest.DependencyGroups);
        Assert.Equal("net10.0", projectedGroup.TargetFramework);
        Assert.False(projectedGroup.IsImplicitManifestGroup);
        BrowserPackageQueryDeclaredDependency projectedDependency =
            Assert.Single(projectedGroup.Dependencies);
        Assert.Equal("Contoso.Dependency", projectedDependency.Id);
        Assert.Equal("[2.0.0,3.0.0)", projectedDependency.VersionRange);
        Assert.Equal("icon.png", projectedManifest.IconFile);
        Assert.Equal(
            "https://example.test/icon.png",
            projectedManifest.IconUrl);
        Assert.Equal(
            BrowserPackageQueryManifestIdentityProvenance.SelfAttested,
            projectedManifest.IdentityProvenance);
        Assert.Equal(
            BrowserPackageQueryEventKind.Progress,
            projectedProgress.Kind);
        Assert.Equal(
            BrowserPackageQueryProgressPhase.Manifest,
            projectedProgress.Progress!.Phase);
        Assert.Equal(3, projectedProgress.Progress.Completed);
        Assert.Equal(20, projectedProgress.Progress.Limit);
    }

    [Fact]
    public void Project_PreservesPackageContentTierAndFailure()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageProfileMatch package = new(
            "Contoso.Tool",
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest(
                "Contoso.Tool",
                "1.0.0",
                isToolPackage: true));
        var match = new PackageQueryMatch(
            package,
            PackageQueryFacetTier.PackageContent,
            [
                new PackageQueryEvidence(
                    PackageQuery.EmbeddedSkillFacetId,
                    new InertString(
                        TextPolicy.Prose,
                        "2 skill documents: skills/SKILL.md, skills/build/SKILL.md."))
                {
                    Scope = PackageQueryEvidenceScope.Package,
                    Summary = new PackageQueryEvidenceSummary(
                        2,
                        [
                            new InertString(TextPolicy.Field, "skills/SKILL.md"),
                            new InertString(
                                TextPolicy.Field,
                                "skills/build/SKILL.md"),
                        ]),
                },
                new PackageQueryEvidence(
                    "package.query.source-selection",
                    new InertString(
                        TextPolicy.Prose,
                        "Selected by producer ranking."))
                {
                    Scope = PackageQueryEvidenceScope.Query,
                },
            ]);
        var failure = new PackageQueryFailure(
            "Contoso.Bad",
            "1.0.0",
            source.Source,
            PackageQueryFailureKind.PackageContentAcquisition,
            "package payload unavailable");

        BrowserPackageQueryEvent projectedMatch =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Match(match));
        BrowserPackageQueryEvent projectedFailure =
            BrowserPackageQueryOperations.Project(
                new PackageQueryEvent.Failure(failure));

        Assert.Equal(
            BrowserPackageQueryFacetTier.PackageContent,
            projectedMatch.Row!.Tier);
        Assert.Collection(
            projectedMatch.Row.Evidence,
            evidence =>
            {
                Assert.Equal(
                    BrowserPackageQueryEvidenceScope.Package,
                    evidence.Scope);
                Assert.Equal(2, evidence.Summary!.Count);
                Assert.Equal(
                    ["skills/SKILL.md", "skills/build/SKILL.md"],
                    evidence.Summary.Preview);
            },
            evidence =>
            {
                Assert.Equal(
                    BrowserPackageQueryEvidenceScope.Query,
                    evidence.Scope);
                Assert.Null(evidence.Summary);
            });
        Assert.Equal(
            BrowserPackageQueryFailureKind.PackageContentAcquisition,
            projectedFailure.Failure!.Kind);
    }

    [Fact]
    public async Task Serialize_RoundTripsThroughBrowserJsonContext()
    {
        var queryEvent = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "Contoso.",
                PackageProducerIdentity.NuGetOrg.Display.ToString(),
                CandidateLimit: 200,
                MatchLimit: 100,
                Candidates: 5,
                Matches: 2,
                Failures: 0,
                BrowserPackageQueryCompletionKind.Exhausted));

        BrowserPackageQueryOperations.StartSerializationPreparation();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        string json = BrowserPackageQueryOperations.Serialize(queryEvent);
        BrowserPackageQueryEvent? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        Assert.Equal(queryEvent, roundTripped);
    }

    [Fact]
    public async Task Serialize_AllowsOwnerValidUnicodeManifestToExpandPastDecodedBudget()
    {
        string packageType = new(
            '\u00E9',
            PackageManifestFactsQuery.MaxScalarCharacters);
        string[] packageTypes = Enumerable
            .Repeat(packageType, 8)
            .ToArray();
        var queryEvent = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Match,
            Row: new BrowserPackageQueryRow(
                "Contoso.Unicode",
                "1.0.0",
                BrowserPackageQueryFacetTier.Nuspec,
                Evidence: [],
                TotalDownloads: null,
                Verified: null,
                Producer: PackageProducerIdentity.NuGetOrg.Display.ToString(),
                RootRequest: "root1:Contoso.Unicode@1.0.0")
            {
                Manifest = new BrowserPackageQueryManifest(
                    "Contoso.Unicode",
                    "1.0.0",
                    "nuspec",
                    Description: null,
                    Authors: null,
                    Repository: null,
                    RepositoryType: null,
                    RepositoryCommit: null,
                    License: null,
                    LicenseUrl: null,
                    packageTypes,
                    IsToolPackage: false,
                    ReadmeFile: null,
                    DependencyGroups: [],
                    IconFile: null,
                    IconUrl: null,
                    BrowserPackageQueryManifestIdentityProvenance.ExpectedCoordinate),
            },
            Failure: null,
            Completion: null);

        Assert.True(packageTypes.Length <= PackageManifestFactsQuery.MaxPackageTypes);
        Assert.True(
            packageTypes.Sum(value => value.Length)
            < PackageManifestFactsQuery.MaxManifestCharacters);

        BrowserPackageQueryOperations.StartSerializationPreparation();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        string json = BrowserPackageQueryOperations.Serialize(queryEvent);
        BrowserPackageQueryEvent? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        Assert.True(json.Length > 1_048_576);
        Assert.Contains("\\u00E9", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            packageTypes,
            roundTripped!.Row!.Manifest!.PackageTypes);
    }

    [Fact]
    public async Task ListFacets_PreparesSerializationWithoutChangingCatalog()
    {
        string json = PackageExports.ListPackageQueryFacets();
        await BrowserPackageQueryOperations.WaitForSerializationPreparationAsync();
        BrowserPackageQueryFacetCatalog? catalog = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryFacetCatalog);

        Assert.NotNull(catalog);
        Assert.Equal(
            BrowserPackageQueryOperations.Facets().Facets,
            catalog.Facets);
    }

    [Fact]
    public async Task Coordinator_TargetsCancellationAndCreditByOperationId()
    {
        BrowserManagedOperationId firstId =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        BrowserManagedOperationId secondId =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        var releaseFirst =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReceivedCredit =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReceivedCredit =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondObservedCancellation =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<BrowserManagedOperationResult<int, string, string>> first =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                firstId,
                initialMatchCredit: 1,
                eventCallback: null,
                async (credit, _, token) =>
                {
                    await credit.WaitAsync(token);
                    await credit.WaitAsync(token);
                    firstReceivedCredit.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                    return 1;
                });
        Task<BrowserManagedOperationResult<int, string, string>> second =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                secondId,
                initialMatchCredit: 1,
                eventCallback: null,
                async (credit, _, token) =>
                {
                    await credit.WaitAsync(token);
                    try
                    {
                        await credit.WaitAsync(token);
                        secondReceivedCredit.SetResult();
                        return 2;
                    }
                    catch (OperationCanceledException)
                    {
                        secondObservedCancellation.SetResult();
                        await releaseSecond.Task;
                        throw;
                    }
                });

        var granted = Assert.IsType<
            BrowserPackageQueryMatchCreditRequestResult.Granted>(
                BrowserPackageQueryOperationCoordinator.RequestMatches(
                    firstId,
                    additionalMatchCredit: 1));
        Assert.Equal(1, granted.AdditionalMatchCredit);
        await firstReceivedCredit.Task;
        Assert.False(secondReceivedCredit.Task.IsCompleted);

        var requested = Assert.IsType<
            BrowserManagedCancellationRequestResult.Requested>(
                BrowserPackageQueryOperationCoordinator.RequestCancellation(
                    secondId,
                    BrowserManagedOperationCancelReason.User));
        Assert.Equal(BrowserManagedOperationCancelReason.User, requested.Reason);
        await secondObservedCancellation.Task;
        var repeated = Assert.IsType<
            BrowserManagedCancellationRequestResult.AlreadyRequested>(
                BrowserPackageQueryOperationCoordinator.RequestCancellation(
                    secondId,
                    BrowserManagedOperationCancelReason.Timeout));
        Assert.Equal(BrowserManagedOperationCancelReason.User, repeated.Reason);

        releaseSecond.SetResult();
        var canceled = Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Canceled>(
                await second);
        Assert.Equal(BrowserManagedOperationCancelReason.User, canceled.Reason);
        Assert.False(first.IsCompleted);

        releaseFirst.SetResult();
        Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await first);
        Assert.IsType<BrowserPackageQueryMatchCreditRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestMatches(firstId, 1));
        Assert.IsType<BrowserPackageQueryMatchCreditRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestMatches(secondId, 1));
    }

    [Fact]
    public async Task Coordinator_RejectsDuplicateActiveIdAndReadmitsAfterRelease()
    {
        BrowserManagedOperationId id =
            BrowserManagedOperationId.From(Guid.NewGuid().ToString());
        var release =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<BrowserManagedOperationResult<int, string, string>> original =
            BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                id,
                initialMatchCredit: 1,
                eventCallback: null,
                async (_, _, token) =>
                {
                    await release.Task.WaitAsync(token);
                    return 1;
                });

        var error = await Assert.ThrowsAsync<
            BrowserManagedOperationBoundaryException>(
                () => BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                    id,
                    initialMatchCredit: 1,
                    eventCallback: null,
                    (_, _, _) => Task.FromResult(2)));
        Assert.Equal("duplicate-active-operation", error.FailureKind);
        Assert.False(original.IsCompleted);

        release.SetResult();
        Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await original);
        Assert.IsType<BrowserManagedCancellationRequestResult.NotActive>(
            BrowserPackageQueryOperationCoordinator.RequestCancellation(
                id,
                BrowserManagedOperationCancelReason.User));

        var readmitted = Assert.IsType<
            BrowserManagedOperationResult<int, string, string>.Succeeded>(
                await BrowserPackageQueryOperationCoordinator.RunAsync<int, object>(
                    id,
                    initialMatchCredit: 1,
                    eventCallback: null,
                    (_, _, _) => Task.FromResult(3)));
        Assert.Equal(3, readmitted.Value);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidPlansBeforeStartingSourceWork()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BrowserPackageQueryOperations.ExecuteAsync(
                "Contoso.Package",
                ["package.query.unknown"],
                maximumCandidates: 200,
                maximumMatches: 100,
                includePrerelease: false,
                matchCredit: null,
                _ => { },
                TestContext.Current.CancellationToken));

        Assert.Contains("facet IDs are unknown", error.Message);
    }

    [Fact]
    public async Task PumpAsync_EmitsOnlyNonterminalEventsAndReturnsCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Progress progress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Search,
                Completed: 0,
                Limit: 1));
        PackageQueryEvent.Failure failure = new(
            new PackageQueryFailure(
                PackageId: null,
                Version: null,
                source.Source,
                PackageQueryFailureKind.Search,
                "search unavailable"));
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 1,
                PackageQueryCompletionKind.Failed));
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent returned =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(progress, failure, completed),
                matchCredit: null,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                BrowserPackageQueryEventKind.Progress,
                BrowserPackageQueryEventKind.Failure,
            ],
            emitted.Select(item => item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, returned.Kind);
    }

    [Fact]
    public async Task EventObserverUsesEnvelopeContentForCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Progress progress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Search,
                Completed: 0,
                Limit: 20));
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
                matchCredit: null,
                emitted.Add,
                deadline: null);

        await observer.ObserveAsync(
            progress,
            TestContext.Current.CancellationToken);
        await observer.ObserveAsync(
            completed,
            TestContext.Current.CancellationToken);
        var envelope = new InspectionEnvelope<
            ImmutableArray<PackageQueryEvent>>(
                [progress, completed],
                new InspectionShare.NonProjectable(
                    "package-query/share",
                    "No canonical Workspace packet."));
        BrowserPackageQueryInspection inspection = observer.Complete(envelope);

        Assert.Equal(
            [
                BrowserPackageQueryEventKind.Progress,
                BrowserPackageQueryEventKind.Completed,
            ],
            inspection.Content.Select(queryEvent => queryEvent.Kind));
        Assert.Equal(
            BrowserInspectionShareKind.NonProjectable,
            inspection.Share.Kind);
        Assert.Equal("package-query/share", inspection.Share.Path);
        Assert.Empty(inspection.Diagnostics);
        Assert.Single(emitted);
        Assert.Equal(
            BrowserPackageQueryEventKind.Progress,
            emitted[0].Kind);
    }

    [Fact]
    public void PackageQueryResultPreservesInspectionEnvelopeProjection()
    {
        var completed = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "Contoso.",
                PackageProducerIdentity.NuGetOrg.Display.ToString(),
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                BrowserPackageQueryCompletionKind.Exhausted));
        var inspection = new BrowserPackageQueryInspection(
            [completed],
            new BrowserInspectionShare(
                BrowserInspectionShareKind.NonProjectable,
                FullUrl: null,
                Packet: null,
                "package-query/share",
                "No canonical Workspace packet."),
            [
                new BrowserInspectionDiagnostic(
                    "package-query-note",
                    "Information",
                    "Package Query completed.",
                    Correspondence: null),
            ]);
        var result = BrowserPackageQueryResult.From(
            new BrowserManagedOperationResult<
                BrowserPackageQueryInspection,
                string,
                string>.Succeeded(inspection));

        string json = JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
        BrowserPackageQueryResult? roundTripped = JsonSerializer.Deserialize(
            json,
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.Version);
        Assert.Null(roundTripped.Value);
        Assert.NotNull(roundTripped.Inspection);
        Assert.Equal(
            inspection.Content,
            roundTripped.Inspection.Content);
        Assert.Equal(inspection.Share, roundTripped.Inspection.Share);
        Assert.Equal(
            inspection.Diagnostics,
            roundTripped.Inspection.Diagnostics);
    }

    [Fact]
    public async Task EventObserverPausesMatchDeliveryUntilCreditIsReplenished()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
            matchCredit,
            emitted.Add,
            deadline: null);

        await observer.ObserveAsync(
            MatchEvent("Contoso.One"),
            TestContext.Current.CancellationToken);
        Task pending = observer.ObserveAsync(
                MatchEvent("Contoso.Two"),
                TestContext.Current.CancellationToken)
            .AsTask();

        Assert.Single(emitted);
        Assert.False(pending.IsCompleted);
        Assert.True(matchCredit.TryAdd(1));
        await pending;
        Assert.Equal(
            ["Contoso.One", "Contoso.Two"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task EventObserverCallerCancellationReleasesWaitingMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        var observer = new BrowserPackageQueryOperations.EventObserver(
            matchCredit,
            emitted.Add,
            deadline: null);

        await observer.ObserveAsync(
            MatchEvent("Contoso.One"),
            cancellation.Token);
        Task pending = observer.ObserveAsync(
                MatchEvent("Contoso.Two"),
                cancellation.Token)
            .AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending);
        Assert.Equal(
            ["Contoso.One", "Contoso.Two"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task EventObserverActiveWorkExpiryDoesNotPublishWaitingMatch()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        using var callerCancellation = new CancellationTokenSource();
        var emitted = new List<BrowserPackageQueryEvent>();

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                async deadline =>
                {
                    var observer =
                        new BrowserPackageQueryOperations.EventObserver(
                            matchCredit,
                            emitted.Add,
                            deadline);
                    await observer.ObserveAsync(
                        MatchEvent("Contoso.One"),
                        deadline.Token);
                    while (!deadline.HasExpired)
                        Thread.SpinWait(100);
                    await observer.ObserveAsync(
                        MatchEvent("Contoso.Two"),
                        deadline.Token);
                    return 0;
                },
                TimeSpan.FromMilliseconds(100),
                callerCancellation.Token));

        Assert.Single(emitted);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task PumpAsync_RejectsAnEventAfterCompletion()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageQueryEvent.Completed completed = new(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: 0,
                Matches: 0,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
        PackageQueryEvent.Progress lateProgress = new(
            new PackageQueryProgress(
                PackageQueryProgressPhase.Manifest,
                Completed: 1,
                Limit: 20));
        var emitted = new List<BrowserPackageQueryEvent>();

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BrowserPackageQueryOperations.PumpAsync(
                    Events(completed, lateProgress),
                    matchCredit: null,
                    emitted.Add,
                    TestContext.Current.CancellationToken));

        Assert.Contains("after completion", error.Message);
        Assert.Empty(emitted);
    }

    [Fact]
    public async Task PumpAsync_PausesMatchDeliveryUntilCreditIsReplenished()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 2);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => established == 3);
        Assert.Equal(2, emitted.Count);
        Assert.False(pumping.IsCompleted);

        Assert.True(matchCredit.TryAdd(1));
        BrowserPackageQueryEvent completed = await pumping;

        Assert.Equal(3, emitted.Count);
        Assert.All(
            emitted,
            item => Assert.Equal(
                BrowserPackageQueryEventKind.Match,
                item.Kind));
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ReadsCompletionWithoutAdditionalMatchCredit()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent completed =
            await BrowserPackageQueryOperations.PumpAsync(
                Events(MatchEvent("Contoso.One"), CompletedEvent()),
                matchCredit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_AssemblyAssessmentDoesNotSpendMatchCredit()
    {
        var match = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Match,
            Row: new BrowserPackageQueryRow(
                "Contoso.Match", "1.0.0",
                BrowserPackageQueryFacetTier.Assembly,
                [new BrowserPackageQueryEvidence(
                    "il-string-literal-contains",
                    "Matched.",
                    BrowserPackageQueryEvidenceScope.Package,
                    null)],
                TotalDownloads: null,
                Verified: null,
                Producer: "nuget.org",
                RootRequest: "opaque-match-root"),
            Failure: null,
            Completion: null);
        var assessment = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Assessment,
            Row: null,
            Failure: null,
            Completion: null,
            Assessment: new BrowserPackageAssemblyAssessment(
                "Contoso.Other", "1.0.0",
                BrowserPackageAssemblyAssessmentKind.NoMatch,
                "No decoded literal matched in the selected assembly.",
                "lib/net10.0/Contoso.Other.dll",
                "opaque-assessment-root"));
        var completion = new BrowserPackageQueryEvent(
            BrowserPackageQueryEventKind.Completed,
            Row: null,
            Failure: null,
            Completion: new BrowserPackageQueryCompletion(
                "", "nuget.org", CandidateLimit: 2, MatchLimit: 2,
                Candidates: 2, Matches: 1, Failures: 0,
                BrowserPackageQueryCompletionKind.ExplicitCandidatesComplete,
                SemanticMisses: 1, NotApplicable: 0,
                Scope: "Selector-issued primary implementation assemblies"));
        using var credit = new BrowserPackageQueryMatchCredit(initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();

        BrowserPackageQueryEvent returned =
            await BrowserPackageQueryOperations.PumpAsync(
                BrowserEvents(match, assessment, completion),
                static item => item,
                credit,
                emitted.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal([match, assessment], emitted);
        Assert.Same(completion, returned);
    }

    [Fact]
    public async Task PumpAsync_CancellationReleasesAWaitingMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageQueryOperations.PumpAsync(
                CountedMatchEvents(() => established++),
                matchCredit,
                emitted.Add,
                cancellation.Token);

        await WaitUntilAsync(() => established == 2);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task PumpAsync_ConsumerWaitOutlivesActiveWorkBudget()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

        Assert.Single(emitted);
        await Task.Delay(1200, TestContext.Current.CancellationToken);
        Assert.False(pumping.IsCompleted);
        Assert.Single(emitted);

        Assert.True(matchCredit.TryAdd(2));
        BrowserPackageQueryEvent completed = await pumping;
        Assert.Equal(3, emitted.Count);
        Assert.Equal(BrowserPackageQueryEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task PumpAsync_ActiveWorkExpiryDoesNotPublishUncreditedMatch()
    {
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        using var callerCancellation = new CancellationTokenSource();
        var emitted = new List<BrowserPackageQueryEvent>();
        int established = 0;

        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() =>
                    {
                        if (++established == 2)
                        {
                            while (!deadline.HasExpired)
                                Thread.SpinWait(100);
                        }
                    }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromMilliseconds(100),
                callerCancellation.Token));

        Assert.Equal(2, established);
        Assert.Single(emitted);
        Assert.False(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task PumpAsync_CallerCancellationStillReleasesBudgetPausedWait()
    {
        using var callerCancellation = new CancellationTokenSource();
        using var matchCredit = new BrowserPackageQueryMatchCredit(
            initialMatchCredit: 1);
        var emitted = new List<BrowserPackageQueryEvent>();
        Task<BrowserPackageQueryEvent> pumping =
            BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageQueryOperations.PumpAsync(
                    CountedMatchEvents(() => { }),
                    matchCredit,
                    emitted.Add,
                    deadline.Token,
                    deadline),
                TimeSpan.FromSeconds(1),
                callerCancellation.Token);

        Assert.Single(emitted);
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pumping);
        Assert.Equal(
            ["Contoso.1", "Contoso.2"],
            emitted.Select(item => item.Row!.PackageId));
    }

    [Fact]
    public async Task PackageOperation_ConsumerWaitDoesNotResetSpentBudget()
    {
        await Assert.ThrowsAsync<TimeoutException>(
            () => BrowserPackageWorkspace.RunPackageOperationAsync<int>(
                async deadline =>
                {
                    while (deadline.Remaining > TimeSpan.FromMilliseconds(500))
                        Thread.SpinWait(100);
                    TimeSpan remaining = deadline.Remaining;

                    await deadline.WaitForConsumerAsync(token =>
                        new ValueTask(Task.Delay(1200, token)));

                    Assert.True(deadline.Remaining <= remaining);
                    Assert.False(deadline.Token.IsCancellationRequested);
                    await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
                    return 0;
                },
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    static async IAsyncEnumerable<PackageQueryEvent> CountedMatchEvents(
        Action established)
    {
        for (int index = 1; index <= 3; index++)
        {
            established();
            yield return MatchEvent($"Contoso.{index}");
        }
        yield return CompletedEvent(matches: 3);
        await Task.CompletedTask;
    }

    static async IAsyncEnumerable<PackageQueryEvent> Events(
        params PackageQueryEvent[] events)
    {
        await Task.CompletedTask;
        foreach (PackageQueryEvent queryEvent in events)
            yield return queryEvent;
    }

    static async IAsyncEnumerable<BrowserPackageQueryEvent> BrowserEvents(
        params BrowserPackageQueryEvent[] events)
    {
        await Task.CompletedTask;
        foreach (BrowserPackageQueryEvent queryEvent in events)
            yield return queryEvent;
    }

    static PackageQueryEvent MatchEvent(string packageId)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        var package = new PackageProfileMatch(
            packageId,
            "1.0.0",
            [],
            TotalDownloads: 42,
            Verified: false,
            source.Source,
            Manifest(packageId, "1.0.0", isToolPackage: false));
        return new PackageQueryEvent.Match(
            new PackageQueryMatch(
                package,
                PackageQueryFacetTier.Nuspec,
                [
                    new PackageQueryEvidence(
                        "package.query.source-verified",
                        new InertString(TextPolicy.Prose, "Matched.")),
                ]));
    }

    static PackageQueryEvent CompletedEvent(int matches = 1)
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        return new PackageQueryEvent.Completed(
            new PackageQuerySummary(
                new InertString(TextPolicy.Field, "Contoso."),
                source.Source,
                CandidateLimit: 20,
                MatchLimit: 20,
                Candidates: matches,
                Matches: matches,
                Failures: 0,
                PackageQueryCompletionKind.Exhausted));
    }

    static PackageManifestFacts Manifest(
        string packageId,
        string version,
        bool isToolPackage) =>
        new(
            PackageSourceCoordinate.Create(packageId, version),
            "nuspec",
            Description: null,
            Authors: null,
            Repository: null,
            RepositoryType: null,
            RepositoryCommit: null,
            License: null,
            LicenseUrl: null,
            PackageTypes: isToolPackage ? ["DotnetTool"] : [],
            IsToolPackage: isToolPackage,
            ReadmeFile: null,
            DependencyGroups: []);
}
