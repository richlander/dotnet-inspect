using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the CLI command structure as an API-like tree view.
/// </summary>
public class CliSchemaCommand
{
    public static int Execute(RootCommand rootCommand, string? commandFilter, Verbosity verbosity)
    {
        var commands = rootCommand.Subcommands.OrderBy(c => c.Name).ToList();

        if (!string.IsNullOrEmpty(commandFilter))
        {
            var match = commands.FirstOrDefault(c => c.Name.Equals(commandFilter, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                Console.Error.WriteLine($"Error: Command '{commandFilter}' not found.");
                return 1;
            }
            commands = [match];
            // Default to full detail unless quiet was explicitly requested
            if (verbosity != Verbosity.Quiet)
                verbosity = Verbosity.Detailed;
        }

        // Quiet: use DocumentSchema + DiscoverOutput for consistent oneline output
        if (verbosity == Verbosity.Quiet)
        {
            var schema = rootCommand.ToDocumentSchema();
            var discover = !string.IsNullOrEmpty(commandFilter) ? new[] { commandFilter } : null;
            return DiscoverOutput.Execute(discover, schema);
        }

        var nodes = commands.Select(c => BuildCommandNode(c, verbosity)).ToList();

        var view = new CliSchemaView
        {
            Name = "dotnet-inspect",
            Version = VersionInfo.Version,
            Description = "CLI tool for inspecting .NET libraries and NuGet packages",
            Commands = nodes
        };

        MarkoutSerializer.Serialize(view, Console.Out, CliSchemaContext.Default);
        return 0;
    }

    private static TreeNode BuildCommandNode(Command command, Verbosity verbosity)
    {
        List<TreeNode> children = [];

        // Minimal: command + description only, no children
        if (verbosity > Verbosity.Minimal)
        {
            // Arguments
            foreach (var arg in command.Arguments)
            {
                if (arg.Hidden) continue;
                var label = $"<{arg.Name}>";
                if (verbosity >= Verbosity.Detailed && !string.IsNullOrEmpty(arg.Description))
                    label += $"  {arg.Description}";
                children.Add(new TreeNode(label));
            }

            // Options (excluding help)
            foreach (var opt in command.Options.Where(o => !o.Hidden && o is not HelpOption))
            {
                var label = FormatOption(opt, verbosity);
                children.Add(new TreeNode(label));
            }

            // Subcommands
            foreach (var sub in command.Subcommands.OrderBy(c => c.Name))
            {
                children.Add(BuildCommandNode(sub, verbosity));
            }
        }

        var nodeLabel = command.Name;
        if (verbosity >= Verbosity.Minimal && !string.IsNullOrEmpty(command.Description))
            nodeLabel += $"  {command.Description}";

        return new TreeNode(nodeLabel) { Children = children };
    }

    private static string FormatOption(Option option, Verbosity verbosity)
    {
        var aliases = option.Aliases.OrderBy(a => a.Length).ThenBy(a => a);
        var allNames = new[] { option.Name }.Concat(aliases).Distinct();
        var name = string.Join(", ", allNames);

        // Add value placeholder for non-boolean options
        if (option.ValueType != typeof(bool))
        {
            var helpName = option.HelpName ?? option.Name.TrimStart('-');
            name += $" <{helpName}>";
        }

        if (verbosity >= Verbosity.Detailed && !string.IsNullOrEmpty(option.Description))
            name += $"  {option.Description}";

        return name;
    }
}
