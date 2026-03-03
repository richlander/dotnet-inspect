using System.CommandLine;
using System.CommandLine.Help;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Builds a <see cref="DocumentSchema"/> from a CLI command tree.
/// </summary>
public static class CliCommandExtensions
{
    public static DocumentSchema ToDocumentSchema(this Command command)
    {
        var schema = new DocumentSchema();
        foreach (var sub in command.Subcommands
            .Where(c => !c.Hidden)
            .OrderBy(c => c.Name))
        {
            var items = new List<string>();
            foreach (var arg in sub.Arguments.Where(a => !a.Hidden))
                items.Add($"<{arg.Name}>");
            foreach (var opt in sub.Options.Where(o => !o.Hidden && o is not HelpOption))
                items.Add(opt.Name);
            if (items.Count > 0)
                schema.Add(sub.Name, "option", items.ToArray());
            else
                schema.AddSection(sub.Name);
        }
        return schema;
    }
}
