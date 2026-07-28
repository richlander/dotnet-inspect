using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

/// <summary>
/// The caller-scope prefilter (#3331) is only allowed to change how much work a caller query does,
/// never which reverse-graph builder answers it. <see cref="ApiMemberAnalysisInspection.CallerScopes"/>
/// is that decision: <see langword="null"/> selects the same-assembly builder and any non-null list
/// selects the cross-assembly one, and the two do not produce identical trees. These assert the
/// decision itself rather than a rendered tree, because the small call-graph fixtures happen not to
/// distinguish the two builders at all — an output-level test here would pass no matter what.
///
/// The decision is a question about the <em>request</em>, never about how readable the scope turned
/// out to be, so most of these pin cases where the scope yields nothing to walk yet the choice must
/// still hold.
/// </summary>
public class ApiMemberAnalysisInspectionTests
{
    static readonly string SelfPath = typeof(ApiMemberAnalysisInspectionTests).Assembly.Location;
    static readonly string AnalysisPath = typeof(ILInspector.Analysis.LibraryBodyIndex).Assembly.Location;
    static readonly string CliPath = typeof(ApiMemberAnalysisInspection).Assembly.Location;

    static ApiMemberAnalysisInspection Create(string assemblyPath, IReadOnlyList<string>? scope)
        => new(assemblyPath, [], new HashSet<string> { SectionNames.CallGraph }, scope, null);

    // Regression: MemberOptions.CallerScopeAssemblies defaults to a non-null EMPTY list and the
    // command layer passes it unconditionally, so a null check here is dead code that routes every
    // ordinary request — the overwhelmingly common case — through the cross-assembly builder and
    // silently changes the default Call Graph and Callers output.
    [Fact]
    public void CallerScopes_WhenTheScopeListIsNull_SelectsTheSameAssemblyBuilder()
    {
        var inspection = Create(SelfPath, null);

        Assert.Null(inspection.CallerScopes(includeAllocations: false));
        Assert.Null(inspection.CallerScopes(includeAllocations: true));
    }

    [Fact]
    public void CallerScopes_WhenTheScopeListIsEmpty_SelectsTheSameAssemblyBuilder()
    {
        var inspection = Create(SelfPath, []);

        Assert.Null(inspection.CallerScopes(includeAllocations: false));
        Assert.Null(inspection.CallerScopes(includeAllocations: true));
    }

    // The prefilter emptying the survivor list must not be mistaken for "no scope was requested".
    // ILInspector.Analysis names neither itself nor dotnet-inspect.Tests as a reference, so it is
    // ruled out without being opened, leaving nothing to walk — but the request was still scoped.
    [Fact]
    public void CallerScopes_WhenEveryScopeAssemblyIsPrefiltered_StillSelectsTheCrossAssemblyBuilder()
    {
        var inspection = Create(SelfPath, [AnalysisPath]);

        var scopes = inspection.CallerScopes(includeAllocations: false);

        Assert.NotNull(scopes);
        Assert.Empty(scopes);
    }

    // The prefilter must not rule out an assembly that really does reference the target.
    [Fact]
    public void CallerScopes_WhenAScopeAssemblyReferencesTheTarget_OpensIt()
    {
        var inspection = Create(AnalysisPath, [CliPath]);

        var scopes = inspection.CallerScopes(includeAllocations: false);

        Assert.NotNull(scopes);
        Assert.Single(scopes);
    }

    // Round-2 review found that "would the unfiltered walk have opened it?" is not decidable in
    // general: an image whose Assembly/AssemblyRef tables read cleanly can still throw when its
    // bodies are indexed. Reproduced with single-byte mutations of a real assembly — 8 of 3000
    // produced exactly that. But the weaker question this routing needs — could ANY scope entry be
    // opened at all — is decidable, and it is the question the unfiltered walk effectively asked:
    // it routed on whether its opened list came back empty. A scope whose every entry is
    // unopenable produced an empty list and took the token-keyed builder, so this must too.
    [Fact]
    public void CallerScopes_WhenNoScopeEntryIsOpenable_SelectsTheSameAssemblyBuilder()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var scopes = Create(SelfPath, [missing]).CallerScopes(includeAllocations: false);

        Assert.Null(scopes);
    }

    // The two lenses are cached independently and must decide identically.
    [Fact]
    public void CallerScopes_CachesTheAllocationLensSeparatelyFromTheCallerLens()
    {
        var inspection = Create(AnalysisPath, [CliPath]);

        var callers = inspection.CallerScopes(includeAllocations: false);
        var graph = inspection.CallerScopes(includeAllocations: true);

        Assert.NotNull(callers);
        Assert.NotNull(graph);
        Assert.NotSame(callers, graph);
        Assert.Same(callers, inspection.CallerScopes(includeAllocations: false));
        Assert.Same(graph, inspection.CallerScopes(includeAllocations: true));
    }

    // A scope holding only a native image yields nothing either way, but it must yield nothing the
    // same way the unfiltered walk did. --bin enumerates every top-level *.dll with no managed-image
    // filter, so this is an ordinary input, not a corner: pointing --bin at a native runtime
    // directory hits it. The unfiltered walk failed to open the native image, ended with an empty
    // opened list, and took the token-keyed builder. Round 7 caught this routing to the structural
    // builder instead and printing a different tree (62 lines against 60) for the same request.
    [Fact]
    public void CallerScopes_WhenTheOnlyScopeEntryIsNotManaged_SelectsTheSameAssemblyBuilder()
    {
        string? native = FindNativeImage();
        Assert.SkipWhen(native is null, "No native PE image available in the runtime directory.");

        var scopes = Create(SelfPath, [native!]).CallerScopes(includeAllocations: false);

        Assert.Null(scopes);
    }

    // The counterpart to the two above, and the reason routing cannot simply follow the opened
    // count: here the scope entry IS openable and is merely ruled out by the closure. The
    // unfiltered walk would have opened it, so the request keeps the cross-assembly builder even
    // though nothing survived selection.
    [Fact]
    public void CallerScopes_WhenAnOpenableScopeEntryIsRuledOut_StillSelectsTheCrossAssemblyBuilder()
    {
        string? native = FindNativeImage();
        Assert.SkipWhen(native is null, "No native PE image available in the runtime directory.");

        var scopes = Create(SelfPath, [native!, AnalysisPath]).CallerScopes(includeAllocations: false);

        Assert.NotNull(scopes);
        Assert.Empty(scopes);
    }

    // Round-3 review found the prefilter's premise was too narrow: it tested a DIRECT reference to
    // the target, but a caller graph walks outward several levels, so an assembly that names only
    // an intermediate still belongs in the tree. The indirect fixture references only the caller
    // assembly, never the target, and reproduced the defect end to end — the graph lost a whole
    // depth-3 branch. Selection is now a reverse-reference closure, so both must open.
    [Fact]
    public void CallerScopes_WhenAScopeAssemblyReferencesTheTargetOnlyIndirectly_OpensIt()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string caller = FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string indirect = FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath();

        // The fixture only proves anything while it stays free of a direct reference to the target.
        Assert.DoesNotContain(
            "ILInspector.Analysis.CallerGraphTarget",
            ReferenceNames(indirect));

        var scopes = Create(target, [caller, indirect]).CallerScopes(includeAllocations: true);

        Assert.NotNull(scopes);
        Assert.Equal(2, scopes.Count);
    }

    // ...and the closure must not degenerate into keeping everything. It closes over the SCOPE, so
    // an assembly whose only bridge to the target was not itself supplied stays out — the walk
    // could not have traversed that bridge either. The lookalike declares its own Target.Api.Ping
    // and reaches the target through nothing at all.
    [Fact]
    public void CallerScopes_WhenNoScopeAssemblyBridgesToTheTarget_RulesThemAllOut()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string indirect = FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath();
        string lookalike = FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath();

        var scopes = Create(target, [indirect, lookalike]).CallerScopes(includeAllocations: true);

        Assert.NotNull(scopes);
        Assert.Empty(scopes);
    }

    // Round-5 review: a zero-byte or malformed *.dll beside real ones was classified as
    // undecidable, which selects the whole scope and disables the prefilter entirely — the 960 MB
    // behavior this change exists to remove, reintroduced by one junk file in a --bin directory.
    // Such an image cannot be opened as a PE at all, and caller analysis opens the same path the
    // same way, so it could not have contributed edges and must be ruled out instead.
    [Fact]
    public void CallerScopes_WhenAScopeEntryIsNotAPortableExecutable_StillRulesOutTheOthers()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string lookalike = FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath();
        string directory = Path.Combine(Path.GetTempPath(), $"scope-junk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string empty = Path.Combine(directory, "empty.dll");
            string truncated = Path.Combine(directory, "truncated.dll");
            File.WriteAllBytes(empty, []);
            File.WriteAllBytes(truncated, "MZ not really a PE"u8.ToArray());

            var scopes = Create(target, [empty, truncated, lookalike])
                .CallerScopes(includeAllocations: true);

            Assert.NotNull(scopes);
            Assert.Empty(scopes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The complement, so the fix above cannot be "rule out anything that fails to read". An image
    // that opens but reads badly may still decode bodies, so it stays undecidable and keeps the
    // scope selected.
    [Fact]
    public void CallerScopes_WhenAScopeEntryIsAnUnreadableDirectory_StillRulesOutTheOthers()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string lookalike = FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath();
        string directory = Path.Combine(Path.GetTempPath(), $"scope-dir-{Guid.NewGuid():N}.dll");
        Directory.CreateDirectory(directory);
        try
        {
            var scopes = Create(target, [directory, lookalike])
                .CallerScopes(includeAllocations: true);

            Assert.NotNull(scopes);
            Assert.Empty(scopes);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    static IReadOnlyList<string> ReferenceNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return ILInspector.Metadata.AssemblyIdentityScanner.Scan(reader).ReferenceNames;
    }

    static string? FindNativeImage()
    {
        foreach (string path in Directory.EnumerateFiles(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
                if (!reader.HasMetadata)
                    return path;
            }
            catch
            {
                // Not a readable PE; keep looking.
            }
        }

        return null;
    }
}
