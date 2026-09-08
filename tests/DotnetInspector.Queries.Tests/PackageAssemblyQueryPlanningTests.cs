using System.IO.Compression;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using InertText;
using ILInspector.Analysis;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyQueryPlanningTests
{
    [Fact]
    public void Plan_PreservesExactOrderedSelectionAndDeclaredRole()
    {
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["System.Text.Json@10.0.0", "System.Text.Encodings.Web@10.0.0"],
            "net10.0",
            "linux-x64");

        Assert.Equal("system.text.json", plan.Coordinates[0].PackageId);
        Assert.Equal("system.text.encodings.web", plan.Coordinates[1].PackageId);
        Assert.All(plan.Coordinates, coordinate =>
            Assert.Equal("10.0.0", coordinate.Version));
        Assert.Equal("net10.0", plan.TargetFramework);
        Assert.Equal("linux-x64", plan.RuntimeIdentifier);
        Assert.Equal(
            PackageAssemblyPatternRole.ImplementationBody,
            plan.Pattern.Pattern.Role);
        Assert.Same(PackageAssemblyEvaluationBudget.Default, plan.Budget);
    }

    [Fact]
    public void Plan_FreezesCallerSelection()
    {
        string[] packages = ["System.Text.Json@10.0.0"];
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            packages,
            "net10.0");
        packages[0] = "Other.Package@1.0.0";

        Assert.Equal("system.text.json", Assert.Single(plan.Coordinates).PackageId);
    }

    [Fact]
    public void Plan_RejectsDuplicateNormalizedCoordinates()
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["Example.Package@1.0", "example.package@1.0.0"],
            "net10.0"));
    }

    [Theory]
    [InlineData("Example.Package")]
    [InlineData("Example.Package@")]
    [InlineData("Example.Package@latest")]
    [InlineData("Example.Package@1.*")]
    public void Plan_RequiresExactVersions(string coordinate)
    {
        Assert.ThrowsAny<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            [coordinate],
            "net10.0"));
    }

    [Fact]
    public void Plan_RejectsEmptyOrOversizedSelection()
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            [],
            "net10.0"));
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            Enumerable.Range(1, PackageAssemblyQuery.MaximumPackages + 1)
                .Select(index => $"Package{index}@1.0.0")
                .ToArray(),
            "net10.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-registered")]
    public void Plan_RejectsUnknownPattern(string patternId)
    {
        Assert.Throws<ArgumentException>(() => PackageAssemblyQuery.Plan(
            patternId,
            "Json",
            ["Example.Package@1.0.0"],
            "net10.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Plan_RequiresExplicitSelectionTarget(string targetFramework)
    {
        Assert.ThrowsAny<ArgumentException>(() => PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["Example.Package@1.0.0"],
            targetFramework));
    }

    [Fact]
    public void Budget_RejectsUnboundedOrNonpositiveInputs()
    {
        StringLiteralUsePatternBudget semantic = StringLiteralUsePatternBudget.Default;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(0, 32, semantic, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 1, semantic, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PackageAssemblyEvaluationBudget(16, 32, semantic, TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public async Task Execute_DelegatesBoundedCandidateAcquisitionWithoutSourceIdentity()
    {
        PackageAssemblyQueryPlan plan = PackageAssemblyQuery.Plan(
            PackageAssemblyPatterns.StringLiteralContains,
            "Json",
            ["First.Package@1.0.0", "Second.Package@2.0.0"],
            "net10.0");
        var provider = new RecordingPayloadProvider();
        var events = new List<PackageAssemblyQueryEvent>();

        await foreach (PackageAssemblyQueryEvent queryEvent in
            PackageAssemblyQuery.ExecuteAsync(
                provider,
                plan,
                TestContext.Current.CancellationToken))
        {
            events.Add(queryEvent);
        }

        Assert.Equal(plan.Coordinates, provider.Coordinates);
        Assert.All(provider.Limits, limits =>
        {
            Assert.Equal(32L * 1024 * 1024, limits.MaxArchiveBytes);
            Assert.Equal(256L * 1024 * 1024, limits.MaxExpandedBytes);
        });
        PackageAssemblyQueryEvent.AcquisitionFailed[] failures =
            [.. events.OfType<PackageAssemblyQueryEvent.AcquisitionFailed>()];
        Assert.Equal(2, failures.Length);
        Assert.All(failures, failure =>
        {
            Assert.Equal("configured-test-source", failure.Value.Producer.ToString());
            Assert.Equal("fixture acquisition refused", failure.Value.Message.ToString());
            Assert.Equal(
                PackageSourceFailureKind.NotFound,
                failure.Value.SourceFailureKind);
        });
        Assert.Equal(
            new PackageAssemblyQuerySummary(2, 0, 0, 0, 2),
            Assert.Single(
                events.OfType<PackageAssemblyQueryEvent.Completed>()).Value);
    }

    [Fact]
    public void ModernProducerKey_RoundTripsAnExactRootRebinding()
    {
        string producer = PackageProducerIdentity.NuGetOrg.Key;
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(
            archive,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            zip.CreateEntry("fixture.nuspec");
        }
        var content = new InMemoryPackageContent(
            archive.ToArray(),
            fromCache: false,
            producer);
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(
                "Modern.Producer.Package",
                "1.0.0"),
            content,
            producer,
            PackagePayloadOrigin.Download);
        PackageRootBinding initial =
            PackageRootBinding.CreateFromSource(
                payload,
                "net10.0");
        PackageRootReacquisitionRequest request =
            initial.CreateReacquisitionRequest();
        Assert.True(
            PackageRootReacquisitionRequest.TryDecode(
                request.Encode(),
                out PackageRootReacquisitionRequest? decoded));

        PackageRootRebindingOutcome rebound =
            PackageRootAcquisition.BindReacquired(
                decoded,
                payload);

        PackageRootBinding binding =
            Assert.IsType<PackageRootRebindingOutcome.Bound>(
                rebound).Binding;
        Assert.Equal(request, binding.CreateReacquisitionRequest());
        Assert.Equal(producer, binding.Coordinate.Producer);
    }

    sealed class RecordingPayloadProvider
        : IPackageRootPayloadProvider
    {
        internal List<PackageSourceCoordinate> Coordinates { get; } = [];
        internal List<PackagePayloadLimits> Limits { get; } = [];

        public ValueTask<PackageRootPayloadResult> GetPayloadAsync(
            PackageSourceCoordinate coordinate,
            string? requiredProducerKey,
            PackagePayloadLimits limits,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(requiredProducerKey);
            Coordinates.Add(coordinate);
            Limits.Add(limits);
            return ValueTask.FromResult<PackageRootPayloadResult>(
                new PackageRootPayloadResult.Unavailable(
                    new InertString(
                        TextPolicy.Field,
                        "configured-test-source"),
                    "fixture acquisition refused",
                    PackageRootAcquisitionFailureKind.PackageUnavailable,
                    PackageSourceFailureKind.NotFound));
        }
    }
}
