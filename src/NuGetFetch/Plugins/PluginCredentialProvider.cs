namespace NuGetFetch.Plugins;

/// <summary>
/// Acquires feed credentials from installed NuGet credential plugins.
/// </summary>
/// <remarks>
/// <para>
/// This is the client half of the NuGet cross-platform plugin protocol, and it is what lets a
/// private feed be read without a password sitting in nuget.config. NuGet ranks credential
/// providers as the most secure of the credential mechanisms, above encrypted passwords,
/// environment macros, and clear text.
/// </para>
/// <para>
/// Plugins are started lazily and then kept for the lifetime of this object. Starting one costs
/// a process launch plus a five-message initialization handshake, so re-launching per request
/// would be a poor trade for a tool that reads several sources.
/// </para>
/// </remarks>
public sealed class PluginCredentialProvider : ICredentialSource, IAsyncDisposable
{
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<PluginExecutable> _executables;
    private readonly Dictionary<string, PluginConnection?> _connections = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Creates a provider over the plugins visible to this process.</summary>
    /// <param name="log">Optional diagnostic sink.</param>
    public PluginCredentialProvider(Action<string>? log = null)
        : this(log, null)
    {
    }

    /// <summary>Creates a provider over an explicit plugin list, bypassing discovery. For tests.</summary>
    internal PluginCredentialProvider(Action<string>? log, IReadOnlyList<PluginExecutable>? executables)
    {
        _log = log;
        _executables = [.. executables ?? PluginDiscovery.Discover()];
    }

    /// <summary>Whether any plugin was found. When false, callers can skip the credential path entirely.</summary>
    public bool HasPlugins => _executables.Count > 0;

    /// <inheritdoc/>
    public bool HasCredentialSources => HasPlugins;

    /// <summary>
    /// Whether plugins may block for user input. False by default, matching <c>dotnet restore</c>
    /// without <c>--interactive</c>: a tool that may run unattended must not hang on a sign-in
    /// prompt. Cached and environment-supplied credentials still work when this is false.
    /// </summary>
    public bool AllowInteractive { get; init; }

    /// <summary>
    /// Asks each plugin in turn for credentials for <paramref name="uri"/>, returning the first
    /// success.
    /// </summary>
    /// <param name="uri">The package source needing credentials.</param>
    /// <param name="isRetry">
    /// False on the first ask. True once credentials have been rejected with a 401, which tells
    /// the plugin to acquire fresh credentials rather than serve its cache. The Azure Artifacts
    /// provider is explicit that skipping this can yield credentials that are already invalid.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Credentials, or null if no plugin serves this URI.</returns>
    public async Task<PackageSourceCredential?> GetCredentialsAsync(
        Uri uri,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        if (_disposed || _executables.Count == 0)
        {
            return null;
        }

        foreach (PluginExecutable executable in _executables)
        {
            PluginConnection? connection = await GetConnectionAsync(executable, cancellationToken).ConfigureAwait(false);

            if (connection is null)
            {
                continue;
            }

            GetAuthenticationCredentialsResponse? response = await connection
                .GetCredentialsAsync(uri, isRetry, !AllowInteractive, AllowInteractive, cancellationToken)
                .ConfigureAwait(false);

            if (response is null)
            {
                continue;
            }

            // NotFound is the ordinary answer from a plugin that does not serve this host, so it
            // means "ask someone else", not "fail".
            if (!string.Equals(response.ResponseCode, ResponseCodes.Success, StringComparison.Ordinal))
            {
                if (string.Equals(response.ResponseCode, ResponseCodes.Error, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(response.Message))
                {
                    _log?.Invoke($"Credential plugin reported an error for {uri.Host}: {response.Message}");
                }

                continue;
            }

            // Credentials travel to the feed as HTTP Basic. A plugin that restricts itself to
            // other schemes cannot be honoured here, so treat that as no answer rather than
            // sending a credential in a form the plugin did not sanction.
            if (response.AuthenticationTypes is { Count: > 0 }
                && !response.AuthenticationTypes.Contains("basic", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(response.Username) && string.IsNullOrEmpty(response.Password))
            {
                continue;
            }

            return new PackageSourceCredential(response.Username ?? string.Empty, response.Password ?? string.Empty);
        }

        return null;
    }

    private async Task<PluginConnection?> GetConnectionAsync(PluginExecutable executable, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // A null entry is a remembered failure: a plugin that would not start, or that does
            // not claim Authentication. Either way there is no point paying to launch it again.
            if (_connections.TryGetValue(executable.Path, out PluginConnection? existing))
            {
                return existing;
            }

            PluginConnection? connection = await PluginConnection
                .StartAsync(executable, _log, cancellationToken)
                .ConfigureAwait(false);

            _connections[executable.Path] = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (PluginConnection? connection in _connections.Values)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        _connections.Clear();
        _gate.Dispose();
    }
}
