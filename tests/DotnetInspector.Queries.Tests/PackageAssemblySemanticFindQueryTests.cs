using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using Inspector.Artifacts.Workspaces;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblySemanticFindQueryTests
{
    const string Framework = "net11.0";
    const string Marker = "shared-literal-use-marker";
    const string Version = "1.0.0";

    static byte[] MatchImage =>
        File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());

    static byte[] NoMatchImage =>
        File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "BindingComposition",
                "package",
                "System.Text.Json.dll"));

    [Fact]
    public async Task CompleteFiveCandidatePopulationPreservesFingerprintAndEnvelope()
    {
        await using var fixture = new SemanticFindSourceFixture();
        string[] packageIds =
        [
            "Contoso.Match",
            "Contoso.Miss.One",
            "Contoso.Miss.Two",
            "Contoso.Miss.Three",
            "Contoso.Miss.Four",
        ];
        await fixture.CacheAssemblyAsync(
            packageIds[0],
            MatchImage);
        foreach (string packageId in packageIds[1..])
            await fixture.CacheAssemblyAsync(packageId, NoMatchImage);

        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                packageIds);
        var sink = new RecordingQuerySink();

        InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
            await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                sink,
                TestContext.Current.CancellationToken);

        PackageAssemblySemanticQueryDocument document = envelope.Content;
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
        Assert.Equal(5, document.CandidateCount);
        Assert.Equal(5, document.EvaluatedCandidateCount);
        Assert.Equal(0, document.NotEvaluatedCount);
        Assert.Equal(1, document.MatchedPackageCount);
        Assert.Equal(2, document.OccurrenceCount);
        Assert.Equal(4, document.SemanticMissCount);
        Assert.Equal(0, document.NotApplicableCount);
        Assert.Equal(0, document.FailureCount);
        Assert.True(document.Completion.IsRequestedPopulationComplete);
        Assert.True(
            document.Completion.AllCandidatesHaveTerminalOutcomes);
        Assert.False(document.Completion.HasFailures);
        Assert.True(document.Completion.IsSemanticEvaluationComplete);
        Assert.False(document.Completion.IsOperationDeadlineExpired);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates,
            document.Completion.Population);
        Assert.Equal(
            document.CandidateOutcomes,
            sink.Outcomes);
        PackageAssemblySemanticQueryResult result =
            Assert.Single(document.Results);
        Assert.Equal(1, result.CandidateOrdinal);
        Assert.Equal(
            population.Candidates[0].Correspondence,
            result.Correspondence);
        Assert.Equal(
            "lib/net11.0/Contoso.Match.dll",
            result.SelectedAsset.Asset.Path.ToString());
        Assert.Equal(
            Assert.IsType<
                PackageAssemblySemanticQueryCandidateOutcome.Matched>(
                document.CandidateOutcomes[0])
            .Result.Occurrences,
            result.Occurrences);
        Assert.All(
            document.CandidateOutcomes,
            outcome =>
            {
                PackageAssemblyEvaluationSubject subject =
                    outcome switch
                    {
                        PackageAssemblySemanticQueryCandidateOutcome.Matched
                            matched =>
                            matched.Result.SelectedAsset.Subject,
                        PackageAssemblySemanticQueryCandidateOutcome.NoMatch
                            noMatch =>
                            noMatch.Evaluation.Subject,
                        PackageAssemblySemanticQueryCandidateOutcome
                            .NotApplicable notApplicable =>
                            notApplicable.Evaluation.Subject,
                        PackageAssemblySemanticQueryCandidateOutcome.Failure
                            {
                                Reason:
                                    PackageAssemblySemanticQueryFailureReason
                                        .Evaluation evaluation,
                            } =>
                            evaluation.Evidence.Subject,
                        _ => throw new InvalidOperationException(
                            "The query outcome has no evaluation subject."),
                    };
                Assert.True(
                    PackageRootReacquisitionRequest.TryDecode(
                        subject.RootRequest.Encode(),
                        out PackageRootReacquisitionRequest? decoded));
                Assert.Equal(subject.RootRequest, decoded);
            });
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
        Assert.Equal(0, fixture.Client.PackageRequests);
    }

    [Fact]
    public async Task AggregateLiteralQueryMatchesCompanionLibraryWithProvenance()
    {
        const string packageId = "Contoso.Aggregate";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"lib/{Framework}/{packageId}.dll", NoMatchImage),
            ($"lib/{Framework}/Z.Companion.dll", MatchImage));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);
        var sink = new RecordingQuerySink();

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                sink,
                TestContext.Current.CancellationToken)).Content;

        var outcome = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.Matched>(
            Assert.Single(document.CandidateOutcomes));
        Assert.Equal(
            [
                $"lib/{Framework}/{packageId}.dll",
                $"lib/{Framework}/Z.Companion.dll",
            ],
            outcome.LibraryEvaluations.Select(
                evaluation =>
                    evaluation.SelectedAsset!.Asset.Path.ToString()));
        Assert.IsType<PackageAssemblyEvaluationOutcome.NoMatch>(
            outcome.LibraryEvaluations[0]);
        Assert.IsType<PackageAssemblyEvaluationOutcome.Matched>(
            outcome.LibraryEvaluations[1]);
        PackageAssemblySemanticQueryResult result =
            Assert.Single(document.Results);
        Assert.Equal(2, result.EvaluatedLibraryCount);
        Assert.Equal(1, result.MatchedLibraryCount);
        Assert.Equal(2, result.LibraryOccurrences.Length);
        Assert.All(
            result.LibraryOccurrences,
            occurrence => Assert.Equal(
                $"lib/{Framework}/Z.Companion.dll",
                occurrence.SelectedAsset.Asset.Path.ToString()));
        Assert.Equal(Marker, result.Occurrences[0].LiteralText.ToString());
        Assert.Equal(Marker, result.Occurrences[1].LiteralText.ToString());
        var streamed = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.Matched>(
            Assert.Single(sink.Outcomes));
        Assert.Equal(
            result.LibraryOccurrences,
            streamed.Result.LibraryOccurrences);
    }

    [Fact]
    public async Task AggregateLiteralQueryPreservesEveryPhysicalOccurrenceInAssetOrder()
    {
        const string packageId = "Contoso.Multiple.Matches";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"lib/{Framework}/A.First.dll", MatchImage),
            ($"lib/{Framework}/Z.Second.dll", MatchImage));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);

        PackageAssemblySemanticQueryResult result =
            Assert.Single(
                (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Content.Results);

        Assert.Equal(2, result.EvaluatedLibraryCount);
        Assert.Equal(2, result.MatchedLibraryCount);
        Assert.Equal(
            [
                $"lib/{Framework}/A.First.dll",
                $"lib/{Framework}/A.First.dll",
                $"lib/{Framework}/Z.Second.dll",
                $"lib/{Framework}/Z.Second.dll",
            ],
            result.LibraryOccurrences.Select(
                occurrence =>
                    occurrence.SelectedAsset.Asset.Path.ToString()));
        Assert.Equal(4, result.Occurrences.Length);
        Assert.All(
            result.Occurrences,
            occurrence => Assert.Equal(
                Marker,
                occurrence.LiteralText.ToString()));
    }

    [Fact]
    public async Task AggregateLiteralOccurrenceLimitFailsWithoutPartialMatch()
    {
        const string packageId = "Contoso.Aggregate.Limit";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"lib/{Framework}/A.First.dll", MatchImage),
            ($"lib/{Framework}/Z.Second.dll", MatchImage));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);
        var budget = new PackageAssemblySemanticFindBudget(
            PackageAssemblySemanticFindBudget.Default.Payload,
            PackageAssemblySemanticFindBudget.Default.Evaluation,
            maximumAggregateOccurrences: 3);

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population, budget),
                operation,
                fixture.PayloadAcquisition,
                cancellationToken:
                    TestContext.Current.CancellationToken)).Content;

        var outcome = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.Failure>(
            Assert.Single(document.CandidateOutcomes));
        var limit = Assert.IsType<
            PackageAssemblySemanticQueryFailureReason.AggregateOccurrenceLimit>(
                outcome.Reason);
        Assert.Equal(3, limit.MaximumOccurrences);
        Assert.Equal(4, limit.ObservedOccurrences);
        Assert.Equal(2, limit.EvaluatedLibraries);
        Assert.Equal(2, limit.SelectedLibraries);
        Assert.Equal(2, outcome.LibraryEvaluations.Length);
        Assert.All(
            outcome.LibraryEvaluations,
            evaluation => Assert.IsType<
                PackageAssemblyEvaluationOutcome.Matched>(evaluation));
        Assert.Empty(document.Results);
        Assert.Equal(1, document.FailureCount);
        Assert.Equal(0, document.MatchedPackageCount);
        Assert.False(document.Completion.IsSemanticEvaluationComplete);
    }

    [Fact]
    public async Task AggregateLiteralNoMatchRequiresEveryImplementationLibrary()
    {
        const string packageId = "Contoso.Aggregate.Miss";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"lib/{Framework}/A.First.dll", NoMatchImage),
            ($"lib/{Framework}/Z.Second.dll", NoMatchImage));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                cancellationToken:
                    TestContext.Current.CancellationToken)).Content;

        var outcome = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.NoMatch>(
            Assert.Single(document.CandidateOutcomes));
        Assert.Equal(2, outcome.LibraryEvaluations.Length);
        Assert.All(
            outcome.LibraryEvaluations,
            evaluation => Assert.IsType<
                PackageAssemblyEvaluationOutcome.NoMatch>(evaluation));
        Assert.Empty(document.Results);
        Assert.Equal(1, document.SemanticMissCount);
    }

    [Fact]
    public async Task AggregateLiteralFailureSuppressesPartialMatch()
    {
        const string packageId = "Contoso.Aggregate.Failure";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"lib/{Framework}/A.Match.dll", MatchImage),
            ($"lib/{Framework}/Z.Invalid.dll", [1, 2, 3]));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                cancellationToken:
                    TestContext.Current.CancellationToken)).Content;

        var outcome = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.Failure>(
            Assert.Single(document.CandidateOutcomes));
        Assert.Collection(
            outcome.LibraryEvaluations,
            evaluation => Assert.IsType<
                PackageAssemblyEvaluationOutcome.Matched>(evaluation),
            evaluation => Assert.IsType<
                PackageAssemblyEvaluationOutcome.Failure>(evaluation));
        Assert.Empty(document.Results);
        Assert.Equal(1, document.FailureCount);
        Assert.Equal(0, document.MatchedPackageCount);
        Assert.False(document.Completion.IsSemanticEvaluationComplete);
    }

    [Fact]
    public async Task AggregateLiteralInspectsImplementationForEmptyCompileGroup()
    {
        const string packageId = "Contoso.Empty.Reference";
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAsync(
            packageId,
            ($"ref/{Framework}/_._", []),
            ($"lib/{Framework}/{packageId}.dll", MatchImage));
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                [packageId]);

        PackageAssemblySemanticQueryResult result =
            Assert.Single(
                (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Content.Results);

        Assert.Equal(1, result.EvaluatedLibraryCount);
        Assert.Equal(
            $"lib/{Framework}/{packageId}.dll",
            result.SelectedAsset.Asset.Path.ToString());
    }

    [Fact]
    public async Task MatchLimitStopsAfterNthMatchedCandidate()
    {
        await using var fixture = new SemanticFindSourceFixture();
        string[] packageIds =
        [
            "Contoso.Match.First",
            "Contoso.Match.Later",
        ];
        foreach (string packageId in packageIds)
            await fixture.CacheAssemblyAsync(packageId, MatchImage);

        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                packageIds);
        var sink = new RecordingQuerySink();

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population, maximumMatches: 1),
                operation,
                fixture.PayloadAcquisition,
                sink,
                TestContext.Current.CancellationToken)).Content;

        Assert.Equal(1, document.CandidateCount);
        Assert.Equal(1, document.EvaluatedCandidateCount);
        Assert.Equal(1, document.MatchedPackageCount);
        Assert.Single(document.Results);
        Assert.Single(document.CandidateOutcomes);
        Assert.Single(sink.Outcomes);
        Assert.False(
            document.Completion.AllCandidatesHaveTerminalOutcomes);
        Assert.True(document.Completion.IsSemanticEvaluationComplete);
        Assert.Equal(1, document.Completion.MatchLimit);
        Assert.True(document.Completion.IsMatchLimitReached);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PinnedProductionFingerprintMatchesAuthorityBearingQuery()
    {
        using Stream manifestStream = Assert.IsAssignableFrom<Stream>(
            typeof(PackageAssemblySemanticFindQueryTests).Assembly
                .GetManifestResourceStream(
                    "DotnetInspector.Queries.Tests.PackageAssemblyQueryBenchmark.json"));
        using JsonDocument manifest =
            await JsonDocument.ParseAsync(
                manifestStream,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        JsonElement root = manifest.RootElement;
        string literal = root.GetProperty("literal").GetString()!;
        string targetFramework =
            root.GetProperty("targetFramework").GetString()!;
        string[] packageTexts =
        [
            .. root.GetProperty("packages")
                .EnumerateArray()
                .Select(value => value.GetString()!),
        ];
        PackageSourceCoordinate[] coordinates =
        [
            .. packageTexts.Select(text =>
            {
                int separator = text.IndexOf('@');
                return PackageSourceCoordinate.Create(
                    text[..separator],
                    text[(separator + 1)..]);
            }),
        ];
        PackageAssemblySemanticFindBudget budget =
            PackageAssemblySemanticFindBudget.Default;
        JsonElement limits = root.GetProperty("limits");
        Assert.Equal(
            PackageAcquisitionPopulation.MaximumCandidates,
            limits.GetProperty("maximumPackages").GetInt32());
        Assert.Equal(
            budget.Evaluation.MaximumEntryBytes,
            limits.GetProperty("maximumEntryBytes").GetInt64());
        Assert.Equal(
            budget.Evaluation.MaximumRetainedImageBytes,
            limits.GetProperty(
                "maximumRetainedImageBytes").GetInt64());
        Assert.Equal(
            budget.MaximumDuration.TotalSeconds,
            limits.GetProperty(
                "maximumDurationSeconds").GetInt32());

        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                authorization.Authorities[0].Association);
        await using PackageSourceSettlementLease rootLease =
            PackageSourceSettlementService.IssueLease(
                _ => source);
        PackageSourceOperationLease operation =
            rootLease.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout: budget.MaximumDuration);
        PackageAcquisitionPopulation population =
            await operation.ResolvePinnedPopulationAsync(
                new FixedAuthorization(authorization),
                coordinates);
        var store = new InMemoryPackageStore();
        var request = new PackageAssemblySemanticFindRequest(
            population,
            PackageHouseTargetContext.Exact(
                targetFramework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                literal),
            budget);

        PackageAssemblySemanticFindDocument document =
            (await PackageAssemblySemanticFindInspection.ExecuteAsync(
                request,
                operation,
                new PackagePayloadAcquisitionPlan(
                    (_, _) => store),
                TestContext.Current.CancellationToken)).Content;

        JsonElement expected = root.GetProperty("expected");
        JsonElement[] expectedCandidates =
            [.. expected.GetProperty("candidates").EnumerateArray()];
        Assert.Equal(expectedCandidates.Length, document.CandidateCount);
        for (int index = 0;
             index < expectedCandidates.Length;
             index++)
        {
            JsonElement candidate = expectedCandidates[index];
            PackageAssemblySemanticFindCandidateOutcome outcome =
                document.CandidateOutcomes[index];
            Assert.Equal(
                candidate.GetProperty("package").GetString(),
                outcome.Coordinate.PackageId);
            Assert.Equal(
                candidate.GetProperty("version").GetString(),
                outcome.Coordinate.Version);
            Assert.Equal(
                candidate.GetProperty("outcome").GetString(),
                OutcomeName(outcome));
            Assert.Equal(
                candidate.GetProperty("asset").GetString(),
                Evaluation(outcome).SelectedAsset!.Asset.Path.ToString());
        }

        JsonElement[] expectedMatches =
            [.. expected.GetProperty("matches").EnumerateArray()];
        Assert.Equal(1, document.MatchedCandidateCount);
        Assert.Equal(4, document.SemanticMissCount);
        Assert.Equal(expectedMatches.Length, document.OccurrenceCount);
        Assert.True(document.Completion.IsRequestedPopulationComplete);
        Assert.True(document.Completion.IsSemanticEvaluationComplete);
        for (int index = 0;
             index < expectedMatches.Length;
             index++)
        {
            JsonElement match = expectedMatches[index];
            PackageAssemblySemanticFindResult result =
                document.Results[index];
            Assert.Equal(
                match.GetProperty("package").GetString(),
                result.Coordinate.PackageId);
            Assert.Equal(
                match.GetProperty("version").GetString(),
                result.Coordinate.Version);
            Assert.Equal(
                match.GetProperty("assembly").GetString(),
                result.SelectedAsset.Asset.AssemblyName.ToString());
            Assert.Equal(
                Convert.ToInt32(
                    match.GetProperty("method").GetString(),
                    16),
                result.Evidence.Address.MethodDefinitionToken);
            Assert.Equal(
                Convert.ToInt32(
                    match.GetProperty("offset").GetString()![3..],
                    16),
                result.Evidence.Address.ILOffset);
            Assert.Equal(
                match.GetProperty("literal").GetString(),
                result.Evidence.LiteralText.ToString());
        }
    }

    [Fact]
    public async Task CandidateMatrixPreservesTypedFailuresAndContinuesInOrder()
    {
        await using var fixture = new SemanticFindSourceFixture();
        string[] packageIds =
        [
            "Contoso.Match",
            "Contoso.Acquisition.Failure",
            "Contoso.No.Match",
            "Contoso.Not.Applicable",
            "Contoso.Evaluation.Failure",
        ];
        await fixture.CacheAssemblyAsync(packageIds[0], MatchImage);
        await fixture.CacheAssemblyAsync(packageIds[2], NoMatchImage);
        await fixture.CacheAsync(
            packageIds[3],
            ("readme.txt", "not an assembly"u8.ToArray()));
        await fixture.CacheAssemblyAsync(
            packageIds[4],
            [1, 2, 3]);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                packageIds);
        var sink = new RecordingSink();

        PackageAssemblySemanticFindDocument document =
            (await PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                sink,
                TestContext.Current.CancellationToken)).Content;

        Assert.Collection(
            document.CandidateOutcomes,
            outcome => Assert.IsType<
                PackageAssemblySemanticFindCandidateOutcome.Matched>(
                outcome),
            outcome =>
            {
                var failure = Assert.IsType<
                    PackageAssemblySemanticFindCandidateOutcome.Failure>(
                    outcome);
                var acquisition = Assert.IsType<
                    PackageAssemblySemanticFindFailureReason.Acquisition>(
                    failure.Reason);
                Assert.Empty(acquisition.Evidence.Failures);
                Assert.Single(
                    acquisition.Evidence.NotFoundAuthorities);
            },
            outcome => Assert.IsType<
                PackageAssemblySemanticFindCandidateOutcome.NoMatch>(
                outcome),
            outcome =>
            {
                var notApplicable = Assert.IsType<
                    PackageAssemblySemanticFindCandidateOutcome.NotApplicable>(
                    outcome);
                Assert.Equal(
                    PackageAssemblyNotApplicableReason.NoCompileAssets,
                    notApplicable.Evaluation.Reason);
            },
            outcome =>
            {
                var failure = Assert.IsType<
                    PackageAssemblySemanticFindCandidateOutcome.Failure>(
                    outcome);
                var evaluation = Assert.IsType<
                    PackageAssemblySemanticFindFailureReason.Evaluation>(
                    failure.Reason);
                Assert.Equal(
                    PackageAssemblyFailureStage.ImageAdmission,
                    evaluation.Evidence.Reason.Stage);
            });
        Assert.Equal(
            Enumerable.Range(1, 5),
            document.CandidateOutcomes.Select(
                outcome => outcome.CandidateOrdinal));
        Assert.Equal(
            document.CandidateOutcomes,
            sink.Outcomes);
        Assert.Equal(5, document.CandidateCount);
        Assert.Equal(1, document.MatchedCandidateCount);
        Assert.Equal(2, document.OccurrenceCount);
        Assert.Equal(1, document.SemanticMissCount);
        Assert.Equal(1, document.NotApplicableCount);
        Assert.Equal(2, document.FailureCount);
        Assert.True(document.Completion.IsRequestedPopulationComplete);
        Assert.True(document.Completion.AllCandidatesCompleted);
        Assert.True(document.Completion.HasFailures);
        Assert.False(document.Completion.IsSemanticEvaluationComplete);
        Assert.Equal(1, fixture.Client.PackageRequests);
    }

    [Fact]
    public async Task PartialSourcePopulationRemainsDistinctFromQueryCompletion()
    {
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAssemblyAsync(
            "Contoso.Available",
            NoMatchImage);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation exact =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Available"]);
        var sourceFailure = new PackageAuthorityFailure(
            InertString.Empty,
            PackageAuthorityFailureKind.Transport,
            "The second selected package could not be resolved.");
        var population = new PackageAcquisitionPopulation(
            requestedCandidates: 2,
            exact.Candidates,
            [
                PackageAcquisitionPopulationFailure.ForCandidate(
                    candidateOrdinal: 2,
                    packageId: "Contoso.Unavailable",
                    coordinate: null,
                    sourceFailure),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);

        PackageAssemblySemanticFindDocument document =
            (await PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken)).Content;

        Assert.Same(population, document.Population);
        Assert.Single(document.Population.Failures);
        Assert.Equal(1, document.CandidateCount);
        Assert.True(document.Completion.AllCandidatesCompleted);
        Assert.False(
            document.Completion.IsRequestedPopulationComplete);
        Assert.False(document.Completion.HasFailures);
        Assert.True(document.Completion.IsSemanticEvaluationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            document.Completion.Population);
    }

    [Fact]
    public async Task OperationDeadlineProducesTerminalDocumentWithoutEvaluation()
    {
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation admitted =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Admitted"]);
        TimeSpan timeout =
            PackageAssemblySemanticFindBudget.Default.MaximumDuration;
        var population = new PackageAcquisitionPopulation(
            requestedCandidates: 2,
            admitted.Candidates,
            [
                PackageAcquisitionPopulationFailure.ForSource(
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Timeout,
                        "Package selection exhausted its operation deadline.")
                    {
                        Timeout = new(
                            PackageSourceTimeoutKind.Operation,
                            timeout),
                    }),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);
        var sink = new RecordingQuerySink();

        InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
            await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                sink,
                TestContext.Current.CancellationToken);
        PackageAssemblySemanticQueryDocument document = envelope.Content;
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
        Assert.Same(population, document.Population);
        Assert.Equal(1, document.CandidateCount);
        Assert.Equal(0, document.EvaluatedCandidateCount);
        Assert.Equal(1, document.NotEvaluatedCount);
        Assert.Empty(document.Results);
        Assert.Equal(0, document.FailureCount);
        Assert.False(document.Completion.IsRequestedPopulationComplete);
        Assert.True(
            document.Completion.AllCandidatesHaveTerminalOutcomes);
        Assert.True(document.Completion.HasFailures);
        Assert.False(document.Completion.IsSemanticEvaluationComplete);
        Assert.True(document.Completion.IsOperationDeadlineExpired);
        Assert.Empty(sink.Outcomes);
        var outcome = Assert.IsType<
            PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated>(
                Assert.Single(document.CandidateOutcomes));
        Assert.Equal(admitted.Candidates[0].Coordinate, outcome.Coordinate);
        var reason = Assert.IsType<
            PackageAssemblySemanticQueryNonEvaluationReason.OperationDeadline>(
                outcome.Reason);
        Assert.Equal(timeout, reason.Timeout.Duration);
        string json = JsonSerializer.Serialize(envelope);
        using JsonDocument serialized = JsonDocument.Parse(json);
        JsonElement serializedOutcome = serialized.RootElement
            .GetProperty("Content")
            .GetProperty("CandidateOutcomes")[0];
        Assert.Equal(
            "notEvaluated",
            serializedOutcome.GetProperty("kind").GetString());
        Assert.Equal(
            "operationDeadline",
            serializedOutcome
                .GetProperty("Reason")
                .GetProperty("kind")
                .GetString());
        Assert.Throws<ObjectDisposedException>(operation.ThrowIfExpired);
    }

    [Fact]
    public async Task RequestTimeoutStillEntersSemanticEvaluation()
    {
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        var population = new PackageAcquisitionPopulation(
            requestedCandidates: 1,
            candidates: [],
            failures:
            [
                PackageAcquisitionPopulationFailure.ForSource(
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Timeout,
                        "One source request timed out.")
                    {
                        Timeout = new(
                            PackageSourceTimeoutKind.Request,
                            TimeSpan.FromSeconds(1)),
                    }),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);

        PackageAssemblySemanticQueryDocument document =
            (await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken)).Content;

        Assert.False(document.Completion.IsOperationDeadlineExpired);
        Assert.True(
            document.Completion.AllCandidatesHaveTerminalOutcomes);
        Assert.True(document.Completion.IsSemanticEvaluationComplete);
        Assert.True(document.Completion.HasFailures);
        Assert.Same(population, document.Population);
    }

    [Fact]
    public async Task OperationDeadlinePreservesCallerCancellation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        PackageAcquisitionPopulation admitted =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Cancelled"]);
        var population = OperationDeadlinePopulation(
            admitted.Candidates);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                        Request(population),
                        operation,
                        fixture.PayloadAcquisition,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Throws<ObjectDisposedException>(operation.ThrowIfExpired);
        Assert.Equal(0, fixture.Client.PackageRequests);
    }

    [Fact]
    public async Task OperationDeadlineRejectsForeignPopulation()
    {
        await using var owner = new SemanticFindSourceFixture();
        await using var foreign = new SemanticFindSourceFixture();
        PackageSourceOperationLease ownerOperation =
            owner.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation admitted =
            await owner.ResolvePopulationAsync(
                ownerOperation,
                ["Contoso.Foreign"]);
        PackageAcquisitionPopulation population =
            OperationDeadlinePopulation(admitted.Candidates);
        PackageSourceOperationLease foreignOperation =
            foreign.IssueOperation(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    Request(population),
                    foreignOperation,
                    foreign.PayloadAcquisition,
                    TestContext.Current.CancellationToken));

        Assert.Throws<ObjectDisposedException>(
            foreignOperation.ThrowIfExpired);
        ownerOperation.ThrowIfExpired();
        ownerOperation.Dispose();
        Assert.Equal(0, foreign.Client.PackageRequests);
    }

    [Fact]
    public async Task CancellationBeforeAcquisitionPublishesNoOutcomeAndReleasesOperation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Cancelled"]);
        var sink = new RecordingSink();
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageAssemblySemanticFindInspection.ExecuteAsync(
                        Request(population),
                        operation,
                        fixture.PayloadAcquisition,
                        sink,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(sink.Outcomes);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
        Assert.Equal(0, fixture.Client.PackageRequests);
    }

    [Fact]
    public async Task EmptyPopulationStillObservesCallerCancellation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        var population = new PackageAcquisitionPopulation(
            requestedCandidates: 1,
            candidates: [],
            failures:
            [
                PackageAcquisitionPopulationFailure.ForSource(
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Configuration,
                        "No package candidate was admitted.")),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageAssemblySemanticFindInspection.ExecuteAsync(
                        Request(population),
                        operation,
                        fixture.PayloadAcquisition,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task EmptyPopulationStillPreservesOperationTimeoutClassification()
    {
        await using var fixture = new SemanticFindSourceFixture();
        TimeSpan timeout = TimeSpan.FromMilliseconds(20);
        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout: timeout);
        var population = new PackageAcquisitionPopulation(
            requestedCandidates: 1,
            candidates: [],
            failures:
            [
                PackageAcquisitionPopulationFailure.ForSource(
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Timeout,
                        "Package selection exhausted its operation deadline.")),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);
        var budget = new PackageAssemblySemanticFindBudget(
            PackageAssemblySemanticFindBudget.Default.Payload,
            new PackageAssemblyEvaluationBudget(
                PackageAssemblyEvaluationBudget.Default.MaximumEntryBytes,
                PackageAssemblyEvaluationBudget.Default
                    .MaximumRetainedImageBytes,
                PackageAssemblyEvaluationBudget.Default.SemanticBudget,
                timeout));
        await Task.Delay(
            TimeSpan.FromMilliseconds(60),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
            async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population, budget),
                    operation,
                    fixture.PayloadAcquisition,
                    TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task CancellationDuringAcquisitionPublishesNoOutcomeAndReleasesOperation()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        fixture.Client.BeforePackage =
            token => Task.Delay(Timeout.InfiniteTimeSpan, token);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Cancelled"]);
        var sink = new RecordingSink();

        Task<InspectionEnvelope<PackageAssemblySemanticFindDocument>>
            pending = PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                sink,
                cancellation.Token).AsTask();
        await fixture.Client.PackageStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending);
        Assert.Empty(sink.Outcomes);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task CancellationAfterPublishedCandidateStopsBeforeTheNextCandidate()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAssemblyAsync(
            "Contoso.First",
            NoMatchImage);
        await fixture.CacheAssemblyAsync(
            "Contoso.Second",
            NoMatchImage);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.First", "Contoso.Second"]);
        var sink =
            new CallerCancellingSink(cancellation);

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    sink,
                    cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(42, failure.Data["fixture"]);
        Assert.Single(sink.Outcomes);
        Assert.Equal(
            "contoso.first",
            sink.Outcomes[0].Coordinate.PackageId);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
        Assert.Equal(0, fixture.Client.PackageRequests);
    }

    [Fact]
    public async Task LinkedSinkCancellationPreservesCallerIdentity()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAssemblyAsync(
            "Contoso.First",
            NoMatchImage);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(cancellation.Token);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.First"]);
        var sink =
            new LinkedCallerCancellingSink(cancellation);

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    sink,
                    cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(42, failure.Data["fixture"]);
        Assert.Single(sink.Outcomes);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task LinkedSinkCancellationPreservesTimeoutClassification()
    {
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAssemblyAsync(
            "Contoso.First",
            NoMatchImage);
        TimeSpan timeout = TimeSpan.FromSeconds(1);
        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout: timeout);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.First"]);
        var budget = new PackageAssemblySemanticFindBudget(
            PackageAssemblySemanticFindBudget.Default.Payload,
            new PackageAssemblyEvaluationBudget(
                PackageAssemblyEvaluationBudget.Default.MaximumEntryBytes,
                PackageAssemblyEvaluationBudget.Default
                    .MaximumRetainedImageBytes,
                PackageAssemblyEvaluationBudget.Default.SemanticBudget,
                timeout));
        var sink = new LinkedWaitingSink();

        NuGetOperationTimeoutException failure =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population, budget),
                    operation,
                    fixture.PayloadAcquisition,
                    sink,
                    TestContext.Current.CancellationToken));

        Assert.Equal(42, failure.Data["fixture"]);
        Assert.Single(sink.Outcomes);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task IndependentSinkCancellationKeepsItsOwnIdentity()
    {
        await using var fixture = new SemanticFindSourceFixture();
        await fixture.CacheAssemblyAsync(
            "Contoso.First",
            NoMatchImage);
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.First"]);
        var sink = new IndependentCancellingSink();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    sink,
                    TestContext.Current.CancellationToken));

        Assert.Equal(sink.CancellationToken, failure.CancellationToken);
        Assert.Equal(42, failure.Data["fixture"]);
        Assert.Single(sink.Outcomes);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task ForeignPopulationIsRejectedWithoutCoordinateFallback()
    {
        await using var owner = new SemanticFindSourceFixture();
        await using var foreign = new SemanticFindSourceFixture();
        PackageSourceOperationLease ownerOperation =
            owner.IssueOperation(
                TestContext.Current.CancellationToken);
        PackageAcquisitionPopulation population =
            await owner.ResolvePopulationAsync(
                ownerOperation,
                ["Contoso.Foreign"]);
        PackageSourceOperationLease foreignOperation =
            foreign.IssueOperation(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    foreignOperation,
                    foreign.PayloadAcquisition,
                    TestContext.Current.CancellationToken));

        Assert.Throws<ObjectDisposedException>(
            foreignOperation.ThrowIfExpired);
        Assert.Equal(0, foreign.Client.PackageRequests);
        ownerOperation.Dispose();
    }

    [Fact]
    public async Task InvalidRequestStillReleasesTransferredOperation()
    {
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.IssueOperation(
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    null!,
                    operation,
                    fixture.PayloadAcquisition,
                    TestContext.Current.CancellationToken));

        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public async Task MismatchedDeadlineReleasesTransferredOperation()
    {
        await using var fixture = new SemanticFindSourceFixture();
        PackageSourceOperationLease operation =
            fixture.Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout: TimeSpan.FromMinutes(2));
        PackageAcquisitionPopulation population =
            await fixture.ResolvePopulationAsync(
                operation,
                ["Contoso.Deadline"]);

        await Assert.ThrowsAsync<ArgumentException>(
            async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    TestContext.Current.CancellationToken));

        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
    }

    [Fact]
    public void DocumentClosureRetainsNoLiveInspectionOrSourceResources()
    {
        Type[] forbidden =
        [
            typeof(PackageSourceOperationLease),
            typeof(PackageSourceSettlementLease),
            typeof(IPackageContent),
            typeof(AcquiredPackageSourcePayload),
            typeof(PackageRootBinding),
            typeof(InspectionWorkspace),
        ];
        Type[] resultTypes =
        [
            typeof(PackageAssemblySemanticFindDocument),
            typeof(PackageAssemblySemanticFindCompletion),
            typeof(PackageAssemblySemanticFindResult),
            typeof(PackageAssemblySemanticFindCandidateOutcome),
            .. typeof(PackageAssemblySemanticFindCandidateOutcome)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageAssemblySemanticFindFailureReason),
            .. typeof(PackageAssemblySemanticFindFailureReason)
                .GetNestedTypes(BindingFlags.Public),
            typeof(PackageAssemblySemanticFindAcquisitionFailure),
            typeof(PackageAcquisitionPopulation),
            typeof(PackageAcquisitionCandidate),
            typeof(PackageAssemblyEvaluationOutcome),
            .. typeof(PackageAssemblyEvaluationOutcome)
                .GetNestedTypes(BindingFlags.Public),
        ];

        Assert.All(
            resultTypes,
            type => Assert.DoesNotContain(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field => forbidden.Any(
                    resource =>
                        ContainsType(
                            field.FieldType,
                            resource))));

        static bool ContainsType(
            Type type,
            Type forbiddenType)
        {
            if (forbiddenType.IsAssignableFrom(type))
                return true;
            return type.IsGenericType
                && type.GetGenericArguments().Any(
                    argument =>
                        ContainsType(
                            argument,
                            forbiddenType));
        }
    }

    private static PackageAssemblySemanticFindRequest Request(
        PackageAcquisitionPopulation population,
        PackageAssemblySemanticFindBudget? budget = null,
        int? maximumMatches = null) =>
        new(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                Marker),
            budget,
            maximumMatches);

    private static PackageAcquisitionPopulation OperationDeadlinePopulation(
        ImmutableArray<PackageAcquisitionCandidate> candidates) =>
        new(
            requestedCandidates: Math.Min(
                PackageAcquisitionPopulation.MaximumCandidates,
                candidates.Length + 1),
            candidates,
            [
                PackageAcquisitionPopulationFailure.ForSource(
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Timeout,
                        "Package selection exhausted its operation deadline.")
                    {
                        Timeout = new(
                            PackageSourceTimeoutKind.Operation,
                            PackageAssemblySemanticFindBudget.Default
                                .MaximumDuration),
                    }),
            ],
            PackageAcquisitionPopulationCompletionKind.SourceFailed);

    private static PackageAssemblyEvaluationOutcome Evaluation(
        PackageAssemblySemanticFindCandidateOutcome outcome) =>
        outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                matched.Evaluation,
            PackageAssemblySemanticFindCandidateOutcome.NoMatch noMatch =>
                noMatch.Evaluation,
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable
                notApplicable =>
                notApplicable.Evaluation,
            PackageAssemblySemanticFindCandidateOutcome.Failure
                {
                    Reason:
                        PackageAssemblySemanticFindFailureReason.Evaluation
                        evaluation,
                } =>
                evaluation.Evidence,
            _ => throw new InvalidOperationException(
                "The outcome has no evaluation evidence."),
        };

    private static string OutcomeName(
        PackageAssemblySemanticFindCandidateOutcome outcome) =>
        outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched =>
                "matched",
            PackageAssemblySemanticFindCandidateOutcome.NoMatch =>
                "no-match",
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable =>
                "not-applicable",
            PackageAssemblySemanticFindCandidateOutcome.Failure =>
                "failure",
            _ => throw new InvalidOperationException(
                "Unknown semantic Find candidate outcome."),
        };

    private sealed class RecordingSink(
        Action<PackageAssemblySemanticFindCandidateOutcome>? observed = null)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Outcomes.Add(outcome);
            observed?.Invoke(outcome);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingQuerySink
        : IPackageAssemblySemanticQueryNonterminalSink
    {
        internal List<PackageAssemblySemanticQueryCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ReportAsync(
            PackageAssemblySemanticQueryCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Outcomes.Add(outcome);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallerCancellingSink(
        CancellationTokenSource cancellation)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            Outcomes.Add(outcome);
            cancellation.Cancel();
            var failure = new OperationCanceledException(
                cancellationToken);
            failure.Data["fixture"] = 42;
            throw failure;
        }
    }

    private sealed class LinkedCallerCancellingSink(
        CancellationTokenSource cancellation)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            Outcomes.Add(outcome);
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            cancellation.Cancel();
            var failure = new OperationCanceledException(
                linked.Token);
            failure.Data["fixture"] = 42;
            throw failure;
        }
    }

    private sealed class LinkedWaitingSink
        : IPackageAssemblySemanticFindNonterminalSink
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public async ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            Outcomes.Add(outcome);
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    linked.Token);
            }
            catch (OperationCanceledException failure)
            {
                failure.Data["fixture"] = 42;
                throw;
            }
        }
    }

    private sealed class IndependentCancellingSink
        : IPackageAssemblySemanticFindNonterminalSink
    {
        private readonly CancellationTokenSource _cancellation = new();

        internal CancellationToken CancellationToken =>
            _cancellation.Token;

        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            Outcomes.Add(outcome);
            _cancellation.Cancel();
            var failure = new OperationCanceledException(
                _cancellation.Token);
            failure.Data["fixture"] = 42;
            throw failure;
        }
    }

    private sealed class FixedAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            authorization;
    }

    private sealed class SemanticFindSourceFixture : IAsyncDisposable
    {
        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);

        internal SourceClient Client { get; }

        internal IPackageSourceClient OwnedClient { get; }

        internal PackageSourceSettlementLease Root { get; }

        internal InMemoryPackageStore Store { get; } = new();

        internal PackagePayloadAcquisitionPlan PayloadAcquisition
            { get; }

        internal SemanticFindSourceFixture()
        {
            SourceClient? client = null;
            OwnedClient = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => client = new SourceClient(factory));
            Client = client!;
            Root =
                PackageSourceSettlementService.IssueLease(
                    _ => OwnedClient);
            PayloadAcquisition =
                new PackagePayloadAcquisitionPlan(
                    (_, _) => Store);
        }

        internal PackageSourceOperationLease IssueOperation() =>
            Root.IssueOperationLease(
                TestContext.Current.CancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);

        internal PackageSourceOperationLease IssueOperation(
            CancellationToken cancellationToken) =>
            Root.IssueOperationLease(
                cancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);

        internal Task<PackageAcquisitionPopulation>
            ResolvePopulationAsync(
                PackageSourceOperationLease operation,
                IReadOnlyList<string> packageIds) =>
            operation.ResolvePinnedPopulationAsync(
                new FixedAuthorization(Authorization),
                packageIds.Select(
                    packageId =>
                        PackageSourceCoordinate.Create(
                            packageId,
                            Version))
                .ToArray());

        internal Task CacheAssemblyAsync(
            string packageId,
            byte[] image) =>
            CacheAsync(
                packageId,
                ($"lib/{Framework}/{packageId}.dll", image));

        internal async Task CacheAsync(
            string packageId,
            params (string Path, byte[] Content)[] entries)
        {
            byte[] archive = CreateArchive(
                packageId,
                entries);
            await Store.CommitAsync(
                packageId,
                Version,
                OwnedClient.Source.Producer.Key,
                new MemoryStream(
                    archive,
                    writable: false),
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            OwnedClient.Dispose();
        }

        private static byte[] CreateArchive(
            string packageId,
            IEnumerable<(string Path, byte[] Content)> entries)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
                buffer,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                Write(
                    archive,
                    $"{packageId}.nuspec",
                    Encoding.UTF8.GetBytes(
                        $"<package><metadata><id>{packageId}</id><version>{Version}</version></metadata></package>"));
                foreach ((string path, byte[] content) in entries)
                    Write(archive, path, content);
            }
            return buffer.ToArray();

            static void Write(
                ZipArchive archive,
                string path,
                byte[] content)
            {
                using Stream entry =
                    archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
    }

    private sealed class SourceClient(
        PackageSourceResultFactory factory)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => factory.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.PackagePayload;

        internal int PackageRequests { get; private set; }

        internal TaskCompletionSource PackageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Func<CancellationToken, Task>? BeforePackage { get; set; }

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public async Task<
            PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            Assert.NotNull(operationContext);
            PackageRequests++;
            PackageStarted.TrySetResult();
            if (BeforePackage is not null)
                await BeforePackage(cancellationToken);
            return factory.FailedPackage(
                PackageSourceCoordinate.Create(
                    packageId,
                    version),
                PackageSourceFailureKind.NotFound);
        }

        public Task<
            PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
