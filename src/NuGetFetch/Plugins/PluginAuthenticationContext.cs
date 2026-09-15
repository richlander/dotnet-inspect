using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace NuGetFetch.Plugins;

/// <summary>
/// Owns the lifetime of one configured source's plugin authentication context.
/// </summary>
public sealed class PluginAuthenticationContextOwner : IDisposable
{
    private static readonly ConditionalWeakTable<
        PackageSourceAssociation,
        PluginAuthenticationContextOwner> LiveContexts = new();

    private readonly PackageSourceAssociation _association;
    private readonly PluginAuthenticationContext _context;
    private int _disposed;

    private PluginAuthenticationContextOwner(
        PackageSourceAssociation association,
        Uri providerQueryUri,
        ICredentialSource provider,
        bool promptOn403)
    {
        _association = association;
        _context = new PluginAuthenticationContext(
            association,
            providerQueryUri,
            provider,
            promptOn403);
    }

    /// <summary>
    /// Gets the non-owning opaque reference supplied to source pipelines.
    /// </summary>
    public PluginAuthenticationContext Context => _context;

    /// <summary>
    /// Creates the authentication context owned by one configured source authority.
    /// </summary>
    public static PluginAuthenticationContextOwner Create(
        PackageSourceAssociation association,
        Uri providerQueryUri,
        ICredentialSource provider,
        bool promptOn403 = false)
    {
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(providerQueryUri);
        ArgumentNullException.ThrowIfNull(provider);
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException(
                "NuGet credential-provider plugins are not supported in Browser/Wasm.");
        }

        var owner = new PluginAuthenticationContextOwner(
            association,
            providerQueryUri,
            provider,
            promptOn403);
        try
        {
            LiveContexts.Add(association, owner);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The package source association already has a live plugin authentication context.",
                exception);
        }

        return owner;
    }

    /// <summary>
    /// Retires the configured source context and clears its credential state.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _context.Retire();
        LiveContexts.Remove(_association);
    }
}

/// <summary>
/// Opaque non-owning reference to one configured source's plugin credential state.
/// </summary>
public sealed class PluginAuthenticationContext
{
    private const string AzureArtifactsHost = "pkgs.dev.azure.com";

    private readonly object _sync = new();
    private readonly PackageSourceAssociation _association;
    private readonly Uri _providerQueryUri;
    private readonly ICredentialSource _provider;
    private readonly ResourceScope _resourceScope;
    private readonly bool _promptOn403;
    private PackageSourceCredential? _credential;
    private long _version;
    private AcquisitionFlight? _flight;
    private bool _retired;

    internal PluginAuthenticationContext(
        PackageSourceAssociation association,
        Uri providerQueryUri,
        ICredentialSource provider,
        bool promptOn403)
    {
        if (!ResourceScope.TryCreate(
                providerQueryUri,
                out ResourceScope resourceScope))
        {
            throw new ArgumentException(
                "The plugin provider-query URI must identify an absolute HTTP or HTTPS package source.",
                nameof(providerQueryUri));
        }

        _association = association;
        _providerQueryUri = providerQueryUri;
        _provider = provider;
        _resourceScope = resourceScope;
        _promptOn403 = promptOn403;
    }

    internal bool HasCredentialSources => _provider.HasCredentialSources;

    internal static bool CanScopeProviderQuery(Uri providerQueryUri)
    {
        ArgumentNullException.ThrowIfNull(providerQueryUri);
        return ResourceScope.TryCreate(providerQueryUri, out _);
    }

    internal bool IsBoundTo(PackageSourceAssociation association) =>
        ReferenceEquals(_association, association);

    internal bool IsResourceInScope(Uri resource) =>
        ResourceScope.TryCreate(resource, out ResourceScope resourceScope)
        && _resourceScope.Equals(resourceScope);

    internal bool IsRetired
    {
        get
        {
            lock (_sync)
            {
                return _retired;
            }
        }
    }

    internal bool IsCredentialChallenge(HttpStatusCode status) =>
        status == HttpStatusCode.Unauthorized
        || _promptOn403 && status == HttpStatusCode.Forbidden;

    internal Participation Read(
        Uri target,
        out CredentialSnapshot snapshot)
    {
        if (!ResourceScope.TryCreate(target, out ResourceScope targetScope)
            || !_resourceScope.Equals(targetScope))
        {
            snapshot = default;
            return Participation.Rejected;
        }

        lock (_sync)
        {
            if (_retired)
            {
                snapshot = default;
                return Participation.Retired;
            }

            snapshot = new CredentialSnapshot(_credential, _version);
            return Participation.Allowed;
        }
    }

    internal async Task<PackageSourceCredential?> AcquireAsync(
        long observedVersion,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        bool hasCredentialSources = HasCredentialSources;
        AcquisitionFlight flight;
        bool start;
        lock (_sync)
        {
            if (_retired)
            {
                return null;
            }

            if (_version != observedVersion && _credential is not null)
            {
                return _credential;
            }

            if (!hasCredentialSources)
            {
                return null;
            }

            start = _flight is null;
            flight = _flight ??= new AcquisitionFlight();
        }

        if (start)
        {
            flight.Start(
                this,
                _provider,
                _providerQueryUri,
                isRetry);
        }

        PackageSourceCredential? acquired =
            await flight.Completion
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        lock (_sync)
        {
            if (_retired)
            {
                return null;
            }

            return _version != observedVersion
                && _credential is not null
                    ? _credential
                    : acquired;
        }
    }

    internal HttpMessageHandler Bind(HttpMessageHandler innerHandler)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        return new SourcePluginAuthenticationHandler(this, innerHandler);
    }

    internal void Retire()
    {
        AcquisitionFlight? flight;
        lock (_sync)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            _credential = null;
            _version++;
            flight = _flight;
        }

        flight?.Retire();
    }

    private void Complete(
        AcquisitionFlight flight,
        PackageSourceCredential? credential,
        Exception? failure,
        CancellationToken cancellationToken)
    {
        bool retired;
        lock (_sync)
        {
            retired = _retired;
            if (ReferenceEquals(_flight, flight))
            {
                _flight = null;
                if (!retired && failure is null && credential is not null)
                {
                    _credential = credential;
                    _version++;
                }
            }
        }

        if (retired)
        {
            flight.TrySetResult(null);
        }
        else if (failure is OperationCanceledException)
        {
            flight.TrySetCanceled(cancellationToken);
        }
        else if (failure is not null)
        {
            flight.TrySetException(failure);
        }
        else
        {
            flight.TrySetResult(credential);
        }
    }

    internal enum Participation
    {
        Allowed,
        Rejected,
        Retired,
    }

    internal readonly record struct CredentialSnapshot(
        PackageSourceCredential? Credential,
        long Version);

    private sealed class AcquisitionFlight
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<PackageSourceCredential?>
            _completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public Task<PackageSourceCredential?> Completion => _completion.Task;

        public void Start(
            PluginAuthenticationContext context,
            ICredentialSource provider,
            Uri providerQueryUri,
            bool isRetry)
        {
            _ = Task.Run(
                () => RunAsync(
                    context,
                    provider,
                    providerQueryUri,
                    isRetry),
                CancellationToken.None);
        }

        public void Retire()
        {
            CancelAndDispose(dispose: false);
            TrySetResult(null);
        }

        public void TrySetResult(PackageSourceCredential? credential) =>
            _completion.TrySetResult(credential);

        public void TrySetCanceled(CancellationToken cancellationToken) =>
            _completion.TrySetCanceled(cancellationToken);

        public void TrySetException(Exception exception) =>
            _completion.TrySetException(exception);

        private async Task RunAsync(
            PluginAuthenticationContext context,
            ICredentialSource provider,
            Uri providerQueryUri,
            bool isRetry)
        {
            PackageSourceCredential? credential = null;
            Exception? failure = null;
            try
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                credential = await provider
                    .GetCredentialsAsync(
                        providerQueryUri,
                        isRetry,
                        _cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            context.Complete(
                this,
                credential,
                failure,
                _cancellation.Token);
            CancelAndDispose(dispose: true);
        }

        private void CancelAndDispose(bool dispose)
        {
            lock (_sync)
            {
                if (dispose)
                {
                    if (!_disposed)
                    {
                        _cancellation.Dispose();
                        _disposed = true;
                    }

                    return;
                }

                if (!_disposed)
                {
                    _cancellation.Cancel();
                }
            }
        }
    }

    private readonly record struct ResourceScope(
        string Scheme,
        string Host,
        int Port,
        string? AzureOrganization)
    {
        public static bool TryCreate(
            Uri uri,
            out ResourceScope scope)
        {
            scope = default;
            if (!uri.IsAbsoluteUri
                || uri.Scheme is not ("http" or "https"))
            {
                return false;
            }

            string host;
            try
            {
                host = uri.IdnHost.ToLowerInvariant();
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (host.Length == 0)
            {
                return false;
            }

            string? organization = null;
            if (host.Equals(
                    AzureArtifactsHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                organization = uri.Segments
                    .Select(segment => segment.Trim('/'))
                    .FirstOrDefault(segment => segment.Length > 0);
                if (organization is null)
                {
                    return false;
                }
            }

            scope = new ResourceScope(
                uri.Scheme.ToLowerInvariant(),
                host,
                uri.Port,
                organization);
            return true;
        }
    }
}

internal sealed class SourcePluginAuthenticationHandler(
    PluginAuthenticationContext context,
    HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private static readonly HttpRequestOptionsKey<
        PluginAuthenticationContext> RejectedContext =
        new("NuGetFetch.PluginAuthenticationContextRejected");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null
            || request.Headers.Authorization is not null
            || IsRejected(request))
        {
            return await base.SendAsync(
                request,
                cancellationToken).ConfigureAwait(false);
        }

        PluginAuthenticationContext.Participation participation =
            context.Read(
                request.RequestUri,
                out PluginAuthenticationContext.CredentialSnapshot snapshot);
        if (participation != PluginAuthenticationContext.Participation.Allowed)
        {
            if (participation == PluginAuthenticationContext.Participation.Rejected)
            {
                request.Options.Set(RejectedContext, context);
            }

            return await base.SendAsync(
                request,
                cancellationToken).ConfigureAwait(false);
        }

        int attempts = 0;
        while (true)
        {
            HttpRequestMessage attempt = CloneRequest(request);
            HttpResponseMessage? response = null;
            bool transferResponse = false;
            bool transferAttempt = false;
            try
            {
                if (snapshot.Credential is not null)
                {
                    attempt.Headers.Authorization =
                        CreateBasicHeader(snapshot.Credential);
                }

                response = await base.SendAsync(
                    attempt,
                    cancellationToken).ConfigureAwait(false);
                if (!context.IsCredentialChallenge(response.StatusCode)
                    || ++attempts >= PluginAuthenticationHandler.MaxAuthRetries)
                {
                    response.RequestMessage ??= attempt;
                    transferAttempt =
                        ReferenceEquals(response.RequestMessage, attempt);
                    transferResponse = true;
                    return response;
                }

                PackageSourceCredential? acquired =
                    await context.AcquireAsync(
                        snapshot.Version,
                        isRetry: snapshot.Credential is not null,
                        cancellationToken).ConfigureAwait(false);
                if (acquired is null)
                {
                    response.RequestMessage ??= attempt;
                    transferAttempt =
                        ReferenceEquals(response.RequestMessage, attempt);
                    transferResponse = true;
                    return response;
                }

                participation = context.Read(
                    request.RequestUri,
                    out snapshot);
                if (participation
                    != PluginAuthenticationContext.Participation.Allowed)
                {
                    response.RequestMessage ??= attempt;
                    transferAttempt =
                        ReferenceEquals(response.RequestMessage, attempt);
                    transferResponse = true;
                    return response;
                }
            }
            finally
            {
                if (!transferResponse && response is not null)
                {
                    HttpRequestMessage? responseRequest =
                        response.RequestMessage;
                    response.Dispose();
                    if (!ReferenceEquals(responseRequest, attempt))
                    {
                        responseRequest?.Dispose();
                    }
                }

                if (!transferAttempt)
                {
                    attempt.Dispose();
                }
            }
        }
    }

    private bool IsRejected(HttpRequestMessage request) =>
        request.Options.TryGetValue(
            RejectedContext,
            out PluginAuthenticationContext? rejected)
        && ReferenceEquals(rejected, context);

    private static AuthenticationHeaderValue CreateBasicHeader(
        PackageSourceCredential credential)
    {
        string encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                $"{credential.Username}:{credential.Password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(
            request.Method,
            request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content,
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header
            in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value);
        }

        foreach (KeyValuePair<string, object?> option
            in request.Options)
        {
            clone.Options.Set(
                new HttpRequestOptionsKey<object?>(option.Key),
                option.Value);
        }

        return clone;
    }
}
