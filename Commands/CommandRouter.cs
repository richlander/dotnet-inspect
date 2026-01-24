namespace DotnetInspector.Commands;

/// <summary>
/// Routes CLI arguments to the appropriate command.
/// </summary>
public static class CommandRouter
{
    /// <summary>
    /// Parses arguments and returns the appropriate command with remaining args.
    /// </summary>
    public static (ICommand command, string[] args) Route(string[] args)
    {
        if (args.Length == 0)
        {
            // No args - show help
            return (new HelpCommand(), []);
        }

        string first = args[0].ToLowerInvariant();

        return first switch
        {
            // Explicit commands
            "package" => (new PackageCommand(), args[1..]),
            "help" => (new HelpCommand(), args[1..]),
            "--help" => (new HelpCommand(), args[1..]),

            // Implicit package command (backward compatibility)
            // If first arg is not a known command, treat it as package name/path
            _ => (new PackageCommand(), args)
        };
    }
}
