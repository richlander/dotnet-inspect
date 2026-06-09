namespace DotnetInspector.Options;

/// <summary>
/// Output format for command results.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Pretty one-row-per-result table output with space-padded columns.
    /// </summary>
    Table,

    /// <summary>
    /// Normalized tab-separated values: one physical line per row, one tab-delimited field per column.
    /// </summary>
    Tsv,

    /// <summary>
    /// JSON Lines: one JSON object per row, with stable property names.
    /// </summary>
    Jsonl,

    /// <summary>
    /// Rich markdown with headings, tables, and field lists.
    /// Implied when any -v:* verbosity flag is specified.
    /// </summary>
    Markdown,

    /// <summary>
    /// Plain text without markdown syntax. Aligned fields, dashed table separators.
    /// </summary>
    PlainText,

    /// <summary>
    /// JSON output.
    /// </summary>
    Json,

    /// <summary>
    /// Standalone Mermaid diagram syntax (graph TD, classDiagram, etc.).
    /// Only works for commands that produce graph/tree data.
    /// </summary>
    Mermaid
}

/// <summary>
/// Resolves the effective output format from CLI flags, environment, and defaults.
/// Precedence: explicit CLI flags → DOTNET_INSPECT_FORMAT env → default.
/// </summary>
public static class OutputFormatResolver
{
    private static OutputFormat? _envOverride;
    private static bool _envParsed;

    /// <summary>
    /// Resolves format. Any -v flag implies Markdown. --json implies Json. --markdown implies Markdown.
    /// Commands may supply a <paramref name="defaultFormat"/> to override the global default (Markdown).
    /// </summary>
    public static OutputFormat Resolve(
        bool jsonFlag,
        bool markdownFlag,
        Verbosity? verbosity,
        bool plainTextFlag = false,
        bool mermaidFlag = false,
        bool tableFlag = false,
        bool tsvFlag = false,
        bool jsonlFlag = false,
        OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        // Explicit CLI flags win
        if (jsonFlag)
            return OutputFormat.Json;
        if (mermaidFlag && !markdownFlag)
            return OutputFormat.Mermaid;
        if (markdownFlag)
            return OutputFormat.Markdown;
        if (plainTextFlag)
            return OutputFormat.PlainText;
        if (jsonlFlag)
            return OutputFormat.Jsonl;
        if (tsvFlag)
            return OutputFormat.Tsv;
        if (tableFlag)
            return OutputFormat.Table;
        if (verbosity != null)
            return OutputFormat.Markdown;

        // Environment variable
        if (GetEnvOverride() is OutputFormat envFormat)
            return envFormat;

        // Command-specific or global default
        return defaultFormat;
    }

    /// <summary>
    /// Returns true when --mermaid is combined with --markdown (embedded mermaid mode).
    /// In this mode, the output is still markdown but tree/graph sections render as mermaid code blocks.
    /// </summary>
    public static bool IsEmbeddedMermaid(bool markdownFlag, bool mermaidFlag)
        => markdownFlag && mermaidFlag;

    /// <summary>
    /// Warns when a tabular format is combined with a verbosity that produces multiple sections
    /// without a section selector. Tabular formats can only render one table at a time.
    /// </summary>
    public static bool WarnIfOneLineDetailMismatch(bool tabular, Verbosity verbosity, HashSet<string>? includeSections)
    {
        if (tabular && verbosity >= Verbosity.Normal && includeSections == null)
        {
            Console.Error.WriteLine("--table, --tsv, and --jsonl show one table at a time. Use -S <section> to select a section, or --markdown/--json for the full view.");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Validates that explicit table/TSV/JSONL output targets at most one selected section.
    /// Markdown and JSON can represent multi-section documents; table/TSV/JSONL are one table at a time.
    /// </summary>
    public static bool ValidateSingleSectionForTabular(bool tabularExplicitlySet, IReadOnlyCollection<string>? includeSections)
    {
        if (!tabularExplicitlySet || includeSections is not { Count: > 1 })
            return true;

        Console.Error.WriteLine($"Error: Selection matches {includeSections.Count} sections: {string.Join(", ", includeSections)}.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("--table, --tsv, and --jsonl display one section at a time.");
        Console.Error.WriteLine("Use -S with a specific section name, or --markdown/--json for multi-section output.");
        return false;
    }

    private static OutputFormat? GetEnvOverride()
    {
        if (!_envParsed)
        {
            var value = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
            _envOverride = value?.ToLowerInvariant() switch
            {
                "table" or "oneline" or "one-line" => OutputFormat.Table,
                "tsv" => OutputFormat.Tsv,
                "jsonl" or "json-lines" => OutputFormat.Jsonl,
                "markdown" or "md" => OutputFormat.Markdown,
                "plaintext" or "plain-text" or "plain" or "text" => OutputFormat.PlainText,
                "json" => OutputFormat.Json,
                "mermaid" => OutputFormat.Mermaid,
                _ => null
            };
            _envParsed = true;
        }
        return _envOverride;
    }
}
