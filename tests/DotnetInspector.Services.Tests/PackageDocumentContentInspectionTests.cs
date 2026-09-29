using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageDocumentContentInspectionTests
{
    private static readonly PackageSourceCoordinate Coordinate =
        PackageSourceCoordinate.Create(
            "Contoso.Documents",
            "1.0.0");

    [Fact]
    public async Task ExactEntryReturnsDetachedBytesAndActualPath()
    {
        byte[] expected = "# Package\n"u8.ToArray();
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.CreateWithContent(
                            ("README.md", expected),
                            ("Contoso.Documents.nuspec", [])),
                        fromCache: true,
                        producerKey));

        InspectionEnvelope<PackageDocumentContentDocument> envelope =
            await PackageDocumentContentInspection.ExecuteAsync(
                new(settlement, "readme.md"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageDocumentContentStatus.Completed,
            envelope.Content.Status);
        Assert.Equal("contoso.documents", envelope.Content.PackageId);
        Assert.Equal("1.0.0", envelope.Content.Version);
        Assert.Equal("README.md", envelope.Content.Path);
        Assert.Equal(expected, envelope.Content.Content.ToArray());
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task MissingEntryIsVisibleRatherThanEmptySuccess()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.Create(
                            "Contoso.Documents.nuspec"),
                        fromCache: true,
                        producerKey));

        InspectionEnvelope<PackageDocumentContentDocument> envelope =
            await PackageDocumentContentInspection.ExecuteAsync(
                new(settlement, "README.md"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageDocumentContentStatus.Unavailable,
            envelope.Content.Status);
        Assert.Empty(envelope.Content.Content);
        Assert.Contains(
            "does not contain",
            envelope.Content.Detail!.Value.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "package-document-content.entry-missing",
            Assert.Single(envelope.Diagnostics).Code);
    }

    [Fact]
    public async Task CaseVariantDuplicatesAreVisiblyAmbiguous()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.CreateWithContent(
                            ("README.md", "first"u8.ToArray()),
                            ("readme.md", "second"u8.ToArray()),
                            ("Contoso.Documents.nuspec", [])),
                        fromCache: true,
                        producerKey));

        InspectionEnvelope<PackageDocumentContentDocument> envelope =
            await PackageDocumentContentInspection.ExecuteAsync(
                new(settlement, "README.md"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageDocumentContentStatus.Unavailable,
            envelope.Content.Status);
        Assert.Empty(envelope.Content.Content);
        Assert.Equal(
            "package-document-content.entry-ambiguous",
            Assert.Single(envelope.Diagnostics).Code);
    }

    private static PackageHouseSettlement.Acquired CreateSettlement(
        Func<string, IPackageContent> createContent)
    {
        var authority =
            new ConfiguredPackageAuthority(PackageSource.NuGetOrg);
        PackageSourceResultIdentity source;
        using (IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association))
        {
            source = client.Source;
        }

        IPackageContent content = createContent(source.Producer.Key);
        PackageAcquisitionCandidate candidate =
            PackageAcquisitionCandidate.CreatePinned(
                new object(),
                Coordinate,
                [authority]);
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(candidate),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire));
        PackageHouseDecisionReceipt decision =
            PackageHouseDecisionReceipt.RetainPackage(
                request,
                Coordinate,
                candidate);
        var initialPayload = new AcquiredPackageSourcePayload(
            Coordinate,
            content,
            source.Producer.Key,
            source.Producer,
            PackagePayloadOrigin.Cache);
        var sourcePayload = new ConfiguredPackagePayloadResult(
            authority,
            source,
            initialPayload,
            failures: [],
            reportingAuthorities: [authority],
            selectionUsesOriginalSources: true,
            transfer: PackageTransferReceipt.Cache);
        AcquiredPackageSourcePayload payload =
            Assert.IsType<AcquiredPackageSourcePayload>(
                sourcePayload.Payload);
        var acquisition = new PackageHouseAcquisitionReceipt(
            decision,
            authority,
            source,
            payload.Origin,
            payload.Content.GenerationIdentity,
            PackageTransferReceipt.Cache);
        var result = new PackageHouseResult.Settled(
            new PackageHouseEvidence(
                request,
                decision,
                acquisition));
        return new PackageHouseSettlement.Acquired(
            result,
            payload,
            sourcePayload,
            selectionUsesOriginalSources: true);
    }
}
