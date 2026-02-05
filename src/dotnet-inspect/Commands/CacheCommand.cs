using DotnetInspector.Packages;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Manages the dotnet-inspect cache.
/// </summary>
public class CacheCommand
{
    public static Task<int> ExecuteAsync(CacheOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        var basePath = NuGetCache.GetAppCacheBasePath();
        
        if (options.Clean)
        {
            return CleanCacheAsync(basePath, logger);
        }

        return ShowCacheInfoAsync(basePath, logger);
    }

    private static Task<int> ShowCacheInfoAsync(string basePath, VerboseLogger logger)
    {
        Console.WriteLine($"Cache location: {basePath}");
        Console.WriteLine();

        if (!Directory.Exists(basePath))
        {
            Console.WriteLine("Cache is empty.");
            return Task.FromResult(0);
        }

        var packagePath = Path.Combine(basePath, "packages");
        var sourcePath = Path.Combine(basePath, "sources");
        var symbolPath = Path.Combine(basePath, "symbols");

        long totalSize = 0;

        if (Directory.Exists(packagePath))
        {
            var (size, count) = GetDirectoryStats(packagePath);
            totalSize += size;
            Console.WriteLine($"Packages: {FormatSize(size)} ({count} packages)");
        }

        if (Directory.Exists(sourcePath))
        {
            var (size, count) = GetDirectoryStats(sourcePath);
            totalSize += size;
            Console.WriteLine($"Sources:  {FormatSize(size)} ({count} files)");
        }

        if (Directory.Exists(symbolPath))
        {
            var (size, count) = GetDirectoryStats(symbolPath);
            totalSize += size;
            Console.WriteLine($"Symbols:  {FormatSize(size)} ({count} files)");
        }

        Console.WriteLine();
        Console.WriteLine($"Total:    {FormatSize(totalSize)}");
        Console.WriteLine();
        Console.WriteLine("Run 'dotnet-inspect cache --clean' to clear the cache.");

        return Task.FromResult(0);
    }

    private static Task<int> CleanCacheAsync(string basePath, VerboseLogger logger)
    {
        if (!Directory.Exists(basePath))
        {
            Console.WriteLine("Cache is already empty.");
            return Task.FromResult(0);
        }

        var (size, _) = GetDirectoryStats(basePath);

        try
        {
            Directory.Delete(basePath, recursive: true);
            Console.WriteLine($"Cleared {FormatSize(size)} from cache.");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error clearing cache: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static (long size, int count) GetDirectoryStats(string path)
    {
        if (!Directory.Exists(path))
            return (0, 0);

        long size = 0;
        int count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch
                {
                    // Skip files we can't access
                }
            }
        }
        catch
        {
            // Skip directories we can't access
        }

        return (size, count);
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
