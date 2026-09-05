using System.CommandLine;
using System.CommandLine.Invocation;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>Routes query metadata before command-specific target acquisition.</summary>
internal static class QueryDiscoveryCommand
{
    internal static void Register(RootCommand root, SharedOptions options)
    {
        foreach (Command command in root.Subcommands.Where(command =>
            command.Name is "library" or "type" or "member" or "package" or "find"))
        {
            command.Options.Add(options.QueryHelp);
            command.Validators.Add(result =>
            {
                if (result.GetResult(options.QueryHelp) is not { Implicit: false })
                    return;
                foreach (Option option in new Option[] { options.Select, options.Discover })
                {
                    if (result.GetResult(option) is { Implicit: false })
                        result.AddError($"-Q cannot be combined with {option.Name}; use -Q <section> on its own.");
                }
            });
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
            return false;

        if (result.CommandResult.Command.Name is not ("library" or "type" or "member" or "package" or "find"))
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
        if (companionDiscover && result.GetValue(options.Count))
        {
            CommandError.Write("Use -Q <section> --count to count query facets, rather than -D.");
            exitCode = 1;
            return true;
        }

        foreach (Option option in new Option[]
        {
            options.Effective, options.Tree, options.Mermaid, options.Bare,
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
        if (query is not null && result.GetValue(options.Schema))
        {
            CommandError.Write("-Q already describes query capabilities without inspection; --schema is for -D.");
            exitCode = 1;
            return true;
        }

        SectionQueryCatalog catalog = SectionQueryCatalog.Create(result.CommandResult.Command.Name);
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
            result, options, catalog, query!, companionDiscover);
        return true;
    }

    private static bool IsCompanionName(string name)
        => name.StartsWith("Query:", StringComparison.OrdinalIgnoreCase);
}
