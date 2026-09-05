using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task SourcePair_AdjacentPdbRequiresCapabilityAndRetainedPath(
        bool allowAdjacent,
        bool retainPath,
        bool compared)
    {
        TestAssembly before = TestAssembly.Create(
            retainPath: retainPath, fixture: FixtureCatalog.SourceDiffV1);
        TestAssembly after = TestAssembly.Create(
            retainPath: retainPath, fixture: FixtureCatalog.SourceDiffV2,
            packageVersion: "2.0.0");
        using var host = QueryHost.WithoutPdb(
            allowLocalSourceReads: true,
            allowAdjacentPdbReads: allowAdjacent);

        var result = await ExecuteSourcePairAsync(before, after, "Value", host);

        Assert.Equal(
            compared ? AssemblyMemberSourcePairStatus.Compared : AssemblyMemberSourcePairStatus.Unavailable,
            result.Status);
        Assert.False(result.IsExact);
        Assert.Empty(host.SourceRequests);
        if (compared)
            Assert.Empty(host.SymbolRequests);
    }

    [Fact]
    public async Task SourcePair_AdjacentPdbHonorsAcquisitionByteLimit()
    {
        TestAssembly before = TestAssembly.Create(
            retainPath: true, fixture: FixtureCatalog.SourceDiffV1);
        TestAssembly after = TestAssembly.Create(
            retainPath: true, fixture: FixtureCatalog.SourceDiffV2,
            packageVersion: "2.0.0");
        using var host = QueryHost.WithoutPdb(
            new DotnetInspector.Packages.SymbolAcquisitionLimits(
                maxSymbolPackageBytes: 1024,
                maxPortablePdbBytes: 1,
                maxSymbolPackageEntries: 1),
            allowLocalSourceReads: true,
            allowAdjacentPdbReads: true);

        var result = await ExecuteSourcePairAsync(before, after, "Value", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        var endpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(endpoint.Source);
        Assert.Equal(PdbMemberSourceOutcome.PortablePdbAcquisitionFailed, unavailable.Inspection.Outcome);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Theory]
    [InlineData("Value", false)]
    [InlineData("Unchanged", true)]
    [InlineData("SameSource", true)]
    [InlineData("Reordered", false)]
    [InlineData("MovedBlock", false)]
    [InlineData("MovedBlockAndEdit", false)]
    public async Task SourcePair_UsesVerifiedAuthoredDeclarationsWithoutDecompilation(
        string memberName,
        bool exact)
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, memberName, host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, result.Status);
        Assert.Equal(exact, result.IsExact);
        Assert.NotNull(result.Comparison);
        var comparison = Assert.IsType<FindingComparison<string>.Complete>(result.Comparison.Value);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(
            result.Before);
        var afterEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(
            result.After);
        var beforeSource = Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
            beforeEndpoint.Source);
        var afterSource = Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
            afterEndpoint.Source);
        Assert.Equal(SourceChecksumVerification.Exact, beforeSource.Inspection.ChecksumVerification);
        Assert.Equal(SourceChecksumVerification.Exact, afterSource.Inspection.ChecksumVerification);
        Assert.Equal(before.Assembly.Registration, beforeEndpoint.Subject.Registration);
        Assert.Equal(after.Assembly.Registration, afterEndpoint.Subject.Registration);
        Assert.NotEqual(beforeEndpoint.Subject.Registration, afterEndpoint.Subject.Registration);
        Assert.Equal(beforeEndpoint.Request.MetadataToken, afterEndpoint.Request.MetadataToken);
        Assert.NotEqual(beforeEndpoint.Subject.Identity.Version, afterEndpoint.Subject.Identity.Version);
        Assert.Equal(0, before.Policy.SelectionCount);
        Assert.Equal(0, after.Policy.SelectionCount);
        Assert.Contains(host.SourceRequests, uri => uri.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal));
        Assert.Contains(host.SourceRequests, uri => uri.AbsolutePath.StartsWith("/v2/", StringComparison.Ordinal));
        if (memberName == "Value")
        {
            Assert.Contains("1 + 2", beforeSource.Inspection.Text);
            Assert.Contains("=> 3", afterSource.Inspection.Text);
        }
        if (memberName is "MovedBlock" or "MovedBlockAndEdit")
            Assert.Contains(comparison.Pairs, pair => pair.Difference == FindingDifferenceKind.Moved);
        if (memberName == "MovedBlock")
            Assert.All(comparison.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public async Task SourcePair_ResolvesEachPhysicalTokenIndependently()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "Value", host, typeName: "MovedCounter");

        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, result.Status);
        Assert.False(result.IsExact);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        var afterEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.After);
        Assert.NotEqual(beforeEndpoint.Request.MetadataToken, afterEndpoint.Request.MetadataToken);
        Assert.Equal(beforeEndpoint.Request.Member, afterEndpoint.Request.Member);
        Assert.Equal(
            after.MemberRequest("Value", "MovedCounter").MetadataToken,
            afterEndpoint.Request.MetadataToken);
    }

    [Fact]
    public async Task SourcePair_MissingTargetRetainsOtherEndpointWithoutComparison()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "BeforeOnly", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        Assert.False(result.IsExact);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(beforeEndpoint.Source);
        var missing = Assert.IsType<AssemblyMemberSourcePairEndpoint.NotFound>(result.After);
        Assert.Equal(AssemblySourceFailureKind.TargetNotFound, missing.Failure.Kind);
        Assert.Equal(after.Assembly.Registration, missing.Subject.Registration);
        Assert.Equal(0, after.Policy.SelectionCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourcePair_UnavailableOrFailedAcquisitionIsNotSourceRemoval(
        bool missingPdb)
    {
        var (before, after) = SourcePairAssemblies();
        using var host = QueryHost.WithPairPdb(
            before,
            after,
            SourcePairBytes(FixtureCatalog.SourceDiffV1),
            "not the compiled source"u8.ToArray(),
            missingAfterPdb: missingPdb);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "Value", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        Assert.False(result.IsExact);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(beforeEndpoint.Source);
        var afterEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.After);
        var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(afterEndpoint.Source);
        Assert.False(unavailable.Inspection.IsComplete);
        if (!missingPdb)
        {
            Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, unavailable.Inspection.Outcome);
            Assert.Equal(SourceChecksumVerification.Mismatch, unavailable.Inspection.ChecksumVerification);
        }
        Assert.Equal(0, before.Policy.SelectionCount);
        Assert.Equal(0, after.Policy.SelectionCount);
    }

    [Fact]
    public async Task SourcePair_FirstAcquisitionFailureDoesNotSuppressSecondEndpoint()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = QueryHost.WithPairPdb(
            before, after,
            "not the compiled source"u8.ToArray(),
            SourcePairBytes(FixtureCatalog.SourceDiffV2));

        var result = await ExecuteSourcePairAsync(before, after, "Value", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(beforeEndpoint.Source);
        Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, unavailable.Inspection.Outcome);
        var afterEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.After);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(afterEndpoint.Source);
    }

    [Fact]
    public async Task SourcePair_RejectedImageRetainsSuccessfulOtherEndpoint()
    {
        var (before, _) = SourcePairAssemblies();
        TestAssembly after = TestAssembly.Create(
            selectedName: "Different.Identity",
            fixture: FixtureCatalog.SourceDiffV2,
            packageVersion: "2.0.0");
        using var host = SourcePairHost(before, after);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "Value", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        var beforeEndpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(beforeEndpoint.Source);
        var rejected = Assert.IsType<AssemblyMemberSourcePairEndpoint.Rejected>(result.After);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcePair_CancellationBeforeOrDuringAcquisitionPropagates(
        bool duringAcquisition)
    {
        var (before, after) = SourcePairAssemblies();
        using var cancellation = new CancellationTokenSource();
        using var host = SourcePairHost(
            before, after, duringAcquisition ? cancellation.Cancel : null);
        if (!duringAcquisition)
            cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteSourcePairAsync(
                before, after, "Value", host,
                cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcePair_RevalidatesBothInputsAfterSecondAcquisition(
        bool invalidateAfter)
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(
            before, after,
            invalidateAfter ? after.Policy.ChangeVersion : before.Policy.ChangeVersion);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "Value", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Failed, result.Status);
        Assert.Equal(AssemblySourceFailureKind.InspectionFailed, result.Failure?.Kind);
        Assert.Null(result.Comparison);
        Assert.False(result.IsExact);
        Assert.Equal(0, before.Policy.SelectionCount);
        Assert.Equal(0, after.Policy.SelectionCount);
    }

    [Fact]
    public async Task SourcePair_NonMethodTargetDoesNotAcquireSource()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, "BuildValue", host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.IsType<AssemblyMemberSourcePairEndpoint.NotFound>(result.Before);
        Assert.IsType<AssemblyMemberSourcePairEndpoint.NotFound>(result.After);
        Assert.Null(result.Comparison);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    static (TestAssembly Before, TestAssembly After) SourcePairAssemblies()
        => (
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1),
            TestAssembly.Create(
                fixture: FixtureCatalog.SourceDiffV2,
                packageVersion: "2.0.0"));

    static byte[] SourcePairBytes(FixtureDefinition fixture)
        => File.ReadAllBytes(Assert.Single(
            fixture.SourcePaths(),
            path => Path.GetFileName(path) == "Counter.cs"));

    static QueryHost SourcePairHost(
        TestAssembly before,
        TestAssembly after,
        Action? duringAfterSource = null)
        => QueryHost.WithPairPdb(
            before,
            after,
            SourcePairBytes(FixtureCatalog.SourceDiffV1),
            SourcePairBytes(FixtureCatalog.SourceDiffV2),
            duringAfterSource);

    static async Task<AssemblyMemberSourcePairResult> ExecuteSourcePairAsync(
        TestAssembly before,
        TestAssembly after,
        string memberName,
        QueryHost host,
        string typeName = "Counter",
        CancellationToken? cancellationToken = null)
    {
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup([before.Participant]);
        AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup([after.Participant]);
        var target = before.MemberTarget(memberName, typeName);
        return await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
            beforeGroup,
            before.Participant,
            afterGroup,
            after.Participant,
            AssemblyMemberSourcePairRequest.From(target.Type, target.Member),
            host.Context,
            cancellationToken ?? TestContext.Current.CancellationToken);
    }
}
