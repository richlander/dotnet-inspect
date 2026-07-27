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

    // Every scope entry unopenable is indistinguishable from having nothing to walk, which is what
    // the unfiltered walk did with an all-garbage scope, so it keeps the same-assembly builder.
    [Fact]
    public void CallerScopes_WhenNoScopeAssemblyCanBeOpened_SelectsTheSameAssemblyBuilder()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var inspection = Create(SelfPath, [missing]);

        Assert.Null(inspection.CallerScopes(includeAllocations: false));
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

    // A file with no managed metadata must fail open rather than count as a prefilter skip: --bin
    // enumerates every top-level *.dll with no managed-image filter, so a native binary in scope is
    // one the unfiltered walk could never have opened. Reporting it as skipped would flip an
    // all-native scope from the same-assembly builder to the cross-assembly one.
    [Fact]
    public void CallerScopes_WhenTheOnlyScopeEntryIsNotManaged_SelectsTheSameAssemblyBuilder()
    {
        string? native = FindNativeImage();
        Assert.SkipWhen(native is null, "No native PE image available in the runtime directory.");

        var inspection = Create(SelfPath, [native!]);

        Assert.Null(inspection.CallerScopes(includeAllocations: false));
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
