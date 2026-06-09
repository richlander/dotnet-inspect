namespace DotnetInspector.Output;

/// <summary>
/// Formats NuGet package search results for console output.
/// </summary>
public static class PackageSearchOutputFormatter
{
    public static string FormatDownloads(long downloads) => CompactNumberFormatter.FormatCompact(downloads);

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
