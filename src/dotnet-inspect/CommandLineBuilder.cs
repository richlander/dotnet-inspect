using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.CSharp;

namespace DotnetInspector;

/// <summary>
/// Builds the System.CommandLine command structure.
/// </summary>
public static class CommandLineBuilder
{
    /// <summary>
    /// When the -NN shorthand is used (e.g. -30), stores the line limit.
    /// Delegates to <see cref="ArgumentPreprocessor.HeadLines"/> for backward compatibility.
    /// </summary>
    public static int? HeadLines => ArgumentPreprocessor.HeadLines;

    /// <summary>
    /// When --tail N is used, stores the tail line count.
    /// Delegates to <see cref="ArgumentPreprocessor.TailLines"/> for backward compatibility.
    /// </summary>
    public static int? TailLines => ArgumentPreprocessor.TailLines;

    /// <summary>
    /// Returns whether the parsed route owns <c>-n</c> as a typed item limit rather
    /// than delegating it to the host's rendered-line writer.
    /// </summary>
    public static bool UsesTypedItemLimit(
        System.CommandLine.ParseResult result)
    {
        if (HasParsedOption(result, "--lines")
            || HasParsedOption(result, "--tail-lines"))
        {
            return false;
        }

        return (result.CommandResult.Command.Name
                    == PackageSearchCommand.Name
                && result.CommandResult.Parent is
                    System.CommandLine.Parsing.CommandResult parentCommand
                && parentCommand.Command.Name == PackageCommand.Name)
            || (result.CommandResult.Command.Name
                    == PackageCommand.Name
                && (HasParsedOption(result, "--versions")
                    || HasParsedOption(
                        result,
                        "--versions-with-feed")));
    }

    /// <summary>
    /// Delegates to <see cref="ArgumentPreprocessor.TryGetStaleDirectionFlagError"/>.
    /// </summary>
    public static bool TryGetStaleDirectionFlagError(string[] args, out string? error)
        => ArgumentPreprocessor.TryGetStaleDirectionFlagError(args, out error);

    /// <summary>
    /// Reports stale direction syntax using the active command's count unit.
    /// </summary>
    public static bool TryGetStaleArgumentError(string[] args, out string? error)
        => TryGetStaleArgumentError(
            args,
            CreateRootCommand(),
            out error);

    internal static bool TryGetStaleArgumentError(
        string[] args,
        RootCommand rootCommand,
        out string? error)
    {
        ParseResult rawParse = rootCommand.Parse(args);
        bool isImplicitPackageVersionCandidate =
            ArgumentPreprocessor.IsImplicitPackageCandidate(
                args,
                UsesImplicitVersionDirectionPresence(args, rootCommand));
        string[] ownershipArgs = args;
        ParseResult ownershipParse = rawParse;
        if (isImplicitPackageVersionCandidate)
        {
            ownershipArgs = [PackageCommand.Name, .. args];
            ownershipParse = rootCommand.Parse(ownershipArgs);
        }

        if (CliRowSelectionCommandRegistry.OwnsShortLimit(
                ownershipParse,
                ownershipArgs))
        {
            error = null;
            return false;
        }

        return ArgumentPreprocessor.TryGetStaleDirectionFlagError(ownershipArgs, out error);
    }

    /// <summary>
    /// Known commands for implicit package command detection.
    /// Delegates to <see cref="ArgumentPreprocessor.KnownCommands"/> for backward compatibility.
    /// </summary>
    public static HashSet<string> KnownCommands => ArgumentPreprocessor.KnownCommands;

    // Platform scope constants delegated to ScopeConstants for backward compatibility.
    internal static string[] PlatformFrameworkNames => ScopeConstants.PlatformFrameworks;

    /// <summary>
    /// Pre-processes args and rewrites line-window shorthand only when the active
    /// command parse does not own the token as a required option value.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
        => PreprocessArgs(args, CreateRootCommand());

    internal static string[] PreprocessArgs(
        string[] args,
        RootCommand rootCommand)
    {
        string[] processed = ArgumentPreprocessor.PreprocessArgs(
            args,
            UsesImplicitVersionDirectionPresence(args, rootCommand));
        if (args.FirstOrDefault()?.StartsWith('-') == true
            && processed.FirstOrDefault() == "router")
        {
            string[] packageArguments = [PackageCommand.Name, .. args];
            ParseResult packageParse = rootCommand.Parse(packageArguments);
            if ((HasParsedOption(packageParse, "--versions")
                    || HasParsedOption(packageParse, "--versions-with-feed"))
                && RouterCommandDefinition.IsAcquisitionFreePackageRoute(
                    processed[1..], rootCommand))
            {
                // These lenses already select the package route. Keep the original
                // positional order rather than hoisting a target across its flags.
                return ArgumentPreprocessor.PreprocessArgs(packageArguments);
            }
        }

        ParseResult parseResult = rootCommand.Parse(processed);
        if (processed.FirstOrDefault() == "router"
            || CliRowSelectionCommandRegistry.OwnsShortLimit(
                parseResult,
                processed))
        {
            return processed;
        }

        return ArgumentPreprocessor.RewriteLineWindowShorthand(
            parseResult,
            processed);
    }

    private static bool UsesImplicitVersionDirectionPresence(
        string[] args,
        RootCommand rootCommand)
    {
        if (args.Length == 0
            || !args[0].StartsWith('-')
            || !args.Any(static argument => argument is "--head" or "--tail"))
            return false;

        int firstPositional = ArgumentPreprocessor.FindFirstPositionalArgument(args);
        if (firstPositional >= 0 && KnownCommands.Contains(args[firstPositional]))
            return false;
        if (!ArgumentPreprocessor.IsImplicitPackageCandidate(args, directionPresence: true))
            return false;

        string[] packageArgs = [PackageCommand.Name, .. args];
        return CliRowSelectionCommandRegistry.OwnsShortLimit(
            rootCommand.Parse(packageArgs),
            packageArgs);
    }

    public static void ApplyParsedLineWindow(
        ParseResult parseResult,
        string[]? rawArgs = null)
        => ArgumentPreprocessor.ApplyParsedLineWindow(parseResult, rawArgs);

    public static bool HasParsedOption(ParseResult parseResult, string alias)
        => ArgumentPreprocessor.HasParsedOption(parseResult, alias);

    /// <summary>
    /// Invokes a parsed command under the payload-projection audit. This is the single
    /// invoke choke point: product and test-harness invocation paths pass through it, so a
    /// render path that drops <c>--print</c>/<c>--value</c>/<c>--urls</c>/<c>--paths</c>/
    /// <c>--count</c> fails loudly in tests rather than shipping unprojected output.
    ///
    /// It also owns parse-error rendering. Router rewrites parse a second token
    /// sequence, and
    /// <c>CommandExecutionTests.Router_AttachedEmptyLibraryValue_PreservesBoundedParseError</c>
    /// fails if that second parse escapes to System.CommandLine's help renderer.
    ///
    /// It is also where an escaping exception is turned back into the CLI's error
    /// contract. System.CommandLine's own default handler would otherwise print
    /// <c>Unhandled exception: </c> and the raw exception to stderr at column 0, which
    /// makes it a second, uncontained writer of this stream: an exception message quotes
    /// attacker-reachable text (an <c>--out</c> path, a zip entry name, a nuspec
    /// fragment), so a line terminator in that text forged a diagnostic outright. Turning
    /// the default handler off and catching here rather than only at the entry point
    /// keeps the containment on the path the test harness exercises too.
    /// </summary>
    public static Task<int> InvokeAsync(
        ParseResult parseResult,
        string[]? rawArgs = null)
        => InvokeParsedAsync(
            parseResult,
            rawArgs,
            installLineWindow: false);

    /// <summary>
    /// Invokes a parsed command with the CLI host's rendered-line writer. The entry point
    /// uses this for explicit commands, and the router uses it only after resolving the
    /// authoritative child parse.
    /// </summary>
    public static Task<int> InvokeWithLineWindowAsync(
        ParseResult parseResult,
        string[]? rawArgs = null)
        => InvokeParsedAsync(
            parseResult,
            rawArgs,
            installLineWindow: true);

    private static async Task<int> InvokeParsedAsync(
        ParseResult parseResult,
        string[]? rawArgs,
        bool installLineWindow)
    {
        ArgumentPreprocessor.SetLineWindow(
            headLines: null,
            tailLines: null);
        CliRowSelectionPreparation rowSelection;
        try
        {
            rowSelection = CliRowSelectionCommandRegistry.Prepare(
                parseResult,
                rawArgs);
        }
        catch (OperationCanceledException)
        {
            // Format validation reports its diagnostic before canceling preparation.
            return 1;
        }
        parseResult = rowSelection.ParseResult;

        // The adopted format guard retains its precedence over positional validation.
        if (rowSelection.HasCompatibilityError)
        {
            CommandError.Write(rowSelection.Error!);
            return 1;
        }

        if (CliOptionValueValidation.FindError(
                parseResult,
                rowSelection.Arguments ?? rawArgs
                    ?? [.. parseResult.Tokens.Select(static token => token.Value)],
                rowSelection.PresenceOptions) is { } optionValueError)
        {
            CommandError.Write(optionValueError);
            return 1;
        }

        if (WriteParseErrors(parseResult))
            return 1;

        if (rowSelection.Error is not null)
        {
            CommandError.Write(rowSelection.Error);
            return 1;
        }

        int? headLines = null;
        int? tailLines = null;
        if (rowSelection.Lowering?.LineIntent is { } lineIntent)
        {
            if (lineIntent.Direction
                == CliLineSelectionDirection.Tail)
            {
                tailLines = lineIntent.Count;
            }
            else
            {
                headLines = lineIntent.Count;
            }

            ArgumentPreprocessor.SetLineWindow(
                headLines,
                tailLines);
        }
        else if (!rowSelection.IsActive)
        {
            ApplyParsedLineWindow(parseResult, rawArgs);
            headLines = HeadLines;
            tailLines = TailLines;
        }

        if (!installLineWindow
            && rowSelection.Lowering?.LineIntent is null)
            return await InvokeCoreAsync(parseResult);

        TextWriter originalWriter = Console.Out;
        TailLineLimitingTextWriter? tailWriter = null;
        bool replaceWriter = false;
        if (rowSelection.IsActive
            || !HasParsedOption(parseResult, "--rows")
                && !UsesTypedItemLimit(parseResult))
        {
            if (headLines is int selectedHeadLines)
            {
                Console.SetOut(
                    new LineLimitingTextWriter(
                        originalWriter,
                        selectedHeadLines));
                replaceWriter = true;
            }
            else if (tailLines is int selectedTailLines)
            {
                tailWriter = new TailLineLimitingTextWriter(
                    originalWriter,
                    selectedTailLines);
                Console.SetOut(tailWriter);
                replaceWriter = true;
            }
        }

        try
        {
            return await InvokeCoreAsync(parseResult);
        }
        finally
        {
            if (replaceWriter)
                Console.SetOut(originalWriter);
            tailWriter?.FlushTail();
        }
    }

    private static async Task<int> InvokeCoreAsync(ParseResult parseResult)
    {
        // Two projections cannot both shape one payload, so reject the combination before
        // the command runs rather than letting one of them be discarded.
        if (!ProjectionAudit.ValidateExclusive(parseResult, message => CommandError.Write(message)))
            return 1;

        try
        {
            using var scope = ProjectionAudit.BeginRequest(parseResult);
            return ProjectionAudit.Verify(
                await parseResult.InvokeAsync(ExceptionsReachTheCliErrorContract),
                message => CommandError.Write(message));
        }
        catch (RowWindowValidationException ex)
        {
            // Defensive: the --rows head/tail window is rejected at parse time by the
            // command validator (SharedOptions.AddOutputOptionsTo), so this is not the
            // primary path. Its message names no untrusted subject and needs no stack, so
            // it keeps the plain one-line error contract.
            CommandError.Write(ex);
            return 1;
        }
        catch (DotnetInspector.CommandLine.PrefixResolutionException ex)
        {
            // --package-prefix expansion needs the network, so unlike the row window it
            // cannot be settled at parse time. This is its primary path, not a defensive
            // one, and its message quotes the prefix the user supplied.
            CommandError.Write(ex);
            return 1;
        }
        catch (DotnetInspector.Services.NuspecParseException ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        catch (PackageSourceMappingException ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch (Exception ex)
        {
            CommandError.WriteUnhandled(ex);
            return 1;
        }
    }

    private static bool WriteParseErrors(ParseResult parseResult)
    {
        if (parseResult.Errors.Count == 0)
            return false;

        foreach (var error in parseResult.Errors)
            CommandError.Write(FormatParseError(error.Message));
        return true;
    }

    internal static string FormatParseError(string message)
    {
        if (message.StartsWith("Cannot parse argument '", StringComparison.Ordinal)
            && TryParseCannotParseArgument(
                message,
                out var value,
                out var option,
                out var type))
        {
            var expected = type switch
            {
                "System.Int32" or "System.Nullable`1[System.Int32]" =>
                    "an integer",
                _ => "a valid value",
            };
            return $"Cannot parse value '{Contain(value)}' for option "
                + $"'{Contain(option)}' as {expected}.";
        }

        return message.StartsWith(
                "Error:",
                StringComparison.OrdinalIgnoreCase)
            ? Contain(message["Error:".Length..].TrimStart())
            : Contain(message);

        static string Contain(string text) =>
            CSharpIdentifier.ContainRenderedText(text);
    }

    private static bool TryParseCannotParseArgument(
        string message,
        out string value,
        out string option,
        out string type)
    {
        value = "";
        option = "";
        type = "";
        const string prefix = "Cannot parse argument '";
        int valueStart = prefix.Length;
        int valueEnd = message.IndexOf('\'', valueStart);
        const string middle = " for option '";
        if (valueEnd < 0
            || !message.AsSpan(valueEnd + 1).StartsWith(
                middle,
                StringComparison.Ordinal))
        {
            return false;
        }

        int optionStart = valueEnd + 1 + middle.Length;
        int optionEnd = message.IndexOf('\'', optionStart);
        const string typeMarker = " as expected type '";
        int typeStart = message.IndexOf(
            typeMarker,
            optionEnd + 1,
            StringComparison.Ordinal);
        if (optionEnd < 0 || typeStart < 0)
            return false;

        typeStart += typeMarker.Length;
        int typeEnd = message.IndexOf('\'', typeStart);
        if (typeEnd < 0)
            return false;

        value = message[valueStart..valueEnd];
        option = message[optionStart..optionEnd];
        type = message[typeStart..typeEnd];
        return true;
    }

    // The default handler prints a raw stack trace for every escaping exception, including
    // ones the tool raises deliberately to report a user-facing failure. Disabling it lets
    // those reach the handlers above, which own the `Error:` contract and keep a general
    // handler for genuinely unexpected exceptions.
    private static readonly InvocationConfiguration ExceptionsReachTheCliErrorContract = new()
    {
        EnableDefaultExceptionHandler = false,
    };

    /// <summary>
    /// Creates the root command with all subcommands configured.
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand(
            $"{VersionInfo.ToolName} {VersionInfo.Version} - A CLI tool for inspecting .NET libraries and NuGet packages");

        // Shared options container (defined once, reused across commands)
        var opts = new SharedOptions();

        // Root-level display options (distinct instances so they appear in root help)
        var rootVerbosityOption = new Option<string?>("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };
        rootVerbosityOption.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, OptionParsers.ValidVerbosityValues);
        rootCommand.Options.Add(rootVerbosityOption);
        var rootTipsOption = new Option<string?>("--tips") { Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)", Arity = ArgumentArity.ZeroOrOne };
        rootTipsOption.Aliases.Add("-T");
        rootCommand.Options.Add(rootTipsOption);
        var offlineOption = new Option<bool>("--offline") { Description = "Disable all network access (use cached data only)" };
        rootCommand.Options.Add(offlineOption);
        var traceMermaidOption = new Option<bool>("--trace-mermaid") { Description = "Write a Mermaid request trace diagram to stderr at process exit" };
        rootCommand.Options.Add(traceMermaidOption);
        var httpTimeoutOption = new Option<int?>("--http-timeout") { Description = "Seconds to wait for a network request before giving up (1-3600, default 30)" };
        rootCommand.Options.Add(httpTimeoutOption);

        // Type command (type discovery, compact table)
        rootCommand.Subcommands.Add(ApiCommandDefinitions.CreateTypeCommand(opts, out var typeArgs));

        // Member command (member inspection, docs by default)
        rootCommand.Subcommands.Add(ApiCommandDefinitions.CreateMemberCommand(opts, out var memberArgs));

        // Library command
        rootCommand.Subcommands.Add(InspectionCommandDefinitions.CreateLibraryCommand(opts));

        // Cache command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateCacheCommand(opts));

        // Diff command
        rootCommand.Subcommands.Add(InspectionCommandDefinitions.CreateDiffCommand(opts));
        rootCommand.Subcommands.Add(InspectionCommandDefinitions.CreateTimelineCommand(opts));

        // Inspection graph command
        rootCommand.Subcommands.Add(
            InspectionGraphCommandDefinitions.CreateGraphCommand(opts));

        // Depends command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateDependsCommand(opts));

        // Dependency evidence command (normalized direct declarations, not a traversal)
        rootCommand.Subcommands.Add(
            DependencyEvidenceCommandDefinitions
                .CreateDependencyEvidenceCommand(opts));

        // Extensions command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateExtensionsCommand(opts));

        // Find command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateFindCommand(opts));

        // Product-owned query vocabulary
        rootCommand.Subcommands.Add(VocabularyCommandDefinitions.CreateVocabularyCommand(opts));

        // Implements command
        rootCommand.Subcommands.Add(SearchCommandDefinitions.CreateImplementsCommand(opts));

        // Match command (pairwise structural-clone correspondence)
        rootCommand.Subcommands.Add(MatchCommandDefinitions.CreateMatchCommand(opts));

        // Package command
        rootCommand.Subcommands.Add(PackageCommandDefinitions.CreatePackageCommand(opts, out var packageArgs));

        // Project command
        rootCommand.Subcommands.Add(ProjectCommandDefinitions.CreateProjectCommand(opts));

        // Workspace share packet conversion
        rootCommand.Subcommands.Add(
            UtilityCommandDefinitions.CreateWorkspaceStateCommand());

        // Product-owned runtime Workspace inventory
        rootCommand.Subcommands.Add(
            WorkspaceCommandDefinitions.CreateWorkspaceCommand(opts));

        // Router command (hidden, implicit default for bare names)
        rootCommand.Subcommands.Add(RouterCommandDefinition.Create(rootCommand, opts, typeArgs, memberArgs, packageArgs));

        // Skill command
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateSkillCommand(opts));

        // Product home demos (run closed section presets)
        rootCommand.Subcommands.Add(UtilityCommandDefinitions.CreateDemoCommand(opts));

        // Override S.CL's built-in --help to use our own renderer
        var helpOption = rootCommand.Options.OfType<System.CommandLine.Help.HelpOption>().FirstOrDefault();
        if (helpOption != null)
            helpOption.Action = new HelpOptionAction();

        // No-args: show help + tips (with -v: show CLI tree view)
        rootCommand.SetAction((parseResult) =>
        {
            var hasVerbosity = parseResult.GetResult(rootVerbosityOption) != null;
            var verbosity = ParseVerbosity(parseResult.GetValue(rootVerbosityOption));

            // -v flag present: show CLI tree view (like former `cli` command)
            if (hasVerbosity)
                return CliSchemaCommand.Execute(rootCommand, commandFilter: null, verbosity);

            HelpWriter.WriteHelp(rootCommand);

            var tipLevel = HeadLines != null || TailLines != null
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(rootTipsOption), parseResult.GetResult(rootTipsOption) != null);
            Hints.WriteTips(tipLevel,
                new Tip(PackageCommand.Name, "<package>", "inspect a NuGet package"),
                new Tip("-T:d", "", "show more tips per command"),
                new Tip(TypeCommand.Name, "--package <package>", "discover types in package"),
                new Tip(MemberCommand.Name, "JsonSerializer --package System.Text.Json", "inspect type members"),
                new Tip(FindCommand.Name, "<pattern> --package <package>", "search package types"),
                new Tip(ProjectCommand.Name, "-S Skills", "index package skills for a project"),
                new Tip(FindCommand.Name, "<pattern> --platform", "search platform libraries"));
            return 0;
        });

        QueryDiscoveryCommand.Register(rootCommand, opts);
        return rootCommand;
    }

    // Parse helpers delegated to OptionParsers (for backward compatibility)
    public static Verbosity ParseVerbosity(string? value) => OptionParsers.ParseVerbosity(value);
    public static TipLevel ParseTipLevel(string? value, bool optionPresent) => OptionParsers.ParseTipLevel(value, optionPresent);
    public static HashSet<string>? ParseSectionList(string? value) => OptionParsers.ParseSectionList(value);
    public static NuGetSourceOptions ParseNuGetSourceOptions(
        ParseResult parseResult, Option<string[]> sourceOption,
        Option<string[]> addSourceOption, Option<string?> nugetConfigOption)
        => OptionParsers.ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption);

    /// <summary>
    /// Parses a -t value as either a numeric limit or null (glob patterns are handled separately).
    /// Delegates to <see cref="CommandLineHelpers.ParseTypeLimit"/> for backward compatibility.
    /// </summary>
    internal static int? ParseTypeLimit(string? value) => CommandLineHelpers.ParseTypeLimit(value);

    /// <summary>
    /// Classifies a positional argument by file extension.
    /// Delegates to <see cref="CommandLineHelpers.TryClassifyAsFilePath"/> for backward compatibility.
    /// </summary>
    internal static bool TryClassifyAsFilePath(string? positional, out string? libraryPath, out string? packagePath)
        => CommandLineHelpers.TryClassifyAsFilePath(positional, out libraryPath, out packagePath);

    /// <summary>
    /// Returns true if the value looks like a version number (e.g. "2.0.0", "8.0.0-preview.1").
    /// Delegates to <see cref="CommandLineHelpers.LooksLikeVersionNumber"/> for backward compatibility.
    /// </summary>
    internal static bool LooksLikeVersionNumber(string? value)
        => CommandLineHelpers.LooksLikeVersionNumber(value);
}
