using ILInspector.CSharp;

namespace DotnetInspector.Output;

/// <summary>
/// Logger that writes progress messages to stderr when verbose mode is enabled.
/// </summary>
/// <remarks>
/// Verbose progress quotes whatever the run is working on -- a type name, a
/// package id, a file path, an exception message from a hostile assembly -- so
/// every message carries untrusted text by construction. Containment therefore
/// belongs here, at the one write, rather than at the hundred-odd call sites
/// that compose the text (issue #3319).
///
/// Enforced by the compiler: <c>eng/BannedSymbols.txt</c> bans
/// <c>Console.Error</c>, so this class cannot write to the stream itself
/// instead of routing through <see cref="CommandError"/>. Demonstrated end to
/// end by the
/// <c>verbose-progress</c> channel of
/// <c>UntrustedArgumentDiagnosticContainmentTests</c>.
/// </remarks>
public class VerboseLogger(bool enabled)
{
    public bool Enabled { get; } = enabled;

    public void Log(string message)
    {
        if (Enabled)
        {
            CommandError.WriteLine(message);
        }
    }

    /// <summary>
    /// Logs a warning, but only in verbose mode.
    /// </summary>
    /// <remarks>
    /// Forty call sites used to spell <c>Log($"Warning: ...")</c>, which put a
    /// real <c>Warning:</c> line on stderr with neither the prefix nor the
    /// message under <see cref="CommandError"/>. Routing them through here
    /// keeps the verbose gating -- these are diagnostics the user only asked
    /// for -- while leaving the prefix and the containment with their owner.
    /// </remarks>
    public void LogWarning(string message)
    {
        if (Enabled)
        {
            CommandError.WriteWarning(message);
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
    /// rare enough that this cannot become noise -- a sweep of 26,584 real assemblies produced
    /// none -- and <c>CoreCache</c> already writes its cache-escape refusal unconditionally.
    /// </remarks>
    public void Warn(string message) => CommandError.WriteWarning(message);
}
