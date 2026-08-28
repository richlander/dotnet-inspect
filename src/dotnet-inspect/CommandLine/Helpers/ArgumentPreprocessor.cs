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
            if (args[i] is not ("--head" or "--tail") || !int.TryParse(args[i + 1], out _))
                continue;

            var flag = args[i];
            var count = args[i + 1];
            var lineMode = args.Take(end).Any(static a => a is "--lines" or "--tail-lines");
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

        var endOfOptions = Array.IndexOf(args, "--");
        if (endOfOptions < 0)
            endOfOptions = args.Length;

        // `--platform` is a required-value option (a library name) for type/member/match/
        // assembly commands, but a value-less bool flag for search-scope commands (find,
        // implements, extensions, depends) -- see CommandLineHelpers.CreatePlatformOption.
        // RewriteValuedPlatformForSearchCommands (above) already rewrites any search-scope
        // `--platform <library>` into `--platform-library <library>`, so by this point any
        // remaining literal `--platform` in a search-scope command is guaranteed to be the
        // bool-flag form and must not be treated as consuming the following token.
        //
        // `--library` and `--version` have the same command-dependent duality on `package`:
        // both are ArgumentArity.ZeroOrOne there ("use alone" selects the primary library /
        // shows the resolved version), but are required-value everywhere else they appear
        // (e.g., `--library` on search-scope commands, `--version` on `library`'s platform
        // runtime-version option). A bare -N following either on `package` must expand to
        // `-n N`, not be swallowed as the option's value.
        int commandTokenIndex = FindFirstCommandTokenIndex(args);
        string? commandToken = commandTokenIndex >= 0 ? args[commandTokenIndex] : null;
        bool platformIsValueless = commandToken != null && SearchScopeCommands.Contains(commandToken);
        bool isPackageCommand = commandToken != null
            && string.Equals(commandToken, PackageCommand.Name, StringComparison.OrdinalIgnoreCase);

        // The implicit-router form (`dotnet-inspect System.Text.Json --version -2`, no
        // explicit `package` keyword) also resolves to the `package` command at runtime --
        // RouterCommandDefinition.RewriteAsync routes any bare, non-file-path target with
        // `--library` or a version query (`--version`/`--versions`/`--versions-with-feed`/
        // `--latest-version`) to `package`, unless a more specific source (`--package`,
        // `--platform`, `--project`) or type/member selector is also present. Mirror only
        // that narrow, deterministic default-fallback shape here; anything with a more
        // specific selector keeps the conservative (required-value) classification, since
        // the full router decision (generic notation, library-file resolution, etc.) is not
        // safely predictable from this synchronous preprocessing step.
        //
        // Two exceptions to "conservative" that are still fully deterministic from the raw
        // tokens: an `.nupkg` target routes straight to `package` (only `.dll` routes to
        // `library`), and a redundant, self-referential `--package <same target>` also routes
        // to `package` (RewriteAsync's `IsExplicitSourceIdentity` check) -- but only when no
        // `--type`/`--member` selector AND no second positional token is also present, since
        // `TryRouteExplicitSourceTarget` checks those before falling back to the self-
        // referential-identity branch and routes to `type`/`member` instead (a second
        // positional is itself the deferred type/member target once the redundant `--package
        // <target>` pair is set aside -- see `TryFindPositionalIndex`). The target token need
        // not sit at index 0 -- a leading global option (e.g. `--tips`) can precede it -- so
        // this keys off the resolved `commandToken` itself, matching how
        // `platformIsValueless`/the explicit-`package`-command check above already work.
        //
        // A target with explicit generic notation (e.g. `List<T>`) never routes to `package`
        // from this fallback shape -- RewriteAsync's `hasExplicitApiSource` branch (driven by
        // any `--library <value>` reaching it unexpanded) takes it to `type`/`member` before the
        // final `ContainsOption(tokens, "--library") => package` catch-all is reached. Expanding
        // its `--library`'s value here would misroute it the same way the self-referential-
        // package/type-selector case above does.
        bool isDllTarget = commandToken != null
            && CommandLineHelpers.TryClassifyAsFilePath(commandToken, out var routedDllPath, out _)
            && routedDllPath != null;
        bool hasTypeOrMemberSelector = commandToken != null
            && ContainsAnyOption(args, "-t", "--type", "-m", "--member");
        bool hasExplicitGenericTarget = commandToken != null
            && TypeMatcher.HasExplicitGenericNotation(commandToken);
        bool hasSelfReferentialPackageOption = commandToken != null
            && !hasTypeOrMemberSelector
            && !HasSecondPositionalToken(args, commandTokenIndex)
            && HasOptionValueEqualTo(args, "--package", commandToken);
        if (!isPackageCommand && commandToken != null
            && !KnownCommands.Contains(commandToken)
            && !isDllTarget
            && !hasExplicitGenericTarget
            && (hasSelfReferentialPackageOption
                || !ContainsAnyOption(args, "--package", "--platform", "--project", "-t", "--type", "-m", "--member"))
            && ContainsAnyOption(args, "--library", "--version", "--versions", "--versions-with-feed", "--latest-version"))
        {
            isPackageCommand = true;
        }

        var valuelessOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (platformIsValueless)
            valuelessOverrides.Add("--platform");
        if (isPackageCommand)
        {
            valuelessOverrides.Add("--library");
            valuelessOverrides.Add("--version");
        }

        // Expand every bare -NN shorthand (e.g., -30) into -n 30 before parsing.
        for (int i = 0; i < endOfOptions; i++)
        {
            if (args[i].Length >= 2 && args[i][0] == '-' && char.IsDigit(args[i][1])
                && !IsFollowingRequiredOptionValue(args, i, valuelessOverrides)
                && int.TryParse(args[i].AsSpan(1), out var headN))
            {
                args = [.. args[..i], "-n", args[i][1..], .. args[(i + 1)..]];
                endOfOptions++;
                i++;
            }
        }

        bool lineModeRequested = args.Take(endOfOptions)
            .Any(static a => IsLineModeFlagSet(a, "--lines") || IsLineModeFlagSet(a, "--tail-lines"));
        if (lineModeRequested)
        {
            int? count = null;
            for (int i = 0; i < endOfOptions; i++)
            {
                var (name, attachedValue) = SplitAttachedOptionValue(args[i]);
                if (!string.Equals(name, "-n", StringComparison.Ordinal))
                    continue;

                if (attachedValue != null)
                {
                    if (int.TryParse(attachedValue, out var inline) && inline > 0)
                        count = inline;
                    break;
                }

                if (i + 1 < endOfOptions
                    && int.TryParse(args[i + 1], out var separate)
                    && separate > 0)
                {
                    count = separate;
                }
                break;
            }

            bool tailLinesRequested = args.Take(endOfOptions)
                .Any(static a => IsLineModeFlagSet(a, "--tail") || IsLineModeFlagSet(a, "--tail-lines"));
            if (tailLinesRequested)
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

    private static bool IsFollowingRequiredOptionValue(string[] args, int index, HashSet<string> valuelessOverrides)
    {
        if (index == 0)
            return false;

        string precedingToken = args[index - 1];
        string optionName = precedingToken.Split('=', 2)[0];
        if (valuelessOverrides.Contains(optionName))
            return false;
        return !precedingToken.Contains('=', StringComparison.Ordinal)
            && RequiredValueOptions.Contains(optionName);
    }

    private static bool ContainsAnyOption(string[] args, params string[] optionNames)
    {
        foreach (var arg in args)
        {
            var optionName = arg.Split('=', 2)[0];
            foreach (var candidate in optionNames)
            {
                if (string.Equals(optionName, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Mirrors <c>RouterCommandDefinition.IsExplicitSourceIdentity</c>: true when <paramref
    /// name="optionName"/> appears with a value equal to <paramref name="value"/> (the
    /// redundant, self-referential "--package X" spelling of a bare target "X").
    /// </summary>
    private static bool HasOptionValueEqualTo(string[] args, string optionName, string value)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[i + 1], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var inline = args[i].Split('=', 2);
            if (inline.Length == 2
                && string.Equals(inline[0], optionName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(inline[1], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        "--tips", "-S", "-s", "--select", "--section", "-D", "--discover"
    };
    private static readonly HashSet<string> RequiredValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--package", "--library", "--assembly", "--project", "--bin", "--directory",
        "--platform", CommandLineHelpers.PlatformLibraryOptionName, "--framework", "--tfm",
        "-t", "--type", "-m", "--member", "-k", "--kind", "--index",
        "--caller-package", "--caller-project", "--match", "--path",
        "--il-offset", "--il-offsets", "--heap", "--extract-resources", "--take", "--row",
        "--where", "--order-by", "--min-confidence", "--triage-shape", "--top", "--session",
        "--package-prefix", "--depth", "-n", "--rows", "--source",
        "--add-source", "--nugetconfig", "--columns", "--fields", "--version"
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

    /// <summary>
    /// Finds the index of the first positional (non-option) token in <paramref name="args"/>,
    /// which is the command name for every supported invocation shape. Unlike
    /// <see cref="FindSearchScopeCommandIndex"/>, this does not filter by command membership --
    /// callers compare the returned token against whichever command set is relevant to them.
    /// </summary>
    private static int FindFirstCommandTokenIndex(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return i;

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

    /// <summary>
    /// True when a second positional (non-option) token exists after <paramref
    /// name="firstIndex"/>, mirroring how <c>TryFindPositionalIndex</c> detects a deferred
    /// type/member target once a redundant "--package &lt;target&gt;" pair is set aside.
    /// </summary>
    private static bool HasSecondPositionalToken(string[] args, int firstIndex)
    {
        for (var i = firstIndex + 1; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return true;

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

    /// <summary>
    /// Splits a token like <c>--lines=true</c> or <c>-n:5</c> into its option name and
    /// attached value, recognizing both the <c>=</c> and <c>:</c> separators System.CommandLine
    /// accepts (this file's other helpers only recognize <c>=</c>, which is what caused
    /// <c>--lines=true</c>/<c>--tail-lines=true</c>/<c>-n:5</c> to silently miss line-mode
    /// detection -- see Round 15).
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
