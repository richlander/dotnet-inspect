using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;
using Catalog = DotnetInspect.Web.Interop.Catalog;
using PlatformAdmission = DotnetInspect.Web.BrowserRetainedWorkspaceAdmissionResult<DotnetInspect.Web.BrowserRetainedWorkspacePlatformPresentation>;
using static DotnetInspect.Web.BrowserOrdinaryWorkerJsonBudget;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task CompactPosting_DeliversTwelveMaximumIconsOnDemand()
    {
        string[] packageIds = [.. Enumerable.Range(0, 12)
            .Select(index => $"Retained.Delivery.Json.{index}")];
        string archivePath = Path.Combine(FindRepositoryRoot(), "fixtures",
            "services", "signatures", "system.text.json.9.0.4.nupkg");
        using ZipArchive source = ZipFile.OpenRead(archivePath);
        using var originalIcon = new MemoryStream();
        using (Stream input = source.GetEntry("Icon.png")!.Open())
            await input.CopyToAsync(originalIcon, TestContext.Current.CancellationToken);
        byte[] icon = ExtendPngToIconLimit(originalIcon.ToArray());
        foreach (string id in packageIds)
        {
            await BrowserPackageWorkspace.RegisterGalleryPackageAsync(
                new BrowserPackage(
                    id, "9.0.4", DeriveDeliveryPackage(source, id, icon),
                    fromCache: false,
                    producerKey: NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json")));
        }

        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            string tabs = string.Join(",", packageIds.Select(id =>
                $$"""["{{id}}","9.0.4","net9.0",null]"""));
            string contexts = string.Join(",", Enumerable.Range(0, 12).Select(index => $"[{index}]"));
            string views = string.Join(",", Enumerable.Range(0, 12).Select(index =>
                $$$"""{"t":{{{index}}},"r":{"k":"package"},"u":{"k":"package"}}"""));
            string packet = EncodeInventoryPacket(
                $$$"""{"f":3,"t":[{{{tabs}}}],"g":[{{{contexts}}}],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{{{views}}}]}""");
            string json = await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                "large", "Large icons", "/workspace", packet);
            Catalog.BrowserRetainedWorkspaceActivationResult activated =
                ReadActivation(json);
            Assert.True(activated.Status == "activated", activated.Failure?.Message);
            Assert.NotNull(activated.Posting);
            Assert.Equal(12, activated.Posting.Packages.Length);
            Assert.Equal(12, activated.Posting.Definition.Tabs.Length);
            Assert.Equal(12, activated.Posting.Navigation.Snapshot.Packages.Length);
            Assert.All(activated.Posting.Packages,
                row => Assert.True(row.Summary.TypeCount > 0));
            AssertTransportAdmitted(json);
            using (JsonDocument compact = JsonDocument.Parse(json))
            {
                Assert.All(compact.RootElement.GetProperty("posting")
                    .GetProperty("packages").EnumerateArray(),
                    row => Assert.False(row.TryGetProperty("surface", out _)));
            }

            BrowserRetainedWorkspacePosting resident =
                Assert.IsType<BrowserRetainedWorkspacePosting>(
                    Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active);
            long residentIconCharacters = resident.Packages.Sum(package =>
                (long)Assert.IsType<BrowserPackageIconPayload>(package.Surface.Icon).Base64.Length);
            Assert.Equal(16_777_248, residentIconCharacters);
            Assert.True(residentIconCharacters > MaxOrdinaryWorkerJsonCharacters);

            foreach (Catalog.BrowserRetainedWorkspacePackageInventory row
                in activated.Posting.Packages)
            {
                string detailJson = await Catalog.CatalogExports.AdmitRetainedWorkspacePackage(
                    "large", activated.Posting.RealizationId, row.NavigationId);
                Catalog.BrowserRetainedWorkspacePackageAdmissionResult detail =
                    ReadPackageAdmission(detailJson);
                Assert.Equal("admitted", detail.Status);
                Assert.NotNull(detail.Package);
                Assert.NotNull(detail.Package.Surface.Icon);
                Assert.Equal(icon, Convert.FromBase64String(detail.Package.Surface.Icon.Base64));
                Assert.Equal(row.Summary.TypeCount, detail.Package.TypePage.TotalTypes);
                Assert.Equal(0, detail.Package.TypePage.Offset);
                AssertTransportAdmitted(detailJson);
            }
            Assert.Same(resident, Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active);

            string retryJson = await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                "large", "Large icons", "/workspace", packet);
            Catalog.BrowserRetainedWorkspaceActivationResult retry = ReadActivation(retryJson);
            Assert.Equal("noEffect", retry.Status);
            Assert.NotNull(retry.Posting);
            Assert.Equal(activated.Posting.RealizationId, retry.Posting.RealizationId);
            Assert.Equal(12, retry.Posting.Packages.Length);
            AssertTransportAdmitted(retryJson);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PlatformAdmission_RejectsA_B_AStaleRealization()
    {
        var options = await DetachedInventoryOptionsAsync(includePlatform: true);
        await using var owner = new BrowserRetainedWorkspaceActivationOwner(() => options);
        string packet = EncodeInventoryPacket(
            """
            {"f":3,"t":[[":Platform","10.0.10","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
            """);
        BrowserRetainedWorkspacePosting first = await ActivateAsync(owner, "a", packet);
        Assert.IsType<PlatformAdmission.Admitted>(
            await owner.AdmitPlatformAsync("a", first.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        _ = await ActivateAsync(owner, "b", Packet());
        BrowserRetainedWorkspacePosting replacement = await ActivateAsync(owner, "a", packet);
        Assert.NotEqual(first.RealizationId, replacement.RealizationId);
        Assert.IsType<PlatformAdmission.Superseded>(
            await owner.AdmitPlatformAsync("a", first.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        Assert.IsType<PlatformAdmission.Superseded>(
            await owner.AdmitPlatformAsync("b", replacement.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        Assert.IsType<PlatformAdmission.Unavailable>(
            await owner.AdmitPlatformAsync("a", replacement.RealizationId, "missing",
                TestContext.Current.CancellationToken));
        var admitted = Assert.IsType<PlatformAdmission.Admitted>(
            await owner.AdmitPlatformAsync("a", replacement.RealizationId, "t0",
                TestContext.Current.CancellationToken));
        Assert.Same(Assert.Single(replacement.Platforms), admitted.Presentation);
        Assert.Same(replacement, owner.Active);
        await owner.DisposeAsync();
        Assert.IsType<PlatformAdmission.Superseded>(
            await owner.AdmitPlatformAsync("a", replacement.RealizationId, "t0",
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailDelivery_RejectsOversizedEvidenceWithoutTruncation(bool collectionLimit)
    {
        _ = await OptionsAsync();
        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            Catalog.BrowserRetainedWorkspaceActivationResult activation = ReadActivation(
                await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                    "detail", "Detail", "/workspace", Packet()));
            Assert.NotNull(activation.Posting);
            Catalog.BrowserRetainedWorkspacePackageAdmissionResult original = ReadPackageAdmission(
                await Catalog.CatalogExports.AdmitRetainedWorkspacePackage(
                    "detail", activation.Posting.RealizationId, "t0"));
            Assert.Equal("admitted", original.Status);
            Assert.NotNull(original.Package);
            Catalog.BrowserPackageSurface oversized = collectionLimit
                ? original.Package.Surface with
                {
                    InspectionErrors = [.. Enumerable.Repeat("", MaxOrdinaryWorkerCollectionEntries)],
                }
                : original.Package.Surface with
                {
                    InspectionError = new string('x', MaxOrdinaryWorkerJsonCharacters),
                };
            Catalog.BrowserRetainedWorkspacePackageAdmissionResult input = original with
            {
                Package = original.Package with { Surface = oversized },
            };
            string json = SerializePackageDetail(input);
            Catalog.BrowserRetainedWorkspacePackageAdmissionResult result = ReadPackageAdmission(json);
            Assert.Equal("unavailable", result.Status);
            Assert.Null(result.Package);
            Assert.Contains("exceeds the ordinary Worker transport limit", result.Message);
            AssertTransportAdmitted(json);
            Assert.Equal(activation.Posting.RealizationId,
                Catalog.BrowserRetainedWorkspaceActivationService.Owner.Active?.RealizationId);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task DetailDelivery_ShrinksTypePagesWithoutLosingContinuation()
    {
        _ = await OptionsAsync();
        await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        try
        {
            var activation = ReadActivation(
                await Catalog.CatalogExports.ActivateRetainedWorkspaceDefinition(
                    "pages", "Pages", "/workspace", Packet()));
            Assert.NotNull(activation.Posting);
            var original = ReadPackageAdmission(
                await Catalog.CatalogExports.AdmitRetainedWorkspacePackage(
                    "pages", activation.Posting.RealizationId, "t0"));
            Assert.NotNull(original.Package);
            Catalog.BrowserTypeSurface[] types =
            [
                .. original.Package.Surface.Types.Take(2).Select(
                    type => type with { Signature = new string('x', 9_000_000) }),
            ];
            Assert.Equal(2, types.Length);
            var input = original with
            {
                Package = original.Package with
                {
                    Surface = original.Package.Surface with { Types = types },
                    TypePage = new(10, 12, null),
                },
            };
            string json = SerializePackageDetail(input);
            var first = ReadPackageAdmission(json);
            Assert.Equal("admitted", first.Status);
            Assert.NotNull(first.Package);
            Assert.Equal(new(10, 12, 11), first.Package.TypePage);
            Assert.Equal(types[0].QueryId, Assert.Single(first.Package.Surface.Types).QueryId);
            Assert.Equal(types[0].Signature, first.Package.Surface.Types[0].Signature);
            AssertTransportAdmitted(json);

            var continuation = input with
            {
                Package = input.Package with
                {
                    Surface = input.Package.Surface with { Types = [types[1]] },
                    TypePage = new(11, 12, null),
                },
            };
            json = SerializePackageDetail(continuation);
            var last = ReadPackageAdmission(json);
            Assert.Equal("admitted", last.Status);
            Assert.NotNull(last.Package);
            Assert.Equal(new(11, 12, null), last.Package.TypePage);
            Assert.Equal(types[1].QueryId, Assert.Single(last.Package.Surface.Types).QueryId);
            Assert.Equal(types[1].Signature, last.Package.Surface.Types[0].Signature);
            AssertTransportAdmitted(json);

            var indivisible = continuation with
            {
                Package = continuation.Package with
                {
                    Surface = continuation.Package.Surface with
                    {
                        Types = [types[1] with { Signature = new string('x', MaxOrdinaryWorkerJsonCharacters) }],
                    },
                },
            };
            json = SerializePackageDetail(indivisible);
            var unavailable = ReadPackageAdmission(json);
            Assert.Equal("unavailable", unavailable.Status);
            Assert.Null(unavailable.Package);
            Assert.Contains("smallest Type page", unavailable.Message);
            AssertTransportAdmitted(json);
        }
        finally
        {
            await Catalog.BrowserRetainedWorkspaceActivationService.ResetForTestsAsync();
        }
    }

    static string SerializePackageDetail(Catalog.BrowserRetainedWorkspacePackageAdmissionResult value) =>
        JsonSerializer.Serialize(
            Catalog.BrowserRetainedWorkspaceDetailWireProjection.Admit(value),
            Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePackageAdmissionResult);

    static Catalog.BrowserRetainedWorkspaceActivationResult ReadActivation(string json) =>
        Assert.IsType<Catalog.BrowserRetainedWorkspaceActivationResult>(
            JsonSerializer.Deserialize(json,
                Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspaceActivationResult));

    static Catalog.BrowserRetainedWorkspacePackageAdmissionResult ReadPackageAdmission(string json) =>
        Assert.IsType<Catalog.BrowserRetainedWorkspacePackageAdmissionResult>(
            JsonSerializer.Deserialize(json,
                Catalog.BrowserCatalogJsonContext.Default.BrowserRetainedWorkspacePackageAdmissionResult));

    static void AssertTransportAdmitted(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(JsonStringifyCharacters(document.RootElement)
            + OrdinaryWorkerResultTupleOverhead <= MaxOrdinaryWorkerJsonCharacters);
        Assert.True(CollectionEntries(document.RootElement)
            + OrdinaryWorkerResultTupleOverhead <= MaxOrdinaryWorkerCollectionEntries);
    }

    static byte[] DeriveDeliveryPackage(ZipArchive source, string id, byte[] icon)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                if (entry.FullName == ".signature.p7s")
                    continue;
                bool isManifest = entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal);
                using Stream output = archive.CreateEntry(
                    isManifest ? $"{id}.nuspec" : entry.FullName).Open();
                if (entry.FullName == "Icon.png")
                {
                    output.Write(icon);
                    continue;
                }
                using Stream input = entry.Open();
                if (isManifest)
                {
                    XDocument manifest = XDocument.Load(input);
                    manifest.Descendants().Single(element => element.Name.LocalName == "id").Value = id;
                    manifest.Save(output);
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }
        return buffer.ToArray();
    }

    static byte[] ExtendPngToIconLimit(byte[] original)
    {
        Assert.Equal("IEND"u8.ToArray(), original[^8..^4]);
        int chunkOffset = original.Length - 12;
        int textLength = PackageIconQuery.MaxIconBytes - original.Length - 12;
        byte[] result = new byte[PackageIconQuery.MaxIconBytes];
        original.AsSpan(0, chunkOffset).CopyTo(result);
        Span<byte> chunk = result.AsSpan(chunkOffset, textLength + 12);
        BinaryPrimitives.WriteInt32BigEndian(chunk, textLength);
        "tEXt"u8.CopyTo(chunk[4..]);
        chunk.Slice(8, textLength).Fill((byte)'x');
        "Comment\0"u8.CopyTo(chunk[8..]);
        uint crc = uint.MaxValue;
        foreach (byte value in chunk.Slice(4, textLength + 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk[^4..], ~crc);
        original.AsSpan(chunkOffset).CopyTo(result.AsSpan(result.Length - 12));
        return result;
    }
}
