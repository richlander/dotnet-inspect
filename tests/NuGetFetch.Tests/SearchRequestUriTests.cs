using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Composing a search request from a feed-declared endpoint.
/// </summary>
/// <remarks>
/// The property is structural, not textual: appending <c>"?q=…"</c> to an
/// endpoint that already carries a query does not add a parameter, it extends
/// the last existing value — so a pre-signed endpoint kept its signature in the
/// URL while silently losing the search term.
/// </remarks>
public class SearchRequestUriTests
{
    const string Secret = "s3cr3t-signature-value";

    static readonly (string Name, string Value)[] SearchParameters =
    [
        ("q", "Contoso.Pkg"),
        ("skip", "0"),
        ("take", "20"),
        ("prerelease", "false"),
        ("semVerLevel", "2.0.0"),
    ];

    [Theory]
    [InlineData("https://feed.test/v3/query")]
    [InlineData("https://feed.test/v3/query?sig=" + Secret)]
    [InlineData("https://feed.test/v3/query?sig=" + Secret + "&api-version=2")]
    [InlineData("https://feed.test/v3/query?")]
    [InlineData("https://feed.test/v3/query#anchor")]
    [InlineData("https://feed.test/v3/query?sig=" + Secret + "#anchor")]
    public void TryCompose_KeepsThePathAndAddsEveryParameterSeparately(
        string endpoint)
    {
        Assert.True(
            SearchRequestUri.TryCompose(endpoint, SearchParameters, out string url));

        var composed = new Uri(url, UriKind.Absolute);
        Assert.Equal("/v3/query", composed.AbsolutePath);
        Assert.Equal(string.Empty, composed.Fragment);

        // Exactly one query boundary: the appended parameters joined the query
        // rather than becoming part of an existing value.
        Assert.Equal(1, url.Count(character => character == '?'));

        Dictionary<string, string> parameters = QueryParameters(composed);
        Assert.Equal("Contoso.Pkg", parameters["q"]);
        Assert.Equal("0", parameters["skip"]);
        Assert.Equal("20", parameters["take"]);
        Assert.Equal("false", parameters["prerelease"]);
        Assert.Equal("2.0.0", parameters["semVerLevel"]);

        // Whatever the endpoint already carried survives as its own parameter.
        bool signed = endpoint.Contains("sig=", StringComparison.Ordinal);
        Assert.Equal(signed, parameters.ContainsKey("sig"));
        if (signed)
            Assert.Equal(Secret, parameters["sig"]);

        bool versioned = endpoint.Contains("api-version=", StringComparison.Ordinal);
        Assert.Equal(versioned, parameters.ContainsKey("api-version"));
        if (versioned)
            Assert.Equal("2", parameters["api-version"]);
    }

    /// <summary>
    /// An existing query is carried verbatim: its exact escaping is what a
    /// signature was computed over.
    /// </summary>
    [Fact]
    public void TryCompose_PreservesTheExistingQueryTextExactly()
    {
        const string existing =
            "s%69g=%73ecret&a=a%2Bb%2Fc%3D&t=x%20y&opaque=%7E%41";

        Assert.True(
            SearchRequestUri.TryCompose(
                $"https://feed.test/v3/query?{existing}",
                SearchParameters,
                out string url));

        Assert.Contains($"?{existing}&q=", url, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCompose_RefusesMalformedRawEscapes()
    {
        Assert.False(
            SearchRequestUri.TryCompose(
                "https://feed.test/v3/query?sig=%zz",
                SearchParameters,
                out string url));
        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void TryCompose_RequiresFeedDeclaredNonAsciiTextToBeEscaped()
    {
        Assert.False(
            SearchRequestUri.TryCompose(
                "https://feed.test/über/query?sig=täg",
                SearchParameters,
                out string invalid));
        Assert.Equal(string.Empty, invalid);

        Assert.True(
            SearchRequestUri.TryCompose(
                "https://feed.test/%C3%BCber/query?sig=t%C3%A4g",
                SearchParameters,
                out string encoded));
        Assert.StartsWith(
            "https://feed.test/%C3%BCber/query?sig=t%C3%A4g&",
            encoded,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("semVerLevel")]
    [InlineData("SEMVERLEVEL")]
    [InlineData("%73emVerLevel")]
    public void TryCompose_ReplacesConflictingProductOwnedParameter(
        string existingName)
    {
        Assert.True(
            SearchRequestUri.TryCompose(
                $"https://feed.test/v3/query?{existingName}=1.0.0&sig={Secret}",
                SearchParameters,
                out string url));

        Assert.Contains($"?sig={Secret}&", url, StringComparison.Ordinal);
        Assert.Equal(
            1,
            url.Split('&').Count(
                pair => Uri.UnescapeDataString(
                        pair.Split('=', 2)[0].TrimStart('?'))
                    .Equals("semVerLevel", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            "2.0.0",
            QueryParameters(new Uri(url))["semVerLevel"]);
    }

    [Fact]
    public void TryCompose_RemovesOnlyTheQueryDelimiter()
    {
        const string endpoint = "https://feed.test/v3/query??sig=x";

        Assert.True(
            SearchRequestUri.TryCompose(
                endpoint,
                [("q", "sample")],
                out string url));

        Assert.Equal(
            "https://feed.test/v3/query??sig=x&q=sample",
            url);
    }

    [Fact]
    public void TryCompose_EscapesTheParametersItContributes()
    {
        Assert.True(
            SearchRequestUri.TryCompose(
                "https://feed.test/v3/query",
                [("q", "a&b=c d")],
                out string url));

        Assert.Equal(
            "https://feed.test/v3/query?q=a%26b%3Dc%20d",
            url);
        Assert.Equal(
            "a&b=c d",
            QueryParameters(new Uri(url)).GetValueOrDefault("q"));
    }

    [Fact]
    public void TryCompose_AddsTheImplicitRootPathToAnAuthorityOnlyEndpoint()
    {
        Assert.True(
            SearchRequestUri.TryCompose(
                "https://feed.test",
                [("q", "sample")],
                out string url));

        Assert.Equal("https://feed.test/?q=sample", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/v3/query")]
    [InlineData("v3/query")]
    [InlineData("ftp://feed.test/query")]
    [InlineData("file:///tmp/query")]
    [InlineData("not a url at all")]
    public void TryCompose_RefusesAnEndpointThatIsNotAbsoluteHttp(string? endpoint)
    {
        Assert.False(
            SearchRequestUri.TryCompose(endpoint, SearchParameters, out string url));
        Assert.Equal(string.Empty, url);
    }

    [Fact]
    public void TryCompose_WithNoParameters_KeepsTheEndpointAsItIs()
    {
        Assert.True(
            SearchRequestUri.TryCompose(
                $"https://feed.test/v3/query?sig={Secret}",
                [],
                out string url));

        Assert.Equal($"https://feed.test/v3/query?sig={Secret}", url);
    }

    static Dictionary<string, string> QueryParameters(Uri uri)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            string name = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            parameters[Uri.UnescapeDataString(name)] =
                Uri.UnescapeDataString(value);
        }

        return parameters;
    }
}
