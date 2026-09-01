using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Preprocesses command-line arguments before parsing.
/// Handles implicit commands, -NN shorthand expansion, and file path classification.
/// </summary>
public static class ArgumentPreprocessor
{
    /// <summary>
    /// When <c>--lines</c> is active, stores the head line limit carried by
    /// <c>-n N</c> or bare <c>-N</c>.
    /// </summary>
    public static int? HeadLines { get; private set; }

    /// <summary>
    /// When <c>--lines</c> is active with <c>--tail</c> or <c>--tail-lines</c>,
    /// stores the tail line limit carried by <c>-n N</c> or bare <c>-N</c>.
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
            if (args[i] is not ("--head" or "--tail")
                || CommandLineModel.IsLimitShorthand(args[i + 1])
                || !int.TryParse(args[i + 1], out _))
                continue;

            var flag = args[i];
            var count = args[i + 1];
            bool lineMode = args.Take(end).Any(static token =>
                IsLineModeFlagSet(token, "--lines")
                || IsLineModeFlagSet(token, "--tail-lines"));
            var replacement = lineMode
                ? $"-n {count} --lines {flag}"
                : $"-n {count} {flag}";
            error = $"'{flag} {count}' is no longer valid. {flag} now names only the direction; "
                + $"the count comes from -n, and --lines makes it a rendered-line limit. "
                + $"Use '{replacement}'.";
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
        "audit", // removed command, reserved so it is not treated as an implicit package target
        "package", "project", "library", "api", "type", "member", "diff", "timeline", "graph", "find", "vocabulary", "source", "list", "ls", "skill", "demo", "extensions", "implements", "match", "depends", "cache", "workspace-state", "help", "--help", "-h", "-?", "--version", "--flavor"
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
    public static string[] PreprocessArgs(
        string[] args,
        RootCommand rootCommand)
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
        args = RewriteValuedPlatformForSearchCommands(args, rootCommand);

        int firstPositional = FindFirstPositionalIndex(args, rootCommand);
        if (firstPositional >= 0 && !KnownCommands.Contains(args[firstPositional]))
        {
            if (CommandLineHelpers.TryClassifyAsFilePath(
                    args[firstPositional],
                    out var dllPath,
                    out var nupkgPath))
            {
                if (dllPath != null)
                    args = ["library", .. args];
                else if (nupkgPath != null)
                    args = ["package", .. args];
            }
            else
            {
                // The router must select the real command before -NN can be classified.
                // Its action calls PreprocessRoutedArgs on the rewritten command line.
                RequestTelemetry.Breadcrumb(
                    "implicit-router",
                    args[firstPositional]);
                return
                [
                    "router",
                    args[firstPositional],
                    .. args[..firstPositional],
                    .. args[(firstPositional + 1)..]
                ];
            }
        }
        else if (firstPositional < 0
            && args.Any(a => a is "-S" or "--select"))
        {
            RequestTelemetry.Breadcrumb(
                "implicit-router",
                "bare section discovery");
            return ["router", .. args];
        }

        return PreprocessRoutedArgs(args, rootCommand);
    }

    internal static string[] PreprocessRoutedArgs(
        string[] args,
        RootCommand rootCommand)
    {
        Command command = rootCommand.Parse(args).CommandResult.Command;
        int endOfOptions = Array.IndexOf(args, "--");
        if (endOfOptions < 0)
            endOfOptions = args.Length;

        for (int i = 0; i < endOfOptions; i++)
        {
            if (CommandLineModel.IsLimitShorthand(args[i])
                && !IsFollowingRequiredOptionValue(
                    args,
                    i,
                    rootCommand,
                    command))
            {
                args =
                [
                    .. args[..i],
                    "-n",
                    args[i][1..],
                    .. args[(i + 1)..]
                ];
                endOfOptions++;
                i++;
            }
        }

        if (args.Take(endOfOptions).Any(static token =>
                IsLineModeFlagSet(token, "--lines")
                || IsLineModeFlagSet(token, "--tail-lines")))
        {
            CaptureLineWindow(rootCommand.Parse(args), rootCommand);
        }
        return args;
    }

    private static void CaptureLineWindow(
        ParseResult parseResult,
        RootCommand rootCommand)
    {
        if (parseResult.Errors.Count > 0)
            return;

        Command command = parseResult.CommandResult.Command;
        bool linesRequested =
            GetBooleanOptionValue(parseResult, rootCommand, command, "--lines");
        bool tailLinesRequested =
            GetBooleanOptionValue(parseResult, rootCommand, command, "--tail-lines");
        if (!linesRequested && !tailLinesRequested)
            return;

        int? count = CommandLineModel.FindOption(
            rootCommand,
            command,
            "-n") is Option<int?> limitOption
            ? parseResult.GetValue(limitOption)
            : null;
        bool tailRequested =
            tailLinesRequested
            || GetBooleanOptionValue(
                parseResult,
                rootCommand,
                command,
                "--tail");
        if (tailRequested)
        {
            TailLines = count;
            HeadLines = null;
        }
        else
        {
            HeadLines = count;
            TailLines = null;
        }
    }

    private static bool GetBooleanOptionValue(
        ParseResult parseResult,
        RootCommand rootCommand,
        Command command,
        string optionName) =>
        CommandLineModel.FindOption(
            rootCommand,
            command,
            optionName) is Option<bool> option
        && parseResult.GetValue(option);

    private static bool IsFollowingRequiredOptionValue(
        string[] args,
        int index,
        RootCommand rootCommand,
        Command command)
    {
        if (index == 0)
            return false;

        string precedingToken = args[index - 1];
        var (optionName, attachedValue) =
            SplitAttachedOptionValue(precedingToken);
        if (attachedValue is not null)
            return false;

        Option? option = CommandLineModel.FindOption(
            rootCommand,
            command,
            optionName);
        return option?.Arity.MinimumNumberOfValues > 0;
    }

    public static void ApplyParsedLineWindow(ParseResult parseResult)
    {
        HeadLines = null;
        TailLines = null;

        if (parseResult.Errors.Count > 0)
            return;

        bool linesRequested =
            FindOptionResult(parseResult, "--lines")?.GetValueOrDefault<bool>()
                == true;
        bool tailLinesRequested =
            FindOptionResult(parseResult, "--tail-lines")?.GetValueOrDefault<bool>()
                == true;
        if (!linesRequested && !tailLinesRequested)
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

        count ??= FindLineWindowCapturedByOptionalOption(parseResult);
        if (count is null)
            return;

        if (tailLinesRequested
            || FindOptionResult(parseResult, "--tail")?.GetValueOrDefault<bool>()
                == true)
        {
            TailLines = count.Value;
        }
        else
        {
            HeadLines = count.Value;
        }
    }

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
        ParseResult parseResult)
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
                if (TryParseAttachedLineWindow(token.Value, out var count))
                {
                    return count;
                }
            }
        }

        return null;
    }

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

    private static readonly string[] SelectAliases = ["-S", "-s", "--select", "--section"];
    private static readonly string[] ColumnsAliases = ["--columns"];
    private static readonly string[] FieldsAliases = ["--fields"];
    private static readonly string[] PathAliases = ["--path"];
    private static readonly string[] AtCategoryOptionAliases = [.. SelectAliases, "-D", "--discover"];
    private static readonly HashSet<string> SearchScopeCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "find", "implements", "extensions", "depends"
    };
    internal const string EscapedAtCategoryPrefix = "__dotnet_inspect_at__";

    private static string[] RewriteValuedPlatformForSearchCommands(
        string[] args,
        RootCommand rootCommand)
    {
        Command command =
            rootCommand.Parse(args).CommandResult.Command;
        if (!SearchScopeCommands.Contains(command.Name))
            return args;

        int commandIndex = Array.FindIndex(
            args,
            token => string.Equals(
                token,
                command.Name,
                StringComparison.OrdinalIgnoreCase));
        if (commandIndex < 0 || args.Length - commandIndex < 3)
            return args;

        string[]? result = null;
        for (var i = commandIndex + 1; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--platform", StringComparison.Ordinal)
                || args[i + 1].StartsWith("-", StringComparison.Ordinal)
                || bool.TryParse(args[i + 1], out _)
                || !ShouldTreatPlatformFollowerAsLibrary(
                    args,
                    commandIndex,
                    i,
                    rootCommand,
                    command))
            {
                continue;
            }

            result ??= (string[])args.Clone();
            result[i] = CommandLineHelpers.PlatformLibraryOptionName;
        }

        return result ?? args;
    }

    private static int FindFirstPositionalIndex(
        string[] args,
        RootCommand rootCommand)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return i;

            var (optionName, attachedValue) =
                SplitAttachedOptionValue(token);
            if (attachedValue is null
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                && CommandLineModel.FindOptions(rootCommand, optionName)
                    .Any(option => CommandLineModel.CanConsumeFollowingToken(
                        option,
                        args[i + 1])))
            {
                i++;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits a token like <c>--lines=true</c> or <c>-n:5</c> into its option name and
    /// attached value, recognizing both the <c>=</c> and <c>:</c> separators System.CommandLine
    /// accepts. This keeps line-mode detection aligned with command-model lookup.
    /// </summary>
    private static (string Name, string? Value) SplitAttachedOptionValue(string token)
    {
        var separatorIndex = token.IndexOfAny(['=', ':']);
        return separatorIndex < 0
            ? (token, null)
            : (token[..separatorIndex], token[(separatorIndex + 1)..]);
    }

    /// <summary>
    /// True when <paramref name="token"/> sets boolean flag <paramref name="flagName"/> to a
    /// truthy value: bare presence (no attached value) or an explicit <c>=true</c>/<c>:true</c>
    /// value. An explicit <c>=false</c>/<c>:false</c> value is not truthy.
    /// </summary>
    private static bool IsLineModeFlagSet(string token, string flagName)
    {
        var (name, value) = SplitAttachedOptionValue(token);
        if (!string.Equals(name, flagName, StringComparison.Ordinal))
            return false;

        return value is null
            || !bool.TryParse(value, out var parsed)
            || parsed;
    }

    private static bool ShouldTreatPlatformFollowerAsLibrary(
        string[] args,
        int commandIndex,
        int platformIndex,
        RootCommand rootCommand,
        Command command)
    {
        // `find Type --platform System.Text.Json` or `find --tfm net10.0 Type --platform System.Text.Json`.
        if (HasSearchTargetBefore(
                args,
                commandIndex,
                platformIndex,
                rootCommand,
                command))
            return true;

        // `find --platform System.Text.Json JsonSerializer`: first value scopes platform,
        // second non-option remains the command target. A lone `--platform JsonSerializer`
        // preserves the old bare-flag-before-target ordering.
        return HasSearchTargetAfter(
            args,
            platformIndex + 2,
            rootCommand,
            command);
    }

    private static bool HasSearchTargetBefore(
        string[] args,
        int commandIndex,
        int platformIndex,
        RootCommand rootCommand,
        Command command)
    {
        for (var i = commandIndex + 1; i < platformIndex; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return true;

            var (optionName, attachedValue) =
                SplitAttachedOptionValue(token);
            if (attachedValue is null
                && CommandLineModel.FindOption(
                    rootCommand,
                    command,
                    optionName) is { } option
                && i + 1 < platformIndex
                && CommandLineModel.CanConsumeFollowingToken(
                    option,
                    args[i + 1]))
            {
                i++;
            }
        }

        return false;
    }

    private static bool HasSearchTargetAfter(
        string[] args,
        int startIndex,
        RootCommand rootCommand,
        Command command)
    {
        for (var i = startIndex; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return true;

            var (optionName, attachedValue) =
                SplitAttachedOptionValue(token);
            if (attachedValue is null
                && CommandLineModel.FindOption(
                    rootCommand,
                    command,
                    optionName) is { } option
                && i + 1 < args.Length
                && CommandLineModel.CanConsumeFollowingToken(
                    option,
                    args[i + 1]))
            {
                i++;
            }
        }

        return false;
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
