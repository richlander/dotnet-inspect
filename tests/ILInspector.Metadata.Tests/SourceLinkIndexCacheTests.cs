using ILInspector.Metadata;
using SLF = SourceLinkFetch;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The persistent type-to-file index is keyed per assembly, not per repository. These live in a
/// SourceLink-named class deliberately: the mutation battery selects <c>*SourceLink*</c>, and a
/// gate it does not run is a gate that is not there.
/// </summary>
public class SourceLinkIndexCacheTests
{
    /// <summary>
    /// The cached index key names this assembly's symbols as well as its origin, so two assemblies
    /// built from one repository at one revision cannot share an entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This gates the composition directly rather than through a built assembly, because the
    /// behavioural version of this test is silently vacuous whenever the assembly under test
    /// carries no SourceLink data: no origin means no key, no key means no caching, and no caching
    /// means two assemblies trivially do not share an entry. A dirty worktree is enough to
    /// suppress SourceLink emission, so that is not a remote condition — it was how the gap was
    /// found, by mutating the key back to origin-only and watching every test still pass.
    /// </para>
    /// <para>
    /// Declining the key when either identity is missing is asserted too. A cache miss costs an
    /// index rebuild; a key that does not name the assembly returns another assembly's files.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCachedIndexKey_NamesTheSymbolsAndNotOnlyTheOrigin()
    {
        const string Origin = "8:host.com|1:o|1:r|3:abc|";
        var symbols = new CodeViewInfo(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, "a.pdb", true);
        var otherSymbols = new CodeViewInfo(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, "b.pdb", true);

        string? first = SourceLinkService.BuildIndexCacheKey(Origin, symbols);
        string? second = SourceLinkService.BuildIndexCacheKey(Origin, otherSymbols);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);

        // Same inputs, same key: the collision this prevents is between assemblies, not between
        // two reads of one assembly, which must still hit the cache.
        Assert.Equal(first, SourceLinkService.BuildIndexCacheKey(Origin, symbols));

        // The age is part of the identity, so two builds sharing a GUID still separate.
        Assert.NotEqual(first, SourceLinkService.BuildIndexCacheKey(Origin, symbols with { Age = 2 }));

        // And the origin still separates two assemblies whose symbols somehow agree, which a
        // deterministic build of identical content across forks can produce.
        Assert.NotEqual(first, SourceLinkService.BuildIndexCacheKey("8:host.com|1:o|1:s|3:abc|", symbols));

        // No identity, no key: caching is declined rather than weakened.
        Assert.Null(SourceLinkService.BuildIndexCacheKey(null, symbols));
        Assert.Null(SourceLinkService.BuildIndexCacheKey(Origin, null));
    }

    /// <summary>
    /// Two assemblies built from one repository at one revision do not share a cached type-to-file
    /// index. The index describes one assembly's types, so a key that names only the origin hands
    /// the second assembly the first one's source files.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised in review, and reproduced with this repository's own output: both assemblies below
    /// declare a <c>SourceLinkResolver</c>, they carry the same origin, and through a shared cache
    /// the second was answered with the first's file. The assertion is on the files rather than on
    /// the key, so it fails for the defect rather than for the fix's shape, and it holds whether
    /// or not SourceLink data is present -- when it is absent no key is formed and each index is
    /// built directly, which is the same required outcome.
    /// </para>
    /// <para>
    /// The two probes are loaded assemblies rather than build-output paths, so this cannot pass by
    /// naming a file that is no longer there.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoAssembliesFromOneOrigin_DoNotShareACachedTypeIndex()
    {
        string fetchPath = typeof(SLF.SourceLinkResolver).Assembly.Location;
        string sourceLinkPath = typeof(SourceLinkService).Assembly.Location;
        Assert.NotEqual(fetchPath, sourceLinkPath);

        var shared = new RecordingIndexCache();

        string[] fromFetch;
        using (var first = SourceLinkService.Open(fetchPath, null, shared))
            fromFetch = first.GetTrackedFilesForType("SourceLinkResolver");

        string[] fromSourceLink;
        using (var second = SourceLinkService.Open(sourceLinkPath, null, shared))
            fromSourceLink = second.GetTrackedFilesForType("SourceLinkResolver");

        // Non-vacuity: both assemblies really do declare the type, so an empty result would mean
        // the probe stopped exercising the collision rather than that the collision is gone.
        Assert.NotEmpty(fromFetch);
        Assert.NotEmpty(fromSourceLink);

        Assert.Contains(fromFetch, f => f.Replace('\\', '/').Contains("/SourceLinkFetch/"));
        Assert.Contains(fromSourceLink, f => f.Replace('\\', '/').Contains("/ILInspector.SourceLink/"));
        Assert.DoesNotContain(fromSourceLink, f => f.Replace('\\', '/').Contains("/SourceLinkFetch/"));

        // And when keys were formed at all, the two assemblies formed different ones.
        Assert.True(shared.Keys.Count is 0 or 2, $"expected 0 or 2 keys, saw {shared.Keys.Count}");
    }

    [Fact]
    public void PdbLoadedThroughContext_InvalidatesServiceState()
    {
        string assemblyPath = typeof(SourceLinkIndexCacheTests).Assembly.Location;
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(File.Exists(pdbPath));

        using var source = SourceLinkService.Open(assemblyPath);
        var first = source.GetTrackedFiles();
        int version = source.Context.PdbVersion;

        source.Context.LoadPdbFromFile(pdbPath);

        Assert.True(source.Context.PdbVersion > version);
        var second = source.GetTrackedFiles();
        Assert.NotSame(first, second);
        Assert.Equal(
            first.Select(static document => document.FilePath),
            second.Select(static document => document.FilePath));
    }

    private sealed class RecordingIndexCache : ISourceLinkIndexCache
    {
        private readonly Dictionary<string, string> _entries = [];

        public HashSet<string> Keys { get; } = [];

        public string? TryGet(string key)
        {
            Keys.Add(key);
            return _entries.GetValueOrDefault(key);
        }

        public void Set(string key, string content)
        {
            Keys.Add(key);
            _entries[key] = content;
        }
    }
}
