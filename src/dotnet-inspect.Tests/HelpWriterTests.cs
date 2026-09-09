using System.CommandLine;
using DotnetInspector.CommandLine;
using DotnetInspector.Views;

namespace DotnetInspector.Tests;

public class HelpWriterTests
{
    [Fact]
    public void RootCommands_AreAlphabetical()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        string[] commands = GetCommandNames(HelpWriter.BuildHelpView(root));

        Assert.Equal(
            commands.OrderBy(command => command, StringComparer.Ordinal),
            commands);
    }

    [Fact]
    public void NestedCommands_PreserveAuthoredOrder()
    {
        var parent = new Command("parent");
        parent.Subcommands.Add(new Command("zeta"));
        parent.Subcommands.Add(new Command("alpha"));

        string[] commands = GetCommandNames(HelpWriter.BuildHelpView(parent));

        Assert.Equal(["zeta", "alpha"], commands);
    }

    private static string[] GetCommandNames(HelpView view)
    {
        Assert.NotNull(view.Commands);
        return
        [
            .. view.Commands.Select(command =>
                command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
        ];
    }
}
