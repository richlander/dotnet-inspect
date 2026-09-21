using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines utility commands.
/// </summary>
public static class UtilityCommandDefinitions
{
    public static Command CreateWorkspaceStateCommand()
    {
        var command = new Command(
            "workspace-state",
            "Convert workspace share packets and their JSON shape");
        command.SetAction(_ =>
        {
            HelpWriter.WriteHelp(command);
            return 0;
        });

        Command decode = CreateConversionCommand(
            "decode",
            "Decode canonical base64url workspace state to canonical JSON",
            "packet",
            "Canonical base64url packet, or '-' to read stdin",
            (input, file, _, cancellationToken) =>
                WorkspaceStateCommand.DecodeAsync(input, file, cancellationToken));
        var urlOption = new Option<bool>("--url")
        {
            Description = "Emit a complete https://dotnet-inspect.net/ share URL instead of the packet",
        };
        Command encode = CreateConversionCommand(
            "encode",
            "Encode workspace-state JSON as a canonical base64url packet",
            "json",
            "Workspace-state JSON, or '-' to read stdin",
            (input, file, parseResult, cancellationToken) =>
                WorkspaceStateCommand.EncodeAsync(
                    input,
                    file,
                    cancellationToken,
                    url: parseResult.GetValue(urlOption)));
        encode.Options.Add(urlOption);
        command.Subcommands.Add(decode);
        command.Subcommands.Add(encode);
        return command;
    }

    private static Command CreateConversionCommand(
        string name,
        string description,
        string argumentName,
        string argumentDescription,
        Func<string?, string?, ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new Command(name, description);
        var inputArgument = new Argument<string?>(argumentName)
        {
            Description = argumentDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var fileOption = new Option<string?>("--file")
        {
            Description = "Read input from a UTF-8 file",
        };
        command.Arguments.Add(inputArgument);
        command.Options.Add(fileOption);
        command.SetAction((parseResult, cancellationToken) =>
        {
            string? input = parseResult.GetValue(inputArgument);
            string? file = parseResult.GetValue(fileOption);
            if (input is null && file is null)
            {
                CommandError.Write(
                    $"Provide <{argumentName}>, '-' for stdin, or --file <path>.");
                return Task.FromResult(1);
            }
            if (input is not null && file is not null)
            {
                CommandError.Write(
                    $"<{argumentName}> and --file are alternate input sources; choose one.");
                return Task.FromResult(1);
            }

            return action(
                input,
                file,
                parseResult,
                cancellationToken);
        });
        return command;
    }

    public static Command CreateCacheCommand(SharedOptions opts)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        opts.AddFormatOptionTo(
            cacheCommand,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown,
            CliPresentationFormat.PlainText);
        opts.AddTableOptionsTo(cacheCommand);
        opts.AddOutputOptionsTo(cacheCommand, supportsRowWindows: false);
        CliOptionValueValidation.RegisterPresenceOptions(
            cacheCommand,
            opts.Head,
            opts.Tail);
        cacheCommand.Validators.Add(
            result => ValidateCacheLineDirection(result, opts));

        // Subcommand: clear
        var clearCommand = new Command("clear", "Clear the cache");
        var sessionOption = new Option<string?>("--session") { Description = "Clear a named isolated session cache" };
        clearCommand.Options.Add(sessionOption);
        opts.AddRowWindowValidators(clearCommand, supportsRowWindows: false);
        clearCommand.Validators.Add(
            result => ValidateCacheLineDirection(result, opts));
        clearCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var session = parseResult.GetValue(sessionOption);
            var options = new CacheOptions(Clean: true, Verbose: false, Session: session);
            return await CacheCommand.ExecuteAsync(options);
        });
        cacheCommand.Subcommands.Add(clearCommand);
        opts.AddLineSelectionOptionsTo(clearCommand);

        cacheCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var verbosity = OptionParsers.ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            var options = new CacheOptions(
                Clean: false,
                Verbose: parseResult.GetValue(opts.Verbose) || verbosity >= Verbosity.Detailed,
                Format: opts.ResolveFormat(parseResult),
                NoHeader: parseResult.GetValue(opts.NoHeaders));

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    private static void ValidateCacheLineDirection(
        CommandResult result,
        SharedOptions opts)
    {
        bool head = result.GetValue(opts.Head);
        bool tail = result.GetValue(opts.Tail);
        if (head == tail || result.GetResult(opts.Limit) is not null)
            return;

        result.AddError($"{(head ? "--head" : "--tail")} requires -n.");
    }

    public static Command CreateSkillCommand(SharedOptions opts)
    {
        var skillCommand = new Command("skill", "Show skill definition (router to focused skills)");
        var skillLineLimit = new Option<int?>("-n")
        {
            Description =
                "Select rendered lines",
        };
        opts.AddLineSelectionOptionsTo(
            skillCommand,
            static _ => OutputFormat.Markdown,
            skillLineLimit);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());

        // Subcommand: list (supports the standard output formats)
        var listCommand = new Command("list", "List available focused skills");
        opts.AddFormatOptionTo(
            listCommand,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown);
        opts.AddTableOptionsTo(listCommand);
        opts.AddLineSelectionOptionsTo(
            listCommand,
            limit: skillLineLimit);
        listCommand.SetAction((parseResult) =>
        {
            var format = opts.ResolveFormat(parseResult);
            var noHeader = parseResult.GetValue(opts.NoHeaders);
            return SkillCommand.ExecuteList(format, noHeader);
        });
        skillCommand.Subcommands.Add(listCommand);

        // Subcommand per registered focused skill (e.g. source, performance)
        foreach (var skill in SkillCommand.Skills)
        {
            var name = skill.Name;
            var focusedCommand = new Command(name, skill.Description);
            opts.AddLineSelectionOptionsTo(
                focusedCommand,
                static _ => OutputFormat.Markdown,
                skillLineLimit);
            focusedCommand.SetAction((parseResult) => SkillCommand.ExecuteSkill(name));
            skillCommand.Subcommands.Add(focusedCommand);
        }

        return skillCommand;
    }

    public static Command CreateDemoCommand(SharedOptions opts)
    {
        var demoCommand = new Command(
            DemoCommand.Name,
            "Run a product home inspection demo (real section output)");
        var limitOption = new Option<string[]>("-n")
        {
            Description = "Select the first N demo rows; pair with --tail to select from the end",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var rowsOption = new Option<string[]>("--rows")
        {
            Description = "Select demo rows by one-based inclusive range: N..M, N.., or ..M",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var scenarioArg = new Argument<string?>("scenario")
        {
            Description = "Home demo id (omit or 'list' to list demos)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        demoCommand.Arguments.Add(scenarioArg);
        opts.AddFormatOptionTo(
            demoCommand,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown,
            CliPresentationFormat.PlainText,
            CliPresentationFormat.Mermaid);
        demoCommand.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(demoCommand);
        demoCommand.Options.Add(limitOption);
        demoCommand.Options.Add(rowsOption);
        demoCommand.Options.Add(opts.Head);
        demoCommand.Options.Add(opts.Tail);
        demoCommand.Options.Add(opts.Lines);
        demoCommand.Options.Add(opts.TailLines);
        opts.AddCountOptionTo(demoCommand);
        opts.RegisterLineSelectionFallback(demoCommand, limitOption);

        var listCommand = new Command("list", "List product home demos");
        opts.AddFormatOptionTo(
            listCommand,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown,
            CliPresentationFormat.PlainText);
        opts.AddTableOptionsTo(listCommand);
        listCommand.Options.Add(limitOption);
        listCommand.Options.Add(rowsOption);
        listCommand.Options.Add(opts.Head);
        listCommand.Options.Add(opts.Tail);
        listCommand.Options.Add(opts.Lines);
        listCommand.Options.Add(opts.TailLines);
        opts.AddCountOptionTo(listCommand);

        CliRowSelectionOptionBindings rowBindings =
            new(
                limitOption,
                rowsOption,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines);
        CliRowSelectionCapabilities rowCapabilities =
            CliRowSelectionCapabilities.HeadTail
            | CliRowSelectionCapabilities.Window
            | CliRowSelectionCapabilities.Lines;
        CliRowSelectionCommandRegistry.Register(
            demoCommand,
            rowBindings,
            rowCapabilities,
            result => IsDemoListAdoption(
                result.GetValue(scenarioArg)),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result)
                        && !result.GetValue(opts.Count),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            listCommand,
            rowBindings,
            rowCapabilities,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result)
                        && !result.GetValue(opts.Count),
                    lowering));

        demoCommand.Validators.Add(result =>
        {
            if (IsDemoListMode(
                    result.GetValue(scenarioArg)))
            {
                return;
            }

            if (result.GetResult(rowsOption) is { Implicit: false })
            {
                result.AddError(
                    "--rows is available only when listing demos.");
            }

            if (result.GetValue(opts.Count))
            {
                result.AddError(
                    "--count is available only when listing demos.");
            }

            if (result.GetResult(limitOption)?.Tokens.Count > 1)
            {
                result.AddError(
                    "-n may only be specified once when running a demo.");
            }
            else if (result.GetResult(limitOption) is
                {
                    Implicit: false,
                    Tokens.Count: 1
                } limitResult
                && !int.TryParse(limitResult.Tokens[0].Value, out _))
            {
                result.AddError(
                    $"Cannot parse argument '{limitResult.Tokens[0].Value}' "
                    + "for option '-n' as expected type "
                    + "'System.Nullable`1[System.Int32]'.");
            }
        });

        listCommand.SetAction(parseResult =>
        {
            // Parent-bound flags (e.g. `demo --format markdown --mermaid list`) must use the
            // same mermaid gates as the root handler — list previously dropped them.
            if (RejectInvalidDemoMermaidFlags(opts, parseResult) is { } mermaidExit)
                return mermaidExit;

            if (!TryGetDemoListRowSelection(
                    parseResult,
                    out var rowSelection))
            {
                return 1;
            }

            var format = opts.ResolveFormat(parseResult);
            var noHeader = parseResult.GetValue(opts.NoHeaders);
            var mermaid = opts.IsMermaidOutput(parseResult);
            return DemoCommand.ExecuteList(
                format,
                noHeader,
                mermaidRequested: mermaid,
                rowSelection: rowSelection,
                count: parseResult.GetValue(opts.Count));
        });
        demoCommand.Subcommands.Add(listCommand);

        demoCommand.SetAction(async (parseResult, _) =>
        {
            if (RejectInvalidDemoMermaidFlags(opts, parseResult) is { } mermaidExit)
                return mermaidExit;

            var format = opts.ResolveFormat(parseResult);
            var noHeader = parseResult.GetValue(opts.NoHeaders);
            var embeddedMermaid = opts.IsEmbeddedMermaid(parseResult);
            var mermaid = opts.IsMermaidOutput(parseResult);
            var scenario = parseResult.GetValue(scenarioArg);
            if (IsDemoListMode(scenario))
            {
                if (!TryGetDemoListRowSelection(
                        parseResult,
                        out var rowSelection))
                {
                    return 1;
                }

                return DemoCommand.ExecuteList(
                    format,
                    noHeader,
                    mermaidRequested: mermaid,
                    rowSelection: rowSelection,
                    count: parseResult.GetValue(opts.Count));
            }

            return await DemoCommand.ExecuteScenarioAsync(
                scenario!,
                format,
                noHeader,
                embeddedMermaid);
        });

        return demoCommand;
    }

    private static bool TryGetDemoListRowSelection(
        ParseResult parseResult,
        out RowSelectionIntent<string>? rowSelection)
    {
        if (CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Demo",
                out rowSelection,
                out string? error))
        {
            return true;
        }

        CommandError.Write(error!);
        return false;
    }

    private static bool IsDemoListAdoption(
        string? scenario) =>
        IsDemoListMode(scenario)
        || scenario is not null
            && CliRowSelectionArgumentAdapter
                .IsBareLimitShorthand(scenario);

    private static bool IsDemoListMode(
        string? scenario) =>
        string.IsNullOrWhiteSpace(scenario)
        || string.Equals(
            scenario,
            "list",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shared mermaid combination gate for root <c>demo</c> and <c>demo list</c>
    /// (parent options can bind before the subcommand token).
    /// </summary>
    private static int? RejectInvalidDemoMermaidFlags(SharedOptions opts, ParseResult parseResult)
    {
        var mermaid = opts.IsMermaidOutput(parseResult);
        var markdown = opts.ResolveFormat(parseResult) == OutputFormat.Markdown;
        var json = opts.IsJsonOutput(parseResult);
        var plainText = opts.IsPlainTextOutput(parseResult);
        var tabular = opts.IsTableFlagExplicitlySet(parseResult);
        if (!DemoCommand.TryValidateMermaidCombinations(
                mermaid, markdown, json, plainText, tabular, out var comboError))
        {
            CommandError.Write(comboError!);
            return 1;
        }

        return null;
    }
}
