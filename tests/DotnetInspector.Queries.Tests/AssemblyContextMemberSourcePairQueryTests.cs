using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // Selected-member outcome and boundary cases are PR-fast.
    [Fact]
    public async Task SourcePair_RealRepositoryMemberUsesAuthoredHouse()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly before = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        TestAssembly after = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")));

        var result = await ExecuteSourcePairAsync(
            before, after, "ExtractMemberText", host, typeName: "MemberTextSlicer");

        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, result.Status);
        Assert.True(result.IsExact);
        var endpoint = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        var house = Assert.IsType<SourceHouseOutcome.Available>(endpoint.HouseOutcome);
        Assert.StartsWith("public static string? ExtractMemberText(", house.Source.Text.TrimStart());
        Assert.Equal(SourceHouseSourceUnitScope.ExactMember, house.Source.Mapping!.Scope);
    }

    [Fact]
    public async Task SourcePair_RealRepositoryResolvesDistinctEndpointAnchors()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly before = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        TestAssembly after = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")));
        var beforeTarget = before.MemberTarget(
            "ExtractMemberText", "MemberTextSlicer");
        var afterTarget = after.MemberTarget(
            "GetMemberTextParts", "MemberTextSlicer");
        var request = new AssemblyMemberSourcePairRequest(
            AssemblyMemberSourcePairEndpointRequest.From(
                beforeTarget.Type, beforeTarget.Member),
            AssemblyMemberSourcePairEndpointRequest.From(
                afterTarget.Type, afterTarget.Member));

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, request, host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, result.Status);
        Assert.False(result.IsExact);
        var beforeEndpoint =
            Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.Before);
        var afterEndpoint =
            Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(result.After);
        Assert.Equal("ExtractMemberText", beforeEndpoint.Request.Member.MemberName);
        Assert.Equal("GetMemberTextParts", afterEndpoint.Request.Member.MemberName);
        Assert.Contains(
            "ExtractMemberText",
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
                beforeEndpoint.Source).Inspection.Text);
        Assert.Contains(
            "GetMemberTextParts",
            Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(
                afterEndpoint.Source).Inspection.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourcePair_RealRepositoryRetainsOneRequestedEndpoint(
        bool requestBefore)
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly before = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        TestAssembly after = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")));
        var target = before.MemberTarget(
            "ExtractMemberText", "MemberTextSlicer");
        AssemblyMemberSourcePairEndpointRequest endpoint =
            AssemblyMemberSourcePairEndpointRequest.From(
                target.Type, target.Member);
        var request = new AssemblyMemberSourcePairRequest(
            requestBefore ? endpoint : null,
            requestBefore ? null : endpoint);

        AssemblyMemberSourcePairResult result = await ExecuteSourcePairAsync(
            before, after, request, host);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        AssemblyMemberSourcePairEndpoint requested =
            requestBefore ? result.Before : result.After;
        AssemblyMemberSourcePairEndpoint unrequested =
            requestBefore ? result.After : result.Before;
        var resolved =
            Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(requested);
        Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(resolved.Source);
        Assert.IsType<AssemblyMemberSourcePairEndpoint.Unrequested>(unrequested);
    }

    [Theory]
    [InlineData(false, PdbMemberSourceOutcome.InvalidSequencePointCoordinates)]
    [InlineData(true, PdbMemberSourceOutcome.SourceTooComplex)]
    public async Task SourcePair_ProducerSlicingFailuresRemainDistinct(
        bool tokenDense, PdbMemberSourceOutcome expected)
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        string original = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs"));
        byte[] source = Encoding.UTF8.GetBytes(tokenDense
            ? new string(';', 500_001) + original
            : "namespace CSharpText.MemberSlicing;");
        byte[] pdb = ReplaceSourcePairDocumentChecksum(pdbPath, "MemberTextSlicer.cs", source);
        TestAssembly before = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        TestAssembly after = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(Path.GetFileName(pdbPath), pdb, source);

        var result = await ExecuteSourcePairAsync(
            before, after, "ExtractMemberText", host, typeName: "MemberTextSlicer");

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        foreach (var endpoint in new[] { result.Before, result.After })
        {
            var resolved = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(endpoint);
            var failure = Assert.IsType<SourceHouseOutcome.Failed>(resolved.HouseOutcome);
            Assert.Equal(SourceHouseFailureStage.SourceSlicing, failure.Failure.Stage);
            Assert.Equal(expected.ToString(), failure.Failure.Code);
            var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(resolved.Source);
            Assert.Equal(expected, unavailable.Inspection.Outcome);
            Assert.Equal(SourceChecksumVerification.Exact, unavailable.Inspection.ChecksumVerification);
            Assert.IsType<FindingInspection<string>.Failed>(unavailable.Inspection.Lines.Value);
        }
    }

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
        foreach (var endpoint in new[] { beforeEndpoint, afterEndpoint })
        {
            var house = Assert.IsType<SourceHouseOutcome.Available>(endpoint.HouseOutcome);
            Assert.Null(endpoint.LibraryFailure);
            Assert.Equal(SourceHousePdbContributionKind.SuppliedCompanion, house.PdbContribution.Kind);
            Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.LeaseSettlement.Consumer);
            var selected = Assert.IsType<SourceHouseTarget.MemberTarget>(house.Request.Target);
            Assert.Equal(endpoint.Request.MetadataToken, selected.MetadataToken);
            Assert.Equal(endpoint.Request.Member, selected.Member);
            var pdb = Assert.IsType<AssemblySourcePdbProvenance>(
                house.PdbContribution.Content!.ArtifactReference.Provenance);
            Assert.Same(endpoint.Subject.Registration, pdb.SourceRegistration);
        }
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

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task SourcePair_SourceHouseByteBoundIsVisibleAndExact(int adjustment)
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        SourceHouseLimits defaults = host.Context.MemberSourcePairLimits;
        int bytes = Math.Max(
            SourcePairBytes(FixtureCatalog.SourceDiffV1).Length,
            SourcePairBytes(FixtureCatalog.SourceDiffV2).Length) + adjustment;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourcePairLimits = new(
                defaults.MaximumAssemblyBytes, defaults.MaximumPortablePdbBytes,
                defaults.TargetBounds, defaults.SourceLinkReadLimits,
                defaults.MaximumDocuments, defaults.MaximumTargetMappings,
                defaults.MaximumCandidateAttempts, bytes, defaults.MaximumSourceTextCharacters),
        };

        var result = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);

        if (adjustment == 0)
        {
            Assert.Equal(AssemblyMemberSourcePairStatus.Compared, result.Status);
        }
        else
        {
            Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
            Assert.Null(result.Comparison);
            Assert.Contains(new[] { result.Before, result.After }, endpoint =>
                endpoint is AssemblyMemberSourcePairEndpoint.Resolved
                {
                    HouseOutcome: SourceHouseOutcome.Incomplete
                    {
                        Boundary: SourceHouseIncompleteBoundary.SourceBytes,
                    },
                });
            foreach (var resolved in new[] { result.Before, result.After }
                .OfType<AssemblyMemberSourcePairEndpoint.Resolved>()
                .Where(endpoint => endpoint.HouseOutcome is SourceHouseOutcome.Incomplete))
            {
                var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(resolved.Source);
                Assert.Equal(PdbMemberSourceOutcome.SourceLimitExceeded, unavailable.Inspection.Outcome);
            }
        }
    }

    [Fact]
    public async Task SourcePair_AssemblyCaptureBoundPreservesLimitOutcome()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        SourceHouseLimits limits = host.Context.MemberSourcePairLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourcePairLimits = new(
                1, 1, limits.TargetBounds, limits.SourceLinkReadLimits,
                limits.MaximumDocuments, limits.MaximumTargetMappings,
                limits.MaximumCandidateAttempts, limits.MaximumSourceBytes,
                limits.MaximumSourceTextCharacters),
        };

        var result = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        foreach (var endpoint in new[] { result.Before, result.After })
        {
            var resolved = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(endpoint);
            Assert.IsType<AssemblyContextLibraryAdapterResult.Incomplete>(resolved.LibraryFailure);
            var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(resolved.Source);
            Assert.Equal(PdbMemberSourceOutcome.SourceLimitExceeded, unavailable.Inspection.Outcome);
        }
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task SourcePair_ExpiredHouseDeadlineIsNotMissingSource()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourcePairTimeout = TimeSpan.FromTicks(1),
        };

        var result = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);

        Assert.Equal(AssemblyMemberSourcePairStatus.Unavailable, result.Status);
        Assert.Null(result.Comparison);
        foreach (var endpoint in new[] { result.Before, result.After })
        {
            var resolved = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(endpoint);
            var incomplete = Assert.IsType<SourceHouseOutcome.Incomplete>(resolved.HouseOutcome);
            Assert.Equal(SourceHouseIncompleteBoundary.Deadline, incomplete.Boundary);
            var unavailable = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(resolved.Source);
            Assert.Equal(PdbMemberSourceOutcome.SourceDeadlineExceeded, unavailable.Inspection.Outcome);
            Assert.IsType<FindingInspection<string>.Failed>(unavailable.Inspection.Lines.Value);
        }
        Assert.Empty(host.SourceRequests);
    }

    static byte[] ReplaceSourcePairDocumentChecksum(
        string pdbPath, string fileName, byte[] source)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(
            new MemoryStream(bytes, writable: false));
        MetadataReader reader = provider.GetMetadataReader();
        Document document = reader.GetDocument(Assert.Single(reader.Documents, handle =>
            reader.GetString(reader.GetDocument(handle).Name).EndsWith(fileName, StringComparison.Ordinal)));
        byte[] checksum = SHA256.HashData(source);
        Assert.Equal(checksum.Length, reader.GetBlobBytes(document.Hash).Length);
        int entry = FindMetadataStreamOffset(bytes, "#Blob") + MetadataTokens.GetHeapOffset(document.Hash);
        checksum.CopyTo(bytes, entry + CompressedIntegerPrefixSize(bytes[entry]));
        return bytes;
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
        CancellationToken? cancellationToken = null,
        AssemblyContextSourceQueryContext? sourceContext = null)
    {
        var target = before.MemberTarget(memberName, typeName);
        return await ExecuteSourcePairAsync(
            before,
            after,
            AssemblyMemberSourcePairRequest.From(target.Type, target.Member),
            host,
            cancellationToken,
            sourceContext);
    }

    static async Task<AssemblyMemberSourcePairResult> ExecuteSourcePairAsync(
        TestAssembly before,
        TestAssembly after,
        AssemblyMemberSourcePairRequest request,
        QueryHost host,
        CancellationToken? cancellationToken = null,
        AssemblyContextSourceQueryContext? sourceContext = null)
    {
        await using var workspace = new InspectionWorkspace();
        AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([before.Participant]);
        AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([after.Participant]);
        return await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
            beforeGroup,
            before.Participant,
            afterGroup,
            after.Participant,
            request,
            sourceContext ?? host.Context,
            cancellationToken ?? TestContext.Current.CancellationToken);
    }
}
