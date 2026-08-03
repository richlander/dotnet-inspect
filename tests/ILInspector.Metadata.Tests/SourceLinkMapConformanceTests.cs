using System.Buffers;
using System.Text;
using System.Text.Json;
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
    /// A commit hash, so that a GitHub row's revision/path boundary is determinable and each row
    /// below is refused or accepted for the reason it names rather than for an incidental one.
    /// </summary>
    private const string ConformanceSha = "0123456789012345678901234567890123456789";

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

        // Rule 2, in the direction the reference consumer states but does not enforce: "if the
        // file path contains a * the URL must contain a *". Honouring this entry would serve one
        // file's content as the source of every document in the subtree.
        new(
            "a wildcard key paired with a constant url is dropped",
            """{"documents":{"/_/*":"https://host/A/pinned.cs"}}""",
            "/_/src/Foo.cs",
            null),

        // "Absolute paths will be checked before a wildcard path with a matching base." The two
        // keys tie on length once the wildcard is stripped, so only the exactness rule separates
        // them. The URLs are spelled so that the exact one sorts ordinally *after* the wildcard's:
        // the comparator falls through to an ordinal URL comparison to stay total, and a pair
        // whose exact URL sorted first would reach the right answer without the exactness rule.
        new(
            "an exact key beats the wildcard key with the same base",
            """{"documents":{"/_/a.cs":"https://host/zzz-exact.cs","/_/a.cs*":"https://host/aaa-prefix/*"}}""",
            "/_/a.cs",
            "https://host/zzz-exact.cs"),
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
        "a wildcard key paired with a constant url is dropped",
        "an exact key beats the wildcard key with the same base",
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
            .Order(StringComparer.Ordinal);

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
    /// A non-string value is a malformed entry, and is rejected on the same terms as a
    /// non-conformant key: dropped individually, reported, and kept out of matching.
    /// </summary>
    /// <remarks>
    /// Letting it into the map was worse than a silent drop. Entries are ordered by specificity,
    /// so a malformed <c>/_/src/*</c> outranked a valid <c>/_/*</c> and resolved everything under
    /// it to no URL at all — a failure wearing the shape of an empty success, and one that
    /// swallowed a URL that would otherwise have resolved. The second assertion is the one that
    /// pins that: the valid, less specific entry must still be reached.
    /// </remarks>
    [Theory]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void AnEntryWhoseValueIsNotAString_IsRejectedRatherThanMatchingNothing(string value)
    {
        var map = SLF.SourceLinkResolver.Parse(
            """{"documents":{"/_/src/*":""" + value + ""","/_/*":"https://right.test/*"}}""");

        Assert.Equal(["/_/src/*"], map.RejectedKeys);
        Assert.Equal("https://right.test/src/Foo.cs", map.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// An entry whose URL names no origin source can be retrieved from is rejected on the same
    /// terms as a malformed value, rather than matching and resolving to a string nothing can
    /// fetch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the non-string defect in its other shape, and it was missed when that one was
    /// fixed. <c>"*"</c> satisfies every wildcard rule — the key has a final wildcard, the URL has
    /// exactly one — so it entered the map and resolved <c>/_/src/Foo.cs</c> to <c>Foo.cs</c>. As
    /// the more specific entry it outranked a valid <c>/_/*</c>, so the second assertion is again
    /// the load-bearing one: the entry that should have won must still be reached.
    /// </para>
    /// <para>
    /// The accept rows are what <c>Microsoft.SourceLink.GitHub</c> and
    /// <c>Microsoft.SourceLink.AzureRepos.Git</c> actually generate, so a rule that refused a real
    /// map would fail here rather than in the field.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("*", false)]
    [InlineData("Foo/*", false)]
    [InlineData("/nope/*", false)]
    [InlineData("//evil.test/*", false)]
    [InlineData("right.test/*", false)]
    [InlineData("file:///tmp/*", false)]
    [InlineData("ftp://h.test/*", false)]
    [InlineData("javascript:alert(1)/*", false)]
    [InlineData("https://*.test/x/*", false)]
    [InlineData("https://example.test:*/fixed.cs", false)]
    [InlineData("htt*ps://h.test/x", false)]
    [InlineData("https://user:*@h.test/x", false)]
    [InlineData("https://h.test/README.md#*", false)]
    [InlineData("https://h.test/a/*/../fixed.cs", false)]
    [InlineData("https://h.test/*%2f..%2ffixed.cs", false)]
    [InlineData("https://h.test/*%2F..%2Ffixed.cs", false)]
    [InlineData("https://h.test/*%5c..%5cfixed.cs", false)]
    [InlineData("https://dev.azure.com/c/w/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=0123456789012345678901234567890123456789&path=/*/../fixed.cs", false)]
    [InlineData("https://dev.azure.com/c/w/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=0123456789012345678901234567890123456789&path=/*%2f..%2ffixed.cs", false)]
    [InlineData("https://dev.azure.com/c/w/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=0123456789012345678901234567890123456789&path=*/../fixed.cs", false)]
    [InlineData("https://h.test/i?path=*/../fixed.cs", false)]
    [InlineData("https://h.test/i?a=x/../y&path=*", true)]
    [InlineData("https://h.test/x?p=*&e=a%2fb", true)]
    [InlineData("https://h.test/x?p=*&note=a/../b", true)]
    [InlineData("https://raw.githubusercontent.com/o/r/0123456789012345678901234567890123456789/*", true)]
    [InlineData("http://internal.test/src/*", true)]
    [InlineData("https://h.test/x?p=*&q=1", true)]
    [InlineData("https://dev.azure.com/c/w/_apis/git/repositories/core/items?api-version=1.0&versionType=commit&version=0123456789012345678901234567890123456789&path=/*", true)]
    public void AnEntryWhoseUrlNamesNoFetchableOrigin_IsRejectedRatherThanShadowingAValidEntry(
        string url,
        bool accepted)
    {
        var map = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{\"/_/src/*\":\"" + url +
            "\",\"/_/*\":\"https://right.test/*\"}}");

        if (accepted)
        {
            Assert.Empty(map.RejectedKeys);
            Assert.NotEqual("https://right.test/src/Foo.cs", map.ResolveUrl("/_/src/Foo.cs"));
            return;
        }

        Assert.Equal(["/_/src/*"], map.RejectedKeys);
        Assert.Equal("https://right.test/src/Foo.cs", map.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// Pins each refused URL shape against a close twin that differs only in the cause, so a row
    /// cannot pass because some unrelated rule happened to refuse it too.
    /// </summary>
    /// <remarks>
    /// This is the round-6 lesson applied to a rule with three ways to fail. Asserting only that
    /// <c>https://h.test/README.md#*</c> is refused would still pass if the refusal came from
    /// something incidental to fragments; pairing it with <c>https://h.test/README.md?x=*</c>,
    /// which differs by one character and is accepted, is what makes the row say <em>the fragment
    /// is why</em>. Every twin here is a URL a real map could carry, so a rule that overreached
    /// from the cause to its neighbourhood fails on the second assertion.
    /// </remarks>
    [Theory]
    // A wildcard in the port is refused; the same wildcard one component along is a path.
    [InlineData("https://h.test:*/fixed.cs", "https://h.test/*/fixed.cs")]
    // A wildcard in the user information chooses the origin; credentials themselves do not.
    [InlineData("https://user:*@h.test/x", "https://user:pw@h.test/*")]
    // A fragment is never transmitted; a query is.
    [InlineData("https://h.test/README.md#*", "https://h.test/README.md?x=*")]
    // Dot-segment removal erases the substitution; without the '..' it survives.
    [InlineData("https://h.test/a/*/../fixed.cs", "https://h.test/a/*/fixed.cs")]
    // An encoded separator hides the traversal from Uri but not from a server that decodes
    // first; an encoded separator that erases nothing is still accepted.
    [InlineData("https://h.test/*%2f..%2ffixed.cs", "https://h.test/*%2ffixed.cs")]
    // A wildcard in the host chooses the origin; in the first path segment it does not.
    [InlineData("https://*.test/x", "https://h.test/*/x")]
    // A traversal inside a query value erases the substitution just as one in the path does; a
    // '..' that erases nothing but the wildcard's own neighbour leaves the document choosing.
    [InlineData("https://h.test/i?path=/*/../fixed.cs", "https://h.test/i?path=/*/fixed.cs")]
    // The same traversal with the wildcard first in its value, so no '/' separates it from the
    // parameter name; a '..' confined to a different parameter erases nothing the document chose.
    [InlineData("https://h.test/i?path=*/../fixed.cs", "https://h.test/i?a=x/../y&path=*")]
    public void EachRefusedUrlShape_IsRefusedForItsOwnReasonAndNotAnIncidentalOne(
        string refused,
        string acceptedTwin)
    {
        var refusedMap = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{\"/_/src/*\":\"" + refused +
            "\",\"/_/*\":\"https://right.test/*\"}}");

        Assert.Equal(["/_/src/*"], refusedMap.RejectedKeys);
        Assert.Equal("https://right.test/src/Foo.cs", refusedMap.ResolveUrl("/_/src/Foo.cs"));

        var twinMap = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{\"/_/src/*\":\"" + acceptedTwin +
            "\",\"/_/*\":\"https://right.test/*\"}}");

        Assert.Empty(twinMap.RejectedKeys);
        Assert.NotEqual("https://right.test/src/Foo.cs", twinMap.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// A wildcard key that names a document exactly substitutes nothing, and that is correct: the
    /// URL's wildcard stands where the document's own name already is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised in review as a bypass, on this map, where the more specific entry resolves
    /// <c>/_/README.md</c> to the repository root and shadows a fallback that would have resolved
    /// it correctly:
    /// </para>
    /// <code>
    /// {"/_/README.md*": "https://…/&lt;sha&gt;/*", "/_/*": "https://…/&lt;sha&gt;/*"}
    /// </code>
    /// <para>
    /// That map is simply wrong — its key says "documents beginning <c>/_/README.md</c>" while its
    /// URL prefix does not name <c>README.md</c> — but no local rule can tell it from the right
    /// one, because SourceLink deliberately does not constrain how a key's text relates to its
    /// URL's. Measured live: the consistent map below resolves to a URL that returns HTTP 200,
    /// and the one above to the repository root, which returns 404. Refusing an empty
    /// substitution would therefore break the working map to spare the broken one, which is why
    /// this is a gate rather than a fix.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWildcardKeyThatNamesADocumentExactly_SubstitutesNothingAndStillNamesIt()
    {
        const string Sha = "0123456789012345678901234567890123456789";
        const string Prefix = "https://raw.githubusercontent.com/o/r/" + Sha + "/";

        var map = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{" +
            "\"/_/README.md*\":\"" + Prefix + "README.md*\"," +
            "\"/_/*\":\"" + Prefix + "*\"}}");

        Assert.Empty(map.RejectedKeys);
        Assert.Equal(Prefix + "README.md", map.ResolveUrl("/_/README.md"));

        // The wildcard still carries whatever follows the key, so the entry is a prefix rule and
        // not an exact one.
        Assert.Equal(Prefix + "README.md.bak", map.ResolveUrl("/_/README.md.bak"));

        // And the less specific entry still governs everything the more specific one misses.
        Assert.Equal(Prefix + "src/Foo.cs", map.ResolveUrl("/_/src/Foo.cs"));
    }

    /// <summary>
    /// A wildcard confined to the query is accepted, because the same shape is how Azure Repos
    /// names the document and how a query-ignoring host names nothing at all. Which one it is is
    /// host knowledge this matcher does not have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised in review as a bypass: aimed at <c>raw.githubusercontent.com</c>, the first URL
    /// below matches the whole subtree, outranks a valid fallback, and serves one file as the
    /// source of every document, because that host ignores the query. Measured, both halves:
    /// </para>
    /// <code>
    /// .../CSharpDeclarationWriter.cs?document=A.cs    same bytes
    /// .../CSharpDeclarationWriter.cs?document=B.cs    same bytes
    /// .../CSharpDeclarationWriter.cs                  same bytes   &lt;- query ignored
    ///
    /// dev.azure.com/.../items?...&amp;path=/README.md     200
    /// dev.azure.com/.../items?...&amp;path=/nope          404          &lt;- query selects
    /// </code>
    /// <para>
    /// So <em>this matcher</em> cannot refuse the shape: it is exactly what
    /// <c>Microsoft.SourceLink.AzureRepos.Git</c> generates, and refusing it would reintroduce the
    /// failure this matcher was collapsed to fix. Telling the two apart needs a per-host content
    /// selector, which belongs with the host grammars in <c>SourceLinkProvenance</c>, not here.
    /// </para>
    /// <para>
    /// That selector now exists for the host where the answer is decidable. A later review showed
    /// the deferral was too generous: with the wildcard confined to the query, provenance
    /// <em>established</em> an origin for a map where every document fetches one file, so the
    /// correspondence gap was visible to the user as a clean attribution. Provenance therefore
    /// refuses a <c>raw.githubusercontent.com</c> URL carrying a query at all, and it also
    /// requires the substituted text to land in the component that selects content, which is the
    /// Azure spelling of the same defect — a substitution in <c>api-version</c> varies the
    /// request without varying the file.
    /// </para>
    /// <para>
    /// Issue #3599's <em>resolution</em> half is now closed too, and this test was renamed for
    /// it: the matcher no longer accepts a shape whose substitution the host cannot read, so such
    /// a map fetches nothing rather than fetching one file unattributed. The deferral in the
    /// paragraph above still stands and is still the point — the matcher does not decide this by
    /// itself, it asks <c>SourceLinkProvenance</c> which component the host reads. That is why
    /// the Azure rows below keep working while the GitHub row is refused, and it is why the
    /// refusal is a <em>rejected key</em> rather than a resolution failure: a rejected entry
    /// stops shadowing the valid fallback, so the documents below now resolve to their own files.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWildcardConfinedToTheQuery_IsRefusedOnlyOnAHostKnownNotToReadIt()
    {
        const string Sha = "0123456789012345678901234567890123456789";
        const string Raw = "https://raw.githubusercontent.com/o/r/" + Sha + "/";

        var map = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{" +
            "\"/_/src/*\":\"" + Raw + "One.cs?document=*\"," +
            "\"/_/*\":\"" + Raw + "*\"}}");

        Assert.Equal(["/_/src/*"], map.RejectedKeys);

        // The refused entry no longer shadows the valid fallback, so each document resolves to
        // its own file instead of all of them resolving to One.cs.
        Assert.Equal(Raw + "src/One.cs", map.ResolveUrl("/_/src/One.cs"));
        Assert.Equal(Raw + "src/Two.cs", map.ResolveUrl("/_/src/Two.cs"));

        // The real Azure Repos shape is the same shape, and must keep working.
        var azure = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{\"/_/*\":\"https://dev.azure.com/c/w/_apis/git/repositories/core/items" +
            "?api-version=1.0&versionType=commit&version=" + Sha + "&path=/*\"}}");

        Assert.Empty(azure.RejectedKeys);

        // The matcher refuses only where the host is known not to read the substitution, so the
        // GitHub map above now attributes cleanly rather than being refused: what it resolves to
        // is no longer one file for every document.
        var provenance = SLF.SourceLinkProvenance.Determine(map, ["/_/src/One.cs", "/_/src/Two.cs"]);
        Assert.True(provenance.IsEstablished, provenance.Reason);

        var azureProvenance = SLF.SourceLinkProvenance.Determine(azure, ["/_/src/One.cs"]);
        Assert.True(azureProvenance.IsEstablished, azureProvenance.Reason);
    }

    /// <summary>
    /// The resolution half of issue #3599: an entry is refused when, and only when, the host is
    /// one this reader can speak for and the substitution lands where that host will not read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows are the discriminator, not the refusal. The obvious reading of #3599 — refuse to
    /// resolve wherever provenance refuses to attribute — passes the two refuse rows and breaks
    /// three of the four accept rows, so a fix built that way would look correct against the
    /// defect and silently stop resolving source for correct maps. Each accept row is therefore a
    /// shape that is <em>unattributable and still fetches the right file</em>:
    /// </para>
    /// <list type="bullet">
    ///   <item>a GitHub path wildcard with an inert query beside it — the query is ignored, which
    ///   is exactly why the path still selects;</item>
    ///   <item>a branch-based GitHub map — unattributable only because the revision/path boundary
    ///   is not determinable, which has no bearing on what is fetched;</item>
    ///   <item>a self-hosted host — unattributable because its grammar is unknown, and refusing it
    ///   would strand every SourceLink deployment outside the two hosts written down here.</item>
    /// </list>
    /// <para>
    /// The last of those is the one that makes this a gate rather than a comment: it is the row
    /// that fails loudly if the two predicates are ever collapsed into one.
    /// </para>
    /// </remarks>
    [Theory]
    // Refused: the substitution cannot select content on a host whose grammar is known.
    [InlineData("https://raw.githubusercontent.com/o/r/" + ConformanceSha + "/One.cs?document=*", false)]
    [InlineData(
        "https://dev.azure.com/org/proj/_apis/git/repositories/repo/items?api-version=*"
        + "&versionType=commit&version=" + ConformanceSha + "&path=/One.cs",
        false)]
    // Accepted: unattributable, yet each fetches the document it matched.
    [InlineData("https://raw.githubusercontent.com/o/r/" + ConformanceSha + "/*?foo=bar", true)]
    [InlineData("https://raw.githubusercontent.com/o/r/main/*", true)]
    [InlineData("https://srclink.contoso.test/raw/*", true)]
    // Accepted and attributable: the shape Microsoft.SourceLink.AzureRepos.Git generates.
    [InlineData(
        "https://dev.azure.com/org/proj/_apis/git/repositories/repo/items?api-version=1.0"
        + "&versionType=commit&version=" + ConformanceSha + "&path=/*",
        true)]
    public void OnlyAnEntryThatCannotSelectContent_IsRefusedResolution(string url, bool resolves)
    {
        var map = SLF.SourceLinkResolver.Parse(
            "{\"documents\":{\"/_/*\":\"" + url + "\"}}");

        Assert.Equal(resolves, map.ResolveUrl("/_/src/Program.cs") is not null);
        Assert.Equal(resolves, map.RejectedKeys.Count == 0);
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
    /// <c>SourceLinkResolver</c> is the owner, and now the only reader. <c>AssemblyInspector</c>
    /// used to walk the same map to report provenance and to audit path normalization, and it
    /// disagreed with the owner about which entry speaks for the assembly; it now asks the owner
    /// for both. The assertion is set equality rather than containment precisely so that this
    /// shrinking had to be made here as well as in the product.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheSourceLinkOwner_ReadsTheDocumentsMap()
    {
        string src = Path.Combine(FindRepoRoot(), "src");

        var readers = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.Contains(".Tests", StringComparison.Ordinal))
            .Where(static file => File.ReadAllText(file).Contains("\"documents\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(src, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["SourceLinkFetch/SourceLinkResolver.cs"], readers);
    }

    /// <summary>
    /// A map means the same thing however its keys happen to be enumerated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the general form of the defect two reviewers found independently. Ordering entries
    /// by prefix length alone leaves ties, and <c>List&lt;T&gt;.Sort</c> is unstable, so a tie was
    /// decided by JSON enumeration order — the exact document-order dependence the collapse set
    /// out to remove, still present in the implementation that claimed to remove it.
    /// </para>
    /// <para>
    /// A gate naming the one input that exposed it would be satisfied by a fix that special-cases
    /// that input, so this asserts the property over every conformance row and every specificity
    /// case below. What it does <em>not</em> cover is a tie that survives the length and exactness
    /// rules: no row here spells one, so the ordinal fall-through is never reached and erasing it
    /// leaves this test green. Raised in review, and confirmed by erasing both ordinal comparisons
    /// and watching the whole suite pass.
    /// <c>AMapWhoseKeysTieOnLengthAndKind_ResolvesTheSameWhicheverOrderTheyAreWrittenIn</c> covers
    /// that case; the claim is split between the two rather than overstated here.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SpecifiedResolutions))]
    public void AMapsMeaning_DoesNotDependOnTheOrderItsKeysAreWritten(
        string because, string map, string documentPath, string? expected)
    {
        Assert.Equal(expected, SLF.SourceLinkResolver.Parse(ReverseDocumentOrder(map)).ResolveUrl(documentPath));
        Assert.NotEmpty(because);
    }

    /// <summary>
    /// Two keys that tie on length and on kind still resolve the same way whichever order the map
    /// writes them in. This is where the comparison has to be a total order rather than merely
    /// usually right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparator's own comment names the only shapes that reach it: keys of equal length and
    /// equal kind cannot both match one document "unless they differ only by case or by
    /// separator", which this reader accepts because it compares paths case-insensitively and
    /// normalizes separators on both sides. Those are the two rows.
    /// </para>
    /// <para>
    /// Measured non-vacuity, since a tie test that does not tie is exactly the defect this exists
    /// for: with the ordinal fall-through erased, the winner follows JSON order — <c>a.test</c>
    /// when written first, <c>b.test</c> when written second — and both rows fail.
    /// </para>
    /// <para>
    /// The two rows do not gate the same comparison, and saying they both gate the URL comparison
    /// was wrong. Measured, one mutation at a time: erasing <c>byUrlPrefix</c> fails the separator
    /// row only, because separators normalize to one <c>PathPrefix</c> so <c>byPrefix</c> ties
    /// there and the URL decides; the case row never reaches the URL, since <c>SRC</c> and
    /// <c>src</c> are ordinally distinct prefixes. Erasing <c>byPrefix</c> alone fails
    /// <em>nothing</em> across the whole suite — the URL comparison then decides the case row and
    /// both write orders still agree. So what these rows gate together is that <em>some</em>
    /// deterministic comparison survives past exactness, plus <c>byUrlPrefix</c> specifically; the
    /// comparator records <c>byPrefix</c> as ungated rather than claiming a gate it does not have.
    /// </para>
    /// </remarks>
    [Theory]
    // Keys differing only by case; both match, and neither is more specific.
    [InlineData("/_/SRC/*", "/_/src/*")]
    // Keys differing only by separator, which normalizes to one prefix on both sides.
    [InlineData("/_\\src\\*", "/_/src/*")]
    public void AMapWhoseKeysTieOnLengthAndKind_ResolvesTheSameWhicheverOrderTheyAreWrittenIn(
        string firstKey,
        string secondKey)
    {
        const string Document = "/_/src/Foo.cs";

        string written = Resolve(firstKey, "https://a.test/*", secondKey, "https://b.test/*");
        string reversed = Resolve(secondKey, "https://b.test/*", firstKey, "https://a.test/*");

        Assert.Equal(written, reversed);

        // Non-vacuity: the pair really does tie, so both entries were candidates for the document.
        Assert.Contains("Foo.cs", written, StringComparison.Ordinal);

        static string Resolve(string keyA, string urlA, string keyB, string urlB)
        {
            // A separator key carries a backslash, which JSON requires escaped.
            var map = SLF.SourceLinkResolver.Parse(
                "{\"documents\":{\"" + Json(keyA) + "\":\"" + urlA + "\",\"" + Json(keyB) + "\":\"" + urlB + "\"}}");

            Assert.Empty(map.RejectedKeys);
            return map.ResolveUrl(Document) ?? "<unresolved>";
        }

        static string Json(string key) => key.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// An exact key and the wildcard key with the same base tie on length once the wildcard is
    /// stripped. The reference consumer states the intent — "absolute paths will be checked before
    /// a wildcard path with a matching base" — but orders by length alone, so it does not achieve
    /// it.
    /// </summary>
    /// <remarks>
    /// Both document orders are asserted, because a tie broken by enumeration order passes one of
    /// them. Both URL orderings are asserted for the same reason one layer down: the comparator
    /// falls through to an ordinal comparison of the URLs to stay total, so a pair whose exact URL
    /// happens to sort first reaches the right answer even with the exactness rule deleted. The
    /// second row spells the exact URL so that it sorts <em>after</em> the wildcard's, leaving
    /// exactness as the only thing that can decide it.
    /// </remarks>
    [Theory]
    [InlineData("https://host/exact.cs", "https://host/prefix/*")]
    [InlineData("https://host/zzz-exact.cs", "https://host/aaa-prefix/*")]
    public void AnExactKey_BeatsTheWildcardKeyWithTheSameBase(string exactUrl, string prefixUrl)
    {
        // Both document orders, because a tie broken by enumeration order passes one of them.
        string exactFirst = $$$"""{"documents":{"/_/a.cs":"{{{exactUrl}}}","/_/a.cs*":"{{{prefixUrl}}}"}}""";
        string prefixFirst = $$$"""{"documents":{"/_/a.cs*":"{{{prefixUrl}}}","/_/a.cs":"{{{exactUrl}}}"}}""";

        foreach (string map in (string[])[exactFirst, prefixFirst])
        {
            Assert.Equal(exactUrl, SLF.SourceLinkResolver.Parse(map).ResolveUrl("/_/a.cs"));
            Assert.Equal(exactUrl, SourceDocumentPath.Resolve("/_/a.cs", map).ResolvedUrl);
        }
    }

    /// <summary>
    /// A wildcard key can consume a document path entirely, leaving no remainder to name the
    /// document by. That is the same situation as a key carrying no wildcard at all, and it must
    /// reach the same answer: the document keeps its own name. Reporting the empty remainder
    /// would erase the document's identity while still reporting a URL for it.
    /// </summary>
    [Fact]
    public void AWildcardKeyThatConsumesTheWholePath_StillNamesTheDocument()
    {
        const string map = """{"documents":{"/_/src/a.cs*":"https://host/prefix/*"}}""";

        var resolution = SourceDocumentPath.Resolve("/_/src/a.cs", map);

        Assert.Equal("src/a.cs", resolution.CanonicalPath);
        Assert.Equal("https://host/prefix/", resolution.ResolvedUrl);
        Assert.True(resolution.IsMapped);
    }

    /// <summary>
    /// A map whose shape has no reading at all fails visibly instead of resolving nothing quietly.
    /// </summary>
    /// <remarks>
    /// The whole map is attacker-controlled. Returning an empty resolver for a structurally
    /// invalid one makes malformed input indistinguishable from an assembly that ships no
    /// SourceLink, which is the success-shaped empty output the repository's failure-visibility
    /// rule forbids. A root carrying only unknown properties is a different case and stays silent:
    /// the format reserves those for extensibility, so such a map declares no documents rather
    /// than failing to declare them.
    /// </remarks>
    [Theory]
    [InlineData("""{"documents":[]}""", true)]
    [InlineData("""{"documents":"nope"}""", true)]
    [InlineData("""{"documents":null}""", true)]
    [InlineData("""["documents"]""", true)]
    [InlineData(""""a string"""", true)]
    [InlineData("""{"version":2}""", false)]
    [InlineData("""{"documents":{}}""", false)]
    public void AStructurallyInvalidMap_SaysWhyRatherThanResolvingNothingQuietly(
        string map, bool expectedToFail)
    {
        var resolver = SLF.SourceLinkResolver.Parse(map);

        Assert.Equal(expectedToFail, resolver.ParseError is not null);
        Assert.Null(resolver.ResolveUrl("/_/a.cs"));
    }

    /// <summary>
    /// Rewrites a map with its <c>documents</c> entries in the opposite order, preserving each
    /// key and value exactly. Used to assert that meaning is independent of enumeration order.
    /// </summary>
    private static string ReverseDocumentOrder(string map)
    {
        using var document = JsonDocument.Parse(map);
        var entries = document.RootElement.GetProperty("documents").EnumerateObject().Reverse().ToList();

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("documents");
            foreach (var entry in entries)
                entry.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
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
