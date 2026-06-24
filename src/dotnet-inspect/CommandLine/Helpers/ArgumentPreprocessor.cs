using DotnetInspector.Core;

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
    /// When --tail N is used, stores the tail line count.
    /// </summary>
    public static int? TailLines { get; private set; }

    /// <summary>
    /// Known/reserved commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "audit", // removed command, reserved so it is not treated as an implicit package target
        "package", "project", "library", "api", "type", "member", "diff", "find", "source", "list", "ls", "skill", "extensions", "implements", "depends", "cache", "help", "--help", "-h", "-?", "--version", "--flavor"
    };

    /// <summary>
    /// Resets the HeadLines value. Used for testing.
    /// </summary>
    internal static void Reset()
    {
        HeadLines = null;
        TailLines = null;
    }

    /// <summary>
    /// Pre-processes args to handle implicit package command and platform framework shorthands.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        // Reset HeadLines for each preprocessing call
        HeadLines = null;
        TailLines = null;

        // These options are single-valued (comma/semicolon-separated), so a natural `-S A -S B`
        // otherwise errors with "expects a single argument". Collapse repeated occurrences into one
        // ';'-joined token so repeated and separated forms behave the same.
        args = MergeRepeatedListOption(args, SelectAliases, "-S");
        args = MergeRepeatedListOption(args, ColumnsAliases, "--columns");
        args = MergeRepeatedListOption(args, FieldsAliases, "--fields");
        args = EscapeAtCategoryOptionValues(args, AtCategoryOptionAliases);
        args = EscapeAtCategoryPathValues(args);

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

        // Set HeadLines for explicit -n N or --head N (so -n 6 behaves like -6)
        if (HeadLines == null)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if ((args[i] == "-n" || args[i] == "--head") && int.TryParse(args[i + 1], out var n))
                {
                    HeadLines = n;
                    break;
                }
            }
        }

        // Set TailLines for --tail N
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--tail" && int.TryParse(args[i + 1], out var tailN))
            {
                TailLines = tailN;
                break;
            }
        }

        // Find the first positional argument, skipping any leading options
        int firstPositional = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                // Skip the value token that follows -n, --head, or --tail (it's a number, not a command)
                if (i > 0 && (args[i - 1] == "-n" || args[i - 1] == "--head" || args[i - 1] == "--tail")) continue;
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
            RequestTelemetry.Breadcrumb("implicit-router", args[firstPositional]);
            return ["router", .. args];
        }

        // Bare discovery flags (-S, --select) with no positional args → route to router
        if (firstPositional < 0 && args.Any(a => a is "-S" or "--select"))
        {
            RequestTelemetry.Breadcrumb("implicit-router", "bare section discovery");
            return ["router", .. args];
        }

        return args;
    }

    private static readonly string[] SelectAliases = ["-S", "-s", "--select", "--section"];
    private static readonly string[] ColumnsAliases = ["--columns"];
    private static readonly string[] FieldsAliases = ["--fields"];
    private static readonly string[] PathAliases = ["--path"];
    private static readonly string[] AtCategoryOptionAliases = [.. SelectAliases, "-D", "--discover"];
    internal const string EscapedAtCategoryPrefix = "__dotnet_inspect_at__";

    // Both escape helpers are copy-on-write: the array is only cloned when a value actually
    // needs escaping (a leading '@'), which is the rare case. This runs for every command
    // before parsing, so the common path returns the original array with no allocation.
    private static string[] EscapeAtCategoryOptionValues(string[] args, string[] aliases)
    {
        string[]? result = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (!IsListOptionAlias(args[i], aliases, out var inlineValue))
                continue;

            if (inlineValue != null)
            {
                var escaped = EscapeAtCategoryValue(inlineValue);
                if (!ReferenceEquals(escaped, inlineValue))
                {
                    result ??= (string[])args.Clone();
                    result[i] = args[i][..args[i].IndexOf('=')] + "=" + escaped;
                }
            }
            else if (i + 1 < args.Length)
            {
                var escaped = EscapeAtCategoryValue(args[i + 1]);
                if (!ReferenceEquals(escaped, args[i + 1]))
                {
                    result ??= (string[])args.Clone();
                    result[i + 1] = escaped;
                }
            }
        }

        return result ?? args;
    }

    private static string[] EscapeAtCategoryPathValues(string[] args)
    {
        string[]? result = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (!IsListOptionAlias(args[i], PathAliases, out var inlineValue))
                continue;

            if (inlineValue != null)
            {
                var escaped = EscapeAtCategoryValue(inlineValue);
                if (!ReferenceEquals(escaped, inlineValue))
                {
                    result ??= (string[])args.Clone();
                    result[i] = args[i][..args[i].IndexOf('=')] + "=" + escaped;
                }
                continue;
            }

            for (var j = i + 1; j < args.Length && !args[j].StartsWith('-'); j++)
            {
                var escaped = EscapeAtCategoryValue(args[j]);
                if (!ReferenceEquals(escaped, args[j]))
                {
                    result ??= (string[])args.Clone();
                    result[j] = escaped;
                }
            }
        }

        return result ?? args;
    }

    private static string EscapeAtCategoryValue(string value)
        => value.StartsWith("@", StringComparison.Ordinal)
            ? EscapedAtCategoryPrefix + value[1..]
            : value;

    /// <summary>
    /// Reverses <see cref="EscapeAtCategoryValue"/>, restoring a leading <c>@</c> that was
    /// escaped to dodge System.CommandLine response-file token processing.
    /// </summary>
    internal static string UnescapeAtCategoryValue(string value)
        => value.StartsWith(EscapedAtCategoryPrefix, StringComparison.Ordinal)
            ? "@" + value[EscapedAtCategoryPrefix.Length..]
            : value;

    /// <summary>
    /// Collapses repeated occurrences of a single-valued list option into one ';'-joined token at the
    /// position of the first occurrence. Handles both `alias value` and `alias=value` forms.
    /// </summary>
    private static string[] MergeRepeatedListOption(string[] args, string[] aliases, string canonical)
    {
        int occurrences = 0;
        foreach (var arg in args)
            if (IsListOptionAlias(arg, aliases, out _)) occurrences++;
        if (occurrences < 2)
            return args;

        var result = new List<string>(args.Length);
        var values = new List<string>();
        int valueSlot = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (IsListOptionAlias(args[i], aliases, out var inlineValue))
            {
                var value = inlineValue ?? (i + 1 < args.Length ? args[++i] : null);
                if (!string.IsNullOrEmpty(value))
                    values.Add(value);

                if (valueSlot < 0)
                {
                    result.Add(canonical);
                    valueSlot = result.Count;
                    result.Add("");
                }
                continue;
            }
            result.Add(args[i]);
        }

        result[valueSlot] = string.Join(';', values);
        return [.. result];
    }

    private static bool IsListOptionAlias(string arg, string[] aliases, out string? inlineValue)
    {
        inlineValue = null;
        foreach (var alias in aliases)
        {
            if (arg == alias)
                return true;
            if (arg.StartsWith(alias + "=", StringComparison.Ordinal))
            {
                inlineValue = arg[(alias.Length + 1)..];
                return true;
            }
        }
        return false;
    }
}
