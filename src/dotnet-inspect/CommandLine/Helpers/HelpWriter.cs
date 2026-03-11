using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Renders help output for CLI commands using Markout's PlainTextFormatter,
/// replacing System.CommandLine's built-in HelpAction.
/// </summary>
public static class HelpWriter
{
    public static void WriteHelp(Command command)
    {
        WriteHelp(command, Console.Out);
    }

    public static void WriteHelp(Command command, TextWriter writer)
    {
        var view = BuildHelpView(command);
        MarkoutSerializer.Serialize(view, writer, new PlainTextFormatter(), HelpViewContext.Default);
    }

    internal static HelpView BuildHelpView(Command command)
    {
        var view = new HelpView();

        // Description
        if (!string.IsNullOrEmpty(command.Description))
            view.Description = [command.Description];

        // Usage
        view.Usage = [BuildUsageLine(command)];

        // Arguments
        var args = command.Arguments.Where(a => !a.Hidden).ToList();
        if (args.Count > 0)
            view.Arguments = FormatEntries(args.Select(a =>
                ($"<{a.Name}>", a.Description ?? "")));

        // Options (help/version last, matching S.CL convention)
        var options = command.Options
            .Where(o => !o.Hidden)
            .OrderBy(o => o is HelpOption || o is VersionOption ? 1 : 0)
            .ToList();
        if (options.Count > 0)
            view.Options = FormatEntries(options.Select(FormatOptionEntry));

        // Subcommands (excluding hidden, preserve insertion order)
        var subcommands = command.Subcommands.Where(c => !c.Hidden).ToList();
        if (subcommands.Count > 0)
        {
            view.Commands = FormatEntries(subcommands.Select(c =>
            {
                var name = c.Name;
                var visibleArgs = c.Arguments.Where(a => !a.Hidden).ToList();
                if (visibleArgs.Count > 0)
                    name += " " + string.Join(" ", visibleArgs.Select(a => $"<{a.Name}>"));
                return (name, c.Description ?? "");
            }));
        }

        return view;
    }

    private static List<string> FormatEntries(IEnumerable<(string Name, string Description)> entries)
    {
        var list = entries.ToList();
        int maxWidth = list.Max(e => e.Name.Length);
        return list.Select(e =>
            string.IsNullOrEmpty(e.Description)
                ? e.Name
                : $"{e.Name.PadRight(maxWidth)}  {e.Description}").ToList();
    }

    private static string BuildUsageLine(Command command)
    {
        var parts = new List<string>();

        // Build command path
        var path = new List<string>();
        var current = command;
        while (current != null)
        {
            if (current is RootCommand root)
                path.Insert(0, root.Name.Length > 0 ? root.Name : "dotnet-inspect");
            else
                path.Insert(0, current.Name);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }
        parts.Add(string.Join(" ", path));

        // Arguments
        foreach (var arg in command.Arguments.Where(a => !a.Hidden))
        {
            var arity = arg.Arity;
            var name = $"<{arg.Name}>";
            if (arity.MaximumNumberOfValues > 1)
                name += "...";
            if (arity.MinimumNumberOfValues == 0)
                name = $"[{name}]";
            parts.Add(name);
        }

        // Subcommands indicator
        if (command.Subcommands.Any(c => !c.Hidden))
            parts.Add("[command]");

        // Options indicator
        if (command.Options.Any(o => !o.Hidden))
            parts.Add("[options]");

        return string.Join(" ", parts);
    }

    private static (string Name, string Description) FormatOptionEntry(Option option)
    {
        // Collect all names: short aliases first, then long. Filter out /style aliases.
        var names = option.Aliases
            .Concat([option.Name])
            .Where(a => a.StartsWith('-'))
            .Distinct()
            .OrderBy(a => a.TrimStart('-').Length)
            .ThenBy(a => a);
        var name = string.Join(", ", names);

        // Add value placeholder for options that take a value
        if (option.ValueType != typeof(bool) && option.ValueType != typeof(void))
        {
            var helpName = option.HelpName ?? option.Name.TrimStart('-');
            name += $" <{helpName}>";
        }

        return (name, option.Description ?? "");
    }
}

/// <summary>
/// Custom action for the --help option that uses HelpWriter instead of S.CL's built-in HelpAction.
/// </summary>
public class HelpOptionAction : SynchronousCommandLineAction
{
    public override int Invoke(ParseResult parseResult)
    {
        HelpWriter.WriteHelp(parseResult.CommandResult.Command);
        return 0;
    }
}
