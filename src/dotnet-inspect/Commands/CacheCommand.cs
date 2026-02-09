using DotnetInspector.Options;
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

        Console.WriteLine($"Cache location: {info.Location}");
        Console.WriteLine();

        if (info.Categories.Count == 0)
        {
            Console.WriteLine("Cache is empty.");
            return Task.FromResult(0);
        }

        foreach (var category in info.Categories)
        {
            Console.WriteLine($"{category.Name + ":",-10}{FormatSize(category.Size)} ({category.Count} {(category.Name == "Packages" ? "packages" : "files")})");
        }

        Console.WriteLine();
        Console.WriteLine($"{"Total:",-10}{FormatSize(info.TotalSize)}");
        Console.WriteLine();
        Console.WriteLine("Run 'dotnet-inspect cache --clean' to clear the cache.");

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
                Console.WriteLine($"Cleared {FormatSize(freed)} from cache.");
            }
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error clearing cache: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }
}
