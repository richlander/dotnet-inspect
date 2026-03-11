using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

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
            if (options.Session != null)
                return Task.FromResult(ClearSession(options.Session));
            return Task.FromResult(CleanCache());
        }

        return Task.FromResult(ShowCacheInfo());
    }

    private static int ShowCacheInfo()
    {
        var info = PackageCacheService.GetCacheInfo();

        if (info.Categories.Count == 0)
        {
            Console.WriteLine("Cache is empty.");
            return 0;
        }

        var view = new CacheInfoView
        {
            Location = info.Location,
            Categories = info.Categories.Select(c =>
            {
                var label = c.Name == "Packages" ? "packages" : "files";
                return new CacheCategoryRow(c.Name, CacheOutputFormatter.FormatSize(c.Size), $"{c.Count} {label}");
            }).ToList(),
            Total = CacheOutputFormatter.FormatSize(info.TotalSize)
        };

        MarkoutSerializer.Serialize(view, Console.Out, new PlainTextFormatter(), CacheInfoContext.Default);
        Console.WriteLine("Run 'dotnet-inspect cache clear' to clear the cache.");
        return 0;
    }

    private static int CleanCache()
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
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error clearing cache: {ex.Message}");
            return 1;
        }
    }

    private static int ClearSession(string sessionName)
    {
        var sessionPath = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-{sessionName}");
        if (!Directory.Exists(sessionPath))
        {
            Console.WriteLine($"Session '{sessionName}' not found.");
            return 0;
        }

        try
        {
            var size = new DirectoryInfo(sessionPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            Directory.Delete(sessionPath, recursive: true);
            Console.WriteLine($"Cleared session '{sessionName}' ({CacheOutputFormatter.FormatSize(size)}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error clearing session '{sessionName}': {ex.Message}");
            return 1;
        }
    }

}
