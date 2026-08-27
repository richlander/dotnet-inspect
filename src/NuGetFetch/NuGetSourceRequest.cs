using System.Buffers;
using System.Net.Http.Headers;
using System.Text;

namespace NuGetFetch;

/// <summary>
/// Normalizes source-owned request URLs and scopes explicit source credentials.
/// </summary>
/// <remarks>
/// Raw ASCII query bytes are preserved for signed endpoints, while Unicode
/// source-owned URLs are escaped with an IDN host. Credentials remain on the
/// source origin, including credentials supplied by plugins.
/// <c>PackageSourceClientProvider_SuppressesPluginCredentialForCrossOriginSearch</c>,
/// <c>PackageSourceClientProvider_SuppressesPluginCredentialForCrossOriginRedirect</c>,
/// <c>V3SearchPreservesDeclaredQueryBytes</c>,
/// <c>V3SearchPreservesSignedBytesWhileNormalizingIdn</c>,
/// <c>V3SearchNormalizesIdnServiceIndex</c>,
/// <c>V3SearchNormalizesAdvertisedUnicodeEndpoint</c>,
/// <c>V3SearchPathlessBaseSourcePreservesSignedQuery</c>, and
/// <c>CanonicalNuGetOrgV3DiscoversSearchWithoutShortcut</c> gate these rules.
/// </remarks>
internal static class NuGetSourceRequest
{
    private static readonly HttpRequestOptionsKey<bool>
        SuppressPluginAuthentication =
            new("NuGetFetch.SuppressPluginAuthentication");

    internal static string EndpointUrl(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string original = endpoint.OriginalString;
        int fragmentStart = original.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
            original = original[..fragmentStart];
        original = EnsureRootPath(original);

        if (endpoint.UserInfo.Length == 0
            && NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                original,
                out _))
        {
            return original;
        }

        if (endpoint.UserInfo.Length > 0
            || !NuGetHttpRequest.HasValidRawText(
                original,
                allowNonAscii: true))
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        string host;
        try
        {
            host = endpoint.HostNameType == UriHostNameType.IPv6
                ? $"[{endpoint.IdnHost}]"
                : endpoint.IdnHost;
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.",
                exception);
        }

        int schemeEnd = original.IndexOf(
            "://",
            StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        int authorityStart = schemeEnd + 3;
        if (authorityStart >= original.Length)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        int suffixStart = original.IndexOfAny(
            ['/', '?'],
            authorityStart);
        if (suffixStart < 0)
            suffixStart = original.Length;

        ReadOnlySpan<char> authority =
            original.AsSpan(authorityStart, suffixStart - authorityStart);
        string escapedAuthority = EscapeAuthorityHost(
            authority,
            host);
        string escapedSuffix = EscapeNonAscii(
            original.AsSpan(suffixStart));
        string escaped =
            original[..authorityStart]
            + escapedAuthority
            + escapedSuffix;
        if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                escaped,
                out _))
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        return escaped;
    }

    internal static bool TryEndpointUrl(
        string? endpoint,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        try
        {
            normalized = EndpointUrl(parsed);
            return true;
        }
        catch (NuGetSourceResponseException)
        {
            return false;
        }
    }

    private static string EnsureRootPath(string endpoint)
    {
        int schemeEnd = endpoint.IndexOf(
            "://",
            StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return endpoint;

        int authorityStart = schemeEnd + 3;
        int suffixStart = endpoint.IndexOfAny(
            ['/', '?'],
            authorityStart);
        if (suffixStart < 0)
            return endpoint + "/";

        return endpoint[suffixStart] == '?'
            ? endpoint.Insert(suffixStart, "/")
            : endpoint;
    }

    private static string EscapeAuthorityHost(
        ReadOnlySpan<char> authority,
        string normalizedHost)
    {
        int hostLength;
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            int closingBracket = authority.IndexOf(']');
            if (closingBracket < 0)
            {
                throw new NuGetSourceResponseException(
                    "The package source service-index endpoint is unusable.");
            }

            hostLength = closingBracket + 1;
        }
        else
        {
            int portSeparator = authority.LastIndexOf(':');
            hostLength = portSeparator >= 0
                ? portSeparator
                : authority.Length;
        }

        ReadOnlySpan<char> declaredHost = authority[..hostLength];
        bool requiresIdn = false;
        foreach (char character in declaredHost)
        {
            if (character > 0x7F)
            {
                requiresIdn = true;
                break;
            }
        }

        if (!requiresIdn)
            return authority.ToString();

        return normalizedHost + authority[hostLength..].ToString();
    }

    private static string EscapeNonAscii(ReadOnlySpan<char> value)
    {
        bool requiresEscaping = false;
        foreach (char character in value)
        {
            if (character > 0x7F)
            {
                requiresEscaping = true;
                break;
            }
        }

        if (!requiresEscaping)
            return value.ToString();

        var escaped = new StringBuilder(value.Length);
        Span<byte> utf8 = stackalloc byte[4];
        while (!value.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                value,
                out Rune rune,
                out int charsConsumed);
            if (status != OperationStatus.Done)
            {
                throw new NuGetSourceResponseException(
                    "The package source service-index endpoint is unusable.");
            }

            value = value[charsConsumed..];
            if (rune.IsAscii)
            {
                escaped.Append((char)rune.Value);
                continue;
            }

            int bytesWritten = rune.EncodeToUtf8(utf8);
            foreach (byte octet in utf8[..bytesWritten])
            {
                escaped.Append('%');
                escaped.Append(octet.ToString("X2"));
            }
        }

        return escaped.ToString();
    }

    internal static PackageSourceCredential? CredentialForEndpoint(
        string? sourceUrl,
        string endpointUrl,
        PackageSourceCredential? credential) =>
        CredentialForEndpoint(
            sourceUrl,
            endpointUrl,
            credential,
            OperatingSystem.IsBrowser());

    internal static PackageSourceCredential? CredentialForEndpoint(
        string? sourceUrl,
        string endpointUrl,
        PackageSourceCredential? credential,
        bool isBrowser)
    {
        if (sourceUrl is null)
            return credential;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? source)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint))
        {
            return null;
        }

        try
        {
            bool sameOrigin = SameOrigin(source, endpoint);
            if (isBrowser && !sameOrigin)
            {
                throw new NuGetSourceResponseException(
                    "The package source advertised a cross-origin resource that the browser transport cannot authorize.");
            }

            return sameOrigin ? credential : null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    internal static AuthenticationHeaderValue? AuthenticationForEndpoint(
        string sourceUrl,
        string endpointUrl,
        PackageSourceCredential? credential)
    {
        PackageSourceCredential? scoped = CredentialForEndpoint(
            sourceUrl,
            endpointUrl,
            credential);
        return scoped is null ? null : Authentication(scoped);
    }

    internal static void ApplyCredential(
        HttpRequestMessage request,
        PackageSourceCredential? credential)
    {
        if (credential is not null)
            request.Headers.Authorization = Authentication(credential);
    }

    internal static void SuppressPluginAuthenticationForCrossOrigin(
        HttpRequestMessage request,
        string? sourceUrl,
        string endpointUrl)
    {
        if (sourceUrl is not null
            && !SameOrigin(sourceUrl, endpointUrl))
        {
            SuppressPluginAuthenticationForRequest(request);
        }
    }

    internal static void SuppressPluginAuthenticationForRequest(
        HttpRequestMessage request) =>
        request.Options.Set(SuppressPluginAuthentication, true);

    internal static bool IsPluginAuthenticationSuppressed(
        HttpRequestMessage request) =>
        request.Options.TryGetValue(
            SuppressPluginAuthentication,
            out bool suppressed)
        && suppressed;

    private static bool SameOrigin(
        string sourceUrl,
        string endpointUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? source)
            || !Uri.TryCreate(
                endpointUrl,
                UriKind.Absolute,
                out Uri? endpoint))
        {
            return false;
        }

        try
        {
            return SameOrigin(source, endpoint);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool SameOrigin(Uri source, Uri endpoint) =>
        source.Scheme.Equals(
            endpoint.Scheme,
            StringComparison.OrdinalIgnoreCase)
        && source.IdnHost.Equals(
            endpoint.IdnHost,
            StringComparison.OrdinalIgnoreCase)
        && source.Port == endpoint.Port;

    private static AuthenticationHeaderValue Authentication(
        PackageSourceCredential credential)
    {
        string encoded = Convert.ToBase64String(
            Encoding.ASCII.GetBytes(
                $"{credential.Username}:{credential.Password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
