using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class ExactPackageArchiveSourceTests
{
    [Fact]
    public async Task ExactArchiveSource_AdmitsRealPackageAndServesExactPayload()
    {
        byte[] content = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "pclstorage.1.0.2.nupkg"),
            TestContext.Current.CancellationToken);
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();

        ExactPackageArchiveSourceAdmission.Available available =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Available>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    content,
                    association,
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        using IPackageSourceClient client = available.Client;
        PackageSourceManifest manifest = Succeeded(
            await client.GetManifestAsync(
                available.Coordinate.PackageId,
                available.Coordinate.Version,
                TestContext.Current.CancellationToken));
        PackageSourcePayload payload = Succeeded(
            await client.GetPackageAsync(
                available.Coordinate.PackageId,
                available.Coordinate.Version,
                TestContext.Current.CancellationToken));
        await using (payload.Content)
        {
            var actual = new byte[content.Length];
            await payload.Content.ReadExactlyAsync(
                actual,
                TestContext.Current.CancellationToken);
            Assert.Equal(content, actual);
        }

        Assert.Equal("pclstorage", available.Coordinate.PackageId);
        Assert.Equal("1.0.2", available.Coordinate.Version);
        Assert.Same(association, client.Source.Association);
        Assert.Equal(
            PackageSourceKind.ExactArchive,
            client.Source.TransportKind);
        Assert.StartsWith(
            "nfs-exact-archive-1.",
            client.Source.Producer.Key,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "package archive sha256:",
            client.Source.Producer.Display.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(
            PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.PackagePayload,
            client.Capabilities);
        Assert.Contains(
            "<id>PCLStorage</id>",
            Encoding.UTF8.GetString(manifest.Content.ToArray()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactArchiveSource_IdentityAndSnapshotDependOnlyOnBytes()
    {
        byte[] content = CreatePackageBytes("Snapshot.Package", "1.2.3");
        string expectedKey = "nfs-exact-archive-1."
            + Convert.ToHexString(SHA256.HashData(content))
                .ToLowerInvariant();
        ExactPackageArchiveSourceAdmission.Available first =
            await AdmitAsync(content);
        ExactPackageArchiveSourceAdmission.Available second =
            await AdmitAsync(content);
        using IPackageSourceClient firstClient = first.Client;
        using IPackageSourceClient secondClient = second.Client;
        content.AsSpan().Fill(0);

        PackageSourcePayload firstPayload = Succeeded(
            await firstClient.GetPackageAsync(
                "Snapshot.Package",
                "1.2.3",
                TestContext.Current.CancellationToken));
        PackageSourcePayload secondPayload = Succeeded(
            await secondClient.GetPackageAsync(
                "Snapshot.Package",
                "1.2.3",
                TestContext.Current.CancellationToken));
        await using (firstPayload.Content)
        await using (secondPayload.Content)
        {
            Assert.Equal(
                firstClient.Source.Producer,
                secondClient.Source.Producer);
            Assert.Equal(expectedKey, firstClient.Source.Producer.Key);
            Assert.NotSame(
                firstClient.Source.Association,
                secondClient.Source.Association);
            Assert.Equal(0x50, firstPayload.Content.ReadByte());
            Assert.Equal(0x50, secondPayload.Content.ReadByte());
        }
    }

    [Fact]
    public async Task ExactArchiveSource_ExposesOnlyItsCoordinate()
    {
        ExactPackageArchiveSourceAdmission.Available available =
            await AdmitAsync(
                CreatePackageBytes("Exact.Package", "2.0.0"));
        using IPackageSourceClient client = available.Client;

        PackageSourceFailure search = Failed(
            await client.SearchAsync(
                "Exact",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        PackageSourceFailure versions = Failed(
            await client.GetVersionsAsync(
                "Exact.Package",
                TestContext.Current.CancellationToken));
        PackageSourceFailure symbols = Failed(
            await client.TryGetSymbolsAsync(
                "Exact.Package",
                "2.0.0",
                TestContext.Current.CancellationToken));
        PackageSourceFailure missingManifest = Failed(
            await client.GetManifestAsync(
                "Other.Package",
                "2.0.0",
                TestContext.Current.CancellationToken));
        PackageSourceFailure missingPackage = Failed(
            await client.GetPackageAsync(
                "Exact.Package",
                "3.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Unsupported, search.Kind);
        Assert.Equal(PackageSourceFailureKind.Unsupported, versions.Kind);
        Assert.Equal(PackageSourceFailureKind.Unsupported, symbols.Kind);
        Assert.Equal(
            PackageSourceFailureKind.NotFound,
            missingManifest.Kind);
        Assert.Equal(
            PackageSourceFailureKind.NotFound,
            missingPackage.Kind);
    }

    [Fact]
    public async Task ExactArchiveSource_ReturnsFreshPayloadStreams()
    {
        ExactPackageArchiveSourceAdmission.Available available =
            await AdmitAsync(CreatePackageBytes("Fresh.Package", "1.0.0"));
        using IPackageSourceClient client = available.Client;
        PackageSourcePayload first = Succeeded(
            await client.GetPackageAsync(
                "Fresh.Package",
                "1.0.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload second = Succeeded(
            await client.GetPackageAsync(
                "Fresh.Package",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using (first.Content)
        await using (second.Content)
        {
            Assert.NotSame(first.Content, second.Content);
            Assert.Equal(0x50, first.Content.ReadByte());
            Assert.Equal(0x50, second.Content.ReadByte());
        }
    }

    [Fact]
    public async Task ExactArchiveSource_RejectsInvalidAndOverBoundArchives()
    {
        ExactPackageArchiveSourceAdmission.Rejected empty =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    ReadOnlyMemory<byte>.Empty,
                    PackageSourceAssociation.Create(),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ExactPackageArchiveSourceAdmission.Rejected malformed =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    "not a package"u8.ToArray(),
                    PackageSourceAssociation.Create(),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ExactPackageArchiveSourceAdmission.Rejected noManifest =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    CreatePackageBytes(
                        "Ignored",
                        "1.0.0",
                        includeManifest: false),
                    PackageSourceAssociation.Create(),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        byte[] package = CreatePackageBytes("Bound.Package", "1.0.0");
        ExactPackageArchiveSourceAdmission.Rejected overBound =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    package,
                    PackageSourceAssociation.Create(),
                    new ExactPackageArchiveSourceOptions
                    {
                        MaxPackageBytes = package.Length - 1,
                    },
                    TestContext.Current.CancellationToken));
        ExactPackageArchiveSourceAdmission.Rejected manifestOverBound =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    package,
                    PackageSourceAssociation.Create(),
                    new ExactPackageArchiveSourceOptions
                    {
                        MaxManifestBytes = 16,
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.EmptyArchive,
            empty.Failure.Kind);
        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.InvalidArchive,
            malformed.Failure.Kind);
        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.InvalidArchive,
            noManifest.Failure.Kind);
        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.ResourceBudget,
            overBound.Failure.Kind);
        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.ResourceBudget,
            manifestOverBound.Failure.Kind);
    }

    [Fact]
    public async Task ExactArchiveSource_RejectsDuplicateRootManifests()
    {
        ExactPackageArchiveSourceAdmission.Rejected rejected =
            Assert.IsType<ExactPackageArchiveSourceAdmission.Rejected>(
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    CreatePackageBytes(
                        "Duplicate.Package",
                        "1.0.0",
                        secondManifest: true),
                    PackageSourceAssociation.Create(),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            ExactPackageArchiveSourceFailureKind.InvalidArchive,
            rejected.Failure.Kind);
    }

    [Fact]
    public async Task ExactArchiveSource_ObservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await PackageSourceClientFactory.CreateExactArchiveAsync(
                    CreatePackageBytes("Canceled.Package", "1.0.0"),
                    PackageSourceAssociation.Create(),
                    cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ExactArchiveSource_OperationsObserveSharedDeadline()
    {
        ExactPackageArchiveSourceAdmission.Available available =
            await AdmitAsync(
                CreatePackageBytes("Deadline.Package", "1.0.0"));
        using IPackageSourceClient client = available.Client;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await client.GetManifestAsync(
                    "Deadline.Package",
                    "1.0.0",
                    cancellation.Token,
                    operation));
    }

    private static async Task<ExactPackageArchiveSourceAdmission.Available>
        AdmitAsync(byte[] content) =>
        Assert.IsType<ExactPackageArchiveSourceAdmission.Available>(
            await PackageSourceClientFactory.CreateExactArchiveAsync(
                content,
                PackageSourceAssociation.Create(),
                cancellationToken:
                    TestContext.Current.CancellationToken));

    private static T Succeeded<T>(
        PackageSourceOperationResult<T> result)
        where T : class
    {
        Assert.Null(result.Failure);
        return Assert.IsType<T>(result.Value);
    }

    private static PackageSourceFailure Failed<T>(
        PackageSourceOperationResult<T> result)
        where T : class
    {
        Assert.Null(result.Value);
        return Assert.IsType<PackageSourceFailure>(result.Failure);
    }

    private static byte[] CreatePackageBytes(
        string id,
        string version,
        bool includeManifest = true,
        bool secondManifest = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            if (includeManifest)
            {
                WriteManifest(
                    archive,
                    $"{id}.nuspec",
                    id,
                    version);
            }
            if (secondManifest)
            {
                WriteManifest(
                    archive,
                    "duplicate.nuspec",
                    id,
                    version);
            }

            ZipArchiveEntry content =
                archive.CreateEntry("lib/net11.0/content.dll");
            using Stream output = content.Open();
            output.Write("managed package content"u8);
        }

        return stream.ToArray();
    }

    private static void WriteManifest(
        ZipArchive archive,
        string path,
        string id,
        string version)
    {
        ZipArchiveEntry manifest = archive.CreateEntry(path);
        using var writer = new StreamWriter(
            manifest.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
              </metadata>
            </package>
            """);
    }
}
