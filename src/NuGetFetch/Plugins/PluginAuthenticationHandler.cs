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
    /// <summary>
    /// Maximum credential attempts for a single request, matching
    /// <c>AmbientAuthenticationState.MaxAuthRetries</c> in NuGet. Bounding this is what stops a
    /// plugin that keeps returning credentials the feed keeps rejecting from looping forever.
    /// </summary>
    public const int MaxAuthRetries = 4;

    private readonly ICredentialSource _provider;
    private readonly ConcurrentDictionary<string, AuthorityState> _authorities = new(StringComparer.OrdinalIgnoreCase);

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

        AuthorityState authority = _authorities.GetOrAdd(GetAuthority(request.RequestUri), static _ => new AuthorityState());

        HttpResponseMessage? response = null;
        bool acquiredCredentials = false;
        int attempts = 0;

        while (true)
        {
            response?.Dispose();

            // Snapshot before sending, so that if a concurrent request refreshes the credential
            // while this one is in flight we can tell and simply retry rather than acquire again.
            (PackageSourceCredential? credential, long version) = authority.Read();

            using (HttpRequestMessage attempt = CloneRequest(request))
            {
                if (credential is not null && attempt.Headers.Authorization is null)
                {
                    attempt.Headers.Authorization = CreateBasicHeader(credential);
                }

                response = await base.SendAsync(attempt, cancellationToken).ConfigureAwait(false);
            }

            if (!IsCredentialChallenge(response.StatusCode) || ++attempts >= MaxAuthRetries)
            {
                return response;
            }

            PackageSourceCredential? acquired = await AcquireAsync(
                authority,
                request.RequestUri,
                version,
                isRetry: acquiredCredentials,
                cancellationToken).ConfigureAwait(false);

            if (acquired is null)
            {
                // No plugin can serve this source. Surface the challenge unchanged so the caller
                // reports an authentication failure rather than a missing package.
                return response;
            }

            acquiredCredentials = true;
        }
    }

    private async Task<PackageSourceCredential?> AcquireAsync(
        AuthorityState authority,
        Uri uri,
        long observedVersion,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        // One acquisition at a time per authority: concurrent requests to the same feed should
        // trigger a single plugin round trip, not one each.
        await authority.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            (PackageSourceCredential? current, long version) = authority.Read();

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
                authority.Set(credential);
            }

            return credential;
        }
        finally
        {
            authority.Gate.Release();
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

    /// <summary>Credentials are scoped to scheme, host and port, so one feed's token is never sent to another.</summary>
    private static string GetAuthority(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

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
            foreach (AuthorityState authority in _authorities.Values)
            {
                authority.Gate.Dispose();
            }

            _authorities.Clear();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The credential currently believed good for one authority, with a version stamp used to
    /// detect refreshes by concurrent requests.
    /// </summary>
    private sealed class AuthorityState
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
