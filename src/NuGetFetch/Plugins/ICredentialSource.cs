namespace NuGetFetch.Plugins;

/// <summary>
/// Supplies credentials for a package source on demand.
/// </summary>
/// <remarks>
/// This exists so the 401 retry loop in <see cref="PluginAuthenticationHandler"/> can be
/// exercised without launching a real plugin process. The production implementation is
/// <see cref="PluginCredentialProvider"/>.
/// </remarks>
public interface ICredentialSource
{
    /// <summary>
    /// Whether any credential source is available. When false the handler stays out of the way
    /// entirely, so a machine with no plugins installed pays nothing.
    /// </summary>
    bool HasCredentialSources { get; }

    /// <summary>Requests credentials for <paramref name="uri"/>, or null if none can be supplied.</summary>
    /// <param name="uri">The package source needing credentials.</param>
    /// <param name="isRetry">True once previously supplied credentials have been rejected with a 401.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PackageSourceCredential?> GetCredentialsAsync(Uri uri, bool isRetry, CancellationToken cancellationToken);
}
