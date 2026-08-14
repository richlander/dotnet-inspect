using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// How a package source is named in a diagnostic. A configured alias is the
/// useful answer; a source the user named by URL has no alias, and printing its
/// name printed its URL.
/// </summary>
public sealed class PackageSourceDisplayTests
{
    const string Secret = "s3cr3t-signature-value";

    [Fact]
    public void ForDiagnostics_KeepsAConfiguredAlias()
    {
        Assert.Equal(
            "contoso-internal",
            PackageSourceDisplay
                .ForDiagnostics(
                    new PackageSource(
                        "contoso-internal",
                        $"https://feed.test/v3/index.json?x={Secret}"))
                .ToString());
    }

    /// <summary>
    /// The unmatched-explicit-source shape: <c>--source https://…</c> that
    /// matches no configured entry is constructed with its URL as its name.
    /// </summary>
    [Fact]
    public void ForDiagnostics_RedactsAUrlNamedSource()
    {
        string url = $"https://feed.test/v3/index.json?x={Secret}";

        string displayed = PackageSourceDisplay
            .ForDiagnostics(new PackageSource(url, url))
            .ToString();

        Assert.DoesNotContain(Secret, displayed, StringComparison.Ordinal);
        Assert.Equal("https://feed.test/v3/index.json?REDACTED", displayed);
    }

    /// <summary>
    /// A name that is URL-shaped but not the source's own URL is the same
    /// hazard arriving another way.
    /// </summary>
    [Fact]
    public void ForDiagnostics_RedactsAUrlShapedName()
    {
        string displayed = PackageSourceDisplay
            .ForDiagnostics(
                $"https://elsewhere.test/feed?x={Secret}",
                "https://feed.test/v3/index.json")
            .ToString();

        Assert.DoesNotContain(Secret, displayed, StringComparison.Ordinal);
        Assert.Equal("https://elsewhere.test/feed?REDACTED", displayed);
    }

    /// <summary>
    /// An alias is user-supplied text like any other, so it is contained even
    /// when it carries nothing secret.
    /// </summary>
    [Fact]
    public void ForDiagnostics_ContainsANonGraphicAlias()
    {
        string displayed = PackageSourceDisplay
            .ForDiagnostics("cont\u202eoso", "https://feed.test/v3/index.json")
            .ToString();

        Assert.DoesNotContain('\u202e', displayed);
    }

    [Fact]
    public void ForDiagnostics_FallsBackToTheUrlWhenUnnamed()
    {
        Assert.Equal(
            "https://feed.test/v3/index.json?REDACTED",
            PackageSourceDisplay
                .ForDiagnostics(
                    name: null,
                    $"https://feed.test/v3/index.json?x={Secret}")
                .ToString());
        Assert.Equal(
            string.Empty,
            PackageSourceDisplay.ForDiagnostics(null).ToString());
    }
}
