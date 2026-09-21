using DotnetInspector.Packages;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace DotnetInspect.Cli.Commands;

internal sealed class WorkspacePackageSourceRuntime(
    PackageSource[] sources,
    HttpClient httpClient,
    ICredentialSource credentialSource,
    IAsyncDisposable? credentialProvider) : IAsyncDisposable
{
    internal PackageSource[] Sources { get; } = sources;

    internal HttpClient HttpClient { get; } = httpClient;

    internal DesktopPackageSourceComposition CreatePackageSourceComposition(
        TimeSpan requestTimeout) =>
        new(requestTimeout, credentialSource);

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        if (credentialProvider is not null)
            await credentialProvider.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class UnavailableWorkspaceCredentialSource
    : ICredentialSource
{
    internal static UnavailableWorkspaceCredentialSource Instance { get; } =
        new();

    private UnavailableWorkspaceCredentialSource()
    {
    }

    public bool HasCredentialSources => false;

    public Task<PackageSourceCredential?> GetCredentialsAsync(
        Uri uri,
        bool isRetry,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "A Workspace without credential-provider sources cannot query a provider.");
}

internal sealed class WorkspaceCredentialSource : ICredentialSource
{
    private readonly ICredentialSource _inner;
    private readonly HashSet<string> _origins;

    internal WorkspaceCredentialSource(
        ICredentialSource inner,
        IEnumerable<Uri> credentialProviderSources)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(credentialProviderSources);
        _inner = inner;
        _origins =
        [
            .. credentialProviderSources.Select(
                static source => source.GetLeftPart(UriPartial.Authority)),
        ];
    }

    public bool HasCredentialSources =>
        _origins.Count != 0 && _inner.HasCredentialSources;

    public Task<PackageSourceCredential?> GetCredentialsAsync(
        Uri uri,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return _origins.Contains(uri.GetLeftPart(UriPartial.Authority))
            ? _inner.GetCredentialsAsync(uri, isRetry, cancellationToken)
            : Task.FromResult<PackageSourceCredential?>(null);
    }
}
