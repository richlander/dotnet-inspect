using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal static class OutputModeOptions
{
    public static string CliOption(DiscoveryOutputMode format) =>
        format switch
        {
            DiscoveryOutputMode.Markdown => "--markdown",
            DiscoveryOutputMode.PlainText => "--plaintext",
            DiscoveryOutputMode.Json => "--json",
            DiscoveryOutputMode.Table => "--table",
            DiscoveryOutputMode.Tsv => "--tsv",
            DiscoveryOutputMode.Jsonl => "--jsonl",
            DiscoveryOutputMode.Tree => "--tree",
            DiscoveryOutputMode.Mermaid => "--mermaid",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Unknown output mode."),
        };
}
