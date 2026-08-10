using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace NuGetFetch.Plugins;

/// <summary>
/// Supplies feed credentials from NuGet credential plugins, acquiring them in response to a 401
/// and replaying the request.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors NuGet's own design. NuGet performs credential acquisition inside
/// <c>HttpSourceAuthenticationHandler</c>, a <see cref="DelegatingHandler"/> in
/// NuGet/NuGet.Client at
/// <c>src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceAuthenticationHandler.cs</c>, rather
/// than at its call sites. Two consequences make that the right shape to copy:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Every request is covered, including ones whose call site forgot to pass a credential.
///     Attaching credentials centrally removes a whole class of "this one path is anonymous" bug.
///   </description></item>
///   <item><description>
///     Credentials are only sought when a server actually demands them, so a public feed never
///     pays for a plugin process launch.
///   </description></item>
/// </list>
/// <para>
/// The retry bound and the 403 policy also follow NuGet: at most
/// <see cref="MaxAuthRetries"/> attempts per source, and 401 always triggers acquisition while
/// 403 does so only when explicitly enabled.
/// </para>
/// </remarks>
public sealed class PluginAuthenticationHandler : DelegatingHandler
{
    private const string AzureArtifactsHost = "pkgs.dev.azure.com";

    /// <summary>
    /// Maximum credential attempts for a single request, matching
    /// <c>AmbientAuthenticationState.MaxAuthRetries</c> in NuGet. Bounding this is what stops a
    /// plugin that keeps returning credentials the feed keeps rejecting from looping forever.
    /// </summary>
    public const int MaxAuthRetries = 4;

    private readonly ICredentialSource _provider;
    private readonly ConcurrentDictionary<string, CredentialScopeState> _credentialScopes =
        new(StringComparer.Ordinal);

    /// <summary>Creates the handler.</summary>
    /// <param name="provider">Supplies credentials for challenged sources.</param>
    /// <param name="innerHandler">The next handler in the pipeline.</param>
    public PluginAuthenticationHandler(ICredentialSource provider, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _provider = provider;
    }

    /// <summary>
    /// Whether a 403 should trigger credential acquisition as well as a 401.
    /// </summary>
    /// <remarks>
    /// Off by default. A 403 usually means "authenticated, but not allowed", where asking for
    /// fresh credentials cannot help; NuGet gates this behind the same opt-in.
    /// </remarks>
    public bool PromptOn403 { get; init; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_provider.HasCredentialSources || request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // A credential already on the request came from configuration, which takes precedence over
        // anything a plugin supplies. Every clone below re-inherits that header, so an acquired
        // credential could never be applied: entering the loop would only resend the same failing
        // request until the retry budget ran out, re-invoking the plugin with isRetry each time and
        // discarding the result. Send once and let the caller see the challenge.
        if (request.Headers.Authorization is not null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        CredentialScopeState credentialScope = _credentialScopes.GetOrAdd(
            GetCredentialScope(request.RequestUri),
            static _ => new CredentialScopeState());

        bool acquiredCredentials = false;
        int attempts = 0;

        while (true)
        {
            // Snapshot before sending, so that if a concurrent request refreshes the credential
            // while this one is in flight we can tell and simply retry rather than acquire again.
            (PackageSourceCredential? credential, long version) = credentialScope.Read();

            HttpRequestMessage attempt = CloneRequest(request);
            HttpResponseMessage? response = null;
            bool transferResponse = false;
            bool transferAttempt = false;
            try
            {
                if (credential is not null && attempt.Headers.Authorization is null)
                {
                    attempt.Headers.Authorization = CreateBasicHeader(credential);
                }

                response = await base.SendAsync(attempt, cancellationToken).ConfigureAwait(false);

                if (!IsCredentialChallenge(response.StatusCode) || ++attempts >= MaxAuthRetries)
                {
                    response.RequestMessage ??= attempt;
                    transferAttempt = ReferenceEquals(response.RequestMessage, attempt);
                    transferResponse = true;
                    return response;
                }

                PackageSourceCredential? acquired = await AcquireAsync(
                    credentialScope,
                    request.RequestUri,
                    version,
                    isRetry: acquiredCredentials,
                    cancellationToken).ConfigureAwait(false);

                if (acquired is null)
                {
                    // No plugin can serve this source. Surface the challenge unchanged so the caller
                    // reports an authentication failure rather than a missing package.
                    response.RequestMessage ??= attempt;
                    transferAttempt = ReferenceEquals(response.RequestMessage, attempt);
                    transferResponse = true;
                    return response;
                }

                acquiredCredentials = true;
            }
            finally
            {
                if (!transferResponse && response is not null)
                {
                    HttpRequestMessage? responseRequest = response.RequestMessage;
                    response.Dispose();
                    if (!ReferenceEquals(responseRequest, attempt))
                        responseRequest?.Dispose();
                }

                if (!transferAttempt)
                    attempt.Dispose();
            }
        }
    }

    private async Task<PackageSourceCredential?> AcquireAsync(
        CredentialScopeState credentialScope,
        Uri uri,
        long observedVersion,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        // One acquisition at a time per credential scope: concurrent requests to the same feed should
        // trigger a single plugin round trip, not one each.
        await credentialScope.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            (PackageSourceCredential? current, long version) = credentialScope.Read();

            // Someone else refreshed while we waited. Their credential is untried by us, so
            // retry with it before asking for another.
            if (version != observedVersion && current is not null)
            {
                return current;
            }

            PackageSourceCredential? credential = await _provider
                .GetCredentialsAsync(uri, isRetry, cancellationToken)
                .ConfigureAwait(false);

            if (credential is not null)
            {
                credentialScope.Set(credential);
            }

            return credential;
        }
        finally
        {
            credentialScope.Gate.Release();
        }
    }

    private bool IsCredentialChallenge(HttpStatusCode status) =>
        status == HttpStatusCode.Unauthorized || (PromptOn403 && status == HttpStatusCode.Forbidden);

    private static AuthenticationHeaderValue CreateBasicHeader(PackageSourceCredential credential)
    {
        string encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    /// <summary>
    /// Returns the credential-sharing boundary for one request.
    /// </summary>
    /// <remarks>
    /// Most feed hosts identify one credential realm, so their origin is the scope. Azure
    /// Artifacts is different: every organization shares <c>pkgs.dev.azure.com</c>. Its first
    /// path segment, the organization, therefore participates in identity while the remainder
    /// does not. This also keeps one credential across Azure's name-to-GUID endpoint aliases:
    /// configured indexes use project and feed names, while discovered endpoints use their GUIDs.
    ///
    /// An Azure URL without an organization path gets its own host-wide root slot. It cannot
    /// collide with any organization slot.
    /// </remarks>
    private static string GetCredentialScope(Uri uri)
    {
        string origin =
            $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
        if (!string.Equals(
                uri.IdnHost,
                AzureArtifactsHost,
                StringComparison.OrdinalIgnoreCase))
        {
            return origin;
        }

        string[] segments =
        [
            .. uri.Segments
                .Select(segment => segment.Trim('/'))
                .Where(segment => segment.Length > 0),
        ];
        if (segments.Length == 0)
            return origin;

        return $"{origin}/{segments[0]}";
    }

    /// <summary>
    /// Copies a request so it can be sent again. An <see cref="HttpRequestMessage"/> cannot be
    /// reused once sent, and NuGet clones for the same reason.
    /// </summary>
    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content,
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (KeyValuePair<string, object?> option in request.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        return clone;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (CredentialScopeState credentialScope in _credentialScopes.Values)
            {
                credentialScope.Gate.Dispose();
            }

            _credentialScopes.Clear();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The credential currently believed good for one scope, with a version stamp used to
    /// detect refreshes by concurrent requests.
    /// </summary>
    private sealed class CredentialScopeState
    {
        private readonly Lock _sync = new();
        private PackageSourceCredential? _credential;
        private long _version;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public (PackageSourceCredential? Credential, long Version) Read()
        {
            lock (_sync)
            {
                return (_credential, _version);
            }
        }

        public void Set(PackageSourceCredential credential)
        {
            lock (_sync)
            {
                _credential = credential;
                _version++;
            }
        }
    }
}
