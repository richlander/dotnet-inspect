using System.CommandLine;
using DotnetInspector.Commands;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the experimental interactive Repl command.
/// </summary>
internal static class ReplCommandDefinition
{
    internal static Command Create()
    {
        var command = new Command("repl", "Start the experimental contextual inspection Repl");
        command.SetAction((_, cancellationToken) => ReplCommand.ExecuteAsync(cancellationToken));
        return command;
    }
}
