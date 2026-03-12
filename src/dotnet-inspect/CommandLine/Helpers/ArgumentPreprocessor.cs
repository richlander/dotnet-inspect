namespace DotnetInspector.CommandLine;

/// <summary>
/// Preprocesses command-line arguments before parsing.
/// Handles implicit commands, -NN shorthand expansion, and file path classification.
/// </summary>
public static class ArgumentPreprocessor
{
    /// <summary>
    /// When the -NN shorthand is used (e.g. -30), stores the line limit.
    /// Also set for explicit -n N so both forms behave consistently.
    /// </summary>
    public static int? HeadLines { get; private set; }

    /// <summary>
    /// Known commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "package", "library", "api", "type", "member", "diff", "find", "source", "list", "ls", "llmstxt", "skill", "extensions", "implements", "depends", "cache", "demo", "perf", "perf-test", "help", "--help", "-h", "-?", "--version", "--flavor"
    };

    /// <summary>
    /// Resets the HeadLines value. Used for testing.
    /// </summary>
    internal static void Reset()
    {
        HeadLines = null;
    }

    /// <summary>
    /// Pre-processes args to handle implicit package command and platform framework shorthands.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        // Reset HeadLines for each preprocessing call
        HeadLines = null;

        // Expand -NN shorthand (e.g., -30) into -n 30, like head -30
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Length >= 2 && args[i][0] == '-' && char.IsDigit(args[i][1])
                && int.TryParse(args[i].AsSpan(1), out var headN))
            {
                HeadLines = headN;
                args = [.. args[..i], "-n", args[i][1..], .. args[(i + 1)..]];
                break;
            }
        }

        // Set HeadLines for explicit -n N (so -n 6 behaves like -6)
        if (HeadLines == null)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-n" && int.TryParse(args[i + 1], out var n))
                {
                    HeadLines = n;
                    break;
                }
            }
        }

        // Find the first positional argument, skipping any leading options
        int firstPositional = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                // Skip the value token that follows -n (it's a number, not a command)
                if (i > 0 && args[i - 1] == "-n") continue;
                firstPositional = i;
                break;
            }
        }

        if (firstPositional >= 0 && !KnownCommands.Contains(args[firstPositional]))
        {
            if (CommandLineHelpers.TryClassifyAsFilePath(args[firstPositional], out var dllPath, out var nupkgPath))
            {
                if (dllPath != null) return ["library", .. args];
                if (nupkgPath != null) return ["package", .. args];
            }

            // Route bare names through the router command (platform-preferred, NuGet fallback)
            return ["router", .. args];
        }

        // Bare discovery flags (-S, --select) with no positional args → route to router
        if (firstPositional < 0 && args.Any(a => a is "-S" or "--select"))
            return ["router", .. args];

        return args;
    }
}
