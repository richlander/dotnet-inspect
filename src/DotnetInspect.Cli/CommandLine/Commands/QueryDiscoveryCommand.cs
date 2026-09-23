using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>Routes query metadata before command-specific target acquisition.</summary>
internal static class QueryDiscoveryCommand
{
    internal static void Register(RootCommand root, SharedOptions options)
    {
        foreach (Command command in root.Subcommands.Where(command =>
            command.Name is "library" or "type" or "member" or "package" or "find" or "depends"))
        {
            command.Options.Add(options.QueryHelp);
            WrapAction(command, options);
        }
    }

    private static void WrapAction(Command command, SharedOptions options)
    {
        CommandLineAction? action = command.Action;
        if (action is not null)
        {
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                if (TryExecute(parseResult, options, out int exitCode))
                    return exitCode;
                return action switch
                {
                    AsynchronousCommandLineAction asynchronous =>
                        await asynchronous.InvokeAsync(parseResult, cancellationToken),
                    SynchronousCommandLineAction synchronous => synchronous.Invoke(parseResult),
                    _ => throw new InvalidOperationException($"Command '{command.Name}' has no action."),
                };
            });
        }
        foreach (Command child in command.Subcommands)
            WrapAction(child, options);
    }

    private static bool TryExecute(ParseResult result, SharedOptions options, out int exitCode)
    {
        exitCode = 0;
        string[]? query = options.ParseQueryHelp(result);
        string[]? select = options.ParseSelect(result);
        string[]? discover = options.ParseDiscover(result);
        bool companionSelect = select?.Any(IsCompanionName) == true;
        bool companionDiscover = discover?.Any(IsCompanionName) == true;
        if (query is null && !companionSelect && !companionDiscover)
        {
            return false;
        }

        string command = CommandIdentity(result);
        if (command is not (
                "library"
                or "library query"
                or "type"
                or "member"
                or "package"
                or "package query"
                or "find"
                or "depends"))
        {
            CommandError.Write($"Query discovery is not supported by the '{result.CommandResult.Command.Name}' subcommand.");
            exitCode = 1;
            return true;
        }
        if (SharedParsers.GetStructuralParseError(result) is { } parseError)
        {
            CommandError.Write(parseError);
            exitCode = 1;
            return true;
        }
        if (query is not null)
        {
            foreach (Option option in new Option[]
            {
                options.Select,
                options.Discover,
            })
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    CommandError.Write(
                        $"-Q cannot be combined with {option.Name}; "
                        + "use -Q <section> on its own.");
                    exitCode = 1;
                    return true;
                }
            }
        }
        if (companionDiscover && result.GetValue(options.Count))
        {
            CommandError.Write("Use -Q <section> --count to count query facets, rather than -D.");
            exitCode = 1;
            return true;
        }

        foreach (Option option in new Option[]
        {
            options.Effective, options.Tree, options.Mermaid, options.Raw,
            options.Print, options.Value, options.Urls, options.Paths, options.JsonArray,
            options.Row, options.RowWhere, options.RowOrderBy, options.PerformanceTriageTop,
            options.PerformanceTriageLoop, options.PerformanceTriageMinConfidence,
            options.PerformanceTriageShape,
        })
        {
            if (result.GetResult(option) is { Implicit: false })
            {
                CommandError.Write($"{option.Name} cannot be combined with query discovery; it does not execute a data query.");
                exitCode = 1;
                return true;
            }
        }
        if (command is "package query" or "library query")
        {
            foreach (Option option in result.CommandResult.Command.Options.Where(option =>
                option.Name is "--take" or "--nuspec-only" or "--platform"))
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    CommandError.Write($"{option.Name} cannot be combined with query discovery; it does not execute a data query.");
                    exitCode = 1;
                    return true;
                }
            }
        }
        Option? depthOption =
            CliArgumentOwnership.FindOption(
                result.CommandResult,
                "--depth");
        if (depthOption is not null
            && result.GetResult(depthOption) is { Implicit: false })
        {
            CommandError.Write(
                "--depth cannot be combined with query discovery; "
                + "it does not execute a data query.");
            exitCode = 1;
            return true;
        }
        if (query is not null && result.GetValue(options.Schema))
        {
            CommandError.Write("-Q already describes query capabilities without inspection; --schema is for -D.");
            exitCode = 1;
            return true;
        }

        SectionQueryCatalog catalog = SectionQueryCatalog.Create(command);
        if (companionSelect || companionDiscover)
        {
            string[] selectors = (companionSelect ? select : discover)!;
            if (!selectors.All(IsCompanionName)
                || companionSelect && discover is not null
                || companionDiscover && select is not null)
            {
                CommandError.Write("Query companion sections cannot be mixed with data sections or another discovery mode; use -Q <section>.");
                exitCode = 1;
                return true;
            }
            SelectResult selection = SelectResolver.ResolveSelectAsSections(
                selectors, [.. catalog.KnownSections.Select(name => $"Query: {name}")],
                categories: new Dictionary<string, string[]>());
            if (SelectOutput.WriteUnresolved(selection))
            {
                exitCode = 1;
                return true;
            }
            query = [.. catalog.KnownSections
                .Where(name => selection.Sections!.Contains($"Query: {name}"))];
        }
        exitCode = QueryDiscoverOutput.Execute(
            result,
            options,
            catalog,
            query!,
            companionDiscover,
            command);
        return true;
    }

    private static bool IsCompanionName(string name)
        => name.StartsWith("Query:", StringComparison.OrdinalIgnoreCase);

    private static string CommandIdentity(ParseResult result)
    {
        string name = result.CommandResult.Command.Name;
        if (name == "query"
            && result.CommandResult.Parent is CommandResult parent
            && parent.Command.Name is "package" or "library")
        {
            return $"{parent.Command.Name} query";
        }
        return name;
    }
}
