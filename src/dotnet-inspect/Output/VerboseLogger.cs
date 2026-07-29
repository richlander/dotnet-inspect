namespace DotnetInspector.Output;

/// <summary>
/// Logger that writes progress messages to stderr when verbose mode is enabled.
/// </summary>
public class VerboseLogger(bool enabled)
{
    public bool Enabled { get; } = enabled;

    public void Log(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine(message);
        }
    }

    public void Log(string format, params object[] args)
    {
        if (Enabled)
        {
            Console.Error.WriteLine(format, args);
        }
    }

    /// <summary>
    /// Writes a warning to stderr regardless of verbosity.
    /// </summary>
    /// <remarks>
    /// <see cref="Log(string)"/> is progress reporting and is correctly silent by default. A
    /// refusal is not progress: when the tool declines to resolve something because the input was
    /// unsafe, the result is a node that looks exactly like an ordinary unresolved reference, so
    /// routing that message through <see cref="Log(string)"/> hides the reason at every verbosity
    /// a user actually runs. Refusals are rare enough to be free -- a sweep of 26,584 assemblies
    /// under the shared framework produced none -- so this cannot become noise on real input.
    /// </remarks>
    public void Warn(string message) => Console.Error.WriteLine(message);
}
