using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the cache and skill commands.
/// </summary>
public static class UtilityCommandDefinitions
{
    public static Command CreateCacheCommand(SharedOptions opts)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        var cleanOption = new Option<bool>("--clean", "--clear") { Hidden = true };

        cacheCommand.Options.Add(cleanOption);
        cacheCommand.Options.Add(opts.Json);
        cacheCommand.Options.Add(opts.Markdown);
        cacheCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(cacheCommand);
        opts.AddOutputOptionsTo(cacheCommand, supportsRowWindows: false);

        // Subcommand: clear
        var clearCommand = new Command("clear", "Clear the cache");
        var sessionOption = new Option<string?>("--session") { Description = "Clear a named isolated session cache" };
        clearCommand.Options.Add(sessionOption);
        clearCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var session = parseResult.GetValue(sessionOption);
            var options = new CacheOptions(Clean: true, Verbose: false, Session: session);
            return await CacheCommand.ExecuteAsync(options);
        });
        cacheCommand.Subcommands.Add(clearCommand);

        cacheCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var clean = parseResult.GetValue(cleanOption);
            if (clean)
            {
                CommandError.WriteLine("hint: use 'dotnet-inspect cache clear' instead of --clean/--clear");
            }

            var verbosity = OptionParsers.ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            var options = new CacheOptions(
                Clean: clean,
                Verbose: parseResult.GetValue(opts.Verbose) || verbosity >= Verbosity.Detailed,
                Format: opts.ResolveFormat(parseResult),
                NoHeader: parseResult.GetValue(opts.NoHeaders));

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    public static Command CreateSkillCommand(SharedOptions opts)
    {
        var skillCommand = new Command("skill", "Show skill definition (router to focused skills)");
        skillCommand.Options.Add(opts.Limit);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());

        // Subcommand: list (supports the standard output formats)
        var listCommand = new Command("list", "List available focused skills");
        listCommand.Options.Add(opts.Json);
        opts.AddTableOptionsTo(listCommand);
        listCommand.Options.Add(opts.Limit);
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
            focusedCommand.Options.Add(opts.Limit);
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
        var scenarioArg = new Argument<string?>("scenario")
        {
            Description = "Home demo id (omit or 'list' to list demos)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        demoCommand.Arguments.Add(scenarioArg);
        demoCommand.Options.Add(opts.Json);
        demoCommand.Options.Add(opts.Markdown);
        demoCommand.Options.Add(opts.PlainText);
        demoCommand.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(demoCommand);
        demoCommand.Options.Add(opts.Limit);

        var listCommand = new Command("list", "List product home demos");
        listCommand.Options.Add(opts.Json);
        listCommand.Options.Add(opts.Markdown);
        listCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(listCommand);
        listCommand.Options.Add(opts.Limit);
        listCommand.SetAction(parseResult =>
        {
            // Parent-bound flags (e.g. `demo --markdown --mermaid list`) must use the
            // same mermaid gates as the root handler — list previously dropped them.
            if (RejectInvalidDemoMermaidFlags(opts, parseResult) is { } mermaidExit)
                return mermaidExit;

            var format = opts.ResolveFormat(parseResult);
            var noHeader = parseResult.GetValue(opts.NoHeaders);
            var mermaid = parseResult.GetValue(opts.Mermaid);
            return DemoCommand.ExecuteList(format, noHeader, mermaidRequested: mermaid);
        });
        demoCommand.Subcommands.Add(listCommand);

        demoCommand.SetAction(async (parseResult, _) =>
        {
            if (RejectInvalidDemoMermaidFlags(opts, parseResult) is { } mermaidExit)
                return mermaidExit;

            var format = opts.ResolveFormat(parseResult);
            var noHeader = parseResult.GetValue(opts.NoHeaders);
            var embeddedMermaid = opts.IsEmbeddedMermaid(parseResult);
            var mermaid = parseResult.GetValue(opts.Mermaid);
            var scenario = parseResult.GetValue(scenarioArg);
            if (string.IsNullOrWhiteSpace(scenario))
                return DemoCommand.ExecuteList(format, noHeader, mermaidRequested: mermaid);

            return await DemoCommand.ExecuteScenarioAsync(scenario, format, noHeader, embeddedMermaid);
        });

        return demoCommand;
    }

    /// <summary>
    /// Shared mermaid combination gate for root <c>demo</c> and <c>demo list</c>
    /// (parent options can bind before the subcommand token).
    /// </summary>
    private static int? RejectInvalidDemoMermaidFlags(SharedOptions opts, ParseResult parseResult)
    {
        var mermaid = parseResult.GetValue(opts.Mermaid);
        var markdown = parseResult.GetValue(opts.Markdown);
        var json = parseResult.GetValue(opts.Json);
        var plainText = parseResult.GetValue(opts.PlainText);
        var tabular = parseResult.GetValue(opts.Table)
            || parseResult.GetValue(opts.Tsv)
            || parseResult.GetValue(opts.Jsonl);
        if (!DemoCommand.TryValidateMermaidCombinations(
                mermaid, markdown, json, plainText, tabular, out var comboError))
        {
            CommandError.Write(comboError!);
            return 1;
        }

        return null;
    }
}
