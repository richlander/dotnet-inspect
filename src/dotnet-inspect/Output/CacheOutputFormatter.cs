namespace DotnetInspector.Output;

/// <summary>
/// Formats cache command output for display.
/// </summary>
public static class CacheOutputFormatter
{
    public static string FormatCacheInfo(string location, List<(string Name, long Size, int Count)> categories, long totalSize)
    {
        List<string> lines = [];
        lines.Add($"Cache location: {location}");
        lines.Add("");

        if (categories.Count == 0)
        {
            lines.Add("Cache is empty.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var category in categories)
        {
            var label = category.Name == "Packages" ? "packages" : "files";
            lines.Add($"{category.Name + ":",-10}{FormatSize(category.Size)} ({category.Count} {label})");
        }

        lines.Add("");
        lines.Add($"{"Total:",-10}{FormatSize(totalSize)}");
        lines.Add("");
        lines.Add("Run 'dotnet-inspect cache --clean' to clear the cache.");

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatSize(long bytes)
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
