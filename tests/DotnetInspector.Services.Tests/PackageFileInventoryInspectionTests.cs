using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Services.Tests;

public sealed class PackageFileInventoryInspectionTests
{
    private static readonly PackageSourceCoordinate Coordinate =
        PackageSourceCoordinate.Create(
            "Contoso.Inventory",
            "1.0.0");

    [Fact]
    public void RowsBorrowTheExactGenerationAndReturnDetachedOrderedData()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.CreateWithContent(
                            ("lib/net8.0/Z.dll", new byte[4]),
                            ("_rels/.rels", new byte[3]),
                            ("Contoso.Inventory.nuspec", new byte[2]),
                            (".signature.p7s", new byte[5]),
                            ("AGENTS.md", new byte[7]),
                            ("README.md", new byte[6])),
                        fromCache: true,
                        producerKey));
        var rows = RowSelectionIntent<string>.Create(
            [
                RowSelectionIntentOperation<string>.Window(3, 4),
            ]);

        InspectionEnvelope<PackageFileInventoryDocument> envelope =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        rows,
                        QuerySpaceTerminalRequirement.Rows)));

        Assert.Equal(
            PackageFileInventoryStatus.Completed,
            envelope.Content.Status);
        Assert.Equal("Contoso.Inventory", envelope.Content.PackageId);
        Assert.Equal("1.0.0", envelope.Content.Version);
        Assert.Equal(2, envelope.Content.Count);
        Assert.Equal(
            ["README.md", "lib/net8.0/Z.dll"],
            envelope.Content.Files
                .Select(static file => file.Path.ToString()));
        Assert.Equal(
            [6, 4],
            envelope.Content.Files.Select(static file => file.Size));
        Assert.True(envelope.Content.HasAgentDocumentation);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public void CountTerminalAgreesWithRowsSelection()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        TestPackageArchive.Create(
                            "a.txt",
                            "b.txt",
                            "c.txt"),
                        fromCache: true,
                        producerKey));
        var rows = RowSelectionIntent<string>.Create(
            [
                RowSelectionIntentOperation<string>.Head(2),
            ]);

        PackageFileInventoryDocument document =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        rows,
                        QuerySpaceTerminalRequirement.Count)))
                .Content;

        Assert.Equal(PackageFileInventoryStatus.Completed, document.Status);
        Assert.Equal(2, document.Count);
        Assert.Empty(document.Files);
    }

    [Fact]
    public void CountTerminalPullsAndDisposesScannerWithoutSnapshottingRows()
    {
        ScannerOnlyManifestContent? content = null;
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    content = new ScannerOnlyManifestContent(
                        producerKey));
        var rows = RowSelectionIntent<string>.Create(
            [
                RowSelectionIntentOperation<string>.Head(2),
            ]);

        PackageFileInventoryDocument document =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        rows,
                        QuerySpaceTerminalRequirement.Count)))
                .Content;

        Assert.Equal(PackageFileInventoryStatus.Completed, document.Status);
        Assert.Equal("contoso.inventory", document.PackageId);
        Assert.Equal(2, document.Count);
        Assert.Empty(document.Files);
        Assert.True(document.HasAgentDocumentation);
        Assert.Equal(6, content!.MoveNextCalls);
        Assert.True(content.ScannerDisposed);
    }

    [Fact]
    public void RealPackageInventoryUsesArchiveManifestLengths()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "nugetfetch",
            "pclstorage.1.0.2.nupkg");
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new InMemoryPackageContent(
                        File.ReadAllBytes(packagePath),
                        fromCache: true,
                        producerKey));

        PackageFileInventoryDocument document =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        RowSelectionIntent<string>.Empty,
                        QuerySpaceTerminalRequirement.Rows)))
                .Content;

        Assert.Equal(PackageFileInventoryStatus.Completed, document.Status);
        PackageFileInventoryEntry manifest =
            Assert.Single(
                document.Files,
                static file =>
                    file.Path.ToString() == "PCLStorage.nuspec");
        Assert.True(manifest.Size > 0);
        Assert.Contains(
            document.Files,
            static file =>
                file.Path.ToString()
                == "lib/sl5/PCLStorage.dll");
        Assert.Equal(
            document.Files
                .OrderBy(
                    static file => file.Path.ToString(),
                    StringComparer.Ordinal),
            document.Files);
    }

    [Fact]
    public void MissingManifestIsVisibleRatherThanAnEmptySuccess()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new ManifestlessContent(producerKey));

        InspectionEnvelope<PackageFileInventoryDocument> envelope =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        RowSelectionIntent<string>.Empty,
                        QuerySpaceTerminalRequirement.Rows)));

        Assert.Equal(
            PackageFileInventoryStatus.Unavailable,
            envelope.Content.Status);
        Assert.Empty(envelope.Content.Files);
        Assert.Equal(
            "package-file-inventory.manifest-unavailable",
            Assert.Single(envelope.Diagnostics).Code);
    }

    [Fact]
    public void DuplicateManifestPathIsVisibleRatherThanCollapsed()
    {
        PackageHouseSettlement.Acquired settlement =
            CreateSettlement(
                producerKey =>
                    new DuplicateManifestContent(producerKey));

        InspectionEnvelope<PackageFileInventoryDocument> envelope =
            PackageFileInventoryInspection.Execute(
                new(
                    settlement,
                    PackageFileInventoryQuery.CreateRequest(
                        RowSelectionIntent<string>.Empty,
                        QuerySpaceTerminalRequirement.Rows)));

        Assert.Equal(
            PackageFileInventoryStatus.Unavailable,
            envelope.Content.Status);
        Assert.Contains(
            "appears more than once",
            envelope.Content.Detail!.Value.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "package-file-inventory.invalid-manifest",
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

    private class ManifestlessContent(string producerKey)
        : IPackageContent
    {
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => true;
        public string ProducerKey => producerKey;
        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public IEnumerable<string> EnumerateEntries() => [];
    }

    private sealed class DuplicateManifestContent(string producerKey)
        : ManifestlessContent(producerKey), IPackageContentEntryManifest
    {
        public bool TryGetEntryLength(
            string relativePath,
            out long length)
        {
            length = 1;
            return true;
        }

        public PackageContentEntryScanner CreateEntryScanner() =>
            PackageContentEntryScanner.From(
            [
                new("lib/net8.0/Contoso.dll", 1),
                new("lib/net8.0/Contoso.dll", 1),
            ]);
    }

    private sealed class ScannerOnlyManifestContent(string producerKey)
        : ManifestlessContent(producerKey), IPackageContentEntryManifest
    {
        private static readonly PackageContentEntry[] Entries =
        [
            new("_rels/.rels", 1),
            new("contoso.inventory.nuspec", 2),
            new("AGENTS.md", 3),
            new("a.txt", 4),
            new("b.txt", 5),
        ];

        public int MoveNextCalls { get; private set; }

        public bool ScannerDisposed { get; private set; }

        public bool TryGetEntryLength(
            string relativePath,
            out long length)
        {
            PackageContentEntry? entry = Entries.FirstOrDefault(
                entry => entry.Path.Equals(
                    relativePath,
                    StringComparison.OrdinalIgnoreCase));
            length = entry?.Length ?? 0;
            return entry is not null;
        }

        public PackageContentEntryScanner CreateEntryScanner() =>
            new TrackingScanner(this);

        private sealed class TrackingScanner(
            ScannerOnlyManifestContent owner)
            : PackageContentEntryScanner
        {
            private int _index;

            public override bool MoveNext(
                out PackageContentEntry entry)
            {
                owner.MoveNextCalls++;
                if (_index >= Entries.Length)
                {
                    entry = default;
                    return false;
                }

                entry = Entries[_index++];
                return true;
            }

            public override void Dispose() =>
                owner.ScannerDisposed = true;
        }
    }
}
