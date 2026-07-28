using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// SourceLink maps are recovered from portable PDBs, which are untrusted artifact content. A map
/// that repeats a key under <c>documents</c> is read by more than one product reader, and those
/// readers select differently over the duplicate set: <see cref="SourceDocumentPathResolver"/>
/// orders by descending pattern length and takes the first match, while the repository-URL reader
/// in <c>AssemblyInspector</c> takes the first value mentioning a known host. Under permissive
/// parsing those rules can land on different origins, which is the spoofed-provenance case in
/// <c>docs/design/untrusted-data-threat-model.md</c>.
/// </summary>
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
}
