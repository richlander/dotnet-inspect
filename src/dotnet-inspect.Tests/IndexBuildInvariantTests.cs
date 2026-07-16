using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

// Runs isolated from all other collections so the shared MethodBodyInspectionSession
// open counter is reliable (no concurrent session opens from parallel tests).
[CollectionDefinition("IndexBuildGuard", DisableParallelization = true)]
public class IndexBuildGuardCollection;

/// <summary>
/// Guards the "build the analysis index once per command" invariant delivered by the #2139 perf
/// work (PRs #2187 member, #2199 type, #2210 library): every index-backed section of one command
/// shares a single <see cref="Analysis.LibraryBodyIndex"/> build. A new section that opens its own
/// <see cref="MethodBodyInspectionSession"/> instead of the shared one would silently reintroduce a
/// per-section rebuild — these tests fail immediately if that happens.
/// </summary>
[Collection("IndexBuildGuard")]
public class IndexBuildInvariantTests
{
    static string FixtureAssembly => typeof(IndexBuildGuardFixture).Assembly.Location;

    [Fact]
    public async Task MemberCommand_MultipleIndexSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(new MemberOptions
        {
            TypeName = typeof(IndexBuildGuardFixture).FullName,
            AssemblyPath = FixtureAssembly,
            MemberFilter = [nameof(IndexBuildGuardFixture.Work)],
            IncludeSections =
            [
                SectionNames.Calls,
                SectionNames.CallGraph,
                SectionNames.CallerGraph,
                SectionNames.UnsafeOperations,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
            ],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Detailed,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public async Task TypeCommand_MultipleAnalysisSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = typeof(IndexBuildGuardFixture).FullName,
            AssemblyPath = FixtureAssembly,
            IncludeSections =
            [
                SectionNames.UnsafeMembers,
                SectionNames.CalledTypes,
                SectionNames.TopLeverage,
                SectionNames.AllocationFacts,
                SectionNames.SafetyFacts,
                SectionNames.CostFacts,
                SectionNames.PerformanceTriage,
            ],
            TipLevel = TipLevel.Quiet,
            Verbosity = Verbosity.Minimal,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public async Task LibraryCommand_MultipleAnalysisSections_BuildsIndexOnce()
    {
        MethodBodyInspectionSession.OpenCountForTests = 0;

        var result = await ConsoleCapture.RunAsync(() => LibraryCommand.ExecuteAsync(new LibraryOptions
        {
            AssemblyName = FixtureAssembly,
            IncludeSections =
            [
                SectionNames.UnsafeMembers,
                SectionNames.TopLeverage,
                SectionNames.PerformanceTriage,
                SectionNames.ResourceTriage,
            ],
            Markdown = true,
            Rows = new RowWindow(25, FromEnd: false),
        }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, MethodBodyInspectionSession.OpenCountForTests);
    }
}

public class IndexBuildGuardFixture
{
    // A body with a call and an allocation so the requested analysis sections have data to render.
    public void Work()
    {
        var list = new List<int>();
        Helper(list);
        System.Console.WriteLine(list.Count);
    }

    public void Helper(List<int> items) => items.Add(1);
}
