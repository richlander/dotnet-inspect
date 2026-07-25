using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class PackageStoreTests : IDisposable
{
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), $"dotnet-inspect-pkgstore-{Guid.NewGuid():N}");

    private static readonly byte[] DllBytes = [0x4D, 0x5A, 0x90, 0x00, 0x01, 0x02];

    public PackageStoreTests()
    {
        NuGetCache.Initialize("dotnet-inspect-test", _cacheDir, skipNuGetCache: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    [Fact]
    public async Task InMemoryPackageStore_CommitThenGet_RoundTripsAndReadsEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryPackageStore();
        var nupkg = MakeNupkg("Example");

        Assert.Null(store.TryGetCached("Foo.Bar", "2.0.0"));

        IPackageContent content;
        using (var stream = new MemoryStream(nupkg))
            content = await store.CommitAsync("Foo.Bar", "2.0.0", stream, ct);

        // In-memory content is never materialized on disk.
        Assert.Null(content.RootPath);
        Assert.Null(content.NupkgPath);
        Assert.True(content.FromCache);

        Assert.True(content.TryOpenEntry("lib/net8.0/Example.dll", out var dll));
        using (dll)
        {
            using var buffer = new MemoryStream();
            await dll.CopyToAsync(buffer, ct);
            Assert.Equal(DllBytes, buffer.ToArray());
        }

        Assert.False(content.TryOpenEntry("lib/net8.0/Missing.dll", out _));

        var entries = content.EnumerateEntries().ToList();
        Assert.Contains("Example.nuspec", entries);
        Assert.Contains("lib/net8.0/Example.dll", entries);

        // Lookup is case-insensitive on name and version.
        Assert.NotNull(store.TryGetCached("foo.bar", "2.0.0"));
        Assert.NotNull(store.TryGetCached("Foo.Bar", "2.0.0"));
    }

    [Fact]
    public async Task InMemoryPackageStore_TryGetLatestCachedVersion_IgnoresPrerelease()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryPackageStore();

        foreach (var version in new[] { "2.0.0", "2.1.0", "3.0.0-beta" })
        {
            using var stream = new MemoryStream(MakeNupkg("Example"));
            await store.CommitAsync("Foo.Bar", version, stream, ct);
        }

        Assert.Equal("2.1.0", store.TryGetLatestCachedVersion("Foo.Bar"));
        Assert.Null(store.TryGetLatestCachedVersion("Unknown.Package"));
    }

    [Fact]
    public async Task FileSystemPackageStore_CommitThenGet_MaterializesAndCaches()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FileSystemPackageStore();
        var nupkg = MakeNupkg("example.package");

        Assert.Null(store.TryGetCached("example.package", "1.0.0"));

        IPackageContent committed;
        using (var stream = new MemoryStream(nupkg))
            committed = await store.CommitAsync("example.package", "1.0.0", stream, ct);

        Assert.NotNull(committed.RootPath);
        Assert.True(Directory.Exists(committed.RootPath));
        Assert.True(File.Exists(Path.Combine(committed.RootPath!, "example.package.nuspec")));
        Assert.True(File.Exists(Path.Combine(committed.RootPath!, "lib", "net8.0", "example.package.dll")));
        Assert.NotNull(committed.NupkgPath);
        Assert.True(File.Exists(committed.NupkgPath));

        // Entries are readable through the host-neutral surface too.
        Assert.True(committed.TryOpenEntry("lib/net8.0/example.package.dll", out var dll));
        dll.Dispose();

        // A second lookup hits the committed cache entry at the same path.
        var cached = store.TryGetCached("example.package", "1.0.0");
        Assert.NotNull(cached);
        Assert.Equal(committed.RootPath, cached!.RootPath);
        Assert.True(cached.FromCache);

        Assert.Equal("1.0.0", store.TryGetLatestCachedVersion("example.package"));
    }

    [Theory]
    [InlineData("../victim", "1.0.0")]
    [InlineData("Foo.Bar", "../1.0.0")]
    [InlineData("Foo/Bar", "1.0.0")]
    [InlineData("", "1.0.0")]
    [InlineData("Foo.Bar", "")]
    [InlineData("   ", "1.0.0")]
    [InlineData("C:Foo", "1.0.0")]
    public async Task FileSystemPackageStore_CommitAsync_RejectsUnsafeCoordinates(
        string packageName, string version)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FileSystemPackageStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using var stream = new MemoryStream(MakeNupkg("example.package"));
            await store.CommitAsync(packageName, version, stream, ct);
        });
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("Foo.Bar", "  ")]
    public async Task InMemoryPackageStore_CommitAsync_RejectsEmptyCoordinates(
        string packageName, string version)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryPackageStore();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using var stream = new MemoryStream(MakeNupkg("example.package"));
            await store.CommitAsync(packageName, version, stream, ct);
        });
    }

    [Fact]
    public async Task InMemoryPackageContent_TryOpenEntry_IsCaseInsensitive()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryPackageStore();

        IPackageContent content;
        using (var stream = new MemoryStream(MakeNupkg("Example")))
            content = await store.CommitAsync("Foo.Bar", "1.0.0", stream, ct);

        // Entry stored as lib/net8.0/Example.dll; a differently-cased request
        // must still resolve, mirroring the desktop filesystem.
        Assert.True(content.TryOpenEntry("LIB/NET8.0/example.DLL", out var dll));
        dll.Dispose();
    }

    [Fact]
    public async Task FileSystemPackageContent_TryOpenEntry_TraversalPath_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FileSystemPackageStore();

        IPackageContent content;
        using (var stream = new MemoryStream(MakeNupkg("example.package")))
            content = await store.CommitAsync("example.package", "1.0.0", stream, ct);

        Assert.Throws<ArgumentException>(() =>
            content.TryOpenEntry("../escape.dll", out _));
        Assert.Throws<ArgumentException>(() =>
            content.TryOpenEntry("C:/escape.dll", out _));
    }

    private static byte[] MakeNupkg(string assemblyName)
        => SnupkgPdbReaderTests.MakeSnupkg(
            ($"{assemblyName}.nuspec", "<package />"u8.ToArray()),
            ($"lib/net8.0/{assemblyName}.dll", DllBytes));
}
