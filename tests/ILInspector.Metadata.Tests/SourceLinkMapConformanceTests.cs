using SLF = SourceLinkFetch;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Pins the SourceLink document-map rule against the specification, and pins that exactly one
/// implementation of it exists.
/// </summary>
/// <remarks>
/// <para>
/// The repository previously carried two independent matchers —
/// <see cref="SLF.SourceLinkResolver"/> and <c>SourceDocumentPathResolver</c> — which disagreed
/// on six of the nine inputs below. Measured against the rule the Source Link specification
/// states (<c>dotnet/designs</c>, <c>accepted/2020/diagnostics/source-link.md</c>), neither was
/// correct, so the disagreement could not be settled by preferring one.
/// </para>
/// <para>
/// Each row therefore carries the answer the specification requires, and every row is asserted
/// through <em>both</em> surviving entry points. Agreement alone would be satisfied by two
/// matchers that are wrong in the same way, so agreement is not what is asserted: conformance is,
/// and agreement follows from it.
/// </para>
/// </remarks>
public class SourceLinkMapConformanceTests
{
    /// <summary>
    /// The nine inputs measured across the two replaced implementations, each paired with the
    /// URL the specification requires. <see cref="EveryRow_IsOneTheReplacedImplementationsGot"/>
    /// keeps this table honest: a row on which both replaced matchers already produced the
    /// specified answer would pass here without gating anything.
    /// </summary>
    private sealed record Row(string Because, string Map, string DocumentPath, string? Expected);

    private static readonly Row[] Rows =
    [
        // "Resolved in order from most specific to least specific" -- not document order. The
        // specification's own worked example lists the shorter key first and still requires the
        // longer one to win.
        new(
            "most specific key wins regardless of document order",
            """{"documents":{"/_/*":"https://host/A/*","/_/src/*":"https://host/B/*"}}""",
            "/_/src/Foo.cs",
            "https://host/B/Foo.cs"),

        // "Original source file paths are compared case-insensitively to documents."
        new(
            "document paths compare case-insensitively",
            """{"documents":{"/_/SRC/*":"https://host/A/*"}}""",
            "/_/src/Foo.cs",
            "https://host/A/Foo.cs"),

        // Rule 3: a key's wildcard must be its final character. A key that breaks the rule cannot
        // be honoured unambiguously and is dropped.
        new(
            "a key whose wildcard is not final is dropped",
            """{"documents":{"/_/*/x":"https://host/A/*"}}""",
            "/_/src/x",
            null),

        // A document name is spliced into a URL, so any character that steers URL parsing must be
        // encoded. An unencoded space is not a legal URL character at all.
        new(
            "a space in a document name is percent-encoded",
            """{"documents":{"/_/*":"https://host/A/*"}}""",
            "/_/my dir/Foo.cs",
            "https://host/A/my%20dir/Foo.cs"),

        // An unencoded '#' would truncate the URL at the fragment, silently fetching the wrong
        // file rather than failing.
        new(
            "a hash in a document name is percent-encoded",
            """{"documents":{"/_/*":"https://host/A/*"}}""",
            "/_/a#b/Foo.cs",
            "https://host/A/a%23b/Foo.cs"),

        // Rule 1: "one and only one". A template with two wildcards has no single substitution.
        new(
            "a url template with two wildcards is dropped",
            """{"documents":{"/_/*":"https://host/*/raw/*"}}""",
            "/_/Foo.cs",
            null),

        new(
            "an exact key resolves to its constant url",
            """{"documents":{"/_/Foo.cs":"https://host/A/Foo.cs"}}""",
            "/_/Foo.cs",
            "https://host/A/Foo.cs"),

        // Rule 4: "If the URL contains a *, it may be anywhere in the URL." This is the shape
        // Azure DevOps emits, and substituting only a trailing wildcard leaves a literal asterisk
        // in the URL -- see AnAzureDevOpsMap_SubstitutesIntoTheMiddleOfTheUrl.
        new(
            "a url wildcard is substituted wherever it appears",
            """{"documents":{"/_/*":"https://host/A/*/raw"}}""",
            "/_/src/Foo.cs",
            "https://host/A/src/Foo.cs/raw"),

        new(
            "a key may end mid-segment",
            """{"documents":{"/_/sr*":"https://host/A/*"}}""",
            "/_/src/Foo.cs",
            "https://host/A/c/Foo.cs"),
    ];

    public static TheoryData<string, string, string, string?> SpecifiedResolutions()
    {
        TheoryData<string, string, string, string?> data = [];
        foreach (var row in Rows)
            data.Add(row.Because, row.Map, row.DocumentPath, row.Expected);
        return data;
    }

    [Theory]
    [MemberData(nameof(SpecifiedResolutions))]
    public void TheSpecifiedUrl_IsProducedByTheSourceLinkOwner(
        string because, string map, string documentPath, string? expected)
    {
        Assert.Equal(expected, SLF.SourceLinkResolver.Parse(map).ResolveUrl(documentPath));
        Assert.NotEmpty(because);
    }

    [Theory]
    [MemberData(nameof(SpecifiedResolutions))]
    public void TheSpecifiedUrl_IsProducedThroughTheMetadataEntryPoint(
        string because, string map, string documentPath, string? expected)
    {
        Assert.Equal(expected, SourceDocumentPath.Resolve(documentPath, map).ResolvedUrl);
        Assert.NotEmpty(because);
    }

    /// <summary>
    /// The rows the collapse actually decided: inputs on which at least one replaced matcher
    /// produced something other than the specified answer.
    /// </summary>
    private static readonly string[] RowsTheCollapseDecided =
    [
        "most specific key wins regardless of document order",
        "document paths compare case-insensitively",
        "a space in a document name is percent-encoded",
        "a hash in a document name is percent-encoded",
        "a url template with two wildcards is dropped",
        "a url wildcard is substituted wherever it appears",
    ];

    /// <summary>
    /// Keeps <see cref="SpecifiedResolutions"/> honest by pinning which of its rows the collapse
    /// decided, as a set rather than a count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row on which both replaced matchers already produced the specified answer gates nothing
    /// about this change: it passed before and passes after. Such rows are still worth carrying
    /// as conformance anchors, so they are kept and labelled rather than deleted — but the label
    /// has to be true, or the distinction is decoration.
    /// </para>
    /// <para>
    /// This derives the decided set from the replaced matchers and asserts set equality against
    /// <see cref="RowsTheCollapseDecided"/>, so both failure directions are covered: promoting an
    /// already-agreed row into the list fails, and weakening a genuinely decided row until the
    /// old matchers would have got it right also fails. Asserting a count instead would let one
    /// row be swapped for another.
    /// </para>
    /// <para>
    /// Reconstructing the replaced behaviour below is an independent oracle, which a harness
    /// owns; it is not a second implementation of the product rule, because no product code
    /// consults it. The key-conformance tests in <c>SourceLinkUrlResolutionTests</c> guard
    /// themselves the same way.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRowsTheCollapseDecided_AreExactlyTheOnesPinnedAsDecided()
    {
        var decided = Rows
            .Where(row =>
                ReplacedFetchMatcher(row.Map, row.DocumentPath) != row.Expected
                || ReplacedMetadataMatcher(row.Map, row.DocumentPath) != row.Expected)
            .Select(row => row.Because)
            .Order();

        Assert.Equal(RowsTheCollapseDecided.Order(), decided);
    }

    /// <summary>
    /// The worked example from the specification, quoted verbatim, with the outcomes the
    /// specification states in prose: everything under <c>bar</c> and <c>foo</c> takes its own
    /// domain, <c>foo/specific.txt</c> takes the exact mapping in preference to the <c>foo</c>
    /// prefix, and anything else under <c>src</c> falls back to the default.
    /// </summary>
    [Theory]
    [InlineData(@"C:\src\bar\a.txt", "http://MyBarDomain.com/src/a.txt")]
    [InlineData(@"C:\src\foo\a.txt", "http://MyFooDomain.com/src/a.txt")]
    [InlineData(@"C:\src\foo\specific.txt", "http://MySpecificFoodDomain.com/src/specific.txt")]
    [InlineData(@"C:\src\other\a.txt", "http://MyDefaultDomain.com/src/other/a.txt")]
    public void TheSpecificationsWorkedExample_ResolvesAsTheSpecificationDescribes(
        string documentPath, string expected)
    {
        const string map = """
            {
                "documents": {
                    "C:\\src\\*":                   "http://MyDefaultDomain.com/src/*",
                    "C:\\src\\foo\\*":              "http://MyFooDomain.com/src/*",
                    "C:\\src\\foo\\specific.txt":   "http://MySpecificFoodDomain.com/src/specific.txt",
                    "C:\\src\\bar\\*":              "http://MyBarDomain.com/src/*"
                }
            }
            """;

        Assert.Equal(expected, SLF.SourceLinkResolver.Parse(map).ResolveUrl(documentPath));
    }

    /// <summary>
    /// Azure DevOps puts the wildcard in the middle of a query string rather than at the end, and
    /// the specification explicitly allows it: "If the URL contains a *, it may be anywhere in the
    /// URL." The replaced metadata matcher substituted only a trailing wildcard, so it returned
    /// this template unchanged — a URL with a literal asterisk where the path belongs, which no
    /// server can serve. Azure DevOps hosted assemblies could not resolve source at all.
    /// </summary>
    [Fact]
    public void AnAzureDevOpsMap_SubstitutesIntoTheMiddleOfTheUrl()
    {
        const string map = """
            {"documents":{"/_/*":"https://dev.azure.com/o/p/_apis/git/repositories/r/items?scopePath=/*&versionDescriptor.version=abc"}}
            """;

        string? resolved = SLF.SourceLinkResolver.Parse(map).ResolveUrl("/_/src/Foo.cs");

        Assert.Equal(
            "https://dev.azure.com/o/p/_apis/git/repositories/r/items?scopePath=/src/Foo.cs&versionDescriptor.version=abc",
            resolved);
        Assert.DoesNotContain('*', resolved!);
    }

    /// <summary>
    /// A document name carrying a wildcard is not a path. Honouring one would let a single
    /// document claim a mapping written for a whole subtree.
    /// </summary>
    [Fact]
    public void ADocumentPathCarryingAWildcard_NeverMatches()
    {
        var map = SLF.SourceLinkResolver.Parse("""{"documents":{"/_/*":"https://host/A/*"}}""");

        Assert.Null(map.ResolveUrl("/_/*"));
        Assert.Null(map.ResolveUrl("/_/src/*"));
    }

    /// <summary>
    /// A key that breaks the wildcard rules is dropped on its own rather than invalidating the
    /// map, and the drop is reported rather than being silently indistinguishable from a key that
    /// simply did not match.
    /// </summary>
    [Fact]
    public void ARejectedKey_IsReportedAndDoesNotDenyTheRestOfTheMap()
    {
        var map = SLF.SourceLinkResolver.Parse(
            """{"documents":{"/_/*/bad":"https://wrong.test/*","/_/*":"https://right.test/*"}}""");

        Assert.Equal(["/_/*/bad"], map.RejectedKeys);
        Assert.Equal("https://right.test/src/Foo.cs", map.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// A map with more than one valid reading resolves nothing, and says why. Reporting the
    /// reason is what keeps this distinguishable from a map that legitimately covers no document.
    /// </summary>
    [Fact]
    public void AMapWithMoreThanOneReading_ResolvesNothingAndSaysWhy()
    {
        var map = SLF.SourceLinkResolver.Parse(
            """{"documents":{"/_/*":"https://evil.test/*","/_/*":"https://right.test/*"}}""");

        Assert.NotNull(map.ParseError);
        Assert.True(map.IsEmpty);
        Assert.Null(map.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// Pins which product files read the SourceLink <c>documents</c> map, so a second
    /// implementation of the mapping rule cannot reappear unnoticed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seam rule 6 in <c>docs/design/inspection-layers.md</c> says a second implementation of a
    /// shared rule is a defect. That rule was already being broken here — two matchers, disagreeing
    /// on six of the nine inputs above — and nothing failed, because nothing was watching. This
    /// watches.
    /// </para>
    /// <para>
    /// <c>SourceLinkResolver</c> is the owner. <c>AssemblyInspector</c> is the remaining second
    /// reader: it walks the same map to report provenance and to audit path normalization, and it
    /// disagrees with the owner about which entry speaks for the assembly. Folding it in is the
    /// next change, so it is listed here as a known exception rather than tolerated silently — and
    /// when it goes, this list must shrink, which is why the assertion is set equality rather than
    /// a containment check.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheSourceLinkOwner_AndTheKnownSecondReader_ReadTheDocumentsMap()
    {
        string src = Path.Combine(FindRepoRoot(), "src");

        var readers = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains(".Tests", StringComparison.Ordinal))
            .Where(static file => File.ReadAllText(file).Contains("\"documents\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(src, file).Replace('\\', '/'))
            .Order();

        Assert.Equal(
            [
                "ILInspector.Metadata/AssemblyInspector.cs",
                "SourceLinkFetch/SourceLinkResolver.cs",
            ],
            readers);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "dotnet-inspect.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (dotnet-inspect.slnx).");
        return dir!.FullName;
    }

    /// <summary>
    /// Reconstructs the replaced <c>SourceLinkFetch</c> matcher: document order, ordinal
    /// comparison, no percent-encoding, and <c>Replace</c> over every wildcard in the template.
    /// </summary>
    private static string? ReplacedFetchMatcher(string map, string documentPath)
    {
        string path = documentPath.Replace('\\', '/');

        foreach (var (pattern, urlTemplate) in ReadDocumentsInOrder(map))
        {
            int star = pattern.IndexOf('*');
            if (star >= 0)
            {
                if (star != pattern.Length - 1)
                    continue;

                string prefix = pattern[..^1];
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    return urlTemplate.Replace("*", path[prefix.Length..]);
            }
            else if (path == pattern)
            {
                return urlTemplate;
            }
        }

        return null;
    }

    /// <summary>
    /// Reconstructs the replaced <c>ILInspector.Metadata</c> matcher: descending pattern length,
    /// case-insensitive comparison, per-segment escaping, and substitution only when the template
    /// ends with a wildcard.
    /// </summary>
    private static string? ReplacedMetadataMatcher(string map, string documentPath)
    {
        string path = documentPath.Replace('\\', '/');

        var ordered = ReadDocumentsInOrder(map)
            .Select(m => (Pattern: m.Key.Replace('\\', '/'), m.Value))
            .OrderByDescending(m => m.Pattern.Length);

        foreach (var (pattern, urlPattern) in ordered)
        {
            if (pattern.EndsWith('*'))
            {
                string prefix = pattern[..^1];
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string suffix = path[prefix.Length..].TrimStart('/');
                return urlPattern.EndsWith('*')
                    ? urlPattern[..^1] + string.Join('/', suffix.Split('/').Select(Uri.EscapeDataString))
                    : urlPattern;
            }

            if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
                return urlPattern;
        }

        return null;
    }

    private static List<KeyValuePair<string, string>> ReadDocumentsInOrder(string map)
    {
        using var document = System.Text.Json.JsonDocument.Parse(map);
        return [.. document.RootElement
            .GetProperty("documents")
            .EnumerateObject()
            .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString()!))];
    }
}
