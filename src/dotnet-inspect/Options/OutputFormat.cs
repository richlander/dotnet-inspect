namespace DotnetInspector.Options;

/// <summary>
/// Output format for command results.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Columnar one-line-per-row output (default). Like git log --oneline or docker images.
    /// </summary>
    OneLine,

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
    Json
}

/// <summary>
/// Resolves the effective output format from CLI flags, environment, and defaults.
/// Precedence: explicit CLI flags → DOTNET_INSPECT_FORMAT env → default (OneLine).
/// </summary>
public static class OutputFormatResolver
{
    private static OutputFormat? _envOverride;
    private static bool _envParsed;

    /// <summary>
    /// Resolves format. Any -v flag implies Markdown. --json implies Json. --markdown implies Markdown.
    /// OneLine is the default when no -v flag is specified.
    /// </summary>
    public static OutputFormat Resolve(bool jsonFlag, bool markdownFlag, Verbosity? verbosity, bool plainTextFlag = false)
    {
        // Explicit CLI flags win
        if (jsonFlag)
            return OutputFormat.Json;
        if (markdownFlag)
            return OutputFormat.Markdown;
        if (plainTextFlag)
            return OutputFormat.PlainText;
        if (verbosity != null)
            return OutputFormat.Markdown;

        // Environment variable
        if (GetEnvOverride() is OutputFormat envFormat)
            return envFormat;

        // Default
        return OutputFormat.OneLine;
    }

    /// <summary>
    /// Warns when --oneline is combined with a verbosity that produces multiple sections
    /// without a section selector. Oneline can only render one table at a time.
    /// </summary>
    public static bool WarnIfOneLineDetailMismatch(bool oneLine, Verbosity verbosity, HashSet<string>? includeSections)
    {
        if (oneLine && verbosity >= Verbosity.Normal && includeSections == null)
        {
            Console.Error.WriteLine("--oneline shows one table at a time. Use -s <section> to select a section, or --markdown for the full view.");
            return true;
        }
        return false;
    }

    private static OutputFormat? GetEnvOverride()
    {
        if (!_envParsed)
        {
            var value = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
            _envOverride = value?.ToLowerInvariant() switch
            {
                "oneline" or "one-line" => OutputFormat.OneLine,
                "markdown" or "md" => OutputFormat.Markdown,
                "plaintext" or "plain-text" or "plain" or "text" => OutputFormat.PlainText,
                "json" => OutputFormat.Json,
                _ => null
            };
            _envParsed = true;
        }
        return _envOverride;
    }
}
