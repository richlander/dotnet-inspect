using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InspectWeb.Engine.PackageFacade;
using NuGetFetch;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageAssemblyQueryTests
{
    const string PackageId = "assembly.query.fixture";
    const string Version = "1.0.0";
    const string Framework = "net8.0";
    const string Marker = "shared-literal-use-marker";
    static string Producer => NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url);
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void DiscoveryUsesTheProductPatternRegistry()
    {
        var patterns = JsonSerializer.Deserialize(
            PackageExports.ListPackageAssemblyQueryPatterns(),
            BrowserPackageJsonContext.Default.BrowserPackageAssemblyQueryPatternArray)!;
        var pattern = Assert.Single(patterns);
        Assert.Equal(PackageAssemblyPatterns.StringLiteralContains, pattern.Id);
        Assert.Equal(PackageAssemblyQuery.MaximumPackages, pattern.MaximumPackages);
        Assert.Equal(PackageAssemblyPatterns.Descriptors[0].MaximumOperandLength, pattern.MaximumOperandLength);
    }

    [Fact]
    public async Task RealMatchProjectsEvidenceAndAnExactResourceFreeOpeningRequest()
    {
        PackageRootBinding binding = await Binding(Archive("lib/net8.0/Primary.dll"));
        PackageAssemblyEvaluationOutcome result = await Evaluate(binding, Marker);
        BrowserPackageQueryEvent projected = Project(result);

        Assert.Equal(BrowserPackageQueryEventKind.Match, projected.Kind);
        var row = Assert.IsType<BrowserPackageQueryRow>(projected.Row);
        Assert.Equal(BrowserPackageQueryFacetTier.Assembly, row.Tier);
        Assert.Equal(PackageId, row.PackageId);
        Assert.Contains(row.Evidence, evidence => evidence.Text.Contains(Marker, StringComparison.Ordinal));
        Assert.True(PackageRootReacquisitionRequest.TryDecode(row.RootRequest, out var request));
        Assert.Equal(binding.CreateReacquisitionRequest(), request);
        Assert.Equal(Framework, request.SelectionTargetFramework);
        Assert.Contains("\"tier\":\"Assembly\"", BrowserPackageQueryOperations.Serialize(projected));
    }

    [Theory]
    [InlineData("lib/net8.0/Primary.dll", "literal-marker-present-only-as-a-constant",
        BrowserPackageAssemblyAssessmentKind.NoMatch)]
    [InlineData("ref/net8.0/Primary.dll", Marker,
        BrowserPackageAssemblyAssessmentKind.NotApplicable)]
    public async Task NonMatchesRetainTheirSemanticDisposition(
        string entry, string operand, BrowserPackageAssemblyAssessmentKind expected)
    {
        PackageRootBinding binding = await Binding(Archive(entry));
        BrowserPackageQueryEvent projected = Project(await Evaluate(binding, operand));

        Assert.Equal(BrowserPackageQueryEventKind.Assessment, projected.Kind);
        Assert.Null(projected.Row);
        Assert.Null(projected.Failure);
        Assert.Equal(expected, projected.Assessment!.Disposition);
        Assert.Equal(binding.CreateReacquisitionRequest().Encode(), projected.Assessment.RootRequest);
    }

    [Fact]
    public async Task UnreadableAssemblyRemainsAnEvaluationFailure()
    {
        PackageRootBinding binding = await Binding(Archive("lib/net8.0/Primary.dll", [1, 2, 3]));
        BrowserPackageQueryEvent projected = Project(await Evaluate(binding, Marker));

        Assert.Equal(BrowserPackageQueryEventKind.Failure, projected.Kind);
        Assert.Equal(BrowserPackageQueryFailureKind.AssemblyEvaluation, projected.Failure!.Kind);
        Assert.Contains("ImageAdmission", projected.Failure.Message);
        Assert.Null(projected.Row);
        Assert.Null(projected.Assessment);
    }

    [Fact]
    public void CompletionRetainsMissesApplicabilityAndFiniteScope()
    {
        BrowserPackageQueryEvent projected = BrowserPackageQueryOperations.ProjectAssembly(
            Plan(), new PackageAssemblyQueryEvent.Completed(new(4, 1, 1, 1, 1)));
        var completion = Assert.IsType<BrowserPackageQueryCompletion>(projected.Completion);
        Assert.Equal(BrowserPackageQueryCompletionKind.ExplicitCandidatesComplete, completion.Kind);
        Assert.Equal(1, completion.SemanticMisses);
        Assert.Equal(1, completion.NotApplicable);
        Assert.Equal(1, completion.Failures);
        Assert.Contains("not all package assemblies", completion.Scope);
    }

    [Fact]
    public async Task ExactOpeningUsesReacquiredContentAndPreservesFrameworkNeutralSelection()
    {
        byte[] archive = Archive("lib/net8.0/Primary.dll");
        PackageRootBinding candidate = await Binding(archive, frameworkNeutral: true);
        BrowserPackageQueryRow row = Project(await Evaluate(candidate, Marker)).Row!;
        using var archiveStream = new MemoryStream(archive, writable: false);
        using var reservation = await BrowserPackageWorkspace.ReservePackageDownloadAsync(
            $"{PackageId}@{Version}", archive.LongLength);
        IPackageContent destination = await BrowserPackageWorkspace.SessionPackageStore.CommitAsync(
            PackageId, Version, Producer, archiveStream, Cancellation);
        reservation.Complete();
        Assert.NotSame(candidate.ContentGenerationIdentity, destination.GenerationIdentity);
        Assert.True(PackageRootReacquisitionRequest.TryDecode(row.RootRequest, out var request));

        BrowserPackageCoordinate reopened = await BrowserPackageWorkspace.ReacquireAsync(request, Cancellation);
        Assert.Same(destination.GenerationIdentity, reopened.Package.Content.GenerationIdentity);
        Assert.True(reopened.Root.ReferencesContent(reopened.Package.Content));
        Assert.Equal(request, reopened.Binding!.CreateReacquisitionRequest());
        Assert.Null(reopened.Binding.Coordinate.Framework);
        Assert.Equal(Framework, reopened.Binding.CreateReacquisitionRequest().SelectionTargetFramework);
        Assert.Equal("net8.0", reopened.Framework);

        string json = await PackageExports.OpenPackageAssemblyQueryResult(row.RootRequest!);
        BrowserPackageSurface surface = JsonSerializer.Deserialize(
            json, BrowserPackageJsonContext.Default.BrowserPackageSurface)!;
        Assert.Equal(PackageId, surface.Package);
        Assert.Equal(Version, surface.Version);
        Assert.Equal("net8.0", surface.ActiveFramework);
        Assert.NotEmpty(surface.Types);
    }

    [Fact]
    public async Task InvalidOpeningTokenFailsInsteadOfResolvingACoordinate()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            PackageExports.OpenPackageAssemblyQueryResult("not-a-root-request"));
    }

    static PackageAssemblyQueryPlan Plan() =>
        PackageAssemblyQuery.Plan(PackageAssemblyPatterns.StringLiteralContains,
            Marker, [$"{PackageId}@{Version}"], Framework);

    static BrowserPackageQueryEvent Project(PackageAssemblyEvaluationOutcome outcome) =>
        BrowserPackageQueryOperations.ProjectAssembly(Plan(), new PackageAssemblyQueryEvent.Evaluated(outcome));

    static Task<PackageAssemblyEvaluationOutcome> Evaluate(PackageRootBinding binding, string operand) =>
        PackageAssemblyEvaluator.EvaluateAsync(binding,
            PackageAssemblyPatterns.CreateRequest(PackageAssemblyPatterns.StringLiteralContains, operand),
            PackageAssemblyEvaluationBudget.Default, Cancellation);

    static async Task<PackageRootBinding> Binding(byte[] archive, bool frameworkNeutral = false)
    {
        var store = new InMemoryPackageStore();
        using var stream = new MemoryStream(archive, writable: false);
        await store.CommitAsync(PackageId, Version, Producer, stream, Cancellation);
        using var http = new HttpClient(new NoNetwork());
        return Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
            await PackageRootAcquisition.AcquireAsync(
                frameworkNeutral
                    ? PackageRootAcquisitionRequest.CreateFrameworkNeutral(PackageId, Version, Framework)
                    : PackageRootAcquisitionRequest.Create(PackageId, Version, Framework),
                new WorkspaceContextLoadOptions
                {
                    HttpClient = http,
                    SourceAuthorization = new UniformPackageSourceAuthorization([PackageSource.NuGetOrg]),
                    PackageStore = store,
                }, Cancellation)).Binding;
    }

    static byte[] Archive(string entryPath, byte[]? image = null)
    {
        image ??= File.ReadAllBytes(FixtureCatalog.AnalysisStringLiterals.AssemblyPath());
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream manifest = archive.CreateEntry("fixture.nuspec").Open())
            {
                manifest.Write(Encoding.UTF8.GetBytes(
                    $"<package><metadata><id>{PackageId}</id><version>{Version}</version>"
                    + "<authors>Tests</authors><description>Assembly query fixture</description>"
                    + "</metadata></package>"));
            }
            using Stream entry = archive.CreateEntry(entryPath).Open();
            entry.Write(image);
        }
        return buffer.ToArray();
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cached fixture acquisition must not use the network.");
    }
}
