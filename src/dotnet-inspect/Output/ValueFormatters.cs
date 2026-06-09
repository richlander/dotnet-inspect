using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats byte counts as human-readable sizes (e.g., "2.3 MB", "150 KB").
/// </summary>
public class ByteSizeFormatter : IMarkoutValueFormatter<long>
{
    public string Format(long value) => FormatBytes(value);

    /// <summary>
    /// Canonical human-readable byte-size formatter. All size output in the tool routes here so
    /// "2 MB" / "1.2 GB" render identically across commands (cache, library, package views).
    /// </summary>
    public static string FormatBytes(long value) => value switch
    {
        // Powers of 1024; the compiler folds these to constants. Divisors are parenthesized so the
        // whole product runs before the division (a / 1024.0 * 1024 would divide then multiply).
        >= 1024 * 1024 * 1024 => $"{value / (1024.0 * 1024 * 1024):0.#} GB",
        >= 1024 * 1024 => $"{value / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{value / 1024.0:0.#} KB",
        _ => $"{value} B"
    };
}

/// <summary>
/// Renders the shared "Source" column ("source@version", or just "source" when unversioned) used
/// identically by the find and implements commands.
/// </summary>
public static class SourceColumn
{
    public static string Format(string? source, string? version)
        => string.IsNullOrEmpty(version) ? source ?? "" : $"{source}@{version}";
}

/// <summary>
/// Formats large numbers as compact strings (e.g., "5.1B", "1.2M", "3.4K").
/// </summary>
public class CompactNumberFormatter : IMarkoutValueFormatter<long>
{
    public string Format(long value) => FormatCompact(value);

    /// <summary>Canonical compact-number formatter (e.g. download counts). Shared so output matches.</summary>
    public static string FormatCompact(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000.0:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000.0:0.#}M",
        >= 1_000 => $"{value / 1_000.0:0.#}K",
        _ => value.ToString()
    };
}
