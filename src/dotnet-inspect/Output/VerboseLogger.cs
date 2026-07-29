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
    /// Writes unconditionally. For a refusal that changes what the tool reports -- rejecting an
    /// untrusted name that would otherwise have been resolved, for example.
    /// </summary>
    /// <remarks>
    /// <see cref="Log(string)"/> is verbose-gated, so reporting a refusal through it leaves the
    /// default run with a quietly thinner result and exit 0, which is the success-shaped failure
    /// <c>AGENTS.md</c> forbids: the user cannot tell a refused input from an absent one. Two
    /// reviewers independently flagged the same pattern at two different guards. Refusals are
    /// rare enough that this cannot become noise -- a sweep of 30,454 real assemblies produced
    /// none -- and <c>CoreCache</c> already writes its cache-escape refusal unconditionally.
    /// </remarks>
    public void Warn(string message) => Console.Error.WriteLine($"Warning: {message}");
}
