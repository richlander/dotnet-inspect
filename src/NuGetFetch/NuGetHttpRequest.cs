namespace NuGetFetch;

internal static class NuGetHttpRequest
{
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

    public static HttpRequestMessage CreateGet(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Options.Set(BrowserStreamingResponse, true);
        return request;
    }

    public static HttpRequestMessage CreateGetPreservingPathAndQuery(
        string requestUri)
    {
        if (!TryCreatePreservingPathAndQuery(requestUri, out Uri? preserved))
        {
            throw new ArgumentException(
                "The request URI must be a well-formed absolute HTTP or HTTPS URI.",
                nameof(requestUri));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, preserved);
        request.Options.Set(BrowserStreamingResponse, true);
        return request;
    }

    public static bool TryCreatePreservingPathAndQuery(
        string requestUri,
        out Uri? preserved)
    {
        preserved = null;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out Uri? validated)
            || validated.Scheme is not ("http" or "https")
            || !HasValidRawText(requestUri))
        {
            return false;
        }

        var options = new UriCreationOptions
        {
            DangerousDisablePathAndQueryCanonicalization = true,
        };
        preserved = new Uri(requestUri, in options);
        return true;
    }

    private static bool HasValidRawText(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == '%')
            {
                if (i + 2 >= value.Length
                    || !Uri.IsHexDigit(value[i + 1])
                    || !Uri.IsHexDigit(value[i + 2]))
                {
                    return false;
                }

                i += 2;
                continue;
            }

            if (char.IsControl(character)
                || char.IsWhiteSpace(character)
                || character is '\\' or '"' or '<' or '>' or '^' or '`' or '{' or '|' or '}')
            {
                return false;
            }
        }

        return true;
    }
}
