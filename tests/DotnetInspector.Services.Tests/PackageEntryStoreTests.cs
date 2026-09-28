using DotnetInspector.Cache;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// The durable entry cache stores untrusted archive paths only as data:
/// every entry is published under the digest of its path, inside its cache
/// family (docs/design/package-cache-policy.md, case 5e).
/// </summary>
[Collection(ManifestPathEnvironmentCollection.Name)]
public sealed class PackageEntryStoreTests
{
    [Fact]
    public void EntryCache_HostileEntryNames_StayContainedAndDistinct()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"entry-store-{Guid.NewGuid():N}");
        NuGetCache.Initialize("dotnet-inspect-test", root, skipNuGetCache: true);
        try
        {
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize(
                    [new PackageSource("feed", "https://feed.example/v3/index.json")]);
            ConfiguredPackageAuthority authority =
                Assert.Single(authorization.Authorities);
            using IPackageSourceClient client = PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association);
            IPackageEntryStore store = new AuthorityScopedFileSystemPackageStore(
                authority,
                client.Source.Producer,
                () => Path.Combine(root, "temporary"));
            Assert.True(store.KeepsEntries);

            (string Path, byte[] Content)[] hostile =
            [
                ("../../escaped.dll", [1]),
                ("/rooted/abs.dll", [2]),
                ("C:\\windows\\drive.dll", [3]),
                ("lib/net8.0/Case.dll", [4]),
                ("lib/net8.0/case.dll", [5]),
            ];
            foreach ((string path, byte[] content) in hostile)
                store.PublishEntry("contoso", "1.0.0", path, content);

            foreach ((string path, byte[] content) in hostile)
            {
                Assert.True(store.TryReadEntry("contoso", "1.0.0", path, out byte[] read));
                Assert.Equal(content, read);
            }

            string family = PersistentCache.GetCategoryPath("package-authority-entries-v1");
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Assert.Equal(hostile.Length, files.Length);
            Assert.All(files, file =>
            {
                Assert.StartsWith(family, file, StringComparison.Ordinal);
                Assert.Matches("^[0-9a-f]{64}$", Path.GetFileName(file));
            });
        }
        finally
        {
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// An authority with a configured credential has no persistent key, so
    /// it keeps no entries (docs/design/package-cache-policy.md, case 5d).
    /// </summary>
    [Fact]
    public void EntryCache_AuthorityWithoutPersistentKey_KeepsNothing()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"entry-store-{Guid.NewGuid():N}");
        NuGetCache.Initialize("dotnet-inspect-test", root, skipNuGetCache: true);
        try
        {
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize(
                    [new PackageSource(
                        "feed",
                        "https://feed.example/v3/index.json",
                        new PackageSourceCredential("user", "pass"))]);
            ConfiguredPackageAuthority authority =
                Assert.Single(authorization.Authorities);
            Assert.Null(authority.PersistentCacheKey);
            using IPackageSourceClient client = PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association);
            IPackageEntryStore store = new AuthorityScopedFileSystemPackageStore(
                authority,
                client.Source.Producer,
                () => Path.Combine(root, "temporary"));

            Assert.False(store.KeepsEntries);
            store.PublishDirectory("contoso", "1.0.0", new byte[] { 1 }, 1);
            store.PublishEntry("contoso", "1.0.0", "lib/net8.0/Contoso.dll", new byte[] { 1 });

            Assert.False(store.TryReadDirectory("contoso", "1.0.0", out _, out _));
            Assert.False(store.TryReadEntry("contoso", "1.0.0", "lib/net8.0/Contoso.dll", out _));
            Assert.Empty(Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                : []);
        }
        finally
        {
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
