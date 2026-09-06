using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class AuthorityScopedPackageStoreTests : IDisposable
{
    private const string PackageName = "Authority.Store.Fixture";
    private const string Version = "1.0.0";
    private readonly string _root = Path.Combine(
        Directory.GetCurrentDirectory(), "artifacts", $"authority-store-{Guid.NewGuid():N}");
    private readonly string? _previousGlobalRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    private int _temporaryRootRequests;

    private string CacheRoot => Path.Combine(_root, "cache");
    private string TemporaryRoot => Path.Combine(_root, "temporary");
    private string GlobalRoot => Path.Combine(_root, "global");

    public AuthorityScopedPackageStoreTests()
    {
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: true);
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", GlobalRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _previousGlobalRoot);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task LocalAuthorities_KeepIndependentPayloadsAndActualProducerProvenance()
    {
        ConfiguredPackageAuthority first = LocalAuthority("first");
        ConfiguredPackageAuthority second = LocalAuthority("second");
        using IPackageSourceClient firstClient = CreateClient(first);
        using IPackageSourceClient secondClient = CreateClient(second);
        var firstStore = CreateStore(first, firstClient);
        var secondStore = CreateStore(second, secondClient);

        IPackageContent firstContent = await CommitAsync(firstStore, firstClient, "first");
        Assert.Null(secondStore.TryGetCached(PackageName, Version, [firstClient.Source.Producer.Key]));
        IPackageContent secondContent = await CommitAsync(secondStore, secondClient, "second");

        Assert.NotEqual(firstContent.RootPath, secondContent.RootPath);
        Assert.Equal("first", ReadPayload(firstContent));
        Assert.Equal("second", ReadPayload(secondContent));
        Assert.Equal(firstClient.Source.Producer.Key, firstContent.ProducerKey);
        Assert.Equal(secondClient.Source.Producer.Key, secondContent.ProducerKey);
        Assert.NotEqual(first.PersistentCacheKey, firstContent.ProducerKey);
        Assert.Contains("package-authority-content-v1", firstContent.RootPath!);
        Assert.Equal(first.PersistentCacheKey, Path.GetFileName(firstContent.RootPath));
        string marker = File.ReadAllText(Path.Combine(
            firstContent.RootPath!, NuGetCache.CommitMarkerFileName));
        Assert.Contains(first.PersistentCacheKey!, marker);
        Assert.Contains(firstContent.ProducerKey, marker);
        Assert.True(firstContent.RequiresArchiveTreeMatch);
        Assert.Equal(PackageContentAdmission.Outcome.Admissible,
            await PackageContentAdmission.EvaluateAsync(
                firstContent, new PackagePayloadLimits(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LocalAuthority_RecreatedFromFileUriReusesTheAuthoritySlot()
    {
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        IPackageContent committed = await CommitAsync(CreateStore(authority, client), client, "retained");
        var alias = new ConfiguredPackageAuthority(new PackageSource(
            "alias", new Uri(authority.LocalIdentity!.CanonicalPath).AbsoluteUri));
        using IPackageSourceClient aliasClient = CreateClient(alias);
        int requestsBeforeLookup = _temporaryRootRequests;
        IPackageContent cached = Assert.IsType<FileSystemPackageContent>(
            CreateStore(alias, aliasClient).TryGetCached(
                PackageName.ToUpperInvariant(), Version, [aliasClient.Source.Producer.Key]));

        Assert.Equal(committed.RootPath, cached.RootPath);
        Assert.Equal(client.Source.Producer.Key, cached.ProducerKey);
        Assert.Equal("retained", ReadPayload(cached));
        Assert.True(cached.FromCache);
        Assert.Equal(requestsBeforeLookup, _temporaryRootRequests);
        Assert.Null(CreateStore(alias, aliasClient).TryGetCached(PackageName, Version, null));
        Assert.Null(CreateStore(alias, aliasClient).TryGetCached(PackageName, Version, ["another-producer"]));
    }

    [Fact]
    public async Task LegacyProducerScopedCache_IsNotReinterpretedOrRetired()
    {
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        string sourceKey = NuGetCache.GetSourceKey(authority.Source.Url);
        string extracted = Path.Combine(_root, "legacy-extracted");
        Directory.CreateDirectory(extracted);
        File.WriteAllText(Path.Combine(extracted, $"{PackageName}.nuspec"), "<package />");
        CommittedPackage legacy = NuGetCache.CommitPackage(
            extracted, null, PackageName, Version, sourceKey);
        string originalMarker = File.ReadAllText(Path.Combine(
            legacy.ExtractPath, NuGetCache.CommitMarkerFileName));
        var store = CreateStore(authority, client);

        Assert.Null(store.TryGetCached(PackageName, Version, [sourceKey, client.Source.Producer.Key]));
        Assert.Equal(0, _temporaryRootRequests);
        IPackageContent current = await CommitAsync(store, client, "new authority entry");
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: true);
        _ = CoreCache.CancelAndWaitForMaintenance(TimeSpan.FromSeconds(10));

        Assert.NotEqual(legacy.ExtractPath, current.RootPath);
        Assert.Equal(legacy.ExtractPath,
            NuGetCache.TryGetCachedPackage(PackageName, Version, [sourceKey]));
        Assert.Equal(
            $"package-content-v5:{PackageName.ToLowerInvariant()}@{Version}:{sourceKey}",
            originalMarker);
        Assert.Equal(originalMarker,
            File.ReadAllText(Path.Combine(legacy.ExtractPath, NuGetCache.CommitMarkerFileName)));
    }

    [Fact]
    public void HttpAuthority_EqualProducerDoesNotAuthorizeLegacyPersistentSlot()
    {
        var authority = new ConfiguredPackageAuthority(new PackageSource(
            "http", "https://feed.example/F/auth/fixture-secret/api"));
        using IPackageSourceClient client = CreateClient(authority);
        string extracted = Path.Combine(_root, "legacy-extracted");
        Directory.CreateDirectory(extracted);
        File.WriteAllText(Path.Combine(extracted, $"{PackageName}.nuspec"), "<package />");
        CommittedPackage legacy = NuGetCache.CommitPackage(
            extracted, null, PackageName, Version, client.Source.Producer.Key);

        Assert.Null(CreateStore(authority, client).TryGetCached(
            PackageName, Version, [client.Source.Producer.Key]));
        Assert.Equal(0, _temporaryRootRequests);
        Assert.Equal(legacy.ExtractPath,
            NuGetCache.TryGetCachedPackage(PackageName, Version, [client.Source.Producer.Key]));
    }

    [Theory]
    [InlineData(
        "https://feed.example/v3/index.json?tenant=first",
        "https://feed.example/v3/index.json?tenant=second")]
    [InlineData(
        "https://feed.example/F/auth/first-secret/api",
        "https://feed.example/F/auth/second-secret/api")]
    public async Task HttpAuthorities_WithEqualProducersKeepInstanceLocalTemporarySlots(
        string firstUrl, string secondUrl)
    {
        var first = new ConfiguredPackageAuthority(new PackageSource("first", firstUrl));
        var second = new ConfiguredPackageAuthority(new PackageSource("second", secondUrl));
        using IPackageSourceClient firstClient = CreateClient(first);
        using IPackageSourceClient secondClient = CreateClient(second);
        Assert.Equal(firstClient.Source.Producer, secondClient.Source.Producer);
        Assert.Null(first.PersistentCacheKey);
        Assert.Null(second.PersistentCacheKey);
        var firstStore = CreateStore(first, firstClient);
        var secondStore = CreateStore(second, secondClient);

        IPackageContent firstContent = await CommitAsync(firstStore, firstClient, "first");
        Assert.Null(secondStore.TryGetCached(PackageName, Version, [firstContent.ProducerKey]));
        Assert.Null(CreateStore(first, firstClient).TryGetCached(
            PackageName, Version, [firstContent.ProducerKey]));
        IPackageContent secondContent = await CommitAsync(secondStore, secondClient, "second");

        Assert.NotEqual(firstContent.RootPath, secondContent.RootPath);
        Assert.Equal("first", ReadPayload(Assert.IsType<FileSystemPackageContent>(
            firstStore.TryGetCached(PackageName, Version, [firstContent.ProducerKey]))));
        Assert.Equal("second", ReadPayload(secondContent));
        Assert.Equal(firstClient.Source.Producer.Key, firstContent.ProducerKey);
        Assert.Equal(secondClient.Source.Producer.Key, secondContent.ProducerKey);
        Assert.False(Directory.Exists(CacheRoot));
        foreach (IPackageContent content in new[] { firstContent, secondContent })
        {
            string relative = Path.GetRelativePath(TemporaryRoot, content.RootPath!);
            string directory = relative.Split(Path.DirectorySeparatorChar)[0];
            Assert.StartsWith("package-authority-", directory);
            Assert.True(Guid.TryParseExact(directory["package-authority-".Length..], "N", out _));
            string marker = File.ReadAllText(Path.Combine(content.RootPath!, NuGetCache.CommitMarkerFileName));
            Assert.DoesNotContain(firstUrl, marker);
            Assert.DoesNotContain(secondUrl, marker);
            Assert.DoesNotContain("first-secret", marker);
            Assert.DoesNotContain("second-secret", marker);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LookupMiss_DoesNotAllocateTemporaryOrPersistentDirectories(bool local)
    {
        ConfiguredPackageAuthority authority = local
            ? LocalAuthority("feed")
            : new(new PackageSource("http", "https://feed.example/v3/index.json?tenant=first"));
        using IPackageSourceClient client = CreateClient(authority);
        var store = CreateStore(authority, client);

        Assert.Null(store.TryGetCached(PackageName, Version, [client.Source.Producer.Key]));
        Assert.Empty(store.EnumerateCached(PackageName, Version, null));
        Assert.Equal(0, _temporaryRootRequests);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("", Version)]
    [InlineData(PackageName, "")]
    public async Task InvalidCoordinates_AreRejectedBeforeAllocatingStorage(string packageName, string version)
    {
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        var store = CreateStore(authority, client);

        Assert.Throws<ArgumentException>(() =>
            store.TryGetCached(packageName, version, [client.Source.Producer.Key]));
        using var archive = new MemoryStream(MakeNupkg("unused"));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.CommitAsync(packageName, version, client.Source.Producer.Key,
                archive, TestContext.Current.CancellationToken));
        Assert.Equal(0, _temporaryRootRequests);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalPackages_LocalAuthorityAcceptsCanonicalPathAndFileUri(bool fileUri)
    {
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: false);
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        string source = fileUri
            ? new Uri(authority.LocalIdentity!.CanonicalPath).AbsoluteUri
            : Path.Combine(authority.LocalIdentity!.CanonicalPath, "child", "..");
        string global = WriteGlobalPackage(source);
        IPackageContent cached = Assert.IsType<FileSystemPackageContent>(
            CreateStore(authority, client).TryGetCached(PackageName, Version, [client.Source.Producer.Key]));

        Assert.Equal(global, cached.RootPath);
        Assert.Equal(client.Source.Producer.Key, cached.ProducerKey);
        Assert.False(cached.RequiresArchiveTreeMatch);
        Assert.Equal("global", ReadPayload(cached));
        Assert.Equal(0, _temporaryRootRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative/feed")]
    [InlineData("file:relative")]
    [InlineData("other-root")]
    [InlineData("https://feed.example/v3/index.json")]
    public void GlobalPackages_MissingInvalidOrDifferentAuthorityMetadataIsAMiss(string? source)
    {
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: false);
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        WriteGlobalPackage(source == "other-root" ? Path.Combine(_root, "other") : source);

        Assert.Null(CreateStore(authority, client).TryGetCached(
            PackageName, Version, [client.Source.Producer.Key]));
        Assert.Equal(0, _temporaryRootRequests);
    }

    [Fact]
    public void GlobalPackages_NoNuGetCacheSkipsMatchingLocalMetadata()
    {
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        WriteGlobalPackage(authority.Source.Url);

        Assert.Null(CreateStore(authority, client).TryGetCached(
            PackageName, Version, [client.Source.Producer.Key]));
        Assert.Equal(0, _temporaryRootRequests);
    }

    [Fact]
    public async Task GlobalPackages_AuthorizedTierRemainsLazyBehindAuthoritySlot()
    {
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: false);
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        var store = CreateStore(authority, client);
        IPackageContent committed = await CommitAsync(store, client, "app cache");

        using IEnumerator<IPackageContent> candidates =
            store.EnumerateCached(PackageName, Version, [client.Source.Producer.Key]).GetEnumerator();
        Assert.True(candidates.MoveNext());
        Assert.Equal(committed.RootPath, candidates.Current.RootPath);
        string global = WriteGlobalPackage(authority.Source.Url);
        Assert.True(candidates.MoveNext());
        Assert.Equal(global, candidates.Current.RootPath);
        Assert.False(candidates.MoveNext());
    }

    [Fact]
    public void GlobalPackages_HttpHasNoDurableAuthorityEvenWithMatchingMetadata()
    {
        NuGetCache.Initialize("dotnet-inspect-test", CacheRoot, skipNuGetCache: false);
        var authority = new ConfiguredPackageAuthority(new PackageSource(
            "http", "https://feed.example/v3/index.json?tenant=first"));
        using IPackageSourceClient client = CreateClient(authority);
        WriteGlobalPackage(authority.Source.Url);

        Assert.Null(CreateStore(authority, client).TryGetCached(
            PackageName, Version, [client.Source.Producer.Key]));
        Assert.Equal(0, _temporaryRootRequests);
    }

    [Fact]
    public async Task LocalAuthority_ConcurrentPublishersConvergeOnOneCompleteSlot()
    {
        ConfiguredPackageAuthority authority = LocalAuthority("feed");
        using IPackageSourceClient client = CreateClient(authority);
        Task<IPackageContent>[] commits = Enumerable.Range(0, 6)
            .Select(_ => CommitAsync(CreateStore(authority, client), client, "winner"))
            .ToArray();
        IPackageContent[] contents = await Task.WhenAll(commits);

        Assert.Single(contents.Select(content => content.RootPath).Distinct());
        foreach (IPackageContent content in contents)
        {
            Assert.Equal("winner", ReadPayload(content));
            Assert.True(File.Exists(Path.Combine(content.RootPath!, NuGetCache.CommitMarkerFileName)));
            Assert.True(File.Exists(content.NupkgPath));
        }
        Assert.Empty(Directory.EnumerateDirectories(CacheRoot, "*.tmp-*", SearchOption.AllDirectories));
    }

    private ConfiguredPackageAuthority LocalAuthority(string name) =>
        new(new PackageSource(name, Path.Combine(_root, name)));

    private static IPackageSourceClient CreateClient(ConfiguredPackageAuthority authority) =>
        authority.LocalIdentity is { } local
            ? PackageSourceClientFactory.Create(local, authority.Association)
            : PackageSourceClientFactory.Create(authority.Source, authority.Association);

    private AuthorityScopedFileSystemPackageStore CreateStore(
        ConfiguredPackageAuthority authority, IPackageSourceClient client) =>
        new(authority, client.Source.Producer, () =>
        {
            Interlocked.Increment(ref _temporaryRootRequests);
            return Directory.CreateDirectory(TemporaryRoot).FullName;
        });

    private static async Task<IPackageContent> CommitAsync(
        IPackageStore store, IPackageSourceClient client, string payload)
    {
        using var archive = new MemoryStream(MakeNupkg(payload));
        return await store.CommitAsync(
            PackageName, Version, client.Source.Producer.Key,
            archive, TestContext.Current.CancellationToken);
    }

    private string WriteGlobalPackage(string? source)
    {
        string directory = Path.Combine(GlobalRoot, PackageName.ToLowerInvariant(), Version);
        Directory.CreateDirectory(directory);
        using var stream = new MemoryStream(MakeNupkg("global"));
        using var archive = new ZipArchive(stream);
        archive.ExtractToDirectory(directory);
        if (source is not null)
            File.WriteAllText(Path.Combine(directory, ".nupkg.metadata"),
                "{\"source\":" + JsonSerializer.Serialize(source) + "}");
        return directory;
    }

    private static byte[] MakeNupkg(string payload) =>
        SnupkgPdbReaderTests.MakeSnupkg(
            ($"{PackageName}.nuspec", Encoding.UTF8.GetBytes($"""
                <package><metadata><id>{PackageName}</id><version>{Version}</version>
                <authors>Fixture</authors><description>Authority store fixture</description>
                </metadata></package>
                """)),
            ("content/payload.txt", Encoding.UTF8.GetBytes(payload)));

    private static string ReadPayload(IPackageContent content)
    {
        Assert.True(content.TryOpenEntry("content/payload.txt", out Stream? stream));
        using (stream)
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }
}
