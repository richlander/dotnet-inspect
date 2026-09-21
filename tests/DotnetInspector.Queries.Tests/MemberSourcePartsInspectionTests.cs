using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // PR-fast: one real repository declaration or one bounded fixture per case.
    [Fact]
    public async Task MemberSourceParts_RealRepositoryEnvelopePreservesDocumentAndNativeParts()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        byte[] sourceBytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs"));
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, sourceBytes, maxDecompilerBodyProjections: 0);
        var request = assembly.MemberRequest("ExtractMemberText", "MemberTextSlicer").WithAuthoredParts();
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await MemberSourceInspection.ExecuteAsync(
                group, assembly.Participant, request, host.Context,
                TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(inspection.Content);
        var source = Assert.IsType<AssemblyMemberSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        var document = Assert.IsType<SourceHouseAuthoredMemberDocument>(source.MemberDocument);
        Assert.Same(house.Source.MemberDocument, document);
        Assert.Equal(SourceLinkService.DecodeSourceText(sourceBytes), document.Text);
        Assert.Equal(document.Text.Substring(document.Parts.Member.Start, document.Parts.Member.Length),
            source.Text);
        Assert.StartsWith("///", source.Text);
        Assert.Contains("public static string? ExtractMemberText(", source.Text);
        Assert.Equal(SourceChecksumVerification.Exact, source.Inspection.ChecksumVerification);
        Assert.Equal(SourceHouseMemberSourceForm.DocumentParts,
            Assert.IsType<SourceHouseTarget.MemberTarget>(house.Request.Target).SourceForm);
        Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
        Assert.Same(request, available.Request);
        Assert.Equal("member-source", inspection.ResourcePath.Value);
        Assert.Empty(inspection.Diagnostics);
        Assert.Single(host.SourceRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberSourceParts_ChecksumFailureHonorsExplicitFallbackPolicy(bool allowFallback)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(assembly.PdbPath, "different source"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);
        var request = assembly.MemberRequest("Value", "Counter").WithAuthoredParts(allowFallback);

        var inspection = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant, request, host.Context, TestContext.Current.CancellationToken);

        if (allowFallback)
        {
            var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(inspection.Content);
            var decompiled = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
            Assert.True(decompiled.Decompilation.PdbSupplied);
            Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, decompiled.PdbAttempt.Outcome);
        }
        else
        {
            var unavailable = Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(inspection.Content);
            Assert.Equal(AssemblySourceFailureKind.AuthoredMemberPartsUnavailable, unavailable.Failure.Kind);
            Assert.Null(unavailable.DecompiledAttempt);
            Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, unavailable.PdbAttempt!.Outcome);
        }
        Assert.IsType<SourceHouseOutcome.Failed>(inspection.Content.HouseOutcome);
        Assert.Single(host.SymbolRequests, uri => uri.AbsolutePath.EndsWith(".snupkg"));
        Assert.Single(host.SourceRequests);
    }

    [Fact]
    public async Task MemberSourceParts_MissingPdbDoesNotSubstituteDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var inspection = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant,
            assembly.MemberRequest("Value", "Counter").WithAuthoredParts(),
            host.Context, TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(inspection.Content);
        Assert.Equal(AssemblySourceFailureKind.AuthoredMemberPartsUnavailable, unavailable.Failure.Kind);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.PdbAttempt!.Text);
        Assert.Empty(host.SourceRequests);
    }
}
