using System.Text.Json;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// SourceLink maps are recovered from portable PDBs, which are untrusted artifact content. JSON
/// leaves duplicate object keys undefined, so a map that repeats a key under <c>documents</c> has
/// more than one valid reading. These tests pin that such a map fails the parse rather than
/// binding one of its readings.
/// </summary>
/// <remarks>
/// This is fail-visible hardening, not a fix for a reader divergence. Both product readers happen
/// to select the first entry for a duplicated key — <c>AssemblyInspector</c> stops at the first
/// <c>documents</c> entry, and <see cref="SourceDocumentPathResolver"/>'s descending pattern-length
/// sort is stable, so equal-length duplicates keep document order. They diverge only on maps with
/// <em>distinct</em> keys, which stay well-formed and accepted; see the SourceLink open-work entry
/// in <c>docs/design/untrusted-data-threat-model.md</c>.
/// </remarks>
public class SourceLinkDuplicateDocumentTests
{
    private const string DuplicateEntryMap = """
        {"documents":{"/_/*":"https://evil.example/raw/*","/_/*":"https://github.com/o/r/raw/*"}}
        """;

    [Fact]
    public void Resolve_DoesNotMapThroughADuplicatedDocumentsEntry()
    {
        var resolution = SourceDocumentPath.Resolve("/_/src/Program.cs", DuplicateEntryMap);

        // Fail closed: no mapping at all, rather than silently binding one of the two origins.
        Assert.False(resolution.IsMapped);
        Assert.Null(resolution.ResolvedUrl);
    }

    [Fact]
    public void Resolve_DoesNotYieldTheAttackerOriginFromADuplicatedEntry()
    {
        var resolution = SourceDocumentPath.Resolve("/_/src/Program.cs", DuplicateEntryMap);

        Assert.DoesNotContain("evil.example", resolution.ResolvedUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsADuplicatedTopLevelDocumentsObject()
    {
        const string map = """
            {"documents":{"/_/*":"https://github.com/o/r/raw/*"},"documents":{"/_/*":"https://evil.example/raw/*"}}
            """;

        var resolution = SourceDocumentPath.Resolve("/_/src/Program.cs", map);

        Assert.False(resolution.IsMapped);
        Assert.Null(resolution.ResolvedUrl);
    }

    [Fact]
    public void Resolve_StillMapsAWellFormedSourceLinkDocument()
    {
        const string map = """
            {"documents":{"/_/*":"https://raw.githubusercontent.com/o/r/abc123/*"}}
            """;

        var resolution = SourceDocumentPath.Resolve("/_/src/Program.cs", map);

        Assert.True(resolution.IsMapped);
        Assert.Equal("src/Program.cs", resolution.CanonicalPath);
        Assert.Equal("https://raw.githubusercontent.com/o/r/abc123/src/Program.cs", resolution.ResolvedUrl);
    }

    [Fact]
    public void Resolve_StillMapsAMapWithSeveralDistinctPatterns()
    {
        // Distinct keys are not duplicates; multi-root maps must keep working.
        const string map = """
            {"documents":{"/_/*":"https://host.example/a/*","/_/sub/*":"https://host.example/b/*"}}
            """;

        var resolution = SourceDocumentPath.Resolve("/_/sub/File.cs", map);

        Assert.True(resolution.IsMapped);
        Assert.Equal("https://host.example/b/File.cs", resolution.ResolvedUrl);
    }

    [Fact]
    public void Resolve_LeavesUnmappedPathsUnmappedWithoutASourceLinkMap()
    {
        var resolution = SourceDocumentPath.Resolve("/_/src/Program.cs", sourceLinkJson: null);

        Assert.False(resolution.IsMapped);
        Assert.Equal("src/Program.cs", resolution.CanonicalPath);
    }

    /// <summary>
    /// The source-generated cache reader is a separate parse path from <c>SourceLinkJson</c>, so it
    /// needs its own duplicate rejection. This gates the "product cache entries parse through the
    /// same guard" claim in <c>docs/design/untrusted-data-threat-model.md</c>; without
    /// <c>AllowDuplicateProperties = false</c> on the context the last value would silently win.
    /// </summary>
    [Fact]
    public void TypeFileIndexCache_RejectsDuplicateKeys()
    {
        const string cached = """
            {"A.B":["first.cs"],"A.B":["second.cs"]}
            """;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize(cached, SourceLinkJsonContext.Default.DictionaryStringStringArray));
    }

    /// <summary>
    /// The cache is written and read by the same context, so hardening the reader must not break
    /// the write/read round trip that source lookup depends on.
    /// </summary>
    [Fact]
    public void TypeFileIndexCache_RoundTripsWhatItWrites()
    {
        var index = new Dictionary<string, string[]>
        {
            ["A.B"] = ["one.cs", "two.cs"],
            ["A.C"] = [],
        };

        string written = JsonSerializer.Serialize(index, SourceLinkJsonContext.Default.DictionaryStringStringArray);
        var read = JsonSerializer.Deserialize(written, SourceLinkJsonContext.Default.DictionaryStringStringArray);

        Assert.NotNull(read);
        Assert.Equal(["one.cs", "two.cs"], read["A.B"]);
        Assert.Empty(read["A.C"]);
    }

    [Fact]
    public void TypeFileIndexCache_StillReadsDistinctKeys()
    {
        const string cached = """
            {"A.B":["first.cs"],"A.C":["second.cs"]}
            """;

        var index = JsonSerializer.Deserialize(cached, SourceLinkJsonContext.Default.DictionaryStringStringArray);

        Assert.NotNull(index);
        Assert.Equal(["first.cs"], index["A.B"]);
        Assert.Equal(["second.cs"], index["A.C"]);
    }
}
