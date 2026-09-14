using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Text;
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
        var observer = new RecordingObserver();

        InspectionEnvelope<PackageAssemblySemanticFindResult> envelope =
            await PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                observer,
                TestContext.Current.CancellationToken);

        PackageAssemblySemanticFindResult result = envelope.Content;
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
        Assert.Equal(5, result.CandidateCount);
        Assert.Equal(1, result.MatchedCandidateCount);
        Assert.Equal(2, result.OccurrenceCount);
        Assert.Equal(4, result.SemanticMissCount);
        Assert.Equal(0, result.NotApplicableCount);
        Assert.Equal(0, result.FailureCount);
        Assert.True(result.Completion.IsRequestedPopulationComplete);
        Assert.True(result.Completion.AllCandidatesCompleted);
        Assert.False(result.Completion.HasFailures);
        Assert.True(result.Completion.IsSemanticEvaluationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates,
            result.Completion.Population);
        Assert.Equal(
            result.CandidateOutcomes,
            observer.Outcomes);
        Assert.All(
            result.Occurrences,
            occurrence =>
            {
                Assert.Equal(1, occurrence.CandidateOrdinal);
                Assert.Equal(
                    population.Candidates[0].Correspondence,
                    occurrence.Correspondence);
                Assert.Equal(
                    "lib/net11.0/Contoso.Match.dll",
                    occurrence.SelectedAsset.Asset.Path.ToString());
            });
        Assert.Equal(
            Assert.IsType<
                PackageAssemblySemanticFindCandidateOutcome.Matched>(
                result.CandidateOutcomes[0])
            .Evaluation.Evidence.Occurrences,
            result.Occurrences.Select(occurrence => occurrence.Evidence));
        Assert.All(
            result.CandidateOutcomes,
            outcome =>
            {
                PackageAssemblyEvaluationSubject subject =
                    Evaluation(outcome).Subject;
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
        var observer = new RecordingObserver();

        PackageAssemblySemanticFindResult result =
            (await PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                observer,
                TestContext.Current.CancellationToken)).Content;

        Assert.Collection(
            result.CandidateOutcomes,
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
            result.CandidateOutcomes.Select(
                outcome => outcome.CandidateOrdinal));
        Assert.Equal(
            result.CandidateOutcomes,
            observer.Outcomes);
        Assert.Equal(5, result.CandidateCount);
        Assert.Equal(1, result.MatchedCandidateCount);
        Assert.Equal(2, result.OccurrenceCount);
        Assert.Equal(1, result.SemanticMissCount);
        Assert.Equal(1, result.NotApplicableCount);
        Assert.Equal(2, result.FailureCount);
        Assert.True(result.Completion.IsRequestedPopulationComplete);
        Assert.True(result.Completion.AllCandidatesCompleted);
        Assert.True(result.Completion.HasFailures);
        Assert.False(result.Completion.IsSemanticEvaluationComplete);
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

        PackageAssemblySemanticFindResult result =
            (await PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                TestContext.Current.CancellationToken)).Content;

        Assert.Same(population, result.Population);
        Assert.Single(result.Population.Failures);
        Assert.Equal(1, result.CandidateCount);
        Assert.True(result.Completion.AllCandidatesCompleted);
        Assert.False(
            result.Completion.IsRequestedPopulationComplete);
        Assert.False(result.Completion.HasFailures);
        Assert.True(result.Completion.IsSemanticEvaluationComplete);
        Assert.Equal(
            PackageAcquisitionPopulationCompletionKind.SourceFailed,
            result.Completion.Population);
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
        var observer = new RecordingObserver();
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await PackageAssemblySemanticFindInspection.ExecuteAsync(
                        Request(population),
                        operation,
                        fixture.PayloadAcquisition,
                        observer,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(observer.Outcomes);
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
        var observer = new RecordingObserver();

        Task<InspectionEnvelope<PackageAssemblySemanticFindResult>>
            pending = PackageAssemblySemanticFindInspection.ExecuteAsync(
                Request(population),
                operation,
                fixture.PayloadAcquisition,
                observer,
                cancellation.Token).AsTask();
        await fixture.Client.PackageStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pending);
        Assert.Empty(observer.Outcomes);
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
        var observer =
            new CallerCancellingObserver(cancellation);

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    Request(population),
                    operation,
                    fixture.PayloadAcquisition,
                    observer,
                    cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(42, failure.Data["fixture"]);
        Assert.Single(observer.Outcomes);
        Assert.Equal(
            "contoso.first",
            observer.Outcomes[0].Coordinate.PackageId);
        Assert.Throws<ObjectDisposedException>(
            operation.ThrowIfExpired);
        Assert.Equal(0, fixture.Client.PackageRequests);
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
    public void ResultClosureRetainsNoLiveInspectionOrSourceResources()
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
            typeof(PackageAssemblySemanticFindResult),
            typeof(PackageAssemblySemanticFindCompletion),
            typeof(PackageAssemblySemanticFindOccurrence),
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
        PackageAssemblySemanticFindBudget? budget = null) =>
        new(
            population,
            PackageHouseTargetContext.Exact(Framework),
            PackageAssemblyPatterns.CreateRequest(
                PackageAssemblyPatterns.StringLiteralContains,
                Marker),
            budget);

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

    private sealed class RecordingObserver(
        Action<PackageAssemblySemanticFindCandidateOutcome>? observed = null)
        : IPackageAssemblySemanticFindObserver
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ObserveAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Outcomes.Add(outcome);
            observed?.Invoke(outcome);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallerCancellingObserver(
        CancellationTokenSource cancellation)
        : IPackageAssemblySemanticFindObserver
    {
        internal List<PackageAssemblySemanticFindCandidateOutcome> Outcomes
            { get; } = [];

        public ValueTask ObserveAsync(
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
