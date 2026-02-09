using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Manages the dotnet-inspect cache.
/// </summary>
public class CacheCommand
{
    public static Task<int> ExecuteAsync(CacheOptions options)
    {
        if (options.Clean)
        {
            return CleanCacheAsync();
        }

        return ShowCacheInfoAsync();
    }

    private static Task<int> ShowCacheInfoAsync()
    {
        var info = PackageCacheService.GetCacheInfo();

        var categories = info.Categories
            .Select(c => (c.Name, c.Size, c.Count))
            .ToList();

        Console.WriteLine(CacheOutputFormatter.FormatCacheInfo(info.Location, categories, info.TotalSize));
        return Task.FromResult(0);
    }

    private static Task<int> CleanCacheAsync()
    {
        try
        {
            long freed = PackageCacheService.ClearCache();
            if (freed == 0)
            {
                Console.WriteLine("Cache is already empty.");
            }
            else
            {
                Console.WriteLine($"Cleared {CacheOutputFormatter.FormatSize(freed)} from cache.");
            }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error clearing cache: {ex.Message}");
            return Task.FromResult(1);
        }
    }

}
