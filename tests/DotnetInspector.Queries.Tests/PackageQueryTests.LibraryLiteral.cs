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
            Settlement.IssueOperationLease(
                cancellationToken,
                operationTimeout:
                    PackageAssemblySemanticFindBudget.Default.MaximumDuration);

        internal Task CacheAssemblyAsync(
            string packageId,
            byte[] image) =>
            CacheAsync(
                packageId,
                ($"lib/{Framework}/{packageId}.dll", image));

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
