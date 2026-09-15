using DotnetInspector.Cache;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Locks the tool side of the SourceLink cache dependency-inversion seam (#2579):
/// <see cref="CoreSourceLinkIndexCache"/> must round-trip the engine's index through
/// <see cref="PersistentCache"/> under the expected category. The engine's integration paths
/// bypass tool startup, so this is where the adapter's category/key mapping is exercised.
/// </summary>
[Collection(PersistentCacheCollection.Name)]
public class CoreSourceLinkIndexCacheTests : IDisposable
{
    private const string Category = "sourcelink-index";

    public CoreSourceLinkIndexCacheTests()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
        PersistentCache.Clear(Category);
    }

    public void Dispose() => PersistentCache.Clear(Category);

    [Fact]
    public void RoundTripsIndexThroughPersistentCacheUnderExpectedCategory()
    {
        ISourceLinkIndexCache cache = CoreSourceLinkIndexCache.Instance;
        const string key = "commit-abc123";
        const string content = "{\"N.T\":[\"src/T.cs\"]}";

        Assert.Null(cache.TryGet(key));           // miss before set
        cache.Set(key, content);
        Assert.Equal(content, cache.TryGet(key)); // hit after set
        // Stored under the category the engine's SourceLink index expects.
        Assert.Equal(content, PersistentCache.TryGet(Category, key));
    }
}
