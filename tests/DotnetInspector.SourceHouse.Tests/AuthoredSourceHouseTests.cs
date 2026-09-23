using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

using CSharpText.MemberSlicing;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.SourceHouse.Tests;

// PR-fast unless individually tagged: bounded settlement over this repository's
// real production assemblies, portable PDBs, and authored source files.
public sealed partial class AuthoredSourceHouseTests
{
    private static readonly ApiSurfaceExtractionBounds s_targetBounds =
        new(
            maxTypes: 2_000,
            maxMembers: 40_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 1_000,
            maxMetadataRows: 500_000,
            maxRetainedTextCharacters: 8_000_000);

    [Fact]
    public async Task
        RealRepositoryMember_OrdersCapabilitiesAndReturnsExactSlice()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        var observedOrder = new List<string>();
        ISourceHouseSourceCapability wrong =
            Capability(
                "local-wrong",
                SourceHouseCapabilityCategory.Local,
                (_, _, _) =>
                {
                    observedOrder.Add("local-wrong");
                    return ValueTask.FromResult<
                        SourceHouseCapabilityOutcome>(
                            new SourceHouseCapabilityOutcome.Available(
                                Encoding.UTF8.GetBytes(
                                    "not the authored source")));
                });
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        ISourceHouseSourceCapability verified =
            Capability(
                "repository-verified",
                SourceHouseCapabilityCategory.Repository,
                (_, maximumBytes, _) =>
                {
                    observedOrder.Add("repository-verified");
                    Assert.True(sourceBytes.Length <= maximumBytes);
                    return ValueTask.FromResult<
                        SourceHouseCapabilityOutcome>(
                            new SourceHouseCapabilityOutcome.Available(
                                sourceBytes));
                });
        ISourceHouseSourceCapability unused =
            Capability(
                "remote-unused",
                SourceHouseCapabilityCategory.Remote,
                (_, _, _) =>
                {
                    observedOrder.Add("remote-unused");
                    return ValueTask.FromResult<
                        SourceHouseCapabilityOutcome>(
                            new SourceHouseCapabilityOutcome.Failed(
                                new("UnexpectedInvocation")));
                });
        LibraryOperationLease operation = library.IssueOperation();

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        [unused, verified, wrong]),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ["local-wrong", "repository-verified"],
            observedOrder);
        Assert.Contains(
            "public static string? ExtractMemberText(",
            available.Source.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static class MemberTextSlicer",
            available.Source.Text,
            StringComparison.Ordinal);
        SourceHouseAuthoredMapping.Member memberMapping =
            Assert.IsType<SourceHouseAuthoredMapping.Member>(
                available.Source.Mapping);
        Assert.Equal(
            SourceHouseSourceUnitScope.ExactMember,
            memberMapping.Scope);
        Assert.Equal(
            asset.MemberTarget.MetadataToken,
            memberMapping.Observation.MetadataToken);
        Assert.Collection(
            available.Source.SourceAttempts,
            first =>
            {
                Assert.Equal(
                    SourceHouseSourceAttemptKind.Rejected,
                    first.Kind);
                Assert.Equal(
                    SourceChecksumVerification.Mismatch,
                    first.ChecksumVerification);
            },
            second =>
            {
                Assert.Equal(
                    SourceHouseSourceAttemptKind.Available,
                    second.Kind);
                Assert.Equal(
                    SourceChecksumVerification.Exact,
                    second.ChecksumVerification);
            });
        Assert.Equal(
            SourceHousePdbContributionKind.SuppliedCompanion,
            available.PdbContribution.Kind);
        Assert.Same(
            library.PortablePdb,
            available.PdbContribution.Content);
        Assert.Same(
            available.Request,
            available.Receipt.Request);
        Assert.Equal(
            SourceHouseSourcePolicy.AuthoredOnly,
            available.Request.SourcePolicy);
        Assert.Equal(
            SourceHousePdbAcquisitionPolicy
                .LibraryCompanionOrEmbeddedOnly,
            available.Request.PdbAcquisitionPolicy);
        Assert.Equal(
            File.ReadAllText(asset.SourcePath).Length,
            available.Work.SourceTextCharactersObserved);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        RealRepositoryPartialType_ReturnsPrimaryAndAdditionalDocuments()
    {
        string assemblyPath =
            typeof(SourceLinkService).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        SourceHouseTarget.TypeTarget target =
            TypeTarget(
                assemblyPath,
                typeof(SourceLinkService).FullName!);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                pdbPath);

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        target,
                        [
                            Capability(
                                "local",
                                SourceHouseCapabilityCategory.Local,
                                (candidate, maximumBytes, _) =>
                                {
                                    byte[] sourceBytes =
                                        ReadCandidateSource(candidate);
                                    Assert.True(
                                        sourceBytes.Length
                                            <= maximumBytes);
                                    return ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes));
                                }),
                        ])));

        SourceHouseAuthoredMapping.Type mapping =
            Assert.IsType<SourceHouseAuthoredMapping.Type>(
                available.Source.Mapping);
        Assert.Equal(
            SourceHouseSourceUnitScope.PrimaryTypeDocument,
            mapping.Scope);
        Assert.Equal(
            SourceHouseMappingStrength.CorrelatedTypeDocument,
            mapping.Strength);
        var primary = Assert.Single(mapping.SourceMapping.Documents,
            document => document.FilePath == mapping.Document.OriginalPath);
        Assert.Equal(
            SourceLinkResolver.SourceResolutionMethod.SourceLink,
            primary.ResolutionMethod);
        Assert.NotNull(primary.GitHubBrowseUrl);
        Assert.NotEmpty(primary.Checksum!);
        Assert.True(mapping.IsPartial);
        Assert.NotEmpty(mapping.AdditionalDocuments);
        Assert.Contains(
            mapping.AdditionalDocuments,
            document => document.OriginalPath.EndsWith(
                "SourceLinkService.SourceContent.cs",
                StringComparison.Ordinal));
        SourceLinkResolver.TypeSourceDocument additional = Assert.Single(
            mapping.SourceMapping.Documents,
            document => document.FilePath.EndsWith(
                "SourceLinkService.SourceContent.cs",
                StringComparison.Ordinal));
        Assert.NotNull(additional.GitHubBrowseUrl);
        Assert.NotEmpty(additional.Checksum!);
        Assert.Contains(
            "public sealed partial class SourceLinkService",
            available.Source.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        CapabilityFailures_AreRetainedAndLaterCandidateCanSucceed()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        string longReason = new('x', 5_000);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "throwing",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    throw new IOException(longReason)),
                            Capability(
                                "typed-failure",
                                SourceHouseCapabilityCategory.Repository,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Failed(
                                                    new(
                                                        "RepositoryUnavailable",
                                                        "native reason")))),
                            Capability(
                                "verified",
                                SourceHouseCapabilityCategory.Remote,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes))),
                        ])));

        Assert.Collection(
            available.Source.SourceAttempts,
            attempt =>
            {
                Assert.Equal(
                    SourceHouseSourceAttemptKind.Failed,
                    attempt.Kind);
                Assert.Equal(
                    "CapabilityThrew",
                    attempt.Observation?.Code);
                Assert.True(
                    attempt.Observation?.DetailWasTruncated);
                Assert.Equal(
                    4_096,
                    attempt.Observation?.Detail?.Length);
            },
            attempt =>
            {
                Assert.Equal(
                    SourceHouseSourceAttemptKind.Failed,
                    attempt.Kind);
                Assert.Equal(
                    "native reason",
                    attempt.Observation?.Detail);
            },
            attempt => Assert.Equal(
                SourceHouseSourceAttemptKind.Available,
                attempt.Kind));
    }

    [Fact]
    public async Task
        ChecksumRejectionExhaustion_IsUnavailableWithAttemptEvidence()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "mismatch",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(
                                                    "not authored source"u8))),
                        ])));

        SourceHouseSourceAttempt rejected = Assert.Single(
            unavailable.AuthoredAttempt.SourceAttempts);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Rejected,
            rejected.Kind);
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            rejected.ChecksumVerification);
        Assert.NotNull(unavailable.AuthoredAttempt.Mapping);
    }

    [Fact]
    public async Task
        UnusableSourceLinkMap_RemoteExhaustionIsFailed()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] malformedPdb =
            MalformSourceLinkMap(
                File.ReadAllBytes(asset.PdbPath));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                malformedPdb);

        SourceHouseOutcome.Failed failed =
            Assert.IsType<SourceHouseOutcome.Failed>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "remote",
                                SourceHouseCapabilityCategory.Remote,
                                (candidate, _, _) =>
                                {
                                    Assert.Null(
                                        candidate.Document.ResolvedUrl);
                                    return ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Unavailable(
                                                    new("NoRemoteUrl")));
                                }),
                        ])));

        Assert.Equal(
            SourceHouseFailureStage.SourceLinkInspection,
            failed.Failure.Stage);
        Assert.Equal(
            "SourceLinkMapUnusable",
            failed.Failure.Code);
        Assert.Equal(
            SourceLinkMapStatus.Unusable,
            failed.PdbContribution.SourceLinkMap?.Map.Status);
        Assert.Contains(
            failed.PdbContribution.Observations,
            observation =>
                observation.Stage
                    == SourceHouseNativeObservationStage.SourceLink
                && observation.Detail.Length > 0);
        SourceHouseSourceAttempt attempt = Assert.Single(
            failed.AuthoredAttempt.SourceAttempts);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Unavailable,
            attempt.Kind);
        Assert.Equal("NoRemoteUrl", attempt.Observation?.Code);
    }

    [Fact]
    public async Task
        UnusableSourceLinkMap_IndependentRepositorySourceCanSucceed()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        byte[] malformedPdb =
            MalformSourceLinkMap(
                File.ReadAllBytes(asset.PdbPath));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                malformedPdb);

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "repository",
                                SourceHouseCapabilityCategory.Repository,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes))),
                        ])));

        Assert.Equal(
            SourceLinkMapStatus.Unusable,
            available.PdbContribution.SourceLinkMap?.Map.Status);
        Assert.Contains(
            available.PdbContribution.Observations,
            observation =>
                observation.Stage
                    == SourceHouseNativeObservationStage.SourceLink);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Available,
            Assert.Single(available.Source.SourceAttempts).Kind);
    }

    [Fact]
    public async Task
        UnusableSourceLinkMap_LocalOnlyAbsenceRemainsUnavailable()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] malformedPdb =
            MalformSourceLinkMap(
                File.ReadAllBytes(asset.PdbPath));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                malformedPdb);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "local",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Unavailable(
                                                    new("NotLocal")))),
                        ])));

        Assert.Equal(
            SourceLinkMapStatus.Unusable,
            unavailable.PdbContribution.SourceLinkMap?.Map.Status);
        Assert.Single(unavailable.AuthoredAttempt.SourceAttempts);
    }

    [Theory]
    [InlineData(SourceHouseCapabilityCategory.Remote, false, true)]
    [InlineData(SourceHouseCapabilityCategory.Repository, true, true)]
    [InlineData(SourceHouseCapabilityCategory.Local, false, true)]
    [InlineData(SourceHouseCapabilityCategory.Remote, false, false)]
    [InlineData(SourceHouseCapabilityCategory.Remote, true, false)]
    public async Task
        PartiallyUsableSourceLinkMap_PreservesDocumentFailure(
            SourceHouseCapabilityCategory category,
            bool sourceAvailable,
            bool selectedDocumentRejected)
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        string map = selectedDocumentRejected
            ? """{"documents":{"*":null,"/unrelated/*":"https://example.com/*"}}"""
            : """{"documents":{"/unrelated/*":null,"*":"https://example.com/*"}}""";
        byte[] suppliedPdb = ReplaceSourceLinkMap(
            File.ReadAllBytes(asset.PdbPath),
            map);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath, suppliedPdb);
        LibraryOperationLease operation = library.IssueOperation();
        SourceHouseOutcome outcome =
            await SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    asset.MemberTarget,
                    [
                        Capability(
                            "source",
                            category,
                            (candidate, _, _) =>
                                ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                                    sourceAvailable
                                        ? new SourceHouseCapabilityOutcome.Available(
                                            sourceBytes)
                                        : new SourceHouseCapabilityOutcome.Unavailable(
                                            new(candidate.Document.ResolvedUrl is null
                                                ? "NoRemoteUrl"
                                                : "NotFound")))),
                    ]),
                operation,
                TestContext.Current.CancellationToken);

        AssertOperationSettled(operation, library.Reference.ApiAssembly);
        Assert.Equal(
            SourceLinkMapStatus.PartiallyUsable,
            outcome.PdbContribution.SourceLinkMap?.Map.Status);
        SourceHouseAuthoredMapping.Member mapping =
            Assert.IsType<SourceHouseAuthoredMapping.Member>(
                outcome.AuthoredAttempt.Mapping);
        Assert.Equal(
            selectedDocumentRejected
                ? SourceDocumentResolutionStatus.Rejected
                : SourceDocumentResolutionStatus.Resolved,
            mapping.Document.ResolutionStatus);
        SourceHouseSourceAttempt attempt =
            Assert.Single(outcome.AuthoredAttempt.SourceAttempts);
        if (sourceAvailable)
        {
            Assert.IsType<SourceHouseOutcome.Available>(outcome);
            Assert.Equal(SourceHouseSourceAttemptKind.Available, attempt.Kind);
        }
        else
        {
            Assert.Equal(SourceHouseSourceAttemptKind.Unavailable, attempt.Kind);
            if (category == SourceHouseCapabilityCategory.Remote
                && selectedDocumentRejected)
            {
                SourceHouseOutcome.Failed failed =
                    Assert.IsType<SourceHouseOutcome.Failed>(outcome);
                Assert.Equal(
                    SourceHouseFailureStage.SourceLinkInspection,
                    failed.Failure.Stage);
                Assert.Equal(
                    "SourceLinkDocumentMappingRejected",
                    failed.Failure.Code);
                Assert.Equal("NoRemoteUrl", attempt.Observation?.Code);
            }
            else
            {
                Assert.IsType<SourceHouseOutcome.Unavailable>(outcome);
            }
        }
    }

    [Fact]
    public async Task MissingPortablePdb_IsUnavailable()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [])));

        Assert.Equal(
            SourceHousePdbContributionKind.Unavailable,
            unavailable.PdbContribution.Kind);
        Assert.Null(unavailable.AuthoredAttempt.Mapping);
    }

    [Fact]
    public async Task EmbeddedPdb_ReturnsSourceAndChargesExpandedBytes()
    {
        RealAsset asset = EmbeddedSourceComparisonAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "embedded-source",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes))),
                        ])));

        Assert.Equal(
            SourceHousePdbContributionKind.Embedded,
            available.PdbContribution.Kind);
        Assert.Null(available.PdbContribution.Content);
        Assert.True(available.PdbContribution.BytesObserved > 0);
        Assert.Equal(
            available.PdbContribution.BytesObserved,
            available.Work.PortablePdbBytesObserved);
        Assert.Contains(
            "public int Value() => 3;",
            available.Source.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        CompilerGeneratedMember_UsesBoundedProjectionAndReturnsExactSlice()
    {
        RealAsset asset = EmbeddedSourceComparisonAsset();
        SourceHouseTarget.MemberTarget target =
            CompilerGeneratedMemberTarget(
                asset.AssemblyPath,
                "SourceDiffFixture.Counter",
                "g__Adjust|");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        target,
                        [
                            Capability(
                                "embedded-source",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes))),
                        ])));

        Assert.Equal(
            SourceHousePdbContributionKind.Embedded,
            available.PdbContribution.Kind);
        Assert.Contains(
            "int Adjust(int input) => input + 2;",
            available.Source.Text,
            StringComparison.Ordinal);
        SourceHouseAuthoredMapping.Member mapping =
            Assert.IsType<SourceHouseAuthoredMapping.Member>(
                available.Source.Mapping);
        Assert.Equal(
            target.MetadataToken,
            mapping.Observation.MetadataToken);
    }

    [Fact]
    public async Task
        EmbeddedPdb_UsesStricterHouseLimitAndChargesDeclaredBytes()
    {
        RealAsset asset = EmbeddedSourceComparisonAsset();
        int embeddedPdbBytes;
        using (SourceLinkService source =
               SourceLinkService.OpenEmbeddedPdbOnly(
                   asset.AssemblyPath))
        {
            embeddedPdbBytes = source.Context.EmbeddedPdbSize;
        }
        Assert.True(embeddedPdbBytes > 1);

        SourceHouseLimits limits = Limits(
            maximumPortablePdbBytes: embeddedPdbBytes - 1,
            maximumEmbeddedPdbBytes: int.MaxValue);
        Assert.Equal(
            embeddedPdbBytes - 1,
            limits.MaximumEmbeddedPdbBytes);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(asset.AssemblyPath);

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [],
                        limits)));

        Assert.Equal(
            SourceHouseIncompleteBoundary.PortablePdbBytes,
            incomplete.Boundary);
        Assert.Equal(
            SourceHousePdbContributionKind.Incomplete,
            incomplete.PdbContribution.Kind);
        Assert.Equal(
            embeddedPdbBytes,
            incomplete.PdbContribution.BytesObserved);
        Assert.Equal(
            embeddedPdbBytes,
            incomplete.Work.PortablePdbBytesObserved);
        Assert.NotEmpty(incomplete.PdbContribution.Observations);
    }

    [Fact]
    public async Task NullRequest_StillSettlesTransferredLease()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = library.IssueOperation();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () =>
                await SourceHouse.ExecuteAuthoredAsync(
                    request: null!,
                    operation,
                    TestContext.Current.CancellationToken));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task MappedSourceWithoutCapability_IsUnavailable()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [])));

        Assert.NotNull(unavailable.AuthoredAttempt.Mapping);
        Assert.Empty(unavailable.AuthoredAttempt.SourceAttempts);
    }

    [Fact]
    public async Task CapabilityFailure_IsFailedAndSettled()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = library.IssueOperation();

        SourceHouseOutcome.Failed failed =
            Assert.IsType<SourceHouseOutcome.Failed>(
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "failing",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Failed(
                                                    new(
                                                        "RepositoryUnavailable")))),
                        ]),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseFailureStage.SourceCapability,
            failed.Failure.Stage);
        Assert.Collection(
            failed.AuthoredAttempt.SourceAttempts,
            attempt => Assert.Equal(
                SourceHouseSourceAttemptKind.Failed,
                attempt.Kind));
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Theory]
    [InlineData("get_Filter")]
    [InlineData("set_Filter")]
    public async Task RealPlatformExplicitAccessor_RecognizesOnePhysicalTarget(string accessor)
    {
        string assemblyPath = typeof(System.Data.DataView).Assembly.Location;
        SourceHouseTarget.MemberTarget target = MemberTarget(
            assemblyPath, typeof(System.Data.DataView).FullName!,
            $"System.ComponentModel.IBindingListView.{accessor}");
        await using LibraryFixture library = await LibraryFixture.CreateAsync(assemblyPath);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(library, Request(library, target, [])));

        Assert.Equal(SourceHousePdbContributionKind.Unavailable, unavailable.PdbContribution.Kind);
        Assert.Empty(unavailable.AuthoredAttempt.SourceAttempts);
    }

    [Fact]
    public async Task ExactInterfaceMemberWithoutSequencePoints_IsUnavailable()
    {
        string assemblyPath =
            typeof(ISourceLinkIndexCache).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        SourceHouseTarget.MemberTarget target =
            MemberTarget(
                assemblyPath,
                typeof(ISourceLinkIndexCache).FullName!,
                nameof(ISourceLinkIndexCache.TryGet));
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                pdbPath);

        SourceHouseOutcome.Unavailable unavailable =
            Assert.IsType<SourceHouseOutcome.Unavailable>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        target,
                        [
                            Capability(
                                "unused",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    throw new InvalidOperationException(
                                        "Mapping absence must short-circuit capabilities.")),
                        ])));

        Assert.Empty(unavailable.AuthoredAttempt.SourceAttempts);
    }

    [Fact]
    public async Task ForeignLease_IsRejectedAndSettled()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = foreign.IssueOperation();

        SourceHouseOutcome.Rejected rejected =
            Assert.IsType<SourceHouseOutcome.Rejected>(
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        selected,
                        asset.MemberTarget,
                        []),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseRejectionKind.LeaseReferenceMismatch,
            rejected.Rejection.Kind);
        AssertOperationSettled(
            operation,
            foreign.Reference.ApiAssembly);
    }

    [Fact]
    public async Task NonAssemblySelection_IsRejected()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        SourceHouseAuthoredRequest request =
            Request(
                library,
                asset.MemberTarget,
                [],
                selectedAssembly: library.PortablePdb!);

        SourceHouseOutcome.Rejected rejected =
            Assert.IsType<SourceHouseOutcome.Rejected>(
                await ExecuteAsync(library, request));

        Assert.Equal(
            SourceHouseRejectionKind.SelectedContentRoleMismatch,
            rejected.Rejection.Kind);
    }

    [Fact]
    public async Task DeclaredAssemblyIdentityMismatch_IsRejected()
    {
        RealAsset asset = MemberSlicingAsset();
        AssemblyReferenceIdentity foreignIdentity =
            ReadAssemblyIdentity(
                typeof(SourceLinkService).Assembly.Location);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath,
                declaredIdentity: foreignIdentity);

        SourceHouseOutcome.Rejected rejected =
            Assert.IsType<SourceHouseOutcome.Rejected>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [])));

        Assert.Equal(
            SourceHouseRejectionKind.AssemblyIdentityMismatch,
            rejected.Rejection.Kind);
    }

    [Fact]
    public async Task MismatchedClaimedPdb_RejectsOwnerCorrespondence()
    {
        RealAsset asset = MemberSlicingAsset();
        string mismatchedPdb = Path.ChangeExtension(
            typeof(SourceLinkService).Assembly.Location,
            ".pdb");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                mismatchedPdb);

        SourceHouseOutcome.Rejected rejected =
            Assert.IsType<SourceHouseOutcome.Rejected>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [])));

        Assert.Equal(
            SourceHouseRejectionKind
                .PortablePdbCorrespondenceMismatch,
            rejected.Rejection.Kind);
        Assert.NotEmpty(rejected.PdbContribution.Observations);
    }

    [Fact]
    public async Task ExactTargetMismatch_IsRejected()
    {
        RealAsset asset = MemberSlicingAsset();
        SourceHouseTarget.MemberTarget wrongTarget =
            MemberTarget(
                asset.AssemblyPath,
                typeof(MemberTextSlicer).FullName!,
                nameof(MemberTextSlicer.ExtractMemberText));
        wrongTarget = new(
            wrongTarget.Type,
            wrongTarget.Member,
            wrongTarget.MetadataToken + 1);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Rejected rejected =
            Assert.IsType<SourceHouseOutcome.Rejected>(
                await ExecuteAsync(
                    library,
                    Request(library, wrongTarget, [])));

        Assert.Equal(
            SourceHouseRejectionKind.TargetMismatch,
            rejected.Rejection.Kind);
    }

    [Fact]
    public async Task
        TargetMissingUnderInspectionFailure_IsFailedNotRejected()
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

        SourceHouseOutcome.Failed failed =
            Assert.IsType<SourceHouseOutcome.Failed>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        new SourceHouseTarget.TypeTarget(targetType),
                        [])));

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

    [Theory]
    [InlineData(BoundaryCase.AssemblyBytes)]
    [InlineData(BoundaryCase.PortablePdbBytes)]
    [InlineData(BoundaryCase.TargetSurface)]
    [InlineData(BoundaryCase.SourceLinkMapBytes)]
    [InlineData(BoundaryCase.SourceLinkMappings)]
    [InlineData(BoundaryCase.Documents)]
    [InlineData(BoundaryCase.TargetMappings)]
    [InlineData(BoundaryCase.CandidateAttempts)]
    [InlineData(BoundaryCase.SourceBytes)]
    [InlineData(BoundaryCase.SourceTextCharacters)]
    [InlineData(BoundaryCase.Deadline)]
    public async Task FiniteBoundary_ReturnsIncomplete(
        BoundaryCase boundaryCase)
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        SourceHouseLimits limits = Limits(
            maximumAssemblyBytes:
                boundaryCase == BoundaryCase.AssemblyBytes
                    ? 1
                    : 64 * 1024 * 1024,
            maximumPortablePdbBytes:
                boundaryCase == BoundaryCase.PortablePdbBytes
                    ? 1
                    : 64 * 1024 * 1024,
            targetBounds:
                boundaryCase == BoundaryCase.TargetSurface
                    ? new ApiSurfaceExtractionBounds(
                        maxTypes: 0,
                        maxMembers: 0,
                        maxInspectionFailures: 0,
                        maxTypeForwarders: 0,
                        maxMetadataRows: 0,
                        maxRetainedTextCharacters: 0)
                    : s_targetBounds,
            maximumSourceLinkMapBytes:
                boundaryCase == BoundaryCase.SourceLinkMapBytes
                    ? 1
                    : 16 * 1024 * 1024,
            maximumSourceLinkMappings:
                boundaryCase == BoundaryCase.SourceLinkMappings
                    ? 0
                    : 100_000,
            maximumDocuments:
                boundaryCase == BoundaryCase.Documents
                    ? 0
                    : 100_000,
            maximumTargetMappings:
                boundaryCase == BoundaryCase.TargetMappings
                    ? 0
                    : 10_000,
            maximumCandidateAttempts:
                boundaryCase == BoundaryCase.CandidateAttempts
                    ? 0
                    : 10,
            maximumSourceBytes:
                boundaryCase == BoundaryCase.SourceBytes
                    ? 1
                    : 16 * 1024 * 1024,
            maximumSourceTextCharacters:
                boundaryCase
                    == BoundaryCase.SourceTextCharacters
                    ? 1
                    : 16 * 1024 * 1024);
        DateTimeOffset deadline =
            boundaryCase == BoundaryCase.Deadline
                ? DateTimeOffset.UtcNow.AddSeconds(-1)
                : DateTimeOffset.UtcNow.AddMinutes(1);
        ISourceHouseSourceCapability source =
            Capability(
                "source",
                SourceHouseCapabilityCategory.Local,
                (_, _, _) =>
                    ValueTask.FromResult<
                        SourceHouseCapabilityOutcome>(
                            new SourceHouseCapabilityOutcome.Available(
                                sourceBytes)));

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [source],
                        limits,
                        deadline: deadline)));

        Assert.Equal(
            boundaryCase switch
            {
                BoundaryCase.AssemblyBytes =>
                    SourceHouseIncompleteBoundary.AssemblyBytes,
                BoundaryCase.PortablePdbBytes =>
                    SourceHouseIncompleteBoundary.PortablePdbBytes,
                BoundaryCase.TargetSurface =>
                    SourceHouseIncompleteBoundary.TargetSurface,
                BoundaryCase.SourceLinkMapBytes =>
                    SourceHouseIncompleteBoundary.SourceLinkMapBytes,
                BoundaryCase.SourceLinkMappings =>
                    SourceHouseIncompleteBoundary.SourceLinkMappings,
                BoundaryCase.Documents =>
                    SourceHouseIncompleteBoundary.Documents,
                BoundaryCase.TargetMappings =>
                    SourceHouseIncompleteBoundary.TargetMappings,
                BoundaryCase.CandidateAttempts =>
                    SourceHouseIncompleteBoundary.CandidateAttempts,
                BoundaryCase.SourceBytes =>
                    SourceHouseIncompleteBoundary.SourceBytes,
                BoundaryCase.SourceTextCharacters =>
                    SourceHouseIncompleteBoundary
                        .SourceTextCharacters,
                BoundaryCase.Deadline =>
                    SourceHouseIncompleteBoundary.Deadline,
                _ => throw new InvalidOperationException(),
            },
            incomplete.Boundary);
    }

    [Fact]
    public async Task
        CandidateAttemptBoundary_PreservesMappingAndEarlierAttempt()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        ISourceHouseSourceCapability notInvoked =
            Capability(
                "not-invoked",
                SourceHouseCapabilityCategory.Repository,
                (_, _, _) =>
                    throw new InvalidOperationException(
                        "Candidate limit must stop before invocation."));

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "unavailable",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Unavailable(
                                                    new("NotPresent")))),
                            notInvoked,
                        ],
                        Limits(maximumCandidateAttempts: 1))));

        Assert.Equal(
            SourceHouseIncompleteBoundary.CandidateAttempts,
            incomplete.Boundary);
        Assert.NotNull(incomplete.AuthoredAttempt.Mapping);
        Assert.Single(incomplete.AuthoredAttempt.SourceAttempts);
        Assert.Equal(1, incomplete.Work.CandidateAttempts);
    }

    [Fact]
    public async Task
        SourceByteBoundary_PreservesRejectedAttemptAndObservedBytes()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] mismatch = "not authored source"u8.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "mismatch",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(mismatch))),
                            Capability(
                                "not-invoked",
                                SourceHouseCapabilityCategory.Repository,
                                (_, _, _) =>
                                    throw new InvalidOperationException(
                                        "Source byte limit must stop before invocation.")),
                        ],
                        Limits(maximumSourceBytes: mismatch.Length))));

        Assert.Equal(
            SourceHouseIncompleteBoundary.SourceBytes,
            incomplete.Boundary);
        Assert.NotNull(incomplete.AuthoredAttempt.Mapping);
        SourceHouseSourceAttempt attempt = Assert.Single(
            incomplete.AuthoredAttempt.SourceAttempts);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Rejected,
            attempt.Kind);
        Assert.Equal(mismatch.Length, incomplete.Work.SourceBytesObserved);
    }

    [Fact]
    public async Task
        DeadlineAfterCapability_RecordsCompletedAttemptAndWork()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        DateTimeOffset deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(750);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        ValueTask<SourceHouseOutcome> execution =
            SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    asset.MemberTarget,
                    [
                        Capability(
                            "late",
                            SourceHouseCapabilityCategory.Local,
                            async (_, _, token) =>
                            {
                                started.SetResult();
                                try
                                {
                                    await release.Task.WaitAsync(token);
                                }
                                catch (OperationCanceledException)
                                    when (token.IsCancellationRequested)
                                {
                                }

                                return new SourceHouseCapabilityOutcome
                                    .Available(sourceBytes);
                            }),
                    ],
                    deadline: deadline),
                library.IssueOperation(),
                TestContext.Current.CancellationToken);
        await started.Task;
        TimeSpan remaining =
            deadline - DateTimeOffset.UtcNow
                + TimeSpan.FromMilliseconds(50);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(
                remaining,
                TestContext.Current.CancellationToken);
        release.SetResult();

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await execution);

        Assert.Equal(
            SourceHouseIncompleteBoundary.Deadline,
            incomplete.Boundary);
        Assert.NotNull(incomplete.AuthoredAttempt.Mapping);
        SourceHouseSourceAttempt attempt = Assert.Single(
            incomplete.AuthoredAttempt.SourceAttempts);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Incomplete,
            attempt.Kind);
        Assert.Equal(sourceBytes.Length, attempt.BytesObserved);
        Assert.Equal(1, incomplete.Work.CandidateAttempts);
        Assert.Equal(
            sourceBytes.Length,
            incomplete.Work.SourceBytesObserved);
    }

    [Fact]
    public async Task
        DeadlineDuringFinalChecksumRejection_IsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        const int sourceLength = 16 * 1024 * 1024;
        var source = new SourceHouseCapabilityOutcome.Available(
            new byte[sourceLength]);

        for (int attemptNumber = 0; attemptNumber < 3; attemptNumber++)
        {
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMilliseconds(500);
            LibraryOperationLease operation = library.IssueOperation();
            SourceHouseOutcome outcome =
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "final-mismatch",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, token) =>
                                {
                                    DateTimeOffset returnAt =
                                        deadline.AddMilliseconds(-1);
                                    TimeSpan sleep =
                                        returnAt - DateTimeOffset.UtcNow
                                            - TimeSpan.FromMilliseconds(50);
                                    if (sleep > TimeSpan.Zero)
                                        Thread.Sleep(sleep);
                                    while (DateTimeOffset.UtcNow < returnAt)
                                        Thread.SpinWait(64);
                                    token.ThrowIfCancellationRequested();
                                    return ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(source);
                                }),
                        ],
                        Limits(maximumSourceBytes: 32 * 1024 * 1024),
                        deadline),
                    operation,
                    TestContext.Current.CancellationToken);
            AssertOperationSettled(
                operation,
                library.Reference.ApiAssembly);

            SourceHouseSourceAttempt? attempt =
                outcome.AuthoredAttempt.SourceAttempts.SingleOrDefault();
            if (attempt?.ChecksumVerification
                != SourceChecksumVerification.Mismatch)
            {
                continue;
            }

            SourceHouseOutcome.Incomplete incomplete =
                Assert.IsType<SourceHouseOutcome.Incomplete>(outcome);
            Assert.Equal(
                SourceHouseIncompleteBoundary.Deadline,
                incomplete.Boundary);
            Assert.Equal(
                SourceHouseSourceAttemptKind.Rejected,
                attempt.Kind);
            Assert.NotNull(incomplete.AuthoredAttempt.Mapping);
            Assert.Equal(1, incomplete.Work.CandidateAttempts);
            Assert.Equal(sourceLength, incomplete.Work.SourceBytesObserved);
            return;
        }

        Assert.Fail(
            "Scheduling expired before source verification in every attempt.");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        DeadlineDuringMappingWithoutCapabilities_IsIncomplete(bool hasMapping)
    {
        string assemblyPath =
            typeof(SourceLinkService).Assembly.Location;
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assemblyPath,
            hasMapping
                ? typeof(SourceLinkService).FullName!
                : typeof(SourceChecksumVerification).FullName!);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assemblyPath,
                Path.ChangeExtension(assemblyPath, ".pdb"));

        double shortestMilliseconds = double.PositiveInfinity;
        for (int warmup = 0; warmup < 5; warmup++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            SourceHouseOutcome.Unavailable unavailable =
                Assert.IsType<SourceHouseOutcome.Unavailable>(
                    await ExecuteAsync(library, Request(library, target, [])));
            shortestMilliseconds = Math.Min(
                shortestMilliseconds,
                stopwatch.Elapsed.TotalMilliseconds);
            Assert.Equal(
                hasMapping,
                unavailable.AuthoredAttempt.Mapping is not null);
            Assert.True(unavailable.Work.DocumentsObserved > 0);
            Assert.Empty(unavailable.AuthoredAttempt.SourceAttempts);
        }

        // Calibrate to native work, not a machine-specific fixed duration.
        // Retained document work distinguishes post-mapping expiry from earlier stops.
        for (int step = 0; step < 96; step++)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(
                shortestMilliseconds * (0.25 + step / 64d));
            LibraryOperationLease operation = library.IssueOperation();
            SourceHouseOutcome outcome =
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(library, target, [], deadline: deadline),
                    operation,
                    TestContext.Current.CancellationToken);
            AssertOperationSettled(operation, library.Reference.ApiAssembly);
            if (outcome is SourceHouseOutcome.Unavailable)
                continue;

            SourceHouseOutcome.Incomplete incomplete =
                Assert.IsType<SourceHouseOutcome.Incomplete>(outcome);
            Assert.Equal(
                SourceHouseIncompleteBoundary.Deadline,
                incomplete.Boundary);
            if (incomplete.Work.DocumentsObserved == 0)
                continue;

            Assert.True(DateTimeOffset.UtcNow >= deadline);
            if (hasMapping)
            {
                Assert.IsType<SourceHouseAuthoredMapping.Type>(
                    incomplete.AuthoredAttempt.Mapping);
                Assert.True(incomplete.Work.TargetMappingsObserved > 0);
            }
            else
            {
                Assert.Null(incomplete.AuthoredAttempt.Mapping);
                Assert.Equal(0, incomplete.Work.TargetMappingsObserved);
            }
            Assert.Equal(
                SourceHousePdbContributionKind.SuppliedCompanion,
                incomplete.PdbContribution.Kind);
            Assert.True(incomplete.Work.DocumentsObserved > 0);
            Assert.Equal(0, incomplete.Work.CandidateAttempts);
            Assert.Equal(0, incomplete.Work.SourceBytesObserved);
            Assert.Equal(0, incomplete.Work.SourceTextCharactersObserved);
            Assert.Empty(incomplete.AuthoredAttempt.SourceAttempts);
            return;
        }

        Assert.Fail(
            "No deadline-limited settlement retained completed native mapping work.");
    }

    [Fact]
    public async Task
        DeadlineDuringCapability_CancelsSuppliedTokenAndReturnsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        using var callerCancellation =
            new CancellationTokenSource(TimeSpan.FromSeconds(3));
        LibraryOperationLease operation = library.IssueOperation();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ValueTask<SourceHouseOutcome> execution =
            SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    asset.MemberTarget,
                    [
                        Capability(
                            "earlier",
                            SourceHouseCapabilityCategory.Local,
                            (_, _, _) =>
                                ValueTask.FromResult<
                                    SourceHouseCapabilityOutcome>(
                                        new SourceHouseCapabilityOutcome
                                            .Unavailable(
                                                new("NotPresent")))),
                        Capability(
                            "cooperative",
                            SourceHouseCapabilityCategory.Repository,
                            async (_, _, token) =>
                            {
                                started.SetResult();
                                await Task.Delay(
                                    Timeout.InfiniteTimeSpan,
                                    token);
                                throw new InvalidOperationException();
                            }),
                    ],
                    deadline:
                        DateTimeOffset.UtcNow
                            .AddMilliseconds(200)),
                operation,
                callerCancellation.Token);
        await started.Task.WaitAsync(callerCancellation.Token);

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await execution);
        stopwatch.Stop();

        Assert.Equal(
            SourceHouseIncompleteBoundary.Deadline,
            incomplete.Boundary);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Deadline settlement took {stopwatch.Elapsed}.");
        Assert.NotNull(incomplete.AuthoredAttempt.Mapping);
        Assert.Equal(
            2,
            incomplete.AuthoredAttempt.SourceAttempts.Count);
        SourceHouseSourceAttempt earlier =
            incomplete.AuthoredAttempt.SourceAttempts[0];
        Assert.Equal(
            SourceHouseSourceAttemptKind.Unavailable,
            earlier.Kind);
        Assert.Equal("NotPresent", earlier.Observation?.Code);
        SourceHouseSourceAttempt attempt =
            incomplete.AuthoredAttempt.SourceAttempts[1];
        Assert.Equal(
            SourceHouseSourceAttemptKind.Incomplete,
            attempt.Kind);
        Assert.Equal(
            "DeadlineExpiredDuringCapability",
            attempt.Observation?.Code);
        Assert.Equal(2, incomplete.Work.CandidateAttempts);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        DeadlineBeyondSingleTimerRange_CanCompleteNormally()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = library.IssueOperation();

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "verified",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(sourceBytes))),
                        ],
                        deadline: DateTimeOffset.UtcNow.AddDays(100)),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "public static string? ExtractMemberText(",
            available.Source.Text,
            StringComparison.Ordinal);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        LateRecognizedCapabilityExceptionAfterDeadline_IsIncomplete()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);

        SourceHouseOutcome.Incomplete incomplete =
            Assert.IsType<SourceHouseOutcome.Incomplete>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "late-io",
                                SourceHouseCapabilityCategory.Local,
                                async (_, _, token) =>
                                {
                                    try
                                    {
                                        await Task.Delay(
                                            Timeout.InfiniteTimeSpan,
                                            token);
                                    }
                                    catch (OperationCanceledException)
                                        when (token.IsCancellationRequested)
                                    {
                                        throw new IOException(
                                            "late transport failure");
                                    }

                                    throw new InvalidOperationException();
                                }),
                        ],
                        deadline:
                            DateTimeOffset.UtcNow
                                .AddMilliseconds(200))));

        Assert.Equal(
            SourceHouseIncompleteBoundary.Deadline,
            incomplete.Boundary);
        SourceHouseSourceAttempt attempt = Assert.Single(
            incomplete.AuthoredAttempt.SourceAttempts);
        Assert.Equal(
            SourceHouseSourceAttemptKind.Incomplete,
            attempt.Kind);
        Assert.Equal(
            "DeadlineExpiredDuringCapability",
            attempt.Observation?.Code);
        Assert.Contains(
            "late transport failure",
            attempt.Observation?.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SnapshotFailure_PreservesNativeDetail()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = library.IssueOperation();
        operation.Dispose();

        SourceHouseOutcome.Failed failed =
            Assert.IsType<SourceHouseOutcome.Failed>(
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        []),
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            SourceHouseFailureStage.AssemblySnapshot,
            failed.Failure.Stage);
        Assert.Contains(
            nameof(ObjectDisposedException),
            failed.Failure.Detail,
            StringComparison.Ordinal);
        SourceHouseNativeObservation observation = Assert.Single(
            failed.PdbContribution.Observations);
        Assert.Equal(
            SourceHouseNativeObservationStage.Assembly,
            observation.Stage);
        Assert.Contains(
            nameof(ObjectDisposedException),
            observation.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedCapabilityException_PropagatesAfterSettlement()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        LibraryOperationLease operation = library.IssueOperation();

        await Assert.ThrowsAsync<ApplicationException>(
            async () =>
                await SourceHouse.ExecuteAuthoredAsync(
                    Request(
                        library,
                        asset.MemberTarget,
                        [
                            Capability(
                                "unexpected",
                                SourceHouseCapabilityCategory.Local,
                                (_, _, _) =>
                                    throw new ApplicationException(
                                        "unexpected capability failure")),
                        ]),
                    operation,
                    TestContext.Current.CancellationToken));

        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        CallerCancellationBeforeDeadline_PreservesTokenAndSettlesLease()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ISourceHouseSourceCapability capability =
            Capability(
                "blocking",
                SourceHouseCapabilityCategory.Local,
                async (_, _, token) =>
                {
                    started.SetResult();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        token);
                    throw new InvalidOperationException();
                });
        using var cancellation = new CancellationTokenSource();
        LibraryOperationLease operation = library.IssueOperation();
        ValueTask<SourceHouseOutcome> execution =
            SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    asset.MemberTarget,
                    [capability],
                    deadline:
                        DateTimeOffset.UtcNow.AddSeconds(5)),
                operation,
                cancellation.Token);
        await started.Task;

        cancellation.Cancel();
        OperationCanceledException canceled =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await execution);

        Assert.Equal(
            cancellation.Token,
            canceled.CancellationToken);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        OwnerAndArtifactRetirement_DrainIssuedOperation()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] sourceBytes = File.ReadAllBytes(asset.SourcePath);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                asset.AssemblyPath,
                asset.PdbPath);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ISourceHouseSourceCapability capability =
            Capability(
                "delayed",
                SourceHouseCapabilityCategory.Local,
                async (_, _, token) =>
                {
                    started.SetResult();
                    await release.Task.WaitAsync(token);
                    return new SourceHouseCapabilityOutcome.Available(
                        sourceBytes);
                });
        LibraryOperationLease operation = library.IssueOperation();
        ValueTask<SourceHouseOutcome> execution =
            SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    asset.MemberTarget,
                    [capability]),
                operation,
                TestContext.Current.CancellationToken);
        await started.Task;

        Task ownerRetirement = library.BeginOwnerRetirement();
        Task artifactRetirement = library.BeginArtifactRetirement();
        Assert.False(ownerRetirement.IsCompleted);
        Assert.False(artifactRetirement.IsCompleted);
        release.SetResult();

        Assert.IsType<SourceHouseOutcome.Available>(
            await execution);
        await ownerRetirement;
        await artifactRetirement;
        Assert.Equal(
            LibraryContentOwnerState.Released,
            library.OwnerState);
    }

    [Fact]
    public void PublicOutcomeClosureRetainsNoLiveAuthority()
    {
        var seen = new HashSet<Type>();
        foreach (Type root in new[]
        {
            typeof(SourceHouseRequestEvidence),
            typeof(SourceHousePdbContribution),
            typeof(SourceHouseAuthoredAttempt),
            typeof(SourceHouseReceipt),
            typeof(SourceHouseOutcome),
        })
        {
            Visit(root);
        }

        Assert.DoesNotContain(typeof(LibraryContentOwner), seen);
        Assert.DoesNotContain(typeof(LibraryOperationLease), seen);
        Assert.DoesNotContain(
            typeof(ISourceHouseSourceCapability),
            seen);
        Assert.DoesNotContain(typeof(Stream), seen);
        Assert.DoesNotContain(typeof(Delegate), seen);
        Assert.DoesNotContain(typeof(SourceHouseOperationPlan), seen);

        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Version))
            {
                return;
            }
            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                    Visit(argument);
                return;
            }

            Assert.False(type.IsByRefLike, type.FullName);
            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(Delegate).IsAssignableFrom(type),
                type.FullName);
            if (type.Assembly == typeof(object).Assembly)
                return;

            foreach (Type nested in type.GetNestedTypes(
                BindingFlags.Public))
            {
                Visit(nested);
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
        }
    }

    private static SourceHouseAuthoredRequest Request(
        LibraryFixture library,
        SourceHouseTarget target,
        IReadOnlyList<ISourceHouseSourceCapability> capabilities,
        SourceHouseLimits? limits = null,
        DateTimeOffset? deadline = null,
        LibraryContentReference? selectedAssembly = null) =>
        new(
            SourceHouseRequestIdentity.Create("test-request"),
            library.Reference,
            selectedAssembly ?? library.Reference.ApiAssembly,
            target,
            new SourceHouseOperationPlan(
                SourceHouseOperationPlanIdentity.Create("test-plan"),
                SourceHousePolicyGeneration.Create("test-policy"),
                limits ?? Limits(),
                deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
                capabilities));

    private static SourceHouseLimits Limits(
        int maximumAssemblyBytes = 64 * 1024 * 1024,
        int maximumPortablePdbBytes = 64 * 1024 * 1024,
        ApiSurfaceExtractionBounds? targetBounds = null,
        int? maximumEmbeddedPdbBytes = null,
        int maximumSourceLinkMapBytes = 16 * 1024 * 1024,
        int maximumSourceLinkMappings = 100_000,
        int maximumDocuments = 100_000,
        int maximumTargetMappings = 10_000,
        int maximumCandidateAttempts = 10,
        int maximumSourceBytes = 16 * 1024 * 1024,
        int maximumSourceTextCharacters = 16 * 1024 * 1024) =>
        new(
            maximumAssemblyBytes,
            maximumPortablePdbBytes,
            targetBounds ?? s_targetBounds,
            new SourceLinkReadLimits(
                maxEmbeddedPdbBytes:
                    maximumEmbeddedPdbBytes
                    ?? maximumPortablePdbBytes,
                maxMapBytes: maximumSourceLinkMapBytes,
                maxMappings: maximumSourceLinkMappings),
            maximumDocuments,
            maximumTargetMappings,
            maximumCandidateAttempts,
            maximumSourceBytes,
            maximumSourceTextCharacters);

    private static async Task<SourceHouseOutcome> ExecuteAsync(
        LibraryFixture library,
        SourceHouseAuthoredRequest request) =>
        await SourceHouse.ExecuteAuthoredAsync(
            request,
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static ISourceHouseSourceCapability Capability(
        string name,
        SourceHouseCapabilityCategory category,
        Func<
            SourceHouseSourceCandidate,
            int,
            CancellationToken,
            ValueTask<SourceHouseCapabilityOutcome>> read) =>
        new DelegateCapability(
            SourceHouseCapabilityIdentity.Create(name),
            category,
            read);

    private static RealAsset MemberSlicingAsset()
    {
        string assemblyPath =
            typeof(MemberTextSlicer).Assembly.Location;
        return new(
            assemblyPath,
            Path.ChangeExtension(assemblyPath, ".pdb"),
            Path.Combine(
                RepositoryRoot(),
                "src",
                "CSharpText.MemberSlicing",
                "MemberTextSlicer.cs"),
            MemberTarget(
                assemblyPath,
                typeof(MemberTextSlicer).FullName!,
                nameof(MemberTextSlicer.ExtractMemberText)));
    }

    private static RealAsset EmbeddedSourceComparisonAsset()
    {
        string assemblyPath = Path.Combine(
            RepositoryRoot(),
            "artifacts",
            "bin",
            "InspectWeb.SourceComparisonFixtures.V2",
            "release",
            "InspectWebSourceComparisonFixture.dll");
        string sourcePath = Path.Combine(
            RepositoryRoot(),
            "fixtures",
            "queries",
            "DotnetInspector.SourceDiff.V2",
            "Counter.cs");
        return new(
            assemblyPath,
            PdbPath: "",
            sourcePath,
            MemberTarget(
                assemblyPath,
                "SourceDiffFixture.Counter",
                "Value"));
    }

    private static SourceHouseTarget.TypeTarget TypeTarget(
        string assemblyPath,
        string typeName)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        ApiType type = Assert.Single(
            session.ApiSurface(includeAll: true).Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        return new(type.DefinitionName!);
    }

    private static byte[] ReadCandidateSource(
        SourceHouseSourceCandidate candidate)
    {
        string originalPath = candidate.Document.OriginalPath;
        if (Path.IsPathRooted(originalPath)
            && File.Exists(originalPath))
        {
            return File.ReadAllBytes(originalPath);
        }

        string normalized = originalPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string marker =
            $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}";
        int src = normalized.LastIndexOf(
            marker,
            StringComparison.Ordinal);
        if (src >= 0)
        {
            string repositoryPath = Path.Combine(
                RepositoryRoot(),
                normalized[(src + 1)..]);
            if (File.Exists(repositoryPath))
                return File.ReadAllBytes(repositoryPath);
        }

        throw new FileNotFoundException(
            "Could not map the authored document to this repository.",
            originalPath);
    }

    private static byte[] MalformSourceLinkMap(byte[] portablePdb)
    {
        byte[] malformed = [.. portablePdb];
        ReadOnlySpan<byte> marker = "{\"documents\":"u8;
        int offset = malformed.AsSpan().IndexOf(marker);
        Assert.True(
            offset >= 0,
            "The real portable PDB did not contain a SourceLink map.");
        malformed[offset] = (byte)'!';
        return malformed;
    }

    private static byte[] ReplaceSourceLinkMap(byte[] portablePdb, string json)
    {
        byte[] result = [.. portablePdb];
        using var provider = MetadataReaderProvider.FromPortablePdbStream(
            new MemoryStream(portablePdb, writable: false));
        MetadataReader reader = provider.GetMetadataReader();
        byte[] original = reader.CustomDebugInformation
            .Select(reader.GetCustomDebugInformation)
            .Where(information =>
                reader.GetGuid(information.Kind)
                    == new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"))
            .Select(information => reader.GetBlobBytes(information.Value))
            .Single();
        byte[] replacement = Encoding.UTF8.GetBytes(json);
        Assert.True(replacement.Length <= original.Length);
        int offset = result.AsSpan().IndexOf(original);
        Assert.True(offset >= 0);
        result.AsSpan(offset, original.Length).Fill((byte)' ');
        replacement.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static byte[] BuildMalformedTargetSurface()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("MalformedTargetSurface.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedTargetSurface"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        StringHandle ns = metadata.GetOrAddString("N");
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            ns,
            metadata.GetOrAddString("Outer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("Inner`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(inner, outer);
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            ns,
            metadata.GetOrAddString("Consumer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddGenericParameter(
            outer,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TOuter"),
            index: 0);
        metadata.AddGenericParameter(
            inner,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("TInner"),
            index: 2);

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                parameterCount: 1,
                returnType => returnType.Void(),
                parameters =>
                {
                    GenericTypeArgumentsEncoder arguments = parameters
                        .AddParameter()
                        .Type()
                        .GenericInstantiation(
                            inner,
                            genericArgumentCount: 2,
                            isValueType: false);
                    arguments.AddArgument().Int32();
                    arguments.AddArgument().String();
                });
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Use"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    private static SourceHouseTarget.MemberTarget MemberTarget(
        string assemblyPath,
        string typeName,
        string methodName)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        ApiType type = Assert.Single(
            session.ApiSurface(includeAll: true).Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                candidate.Name == methodName
                && candidate.MetadataToken is not null);
        return new(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, member),
            member.MetadataToken!.Value);
    }

    private static SourceHouseTarget.MemberTarget
        CompilerGeneratedMemberTarget(
            string assemblyPath,
            string typeName,
            string methodNameFragment)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        ApiSurface surface = Assert.IsType<
                ApiSurfaceExtractionResult.Extracted>(
                session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    s_targetBounds,
                    includeCompilerGenerated: true))
            .Surface;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                candidate.Name.Contains(
                    methodNameFragment,
                    StringComparison.Ordinal)
                && candidate.MetadataToken is not null);
        return new(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, member),
            member.MetadataToken!.Value);
    }

    private static AssemblyReferenceIdentity ReadAssemblyIdentity(
        string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    private static AssemblyReferenceIdentity ReadAssemblyIdentity(
        byte[] assembly)
    {
        using var stream = new MemoryStream(
            assembly,
            writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    private static void AssertOperationSettled(
        LibraryOperationLease operation,
        LibraryContentReference content)
    {
        Assert.Throws<ObjectDisposedException>(
            () => operation.Snapshot(
                content,
                static (_, _) => true));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    public enum BoundaryCase
    {
        AssemblyBytes,
        PortablePdbBytes,
        TargetSurface,
        SourceLinkMapBytes,
        SourceLinkMappings,
        Documents,
        TargetMappings,
        CandidateAttempts,
        SourceBytes,
        SourceTextCharacters,
        Deadline,
    }

    private sealed record RealAsset(
        string AssemblyPath,
        string PdbPath,
        string SourcePath,
        SourceHouseTarget.MemberTarget MemberTarget);

    private sealed class DelegateCapability(
        SourceHouseCapabilityIdentity identity,
        SourceHouseCapabilityCategory category,
        Func<
            SourceHouseSourceCandidate,
            int,
            CancellationToken,
            ValueTask<SourceHouseCapabilityOutcome>> read)
        : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            identity;
        public SourceHouseCapabilityCategory Category { get; } =
            category;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken) =>
            read(candidate, maximumBytes, cancellationToken);
    }

    private sealed class LibraryFixture : IAsyncDisposable
    {
        private readonly ArtifactFixture _artifacts;
        private readonly LibraryContentOwner _owner;
        private Task? _ownerRetirement;
        private Task? _artifactRetirement;

        private LibraryFixture(
            ArtifactFixture artifacts,
            LibraryReference reference,
            LibraryContentOwner owner)
        {
            _artifacts = artifacts;
            Reference = reference;
            _owner = owner;
            PortablePdb = reference.Contents.SingleOrDefault(
                content =>
                    content.HasRole(
                        LibraryContentRole.PortablePdb));
        }

        public LibraryReference Reference { get; }
        public LibraryContentReference? PortablePdb { get; }
        public LibraryContentOwnerState OwnerState => _owner.State;

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                _owner.IssueOperationLease(Reference))
                .Lease;

        public Task BeginOwnerRetirement() =>
            _ownerRetirement ??= _owner.DisposeAsync().AsTask();

        public Task BeginArtifactRetirement() =>
            _artifactRetirement ??=
                _artifacts.BeginRetirement();

        public static async Task<LibraryFixture> CreateAsync(
            string assemblyPath,
            string? pdbPath = null,
            AssemblyReferenceIdentity? declaredIdentity = null)
        {
            byte[] assembly = await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken);
            byte[]? pdb = pdbPath is null
                ? null
                : await File.ReadAllBytesAsync(
                    pdbPath,
                    TestContext.Current.CancellationToken);
            return await CreateAsync(
                assembly,
                pdb,
                declaredIdentity
                    ?? ReadAssemblyIdentity(assemblyPath));
        }

        public static Task<LibraryFixture> CreateAsync(
            byte[] assembly,
            AssemblyReferenceIdentity declaredIdentity) =>
            CreateAsync(
                assembly,
                pdb: null,
                declaredIdentity);

        public static async Task<LibraryFixture> CreateAsync(
            string assemblyPath,
            byte[] portablePdb)
        {
            byte[] assembly = await File.ReadAllBytesAsync(
                assemblyPath,
                TestContext.Current.CancellationToken);
            return await CreateAsync(
                assembly,
                portablePdb,
                ReadAssemblyIdentity(assemblyPath));
        }

        public static async Task<LibraryFixture> CreateAsync(
            byte[] assembly,
            byte[]? pdb,
            AssemblyReferenceIdentity declaredIdentity)
        {
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(
                    pdb is null
                        ? [assembly]
                        : [assembly, pdb]);
            try
            {
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            new ManagedMetadataIdentity.Assembly(
                                declaredIdentity),
                            artifacts[0],
                            new ManagedMetadataIdentity.Assembly(
                                declaredIdentity)),
                        pdb is null
                            ? null
                            :
                            [
                                new LibraryCompanionCorrespondence(
                                    artifacts[1],
                                    LibraryContentRole.PortablePdb,
                                    artifacts[0]),
                            ]);
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
                    artifacts,
                    reference,
                    owner);
            }
            catch
            {
                await artifacts.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await BeginOwnerRetirement();
            }
            finally
            {
                await BeginArtifactRetirement();
            }
        }
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private ArtifactQueryLease? _queryLease;
        private readonly IReadOnlyList<ArtifactContentReference>
            _references;
        private Task? _retirement;

        private ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            _session = session;
            _queryLease = queryLease;
            _references = references;
        }

        public ArtifactContentReference this[int index] =>
            _references[index];

        public ArtifactContentLease[] IssueContentLeases()
        {
            ArtifactQueryLease queryLease =
                _queryLease
                ?? throw new ObjectDisposedException(
                    nameof(ArtifactFixture));
            return _references
                .Select(
                    reference =>
                        _session.IssueContentLease(
                            reference,
                            queryLease))
                .ToArray();
        }

        public static async Task<ArtifactFixture> CreateAsync(
            IReadOnlyList<byte[]> contents)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < contents.Count; index++)
                {
                    int retainedIndex = index;
                    byte[] retainedContent = contents[index];
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance(
                                        $"source-house-{retainedIndex}"),
                                    _ => new MemoryStream(
                                        retainedContent,
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [contribution],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                }

                Assert.IsType<
                    ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease queryLease =
                    session.IssueLease(authorization);
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public Task BeginRetirement()
        {
            ArtifactQueryLease? queryLease =
                Interlocked.Exchange(ref _queryLease, null);
            queryLease?.Dispose();
            return _retirement ??=
                _session.DisposeAsync().AsTask();
        }

        public async ValueTask DisposeAsync() =>
            await BeginRetirement();
    }

    private sealed record Provenance(string Name) :
        IArtifactProvenance;
}
