namespace DotnetInspector.Commands;

/// <summary>
/// Interface for CLI commands.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Executes the command with the given arguments.
    /// </summary>
    /// <param name="args">Command arguments (after command name).</param>
    /// <returns>Exit code (0 for success).</returns>
    Task<int> ExecuteAsync(string[] args);
}
