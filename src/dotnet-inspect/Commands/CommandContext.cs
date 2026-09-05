using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared context for command execution, providing common services like logging and HTTP.
/// </summary>
public class CommandContext
{
    private readonly Func<DesktopPackageSourceComposition>? _createPackageSourceComposition;

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
        HttpClient httpClient,
        Func<DesktopPackageSourceComposition>? createPackageSourceComposition = null)
    {
        Logger = new VerboseLogger(verbose);
        HttpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
        _createPackageSourceComposition = createPackageSourceComposition;
    }

    internal DesktopPackageSourceComposition
        CreatePackageSourceComposition() =>
        _createPackageSourceComposition?.Invoke() ?? new(HttpClient.Timeout);
}
