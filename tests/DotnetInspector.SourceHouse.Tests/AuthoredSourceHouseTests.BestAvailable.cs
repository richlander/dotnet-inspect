using CSharpText.MemberSlicing;
using DotnetInspector.Libraries;
using ILInspector.Decompiler;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    [Fact]
    public async Task
        BestAvailable_AuthoredSuccessSkipsDecompilation()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation =
            library.IssueOperation();

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        [
                            Capability(
                                "repository",
                                SourceHouseCapabilityCategory
                                    .Repository,
                                (candidate, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(
                                                    ReadCandidateSource(
                                                        candidate)))),
                        ]),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Authored,
            available.Selected);
        Assert.IsType<SourceHouseOutcome.Available>(
            available.AuthoredOutcome);
        Assert.Null(available.DecompilationOutcome);
        Assert.Contains(
            nameof(MemberTextSlicer.ExtractMemberText),
            available.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceHouseSourcePolicy.BestAvailable,
            available.Request.SourcePolicy);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        BestAvailable_AuthoredUnavailableFallsBackToDecompilation()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        capabilities: []),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Decompiled,
            available.Selected);
        Assert.IsType<SourceHouseOutcome.Unavailable>(
            available.AuthoredOutcome);
        SourceHouseDecompilationOutcome.Completed decompiled =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationOutcome);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            decompiled.Attempt.Status);
        Assert.Contains(
            nameof(MemberTextSlicer.ExtractMemberText),
            available.Text,
            StringComparison.Ordinal);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        BestAvailable_PortablePdbUnavailableSkipsAuthoredWork()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        capabilities: [],
                        authoredPrecondition:
                            SourceHouseBestAvailableAuthoredPrecondition
                                .PortablePdbUnavailable),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Decompiled,
            available.Selected);
        SourceHouseOutcome.Unavailable authored =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                available.AuthoredOutcome);
        Assert.Equal(
            SourceHouseBestAvailableAuthoredPrecondition
                .PortablePdbUnavailable,
            available.Request.AuthoredPrecondition);
        Assert.Equal(
            0,
            authored.Work.AssemblyBytesObserved);
        Assert.Empty(
            authored.AuthoredAttempt.SourceAttempts);
    }

    [Fact]
    public async Task
        BestAvailable_PortablePdbUnavailableDoesNotOverrideCompanion()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        [
                            Capability(
                                "repository",
                                SourceHouseCapabilityCategory
                                    .Repository,
                                (candidate, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(
                                                    ReadCandidateSource(
                                                        candidate)))),
                        ],
                        authoredPrecondition:
                            SourceHouseBestAvailableAuthoredPrecondition
                                .PortablePdbUnavailable),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Authored,
            available.Selected);
        Assert.IsType<SourceHouseOutcome.Available>(
            available.AuthoredOutcome);
        Assert.Null(available.DecompilationOutcome);
        Assert.True(
            available.AuthoredOutcome.Work.AssemblyBytesObserved > 0);
    }

    [Fact]
    public async Task
        BestAvailable_BodyProjectionBudgetIsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);

        SourceHouseBestAvailableOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Incomplete>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        capabilities: [],
                        maximumBodyProjections: 0),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseIncompleteBoundary.BodyProjections,
            incomplete.Boundary);
        SourceHouseDecompilationOutcome.Completed decompiled =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                incomplete.DecompilationOutcome);
        Assert.Equal(
            CSharpDecompilationStatus.Incomplete,
            decompiled.Attempt.Status);
    }

    [Fact]
    public async Task
        BestAvailable_CancellationSettlesLease()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
            async () =>
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset,
                        capabilities: []),
                    operation,
                    cancellation.Token));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        BestAvailable_MismatchedLeaseRejectsAndSettles()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture requested =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);
        await using LibraryFixture issued =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);
        LibraryOperationLease operation =
            issued.IssueOperation();

        SourceHouseBestAvailableOutcome.Rejected rejected =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Rejected>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        requested,
                        asset,
                        capabilities: []),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseRejectionKind
                .LeaseReferenceMismatch,
            rejected.Rejection.Kind);
        Assert.IsType<SourceHouseOutcome.Rejected>(
            rejected.AuthoredOutcome);
        Assert.IsType<
            SourceHouseDecompilationOutcome.Rejected>(
                rejected.DecompilationOutcome);
        AssertOperationSettled(
            operation,
            issued.Reference.ApiAssembly);
    }

    private static SourceHouseBestAvailableRequest
        BestAvailableRequest(
            LibraryFixture library,
            RealAsset asset,
            IReadOnlyList<ISourceHouseSourceCapability>
                capabilities,
            int maximumBodyProjections =
                CSharpDecompilerService
                    .DefaultMaxBodyProjections,
            SourceHouseBestAvailableAuthoredPrecondition
                authoredPrecondition =
                    SourceHouseBestAvailableAuthoredPrecondition
                        .Attempt)
    {
        SourceHouseAuthoredRequest authored = Request(
            library,
            asset.MemberTarget,
            capabilities);
        SourceHouseDecompilationRequest decompiled =
            DecompilationRequest(
                library,
                asset.MemberTarget,
                asset.AssemblyPath,
                maximumBodyProjections:
                    maximumBodyProjections);
        SourceHouseDecompilationPlan plan =
            new(
                authored.Plan.Identity,
                authored.Plan.PolicyGeneration,
                decompiled.Plan.Limits,
                decompiled.Plan.BindingPolicy,
                decompiled.Plan.PrinterOptions,
                decompiled.Plan.MaximumBodyProjections);
        return new(
            SourceHouseRequestIdentity.Create(
                "test-best-available"),
            library.Reference,
            library.Reference.ApiAssembly,
            asset.MemberTarget,
            authored.Plan,
            plan,
            authoredPrecondition);
    }
}
