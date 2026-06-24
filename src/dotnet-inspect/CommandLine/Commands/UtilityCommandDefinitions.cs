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
        opts.AddOutputOptionsTo(cacheCommand);

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
                Console.Error.WriteLine("hint: use 'dotnet-inspect cache clear' instead of --clean/--clear");
            }

            var verbosity = OptionParsers.ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            var options = new CacheOptions(
                Clean: clean,
                Verbose: parseResult.GetValue(opts.Verbose) || verbosity >= Verbosity.Detailed);

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    public static Command CreateSkillCommand(SharedOptions opts)
    {
        var skillCommand = new Command("skill", "Show skill definition");
        skillCommand.Options.Add(opts.Limit);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());
        return skillCommand;
    }
}
