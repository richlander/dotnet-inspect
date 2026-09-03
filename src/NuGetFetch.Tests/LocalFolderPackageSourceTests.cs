using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class LocalFolderPackageSourceTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("local-feed").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task LocalFolderSource_ConsumesCanonicalIdentityWithoutReparsing()
    {
        LocalPackageSourceIdentity path =
            LocalPackageSourceIdentity.Create(_root, _root);
        LocalPackageSourceIdentity fileUri =
            LocalPackageSourceIdentity.Create(
                new Uri(_root).AbsoluteUri,
                Path.GetDirectoryName(_root)!);
        var host = new RecordingMissingRootHost();
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();

        using IPackageSourceClient first = PackageSourceClientFactory.Create(
            path,
            association,
            host);
        using IPackageSourceClient second = PackageSourceClientFactory.Create(
            fileUri,
            association,
            host);
        PackageSourceFailure failure = Failed(
            await first.SearchAsync(
                string.Empty,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Same(path, host.ObservedIdentity);
        Assert.Equal(first.Source.Producer, second.Source.Producer);
        Assert.Equal(path.CanonicalPath, first.Source.Producer.Display.ToString());
        Assert.Equal(PackageSourceFailureKind.Transport, failure.Kind);

        if (!OperatingSystem.IsWindows())
        {
            using IPackageSourceClient lower =
                PackageSourceClientFactory.Create(
                    LocalPackageSourceIdentity.Create(
                        _root + "a",
                        _root),
                    association,
                    UnavailableLocalPackageSourceFileSystem.Instance);
            using IPackageSourceClient upper =
                PackageSourceClientFactory.Create(
                    LocalPackageSourceIdentity.Create(
                        _root + "A",
                        _root),
                    association,
                    UnavailableLocalPackageSourceFileSystem.Instance);
            Assert.NotEqual(lower.Source.Producer, upper.Source.Producer);
        }
    }

    [Fact]
    public async Task LocalFolderSource_AllSettlementsUseBoundResultFactory()
    {
        WriteV2Package(_root, "Contoso", "1.0.0");
        PackageSourceAssociation association =
            PackageSourceAssociation.Create();
        using IPackageSourceClient client = CreateClient(
            _root,
            association);

        PackageSearchResult search = Succeeded(
            await client.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        PackageVersionResult versions = Succeeded(
            await client.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));
        PackageSourceManifest manifest = Succeeded(
            await client.GetManifestAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload package = Succeeded(
            await client.GetPackageAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourceFailure symbols = Failed(
            await client.TryGetSymbolsAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourceFailure notFound = Failed(
            await client.GetManifestAsync(
                "Missing",
                "1.0",
                TestContext.Current.CancellationToken));
        await package.Content.DisposeAsync();

        Assert.Same(client.Source, search.Source);
        Assert.Same(client.Source, search.Matches[0].Candidate.Source);
        Assert.Same(client.Source, versions.Source);
        Assert.Same(client.Source, versions.Candidates[0].Source);
        Assert.Same(client.Source, manifest.Source);
        Assert.Same(client.Source, package.Source);
        Assert.Same(client.Source, symbols.Source);
        Assert.Same(client.Source, notFound.Source);
        Assert.Same(association, client.Source.Association);
        Assert.Equal(PackageSourceKind.LocalFolder, client.Source.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Unsupported, symbols.Kind);
        Assert.Equal(PackageSourceCapabilities.SymbolPayload, symbols.Capability);
        Assert.Equal(PackageSourceFailureKind.NotFound, notFound.Kind);
        Assert.Equal(PackageSourceCapabilities.Manifest, notFound.Capability);
    }

    [Fact]
    public async Task LocalFolderSource_V2FlatAndImmediateChildCapabilities()
    {
        WriteV2Package(_root, "Alpha", "1.0.0");
        string child = Directory.CreateDirectory(
            Path.Combine(_root, "child")).FullName;
        WriteV2Package(child, "Alpha", "2.0.0", "child package", "tools");
        string tooDeep = Directory.CreateDirectory(
            Path.Combine(child, "deeper")).FullName;
        WriteV2Package(tooDeep, "Ignored", "1.0.0");
        File.WriteAllBytes(
            Path.Combine(_root, "Alpha.3.0.0.symbols.nupkg"),
            "not a package"u8.ToArray());
        File.WriteAllBytes(
            Path.Combine(_root, "Alpha.3.0.0.snupkg"),
            "not a package"u8.ToArray());
        using IPackageSourceClient client = CreateClient(_root);

        PackageSearchResult search = Succeeded(
            await client.SearchAsync(
                "tools",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        PackageVersionResult versions = Succeeded(
            await client.GetVersionsAsync(
                "Alpha",
                TestContext.Current.CancellationToken));
        PackageSourceManifest manifest = Succeeded(
            await client.GetManifestAsync(
                "Alpha",
                "2.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload payload = Succeeded(
            await client.GetPackageAsync(
                "Alpha",
                "2.0",
                TestContext.Current.CancellationToken));
        await using (payload.Content)
        {
            Assert.True(payload.Content.CanSeek);
            Assert.Equal(payload.AdvertisedLength, payload.Content.Length);
        }

        Assert.Equal(
            PackageSourceCapabilities.Search
            | PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest
            | PackageSourceCapabilities.PackagePayload,
            client.Capabilities);
        Assert.Equal("Alpha", Assert.Single(search.Matches).Metadata.Id);
        Assert.Equal(
            PackageListingState.NotApplicable,
            search.Matches[0].Candidate.ListingState);
        Assert.Equal(
            ["1.0.0", "2.0.0"],
            versions.Candidates.Select(
                candidate => candidate.Coordinate.Version));
        Assert.All(
            versions.Candidates,
            candidate =>
            {
                Assert.Equal(
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    candidate.DiscoveryContract);
                Assert.Equal(
                    PackageListingState.NotApplicable,
                    candidate.ListingState);
            });
        Assert.False(versions.HasAuthoritativeListingState);
        Assert.Contains(
            "<id>Alpha</id>",
            Encoding.UTF8.GetString(manifest.Content.ToArray()),
            StringComparison.Ordinal);
        Assert.Empty(
            Succeeded(
                await client.GetVersionsAsync(
                    "Ignored",
                    TestContext.Current.CancellationToken))
                .Candidates);
    }

    [Fact]
    public async Task LocalFolderSource_V3HierarchicalCapabilities()
    {
        WriteV3Package(_root, "Contoso.Tools", "1.0.0");
        WriteV3Package(_root, "Contoso.Tools", "2.0.0-beta.1");
        using IPackageSourceClient client = CreateClient(_root);

        PackageSearchResult stable = Succeeded(
            await client.SearchByPrefixAsync(
                "CONTOSO",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        PackageSearchResult prerelease = Succeeded(
            await client.SearchAsync(
                string.Empty,
                prerelease: true,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        PackageSourceManifest manifest = Succeeded(
            await client.GetManifestAsync(
                "CONTOSO.TOOLS",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload package = Succeeded(
            await client.GetPackageAsync(
                "contoso.tools",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using (package.Content)
        {
            var signature = new byte[2];
            Assert.Equal(
                2,
                await package.Content.ReadAsync(
                    signature,
                    TestContext.Current.CancellationToken));
            Assert.Equal([0x50, 0x4b], signature);
        }

        Assert.Equal("1.0.0", Assert.Single(stable.Matches).Metadata.Version);
        Assert.Equal(
            "2.0.0-beta.1",
            Assert.Single(prerelease.Matches).Metadata.Version);
        Assert.Equal("Contoso.Tools", prerelease.Matches[0].Metadata.Id);
        Assert.Equal("contoso.tools", manifest.Coordinate.PackageId);

        string wrongCase = Path.Combine(
            _root,
            "Wrong.Case",
            "1.0.0");
        Directory.CreateDirectory(wrongCase);
        WritePackage(
            Path.Combine(wrongCase, "Wrong.Case.1.0.0.nupkg"),
            "Wrong.Case",
            "1.0.0");
        Assert.Empty(
            Succeeded(
                await client.GetVersionsAsync(
                    "Wrong.Case",
                    TestContext.Current.CancellationToken))
                .Candidates);
    }

    [Fact]
    public async Task LocalFolderSource_MixedLayoutsRejectDuplicateCoordinates()
    {
        WriteV2Package(_root, "Duplicate", "1.0.0");
        WriteV3Package(_root, "Duplicate", "1.0.0");
        using IPackageSourceClient client = CreateClient(_root);

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await client.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        string encryptionRoot = Directory.CreateDirectory(
            Path.Combine(_root, "manifest-encryption")).FullName;
        string encryptionPath = WriteV2Package(
            encryptionRoot,
            "Encrypted",
            "1.0.0");
        PatchManifestFlags(encryptionPath, 1);
        using IPackageSourceClient encryption =
            CreateClient(encryptionRoot);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await encryption.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await client.GetPackageAsync(
                    "Duplicate",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);

        string v2Root = Directory.CreateDirectory(
            Path.Combine(_root, "v2-duplicates")).FullName;
        WriteV2Package(
            Directory.CreateDirectory(
                Path.Combine(v2Root, "first")).FullName,
            "Duplicate",
            "2.0.0");
        WriteV2Package(
            Directory.CreateDirectory(
                Path.Combine(v2Root, "second")).FullName,
            "Duplicate",
            "2.0.0");
        using IPackageSourceClient v2Duplicates = CreateClient(v2Root);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await v2Duplicates.GetVersionsAsync(
                    "Duplicate",
                    TestContext.Current.CancellationToken)).Kind);

        string reverseRoot = Directory.CreateDirectory(
            Path.Combine(_root, "reverse")).FullName;
        WriteV3Package(reverseRoot, "Reverse", "1.0.0");
        WriteV2Package(reverseRoot, "Reverse", "1.0.0");
        using IPackageSourceClient reverse = CreateClient(reverseRoot);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await reverse.GetManifestAsync(
                    "Reverse",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task LocalFolderSource_DirectoryBoundRejectsWithoutPartialAuthority()
    {
        WriteV2Package(_root, "First", "1.0.0");
        WriteV2Package(_root, "Second", "1.0.0");
        using IPackageSourceClient client = CreateClient(
            _root,
            options: new LocalPackageSourceOptions
            {
                MaxDirectoryEntries = 1,
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateClient(
                _root,
                options: new LocalPackageSourceOptions
                {
                    MaxDirectoryEntries = 0,
                }));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await client.SearchAsync(
                    string.Empty,
                    take: 1,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        string emptyRoot = Directory.CreateDirectory(
            Path.Combine(_root, "empty-bound")).FullName;
        Directory.CreateDirectory(Path.Combine(emptyRoot, "empty"));
        using IPackageSourceClient exactBound = CreateClient(
            emptyRoot,
            options: new LocalPackageSourceOptions
            {
                MaxDirectoryEntries = 1,
            });
        Assert.Empty(
            Succeeded(
                await exactBound.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);
        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await client.GetVersionsAsync(
                    "First",
                    TestContext.Current.CancellationToken)).Kind);

        using IPackageSourceClient overflow =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new OverflowHost());
        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await overflow.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        string extentRoot = Directory.CreateDirectory(
            Path.Combine(_root, "manifest-extent")).FullName;
        string prefix =
            "<package><metadata><id>Extent</id><version>1.0.0</version>"
            + "</metadata></package>";
        string extentPath = Path.Combine(
            extentRoot,
            "Extent.1.0.0.nupkg");
        WritePackage(
            extentPath,
            "Extent",
            "1.0.0",
            manifestOverride: prefix + "EXCESS",
            compressionLevel: CompressionLevel.NoCompression);
        UnderdeclareManifest(extentPath, prefix);
        using IPackageSourceClient extent = CreateClient(extentRoot);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await extent.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        string methodRoot = Directory.CreateDirectory(
            Path.Combine(_root, "manifest-method")).FullName;
        string methodPath = WriteV2Package(
            methodRoot,
            "Method",
            "1.0.0");
        PatchManifestMethod(methodPath, 99);
        using IPackageSourceClient method = CreateClient(methodRoot);
        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            Failed(
                await method.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task LocalFolderSource_ArchivePreflightBoundsMaterialization()
    {
        string path = WriteV2Package(_root, "Bounded", "1.0.0");
        PatchEndOfCentralDirectory(
            path,
            record =>
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    record[8..],
                    2);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    record[10..],
                    2);
            });
        using IPackageSourceClient entries = CreateClient(
            _root,
            options: new LocalPackageSourceOptions
            {
                MaxArchiveEntries = 1,
            });
        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await entries.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        File.Delete(path);
        path = WriteV2Package(_root, "Bounded", "1.0.0");
        PatchEndOfCentralDirectory(
            path,
            record => BinaryPrimitives.WriteUInt32LittleEndian(
                record[12..],
                1024));
        using IPackageSourceClient directory = CreateClient(
            _root,
            options: new LocalPackageSourceOptions
            {
                MaxCentralDirectoryBytes = 128,
            });
        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await directory.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        File.Delete(path);
        WriteV2Package(
            _root,
            "First",
            "1.0.0",
            description: new string('a', 600));
        WriteV2Package(
            _root,
            "Second",
            "1.0.0",
            description: new string('b', 600));
        using IPackageSourceClient aggregate = CreateClient(
            _root,
            options: new LocalPackageSourceOptions
            {
                MaxManifestBytes = 2_000,
                MaxAggregateManifestBytes = 1_000,
            });
        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            Failed(
                await aggregate.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task LocalFolderSource_ExactOperationsValidateEmbeddedCoordinate()
    {
        WritePackage(
            Path.Combine(_root, "Renamed.1.0.0.nupkg"),
            "Other",
            "1.0.0");
        WritePackage(
            Path.Combine(_root, "Malformed.1.0.0.nupkg"),
            "Malformed",
            "1.0.0",
            manifestOverride: "<package>");
        WritePackageWithoutManifest(
            Path.Combine(_root, "Missing.1.0.0.nupkg"));
        WritePackage(
            Path.Combine(_root, "Multiple.1.0.0.nupkg"),
            "Multiple",
            "1.0.0",
            secondManifest: true);
        WritePackage(
            Path.Combine(_root, "Versionless.nupkg"),
            "Versionless",
            "1.0.0");
        WritePackage(
            Path.Combine(_root, "Suffix.invalid.nupkg"),
            "Suffix",
            "1.0.0");
        WritePackage(
            Path.Combine(_root, "Metadata.1.0.0.nupkg"),
            "Metadata",
            "1.0.0",
            manifestOverride:
                """
                <package>
                  <metadata><id>Metadata</id><version>1.0.0</version></metadata>
                  <metadata><id>Metadata</id><version>1.0.0</version></metadata>
                </package>
                """);
        using IPackageSourceClient client = CreateClient(_root);

        foreach (string id in new[]
                 {
                     "Renamed",
                     "Malformed",
                     "Missing",
                     "Multiple",
                     "Versionless",
                     "Suffix",
                     "Metadata",
                 })
        {
            Assert.Equal(
                PackageSourceFailureKind.InvalidResponse,
                Failed(
                    await client.GetManifestAsync(
                        id,
                        "1.0.0",
                        TestContext.Current.CancellationToken)).Kind);
            Assert.Equal(
                PackageSourceFailureKind.InvalidResponse,
                Failed(
                    await client.GetPackageAsync(
                        id,
                        "1.0.0",
                        TestContext.Current.CancellationToken)).Kind);
        }

    }

    [Fact]
    public async Task LocalFolderSource_AbsentUnreadableAndChangedRootsRemainDistinct()
    {
        using IPackageSourceClient empty = CreateClient(_root);
        Assert.Equal(
            PackageSourceFailureKind.NotFound,
            Failed(
                await empty.GetManifestAsync(
                    "Absent",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);

        string absentPath = Path.Combine(_root, "gone");
        LocalPackageSourceIdentity absentIdentity =
            LocalPackageSourceIdentity.Create(absentPath, _root);
        using IPackageSourceClient absent = PackageSourceClientFactory.Create(
            absentIdentity,
            PackageSourceAssociation.Create());
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            Failed(
                await absent.GetManifestAsync(
                    "Absent",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);

        using IPackageSourceClient unreadable =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new ThrowingHost());
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            Failed(
                await unreadable.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);

        byte[] package = CreatePackageBytes("Changed", "1.0.0");
        using IPackageSourceClient changed =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new MemoryHost(
                    "Changed.1.0.0.nupkg",
                    package,
                    observedLength: package.Length + 1));
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            Failed(
                await changed.GetPackageAsync(
                    "Changed",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task LocalFolderSource_ContextBoundsEveryOperation()
    {
        using IPackageSourceClient unavailable =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                UnavailableLocalPackageSourceFileSystem.Instance);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        using var canceled = new NuGetOperationContext(caller.Token);

        OperationCanceledException error =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => unavailable.SearchAsync(
                    string.Empty,
                    cancellationToken: caller.Token,
                    operationContext: canceled));
        Assert.Equal(caller.Token, error.CancellationToken);

        using var expired = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(30),
            TestContext.Current.CancellationToken);
        PackageSourceFailure failure = Failed(
            await unavailable.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken,
                expired));
        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);

        PackageSourceFailure symbols = Failed(
            await unavailable.TryGetSymbolsAsync(
                "Contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(PackageSourceFailureKind.Unsupported, symbols.Kind);

        using IPackageSourceClient slowDirectory =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new SlowListHost());
        using var directoryContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageSourceFailureKind.Timeout,
            Failed(
                await slowDirectory.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    operationContext: directoryContext)).Kind);

        using IPackageSourceClient slowFailure =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new SlowFailHost());
        using var failureContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageSourceFailureKind.Timeout,
            Failed(
                await slowFailure.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken,
                    operationContext: failureContext)).Kind);

        byte[] package = CreatePackageBytes("Slow", "1.0.0");
        using IPackageSourceClient slowArchive =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                new SlowReadHost("Slow.1.0.0.nupkg", package));
        using var archiveContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageSourceFailureKind.Timeout,
            Failed(
                await slowArchive.GetManifestAsync(
                    "Slow",
                    "1.0.0",
                    TestContext.Current.CancellationToken,
                    archiveContext)).Kind);

        var payloadHost = new MemoryHost(
            "Payload.1.0.0.nupkg",
            CreatePackageBytes("Payload", "1.0.0"));
        using IPackageSourceClient payloadClient =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                payloadHost);
        using var payloadContext = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
        PackageSourcePayload payload = Succeeded(
            await payloadClient.GetPackageAsync(
                "Payload",
                "1.0.0",
                TestContext.Current.CancellationToken,
                payloadContext));
        await Task.Delay(
            TimeSpan.FromMilliseconds(150),
            TestContext.Current.CancellationToken);
        PackageSourceStreamException payloadTimeout =
            Assert.Throws<PackageSourceStreamException>(
                () => payload.Content.ReadByte());
        Assert.Equal(PackageSourceFailureKind.Timeout, payloadTimeout.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                TimeSpan.FromMilliseconds(100)),
            payloadTimeout.Timeout);
        PackageSourceStreamException disposalTimeout =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                async () => await payload.Content.DisposeAsync());
        Assert.Equal(PackageSourceFailureKind.Timeout, disposalTimeout.Kind);
    }

    [Fact]
    public async Task LocalFolderSource_PayloadTransfersValidatedStreamOwnership()
    {
        byte[] package = CreatePackageBytes("Transfer", "1.0.0");
        var host = new MemoryHost(
            "Transfer.1.0.0.nupkg",
            package);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                LocalPackageSourceIdentity.Create(_root, _root),
                PackageSourceAssociation.Create(),
                host);

        PackageSourcePayload payload = Succeeded(
            await client.GetPackageAsync(
                "Transfer",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(1, host.OpenCount);
        client.Dispose();
        Assert.Equal(0x50, payload.Content.ReadByte());

        host.Stream!.FailReads = true;
        PackageSourceStreamException readFailure =
            Assert.Throws<PackageSourceStreamException>(
                () => payload.Content.ReadByte());
        Assert.Same(client.Source, readFailure.ResultSource);
        Assert.Equal(PackageSourceFailureKind.Transport, readFailure.Kind);
        Assert.False(readFailure.CleanupFailed);

        host.Stream.FailReads = false;
        host.Stream.FailDisposal = true;
        PackageSourceStreamException disposalFailure =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                async () => await payload.Content.DisposeAsync());
        Assert.Same(client.Source, disposalFailure.ResultSource);
        Assert.Equal(PackageSourceFailureKind.Transport, disposalFailure.Kind);
        Assert.True(disposalFailure.CleanupFailed);
    }

    [Fact]
    public async Task LocalFolderSource_HostUnavailableCannotConstructHttp()
    {
        LocalPackageSourceIdentity identity =
            LocalPackageSourceIdentity.Create(_root, _root);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                identity,
                PackageSourceAssociation.Create(),
                UnavailableLocalPackageSourceFileSystem.Instance);

        Assert.Equal(PackageSourceCapabilities.None, client.Capabilities);
        Assert.Equal(PackageSourceKind.LocalFolder, client.Source.TransportKind);
        Assert.Equal(
            identity.CanonicalPath,
            client.Source.Producer.Display.ToString());
        Assert.Equal(
            PackageSourceFailureKind.Unsupported,
            Failed(
                await client.SearchAsync(
                    string.Empty,
                    cancellationToken:
                        TestContext.Current.CancellationToken)).Kind);
        Assert.Equal(
            PackageSourceFailureKind.Unsupported,
            Failed(
                await client.GetManifestAsync(
                    "Contoso",
                    "1.0.0",
                    TestContext.Current.CancellationToken)).Kind);
    }

    private IPackageSourceClient CreateClient(
        string root,
        PackageSourceAssociation? association = null,
        LocalPackageSourceOptions? options = null) =>
        PackageSourceClientFactory.Create(
            LocalPackageSourceIdentity.Create(root, _root),
            association ?? PackageSourceAssociation.Create(),
            options);

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

    private static string WriteV2Package(
        string directory,
        string id,
        string version,
        string? description = null,
        string? tags = null)
    {
        string path = Path.Combine(
            directory,
            $"{id}.{version}.nupkg");
        WritePackage(path, id, version, description, tags);
        return path;
    }

    private static string WriteV3Package(
        string root,
        string id,
        string version)
    {
        string normalizedId = id.ToLowerInvariant();
        string normalizedVersion = NuGet.Versioning.NuGetVersion
            .Parse(version)
            .ToNormalizedString()
            .ToLowerInvariant();
        string directory = Path.Combine(
            root,
            normalizedId,
            normalizedVersion);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"{normalizedId}.{normalizedVersion}.nupkg");
        WritePackage(path, id, version);
        return path;
    }

    private static void WritePackage(
        string path,
        string id,
        string version,
        string? description = null,
        string? tags = null,
        string? manifestOverride = null,
        bool secondManifest = false,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(
            path,
            CreatePackageBytes(
                id,
                version,
                description,
                tags,
                manifestOverride,
                secondManifest,
                compressionLevel));
    }

    private static void WritePackageWithoutManifest(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("content.txt");
        using StreamWriter writer = new(entry.Open());
        writer.Write("content");
    }

    private static byte[] CreatePackageBytes(
        string id,
        string version,
        string? description = null,
        string? tags = null,
        string? manifestOverride = null,
        bool secondManifest = false,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            string manifest = manifestOverride
                ?? $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package>
                      <metadata>
                        <id>{id}</id>
                        <version>{version}</version>
                        <description>{description ?? "package"}</description>
                        <tags>{tags ?? string.Empty}</tags>
                      </metadata>
                    </package>
                    """;
            WriteEntry(
                archive,
                "_rels/.rels",
                "<Relationships />",
                compressionLevel);
            WriteEntry(
                archive,
                $"{id}.nuspec",
                manifest,
                compressionLevel);
            if (secondManifest)
            {
                WriteEntry(
                    archive,
                    "second.nuspec",
                    manifest,
                    compressionLevel);
            }
        }

        return output.ToArray();
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        string content,
        CompressionLevel compressionLevel)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            name,
            compressionLevel);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void PatchEndOfCentralDirectory(
        string path,
        Action<Span<byte>> patch)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = FindEndOfCentralDirectory(bytes);
        patch(bytes.AsSpan(offset, 22));
        File.WriteAllBytes(path, bytes);
    }

    private static int FindEndOfCentralDirectory(byte[] bytes)
    {
        for (int offset = bytes.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(offset))
                == 0x06054b50)
            {
                return offset;
            }
        }

        throw new InvalidOperationException("Test ZIP has no EOCD.");
    }

    private static void UnderdeclareManifest(
        string path,
        string declaredContent)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int local = FindSignature(bytes, 0x04034b50, occurrence: 1);
        int central = FindSignature(bytes, 0x02014b50, occurrence: 1);
        byte[] declared = Encoding.UTF8.GetBytes(declaredContent);
        uint crc = ComputeCrc32(declared);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(local + 14),
            crc);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(local + 22),
            checked((uint)declared.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(central + 16),
            crc);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(central + 24),
            checked((uint)declared.Length));
        File.WriteAllBytes(path, bytes);
    }

    private static void PatchManifestMethod(
        string path,
        ushort method)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int local = FindSignature(bytes, 0x04034b50, occurrence: 1);
        int central = FindSignature(bytes, 0x02014b50, occurrence: 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(local + 8),
            method);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(central + 10),
            method);
        File.WriteAllBytes(path, bytes);
    }

    private static void PatchManifestFlags(
        string path,
        ushort flags)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int local = FindSignature(bytes, 0x04034b50, occurrence: 1);
        int central = FindSignature(bytes, 0x02014b50, occurrence: 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(local + 6),
            flags);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(central + 8),
            flags);
        File.WriteAllBytes(path, bytes);
    }

    private static int FindSignature(
        byte[] bytes,
        uint signature,
        int occurrence)
    {
        for (int offset = 0; offset <= bytes.Length - 4; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.AsSpan(offset))
                == signature)
            {
                if (occurrence == 0)
                    return offset;

                occurrence--;
            }
        }

        throw new InvalidOperationException(
            "Test ZIP is missing an expected record.");
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1)
                    ^ (0xedb88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }

    private sealed class RecordingMissingRootHost
        : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceIdentity? ObservedIdentity { get; private set; }

        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            ObservedIdentity = source;
            directory = null;
            return false;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingHost : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation) =>
            throw new IOException("unreadable");

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();
    }

    private sealed class OverflowHost : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation) =>
            new([], [], HasMoreEntries: true);

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();
    }

    private sealed class SlowListHost : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation)
        {
            Thread.Sleep(30);
            return new LocalPackageSourceDirectoryListing(
                [],
                [],
                HasMoreEntries: false);
        }

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();
    }

    private sealed class SlowFailHost : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation)
        {
            Thread.Sleep(30);
            throw new IOException("late failure");
        }

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            throw new NotSupportedException();
    }

    private sealed class SlowReadHost(
        string name,
        byte[] content)
        : ILocalPackageSourceFileSystem
    {
        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation) =>
            new(
                [],
                [new LocalPackageSourceFile(name, new object())],
                HasMoreEntries: false);

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation) =>
            new(new SlowReadMemoryStream(content), content.Length);
    }

    private sealed class MemoryHost : ILocalPackageSourceFileSystem
    {
        private readonly string _name;
        private readonly byte[] _content;
        private readonly long? _observedLength;

        public MemoryHost(
            string name,
            byte[] content,
            long? observedLength = null)
        {
            _name = name;
            _content = content;
            _observedLength = observedLength;
        }

        public int OpenCount { get; private set; }
        public FaultableMemoryStream? Stream { get; private set; }

        public LocalPackageSourceHostCapabilities Capabilities =>
            LocalPackageSourceHostCapabilities.List
            | LocalPackageSourceHostCapabilities.Read
            | LocalPackageSourceHostCapabilities.Transfer;

        public bool TryGetDirectory(
            LocalPackageSourceIdentity source,
            out LocalPackageSourceDirectory? directory)
        {
            directory = new LocalPackageSourceDirectory(
                string.Empty,
                new object());
            return true;
        }

        public LocalPackageSourceDirectoryListing List(
            LocalPackageSourceDirectory directory,
            int maximumEntries,
            NuGetOperationDeadline operation) =>
            new(
                [],
                [
                    new LocalPackageSourceFile(
                        _name,
                        new object(),
                        _observedLength),
                ],
                HasMoreEntries: false);

        public LocalPackageSourceOpenFile OpenRead(
            LocalPackageSourceFile file,
            NuGetOperationDeadline operation)
        {
            OpenCount++;
            Stream = new FaultableMemoryStream(_content);
            return new LocalPackageSourceOpenFile(
                Stream,
                _content.Length);
        }
    }

    private sealed class FaultableMemoryStream(byte[] content)
        : MemoryStream(content, writable: false)
    {
        public bool FailReads { get; set; }
        public bool FailDisposal { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (FailReads)
                throw new IOException("read failed");

            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            if (FailReads)
                throw new IOException("read failed");

            return base.Read(buffer);
        }

        public override int ReadByte()
        {
            if (FailReads)
                throw new IOException("read failed");

            return base.ReadByte();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && FailDisposal)
                throw new IOException("disposal failed");
        }

        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SlowReadMemoryStream(byte[] content)
        : MemoryStream(content, writable: false)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(30),
                cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
