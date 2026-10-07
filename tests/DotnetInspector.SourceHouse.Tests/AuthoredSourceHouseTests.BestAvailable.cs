using CSharpText.MemberSlicing;
using DotnetInspector.Libraries;
using ILInspector.Decompiler;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    [Fact]
    public async Task
        BestAvailable_TypeAuthoredSuccessSkipsDecompilation()
    {
        var asset = PartialTypeAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                Path.ChangeExtension(
                    asset.AssemblyPath,
                    ".pdb"));
        LibraryOperationLease operation =
            library.IssueOperation();
        byte[]? selectedBytes = null;

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        asset.Target,
                        asset.AssemblyPath,
                        [
                            Capability(
                                "type-document",
                                SourceHouseCapabilityCategory
                                    .Local,
                                (candidate, maximumBytes, _) =>
                                {
                                    selectedBytes =
                                        ReadCandidateSource(
                                            candidate);
                                    Assert.True(
                                        selectedBytes.Length
                                        <= maximumBytes);
                                    return ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                        new SourceHouseCapabilityOutcome
                                            .Available(
                                                selectedBytes));
                                }),
                        ]),
                    operation,
                    TestContext.Current
                        .CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Authored,
            available.Selected);
        Assert.NotNull(selectedBytes);
        Assert.Equal(
            SourceLinkService.DecodeSourceText(
                selectedBytes),
            available.Text);
        Assert.IsType<SourceHouseOutcome.Available>(
            available.AuthoredOutcome);
        Assert.Null(available.DecompilationOutcome);
        Assert.Equal(
            asset.Target.Type,
            Assert.IsType<SourceHouseTarget.TypeTarget>(
                available.Request.Target).Type);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        BestAvailable_TypeAuthoredUnavailableFallsBackToDecompilation()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();
        var target = new SourceHouseTarget.TypeTarget(
            asset.MemberTarget.Type);

        SourceHouseBestAvailableOutcome.Available available =
            Assert.IsType<
                SourceHouseBestAvailableOutcome.Available>(
                await SourceHouse.ExecuteBestAvailableAsync(
                    BestAvailableRequest(
                        library,
                        target,
                        asset.AssemblyPath,
                        [],
                        authoredPrecondition:
                            SourceHouseBestAvailableAuthoredPrecondition
                                .PortablePdbUnavailable),
                    operation,
                    TestContext.Current
                        .CancellationToken));

        Assert.Equal(
            SourceHouseSelectedSource.Decompiled,
            available.Selected);
        Assert.Contains(
            "MemberTextSlicer",
            available.Text);
        Assert.IsType<SourceHouseOutcome.Unavailable>(
            available.AuthoredOutcome);
        SourceHouseDecompilationOutcome.Completed decompiled =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationOutcome);
        Assert.Equal(
            target.Type,
            Assert.IsType<SourceHouseTarget.TypeTarget>(
                decompiled.Request.Target).Type);
        Assert.Equal(
            SourceHouseBestAvailableAuthoredPrecondition
                .PortablePdbUnavailable,
            available.Request.AuthoredPrecondition);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        BestAvailable_TypeDocumentSelectionIsRejected()
    {
        var asset = PartialTypeAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                Path.ChangeExtension(
                    asset.AssemblyPath,
                    ".pdb"));

        Assert.Throws<ArgumentException>(
            "target",
            () => BestAvailableRequest(
                library,
                new SourceHouseTarget.TypeTarget(
                    asset.Target.Type,
                    asset.Additional.FilePath),
                asset.AssemblyPath,
                []));
    }

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
        => BestAvailableRequest(
            library,
            asset.MemberTarget,
            asset.AssemblyPath,
            capabilities,
            maximumBodyProjections,
            authoredPrecondition);

    private static SourceHouseBestAvailableRequest
        BestAvailableRequest(
            LibraryFixture library,
            SourceHouseTarget target,
            string assemblyPath,
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
            target,
            capabilities);
        SourceHouseDecompilationRequest decompiled =
            DecompilationRequest(
                library,
                target,
                assemblyPath,
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
                target,
            authored.Plan,
            plan,
            authoredPrecondition);
    }
}
