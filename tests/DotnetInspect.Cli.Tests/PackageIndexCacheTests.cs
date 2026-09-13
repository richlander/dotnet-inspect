using DotnetInspector.Cache;
using System.Security.Cryptography;
using System.IO.Compression;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageIndexCacheTests
{
    public PackageIndexCacheTests()
        => PersistentCache.Initialize("dotnet-inspect-test");

    [Fact]
    public void PackageIndexCache_SubjectControlsPersistentReuse()
    {
        string packageId = $"subject.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject subject = Subject(
            packageId,
            authority: "authority-a",
            content: [1]);
        InspectionResult value = Result(packageId);
        PackageIndexCache.Set(Complete(subject, value));

        PackageIndexCacheSubject otherAuthority = Subject(
            packageId,
            authority: "authority-b",
            content: [1]);
        PackageIndexCacheSubject otherVersion = Subject(
            packageId,
            version: "2.0.0",
            authority: "authority-a",
            content: [1]);
        PackageIndexCacheSubject otherDigest = Subject(
            packageId,
            authority: "authority-a",
            content: [2]);

        Assert.NotNull(PackageIndexCache.TryGet(subject));
        Assert.Null(PackageIndexCache.TryGet(otherAuthority));
        Assert.Null(PackageIndexCache.TryGet(otherVersion));
        Assert.Null(PackageIndexCache.TryGet(otherDigest));

        byte[] bytes = PersistentCache.TryGetBytes(
            PackageIndexCache.Category,
            PackageIndexCache.CacheKey(subject),
            extension: "bin")!;
        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            PackageIndexCache.CacheKey(otherAuthority),
            bytes,
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(otherAuthority));

        var payload = new AcquiredPackageSourcePayload(
            NuGetFetch.PackageSourceCoordinate.Create(packageId, "1.0.0"),
            new InMemoryPackageContent(
                [1, 2, 3],
                fromCache: true,
                producerKey: "same-producer"),
            "same-producer",
            PackagePayloadOrigin.Cache);
        Assert.Null(PackageIndexCacheSubject.TryCreate(
            new PackageExtractionResult(
                "/unused",
                TempDir: null,
                packageId,
                "1.0.0",
                ProducerKey: "same-producer")
            {
                Authority = new ConfiguredPackageAuthority(
                    new NuGetFetch.PackageSource(
                        "remote",
                        "https://example.invalid/v3/index.json")),
                AcquiredPayload = payload,
            },
            _ => { },
            TestContext.Current.CancellationToken));

            var archiveLessForeignContent = new FileSystemPackageContent(
            "/unused",
            nupkgPath: null,
            fromCache: true,
            producerKey: "same-producer");
            Assert.Null(PackageIndexCacheSubject.TryCreate(
            new PackageExtractionResult(
                "/unused",
                TempDir: null,
                packageId,
                "1.0.0",
                ProducerKey: "same-producer")
            {
                Authority = new ConfiguredPackageAuthority(
                    new NuGetFetch.PackageSource("local", "/unused")),
                AcquiredPayload = new AcquiredPackageSourcePayload(
                    NuGetFetch.PackageSourceCoordinate.Create(
                        packageId,
                        "1.0.0"),
                    archiveLessForeignContent,
                    "same-producer",
                    PackagePayloadOrigin.Cache),
            },
            _ => { },
            TestContext.Current.CancellationToken));

        string foreignArchive = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(foreignArchive, [1, 2, 3]);
            var foreignContent = new FileSystemPackageContent(
                "/unused",
                foreignArchive,
                fromCache: true,
                producerKey: "same-producer");
            Assert.Null(PackageIndexCacheSubject.TryCreate(
                new PackageExtractionResult(
                    "/unused",
                    TempDir: null,
                    packageId,
                    "1.0.0",
                    ProducerKey: "same-producer")
                {
                    Authority = new ConfiguredPackageAuthority(
                        new NuGetFetch.PackageSource("local", "/unused")),
                    AcquiredPayload = new AcquiredPackageSourcePayload(
                        NuGetFetch.PackageSourceCoordinate.Create(
                            packageId,
                            "1.0.0"),
                        foreignContent,
                        "same-producer",
                        PackagePayloadOrigin.Cache),
                },
                _ => { },
                TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(foreignArchive);
        }
    }

    [Fact]
    public void PackageIndexCache_ProjectionContainsOnlyStableFacts()
    {
        string packageId = $"projection.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject subject = Subject(packageId);
        var description = new InertString(
            TextPolicy.Prose,
            "first\r\n\tliteral \\u202E and live \u202E\nauthors: attacker\n");
        InspectionResult value = Result(packageId);
        value.Description = description;
        value.ContentDirectories = ["lib", "tools"];
        value.TargetFrameworks = ["net8.0", "net9.0"];
        value.SupportedRids = ["linux-x64", "win-x64"];
        value.LibraryFiles = ["lib/net8.0/A.dll", "lib/net9.0/A.dll"];
        value.NativeFiles = ["linux-x64: liba.so", "win-x64: a.dll"];
        value.DependencyGroups =
        [
            new DependencyGroup
            {
                TargetFramework = "net8.0",
                Dependencies =
                [
                    new PackageDependency
                    {
                        Id = "A",
                        Version = "[1.0.0,2.0.0)",
                    },
                    new PackageDependency { Id = "B", Version = "2.0.0" },
                ],
            },
        ];
        value.RuntimeDependencies =
        [
            new PackageDependency { Id = "A", Version = "1.0.0" },
            new PackageDependency { Id = "B", Version = "2.0.0" },
        ];
        value.RuntimeIdentifierPackages =
        [
            new RidPackageReference
            {
                RuntimeIdentifier = "linux-x64",
                PackageId = $"{packageId}.linux-x64",
                Exists = true,
            },
        ];
        value.BinarySignals = new PackageBinarySignals
        {
            TotalBinaries = 2,
            SymbolsAvailable = 2,
            SourceLinkAvailable = 1,
            EmbeddedPdbs = 1,
            InPackagePdbs = 1,
            EmbeddedSourceLinkPdbs = 1,
        };
        value.BuiltDate = DateTimeOffset.UtcNow;
        value.Published = DateTimeOffset.UtcNow;
        value.TotalDownloads = 42;
        value.Files = [new PackageFile("payload.txt", 1)];

        PackageIndexCache.Set(Complete(subject, value));
        InspectionResult cached =
            Assert.IsType<InspectionResult>(PackageIndexCache.TryGet(subject));

        Assert.Equal(description, cached.Description);
        Assert.Equal(
            "first\r\n\tliteral \\\\u202E and live \\u202E\nauthors: attacker\n",
            cached.Description?.ToString());
        Assert.Equal(
            "[1.0.0,2.0.0)",
            Assert.Single(cached.DependencyGroups!)
                .Dependencies[0].Version);
        Assert.Equal("A", Assert.Single(cached.RuntimeDependencies!, d => d.Id == "A").Id);
        Assert.Null(Assert.Single(cached.RuntimeIdentifierPackages!).Exists);
        Assert.Equal(2, cached.BinarySignals!.SymbolsAvailable);
        Assert.Equal(1, cached.BinarySignals.SourceLinkAvailable);
        Assert.Equal(0, cached.BinarySignals.SnupkgPdbs);
        Assert.Null(cached.BuiltDate);
        Assert.Null(cached.Published);
        Assert.Null(cached.TotalDownloads);
        Assert.Null(cached.Files);

        value.TargetFrameworks = ["net9.0", "net8.0"];
        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                subject,
                subject.Generation,
                value,
                isComplete: true));

        InspectionResult externalSignals = Result(packageId);
        externalSignals.BinarySignals = new PackageBinarySignals
        {
            TotalBinaries = 1,
            SymbolsAvailable = 1,
            SnupkgPdbs = 1,
        };
        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                subject,
                subject.Generation,
                externalSignals,
                isComplete: true));
    }

    [Fact]
    public void PackageIndexCache_PublicationRequiresOneSuccessfulFrozenSubject()
    {
        string packageId = $"incomplete.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject subject = Subject(packageId);
        PackageIndexProduction production = PackageIndexProduction.Create(
            subject,
            subject.Generation,
            Result(packageId),
            isComplete: false,
            "A deps file could not be parsed.");

        Assert.IsType<PackageIndexProduction.Incomplete>(production);
        if (production is PackageIndexProduction.Complete complete)
            PackageIndexCache.Set(complete);
        Assert.Null(PackageIndexCache.TryGet(subject));

        PackageIndexCacheSubject replacement =
            Subject(packageId, content: [9]);
        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                subject,
                replacement.Generation,
                Result(packageId),
                isComplete: true));

        InspectionResult shuffled = Result(packageId);
        shuffled.TargetFrameworks = ["net9.0", "net8.0"];
        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                subject,
                subject.Generation,
                shuffled,
                isComplete: true));

        PackageIndexCacheSubject prerelease = Subject(
            packageId,
            version: "1.0.0-beta.1");
        InspectionResult mixedCaseVersion = Result(
            packageId,
            version: "1.0.0-BETA.1");
        Assert.IsType<PackageIndexProduction.Complete>(
            PackageIndexProduction.Create(
                prerelease,
                prerelease.Generation,
                mixedCaseVersion,
                isComplete: true));
    }

    [Fact]
    public void PackageIndexCache_GenerationAndDigestFenceLookupAndPublication()
    {
        string packageId = $"generation.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject first = Subject(packageId, content: [1]);
        PackageIndexCacheSubject replacement = Subject(packageId, content: [2]);
        PackageIndexCacheSubject sameBytesNewGeneration =
            Subject(packageId, content: [1]);

        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                first,
                replacement.Generation,
                Result(packageId),
                isComplete: true));

        PackageIndexCache.Set(Complete(first, Result(packageId)));
        Assert.Null(PackageIndexCache.TryGet(replacement));
        Assert.NotNull(PackageIndexCache.TryGet(sameBytesNewGeneration));
    }

    [Fact]
    public void PackageIndexCache_EntryIsCompleteAndCurrent()
    {
        string packageId = $"malformed.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject subject = Subject(packageId);
        PackageIndexCache.Set(Complete(subject, Result(packageId)));
        string key = PackageIndexCache.CacheKey(subject);
        byte[] bytes = PersistentCache.TryGetBytes(
            PackageIndexCache.Category,
            key,
            extension: "bin")!;

        byte[] missingCompletion = [.. bytes];
        int completionOffset = missingCompletion.AsSpan()
            .IndexOf(new byte[] { 0x50, 0x4D, 0x4F, 0x43 });
        Assert.True(completionOffset >= 0);
        missingCompletion[completionOffset] = 0;
        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            key,
            missingCompletion,
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(subject));

        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            key,
            [.. bytes, 0],
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(subject));

        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            key,
            bytes[..^sizeof(int)],
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(subject));

        byte[] malformed = [.. bytes];
        malformed[0] ^= 0xFF;
        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            key,
            malformed,
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(subject));

        byte[] oversizedString = [.. bytes];
        for (int i = 0; i < 4; i++)
            oversizedString[12 + i] = 0xFF;
        oversizedString[16] = 0x7F;
        PersistentCache.SetBytes(
            PackageIndexCache.Category,
            key,
            oversizedString,
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(subject));

        PackageIndexCacheSubject predecessorSubject = Subject(
            $"{packageId}.predecessor");
        PersistentCache.SetBytes(
            "pkg-index-v16",
            PackageIndexCache.CacheKey(predecessorSubject),
            bytes,
            extension: "bin");
        Assert.Null(PackageIndexCache.TryGet(predecessorSubject));
    }

    [Fact]
    public void PackageIndexCache_TruncatedDescriptionsCannotPublish()
    {
        string packageId = $"description.package.{Guid.NewGuid():N}";
        PackageIndexCacheSubject subject = Subject(packageId);
        InspectionResult value = Result(packageId);
        value.Description = new InertString(
            TextPolicy.Field,
            "a\u202Eb",
            maxLength: 3);

        Assert.IsType<PackageIndexProduction.Incomplete>(
            PackageIndexProduction.Create(
                subject,
                subject.Generation,
                value,
                isComplete: true));
    }

    [Fact]
    public async Task PackageInspector_ColdAndWarmCacheableProjectionAgree()
    {
        string root = Directory.CreateTempSubdirectory(
            "package-index-projection-").FullName;
        string nupkg = $"{root}.nupkg";
        string? partialRoot = null;
        string packageId = $"Projection.Agreement.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        try
        {
            string lib = Path.Combine(root, "lib", "net8.0");
            Directory.CreateDirectory(lib);
            await File.WriteAllTextAsync(
                Path.Combine(lib, "payload.txt"),
                "payload",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                "# Package",
                TestContext.Current.CancellationToken);
            WriteBuildDateArchive(
                nupkg,
                new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.Zero));

            var content = new InMemoryPackageContent(
                [1, 2, 3, 4],
                fromCache: true,
                producerKey: "projection-producer");
            var resolution = new PackageExtractionResult(
                root,
                TempDir: null,
                PackageName: packageId,
                Version: version,
                NupkgPath: nupkg,
                ProducerKey: "projection-producer")
            {
                Authority = new ConfiguredPackageAuthority(
                    new NuGetFetch.PackageSource(
                        "projection",
                        root)),
                AcquiredPayload = new AcquiredPackageSourcePayload(
                    NuGetFetch.PackageSourceCoordinate.Create(
                        packageId,
                        version),
                    content,
                    "projection-producer",
                    PackagePayloadOrigin.Cache),
            };
            NuspecData nuspec = NuspecParser.ParseContent($"""
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <description>projection agreement</description>
                    <dependencies>
                      <dependency id="B" version="2.0.0" />
                      <dependency id="A" version="1.0.0" />
                    </dependencies>
                  </metadata>
                </package>
                """);
            using var client = new HttpClient();

            InspectionResult cold = await PackageInspector.InspectAsync(
                resolution,
                packageId,
                version,
                isLocalFile: false,
                localFilePath: null,
                nuspec,
                client,
                new VerboseLogger(enabled: false));
            Directory.Delete(root, recursive: true);
            WriteBuildDateArchive(
                nupkg,
                new DateTimeOffset(2025, 2, 3, 4, 6, 0, TimeSpan.Zero));
            InspectionResult warm = await PackageInspector.InspectAsync(
                resolution,
                packageId,
                version,
                isLocalFile: false,
                localFilePath: null,
                nuspec: null,
                client,
                new VerboseLogger(enabled: false));

            Assert.Equal(cold.PackageName, warm.PackageName);
            Assert.Equal(cold.Version, warm.Version);
            Assert.Equal(cold.Description, warm.Description);
            Assert.Equal(cold.PackageReadmeFile, warm.PackageReadmeFile);
            Assert.Equal(cold.HasReadme, warm.HasReadme);
            Assert.Equal(cold.ContentDirectories, warm.ContentDirectories);
            Assert.Equal(cold.TargetFrameworks, warm.TargetFrameworks);
            Assert.Equal(cold.LibraryFiles, warm.LibraryFiles);
            Assert.Equal(2024, cold.BuiltDate?.Year);
            Assert.Equal(2025, warm.BuiltDate?.Year);
            Assert.Equal(
                cold.DependencyGroups!.SelectMany(g => g.Dependencies)
                    .Select(d => (d.Id, d.Version)),
                warm.DependencyGroups!.SelectMany(g => g.Dependencies)
                    .Select(d => (d.Id, d.Version)));

            partialRoot = Directory.CreateTempSubdirectory(
                "package-index-partial-").FullName;
            string tools = Path.Combine(partialRoot, "tools", "net8.0", "any");
            Directory.CreateDirectory(tools);
            string brokenLib = Path.Combine(partialRoot, "lib", "net8.0");
            Directory.CreateDirectory(brokenLib);
            await File.WriteAllTextAsync(
                Path.Combine(brokenLib, "Broken.dll"),
                "not a managed assembly",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(tools, "first.deps.json"),
                """{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0/linux-x64"}}""",
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(tools, "second.deps.json"),
                """{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0/win-x64"}}""",
                TestContext.Current.CancellationToken);
            string partialPackage = $"{packageId}.partial";
            var partialContent = new InMemoryPackageContent(
                [5, 6, 7, 8],
                fromCache: true,
                producerKey: "projection-producer");
            var partialResolution = new PackageExtractionResult(
                partialRoot,
                TempDir: null,
                PackageName: partialPackage,
                Version: version,
                ProducerKey: "projection-producer")
            {
                Authority = new ConfiguredPackageAuthority(
                    new NuGetFetch.PackageSource("partial", partialRoot)),
                AcquiredPayload = new AcquiredPackageSourcePayload(
                    NuGetFetch.PackageSourceCoordinate.Create(
                        partialPackage,
                        version),
                    partialContent,
                    "projection-producer",
                    PackagePayloadOrigin.Cache),
            };

            await PackageInspector.InspectAsync(
                partialResolution,
                partialPackage,
                version,
                isLocalFile: false,
                localFilePath: null,
                NuspecParser.ParseContent($"""
                    <package>
                      <metadata>
                        <id>{partialPackage}</id>
                        <version>{version}</version>
                      </metadata>
                    </package>
                    """),
                client,
                new VerboseLogger(enabled: false));
            PackageIndexCacheSubject partialSubject =
                Assert.IsType<PackageIndexCacheSubject>(
                    PackageIndexCacheSubject.TryCreate(
                        partialResolution,
                        _ => { },
                        TestContext.Current.CancellationToken));
            Assert.Null(PackageIndexCache.TryGet(partialSubject));
            Directory.Delete(partialRoot, recursive: true);
            partialRoot = null;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (File.Exists(nupkg))
                File.Delete(nupkg);
            if (partialRoot is not null && Directory.Exists(partialRoot))
                Directory.Delete(partialRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{"libraries":[]}""")]
    [InlineData("""{"runtimeTarget":{"name":"\uD800"}}""")]
    public async Task PackageInspector_MalformedDepsPreservesColdResultAndDeclinesPublication(
        string depsJson)
    {
        string root = Directory.CreateTempSubdirectory(
            "package-index-wrong-deps-").FullName;
        string packageId = $"Wrong.Deps.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        try
        {
            string tools = Path.Combine(root, "tools", "net8.0", "any");
            Directory.CreateDirectory(tools);
            await File.WriteAllTextAsync(
                Path.Combine(tools, "wrong.deps.json"),
                depsJson,
                TestContext.Current.CancellationToken);
            var content = new InMemoryPackageContent(
                [9, 10, 11, 12],
                fromCache: true,
                producerKey: "wrong-deps-producer");
            var resolution = new PackageExtractionResult(
                root,
                TempDir: null,
                PackageName: packageId,
                Version: version,
                ProducerKey: "wrong-deps-producer")
            {
                Authority = new ConfiguredPackageAuthority(
                    new NuGetFetch.PackageSource("wrong-deps", root)),
                AcquiredPayload = new AcquiredPackageSourcePayload(
                    NuGetFetch.PackageSourceCoordinate.Create(
                        packageId,
                        version),
                    content,
                    "wrong-deps-producer",
                    PackagePayloadOrigin.Cache),
            };

            using var client = new HttpClient();
            InspectionResult cold = await PackageInspector.InspectAsync(
                resolution,
                packageId,
                version,
                isLocalFile: false,
                localFilePath: null,
                NuspecParser.ParseContent($"""
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>{version}</version>
                      </metadata>
                    </package>
                    """),
                client,
                new VerboseLogger(enabled: false));

            Assert.Equal(packageId, cold.PackageName);
            PackageIndexCacheSubject subject =
                Assert.IsType<PackageIndexCacheSubject>(
                    PackageIndexCacheSubject.TryCreate(
                        resolution,
                        _ => { },
                        TestContext.Current.CancellationToken));
            Assert.Null(PackageIndexCache.TryGet(subject));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteBuildDateArchive(
        string path,
        DateTimeOffset timestamp)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("payload.txt");
        entry.LastWriteTime = timestamp;
        using Stream content = entry.Open();
        content.WriteByte(1);
    }

    private static PackageIndexProduction.Complete Complete(
        PackageIndexCacheSubject subject,
        InspectionResult value)
        => Assert.IsType<PackageIndexProduction.Complete>(
            PackageIndexProduction.Create(
                subject,
                subject.Generation,
                value,
                isComplete: true));

    private static InspectionResult Result(
        string packageId,
        string version = "1.0.0")
        => new()
        {
            PackageName = packageId,
            Version = version,
        };

    private static PackageIndexCacheSubject Subject(
        string packageId,
        string version = "1.0.0",
        string authority = "authority-a",
        byte[]? content = null)
    {
        byte[] bytes = content ?? [1, 2, 3];
        var packageContent = new InMemoryPackageContent(
            bytes,
            fromCache: false,
            producerKey: "producer");
        return new PackageIndexCacheSubject(
            authority,
            packageId.ToLowerInvariant(),
            version,
            "SHA-256",
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            packageContent.GenerationIdentity);
    }
}
