using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Libraries;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    [Fact]
    public async Task
        MemberDecompilation_NoPdbPreservesNativeAttemptAndFreshLeases()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        LibraryOperationLease firstLease =
            library.IssueOperation();

        SourceHouseMemberDecompilationOutcome.Completed first =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    firstLease,
                    TestContext.Current.CancellationToken));

        AssertOperationSettled(
            firstLease,
            library.Reference.ApiAssembly);
        Assert.Equal(
            SourceHouseSourcePolicy.DecompiledOnly,
            first.Request.SourcePolicy);
        Assert.Equal(
            SourceHousePdbContributionKind.Unavailable,
            first.PdbContribution.Kind);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            first.Attempt.Status);
        Assert.False(first.Attempt.PdbSupplied);
        Assert.Contains(
            "ExtractMemberText",
            first.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            first.Attempt.BodyProjectionsAttempted,
            first.Work.BodyProjectionsAttempted);

        SourceHouseMemberDecompilationOutcome.Completed second =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));
        Assert.Equal(first.Attempt.Text, second.Attempt.Text);
    }

    [Fact]
    public async Task
        MemberDecompilation_CompanionPdbContributesNativeSymbols()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHousePdbContributionKind.SuppliedCompanion,
            completed.PdbContribution.Kind);
        Assert.Same(
            library.PortablePdb,
            completed.PdbContribution.Content);
        Assert.True(completed.Attempt.PdbSupplied);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
        Assert.NotEmpty(completed.Attempt.BodyProjections);
    }

    [Fact]
    public async Task
        MemberDecompilation_InvalidCompanionFailsWithoutNoPdbRetry()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                "not a portable pdb"u8.ToArray());

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHousePdbContributionKind.SuppliedCompanion,
            completed.PdbContribution.Kind);
        Assert.True(completed.Attempt.PdbSupplied);
        Assert.Equal(
            CSharpDecompilationStatus.Failed,
            completed.Attempt.Status);
        Assert.Contains(
            "InvalidDataException",
            completed.Attempt.DiagnosticSummary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        MemberDecompilation_CompanionLimitRetainsPdbFreeResult()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath,
                        limits: DecompilationLimits(
                            maximumPortablePdbBytes: 1)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHousePdbContributionKind.Incomplete,
            completed.PdbContribution.Kind);
        Assert.False(completed.Attempt.PdbSupplied);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
    }

    [Fact]
    public async Task
        MemberDecompilation_ExactAccessorPreservesMethodIdentity()
    {
        string assemblyPath =
            typeof(SourceHousePdbContribution).Assembly.Location;
        SourceHouseTarget.MemberTarget target =
            AccessorTarget(
                assemblyPath,
                typeof(SourceHousePdbContribution).FullName!,
                nameof(SourceHousePdbContribution.Kind),
                "get_Kind");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
        CSharpBodyProjection body =
            Assert.Single(
                completed.Attempt.BodyProjections,
                projection =>
                    projection.Address.Token
                        == target.MetadataToken);
        Assert.True(body.ContributesToOutput);
        Assert.Contains(
            "get_Kind",
            completed.Attempt.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        MemberDecompilation_EmbeddedPdbContributesNativeSymbols()
    {
        RealAsset asset = EmbeddedSourceComparisonAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHousePdbContributionKind.Embedded,
            completed.PdbContribution.Kind);
        Assert.True(completed.Attempt.PdbSupplied);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
    }

    [Fact]
    public async Task
        MemberDecompilation_FiniteBodyProjectionLimitIsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseMemberDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath,
                        maximumBodyProjections: 0),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            completed.Attempt.Status);
        Assert.Equal(0, completed.Attempt.BodyProjectionsAttempted);
        Assert.Equal(0, completed.Work.BodyProjectionsAttempted);
    }

    [Fact]
    public async Task
        MemberDecompilation_FiniteAssemblyLimitIsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseMemberDecompilationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Incomplete>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath,
                        limits: DecompilationLimits(
                            maximumAssemblyBytes: 1)),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseIncompleteBoundary.AssemblyBytes,
            incomplete.Boundary);
        Assert.True(incomplete.Work.AssemblyBytesObserved > 1);
    }

    [Fact]
    public async Task
        MemberDecompilation_MismatchedAnchorIsRejected()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        SourceHouseTarget.MemberTarget target = new(
            asset.MemberTarget.Type,
            asset.MemberTarget.Member with
            {
                MemberName = "NotTheSelectedMember",
            },
            asset.MemberTarget.MetadataToken);

        SourceHouseMemberDecompilationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Rejected>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseRejectionKind.TargetMismatch,
            rejected.Rejection.Kind);
    }

    [Fact]
    public async Task
        MemberDecompilation_TargetInspectionFailureIsFailedNotRejected()
    {
        byte[] assembly = BuildMalformedTargetSurface();
        AssemblyReferenceIdentity identity =
            ReadAssemblyIdentity(assembly);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                identity);
        MetadataTypeDefinitionName targetType = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Consumer"]))
            .Name;
        const string canonicalSignature = "void N.Consumer.Use()";
        SourceHouseTarget.MemberTarget target = new(
            targetType,
            new(
                "Use:1",
                canonicalSignature,
                MemberAnchor.ComputeFingerprint(canonicalSignature),
                "N.Consumer",
                "Use"),
            MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)));

        SourceHouseMemberDecompilationOutcome.Failed failed =
            Assert.IsType<
                SourceHouseMemberDecompilationOutcome.Failed>(
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        typeof(SourceHouse).Assembly.Location),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseFailureStage.AssemblyInspection,
            failed.Failure.Stage);
        Assert.Equal(
            "TargetInspectionFailed",
            failed.Failure.Code);
        Assert.Contains(
            "type row",
            failed.Failure.Detail,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceHousePdbContributionKind.Failed,
            failed.PdbContribution.Kind);
        Assert.Single(failed.PdbContribution.Observations);
    }

    [Fact]
    public async Task
        MemberDecompilation_CancellationSettlesOperationLease()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await SourceHouse.ExecuteMemberDecompilationAsync(
                    DecompilationRequest(
                        library,
                        asset.MemberTarget,
                        asset.AssemblyPath),
                    operation,
                    cancellation.Token));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    private static SourceHouseMemberDecompilationRequest
        DecompilationRequest(
            LibraryFixture library,
            SourceHouseTarget.MemberTarget target,
            string assemblyPath,
            SourceHouseMemberDecompilationLimits? limits = null,
            int maximumBodyProjections =
                CSharpDecompilerService.DefaultMaxBodyProjections) =>
        new(
            SourceHouseRequestIdentity.Create(
                "test-member-decompilation"),
            library.Reference,
            library.Reference.ApiAssembly,
            target,
            new(
                SourceHouseOperationPlanIdentity.Create(
                    "test-member-decompilation-plan"),
                SourceHousePolicyGeneration.Create(
                    "test-member-decompilation-policy"),
                limits ?? DecompilationLimits(),
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        assemblyPath)),
                maximumBodyProjections:
                    maximumBodyProjections));

    private static SourceHouseMemberDecompilationLimits
        DecompilationLimits(
            int maximumAssemblyBytes = 64 * 1024 * 1024,
            int maximumPortablePdbBytes = 64 * 1024 * 1024) =>
        new(
            maximumAssemblyBytes,
            maximumPortablePdbBytes,
            s_targetBounds,
            new SourceLinkReadLimits(
                maxEmbeddedPdbBytes:
                    maximumPortablePdbBytes,
                maxMapBytes: 16 * 1024 * 1024,
                maxMappings: 100_000));

    private static SourceHouseTarget.MemberTarget AccessorTarget(
        string assemblyPath,
        string typeName,
        string propertyName,
        string accessorName)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        ApiType type = Assert.Single(
            session.ApiSurface(includeAll: true).Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        ApiMember owner = Assert.Single(
            type.Members,
            candidate => candidate.Name == propertyName);
        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(owner, type),
            candidate => candidate.Name == accessorName);
        return new(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, accessor),
            accessor.MetadataToken!.Value);
    }
}
