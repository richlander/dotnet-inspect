using System.CommandLine;
using System.CommandLine.Help;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the CLI command structure as an API-like tree view.
/// </summary>
public class CliSchemaCommand
{
    public static int Execute(RootCommand rootCommand)
    {
        var view = BuildSchemaView(rootCommand);
        MarkoutSerializer.Serialize(view, Console.Out, CliSchemaContext.Default);
        return 0;
    }

    private static CliSchemaView BuildSchemaView(RootCommand rootCommand)
    {
        var nodes = new List<TreeNode>();

        foreach (var cmd in rootCommand.Subcommands.OrderBy(c => c.Name))
        {
            nodes.Add(BuildCommandNode(cmd));
        }

        return new CliSchemaView
        {
            Name = "dotnet-inspect",
            Version = VersionInfo.Version,
            Description = "CLI tool for inspecting .NET assemblies and NuGet packages",
            Commands = nodes
        };
    }

    private static TreeNode BuildCommandNode(Command command)
    {
        var children = new List<TreeNode>();

        // Arguments
        foreach (var arg in command.Arguments)
        {
            if (arg.Hidden) continue;
            var label = $"<{arg.Name}>";
            if (!string.IsNullOrEmpty(arg.Description))
                label += $"  {arg.Description}";
            children.Add(new TreeNode(label));
        }

        // Options (excluding help)
        foreach (var opt in command.Options.Where(o => !o.Hidden && o is not HelpOption))
        {
            var label = FormatOption(opt);
            children.Add(new TreeNode(label));
        }

        // Subcommands
        foreach (var sub in command.Subcommands.OrderBy(c => c.Name))
        {
            children.Add(BuildCommandNode(sub));
        }

        var nodeLabel = command.Name;
        if (!string.IsNullOrEmpty(command.Description))
            nodeLabel += $"  {command.Description}";

        return new TreeNode(nodeLabel, children);
    }

    private static string FormatOption(Option option)
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

        if (!string.IsNullOrEmpty(option.Description))
            name += $"  {option.Description}";

        return name;
    }
}

[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class CliSchemaView
{
    [MarkoutIgnore]
    public string Name { get; set; } = "";

    [MarkoutIgnore]
    public string Version { get; set; } = "";

    [MarkoutIgnore]
    public string Title => $"{Name} {Version}";

    [MarkoutIgnore]
    public string Description { get; set; } = "";

    [MarkoutIgnoreInTable]
    public List<TreeNode> Commands { get; set; } = [];
}

[MarkoutContext(typeof(CliSchemaView))]
public partial class CliSchemaContext : MarkoutSerializerContext
{
}
