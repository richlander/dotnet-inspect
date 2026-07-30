using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public class ResponseCacheTests
{
    private ResponseCache CreateCache(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"nf-rc-{Guid.NewGuid():N}");
        return new ResponseCache("nugetfetch-test", dir);
    }

    [Fact]
    public void Set_ThenGet_ReturnsValue()
    {
        var cache = CreateCache(out var dir);
        try
        {
            cache.Set("cat", "key1", """{"hello":"world"}""");
            Assert.Equal("""{"hello":"world"}""", cache.TryGet("cat", "key1"));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public void TryGet_NonExistent_ReturnsNull()
    {
        var cache = CreateCache(out var dir);
        try
        {
            Assert.Null(cache.TryGet("cat", "missing"));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public void TryGet_WithTtl_RecentEntry_ReturnsValue()
    {
        var cache = CreateCache(out var dir);
        try
        {
            cache.Set("cat", "key1", "data");
            Assert.NotNull(cache.TryGet("cat", "key1", TimeSpan.FromMinutes(5)));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public void SetBytes_ThenGetBytes()
    {
        var cache = CreateCache(out var dir);
        try
        {
            byte[] data = [1, 2, 3, 4, 5];
            cache.SetBytes("bin", "key1", data);
            byte[]? result = cache.TryGetBytes("bin", "key1");
            Assert.NotNull(result);
            Assert.Equal(data, result);
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public void Clear_RemovesCategory()
    {
        var cache = CreateCache(out var dir);
        try
        {
            cache.Set("cat1", "key1", "data1");
            cache.Set("cat2", "key2", "data2");

            long freed = cache.Clear("cat1");
            Assert.True(freed > 0);
            Assert.Null(cache.TryGet("cat1", "key1"));
            Assert.Equal("data2", cache.TryGet("cat2", "key2"));
        }
        finally { CleanUp(dir); }
    }

    [Fact]
    public void GetCacheInfo_ReturnsStats()
    {
        var cache = CreateCache(out var dir);
        try
        {
            cache.Set("cat", "key1", "data");
            var info = cache.GetCacheInfo();
            Assert.True(info.SizeBytes > 0);
            Assert.True(info.FileCount > 0);
        }
        finally { CleanUp(dir); }
    }

    private static void CleanUp(string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
