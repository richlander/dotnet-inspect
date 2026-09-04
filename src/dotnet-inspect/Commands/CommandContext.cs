using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared context for command execution, providing common services like logging and HTTP.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// Logger for verbose output.
    /// </summary>
    public VerboseLogger Logger { get; }

    /// <summary>
    /// Shared HTTP client for all network requests.
    /// </summary>
    public HttpClient HttpClient { get; }

    /// <summary>
    /// Creates a new command context with the specified verbosity.
    /// </summary>
    public CommandContext(bool verbose)
        : this(verbose, HttpClientFactory.Shared)
    {
    }

    internal CommandContext(
        bool verbose,
        HttpClient httpClient)
    {
        Logger = new VerboseLogger(verbose);
        HttpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
    }

    internal DesktopPackageSourceComposition
        CreatePackageSourceComposition() =>
        new(HttpClient.Timeout);
}
