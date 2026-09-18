using System.Net;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.SourceLink;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    [Fact]
    public async Task TypeSourceInspection_RealRepositoryAuthoredResultIsDetached()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")),
            maxDecompilerBodyProjections: 0);
        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("MemberTextSlicer"),
                host.Context, TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var pdb = Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        Assert.Contains("public static class MemberTextSlicer", pdb.Text);
        Assert.Equal(pdb.Text, house.Source.Text);
        Assert.Equal(PdbTypeSourceOutcome.Complete, pdb.Inspection.Outcome);
        Assert.Equal(PdbTypeSourceUnitScope.PrimaryTypeDocument, pdb.Inspection.Scope);
        Assert.Equal(PdbTypeSourceMappingStrength.CorrelatedTypeDocument, pdb.Inspection.Strength);
        Assert.Equal(SourceChecksumVerification.Exact, pdb.Inspection.ChecksumVerification);
        Assert.IsType<SourceHouseTarget.TypeTarget>(house.Request.Target);
        var provenance = Assert.IsType<AssemblySourcePdbProvenance>(
            house.PdbContribution.Content!.ArtifactReference.Provenance);
        Assert.Same(assembly.Assembly.Registration, provenance.SourceRegistration);
        Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Equal("type-source/share",
            Assert.IsType<InspectionShare.NonProjectable>(inspection.Share).Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeSourceInspection_MissingOrChecksumFailureRetainsSymbolsForFallback(
        bool checksumFailure)
    {
        TestAssembly assembly = checksumFailure
            ? TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1)
            : TestAssembly.Create(File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location));
        using var host = checksumFailure
            ? QueryHost.WithPdb(assembly.PdbPath, "not the checksum-verified source"u8.ToArray())
            : QueryHost.WithUnavailableSource(HttpStatusCode.NotFound);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant,
                assembly.TypeRequest(checksumFailure ? "Counter" : "EmbeddedSourceFixture"),
                host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Decompiled>(available.Source);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Equal(
            checksumFailure
                ? PdbTypeSourceOutcome.ChecksumMismatch
                : PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
            source.PdbAttempt.Outcome);
        if (checksumFailure)
        {
            Assert.IsType<FindingInspection<string>.Failed>(source.PdbAttempt.Lines.Value);
            Assert.IsType<SourceHouseOutcome.Failed>(available.HouseOutcome);
            Assert.Single(host.SymbolRequests, uri => uri.AbsolutePath.EndsWith(".snupkg"));
        }
        else
        {
            Assert.IsType<FindingInspection<string>.Absent>(source.PdbAttempt.Lines.Value);
            Assert.IsType<SourceHouseOutcome.Unavailable>(available.HouseOutcome);
            Assert.Empty(host.SymbolRequests);
        }
        Assert.Single(host.SourceRequests);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("source-bytes")]
    [InlineData("assembly")]
    public async Task TypeSourceInspection_BoundsRetainFailureAndFallback(string boundary)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        SourceHouseLimits defaults = host.Context.TypeSourceLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = boundary == "deadline"
                ? TimeSpan.FromTicks(1)
                : TimeSpan.FromMinutes(5),
            TypeSourceLimits = new(
                boundary == "assembly" ? 1 : defaults.MaximumAssemblyBytes,
                boundary == "assembly" ? 1 : defaults.MaximumPortablePdbBytes,
                defaults.TargetBounds, defaults.SourceLinkReadLimits,
                defaults.MaximumDocuments, defaults.MaximumTargetMappings,
                defaults.MaximumCandidateAttempts,
                boundary == "source-bytes" ? 1 : defaults.MaximumSourceBytes,
                defaults.MaximumSourceTextCharacters),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("Counter"),
                context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Decompiled>(available.Source);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Equal(
            boundary == "deadline"
                ? PdbTypeSourceOutcome.SourceDeadlineExceeded
                : PdbTypeSourceOutcome.SourceLimitExceeded,
            source.PdbAttempt.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(source.PdbAttempt.Lines.Value);
        if (boundary == "assembly")
        {
            Assert.IsType<AssemblyContextLibraryAdapterResult.Incomplete>(
                available.LibraryFailure);
            Assert.Null(available.HouseOutcome);
        }
        else
        {
            Assert.Equal(
                boundary == "deadline"
                    ? SourceHouseIncompleteBoundary.Deadline
                    : SourceHouseIncompleteBoundary.SourceBytes,
                Assert.IsType<SourceHouseOutcome.Incomplete>(
                    available.HouseOutcome).Boundary);
            Assert.Null(available.LibraryFailure);
        }
    }

    [Fact]
    public async Task TypeSourceInspection_TypeBoundsAreIndependentFromMemberAndPair()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            TypeSourceTimeout = TimeSpan.FromTicks(1),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([before.Participant]);

        var type = await TypeSourceInspection.ExecuteAsync(
            group, before.Participant, before.TypeRequest("Counter"),
            context, TestContext.Current.CancellationToken);
        var member = await MemberSourceInspection.ExecuteAsync(
            group, before.Participant, before.MemberRequest("Value", "Counter"),
            context, TestContext.Current.CancellationToken);
        AssemblyMemberSourcePairResult pair = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);

        var typeSource = Assert.IsType<AssemblyTypeSource.Decompiled>(
            Assert.IsType<AssemblyTypeSourceEntry.Available>(type.Content).Source);
        Assert.Equal(PdbTypeSourceOutcome.SourceDeadlineExceeded, typeSource.PdbAttempt.Outcome);
        Assert.IsType<AssemblyMemberSource.Pdb>(
            Assert.IsType<AssemblyMemberSourceEntry.Available>(member.Content).Source);
        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, pair.Status);
    }

    [Fact]
    public async Task TypeSourceInspection_PreservesPrimaryPartialDocumentEvidence()
    {
        string path = typeof(SourceLinkService).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(
            pdbPath,
            File.ReadAllBytes(Path.Combine(
                FindRepositoryRoot(), "src", "ILInspector.SourceLink", "SourceLinkService.cs")));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);

        InspectionEnvelope<AssemblyTypeSourceEntry> inspection =
            await TypeSourceInspection.ExecuteAsync(
                group, assembly.Participant, assembly.TypeRequest("SourceLinkService"),
                host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyTypeSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyTypeSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        var native = Assert.IsType<SourceHouseAuthoredMapping.Type>(house.Source.Mapping);
        SourceLinkResolver.TypeSourceInfo projected =
            Assert.IsType<SourceLinkResolver.TypeSourceInfo>(source.Inspection.Mapping);
        Assert.Equal(PdbTypeSourceUnitScope.PrimaryTypeDocument, source.Inspection.Scope);
        Assert.Equal(
            PdbTypeSourceMappingStrength.CorrelatedTypeDocument,
            source.Inspection.Strength);
        Assert.True(source.Inspection.IsPartial);
        Assert.Same(native.SourceMapping, projected);
        var primary = Assert.Single(projected.Documents,
            document => document.FilePath == native.Document.OriginalPath);
        Assert.Equal(
            SourceLinkResolver.SourceResolutionMethod.SourceLink,
            primary.ResolutionMethod);
        Assert.NotNull(primary.GitHubBrowseUrl);
        Assert.NotEmpty(primary.Checksum!);
        Assert.NotNull(primary.ChecksumAlgorithm);
        Assert.EndsWith("SourceLinkService.cs", source.Inspection.Document!.OriginalPath);
        Assert.Equal(native.Document, source.Inspection.Document);
        Assert.Equal(native.AdditionalDocuments.Count, source.Inspection.AdditionalDocuments.Count);
        Assert.Contains(
            source.Inspection.AdditionalDocuments,
            document => document.OriginalPath.EndsWith(
                "SourceLinkService.SourceContent.cs", StringComparison.Ordinal));
        SourceLinkResolver.TypeSourceDocument additional = Assert.Single(
            projected.Documents,
            document => document.FilePath.EndsWith(
                "SourceLinkService.SourceContent.cs", StringComparison.Ordinal));
        Assert.NotNull(additional.GitHubBrowseUrl);
        Assert.NotEmpty(additional.Checksum!);
        Assert.NotNull(additional.ChecksumAlgorithm);
    }
}
