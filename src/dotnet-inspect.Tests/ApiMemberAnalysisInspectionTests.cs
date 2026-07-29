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

    // Round-8 review (Gemini): the routing flag must not be set by a candidate this walk opens
    // itself. Classification could not decide this path, so it is SELECTED, so the open below
    // settles whether the scope really had a session — and when that open fails, the unfiltered
    // walk also ended with an empty opened list and took the token builder. Deriving the flag from
    // every candidate rather than only the ruled-out ones routed this to the structural builder
    // instead and printed a different tree for the same request.
    //
    // The path carries an embedded null, so File.OpenRead throws ArgumentException — outside the
    // BadImageFormatException/IOException/UnauthorizedAccessException set that means "definitely
    // unopenable". It therefore classifies as undecidable, which is the whole point: an
    // undecidable candidate must not be treated as evidence that the scope was openable, because
    // this walk goes on to open it and find out.
    [Fact]
    public void CallerScopes_WhenTheOnlySelectedScopeEntryFailsToOpen_SelectsTheSameAssemblyBuilder()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        string undecidable = "scope\0entry.dll";

        var scopes = Create(target, [undecidable]).CallerScopes(includeAllocations: true);

        Assert.Null(scopes);
    }

    static IReadOnlyList<string> ReferenceNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return ILInspector.Metadata.AssemblyIdentityScanner.Scan(reader).ReferenceNames;
    }

    // --- #3333 increment 2: narrowing the single-hop Callers path ---

    static ApiMemberAnalysisInspection CreateForCallers(string assemblyPath, IReadOnlyList<string>? scope)
        => new(assemblyPath, [], new HashSet<string> { SectionNames.Callers }, scope, null);

    static int TokenOf(string assemblyPath, string declaringTypeName, string methodName)
        => ILInspector.Analysis.LibraryBodyIndex.Open(assemblyPath).Methods
            .First(m => m.DeclaringType.Name == declaringTypeName && m.Name == methodName)
            .MetadataToken;

    static string[] FullScope =>
    [
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
        FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath(),
        FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath(),
        FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath(),
    ];

    // The Callers table is built from strictly single-hop edges, so it needs the assemblies that
    // name the declaring type, not the transitive closure the Call Graph needs. Box`1 is declared
    // by the target and referenced only by the caller fixture: the twin references the target
    // assembly but never Box`1, and the indirect fixture reaches the target only through the
    // caller. The closure keeps all three; only one can contribute an edge.
    [Fact]
    public void DirectCallerScopes_NarrowsToAssembliesNamingTheDeclaringType()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        int store = TokenOf(target, "Box`1", "Store");

        var wide = CreateForCallers(target, FullScope).CallerScopes(includeAllocations: false);
        var narrow = CreateForCallers(target, FullScope).DirectCallerScopes(store);

        Assert.NotNull(wide);
        Assert.NotNull(narrow);

        // Non-vacuity: the narrowing has to be a real reduction, or the equivalence test below
        // would be comparing a scope against itself.
        Assert.Equal(3, wide.Count);
        Assert.Equal(
            ["ILInspector.Analysis.CallerGraphCaller"],
            narrow.Select(s => s.SourceName).OrderBy(n => n, StringComparer.Ordinal));
    }

    // The obligation: narrowing changes how many assemblies are opened, never the answer. Every
    // method in the target fixture is compared, with the un-narrowed scope as the control.
    //
    // The control is obtained by resolving the graph lens first, which makes DirectCallerScopes
    // reuse that wider set — so this exercises the reuse branch as well as pinning equivalence.
    [Fact]
    public void CallerEdges_AreUnchangedByNarrowing()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        var index = ILInspector.Analysis.LibraryBodyIndex.Open(target);

        int compared = 0;
        int withEdges = 0;
        foreach (var method in index.Methods)
        {
            var narrowed = CreateForCallers(target, FullScope);

            var control = CreateForCallers(target, FullScope);
            Assert.NotNull(control.CallerScopes(includeAllocations: true));

            string[] Render(ApiMemberAnalysisInspection inspection) => inspection
                .CallerEdges(method.MetadataToken)
                .Select(edge => $"{edge.Source}|{edge.Call.Caller.Name}|{edge.Call.ILOffset}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var expected = Render(control);
            Assert.Equal(expected, Render(narrowed));

            compared++;
            if (expected.Length > 0)
                withEdges++;
        }

        // Without these the comparison could hold by both sides always being empty, which is what
        // a filter that ruled everything out would produce.
        Assert.True(compared > 0, "no member was compared");
        Assert.True(withEdges > 0, "no member had any cross-assembly caller, so nothing was proven");
    }

    // Narrowing must never reach the Call Graph, which is transitive and would be truncated by it.
    // The indirect fixture is the case that distinguishes them: it belongs in the graph and cannot
    // contribute a direct edge.
    //
    // The lens asked for here is deliberately includeAllocations: false, because that is the one
    // that shares a cache with the path CallerEdges used to take. Asking the allocations lens would
    // read the other cache and so could not observe a narrow set leaking into the graph. Narrowing
    // is resolved first, so a leak is present before the graph asks.
    [Fact]
    public void CallerScopes_ForTheGraphIsNotNarrowed()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        int store = TokenOf(target, "Box`1", "Store");

        var inspection = CreateForCallers(target, FullScope);
        var narrow = inspection.DirectCallerScopes(store);

        Assert.NotNull(narrow);
        Assert.DoesNotContain(
            "ILInspector.Analysis.CallerGraphIndirectCaller",
            narrow.Select(s => s.SourceName));

        var graph = inspection.CallerScopes(includeAllocations: false);

        Assert.NotNull(graph);
        Assert.Contains(
            "ILInspector.Analysis.CallerGraphIndirectCaller",
            graph.Select(s => s.SourceName));
    }

    // The narrow scope is cached per declaring type, so a second member with a *different*
    // declaring type must not be answered from the first one's entry. Box`1 and Api are declared by
    // the same fixture but named by different callers, so a single shared cache entry would hand
    // one of them the other's scope.
    [Fact]
    public void DirectCallerScopes_AreCachedPerDeclaringType()
    {
        string target = FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        int store = TokenOf(target, "Box`1", "Store");
        int ping = TokenOf(target, "Api", "Ping");

        static string[] Names(IReadOnlyList<MethodBodyInspectionSession>? scopes) => scopes!
            .Select(s => s.SourceName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var forward = CreateForCallers(target, FullScope);
        var storeFirst = Names(forward.DirectCallerScopes(store));
        var pingSecond = Names(forward.DirectCallerScopes(ping));

        // The reverse order must produce the same two answers, or the first call is poisoning the
        // second through the cache.
        var reverse = CreateForCallers(target, FullScope);
        var pingFirst = Names(reverse.DirectCallerScopes(ping));
        var storeSecond = Names(reverse.DirectCallerScopes(store));

        Assert.Equal(storeFirst, storeSecond);
        Assert.Equal(pingFirst, pingSecond);

        // Non-vacuity: if the two declaring types selected the same assemblies, a shared cache
        // entry would satisfy the assertions above and prove nothing.
        Assert.NotEqual(storeFirst, pingFirst);
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
