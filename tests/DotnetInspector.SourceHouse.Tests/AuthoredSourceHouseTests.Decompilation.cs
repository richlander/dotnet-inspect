using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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

        SourceHouseDecompilationOutcome.Completed first =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed second =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Incomplete>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Rejected>(
                await SourceHouse.ExecuteDecompilationAsync(
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

        SourceHouseDecompilationOutcome.Failed failed =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Failed>(
                await SourceHouse.ExecuteDecompilationAsync(
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
                await SourceHouse.ExecuteDecompilationAsync(
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

    // PR-fast: bounded exact-type cases over local test and source fixtures.
    [Fact]
    public async Task
        TypeDecompilation_ExactNestedGenericTypePreservesIdentityAndNativeAttempt()
    {
        string assemblyPath =
            typeof(DecompilationFixture.Outer<>.Inner<>).Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.Outer<>.Inner<>).FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(target.Type, completed.Request.Target.Type);
        Assert.Equal(
            SourceHousePdbContributionKind.Unavailable,
            completed.PdbContribution.Kind);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
        Assert.False(completed.Attempt.PdbSupplied);
        Assert.Contains(
            "Inner<",
            completed.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value()",
            completed.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            completed.Attempt.BodyProjections,
            projection => projection.ContributesToOutput);
        Assert.Equal(
            completed.Attempt.BodyProjectionsAttempted,
            completed.Work.BodyProjectionsAttempted);
    }

    [Fact]
    public async Task
        TypeDecompilation_ExactTypeIncludesNonPublicMembers()
    {
        string assemblyPath =
            typeof(DecompilationFixture.SurfaceSelection).Assembly.Location;
        SourceHouseTarget.TypeTarget target =
            TypeTarget(
                assemblyPath,
                typeof(DecompilationFixture.SurfaceSelection)
                    .FullName!
                    .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
        Assert.Contains(
            "public int Included()",
            completed.Attempt.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static int ConcealedCore()",
            completed.Attempt.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        TypeDecompilation_SuppliedOrEmbeddedPdbContributesNativeSymbols(
            bool embedded)
    {
        RealAsset asset = embedded
            ? EmbeddedSourceComparisonAsset()
            : MemberSlicingAsset();
        await using LibraryFixture library = embedded
            ? await LibraryFixture.CreateAsync(asset.AssemblyPath)
            : await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        new SourceHouseTarget.TypeTarget(
                            asset.MemberTarget.Type),
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            embedded
                ? SourceHousePdbContributionKind.Embedded
                : SourceHousePdbContributionKind.SuppliedCompanion,
            completed.PdbContribution.Kind);
        Assert.True(completed.Attempt.PdbSupplied);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            completed.Attempt.Status);
    }

    [Fact]
    public async Task
        TypeDecompilation_FiniteBodyProjectionLimitIsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        new SourceHouseTarget.TypeTarget(
                            asset.MemberTarget.Type),
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
        TypeDecompilation_ExplicitAuthoredDocumentIsNotADecompilationTarget()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        Assert.Throws<ArgumentException>(
            "target",
            () => DecompilationRequest(
                library,
                new SourceHouseTarget.TypeTarget(
                    asset.MemberTarget.Type,
                    "/source/MemberTextSlicer.cs"),
                asset.AssemblyPath));
    }

    [Fact]
    public async Task
        TypeDecompilation_MissingExactTypeIsRejected()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        MetadataTypeDefinitionName missing = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "CSharpText.MemberSlicing",
                    ["MissingType"]))
            .Name;

        SourceHouseDecompilationOutcome.Rejected rejected =
            Assert.IsType<SourceHouseDecompilationOutcome.Rejected>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        new SourceHouseTarget.TypeTarget(missing),
                        asset.AssemblyPath),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseRejectionKind.TargetMismatch,
            rejected.Rejection.Kind);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_NoPdbPreservesNativeOutcomeAndLease()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    operation,
                    TestContext.Current.CancellationToken));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
        Assert.Equal(
            SourceHouseDecompilationProduct.StructuredTypeDocument,
            completed.Request.Product);
        Assert.IsType<
            SourceHouseDecompilationContent.StructuredTypeDocument>(
                completed.Content);
        CSharpTypeDocument document =
            Assert.IsType<CSharpTypeDocumentOutcome.Available>(
                completed.TypeDocument)
                .Document;
        Assert.Equal(target.Type, document.TypeName);
        Assert.False(document.Source.PdbSupplied);
        Assert.Equal(
            SourceHousePdbContributionKind.Unavailable,
            completed.PdbContribution.Kind);
        Assert.Equal(
            completed.TypeDocument.BodyProjectionsAttempted,
            completed.Work.BodyProjectionsAttempted);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            completed.LeaseSettlement.Consumer);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_CompanionPdbPreservesNativeProvenance()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                pdbPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        CSharpTypeDocument document =
            Assert.IsType<CSharpTypeDocumentOutcome.Available>(
                completed.TypeDocument)
                .Document;
        Assert.True(document.Source.PdbSupplied);
        Assert.Equal(
            SourceHousePdbContributionKind.SuppliedCompanion,
            completed.PdbContribution.Kind);
        Assert.Same(
            library.PortablePdb,
            completed.PdbContribution.Content);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_CompanionLimitRetainsPdbFreeDocument()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                pdbPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        limits: DecompilationLimits(
                            maximumPortablePdbBytes: 1),
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        CSharpTypeDocument document =
            Assert.IsType<CSharpTypeDocumentOutcome.Available>(
                completed.TypeDocument)
                .Document;
        Assert.False(document.Source.PdbSupplied);
        Assert.Equal(
            SourceHousePdbContributionKind.Incomplete,
            completed.PdbContribution.Kind);
        Assert.True(completed.Work.PortablePdbBytesObserved > 1);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_ZeroBudgetPreservesNativeIncomplete()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        maximumBodyProjections: 0,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        var incomplete =
            Assert.IsType<CSharpTypeDocumentOutcome.Incomplete>(
                completed.TypeDocument);
        Assert.Equal(0, incomplete.BodyProjectionsAttempted);
        Assert.Equal(0, completed.Work.BodyProjectionsAttempted);
        Assert.Contains(
            incomplete.Document.Bodies,
            body =>
                body.Diagnostics.Any(diagnostic =>
                    diagnostic.Id
                        == DiagnosticIds.CompositionBudgetExceeded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        TypeDocumentDecompilation_InvalidPdbPreservesNativeTerminalOutcome(
            bool empty)
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                empty ? [] : [1, 2, 3, 4]);

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        if (empty)
        {
            Assert.IsType<CSharpTypeDocumentOutcome.Rejected>(
                completed.TypeDocument);
        }
        else
        {
            Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
                completed.TypeDocument);
        }
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_LateMalformedBodyPreservesAttemptedWork()
    {
        string assemblyPath =
            typeof(DecompilationFixture.SurfaceSelection)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.SurfaceSelection)
                .FullName!
                .Replace('+', '.'));
        byte[] assembly = MalformSecondManagedBody(
            assemblyPath,
            nameof(DecompilationFixture.SurfaceSelection));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                ReadAssemblyIdentity(assembly));

        SourceHouseDecompilationOutcome.Completed completed =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        maximumBodyProjections: 1,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        var unavailable =
            Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
                completed.TypeDocument);
        Assert.Contains(
            nameof(BadImageFormatException),
            unavailable.Reason,
            StringComparison.Ordinal);
        Assert.Equal(1, unavailable.BodyProjectionsAttempted);
        Assert.Equal(1, completed.Work.BodyProjectionsAttempted);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_AssemblyLimitIsIncompleteAndSettlesLease()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();

        SourceHouseDecompilationOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseDecompilationOutcome.Incomplete>(
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        limits: DecompilationLimits(
                            maximumAssemblyBytes: 1),
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    operation,
                    TestContext.Current.CancellationToken));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
        Assert.Equal(
            SourceHouseDecompilationProduct.StructuredTypeDocument,
            incomplete.Request.Product);
        Assert.Equal(
            SourceHouseIncompleteBoundary.AssemblyBytes,
            incomplete.Boundary);
        Assert.True(incomplete.Work.AssemblyBytesObserved > 1);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_CancellationSettlesOperationLease()
    {
        string assemblyPath =
            typeof(DecompilationFixture.DocumentInterface)
                .Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            typeof(DecompilationFixture.DocumentInterface)
                .FullName!
                .Replace('+', '.'));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assemblyPath);
        LibraryOperationLease operation =
            library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await SourceHouse.ExecuteDecompilationAsync(
                    DecompilationRequest(
                        library,
                        target,
                        assemblyPath,
                        product: SourceHouseDecompilationProduct
                            .StructuredTypeDocument),
                    operation,
                    cancellation.Token));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        TypeDocumentDecompilation_RequiresExactTypeTarget()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        Assert.Throws<ArgumentException>(
            "target",
            () => DecompilationRequest(
                library,
                asset.MemberTarget,
                asset.AssemblyPath,
                product: SourceHouseDecompilationProduct
                    .StructuredTypeDocument));
    }

    private static SourceHouseDecompilationRequest
        DecompilationRequest(
            LibraryFixture library,
            SourceHouseTarget target,
            string assemblyPath,
            SourceHouseDecompilationLimits? limits = null,
            int maximumBodyProjections =
                CSharpDecompilerService.DefaultMaxBodyProjections,
            SourceHouseDecompilationProduct product =
                SourceHouseDecompilationProduct.SourceText) =>
        new(
            SourceHouseRequestIdentity.Create(
                "test-decompilation"),
            library.Reference,
            library.Reference.ApiAssembly,
            target,
            new(
                SourceHouseOperationPlanIdentity.Create(
                    "test-decompilation-plan"),
                SourceHousePolicyGeneration.Create(
                    "test-decompilation-policy"),
                limits ?? DecompilationLimits(),
                new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        assemblyPath)),
                maximumBodyProjections:
                    maximumBodyProjections),
            product);

    private static SourceHouseDecompilationLimits
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

    private static byte[] MalformSecondManagedBody(
        string assemblyPath,
        string typeName)
    {
        byte[] assembly = File.ReadAllBytes(assemblyPath);
        int bodyOffset;
        using (var stream =
            new MemoryStream(assembly, writable: false))
        using (var pe = new PEReader(stream))
        {
            MetadataReader reader = pe.GetMetadataReader();
            TypeDefinitionHandle typeHandle = Assert.Single(
                reader.TypeDefinitions,
                handle =>
                    reader.GetString(
                        reader.GetTypeDefinition(handle).Name)
                    == typeName);
            MethodDefinitionHandle[] managedBodies =
            [
                .. reader.GetTypeDefinition(typeHandle)
                    .GetMethods()
                    .Where(handle =>
                        reader.GetMethodDefinition(handle)
                            .RelativeVirtualAddress != 0),
            ];
            Assert.True(managedBodies.Length >= 2);
            int bodyRva = reader
                .GetMethodDefinition(managedBodies[1])
                .RelativeVirtualAddress;
            SectionHeader section = Assert.Single(
                pe.PEHeaders.SectionHeaders,
                candidate =>
                    bodyRva >= candidate.VirtualAddress
                    && bodyRva
                        < candidate.VirtualAddress
                            + Math.Max(
                                candidate.VirtualSize,
                                candidate.SizeOfRawData));
            bodyOffset =
                bodyRva
                - section.VirtualAddress
                + section.PointerToRawData;
        }

        assembly[bodyOffset] = 0;
        return assembly;
    }

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

    private static class DecompilationFixture
    {
        public sealed class Outer<TOuter>
        {
            public sealed class Inner<TInner>
            {
                public int Value() => 42;
            }
        }

        public sealed class SurfaceSelection
        {
            public int Included() => ConcealedCore();

            private static int ConcealedCore() => 1;
        }

        public interface DocumentInterface
        {
            int Read() => 7;
        }
    }
}
