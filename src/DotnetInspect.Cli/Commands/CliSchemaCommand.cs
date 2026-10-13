using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

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
                CommandError.Write($"Command '{commandFilter}' not found.");
                return 1;
            }
            commands = [match];
            // Default to full detail unless quiet was explicitly requested
            if (verbosity != Verbosity.Quiet)
                verbosity = Verbosity.Detailed;
        }

        // Quiet: use DocumentSchema + DiscoverOutput for consistent table output
        if (verbosity == Verbosity.Quiet)
        {
            var schema = rootCommand.ToDocumentSchema();
            var discover = !string.IsNullOrEmpty(commandFilter) ? new[] { commandFilter } : null;
            return DiscoverOutput.Execute(
                discover,
                schema,
                new DiscoveryOutputRequest());
        }

        CliSchemaPresentation.Write(
            new CliSchemaPresentationPlan(
                "dotnet-inspect",
                VersionInfo.Version,
                "CLI tool for inspecting .NET libraries and NuGet packages"),
            sink => WriteCommands(
                sink,
                commands,
                verbosity),
            Console.Out);
        return 0;
    }

    private static void WriteCommands(
        IInspectionHierarchySink<string> sink,
        IReadOnlyList<Command> commands,
        Verbosity verbosity)
    {
        for (int index = 0; index < commands.Count; index++)
        {
            WriteCommand(
                sink,
                commands[index],
                verbosity,
                isLastSibling: index == commands.Count - 1);
        }
    }

    private static void WriteCommand(
        IInspectionHierarchySink<string> sink,
        Command command,
        Verbosity verbosity,
        bool isLastSibling)
    {
        string nodeLabel = command.Name;
        if (verbosity >= Verbosity.Minimal
            && !string.IsNullOrEmpty(command.Description))
        {
            nodeLabel += $"  {command.Description}";
        }

        if (verbosity <= Verbosity.Minimal
            || !HasVisibleChildren(command))
        {
            sink.WriteNode(
                nodeLabel,
                isLastSibling);
            return;
        }

        sink.WriteNode(
            nodeLabel,
            isLastSibling,
            children => WriteCommandChildren(
                children,
                command,
                verbosity));
    }

    private static bool HasVisibleChildren(Command command)
    {
        return command.Arguments.Any(static argument => !argument.Hidden)
            || command.Options.Any(
                static option =>
                    !option.Hidden
                    && option is not HelpOption)
            || command.Subcommands.Count > 0;
    }

    private static void WriteCommandChildren(
        IInspectionHierarchySink<string> sink,
        Command command,
        Verbosity verbosity)
    {
        Argument[] arguments =
        [
            .. command.Arguments.Where(
                static argument => !argument.Hidden),
        ];
        Option[] options =
        [
            .. command.Options.Where(
                static option =>
                    !option.Hidden
                    && option is not HelpOption),
        ];
        Command[] subcommands =
        [
            .. command.Subcommands.OrderBy(
                static subcommand => subcommand.Name),
        ];
        int childCount =
            arguments.Length
            + options.Length
            + subcommands.Length;
        int childIndex = 0;

        foreach (Argument argument in arguments)
        {
            string label = $"<{argument.Name}>";
            if (verbosity >= Verbosity.Detailed
                && !string.IsNullOrEmpty(argument.Description))
            {
                label += $"  {argument.Description}";
            }

            sink.WriteNode(
                label,
                isLastSibling: ++childIndex == childCount);
        }

        foreach (Option option in options)
        {
            sink.WriteNode(
                FormatOption(option, verbosity),
                isLastSibling: ++childIndex == childCount);
        }

        foreach (Command subcommand in subcommands)
        {
            WriteCommand(
                sink,
                subcommand,
                verbosity,
                isLastSibling: ++childIndex == childCount);
        }
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
