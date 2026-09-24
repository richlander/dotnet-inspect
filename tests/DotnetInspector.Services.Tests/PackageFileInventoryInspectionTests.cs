using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
                            ("README.md", new byte[6])),
                        fromCache: true,
                        producerKey));
        var rows = RowSelectionIntent<string>.Create(
            [
                RowSelectionIntentOperation<string>.Window(2, 3),
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
        Assert.Equal("contoso.inventory", envelope.Content.PackageId);
        Assert.Equal("1.0.0", envelope.Content.Version);
        Assert.Equal(2, envelope.Content.Count);
        Assert.Equal(
            ["README.md", "lib/net8.0/Z.dll"],
            envelope.Content.Files
                .Select(static file => file.Path.ToString()));
        Assert.Equal(
            [6, 4],
            envelope.Content.Files.Select(static file => file.Size));
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);

        string json = JsonSerializer.Serialize(
            envelope,
            PackageFileInventoryJsonContext.Default
                .InspectionEnvelopePackageFileInventoryDocument);
        using JsonDocument serialized = JsonDocument.Parse(json);
        Assert.Equal(
            "Rows",
            serialized.RootElement
                .GetProperty("content")
                .GetProperty("terminal")
                .GetString());
        Assert.Equal(
            "README.md",
            serialized.RootElement
                .GetProperty("content")
                .GetProperty("files")[0]
                .GetProperty("path")
                .GetString());
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

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            [
                new("lib/net8.0/Contoso.dll", 1),
                new("lib/net8.0/Contoso.dll", 1),
            ];
    }
}
