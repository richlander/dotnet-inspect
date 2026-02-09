using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats byte counts as human-readable sizes (e.g., "2.3 MB", "150 KB").
/// </summary>
public class ByteSizeFormatter : IMarkoutValueFormatter<long>
{
    public string Format(long value) => value switch
    {
        >= 1_073_741_824 => $"{value / 1_073_741_824.0:0.#} GB",
        >= 1_048_576 => $"{value / 1_048_576.0:0.#} MB",
        >= 1_024 => $"{value / 1_024.0:0.#} KB",
        _ => $"{value} B"
    };
}

/// <summary>
/// Formats large numbers as compact strings (e.g., "5.1B", "1.2M", "3.4K").
/// </summary>
public class CompactNumberFormatter : IMarkoutValueFormatter<long>
{
    public string Format(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000.0:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000.0:0.#}M",
        >= 1_000 => $"{value / 1_000.0:0.#}K",
        _ => value.ToString()
    };
}
