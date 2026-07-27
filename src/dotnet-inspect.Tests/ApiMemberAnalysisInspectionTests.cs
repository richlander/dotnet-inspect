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

    // Round-2 review found that the previous "would the unfiltered walk have opened it?" rule was
    // not decidable: an image whose Assembly/AssemblyRef tables read cleanly (so the prefilter can
    // rule it out) can still throw when its bodies are indexed. Reproduced with single-byte
    // mutations of a real assembly — 8 of 3000 produced exactly that. Openability therefore cannot
    // drive the choice, so a scoped request gets the cross-assembly builder even when nothing in
    // the scope can be opened at all.
    [Fact]
    public void CallerScopes_WhenNoScopeAssemblyCanBeOpened_StillSelectsTheCrossAssemblyBuilder()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var scopes = Create(SelfPath, [missing]).CallerScopes(includeAllocations: false);

        Assert.NotNull(scopes);
        Assert.Empty(scopes);
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

    // A file with no managed metadata must fail open rather than be ruled out: --bin enumerates
    // every top-level *.dll with no managed-image filter, so ruling it out here would only
    // duplicate the catch around MethodBodyInspectionSession.Open. Either way the scope yields
    // nothing, and the request was still scoped.
    [Fact]
    public void CallerScopes_WhenTheOnlyScopeEntryIsNotManaged_StillSelectsTheCrossAssemblyBuilder()
    {
        string? native = FindNativeImage();
        Assert.SkipWhen(native is null, "No native PE image available in the runtime directory.");

        var scopes = Create(SelfPath, [native!]).CallerScopes(includeAllocations: false);

        Assert.NotNull(scopes);
        Assert.Empty(scopes);
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
