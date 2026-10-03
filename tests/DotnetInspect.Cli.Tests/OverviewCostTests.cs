using DotnetInspect.Cli;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

// Runs isolated from all other collections so the process-wide method-body read
// and body-session counters see only this test's command.
[CollectionDefinition("OverviewCost", DisableParallelization = true)]
public class OverviewCostCollection;

/// <summary>
/// The overview cost gate (docs/design/progressive-disclosure.md#overview-cost):
/// a library overview reads metadata only, so it reads no method body and opens
/// no body session. The fixture calls <c>AppContext.TryGetSwitch</c>, so an
/// overview that inventories call sites fails here.
/// </summary>
[Collection("OverviewCost")]
public class OverviewCostTests
{
    static string Fixture => typeof(AppContextSwitchFixture).Assembly.Location;

    [Theory]
    [InlineData]
    [InlineData("-v:q")]
    [InlineData("-S", "Library Info")]
    public async Task LibraryOverview_ReadsNoMethodBody(params string[] gesture)
    {
        var (exit, error) = await RunCountingAsync(["library", Fixture, .. gesture]);

        Assert.True(exit == 0, error);
        Assert.Equal(0, MethodBodySource.ReadCountForTests);
        Assert.Equal(0, MethodBodyInspectionSession.OpenCountForTests);
    }

    [Fact]
    public async Task SwitchesSection_ReadsBodies_SoTheGateObservesBodyWork()
    {
        var (exit, error) = await RunCountingAsync(["library", Fixture, "-S", "Switches"]);

        Assert.True(exit == 0, error);
        Assert.True(MethodBodySource.ReadCountForTests > 0);
    }

    [Fact]
    public async Task LibraryInfoSwitchesRow_CountsDeclaredSwitchesBesideTheSectionsCallSites()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", Fixture, "-S", "Library Info", "-S", "Switches");

        Assert.True(exit == 0, error);
        Assert.Contains("| Switches | 0 |", output);
        Assert.Contains("DotnetInspector.Fixtures.AppContextOnly", output);
    }

    [Fact]
    public async Task BareDiscovery_ListsSwitchesForAnAppContextOnlyAssembly()
    {
        // Plain -D is not an overview: it still reads call sites to decide
        // whether the Switches section has data. The fixture declares no
        // switch and calls AppContext.TryGetSwitch; System.Runtime does neither.
        string switchFree = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");

        var (exit, output, error) = await RunAppAsync("library", Fixture, "-D");
        var (contrastExit, contrastOutput, contrastError) =
            await RunAppAsync("library", switchFree, "-D");

        Assert.True(exit == 0, error);
        Assert.Contains("| Switches | section |", output);
        Assert.True(contrastExit == 0, contrastError);
        Assert.Contains("| Library Info | section |", contrastOutput);
        Assert.DoesNotContain("| Switches |", contrastOutput);
    }

    static async Task<(int Exit, string Error)> RunCountingAsync(string[] args)
    {
        MethodBodySource.ReadCountForTests = 0;
        MethodBodyInspectionSession.OpenCountForTests = 0;
        var (exit, _, error) = await RunAppAsync(args);
        return (exit, error);
    }

    static Task<(int Exit, string Output, string Error)> RunAppAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });
}
