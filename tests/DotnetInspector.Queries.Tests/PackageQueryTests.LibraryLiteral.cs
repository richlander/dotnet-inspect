using System.IO.Compression;
using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageQueryTests
{
    [Fact]
    public async Task LibraryLiteralPublishesOnlyFinalTypedMatches()
    {
        const string packageId = "Contoso.Match";
        const string literal = "shared-literal-use-marker";
        await using var fixture = new SemanticQueryFixture();
        await fixture.CacheAssemblyAsync(
            packageId,
            File.ReadAllBytes(
                FixtureCatalog.AnalysisStringLiterals.AssemblyPath()));
        var source = new FakePackageSource(
            [Match(packageId)],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            [Term(PackageQuery.LibraryLiteralTermKey, literal)],
            maximumCandidates: 5,
            maximumMatches: 1,
            targetFramework: "net11.0"));
        var sink = new RecordingPackageQueryNonterminalSink();
        var assessmentSink =
            new RecordingLibraryLiteralAssessmentSink();
        var semanticExecution = new PackageQueryAssemblySemanticExecution(
            new FixedAuthorization(fixture.Authorization),
            fixture.IssueOperation,
            fixture.PayloadAcquisition,
            PackageAssemblySemanticFindBudget.Default,
            assessmentSink);

        InspectionEnvelope<PackageQueryDocument> envelope =
            await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                dependencyTraversalServices: null,
                semanticExecution,
                sink,
                TestContext.Current.CancellationToken);

        PackageQueryMatch match = Assert.Single(envelope.Content.Results);
        Assert.NotNull(match.LibraryLiteral);
        Assert.Equal(
            packageId.ToLowerInvariant(),
            match.Package.PackageId.ToLowerInvariant());
        Assert.NotEmpty(match.LibraryLiteral.Occurrences);
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                match.LibraryLiteral.RootRequest.Encode(),
                out _));
        PackageQueryLibraryLiteralAssessment assessment =
            Assert.Single(envelope.Content.LibraryLiteralAssessments);
        Assert.Equal(
            PackageQueryLibraryLiteralAssessmentKind.Matched,
            assessment.Kind);
        Assert.Equal(1, envelope.Content.Summary.EvaluatedCandidates);
        Assert.Equal(1, envelope.Content.Summary.SemanticMatches);
        Assert.Equal(
            match.LibraryLiteral.Occurrences.Length,
            envelope.Content.Summary.Occurrences);
        PackageQueryLibraryLiteralAssessment publishedAssessment =
            Assert.Single(assessmentSink.Assessments);
        Assert.Same(assessment, publishedAssessment);
        Assert.Equal(assessment, publishedAssessment);
        PackageQueryEvent.Match published = Assert.Single(
            sink.Events.OfType<PackageQueryEvent.Match>());
        Assert.NotNull(published.Value.LibraryLiteral);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryLiteralMatchLimitStopsBeforeLaterCandidate(
        bool laterCandidateWouldMatch)
    {
        const string firstPackage = "Contoso.Match.First";
        const string laterPackage = "Contoso.Match.Later";
        const string literal = "shared-literal-use-marker";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());
        await using var fixture = new SemanticQueryFixture();
        await fixture.CacheAssemblyAsync(firstPackage, image);
        if (laterCandidateWouldMatch)
            await fixture.CacheAssemblyAsync(laterPackage, image);
        var source = new FakePackageSource(
            [Match(firstPackage), Match(laterPackage)],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            [Term(PackageQuery.LibraryLiteralTermKey, literal)],
            maximumCandidates: 2,
            maximumMatches: 1,
            targetFramework: "net11.0"));
        var sink = new RecordingPackageQueryNonterminalSink();
        var assessmentSink =
            new RecordingLibraryLiteralAssessmentSink();
        var semanticExecution = new PackageQueryAssemblySemanticExecution(
            new FixedAuthorization(fixture.Authorization),
            fixture.IssueOperation,
            fixture.PayloadAcquisition,
            PackageAssemblySemanticFindBudget.Default,
            assessmentSink);

        PackageQueryDocument document =
            (await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                dependencyTraversalServices: null,
                semanticExecution,
                sink,
                TestContext.Current.CancellationToken)).Content;

        PackageQueryMatch match = Assert.Single(document.Results);
        Assert.Equal(
            firstPackage.ToLowerInvariant(),
            match.Package.PackageId.ToLowerInvariant());
        PackageQueryLibraryLiteralAssessment assessment =
            Assert.Single(document.LibraryLiteralAssessments);
        Assert.Equal(
            firstPackage.ToLowerInvariant(),
            assessment.PackageId.ToLowerInvariant());
        Assert.Single(assessmentSink.Assessments);
        Assert.Empty(document.Failures);
        Assert.Equal(2, document.Summary.Candidates);
        Assert.Equal(1, document.Summary.EvaluatedCandidates);
        Assert.Equal(0, document.Summary.NotEvaluatedCandidates);
        Assert.Equal(1, document.Summary.SemanticMatches);
        Assert.Equal(
            PackageQueryCompletionKind.MatchLimitReached,
            document.Summary.Completion);
        Assert.Single(sink.Events.OfType<PackageQueryEvent.Match>());
        Assert.DoesNotContain(
            document.LibraryLiteralAssessments,
            item => item.PackageId.Equals(
                laterPackage,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LibraryLiteralOperationDeadlineRetainsTerminalAssessment()
    {
        const string admittedPackage = "Contoso.Admitted";
        const string delayedPackage = "Contoso.Delayed";
        const string literal = "shared-literal-use-marker";
        await using var fixture = new SemanticQueryFixture();
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());
        await fixture.CacheAssemblyAsync(admittedPackage, image);
        await fixture.CacheAssemblyAsync(delayedPackage, image);
        var source = new FakePackageSource(
            [Match(admittedPackage), Match(delayedPackage)],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            [Term(PackageQuery.LibraryLiteralTermKey, literal)],
            maximumCandidates: 2,
            maximumMatches: 2,
            targetFramework: "net11.0"));
        var assessmentSink =
            new RecordingLibraryLiteralAssessmentSink();
        TimeSpan timeout = TimeSpan.FromMilliseconds(100);
        var semanticExecution = new PackageQueryAssemblySemanticExecution(
            new DelayedAuthorization(
                fixture.Authorization,
                delayOnCall: 2,
                TimeSpan.FromMilliseconds(250)),
            cancellationToken =>
                fixture.IssueOperation(cancellationToken, timeout),
            fixture.PayloadAcquisition,
            new PackageAssemblySemanticFindBudget(
                PackageAssemblySemanticFindBudget.Default.Payload,
                new PackageAssemblyEvaluationBudget(
                    PackageAssemblyEvaluationBudget.Default.MaximumEntryBytes,
                    PackageAssemblyEvaluationBudget.Default
                        .MaximumRetainedImageBytes,
                    PackageAssemblyEvaluationBudget.Default.SemanticBudget,
                    timeout)),
            assessmentSink);

        PackageQueryDocument document =
            (await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                dependencyTraversalServices: null,
                semanticExecution,
                nonterminalSink: null,
                TestContext.Current.CancellationToken)).Content;

        Assert.Equal(0, document.Summary.EvaluatedCandidates);
        Assert.Equal(1, document.Summary.NotEvaluatedCandidates);
        PackageQueryLibraryLiteralAssessment assessment =
            Assert.Single(document.LibraryLiteralAssessments);
        Assert.Equal(
            admittedPackage.ToLowerInvariant(),
            assessment.PackageId.ToLowerInvariant());
        Assert.Equal(
            PackageQueryLibraryLiteralAssessmentKind.NotEvaluated,
            assessment.Kind);
        Assert.Equal(
            PackageQueryLibraryLiteralNonEvaluationKind.OperationDeadline,
            assessment.NonEvaluationKind);
        Assert.Empty(assessmentSink.Assessments);
        Assert.Empty(document.Results);
    }

    [Fact]
    public async Task
        LibraryLiteralAggregateOccurrenceLimitIsVisibleWithoutPartialMatch()
    {
        const string packageId = "Contoso.Aggregate.Limit";
        const string literal = "shared-literal-use-marker";
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisStringLiterals.AssemblyPath());
        await using var fixture = new SemanticQueryFixture();
        await fixture.CacheAssembliesAsync(
            packageId,
            ("A.First.dll", image),
            ("Z.Second.dll", image));
        var source = new FakePackageSource(
            [Match(packageId)],
            new Dictionary<string, byte[]>());
        PackageQueryPlan plan = Accepted(PackageQuery.PlanInput(
            "Contoso.*",
            [Term(PackageQuery.LibraryLiteralTermKey, literal)],
            maximumCandidates: 1,
            maximumMatches: 1,
            targetFramework: "net11.0"));
        var assessmentSink =
            new RecordingLibraryLiteralAssessmentSink();
        var semanticExecution = new PackageQueryAssemblySemanticExecution(
            new FixedAuthorization(fixture.Authorization),
            fixture.IssueOperation,
            fixture.PayloadAcquisition,
            new PackageAssemblySemanticFindBudget(
                PackageAssemblySemanticFindBudget.Default.Payload,
                PackageAssemblySemanticFindBudget.Default.Evaluation,
                maximumAggregateOccurrences: 1),
            assessmentSink);

        PackageQueryDocument document =
            (await PackageQueryInspection.ExecuteAsync(
                source,
                plan,
                contentProvider: null,
                dependencyTraversalServices: null,
                semanticExecution,
                nonterminalSink: null,
                TestContext.Current.CancellationToken)).Content;

        Assert.Empty(document.Results);
        PackageQueryFailure failure = Assert.Single(document.Failures);
        Assert.Equal(
            PackageQueryFailureKind.AssemblyEvaluation,
            failure.Kind);
        Assert.Contains("aggregate limit of 1", failure.Message);
        PackageQueryLibraryLiteralAssessment assessment =
            Assert.Single(document.LibraryLiteralAssessments);
        Assert.Equal(
            PackageQueryLibraryLiteralAssessmentKind.Failure,
            assessment.Kind);
        Assert.Equal(
            PackageQueryLibraryLiteralFailureKind.Evaluation,
            assessment.FailureKind);
        Assert.Equal(
            PackageAssemblyFailureStage.SemanticWorkLimit.ToString(),
            assessment.FailureStage);
        Assert.NotEmpty(assessment.Libraries);
        Assert.All(
            assessment.Libraries,
            library => Assert.Equal(
                PackageQueryLibraryLiteralLibraryAssessmentKind.Matched,
                library.Kind));
        Assert.Single(assessmentSink.Assessments);
        Assert.Equal(0, document.Summary.SemanticMatches);
        Assert.Equal(0, document.Summary.Occurrences);
    }

    private sealed class RecordingLibraryLiteralAssessmentSink
        : IPackageQueryLibraryLiteralAssessmentSink
    {
        internal List<PackageQueryLibraryLiteralAssessment> Assessments
            { get; } = [];

        public ValueTask ReportAsync(
            PackageQueryLibraryLiteralAssessment assessment,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assessments.Add(assessment);
            return ValueTask.CompletedTask;
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

    private sealed class DelayedAuthorization(
        PackageSourceAuthorization authorization,
        int delayOnCall,
        TimeSpan delay)
        : IPackageSourceAuthorization
    {
        private int _calls;

        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            if (Interlocked.Increment(ref _calls) == delayOnCall)
                Thread.Sleep(delay);
            return authorization;
        }
    }

    private sealed class SemanticQueryFixture : IAsyncDisposable
    {
        private const string Framework = "net11.0";
        private const string Version = "1.0.0";

        internal PackageSourceAuthorization Authorization { get; } =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);

        private IPackageSourceClient Source { get; }

        private PackageSourceSettlementLease Settlement { get; }

        private InMemoryPackageStore Store { get; } = new();

        internal PackagePayloadAcquisitionPlan PayloadAcquisition { get; }

        internal SemanticQueryFixture()
        {
            Source = PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                Authorization.Authorities[0].Association,
                factory => new MissingPayloadSource(factory));
            Settlement = PackageSourceSettlementService.IssueLease(
                _ => Source);
            PayloadAcquisition = new PackagePayloadAcquisitionPlan(
                (_, _) => Store);
        }

        internal PackageSourceOperationLease IssueOperation(
            CancellationToken cancellationToken) =>
            IssueOperation(
                cancellationToken,
                PackageAssemblySemanticFindBudget.Default.MaximumDuration);

        internal PackageSourceOperationLease IssueOperation(
            CancellationToken cancellationToken,
            TimeSpan operationTimeout) =>
            Settlement.IssueOperationLease(
                cancellationToken,
                operationTimeout: operationTimeout);

        internal Task CacheAssemblyAsync(
            string packageId,
            byte[] image) =>
            CacheAsync(
                packageId,
                ($"lib/{Framework}/{packageId}.dll", image));

        internal Task CacheAssembliesAsync(
            string packageId,
            params (string FileName, byte[] Image)[] assemblies) =>
            CacheAsync(
                packageId,
                [
                    .. assemblies.Select(assembly =>
                        ($"lib/{Framework}/{assembly.FileName}",
                            assembly.Image)),
                ]);

        private async Task CacheAsync(
            string packageId,
            params (string Path, byte[] Content)[] entries)
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

            await Store.CommitAsync(
                packageId,
                Version,
                Source.Source.Producer.Key,
                new MemoryStream(
                    buffer.ToArray(),
                    writable: false),
                TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Settlement.DisposeAsync();
            Source.Dispose();
        }

        private static void Write(
            ZipArchive archive,
            string path,
            byte[] content)
        {
            using Stream entry = archive.CreateEntry(path).Open();
            entry.Write(content);
        }
    }

    private sealed class MissingPayloadSource(
        PackageSourceResultFactory results)
        : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source => results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.PackagePayload;

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            Task.FromResult(
                results.FailedPackage(
                    PackageSourceCoordinate.Create(packageId, version),
                    PackageSourceFailureKind.NotFound));

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

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
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
