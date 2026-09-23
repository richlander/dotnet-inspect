using System.Text;

using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    [Fact]
    public async Task MemberParts_RealRepositoryDocumentRetainsExactTextAndSettlement()
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] bytes = File.ReadAllBytes(asset.SourcePath);
        string original = SourceLinkService.DecodeSourceText(bytes);
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, asset.PdbPath);
        var target = PartsTarget(asset);
        var operation = library.IssueOperation();
        int reads = 0;

        var outcome = Assert.IsType<SourceHouseOutcome.Available>(
            await SourceHouse.ExecuteAuthoredAsync(
                Request(library, target,
                [
                    Capability("source", SourceHouseCapabilityCategory.Repository, (_, _, _) =>
                    {
                        reads++;
                        return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                            new SourceHouseCapabilityOutcome.Available(bytes));
                    }),
                ]),
                operation, TestContext.Current.CancellationToken));

        var document = Assert.IsType<SourceHouseAuthoredMemberDocument>(outcome.Source.MemberDocument);
        Assert.Equal(original, document.Text);
        Assert.Equal(original.Substring(document.Parts.Member.Start, document.Parts.Member.Length),
            outcome.Source.Text);
        Assert.StartsWith("///", outcome.Source.Text);
        Assert.Contains("public static string? ExtractMemberText(", outcome.Source.Text);
        Assert.DoesNotContain("public static class MemberTextSlicer", outcome.Source.Text);
        string declaration = original.Substring(
            document.Parts.Declaration.Start,
            document.Parts.Declaration.Length);
        Assert.StartsWith(
            "public static string? ExtractMemberText(",
            declaration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/// <summary>",
            declaration,
            StringComparison.Ordinal);
        Assert.Equal(SourceChecksumVerification.Exact, outcome.Source.Selected.ChecksumVerification);
        var mapping = Assert.IsType<SourceHouseAuthoredMapping.Member>(outcome.Source.Mapping);
        Assert.Equal(asset.MemberTarget.MetadataToken, mapping.Observation.MetadataToken);
        Assert.Same(target, outcome.Request.Target);
        Assert.Same(outcome.Request, outcome.Receipt.Request);
        Assert.Equal(original.Length, outcome.Work.SourceTextCharactersObserved);
        Assert.Equal(1, reads);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task MemberParts_ChecksumFailureNeverPublishesParts()
    {
        RealAsset asset = MemberSlicingAsset();
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, asset.PdbPath);
        var target = PartsTarget(asset);
        var outcome = await ExecuteAsync(library, Request(library, target,
        [
            Capability("source", SourceHouseCapabilityCategory.Repository, (_, _, _) =>
                ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                    new SourceHouseCapabilityOutcome.Available(
                        Encoding.UTF8.GetBytes("not the verified document")))),
        ]));

        Assert.IsNotType<SourceHouseAuthoredAttempt.Available>(outcome.AuthoredAttempt);
        Assert.Equal(SourceChecksumVerification.Mismatch,
            Assert.Single(outcome.AuthoredAttempt.SourceAttempts).ChecksumVerification);
        Assert.Same(target, outcome.Receipt.Request.Target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MemberParts_SourceBoundsRemainExplicit(bool characterBound)
    {
        RealAsset asset = MemberSlicingAsset();
        byte[] bytes = File.ReadAllBytes(asset.SourcePath);
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, asset.PdbPath);
        var target = PartsTarget(asset);
        var outcome = Assert.IsType<SourceHouseOutcome.Incomplete>(
            await ExecuteAsync(library, Request(library, target,
            [
                Capability("source", SourceHouseCapabilityCategory.Repository, (_, _, _) =>
                    ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                        new SourceHouseCapabilityOutcome.Available(bytes))),
            ],
            limits: characterBound
                ? Limits(maximumSourceTextCharacters: 1)
                : Limits(maximumSourceBytes: 1))));

        Assert.Equal(characterBound
            ? SourceHouseIncompleteBoundary.SourceTextCharacters
            : SourceHouseIncompleteBoundary.SourceBytes, outcome.Boundary);
        Assert.Same(target, outcome.Receipt.Request.Target);
    }

    private static SourceHouseTarget.MemberTarget PartsTarget(RealAsset asset) =>
        new(asset.MemberTarget.Type, asset.MemberTarget.Member, asset.MemberTarget.MetadataToken,
            SourceHouseMemberSourceForm.DocumentParts);
}
