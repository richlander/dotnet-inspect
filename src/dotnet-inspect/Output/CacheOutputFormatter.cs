namespace DotnetInspector.Output;

/// <summary>
/// Formats cache sizes for display.
/// </summary>
public static class CacheOutputFormatter
{
    public static string FormatSize(long bytes) => ByteSizeFormatter.FormatBytes(bytes);
}
