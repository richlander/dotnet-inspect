using System.Diagnostics;
using SLF = SourceLinkFetch;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Covers <see cref="SLF.SourceLinkResolver.ResolveUrl"/>, which maps a PDB document path to a
/// source URL using the SourceLink document keys embedded in that PDB. Those keys are
/// attacker-controlled: they arrive inside the assembly under inspection, which the user does
/// not necessarily trust (see docs/design/untrusted-data-threat-model.md).
/// </summary>
public class SourceLinkUrlResolutionTests
{
    private static SLF.SourceLinkResolver For(params (string Pattern, string Url)[] mappings)
        => new SLF.SourceLinkResolver(mappings.ToDictionary(m => m.Pattern, m => m.Url));

    [Fact]
    public void ATrailingWildcard_MapsThePathSuffixIntoTheUrl()
    {
        var resolver = For(("/_/src/*", "https://raw.githubusercontent.com/o/r/abc/*"));

        Assert.Equal(
            "https://raw.githubusercontent.com/o/r/abc/System/Text/Json/Utf8JsonReader.cs",
            resolver.ResolveUrl("/_/src/System/Text/Json/Utf8JsonReader.cs"));
    }

    [Fact]
    public void AWildcardKey_MatchesTheEmptySuffix()
    {
        var resolver = For(("/_/*", "https://example.test/*"));

        Assert.Equal("https://example.test/", resolver.ResolveUrl("/_/"));
    }

    [Fact]
    public void AWildcardKey_DoesNotMatchAPathOutsideItsPrefix()
    {
        var resolver = For(("/_/src/*", "https://example.test/*"));

        Assert.Null(resolver.ResolveUrl("/other/src/File.cs"));
    }

    [Fact]
    public void AKeyWithoutAWildcard_MatchesOnlyThatExactPath()
    {
        var resolver = For(("/_/src/File.cs", "https://example.test/File.cs"));

        Assert.Equal("https://example.test/File.cs", resolver.ResolveUrl("/_/src/File.cs"));
        Assert.Null(resolver.ResolveUrl("/_/src/File.cs.bak"));
    }

    [Fact]
    public void ABackslashPath_IsNormalizedBeforeMatching()
    {
        var resolver = For((@"/_/src/*", "https://example.test/*"));

        Assert.Equal("https://example.test/a/b.cs", resolver.ResolveUrl(@"\_\src\a\b.cs"));
    }

    /// <summary>
    /// SourceLink requires a key to carry at most one '*' and requires that '*' to be the final
    /// character. A key that breaks either rule cannot be honored unambiguously, and honoring it
    /// is what previously created the denial of service pinned by
    /// <see cref="AKeyWithManyWildcards_ResolvesPromptly_AndDoesNotBacktrack"/>.
    /// </summary>
    [Theory]
    [InlineData("/_/*/src/*")]      // two wildcards
    [InlineData("/_/*/src")]        // one wildcard, not final
    [InlineData("/*/*/*/*/*/*a")]   // the denial-of-service shape
    public void ANonConformantKey_IsIgnored(string pattern)
    {
        var resolver = For((pattern, "https://example.test/*"));

        Assert.Null(resolver.ResolveUrl("/_/a/src/b.cs"));
    }

    [Fact]
    public void ANonConformantKey_DoesNotShadowAConformantOne()
    {
        var resolver = For(
            ("/_/*/bad/*", "https://wrong.test/*"),
            ("/_/src/*", "https://right.test/*"));

        Assert.Equal("https://right.test/a.cs", resolver.ResolveUrl("/_/src/a.cs"));
    }

    /// <summary>
    /// The previous implementation built a regex by replacing every '*' in the key with the
    /// greedy group "(.*)", then matched with no timeout. Adjacent greedy groups over a
    /// separator-rich path backtrack exponentially: measured on the pre-fix code, twelve
    /// wildcards against a 62-character path took 14.3 seconds, roughly doubling per added
    /// wildcard, from a key an attacker supplies inside a PDB.
    ///
    /// Prefix matching cannot backtrack, so this asserts a bound rather than pinning a shape.
    /// The bound is deliberately loose: it must fail on exponential behavior and never on a
    /// slow machine. The pre-fix code exceeds it by more than three orders of magnitude.
    /// </summary>
    [Fact]
    public void AKeyWithManyWildcards_ResolvesPromptly_AndDoesNotBacktrack()
    {
        string pattern = string.Concat(Enumerable.Repeat("/*", 24)) + "a";
        string filePath = string.Concat(Enumerable.Repeat("/a", 40)) + "/b";
        var resolver = For((pattern, "https://example.test/*"));

        var stopwatch = Stopwatch.StartNew();
        string? resolved = resolver.ResolveUrl(filePath);
        stopwatch.Stop();

        Assert.Null(resolved);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"resolution took {stopwatch.ElapsedMilliseconds}ms, which indicates backtracking");
    }

    /// <summary>
    /// Every SourceLink document key in the 1,452 PDBs cached locally when this fix was written
    /// carried exactly one '*' in the final position -- 1,584 keys, no exceptions. This pins the
    /// shape that survey found, so that the conformant path stays exercised by a real-world key
    /// rather than only by hand-written ones.
    /// </summary>
    [Fact]
    public void TheKeyShapeRealPdbsEmit_Resolves()
    {
        var resolver = For(("/_/*", "https://raw.githubusercontent.com/dotnet/dotnet/f7d9079/*"));

        Assert.Equal(
            "https://raw.githubusercontent.com/dotnet/dotnet/f7d9079/src/libraries/a.cs",
            resolver.ResolveUrl("/_/src/libraries/a.cs"));
    }
}
