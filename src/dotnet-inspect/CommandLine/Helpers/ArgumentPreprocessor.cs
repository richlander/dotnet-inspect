using DotnetInspector.Core;
using System.CommandLine;
using System.CommandLine.Parsing;

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
    /// When --tail is used, stores the line count taken from -n/-NN.
    /// </summary>
    public static int? TailLines { get; private set; }

    /// <summary>
    /// Reports the pre-#3364 spelling <c>--head N</c>/<c>--tail N</c>, where the count
    /// rode on the direction flag. Those flags now name only a direction, so the count
    /// would bind as a positional instead: <c>--tail 20</c> would go looking for a
    /// package named "20" and report that it does not exist. Reporting the stale
    /// spelling directly keeps an old command line from failing at an unrelated task,
    /// or worse, succeeding at one.
    ///
    /// This is a raw-token question -- by the time the parser has bound the count to a
    /// positional there is nothing left to recognize -- so it runs before parsing
    /// rather than as a validator. It lives here, not in the entry point, so that every
    /// host that preprocesses args gets the same answer.
    ///
    /// The scan stops at the <c>--</c> end-of-options separator. After it, <c>--tail</c>
    /// is a literal positional and not a direction flag at all, so the stale spelling
    /// cannot be what the user meant and reporting it would be a false positive.
    /// </summary>
    public static bool TryGetStaleDirectionFlagError(string[] args, out string? error)
    {
        error = null;
        var end = Array.IndexOf(args, "--");
        if (end < 0)
            end = args.Length;

        for (var i = 0; i < end - 1; i++)
        {
            if (args[i] is not ("--head" or "--tail") || !int.TryParse(args[i + 1], out _))
                continue;

            var flag = args[i];
            var count = args[i + 1];
            var rowMode = args.Take(end).Any(static a => a == "--rows" || a.StartsWith("--rows=", StringComparison.Ordinal));
            var replacement = rowMode ? $"--rows {count} {flag}" : $"-n {count} {flag}";
            error = $"'{flag} {count}' is no longer valid. {flag} now names only the direction; "
                + $"the count comes from -n (output lines) or --rows (data rows). Use '{replacement}'.";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Answers raw-token questions that must be resolved before parsing: spellings the product
    /// used to accept and no longer does. The parser can only say "Unrecognized option", which
    /// is true but leaves the caller to find the replacement themselves.
    /// </summary>
    public static bool TryGetStaleArgumentError(string[] args, out string? error)
        => TryGetStaleDirectionFlagError(args, out error);

    /// <summary>
    /// The replacement guidance for a package option this product removed, or <c>null</c> when the
    /// token names something the command never had.
    ///
    /// Unlike the stale direction flag -- which parses cleanly and so has to be caught before
    /// parsing -- a removed option is something the parser itself rejects, so this answers the
    /// command's own unrecognized-option outcome rather than scanning raw tokens. That is what
    /// keeps a run that parses from being second-guessed: in <c>--out --readme</c> the token is an
    /// output file name and never reaches here, and a bare name that routes to a library or
    /// platform assembly gets that command's answer rather than package-specific advice.
    /// </summary>
    public static string? GetRemovedPackageOptionError(string option)
    {
        // --readme was a boolean option, so the parser also accepted --readme=true. Both spellings
        // named the removed flag and both deserve the replacement.
        if (!option.Equals("--readme", StringComparison.Ordinal)
            && !option.StartsWith("--readme=", StringComparison.Ordinal))
        {
            return null;
        }

        // package --readme was removed: printing a document is a projection over a selected
        // section rather than a lens of its own, so a flag naming one document competed with the
        // section selection for the same question. Scoped to the package command because
        // project --readme <package-id> is a different option that still exists.
        return "'--readme' is no longer valid. Printing a document is a projection over a "
            + "selected section: use '-S \"Package README file\" --print' for one package, "
            + "or '--content --path @readme' to survey several.";
    }

    /// <summary>
    /// Known/reserved commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "audit", // removed commands, reserved so they are not treated as implicit package targets
        "package", "project", "library", "type", "member", "diff", "timeline", "graph", "find", "vocabulary", "source", "list", "ls", "skill", "demo", "extensions", "implements", "match", "depends", "cache", "workspace", "workspace-state", "help", "--help", "-h", "-?", "--version", "--flavor"
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
        // System.CommandLine otherwise parses `--columns=` like a bare `--columns`; split the
        // inline-empty spelling first so projection validation can distinguish the explicit value.
        args = ExpandInlineEmptyListOption(args, ColumnsAliases);
        args = ExpandInlineEmptyListOption(args, FieldsAliases);
        args = MergeRepeatedListOption(args, SelectAliases, "-S");
        args = MergeRepeatedListOption(args, ColumnsAliases, "--columns");
        args = MergeRepeatedListOption(args, FieldsAliases, "--fields");
        args = EscapeAtCategoryOptionValues(args, AtCategoryOptionAliases);
        args = EscapeAtCategoryPathValues(args);
        args = RewriteValuedPlatformForSearchCommands(args);

        // Find the first positional argument, skipping any leading options
        int firstPositional = -1;
        for (int i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith('-'))
            {
                firstPositional = i;
                break;
            }

            var optionName = token.Split('=', 2)[0];
            if (TrySkipSeparatedDirectionValue(args, ref i, args.Length))
            {
                continue;
            }

            if (OptionsWithFollowingValue.Contains(optionName)
                && !token.Contains('=', StringComparison.Ordinal)
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                i++;
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
            return ["router", args[firstPositional], .. args[..firstPositional], .. args[(firstPositional + 1)..]];
        }

        // Bare discovery flags (-S, --select) with no positional args → route to router
        if (firstPositional < 0 && args.Any(a => a is "-S" or "--select"))
        {
            RequestTelemetry.Breadcrumb("implicit-router", "bare section discovery");
            return ["router", .. args];
        }

        return args;
    }

    internal static string[] RewriteLineWindowShorthand(
        ParseResult parseResult,
        string[] args)
    {
        var rewritten = new List<string>(args.Length);
        bool changed = false;
        for (var i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token == "--")
            {
                rewritten.AddRange(args[i..]);
                break;
            }

            if (token.Length >= 2
                && token[0] == '-'
                && char.IsDigit(token[1])
                && int.TryParse(token.AsSpan(1), out _)
                && !IsClaimedByRequiredOption(parseResult, args, i))
            {
                rewritten.Add("-n");
                rewritten.Add(token[1..]);
                changed = true;
                continue;
            }

            rewritten.Add(token);
        }

        return changed ? [.. rewritten] : args;
    }

    public static void ApplyParsedLineWindow(
        ParseResult parseResult,
        IReadOnlyList<string>? rawArgs = null)
    {
        HeadLines = null;
        TailLines = null;

        if (parseResult.Errors.Count > 0)
            return;

        int? count = null;
        OptionResult? limit = FindOptionResult(parseResult, "-n");
        if (limit is
            {
                Implicit: false,
                Tokens.Count: > 0,
            }
            && int.TryParse(limit.Tokens[^1].Value, out var parsedCount)
            && !IsClaimedByPrecedingRequiredOption(parseResult, limit))
        {
            count = parsedCount;
        }

        count ??= FindLineWindowCapturedByOptionalOption(parseResult, rawArgs);
        if (count is null)
            return;

        if (FindOptionResult(parseResult, "--tail")?.GetValueOrDefault<bool>() == true)
            TailLines = count.Value;
        else
            HeadLines = count.Value;
    }

    public static bool HasParsedOption(ParseResult parseResult, string alias)
        => FindOptionResult(parseResult, alias) is { Implicit: false };

    private static OptionResult? FindOptionResult(
        ParseResult parseResult,
        string alias)
    {
        foreach (OptionResult option in GetOptionResults(parseResult))
        {
            if (option.Option.Name == alias
                || option.Option.Aliases.Contains(alias))
            {
                return option;
            }
        }

        return null;
    }

    private static IEnumerable<OptionResult> GetOptionResults(
        ParseResult parseResult)
    {
        for (SymbolResult? scope = parseResult.CommandResult;
            scope is not null;
            scope = scope.Parent)
        {
            if (scope is not CommandResult command)
                continue;

            foreach (OptionResult option in command.Children.OfType<OptionResult>())
                yield return option;
        }
    }

    private static bool IsClaimedByPrecedingRequiredOption(
        ParseResult parseResult,
        OptionResult limit)
    {
        IReadOnlyList<Token> tokens = parseResult.Tokens;
        for (var i = 1; i < tokens.Count; i++)
        {
            if (!Equals(tokens[i], limit.IdentifierToken))
                continue;

            Token preceding = tokens[i - 1];
            return GetOptionResults(parseResult).Any(
                option => Equals(option.IdentifierToken, preceding)
                    && option.Option.Arity.MinimumNumberOfValues > 0);
        }

        return false;
    }

    private static int? FindLineWindowCapturedByOptionalOption(
        ParseResult parseResult,
        IReadOnlyList<string>? rawArgs)
    {
        foreach (OptionResult option in GetOptionResults(parseResult))
        {
            string? identifier = option.IdentifierToken?.Value;
            if (option.Option.Arity.MinimumNumberOfValues != 0
                || identifier?.Contains('=', StringComparison.Ordinal) == true
                || identifier?.Contains(':', StringComparison.Ordinal) == true)
            {
                continue;
            }

            foreach (Token token in option.Tokens)
            {
                if (!IsInlineOptionValue(rawArgs, option.Option, token.Value)
                    && TryParseAttachedLineWindow(token.Value, out var count))
                {
                    return count;
                }
            }
        }

        return null;
    }

    private static bool IsInlineOptionValue(
        IReadOnlyList<string>? rawArgs,
        Option option,
        string value)
    {
        if (rawArgs is null)
            return false;

        foreach (string alias in option.Aliases.Append(option.Name))
        {
            if (rawArgs.Any(arg => IsInlineOptionValue(arg, alias, value)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInlineOptionValue(
        string argument,
        string alias,
        string value)
        => argument.Equals($"{alias}={value}", StringComparison.Ordinal)
            || argument.Equals($"{alias}:{value}", StringComparison.Ordinal)
            || alias is ['-', not '-']
                && argument.Equals($"{alias}{value}", StringComparison.Ordinal);

    private static bool TryParseAttachedLineWindow(
        string token,
        out int count)
    {
        count = 0;
        if (!token.StartsWith("-n", StringComparison.Ordinal)
            || token.Length <= 2)
        {
            return false;
        }

        ReadOnlySpan<char> value = token.AsSpan(2);
        if (value[0] is '=' or ':')
            value = value[1..];
        return int.TryParse(value, out count);
    }

    private static bool IsClaimedByRequiredOption(
        ParseResult parseResult,
        IReadOnlyList<string> args,
        int index)
    {
        string value = args[index];
        int occurrence = 0;
        for (var i = 0; i <= index; i++)
        {
            string token = args[i];
            if (token.Equals(value, StringComparison.Ordinal))
            {
                occurrence++;
                continue;
            }

            if (GetOptionResults(parseResult).Any(
                option => option.Tokens.Any(
                        optionToken => optionToken.Value.Equals(
                            value,
                            StringComparison.Ordinal))
                    && option.Option.Aliases
                        .Append(option.Option.Name)
                        .Any(alias => IsInlineOptionValue(
                            token,
                            alias,
                            value))))
            {
                occurrence++;
            }
        }

        Token? candidate = parseResult.Tokens
            .Where(token => token.Value.Equals(value, StringComparison.Ordinal))
            .Skip(occurrence - 1)
            .FirstOrDefault();
        return candidate is not null
            && GetOptionResults(parseResult).Any(
                option => option.Option.Arity.MinimumNumberOfValues > 0
                    && option.Tokens.Any(token => ReferenceEquals(token, candidate)));
    }

    private static readonly string[] SelectAliases = ["-S", "-s", "--select", "--section"];
    private static readonly string[] ColumnsAliases = ["--columns"];
    private static readonly string[] FieldsAliases = ["--fields"];
    private static readonly string[] PathAliases = ["--path"];
    private static readonly HashSet<string> OptionsWithOptionalFollowingValue =
        new(
            [
                "-v", "-T", "--tips",
                "-S", "-s", "--select", "--section",
                "-D", "--discover", "--columns", "--fields",
            ],
            StringComparer.Ordinal);
    private static readonly HashSet<string> PackageOptionsWithOptionalFollowingValue =
        new(
            ["--path", "--library", "--version", "--versions", "--versions-with-feed"],
            StringComparer.Ordinal);
    private static readonly string[] AtCategoryOptionAliases = [.. SelectAliases, "-D", "--discover"];
    private static readonly HashSet<string> SearchScopeCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "find", "implements", "extensions", "depends"
    };
    private static readonly HashSet<string> OptionsWithFollowingValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "--package", "--library", "--assembly", "--project", "--bin", "--directory",
        "--platform", CommandLineHelpers.PlatformLibraryOptionName, "--framework", "--tfm",
        "-t", "--type", "-m", "--member", "-k", "--kind", "--index",
        "--caller-package", "--caller-project", "--match", "--path",
        "--il-offset", "--il-offsets", "--heap", "--extract-resources", "--version", "--versions", "--versions-with-feed",
        "--out", "--output", "-o", "--take", "--row", "--where", "--order-by",
        "--min-confidence", "--triage-shape", "--top", "--session",
        "--package-prefix", "--depth", "-n", "--rows", "--source",
        "--add-source", "--nugetconfig", "--columns", "--fields", "-v", "-T",
        "--tips", "-S", "-s", "--select", "--section", "-D", "--discover",
        "--at", "--file", "--finding", "--readme", "--relationship", "--repo"
    };
    internal const string EscapedAtCategoryPrefix = "__dotnet_inspect_at__";

    private static string[] RewriteValuedPlatformForSearchCommands(string[] args)
    {
        var commandIndex = FindSearchScopeCommandIndex(args);
        if (commandIndex < 0 || args.Length - commandIndex < 3)
            return args;

        string[]? result = null;
        for (var i = commandIndex + 1; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--platform", StringComparison.Ordinal)
                || args[i + 1].StartsWith("-", StringComparison.Ordinal)
                || !ShouldTreatPlatformFollowerAsLibrary(args, commandIndex, i))
            {
                continue;
            }

            result ??= (string[])args.Clone();
            result[i] = CommandLineHelpers.PlatformLibraryOptionName;
        }

        return result ?? args;
    }

    private static int FindSearchScopeCommandIndex(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return SearchScopeCommands.Contains(token) ? i : -1;

            if (TrySkipSeparatedDirectionValue(args, ref i, args.Length))
                continue;

            var optionName = token.Split('=', 2)[0];
            if (OptionsWithFollowingValue.Contains(optionName)
                && !token.Contains('=', StringComparison.Ordinal)
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                i++;
            }
        }

        return -1;
    }

    private static bool ShouldTreatPlatformFollowerAsLibrary(string[] args, int commandIndex, int platformIndex)
    {
        // `find Type --platform System.Text.Json` or `find --tfm net10.0 Type --platform System.Text.Json`.
        if (HasSearchTargetBefore(args, commandIndex, platformIndex))
            return true;

        // `find --platform System.Text.Json JsonSerializer`: first value scopes platform,
        // second non-option remains the command target. A lone `--platform JsonSerializer`
        // preserves the old bare-flag-before-target ordering.
        return HasSearchTargetAfter(args, platformIndex + 2);
    }

    private static bool HasSearchTargetBefore(string[] args, int commandIndex, int platformIndex)
    {
        for (var i = commandIndex + 1; i < platformIndex; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return true;

            if (TrySkipSeparatedDirectionValue(args, ref i, platformIndex))
                continue;

            var optionName = token.Split('=', 2)[0];
            if (OptionsWithFollowingValue.Contains(optionName)
                && !token.Contains('=', StringComparison.Ordinal)
                && i + 1 < platformIndex
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                i++;
            }
        }

        return false;
    }

    private static bool HasSearchTargetAfter(string[] args, int startIndex)
    {
        for (var i = startIndex; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return true;

            if (TrySkipSeparatedDirectionValue(args, ref i, args.Length))
                continue;

            var optionName = token.Split('=', 2)[0];
            if (OptionsWithFollowingValue.Contains(optionName)
                && !token.Contains('=', StringComparison.Ordinal)
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                i++;
            }
        }

        return false;
    }

    private static bool TrySkipSeparatedDirectionValue(
        string[] args,
        ref int index,
        int end)
    {
        if (args[index] is not ("--head" or "--tail")
            || index + 1 >= end
            || !bool.TryParse(args[index + 1], out _))
        {
            return false;
        }

        index++;
        return true;
    }

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
        {
            if (arg == "--")
                break;
            if (IsListOptionAlias(arg, aliases, out _)) occurrences++;
        }
        if (occurrences < 2)
            return args;

        var result = new List<string>(args.Length);
        var values = new List<string>();
        bool hasExplicitValue = false;
        int valueSlot = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                result.AddRange(args[i..]);
                break;
            }

            if (IsListOptionAlias(args[i], aliases, out var inlineValue))
            {
                var value = inlineValue;
                if (value is null
                    && i + 1 < args.Length
                    && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    value = args[++i];
                }

                hasExplicitValue |= value is not null;
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

        if (hasExplicitValue)
            result[valueSlot] = string.Join(';', values);
        else
            result.RemoveAt(valueSlot);
        return [.. result];
    }

    private static string[] ExpandInlineEmptyListOption(string[] args, string[] aliases)
    {
        List<string>? result = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                result?.AddRange(args[i..]);
                break;
            }

            if (IsListOptionAlias(args[i], aliases, out var inlineValue) && inlineValue == "")
            {
                result ??= [.. args[..i]];
                result.Add(args[i][..^1]);
                result.Add("");
            }
            else
            {
                result?.Add(args[i]);
            }
        }

        return result is null ? args : [.. result];
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
