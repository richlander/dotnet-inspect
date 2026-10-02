using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageFileContentInspectionTests
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
                "README.md",
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.CreateWithContent(
                            ("README.md", expected),
                            ("Contoso.Documents.nuspec", [])),
                        fromCache: true,
                        producerKey));

        InspectionEnvelope<PackageFileContentDocument> envelope =
            await PackageFileContentInspection.ExecuteAsync(
                new(
                    Acquire(settlement, "readme.md"),
                    PackageDocumentContentLimits.MaxDecodedBytes),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageFileContentStatus.Completed,
            envelope.Content.Status);
        Assert.Equal("contoso.documents", envelope.Content.PackageId);
        Assert.Equal("1.0.0", envelope.Content.Version);
        Assert.Equal("README.md", envelope.Content.Path);
        Assert.Equal(expected, envelope.Content.Content.ToArray());
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task EntryAboveDetachedLimitFailsBeforeOpeningContent()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                "README.md",
                producerKey =>
                    new DeclaredPackageContent(
                        "README.md",
                        PackageDocumentContentLimits.MaxDecodedBytes + 1L,
                        producerKey));

        InspectionEnvelope<PackageFileContentDocument> envelope =
            await PackageFileContentInspection.ExecuteAsync(
                new(
                    Acquire(settlement, "README.md"),
                    PackageDocumentContentLimits.MaxDecodedBytes),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            PackageFileContentStatus.Unavailable,
            envelope.Content.Status);
        Assert.Empty(envelope.Content.Content);
        Assert.Equal(
            "package-file-content.length-unavailable",
            Assert.Single(envelope.Diagnostics).Code);
    }

    [Fact]
    public void EntryResolver_CaseVariantDuplicatesAreVisiblyAmbiguous()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                "README.md",
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.CreateWithContent(
                            ("README.md", "first"u8.ToArray()),
                            ("readme.md", "second"u8.ToArray()),
                            ("Contoso.Documents.nuspec", [])),
                        fromCache: true,
                        producerKey));

        PackageFileEntryResolution resolution =
            PackageFileEntryResolver.Resolve(
                settlement,
                "README.md");

        Assert.Equal(
            PackageFileEntryResolutionStatus.Ambiguous,
            resolution.Status);
        Assert.Null(resolution.Entry);
    }

    private static PackageFileAcquisitionResult.Acquired Acquire(
        PackageHouseSettlement.Acquired settlement,
        string path)
    {
        PackageFileEntryResolution resolution =
            PackageFileEntryResolver.Resolve(settlement, path);
        if (resolution.Status != PackageFileEntryResolutionStatus.Resolved)
        {
            throw new InvalidOperationException(
                $"Test package file resolution failed: {resolution.Status}.");
        }
        return new(
            settlement,
            resolution.Entry
                ?? throw new InvalidOperationException(
                    "A resolved test package file entry is required."));
    }

    private static PackageHouseSettlement.Acquired CreateSettlement(
        string path,
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
                PackageHouseOperationProfile.Acquire),
            contentQuery:
                PackageHouseContentQuery.PackageFilesWithFileList([path]));
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
                acquisition,
                fileList:
                    new PackageHouseFileList(
                        Assert
                            .IsAssignableFrom<IPackageContentEntryManifest>(
                                content)
                            .EnumerateEntriesWithLengths())));
        return new PackageHouseSettlement.Acquired(
            result,
            payload,
            sourcePayload,
            selectionUsesOriginalSources: true);
    }

    private sealed class DeclaredPackageContent(
        string path,
        long length,
        string producerKey)
        : IPackageContent, IPackageContentEntryManifest
    {
        public string? RootPath => null;

        public string? NupkgPath => null;

        public bool FromCache => true;

        public string ProducerKey => producerKey;

        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
            out Stream? stream)
        {
            stream = null;
            return false;
        }

        public IEnumerable<string> EnumerateEntries()
        {
            yield return path;
        }

        public bool TryGetEntryLength(
            string relativePath,
            out long entryLength)
        {
            if (relativePath.Equals(
                    path,
                    StringComparison.OrdinalIgnoreCase))
            {
                entryLength = length;
                return true;
            }

            entryLength = 0;
            return false;
        }

        public PackageContentEntryScanner CreateEntryScanner() =>
            new DeclaredEntryScanner(new(path, length));
    }

    private sealed class DeclaredEntryScanner(
        PackageContentEntry entry)
        : PackageContentEntryScanner
    {
        private bool _read;

        public override bool MoveNext(out PackageContentEntry next)
        {
            if (_read)
            {
                next = default;
                return false;
            }

            _read = true;
            next = entry;
            return true;
        }

        public override void Dispose()
        {
        }
    }
}
