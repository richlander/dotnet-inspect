using DotnetInspector.Packages;

namespace DotnetInspector.Output;

/// <summary>
/// Formats NuGet package search results for console output.
/// </summary>
public static class PackageSearchOutputFormatter
{
    public static string FormatResults(List<NuGetSearchResult> results, string query)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# NuGet Search: {query}");
        sb.AppendLine();

        // Compute column widths
        int maxId = Math.Max(10, results.Max(r => r.PackageId.Length));
        int maxVer = Math.Max(7, results.Max(r => r.Version.Length));

        // Cap ID column at 50 chars
        maxId = Math.Min(maxId, 50);

        sb.AppendLine($"{"Package".PadRight(maxId)}  {"Version".PadRight(maxVer)}  {"Downloads".PadLeft(12)}  Description");
        sb.AppendLine($"{new string('-', maxId)}  {new string('-', maxVer)}  {new string('-', 12)}  {new string('-', 40)}");

        foreach (var r in results)
        {
            string id = r.PackageId.Length > maxId ? r.PackageId[..(maxId - 1)] + "..." : r.PackageId;
            string downloads = FormatDownloads(r.TotalDownloads);
            string desc = TruncateDescription(r.Description, 60);

            sb.AppendLine($"{id.PadRight(maxId)}  {r.Version.PadRight(maxVer)}  {downloads.PadLeft(12)}  {desc}");
        }

        sb.AppendLine();
        sb.Append($"{results.Count} package(s) found");

        return sb.ToString();
    }

    public static string FormatDownloads(long downloads)
    {
        return downloads switch
        {
            >= 1_000_000_000 => $"{downloads / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{downloads / 1_000_000.0:F1}M",
            >= 1_000 => $"{downloads / 1_000.0:F1}K",
            _ => downloads.ToString()
        };
    }

    public static string TruncateDescription(string? description, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        // Collapse newlines and multiple spaces
        description = description.ReplaceLineEndings(" ").Replace("  ", " ").Trim();

        if (description.Length <= maxLength)
            return description;

        return description[..(maxLength - 1)] + "...";
    }
}
