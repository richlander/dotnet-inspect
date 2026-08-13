using DotnetInspector.Core;
using InertText;

namespace DotnetInspector.Tests;

/// <summary>
/// The one owner of what a URL may look like in a diagnostic.
/// </summary>
/// <remarks>
/// The property under test is negative and total: no component a feed controls
/// survives into printed text. That is why these cases are mostly close
/// negatives with an unfamiliar parameter name — a redaction that recognizes
/// <c>sig</c> and <c>access_token</c> looks correct against every example
/// anyone thought to write down, and republishes the same credential the moment
/// a feed renames its parameter to <c>x</c>.
/// </remarks>
public class UrlRedactionTests
{
    const string Secret = "s3cr3t-signature-value";

    /// <summary>
    /// The parameter name is feed-controlled, so recognizing names cannot be
    /// the rule. Every one of these carries the same credential.
    /// </summary>
    [Theory]
    [InlineData("https://feed.test/flat/a.nupkg?sig={0}")]
    [InlineData("https://feed.test/flat/a.nupkg?x={0}")]
    [InlineData("https://feed.test/flat/a.nupkg?whatever={0}")]
    [InlineData("https://feed.test/flat/a.nupkg?a=1&x={0}")]
    [InlineData("https://feed.test/flat/a.nupkg?{0}")]
    [InlineData("https://feed.test/flat/a.nupkg?x={0}#fragment")]
    [InlineData("https://feed.test/flat/a.nupkg?x%3D={0}")]
    [InlineData("https://feed.test/flat/a.nupkg?x=%73%33%63%72%33%74")]
    public void ForDiagnostics_DropsEveryQueryValueWhateverItIsCalled(
        string template)
    {
        string url = string.Format(template, Secret);

        string redacted = UrlRedaction.ForDiagnostics(url).ToString();

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.Equal(
            $"https://feed.test/flat/a.nupkg?{UrlRedaction.QueryMarker}",
            redacted);
    }

    [Fact]
    public void ForDiagnostics_KeepsTheSourceAndResourceIdentity()
    {
        Assert.Equal(
            "https://feed.test/v3/flat/sample/1.0.0/sample.1.0.0.nupkg",
            UrlRedaction
                .ForDiagnostics(
                    "https://feed.test/v3/flat/sample/1.0.0/sample.1.0.0.nupkg")
                .ToString());
    }

    [Fact]
    public void ForDiagnostics_DropsAFragmentEvenWithoutAQuery()
    {
        string redacted = UrlRedaction
            .ForDiagnostics($"https://feed.test/flat/a.nupkg#{Secret}")
            .ToString();

        Assert.Equal("https://feed.test/flat/a.nupkg", redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDiagnostics_DropsUserInfo()
    {
        string redacted = UrlRedaction
            .ForDiagnostics($"https://user:{Secret}@feed.test/flat/a.nupkg")
            .ToString();

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.Equal("https://feed.test/flat/a.nupkg", redacted);
    }

    [Theory]
    [InlineData("//user:{0}@feed.test/F/auth/{0}/api?x={0}",
        "//feed.test/F/auth/REDACTED/api?REDACTED")]
    [InlineData("\\/user:{0}@feed.test/F/auth/{0}/api?x={0}",
        "\\/feed.test/F/auth/REDACTED/api?REDACTED")]
    public void ForDiagnostics_DropsUserInfoFromNetworkPathReferences(
        string template,
        string expected)
    {
        string redacted = UrlRedaction
            .ForDiagnostics(string.Format(template, Secret))
            .ToString();

        Assert.Equal(expected, redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDiagnostics_KeepsTheKnownPathTokenRule()
    {
        string redacted = UrlRedaction
            .ForDiagnostics($"https://host.test/F/feed/auth/{Secret}/api/v3/index.json")
            .ToString();

        Assert.Equal(
            "https://host.test/F/feed/auth/REDACTED/api/v3/index.json",
            redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDiagnostics_RedactsConsecutivePathTokens()
    {
        string redacted = UrlRedaction
            .ForDiagnostics(
                $"https://host.test/F/auth/auth/{Secret}/api/v3/index.json")
            .ToString();

        Assert.Equal(
            "https://host.test/F/auth/REDACTED/REDACTED/api/v3/index.json",
            redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ForDiagnostics_RedactsAuthTokenAcrossEmptyPathSegments()
    {
        string redacted = UrlRedaction
            .ForDiagnostics(
                $"https://host.test/F/auth//{Secret}/api")
            .ToString();

        Assert.Equal(
            "https://host.test/F/auth//REDACTED/api",
            redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A network path with an empty authority leaves credential-shaped text in
    /// the path. Fail closed rather than echoing it.
    /// </summary>
    [Theory]
    [InlineData("///user:{0}@feed.test/path")]
    [InlineData("////user:{0}@feed.test/path")]
    [InlineData("///user:{0}@feed.test/path?x=1")]
    public void ForDiagnostics_RefusesEmptyAuthorityNetworkPaths(string template)
    {
        string redacted = UrlRedaction
            .ForDiagnostics(string.Format(template, Secret))
            .ToString();

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.StartsWith(
            UrlRedaction.UnparsableMarker,
            redacted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForDiagnostics_StillRedactsNetworkPathUserInfoWithAuthority()
    {
        string redacted = UrlRedaction
            .ForDiagnostics($"//user:{Secret}@feed.test/path")
            .ToString();

        Assert.Equal("//feed.test/path", redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/relative/flat/a.nupkg?x={0}", "/relative/flat/a.nupkg?REDACTED")]
    [InlineData("relative?x={0}", "relative?REDACTED")]
    [InlineData("relative#{0}", "relative")]
    [InlineData("auth/{0}/index.json", "auth/REDACTED/index.json")]
    [InlineData("not a url at all ?x={0}", "not a url at all ?REDACTED")]
    [InlineData("::::?x={0}", "::::?REDACTED")]
    public void ForDiagnostics_AppliesTheSameRuleToTextThatIsNotAnAbsoluteUrl(
        string template,
        string expected)
    {
        string redacted = UrlRedaction
            .ForDiagnostics(string.Format(template, Secret))
            .ToString();

        Assert.Equal(expected, redacted);
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Text that names an authority but cannot be parsed has no locatable
    /// components, so nothing in it can be shown to be safe — and a password
    /// sits in exactly the part that failed to parse.
    /// </summary>
    [Theory]
    [InlineData("https://user:{0}@bad[")]
    [InlineData("https://user:{0}@")]
    [InlineData("user:{0}@host")]
    [InlineData("https://[{0}")]
    [InlineData("weird-scheme://user:{0}@host/path")]
    [InlineData("https://user:{0}@bad[?x=1")]
    public void ForDiagnostics_RefusesToEchoAnUnparsableAuthority(string template)
    {
        string redacted = UrlRedaction
            .ForDiagnostics(string.Format(template, Secret))
            .ToString();

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("user", redacted, StringComparison.Ordinal);
        Assert.StartsWith(
            UrlRedaction.UnparsableMarker,
            redacted,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The close negative: a relative path has no authority to hide a
    /// credential in, and it is a useful diagnostic, so it is not swallowed by
    /// the fail-closed rule.
    /// </summary>
    [Theory]
    [InlineData("/relative/flat/a.nupkg", "/relative/flat/a.nupkg")]
    [InlineData("relative/flat/a.nupkg", "relative/flat/a.nupkg")]
    [InlineData("versions markout", "versions markout")]
    [InlineData("C:/cache/packages", "C:/cache/packages")]
    public void ForDiagnostics_KeepsRelativeAndNonLocatorDiagnostics(
        string value,
        string expected)
    {
        Assert.Equal(expected, UrlRedaction.ForDiagnostics(value).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForDiagnostics_HandlesAbsentInput(string? url)
    {
        Assert.Equal(string.Empty, UrlRedaction.ForDiagnostics(url).ToString());
    }

    [Fact]
    public void ForDiagnostics_HandlesAnEmptyQuery()
    {
        Assert.Equal(
            "https://feed.test/flat/a.nupkg",
            UrlRedaction
                .ForDiagnostics("https://feed.test/flat/a.nupkg?")
                .ToString());
    }

    /// <summary>
    /// A URL is text before it is a locator, so a scalar that can act on the
    /// sink is encoded even when nothing about it is secret.
    /// </summary>
    [Fact]
    public void ForDiagnostics_EncodesNonGraphicScalars()
    {
        InertString redacted = UrlRedaction.ForDiagnostics(
            "https://feed.test/flat/\u202egnp.evil");

        Assert.True(redacted.WasEncoded);
        Assert.DoesNotContain('\u202e', redacted.ToString());
    }

    /// <summary>
    /// A reachable consumer: credential scoping is handed the endpoint URL a
    /// feed declared, decides not to send credentials to it, and says so. The
    /// endpoint text may be malformed — that is one reason it is not the
    /// source's origin — and it must still not be echoed.
    /// </summary>
    [Fact]
    public void CredentialScope_WithheldEndpoint_IsNeverEchoed()
    {
        var source = new NuGetFetch.PackageSource(
            "private",
            "https://feed.test/v3/index.json",
            new NuGetFetch.PackageSourceCredential("user", "pass"));
        List<string> logs = [];

        foreach (string endpoint in
            new[]
            {
                $"https://elsewhere.test/flat/a.nupkg?x={Secret}",
                $"https://user:{Secret}@bad[",
            })
        {
            Assert.Null(
                DotnetInspector.Packages.NuGetCredentialScope.AuthFor(
                    source,
                    endpoint,
                    logs.Add));
        }

        Assert.Equal(2, logs.Count);
        Assert.All(
            logs,
            line => Assert.DoesNotContain(
                Secret,
                line,
                StringComparison.Ordinal));
    }

    [Fact]
    public void DescribeRequestFailure_NamesTheCategoryAndNotTheMessage()
    {
        string described = UrlRedaction.DescribeRequestFailure(
            $"https://feed.test/flat/a.nupkg?x={Secret}",
            new HttpRequestException(
                $"No such host is known: https://feed.test/flat/a.nupkg?x={Secret}"))
            .ToString();

        Assert.DoesNotContain(Secret, described, StringComparison.Ordinal);
        Assert.Contains("HttpRequestException", described, StringComparison.Ordinal);
        Assert.Contains(
            $"https://feed.test/flat/a.nupkg?{UrlRedaction.QueryMarker}",
            described,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure text a lookup prints is built from recorded URLs, so the
    /// recording is where the rule has to hold — not the rendering.
    /// </summary>
    [Fact]
    public void FeedFailureDescription_CarriesNoQueryValue()
    {
        using (FeedFailureTelemetry.Scope())
        {
            FeedFailureTelemetry.Record(
                $"https://feed.test/flat/sample/index.json?x={Secret}",
                System.Net.HttpStatusCode.Unauthorized);

            string described = FeedFailureTelemetry.Current!
                .DescribeFailure("sample")!
                .Value
                .ToString();

            Assert.DoesNotContain(Secret, described, StringComparison.Ordinal);
            Assert.Contains(
                "feed.test/flat/sample/index.json",
                described,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Cache telemetry keys are URLs on the acquisition paths, and observers
    /// print them.
    /// </summary>
    [Fact]
    public void CacheObservationKey_CarriesNoQueryValue()
    {
        var observed = new List<CacheObservation>();
        using (CacheTelemetry.Subscribe(new CacheObserver(observed)))
        {
            CacheTelemetry.Record(
                "package",
                $"https://feed.test/flat/sample/index.json?x={Secret}",
                CacheAccessResult.Miss);
        }

        CacheObservation observation = Assert.Single(observed);
        Assert.DoesNotContain(
            Secret,
            observation.Key.ToString(),
            StringComparison.Ordinal);
    }

    sealed class CacheObserver(List<CacheObservation> observed)
        : IObserver<CacheObservation>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(CacheObservation value) => observed.Add(value);
    }
}
