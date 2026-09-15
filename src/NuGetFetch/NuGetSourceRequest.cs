using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace NuGetFetch;

/// <summary>
/// Normalizes source-owned request URLs and scopes explicit source credentials.
/// </summary>
/// <remarks>
/// Raw ASCII query bytes are preserved for signed endpoints, while Unicode
/// source-owned URLs are escaped with an IDN host. Credentials remain on the
/// source origin. <c>V3SearchPreservesDeclaredQueryBytes</c>,
/// <c>V3SearchPreservesSignedBytesWhileNormalizingIdn</c>,
/// <c>V3SearchNormalizesIdnServiceIndex</c>,
/// <c>V3SearchNormalizesAdvertisedUnicodeEndpoint</c>,
/// <c>V3SearchPathlessServiceIndexPreservesSignedQuery</c>, and
/// <c>CanonicalNuGetOrgV3DiscoversSearchWithoutShortcut</c> gate these rules.
/// </remarks>
internal static class NuGetSourceRequest
{
    internal enum EndpointHostKind
    {
        Dns,
        IPv4,
        IPv6,
    }

    internal sealed class EndpointProjection
    {
        private readonly byte[] _addressBytes;

        private EndpointProjection(
            string scheme,
            EndpointHostKind hostKind,
            string dnsHost,
            byte[] addressBytes,
            string zone,
            int port,
            string escapedPath)
        {
            Scheme = scheme;
            HostKind = hostKind;
            DnsHost = dnsHost;
            _addressBytes = addressBytes;
            Zone = zone;
            Port = port;
            EscapedPath = escapedPath;
        }

        internal string Scheme { get; }
        internal EndpointHostKind HostKind { get; }
        internal string DnsHost { get; }
        internal ReadOnlySpan<byte> AddressBytes => _addressBytes;
        internal string Zone { get; }
        internal int Port { get; }
        internal string EscapedPath { get; }

        internal static EndpointProjection Create(
            string scheme,
            EndpointHostKind hostKind,
            string dnsHost,
            byte[] addressBytes,
            string zone,
            int port,
            string escapedPath) =>
            new(
                scheme,
                hostKind,
                dnsHost,
                addressBytes,
                zone,
                port,
                escapedPath);
    }

    internal static EndpointProjection ProjectEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri
            || (endpoint.Scheme != Uri.UriSchemeHttp
                && endpoint.Scheme != Uri.UriSchemeHttps)
            || endpoint.UserInfo.Length != 0)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        Uri identityEndpoint = WithoutQueryAndFragment(endpoint);
        string normalized = EndpointUrl(identityEndpoint);
        int schemeEnd = normalized.IndexOf(
            "://",
            StringComparison.Ordinal);
        int authorityStart = schemeEnd + 3;
        int pathStart = normalized.IndexOf(
            '/',
            authorityStart);
        if (schemeEnd <= 0 || pathStart < authorityStart)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        int queryStart = normalized.IndexOf(
            '?',
            pathStart);
        string escapedPath = queryStart < 0
            ? normalized[pathStart..]
            : normalized[pathStart..queryStart];
        if (escapedPath.Length == 0)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        string idnHost;
        try
        {
            idnHost = identityEndpoint.IdnHost;
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.",
                exception);
        }

        EndpointHostKind hostKind;
        string dnsHost = "";
        byte[] addressBytes = [];
        string zone = "";
        switch (identityEndpoint.HostNameType)
        {
            case UriHostNameType.Dns:
                hostKind = EndpointHostKind.Dns;
                dnsHost = ToLowerAscii(idnHost);
                break;
            case UriHostNameType.IPv4:
                hostKind = EndpointHostKind.IPv4;
                addressBytes = ParseAddress(idnHost, AddressFamily.InterNetwork);
                break;
            case UriHostNameType.IPv6:
                hostKind = EndpointHostKind.IPv6;
                int zoneStart = idnHost.IndexOf(
                    "%25",
                    StringComparison.OrdinalIgnoreCase);
                string address = zoneStart < 0
                    ? idnHost
                    : idnHost[..zoneStart];
                zone = zoneStart < 0
                    ? ""
                    : idnHost[(zoneStart + 3)..];
                addressBytes = ParseAddress(
                    address,
                    AddressFamily.InterNetworkV6);
                break;
            default:
                throw new NuGetSourceResponseException(
                    "The package source service-index endpoint is unusable.");
        }

        return EndpointProjection.Create(
            ToLowerAscii(endpoint.Scheme),
            hostKind,
            dnsHost,
            addressBytes,
            zone,
            identityEndpoint.Port,
            escapedPath);
    }

    internal static bool CanProjectEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            _ = ProjectEndpoint(endpoint);
            return true;
        }
        catch (NuGetSourceResponseException)
        {
            return false;
        }
    }

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

    private static byte[] ParseAddress(
        string text,
        AddressFamily expectedFamily)
    {
        if (!IPAddress.TryParse(text, out IPAddress? address)
            || address.AddressFamily != expectedFamily)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        return address.GetAddressBytes();
    }

    private static Uri WithoutQueryAndFragment(Uri endpoint)
    {
        string locator = endpoint.OriginalString;
        int fragmentStart = locator.IndexOf(
            '#',
            StringComparison.Ordinal);
        if (fragmentStart >= 0)
            locator = locator[..fragmentStart];
        int queryStart = locator.IndexOf(
            '?',
            StringComparison.Ordinal);
        if (queryStart >= 0)
            locator = locator[..queryStart];

        try
        {
            return new Uri(
                locator,
                new UriCreationOptions
                {
                    DangerousDisablePathAndQueryCanonicalization = true,
                });
        }
        catch (UriFormatException exception)
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.",
                exception);
        }
    }

    private static string ToLowerAscii(string value)
    {
        return string.Create(
            value.Length,
            value,
            static (destination, source) =>
            {
                for (int i = 0; i < source.Length; i++)
                {
                    char character = source[i];
                    if (character > 0x7F)
                    {
                        throw new NuGetSourceResponseException(
                            "The package source service-index endpoint is unusable.");
                    }

                    destination[i] = character is >= 'A' and <= 'Z'
                        ? (char)(character + ('a' - 'A'))
                        : character;
                }
            });
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
            bool sameOrigin = source.Scheme.Equals(
                    endpoint.Scheme,
                    StringComparison.OrdinalIgnoreCase)
                && source.IdnHost.Equals(
                    endpoint.IdnHost,
                    StringComparison.OrdinalIgnoreCase)
                && source.Port == endpoint.Port;
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

    private static AuthenticationHeaderValue Authentication(
        PackageSourceCredential credential)
    {
        string encoded = Convert.ToBase64String(
            Encoding.ASCII.GetBytes(
                $"{credential.Username}:{credential.Password}"));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
