using System.Net.Http.Headers;
using System.Text;

namespace NuGetFetch;

/// <summary>
/// Normalizes source-owned request URLs and scopes explicit source credentials.
/// </summary>
/// <remarks>
/// Raw ASCII query bytes are preserved for signed endpoints, while Unicode
/// source URLs are escaped with an IDN host. Credentials remain on the source
/// origin. <c>V3SearchPreservesDeclaredQueryBytes</c>,
/// <c>V3SearchNormalizesIdnServiceIndex</c>, and
/// <c>CanonicalNuGetOrgV3DiscoversSearchWithoutShortcut</c> gate these rules.
/// </remarks>
internal static class NuGetSourceRequest
{
    internal static string EndpointUrl(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string original = endpoint.OriginalString;
        int fragmentStart = original.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
            original = original[..fragmentStart];

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

        string pathAndQuery = endpoint.GetComponents(
            UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
        string escaped =
            $"{endpoint.Scheme}://{host}"
            + (endpoint.IsDefaultPort ? "" : $":{endpoint.Port}")
            + (pathAndQuery.StartsWith("/", StringComparison.Ordinal)
                ? pathAndQuery
                : "/" + pathAndQuery);
        if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                escaped,
                out _))
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        return escaped;
    }

    internal static PackageSourceCredential? CredentialForEndpoint(
        string? sourceUrl,
        string endpointUrl,
        PackageSourceCredential? credential)
    {
        if (credential is null || sourceUrl is null)
            return credential;

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? source)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint))
        {
            return null;
        }

        try
        {
            return source.Scheme.Equals(
                    endpoint.Scheme,
                    StringComparison.OrdinalIgnoreCase)
                && source.IdnHost.Equals(
                    endpoint.IdnHost,
                    StringComparison.OrdinalIgnoreCase)
                && source.Port == endpoint.Port
                    ? credential
                    : null;
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
